using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class LongDisconnectionTests
{
    [TestCase( 3712 )]
    public async Task disconnected_socket_Async( int port )
    {
        (CommunicationChannel client, ConnectionAvailabilityTracker tracker) = await OpenDisconnectedTcpChannelAsync( port )
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

        while( client.ConnectionStatus != ConnectionAvailability.None )
        {
            TestHelper.Monitor.Info( "Waiting for 50ms." );
            await Task.Delay( 50 );
        }
        await client.DisposeAsync();

        // We may have the Connected event or not.
        tracker.Events.Length.ShouldBeInRange( 3, 4 );
        if( tracker.Events.Length == 3 )
        {
            tracker.Events.ShouldBe( [
                    ConnectionAvailability.Low,
                    ConnectionAvailability.DangerZone,
                    ConnectionAvailability.None] );
        }
        else
        {
            tracker.Events.ShouldBe( [
                    ConnectionAvailability.Connected,
                    ConnectionAvailability.Low,
                    ConnectionAvailability.DangerZone,
                    ConnectionAvailability.None] );
        }
    }

    static async Task<(CommunicationChannel,ConnectionAvailabilityTracker)> OpenDisconnectedTcpChannelAsync( int port )
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

