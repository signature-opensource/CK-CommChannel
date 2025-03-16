using CK.Core;
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
        try
        {
            // Default values for KeepAlive parameters.
            Throw.DebugAssert( (int?)socket.GetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.KeepAlive ) == 0 );
            Throw.DebugAssert( (int?)socket.GetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval ) == 1 );
            Throw.DebugAssert( (int?)socket.GetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime ) == 7200 );
            Throw.DebugAssert( (int?)socket.GetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount ) == 10 );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, true );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, configuration.TcpKeepAliveIntervalSeconds );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, configuration.TcpKeepAliveTimeSeconds );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, configuration.TcpKeepAliveRetryCount );
        }
        catch( SocketException ex )
        {
            monitor.Warn( "Error while enabling KeepAlive.", ex );
        }
    }

    static void ResetSocketKeepAlive( IActivityMonitor monitor, Socket socket )
    {
        try
        {
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.KeepAlive, 0 );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1 );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 7200 );
            socket.SetSocketOption( SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 10 );
        }
        catch( SocketException ex )
        {
            monitor.Warn( "Error while disabling KeepAlive.", ex );
        }
    }
}
