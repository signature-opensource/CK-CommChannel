using CK.Core;
using System;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

sealed class TcpChannel : ChannelImpl
{
    readonly Socket _client;
    NetworkStream? _stream;

    internal TcpChannel( IActivityMonitor monitor, TcpChannelConfiguration configuration )
        : base( IStablePipeBehavior.Default, IStablePipeBehavior.Default )
    {
        Throw.DebugAssert( configuration != null );
        Throw.DebugAssert( "Configuration has been validated.", configuration.Host != null );
        _client = new Socket( SocketType.Stream, ProtocolType.Tcp );
        _client.NoDelay = configuration.NoDelay;
        if( configuration.LingerSeconds is int linger )
        {
            _client.LingerState = new LingerOption( true, linger );
        }
        if( configuration.SendBufferSize is int sndBuf )
        {
            _client.SendBufferSize = sndBuf;
        }
        if( configuration.ReceiveBufferSize is int rcvBuf )
        {
            _client.ReceiveBufferSize = rcvBuf;
        }
        if( configuration.EnableTcpKeepalive )
        {
            SetupSocketKeepAlive( monitor, _client, configuration );
        }
    }

    protected override async Task DoInitialOpenAsync( IActivityMonitor monitor,
                                                      CommunicationChannelConfiguration configuration,
                                                      CancellationToken cancel )
    {
        Throw.DebugAssert( Input == null && Output == null );

        var c = (TcpChannelConfiguration)configuration;
        Throw.DebugAssert( "Configuration has been validated.", c.Host != null );
        await _client.ConnectAsync( c.Host, c.Port, cancel ).ConfigureAwait( false );
        _stream = new NetworkStream( _client, true );
        OnInitialOpen( PipeReader.Create( _stream ), PipeWriter.Create( _stream ) );
    }

    protected internal override ValueTask DynamicReconfigureAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration )
    {
        var c = (TcpChannelConfiguration)configuration;
        _client.NoDelay = c.NoDelay;
        if( c.LingerSeconds is int linger )
        {
            _client.LingerState = new LingerOption( true, linger );
        }
        if( c.EnableTcpKeepalive )
        {
            SetupSocketKeepAlive( monitor, _client, c );
        }
        else
        {
            ResetSocketKeepAlive( monitor, _client );
        }
        return default;
    }

    public override ValueTask DisposeAsync( IActivityMonitor monitor )
    {
        // The stream owns the client.
        _stream?.Dispose();
        return default;
    }

    static void SetupSocketKeepAlive( IActivityMonitor monitor, Socket socket, TcpChannelConfiguration configuration )
    {
        // SO_KEEPALIVE is a socket-level option; the three TcpKeepAlive* tuning options are TCP-level.
        // Each option is set independently so one unsupported option (e.g. TcpKeepAliveRetryCount on
        // Windows < 1709) cannot prevent the others from applying. No precondition on the current
        // KeepAlive state: this runs both on a fresh socket (ctor) and on an already-configured one
        // (DynamicReconfigureAsync), and unconditionally enables it.
        TrySetSocketOption( monitor, "KeepAlive", () => socket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true ) );
        TrySetSocketOption( monitor, "TcpKeepAliveInterval", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, configuration.TcpKeepAliveIntervalSeconds ) );
        TrySetSocketOption( monitor, "TcpKeepAliveTime", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, configuration.TcpKeepAliveTimeSeconds ) );
        TrySetSocketOption( monitor, "TcpKeepAliveRetryCount", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, configuration.TcpKeepAliveRetryCount ) );
    }

    static void ResetSocketKeepAlive( IActivityMonitor monitor, Socket socket )
    {
        TrySetSocketOption( monitor, "KeepAlive", () => socket.SetSocketOption( SocketOptionLevel.Socket, SocketOptionName.KeepAlive, 0 ) );
        TrySetSocketOption( monitor, "TcpKeepAliveInterval", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1 ) );
        TrySetSocketOption( monitor, "TcpKeepAliveTime", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 7200 ) );
        TrySetSocketOption( monitor, "TcpKeepAliveRetryCount", () => socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 10 ) );
    }

    static void TrySetSocketOption( IActivityMonitor monitor, string optionName, Action set )
    {
        try
        {
            set();
        }
        catch( SocketException ex )
        {
            monitor.Warn( $"Error while setting socket option '{optionName}'.", ex );
        }
    }
}
