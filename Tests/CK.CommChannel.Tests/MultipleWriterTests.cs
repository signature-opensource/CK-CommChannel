using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class MultipleWriterTests
{
    // Scaled with the machine to get real contention, with a floor so that the test keeps its point
    // on a small agent. The reader below drains concurrently, which is what keeps the data in flight
    // far below the pipe's default PauseWriterThreshold (64 KiB) whatever the sender count is.
    static readonly int SenderCount = Math.Max( 8, Environment.ProcessorCount * 2 );
    const int MessagesPerSender = 50;
    static readonly int TotalMessageCount = SenderCount * MessagesPerSender;

    /// <summary>
    /// Concurrent writers on a <see cref="MessageWriterBase{T}.MultipleWriters"/> writer must produce
    /// exactly the messages that were written, none lost, none duplicated and none interleaved with
    /// another one.
    /// </summary>
    [TestCase( "UsePipe" )]
    [TestCase( "UseNetworkStream" )]
    [CancelAfter( 60_000 )]
    public async Task MultipleWriters_serialize_concurrent_writes_Async( string channelType, CancellationToken cancel )
    {
        bool usePipe = channelType == "UsePipe";
        using var gLog = TestHelper.Monitor.OpenInfo( $"{nameof( MultipleWriters_serialize_concurrent_writes_Async )}({channelType})" );

        await using var ep = await TestMemoryEndPoint.AllocateAsync( usePipe );

        var rConfig = new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true, ConnectionTimeout = 300, Reverted = true };
        var rChannel = CommunicationChannel.Create( TestHelper.Monitor, rConfig );

        var config = new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true, ConnectionTimeout = 300 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        try
        {
            var writer = new StringLineMessageWriter( channel.Writer, Encoding.ASCII ) { MultipleWriters = true };

            // The reader runs concurrently with the senders: it must, since the endpoint pipe would
            // otherwise fill up and block every writer forever.
            var reader = Task.Run( async () =>
            {
                var r = new StringLineMessageReader( rChannel.Reader, Encoding.ASCII, "\r\n" );
                var received = new List<string>( TotalMessageCount );
                while( received.Count < TotalMessageCount )
                {
                    var m = await r.ReadNextAsync( cancel );
                    // Null is the EmptyMessage: a canceled read, not a message.
                    if( m != null ) received.Add( m );
                }
                return received;
            }, cancel );

            var senders = Enumerable.Range( 0, SenderCount )
                                    .Select( iTask => Task.Run( async () =>
                                    {
                                        for( int i = 0; i < MessagesPerSender; i++ )
                                        {
                                            await writer.WriteAsync( $"{iTask}/{i}", cancel );
                                        }
                                    }, cancel ) )
                                    .ToArray();

            await Task.WhenAll( senders );
            var received = await reader;

            // This is the actual point of the test: had two concurrent writes interleaved, the
            // delimited messages would be corrupted, so the received set would not match.
            var expected = Enumerable.Range( 0, SenderCount )
                                     .SelectMany( iTask => Enumerable.Range( 0, MessagesPerSender )
                                                                     .Select( i => $"{iTask}/{i}" ) );
            received.Order().ShouldBe( expected.Order() );
        }
        finally
        {
            await ep.DeallocateAsync();
            await channel.DisposeAsync();
            await rChannel.DisposeAsync();
        }
    }

    /// <summary>
    /// Without <see cref="MessageWriterBase{T}.MultipleWriters"/>, a concurrent write throws.
    /// <para>
    /// The <see cref="GatedPipeWriter"/> makes the overlap certain: the first write is parked inside
    /// its flush when the second one is issued.
    /// </para>
    /// </summary>
    [Test]
    [CancelAfter( 30_000 )]
    public async Task without_MultipleWriters_a_concurrent_write_throws_Async( CancellationToken cancel )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var gate = new GatedPipeWriter( pipe.Writer );
        var writer = new StringLineMessageWriter( gate, Encoding.ASCII );
        writer.MultipleWriters.ShouldBeFalse( "This is the default." );

        var first = writer.WriteAsync( "first", cancel ).AsTask();
        await gate.FlushEntered.WaitAsync( cancel );

        await Util.Awaitable( () => writer.WriteAsync( "second", cancel ).AsTask() )
                  .ShouldThrowAsync<InvalidOperationException>();

        gate.Release();
        (await first).ShouldBeTrue();

        // The refused write wrote nothing: only "first" is on the pipe.
        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync( cancel )).ShouldBe( "first" );
    }

    /// <summary>
    /// A <see cref="MessageWriterBase{T}"/> of strings that takes an explicit behavior, so that the
    /// retry path can be exercised.
    /// </summary>
    sealed class BehaviorStringWriter : MessageWriterBase<string>
    {
        public BehaviorStringWriter( PipeWriter writer, IStablePipeBehavior behavior )
            : base( writer, behavior )
        {
        }

        protected override void WriteMessage( in string message, IBufferWriter<byte> buffer )
        {
            Encoding.ASCII.GetBytes( message.AsSpan(), buffer );
            Encoding.ASCII.GetBytes( "\r\n".AsSpan(), buffer );
        }
    }

    /// <summary>
    /// A write that fails and is retried by the behavior must leave the writer's lock balanced.
    /// </summary>
    [Test]
    [CancelAfter( 30_000 )]
    public async Task MultipleWriters_lock_stays_balanced_across_a_retry_Async( CancellationToken cancel )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var failing = new FailingOncePipeWriter( pipe.Writer );
        var behavior = new ConfigurableStablePipeBehavior { OnErrorAction = OnErrorAction.Retry };
        var writer = new BehaviorStringWriter( failing, behavior ) { MultipleWriters = true };

        // The lock is taken once, on the first attempt only. Releasing it per attempt overshoots the
        // SemaphoreSlim(1,1) maximum and this very call throws a SemaphoreFullException.
        (await writer.WriteAsync( "retried", cancel )).ShouldBeTrue();
        failing.FlushCount.ShouldBe( 2, "The first flush failed and the behavior asked for a retry." );

        // And the lock is still usable afterwards.
        (await writer.WriteAsync( "after", cancel )).ShouldBeTrue();

        // The retry re-flushed the buffer rather than re-encoding the message: no duplicate.
        var reader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "retried" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "after" );
    }

    /// <summary>
    /// With <see cref="MessageWriterBase{T}.MultipleWriters"/>, the very same overlap waits instead of
    /// throwing, and both messages are written in full.
    /// </summary>
    [Test]
    [CancelAfter( 30_000 )]
    public async Task with_MultipleWriters_a_concurrent_write_waits_Async( CancellationToken cancel )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var gate = new GatedPipeWriter( pipe.Writer );
        var writer = new StringLineMessageWriter( gate, Encoding.ASCII ) { MultipleWriters = true };

        var first = writer.WriteAsync( "first", cancel ).AsTask();
        await gate.FlushEntered.WaitAsync( cancel );

        var second = writer.WriteAsync( "second", cancel ).AsTask();
        // Not a race: "second" is blocked on the writer's lock, which "first" only releases once the
        // gate is opened below.
        second.IsCompleted.ShouldBeFalse( "The second write waits for the first one." );

        gate.Release();
        (await first).ShouldBeTrue();
        (await second).ShouldBeTrue();

        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync( cancel )).ShouldBe( "first" );
        (await lineReader.ReadNextAsync( cancel )).ShouldBe( "second" );
    }
}
