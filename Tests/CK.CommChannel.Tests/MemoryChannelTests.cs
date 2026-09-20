using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class MemoryChannelTests
{
    static readonly byte[] HelloBuffer = Encoding.ASCII.GetBytes( "Hello" );
    static readonly byte[] WorldBuffer = Encoding.ASCII.GetBytes( "World" );

    [Test]
    [CancelAfter( 30_000 )]
    public async Task simple_read_through_PipeChannel_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        var config = new MemoryChannelConfiguration() { EndPointName = ep.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        await ep.Input.WriteAsync( HelloBuffer, cancel );

        var readResult = await channel.Reader.ReadAsync( cancel );
        var buffer = readResult.Buffer;
        Match( ref buffer, HelloBuffer ).ShouldBeTrue();
        channel.Reader.AdvanceTo( buffer.Start );

        await ep.Input.WriteAsync( WorldBuffer, cancel );

        readResult = await channel.Reader.ReadAsync( cancel );
        buffer = readResult.Buffer;
        Match( ref buffer, WorldBuffer ).ShouldBeTrue();
        channel.Reader.AdvanceTo( buffer.Start );

        await ep.Input.CompleteAsync();

        // If AutoReconnect is true, this waits indefinitely since we have no timeout.
        channel.AutoReconnect = false;
        readResult = await channel.Reader.ReadAsync( cancel );
        readResult.IsCompleted.ShouldBeTrue();
    }

    static bool Match( ref ReadOnlySequence<byte> buffer, ReadOnlySpan<byte> match )
    {
        var reader = new SequenceReader<byte>( buffer );
        if( reader.Remaining < match.Length ) return false;
        if( !reader.IsNext( match, advancePast: true ) )
        {
            throw new InvalidDataException( $"Expected match (of {match.Length} bytes) not found." );
        }
        buffer = buffer.Slice( reader.Position );
        return true;
    }

    // The timeouts below are asserted with lower bounds only. A timeout guarantees that the operation
    // does not return *before* its due date; it cannot guarantee that it returns soon after, since any
    // scheduling hiccup (a loaded CI agent, a GC pause, thread pool starvation) delays the continuation.
    // Warning: do not add an upper bound such as ShouldBeLessThan( timeout + margin ) here, there is
    // no margin that is both tight enough to mean anything and loose enough to always hold.
    // [CancelAfter] is what catches an operation that never returns at all.
    //
    // Timers may fire slightly early, hence this tolerance on the lower bounds.
    const int TimerTolerance = 30;

    [TestCase( true )]
    [TestCase( false )]
    [CancelAfter( 30_000 )]
    public async Task with_explicit_or_default_read_timeouts_Async( bool autoReconnect, CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        var config = new MemoryChannelConfiguration()
        {
            EndPointName = ep.Name,
            DefaultReadTimeout = 100,
            DefaultWriteTimeout = 100,
            AutoReconnect = autoReconnect
        };

        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        // Reading without any inputs: the DefaultReadTimeout = 100 must trigger (and not before 100 ms).
        // The token must stay non cancelable here: a cancelable one is precisely what replaces the
        // DefaultReadTimeout (this is what the next block checks).
        var sw = Stopwatch.StartNew();
        await Util.Awaitable( () => channel.Reader.ReadAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();
        sw.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo( 100 - TimerTolerance );

        // Reading with an explicit token: this overrides the DefaultReadTimeout, so the read must still
        // be pending after 250 ms. The token's own deadline is far away: it is only a safety net, it
        // must never be the one that wins the race against the 250 ms below.
        sw.Restart();
        using( var cts = CancellationTokenSource.CreateLinkedTokenSource( cancel ) )
        {
            cts.CancelAfter( 20_000 );
            var read = channel.Reader.ReadAsync( cts.Token );
            await Task.Delay( 250, cancel );
            read.IsCompleted.ShouldBeFalse( "The explicit token replaced the 100 ms DefaultReadTimeout." );
            await ep.Input.WriteAsync( HelloBuffer, cancel );
            var result = await read;
            sw.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo( 250 - TimerTolerance );
            result.Buffer.ToArray().ShouldBe( HelloBuffer );
            channel.Reader.AdvanceTo( result.Buffer.End );
        }
        sw.Restart();
        // A cancelable token skips the DefaultReadTimeout and throws an OperationCanceledException.
        using( var cts = CancellationTokenSource.CreateLinkedTokenSource( cancel ) )
        {
            cts.CancelAfter( 300 );
            await Util.Awaitable( () => channel.Reader.ReadAsync( cts.Token ).AsTask() ).ShouldThrowAsync<OperationCanceledException>();
            sw.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo( 300 - TimerTolerance );
        }
    }

    /// <summary>
    /// Replacing a NetworkStream endpoint must disconnect both of the channels bound to it, whatever
    /// side of the socket pair each one sits on.
    /// </summary>
    [Test]
    [CancelAfter( 30_000 )]
    public async Task replacing_a_NetworkStream_endpoint_reconnects_both_channels_Async( CancellationToken cancel )
    {
        await using var ep = await TestMemoryEndPoint.AllocateNetworkStreamAsync();

        var c1 = CommunicationChannel.Create( TestHelper.Monitor,
                    new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true } );
        var c2 = CommunicationChannel.Create( TestHelper.Monitor,
                    new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true, Reverted = true } );
        try
        {
            await c1.WaitForConnectionStatusAsync( ConnectionAvailability.Connected, cancel );
            await c2.WaitForConnectionStatusAsync( ConnectionAvailability.Connected, cancel );

            var writer = new StringLineMessageWriter( c1.Writer, Encoding.ASCII );
            var reader = new StringLineMessageReader( c2.Reader, Encoding.ASCII );

            await writer.WriteAsync( "before", cancel );
            (await reader.ReadNextAsync( cancel )).ShouldBe( "before" );

            // Traffic while the endpoint is replaced, over and over.
            // Note: this covers endpoint churn under load, but it is not a regression guard for the
            // half-teardown bug in NetworkStreamChannel.DisposeAsync (where only one of the two
            // NetworkStreams was disposed): it passes either way. What reproduces that one is
            // AutoReconnectionTests at FailureConfiguration.High on a loaded machine.
            using var pumpStop = CancellationTokenSource.CreateLinkedTokenSource( cancel );
            var pump = Task.Run( async () =>
            {
                try
                {
                    while( !pumpStop.IsCancellationRequested )
                    {
                        await writer.WriteAsync( "ping", pumpStop.Token );
                        await Task.Delay( 5, pumpStop.Token );
                    }
                }
                catch( OperationCanceledException )
                {
                    // Stopped below.
                }
            }, pumpStop.Token );

            for( int i = 0; i < 25; ++i )
            {
                await ep.ReconnectAsync( usePipe: false );
                await Task.Delay( 20, cancel );
            }

            // The writer has no MultipleWriters: the pump must be done before it is used again.
            await pumpStop.CancelAsync();
            await pump;

            // Whatever happened during the churn, the link must work again afterwards.
            var received = Task.Run( async () =>
            {
                while( await reader.ReadNextAsync( cancel ) != "after" ) { }
            }, cancel );

            // A message written while the link is being replaced is legitimately lost, so this
            // resends. What must not happen is that it is lost every single time.
            while( !received.IsCompleted )
            {
                await writer.WriteAsync( "after", cancel );
                await Task.Delay( 100, cancel );
            }
            await received;
        }
        finally
        {
            await c1.DisposeAsync();
            await c2.DisposeAsync();
        }
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task multiple_channels_same_pipe_Async( CancellationToken cancel )
    {
        await using var ep = await TestMemoryEndPoint.AllocateNetworkStreamAsync();
        var config1 = new MemoryChannelConfiguration() { EndPointName = ep.Name };
        var config2 = new MemoryChannelConfiguration() { EndPointName = ep.Name, Reverted = true };

        var channel1 = CommunicationChannel.Create( TestHelper.Monitor, config1 );
        var channel2 = CommunicationChannel.Create( TestHelper.Monitor, config2 );

        Task tReceive = Task.Run( async () =>
        {
            var monitor = new ActivityMonitor();
            monitor.Info( "Receiving data" );
            var r = await channel2.Reader.ReadAsync( cancel ).ConfigureAwait( false );
            monitor.Info( "Received data" );
            r.Buffer.Length.ShouldBe( 3 );
        }, cancel );

        Task tSend = Task.Run( async () =>
        {
            var monitor = new ActivityMonitor();
            monitor.Info( "Sending data" );
            await channel1.Writer.WriteAsync( new ReadOnlyMemory<byte>( [1, 2, 3] ), cancel ).ConfigureAwait( false );
            monitor.Info( "Sent data" );
        }, cancel );

        TestHelper.Monitor.Info( "Waiting" );
        // Awaiting the tasks themselves: [CancelAfter] bounds the wait, and an assertion that fails
        // inside tReceive surfaces as itself.
        // Warning: signaling a ManualResetEventSlim at the end of tReceive and waiting on it with a
        // guessed budget hides both, an assertion failure then only shows up as "the wait returned false".
        await Task.WhenAll( tSend, tReceive );
    }
}
