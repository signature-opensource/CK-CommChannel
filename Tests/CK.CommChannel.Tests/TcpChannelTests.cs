using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;
using System.Threading;

namespace CK.CommChannel.Tests;

[TestFixture]
public class TcpChannelTests
{
    TcpChannelConfiguration CreateTcpChannelConfiguration( string host, int port )
    {
        return new TcpChannelConfiguration()
        {
            Host = host,
            Port = port,
            AutoReconnect = false,
            DefaultReadTimeout = -1,
            DefaultWriteTimeout = -1,
            DefaultRetryWriteCount = 0
        };
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task TcpEchoServer_echoes_messages_correctly_Async( CancellationToken cancel )
    {
        await using var echoServer = new TcpEchoServer( IPAddress.Loopback, 12345 );

        // Start the echo server in the background
        echoServer.Start();

        using var client = new TcpClient();
        await client.ConnectAsync( IPAddress.Loopback, 12345, cancel );

        await using var stream = client.GetStream();
        byte[] request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await stream.WriteAsync( request, cancel );

        byte[] response = new byte[request.Length];
        int readBytes = await stream.ReadAsync( response, cancel );
        readBytes.ShouldBe( request.Length );

        string responseStr = Encoding.UTF8.GetString( response );
        responseStr.ShouldBe( "Hello, server!" );
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task TcpChannel_can_connect_to_TcpEchoServer_Async( CancellationToken cancel )
    {
        IPAddress host = IPAddress.Loopback;
        int port = 46712;
        await using var echoServer = new TcpEchoServer( host, port );
        echoServer.Start();

        TcpChannelConfiguration config = CreateTcpChannelConfiguration( host.ToString(), port );

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config );

        byte[] request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        var readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        string responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );
    }

    [Test]
    [CancelAfter( 1000 )]
    public async Task TcpChannel_can_switch_TcpEchoServers_Async( CancellationToken cancel )
    {
        IPAddress host = IPAddress.Loopback;
        int port1 = 46713;
        int port2 = 46714;
        int port3 = 46715;
        await using var echoServer1 = new TcpEchoServer( host, port1 );
        await using var echoServer2 = new TcpEchoServer( host, port2 );
        await using var echoServer3 = new TcpEchoServer( host, port3 );
        echoServer1.Start();
        echoServer2.Start();
        echoServer3.Start();

        TcpChannelConfiguration config1 = CreateTcpChannelConfiguration( host.ToString(), port1 );

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config1 );

        // Send-receive 1

        byte[] request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        var readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        string responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );

        // Change config

        TcpChannelConfiguration config2 = CreateTcpChannelConfiguration( host.ToString(), port2 );
        await cc.ReconfigureAsync( TestHelper.Monitor, config2 );

        await echoServer1.StopAsync();

        // Send-receive 2

        request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );

        // Change config

        TcpChannelConfiguration config3 = CreateTcpChannelConfiguration( host.ToString(), port3 );
        await cc.ReconfigureAsync( TestHelper.Monitor, config3 );

        await echoServer2.StopAsync();

        // Send-receive 3

        request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );
    }
}
