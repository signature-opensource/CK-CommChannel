using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class TcpSocketOptionTests
{
    [Test]
    public void SO_KEEPALIVE_must_be_set_at_Socket_level()
    {
        // Documents and locks the level forever: SO_KEEPALIVE is a socket-level option.
        using( var socket = new Socket( SocketType.Stream, ProtocolType.Tcp ) )
        {
            socket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true );
            ((int)socket.GetSocketOption( SocketOptionLevel.Socket, SocketOptionName.KeepAlive )!).ShouldBe( 1 );
        }
        // On Windows, the wrong level (the original bug) throws ProtocolOption (WSAENOPROTOOPT).
        // Guarded because Linux does not necessarily throw for this invalid pairing.
        if( OperatingSystem.IsWindows() )
        {
            using var socket = new Socket( SocketType.Stream, ProtocolType.Tcp );
            var ex = Should.Throw<SocketException>(
                () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, true ) );
            ex.SocketErrorCode.ShouldBe( SocketError.ProtocolOption );
        }
    }

    [Test]
    public async Task Enabling_keep_alive_does_not_warn_Async()
    {
        // Keep-alive is configured synchronously in the TcpChannel ctor during Create,
        // on the monitor passed to Create. Before the fix this logs a Warn ("Error while
        // enabling KeepAlive."); after the fix the option is set at the Socket level and no Warn appears.
        var config = new TcpChannelConfiguration
        {
            Host = "127.0.0.1",
            Port = 47731,            // nothing is listening: connect is refused fast, on the channel's own monitor.
            AutoReconnect = false,
            DefaultReadTimeout = -1,
            DefaultWriteTimeout = -1,
            DefaultRetryWriteCount = 0,
            EnableTcpKeepalive = true,
            TcpKeepAliveTimeSeconds = 15,
            TcpKeepAliveIntervalSeconds = 5,
            TcpKeepAliveRetryCount = 4
        };

        IReadOnlyList<ActivityMonitorSimpleCollector.Entry> entries;
        using( TestHelper.Monitor.CollectEntries( out entries, LogLevelFilter.Warn ) )
        {
            await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config );
        }
        entries.Any( e => e.Text.Contains( "KeepAlive", StringComparison.OrdinalIgnoreCase ) )
               .ShouldBeFalse( "Keep-alive must be enabled at the Socket level without warning." );
    }

    [Test]
    public void Serialization_round_trips_the_v2_socket_options()
    {
        var c = new TcpChannelConfiguration
        {
            Host = "host",
            Port = 4321,
            AutoReconnect = false,
            DefaultReadTimeout = -1,
            DefaultWriteTimeout = -1,
            DefaultRetryWriteCount = 0,
            EnableTcpKeepalive = true,
            TcpKeepAliveTimeSeconds = 15,
            TcpKeepAliveIntervalSeconds = 5,
            TcpKeepAliveRetryCount = 4,
            NoDelay = true,
            LingerSeconds = 3,
            SendBufferSize = 64 * 1024,
            ReceiveBufferSize = 32 * 1024
        };

        var clone = c.DeepClone()!;

        clone.NoDelay.ShouldBe( true );
        clone.LingerSeconds.ShouldBe( 3 );
        clone.SendBufferSize.ShouldBe( 64 * 1024 );
        clone.ReceiveBufferSize.ShouldBe( 32 * 1024 );
        // Existing fields still round-trip.
        clone.Host.ShouldBe( "host" );
        clone.Port.ShouldBe( 4321 );
        clone.EnableTcpKeepalive.ShouldBe( true );
        clone.TcpKeepAliveTimeSeconds.ShouldBe( 15 );
    }

    [Test]
    public void DoCheckValid_rejects_invalid_buffer_sizes_and_linger()
    {
        // Each Check logs expected validation Errors to the monitor (which do not fail the test);
        // it returns the CheckValid result.
        static bool Check( Action<TcpChannelConfiguration> setup )
        {
            var c = new TcpChannelConfiguration { Host = "h", Port = 1000 };
            setup( c );
            return c.CheckValid( TestHelper.Monitor );
        }

        Check( c => c.SendBufferSize = 0 ).ShouldBeFalse();
        Check( c => c.SendBufferSize = -1 ).ShouldBeFalse();
        Check( c => c.ReceiveBufferSize = 0 ).ShouldBeFalse();
        Check( c => c.ReceiveBufferSize = -1 ).ShouldBeFalse();
        Check( c => c.LingerSeconds = -1 ).ShouldBeFalse();

        Check( c => c.LingerSeconds = 0 ).ShouldBeTrue();          // 0 = abortive close, valid.
        Check( c => c.SendBufferSize = 64 * 1024 ).ShouldBeTrue();
        Check( c => c.ReceiveBufferSize = 32 * 1024 ).ShouldBeTrue();
    }

    [Test]
    public void A_v1_blob_still_deserializes_with_defaulted_v2_fields()
    {
        // Hand-write the exact v1 byte layout (base v1 + tcp v1, no v2 fields).
        byte[] v1Blob;
        using( var mem = new MemoryStream() )
        {
            using( var w = new CKBinaryWriter( mem ) )
            {
                // CommunicationChannelConfiguration base, version 1.
                w.Write( (byte)1 );
                w.WriteNullableInt32( null );   // DefaultReadTimeout
                w.WriteNullableInt32( null );   // DefaultWriteTimeout
                w.WriteNullableInt32( null );   // DefaultRetryWriteCount
                w.WriteNullableBool( null );    // AutoReconnect
                // TcpChannelConfiguration, version 1.
                w.Write( (byte)1 );
                w.WriteNullableString( "legacy-host" );
                w.Write( 1234 );                // Port
                w.Write( true );                // EnableTcpKeepalive
                w.Write( 15 );                  // TcpKeepAliveTimeSeconds
                w.Write( 5 );                   // TcpKeepAliveIntervalSeconds
                w.Write( 4 );                   // TcpKeepAliveRetryCount
            }
            v1Blob = mem.ToArray();
        }

        TcpChannelConfiguration config;
        using( var mem = new MemoryStream( v1Blob ) )
        using( var r = new CKBinaryReader( mem ) )
        {
            config = new TcpChannelConfiguration( r );
        }

        config.Host.ShouldBe( "legacy-host" );
        config.Port.ShouldBe( 1234 );
        config.EnableTcpKeepalive.ShouldBe( true );
        config.TcpKeepAliveRetryCount.ShouldBe( 4 );
        // v2 fields take defaults.
        config.NoDelay.ShouldBe( false );
        config.LingerSeconds.ShouldBeNull();
        config.SendBufferSize.ShouldBeNull();
        config.ReceiveBufferSize.ShouldBeNull();
    }

    [Test]
    public void Reconfigure_classification_for_new_socket_options()
    {
        static TcpChannelConfiguration Base() => new() { Host = "h", Port = 1000 };

        // Identical -> null (no change).
        Base().CanDynamicReconfigureWith( Base() ).ShouldBeNull();

        // Live-settable options -> true (dynamic reconfigure).
        var withNoDelay = Base(); withNoDelay.NoDelay = true;
        Base().CanDynamicReconfigureWith( withNoDelay ).ShouldBe( true );

        var withLinger = Base(); withLinger.LingerSeconds = 0;
        Base().CanDynamicReconfigureWith( withLinger ).ShouldBe( true );

        // Buffer sizes -> false (must reopen, best set pre-connect).
        var withSnd = Base(); withSnd.SendBufferSize = 64 * 1024;
        Base().CanDynamicReconfigureWith( withSnd ).ShouldBe( false );

        var withRcv = Base(); withRcv.ReceiveBufferSize = 32 * 1024;
        Base().CanDynamicReconfigureWith( withRcv ).ShouldBe( false );

        // Host/port still force reopen.
        var withHost = Base(); withHost.Host = "other";
        Base().CanDynamicReconfigureWith( withHost ).ShouldBe( false );
    }

    [Test]
    [CancelAfter( 3000 )]
    public async Task TcpChannel_connects_with_socket_options_and_reconfigures_Async( CancellationToken cancel )
    {
        IPAddress host = IPAddress.Loopback;
        int port = 47740;
        await using var echoServer = new TcpEchoServer( host, port );
        echoServer.Start();

        var config = new TcpChannelConfiguration
        {
            Host = host.ToString(),
            Port = port,
            AutoReconnect = false,
            DefaultReadTimeout = -1,
            DefaultWriteTimeout = -1,
            DefaultRetryWriteCount = 0,
            NoDelay = true,
            LingerSeconds = 1,
            SendBufferSize = 64 * 1024,
            ReceiveBufferSize = 32 * 1024
        };

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config );

        // ctor applied NoDelay + Linger + both buffer sizes before connect, without throwing.
        await EchoAsync( cc, "ping1", cancel );

        // Flip NoDelay only -> dynamic reconfigure on the same socket.
        var dyn = config.DeepClone()!;
        dyn.NoDelay = false;
        await cc.ReconfigureAsync( TestHelper.Monitor, dyn );
        await EchoAsync( cc, "ping2", cancel );

        // Change SendBufferSize -> forces reopen; the fresh ctor re-applies all options.
        var reopen = dyn.DeepClone()!;
        reopen.SendBufferSize = 128 * 1024;
        await cc.ReconfigureAsync( TestHelper.Monitor, reopen );
        await EchoAsync( cc, "ping3", cancel );
    }

    [Test]
    [CancelAfter( 3000 )]
    public async Task Dynamic_reconfigure_with_keep_alive_staying_on_does_not_throw_Async( CancellationToken cancel )
    {
        // Regression: SetupSocketKeepAlive is re-applied on every dynamic reconfigure when keep-alive
        // is enabled. On that path the socket already has SO_KEEPALIVE == 1, so a precondition assert
        // that it is 0 would (wrongly) fail. This exercises the previously-uncovered path.
        IPAddress host = IPAddress.Loopback;
        int port = 47742;
        await using var echoServer = new TcpEchoServer( host, port );
        echoServer.Start();

        var config = new TcpChannelConfiguration
        {
            Host = host.ToString(),
            Port = port,
            AutoReconnect = false,
            DefaultReadTimeout = -1,
            DefaultWriteTimeout = -1,
            DefaultRetryWriteCount = 0,
            EnableTcpKeepalive = true,   // ctor enables SO_KEEPALIVE on the socket.
            NoDelay = false
        };

        await using var cc = CommunicationChannel.Create( TestHelper.Monitor, config );
        await EchoAsync( cc, "ka1", cancel );

        // Keep-alive stays ON; only NoDelay changes -> dynamic reconfigure re-applies keep-alive
        // on the already-keep-alive socket. Must complete without throwing.
        var dyn = config.DeepClone()!;
        dyn.NoDelay = true;
        await cc.ReconfigureAsync( TestHelper.Monitor, dyn );
        await EchoAsync( cc, "ka2", cancel );
    }

    static async Task EchoAsync( CommunicationChannel cc, string message, CancellationToken cancel )
    {
        await cc.Writer.WriteAsync( Encoding.UTF8.GetBytes( message ), cancel );
        var read = await cc.Reader.ReadAsync( cancel );
        Encoding.UTF8.GetString( read.Buffer.ToArray() ).ShouldBe( message );
        cc.Reader.AdvanceTo( read.Buffer.End );
    }
}
