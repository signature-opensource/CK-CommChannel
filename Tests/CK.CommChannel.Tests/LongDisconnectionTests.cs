using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class LongDisconnectionTests
{
    [Test]
    [CancelAfter( 60_000 )]
    public async Task disconnected_socket_Async( CancellationToken cancel )
    {
        (CommunicationChannel client, ConnectionAvailabilityTracker tracker) = await OpenDisconnectedTcpChannelAsync()
                                                                                     .ConfigureAwait( false );
        var writer = new StringLineMessageWriter( client.Writer, Encoding.UTF8 );

        client.ConnectionStatus.ShouldBe( ConnectionAvailability.Connected );

        TestHelper.Monitor.Info( "The first write doesn't detect anything. Why?" );
        await writer.WriteAsync( $"No more Server here... n°1.", default );

        TestHelper.Monitor.Info( "The second one receives an IOException. (TimeOut: 200ms)" );
        // The call is now waiting for a new channel implementation.
        // We need a timeout to exit.
        await Util.Awaitable( () => writer.WriteAsync( $"No more Server here... n°2.", default, timeout: 200 ).AsTask() )
            .ShouldThrowAsync<TimeoutException>();

        // The decay to None goes through a few retries: how long that takes is not the point of this
        // test, but it must have a way out if it never happens, hence the bounded wait.
        await client.WaitForConnectionStatusAsync( ConnectionAvailability.None, cancel );
        await client.DisposeAsync();

        // What this test is about is the decay, not how many steps it takes.
        // Warning: do not assert the exact event array, nor its length. Whether the initial Connected
        // is caught depends on how quickly the tracker was registered, and how many events the decay
        // produces depends on how many reconnection attempts happen before the channel gives up.
        var events = tracker.Events;
        events.ShouldNotBeEmpty();
        events[^1].ShouldBe( ConnectionAvailability.None, "The channel ends up giving up." );
        events.ShouldContain( ConnectionAvailability.Low );
        events.ShouldContain( ConnectionAvailability.DangerZone );
        // ConnectionAvailability is ordered from None to Connected, and the server is gone for good:
        // availability only ever degrades, it never climbs back.
        events.Select( e => (int)e ).ShouldBeInOrder( SortDirection.Descending );
    }

    static async Task<(CommunicationChannel,ConnectionAvailabilityTracker)> OpenDisconnectedTcpChannelAsync()
    {
        var server = new Socket( SocketType.Stream, ProtocolType.Tcp );
        server.Bind( new IPEndPoint( IPAddress.Any, 0 ) );
        var serverAddress = (IPEndPoint?)server.LocalEndPoint;
        Throw.Assert( serverAddress != null );
        server.Listen( backlog: 0 );
        var serverSocketTask = server.AcceptAsync();

        var clientConfig = new TcpChannelConfiguration { Host = "127.0.0.1", Port = serverAddress.Port };
        var client = CommunicationChannel.Create( TestHelper.Monitor, clientConfig );
        var tracker = new ConnectionAvailabilityTracker( client );
        var clientReader = new StringLineMessageReader( client.Reader, Encoding.UTF8 );

        var serverSocket = await serverSocketTask.ConfigureAwait( false );
        // No need to listen anymore now that we have the socket.
        // We can dispose the server.
        server.Dispose();

        TestHelper.Monitor.Info( "Check that the Server can talk to the Client." );
        {
            await serverSocket.SendAsync( Encoding.UTF8.GetBytes( "Server => Client.\r\n" ), SocketFlags.None ).ConfigureAwait( false );
            (await clientReader.ReadNextAsync().ConfigureAwait( false )).ShouldBe( "Server => Client." );
        }
        TestHelper.Monitor.Info( "Check that Client can talk to the Server." );
        {
            await client.Writer.WriteAsync( Encoding.UTF8.GetBytes( "Client => Server.\r\n" ) ).ConfigureAwait( false );
            var buffer = new byte[256];
            int lenRead = await serverSocket.ReceiveAsync( buffer.AsMemory(), SocketFlags.None ).ConfigureAwait( false );
            Encoding.UTF8.GetString( buffer, 0, lenRead ).ShouldBe( "Client => Server.\r\n" );
        }
        TestHelper.Monitor.Info( "Dispose the Server socket." );
        serverSocket.Dispose();
        return (client, tracker);
    }
}

