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
    // Generous: it is a guard against a hang, not a claim about how fast a loopback round trip is.
    // Warning: a budget of the order of a second is tight enough that a loaded machine fails these
    // tests on scheduling alone.
    const int TestTimeoutMS = 30_000;

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
    [CancelAfter( TestTimeoutMS )]
    public async Task TcpEchoServer_echoes_messages_correctly_Async( CancellationToken cancel )
    {
        // Port 0: the OS picks a free one, echoServer.Port gives it back.
        await using var echoServer = new TcpEchoServer( IPAddress.Loopback );

        // Start the echo server in the background
        echoServer.Start();

        using var client = new TcpClient();
        await client.ConnectAsync( IPAddress.Loopback, echoServer.Port, cancel );

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
    [CancelAfter( TestTimeoutMS )]
    public async Task TcpChannel_can_connect_to_TcpEchoServer_Async( CancellationToken cancel )
    {
        IPAddress host = IPAddress.Loopback;
        await using var echoServer = new TcpEchoServer( host );
        echoServer.Start();

        TcpChannelConfiguration config = CreateTcpChannelConfiguration( host.ToString(), echoServer.Port );

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config );

        byte[] request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        var readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        string responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );
    }

    [Test]
    [CancelAfter( TestTimeoutMS )]
    public async Task TcpChannel_can_switch_TcpEchoServers_Async( CancellationToken cancel )
    {
        IPAddress host = IPAddress.Loopback;
        await using var echoServer1 = new TcpEchoServer( host );
        await using var echoServer2 = new TcpEchoServer( host );
        await using var echoServer3 = new TcpEchoServer( host );
        echoServer1.Start();
        echoServer2.Start();
        echoServer3.Start();

        TcpChannelConfiguration config1 = CreateTcpChannelConfiguration( host.ToString(), echoServer1.Port );

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config1 );

        // Send-receive 1

        byte[] request = Encoding.UTF8.GetBytes( "Hello, server!" );
        await cc.Writer.WriteAsync( request, cancel );

        var readResult = await cc.Reader.ReadAsync( cancel );
        readResult.Buffer.Length.ShouldBe( request.Length );

        string responseStr = Encoding.UTF8.GetString( readResult.Buffer );
        responseStr.ShouldBe( "Hello, server!" );

        // Change config

        TcpChannelConfiguration config2 = CreateTcpChannelConfiguration( host.ToString(), echoServer2.Port );
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

        TcpChannelConfiguration config3 = CreateTcpChannelConfiguration( host.ToString(), echoServer3.Port );
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
