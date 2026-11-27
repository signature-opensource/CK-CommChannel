using CK.Core;
using Shouldly;
using NUnit.Framework;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

/// <summary>
/// Without AutoReconnect, a lost connection has one outcome whatever the operating system reports
/// (an end of stream, a reset, an abort): the pipe is completed.
/// These tests use the memory pipe end point: they depend on no socket and on no timing.
/// </summary>
[TestFixture]
public class ConnectionLostTests
{
    static IOException Lost( SocketError code ) => new IOException( "Connection lost (test).", new SocketException( (int)code ) );

    static CommunicationChannel CreateWithoutAutoReconnect( TestMemoryEndPoint ep )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = ep.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );
        channel.AutoReconnect = false;
        return channel;
    }

    [TestCase( SocketError.ConnectionReset )]
    [TestCase( SocketError.OperationAborted )]
    [CancelAfter( 30_000 )]
    public async Task without_AutoReconnect_a_lost_connection_completes_a_pending_read_Async( SocketError code, CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        var channel = CreateWithoutAutoReconnect( ep );
        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        var read = reader.ReadNextAsync( cancel ).AsTask();
        var lost = Lost( code );
        // The pending read of the channel fails with this exception.
        ep.Input.Complete( lost );

        (await read).ShouldBeNull();
        reader.IsCompleted.ShouldBeTrue();
        channel.Reader.CompleteException.ShouldBeSameAs( lost );
        (await reader.ReadNextAsync( cancel )).ShouldBeNull();
        await channel.DisposeAsync();
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task without_AutoReconnect_a_lost_connection_completes_the_writer_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        var channel = CreateWithoutAutoReconnect( ep );
        var writer = new StringLineMessageWriter( channel.Writer, Encoding.ASCII );

        var lost = Lost( SocketError.Shutdown );
        // The next flush of the channel fails with this exception.
        ep.EndPoint.Output.Complete( lost );

        (await writer.WriteAsync( "Lost.", cancel )).ShouldBeFalse();
        channel.Writer.IsCompleted.ShouldBeTrue();
        channel.Writer.CompleteException.ShouldBeSameAs( lost );
        await channel.DisposeAsync();
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task without_AutoReconnect_an_error_that_is_not_a_lost_connection_is_thrown_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        var channel = CreateWithoutAutoReconnect( ep );
        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        var read = reader.ReadNextAsync( cancel ).AsTask();
        ep.Input.Complete( new InvalidDataException( "Not a lost connection (test)." ) );

        await Util.Awaitable( () => read ).ShouldThrowAsync<InvalidDataException>();
        channel.Reader.IsCompleted.ShouldBeFalse();
        await channel.DisposeAsync();
    }
}
