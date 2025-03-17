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
    public async Task simple_read_through_PipeChannel_Async()
    {
        using var _ = TestHelper.Monitor.OpenInfo( nameof( simple_read_through_PipeChannel_Async ) );

        var endPoint = MemoryChannel.AllocatePipeChannel( "Test" );
        var config = new MemoryChannelConfiguration() { EndPointName = "Test", DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        await endPoint.Input.WriteAsync( HelloBuffer );

        var readResult = await channel.Reader.ReadAsync();
        var buffer = readResult.Buffer;
        Match( ref buffer, HelloBuffer ).ShouldBeTrue();
        channel.Reader.AdvanceTo( buffer.Start );

        await endPoint.Input.WriteAsync( WorldBuffer );

        readResult = await channel.Reader.ReadAsync();
        buffer = readResult.Buffer;
        Match( ref buffer, WorldBuffer ).ShouldBeTrue();
        channel.Reader.AdvanceTo( buffer.Start );

        await endPoint.Input.CompleteAsync();

        // If AutoReconnect is true, this waits indefinitely since we have no timeout.
        channel.AutoReconnect = false;
        readResult = await channel.Reader.ReadAsync();
        readResult.IsCompleted.ShouldBeTrue();

        await MemoryChannel.DeallocateAsync( "Test" );
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

    [TestCase( true )]
    [TestCase( false )]
    public async Task with_explicit_or_default_read_timeouts_Async( bool autoReconnect  )
    {
        using var _ = TestHelper.Monitor.OpenInfo( nameof( with_explicit_or_default_read_timeouts_Async ) );

        var config = new MemoryChannelConfiguration()
        {
            EndPointName = "Test",
            DefaultReadTimeout = 100,
            DefaultWriteTimeout = 100,
            AutoReconnect = autoReconnect
        };

        var endPoint = MemoryChannel.AllocatePipeChannel( "Test" );

        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        // Reading without any inputs: the DefaultReadTimeout = 100 must trigger.
        var sw = Stopwatch.StartNew();
        await Util.Awaitable( () => channel.Reader.ReadAsync().AsTask() ).ShouldThrowAsync<TimeoutException>();
        sw.ElapsedMilliseconds.ShouldBeLessThan( 150 );

        // Reading with 500 ms: this overrides the default.
        sw.Restart();
        using( var cts = new CancellationTokenSource( 500 ) )
        {
            var read = channel.Reader.ReadAsync( cts.Token );
            await Task.Delay( 250 + 30 );
            await endPoint.Input.WriteAsync( HelloBuffer );
            var result = await read;
            sw.ElapsedMilliseconds.ShouldBeGreaterThan( 250 );
            result.Buffer.ToArray().ShouldBe( HelloBuffer );
            channel.Reader.AdvanceTo( result.Buffer.End );
        }
        sw.Restart();
        // A cancelable token skips the DefaultReadTimeout and throws an OperationCanceledException.
        using( var cts = new CancellationTokenSource( 300 ) )
        {
            await Util.Awaitable( () => channel.Reader.ReadAsync( cts.Token ).AsTask() ).ShouldThrowAsync<OperationCanceledException>();
            sw.ElapsedMilliseconds.ShouldBeGreaterThan( 300 - 20 );
        }

        await MemoryChannel.DeallocateAsync( "Test" );
    }

    [Test]
    public async Task multiple_channels_same_pipe_Async()
    {
        using var _ = TestHelper.Monitor.OpenInfo( nameof(multiple_channels_same_pipe_Async) );

        var config1 = new MemoryChannelConfiguration()
            {EndPointName = "Test"};
        var config2 = new MemoryChannelConfiguration()
            {EndPointName = "Test", Reverted = true};

        await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );
        try
        {
            var channel1 = CommunicationChannel.Create( TestHelper.Monitor, config1 );
            var channel2 = CommunicationChannel.Create( TestHelper.Monitor, config2 );

            var mre = new ManualResetEventSlim();

            Task tReceive = Task.Run( async () =>
            {
                var monitor = new ActivityMonitor();
                monitor.Info( "Receiving data" );
                var r = await channel2.Reader.ReadAsync();
                monitor.Info( "Received data" );
                r.Buffer.Length.ShouldBe( 3 );
                mre.Set();
            } );

            Task tSend = Task.Run( async () =>
            {
                var monitor = new ActivityMonitor();
                monitor.Info( "Sending data" );
                await channel1.Writer.WriteAsync( new ReadOnlyMemory<byte>( new byte[] {1, 2, 3} ) );
                monitor.Info( "Sent data" );
            } );

            TestHelper.Monitor.Info( "Waiting" );
            mre.Wait(1000);
            mre.IsSet.ShouldBeTrue();
        }
        finally
        {

            await MemoryChannel.DeallocateAsync( "Test" );
        }
    }
}
