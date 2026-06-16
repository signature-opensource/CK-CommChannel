using System.Net.Sockets;
using CK.Core;

namespace CK.CommChannel;

/// <summary>
/// Configuration for TCP channel.
/// </summary>
public sealed class TcpChannelConfiguration : CommunicationChannelConfiguration
{
    /// <summary>
    /// Initializes a new empty TCP configuration.
    /// At least <see cref="Host"/> must be configured.
    /// </summary>
    public TcpChannelConfiguration()
    {
    }

    /// <summary>
    /// Gets or sets the IP address (must be valid) or a host name.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets the IP port.
    /// Defaults to 2101.
    /// </summary>
    public int Port { get; set; } = 2101;

    /// <summary>
    /// Enables the RFC1122 4.2.3.6 (TCP) Keep-Alive by configuring the underlying socket.
    /// <para>Defaults to false (per RFC1122 4.2.3.6). Not all servers support the TCP Keep-Alive.</para>
    /// </summary>
    public bool EnableTcpKeepalive { get; set; }

    /// <summary>
    /// The number of seconds a TCP connection will remain alive/idle before KeepAlive probes are sent to the remote.
    /// <para>Defaults to 7 200, 2 hours (per RFC1122 4.2.3.6).</para>
    /// <para>No effect if <see cref="EnableTcpKeepalive"/> is false.</para>
    /// <para><seealso cref="SocketOptionName.TcpKeepAliveTime"/></para>
    /// </summary>
    public int TcpKeepAliveTimeSeconds { get; set; } = 7_200;

    /// <summary>
    /// The number of seconds a TCP connection will wait for a KeepAlive response before sending another KeepAlive probe.
    /// <para>Defaults to 1 second.</para>
    /// <para>No effect if <see cref="EnableTcpKeepalive"/> is false.</para>
    /// <para><seealso cref="SocketOptionName.TcpKeepAliveInterval"/></para>
    /// </summary>
    public int TcpKeepAliveIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// <para>The number of TCP keep alive probes that will be sent before the connection is terminated.</para>
    /// <para>Defaults to 10.</para>
    /// <para>No effect if <see cref="EnableTcpKeepalive"/> is false.</para>
    /// <para><seealso cref="SocketOptionName.TcpKeepAliveRetryCount"/></para>
    /// </summary>
    public int TcpKeepAliveRetryCount { get; set; } = 10;

    /// <summary>
    /// Disables Nagle's algorithm (TCP_NODELAY) on the socket when true.
    /// Recommended for small request/response telegram protocols where latency matters more than
    /// send coalescing. Defaults to false (Nagle enabled, the OS default).
    /// <para><seealso cref="System.Net.Sockets.Socket.NoDelay"/></para>
    /// </summary>
    public bool NoDelay { get; set; }

    /// <summary>
    /// SO_LINGER timeout in seconds. Null leaves the OS default (graceful close); 0 forces an
    /// abortive close (immediate RST); a positive value lingers up to that many seconds on close.
    /// Defaults to null.
    /// <para><seealso cref="System.Net.Sockets.Socket.LingerState"/></para>
    /// </summary>
    public int? LingerSeconds { get; set; }

    /// <summary>
    /// SO_SNDBUF send buffer size in bytes. Null leaves the OS default. Best set before connect, so
    /// a change forces the channel to reopen. Defaults to null.
    /// <para><seealso cref="System.Net.Sockets.Socket.SendBufferSize"/></para>
    /// </summary>
    public int? SendBufferSize { get; set; }

    /// <summary>
    /// SO_RCVBUF receive buffer size in bytes. Null leaves the OS default. Best set before connect, so
    /// a change forces the channel to reopen. Defaults to null.
    /// <para><seealso cref="System.Net.Sockets.Socket.ReceiveBufferSize"/></para>
    /// </summary>
    public int? ReceiveBufferSize { get; set; }

    /// <inheritdoc />
    protected override ChannelImpl DoCreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection ) => new TcpChannel( monitor, this );

    /// <summary>
    /// Classifies how <paramref name="configuration"/> can be applied.
    /// <para>
    /// Returns false (a new channel implementation must be obtained) when it is not a
    /// <see cref="TcpChannelConfiguration"/>, when <see cref="Host"/> or <see cref="Port"/> differ,
    /// or when <see cref="SendBufferSize"/> or <see cref="ReceiveBufferSize"/> differ (buffer sizes
    /// are best applied before connect).
    /// </para>
    /// <para>Returns null when every option is identical.</para>
    /// <para>
    /// Otherwise returns true: only live-settable options (keep alive, <see cref="NoDelay"/> or
    /// <see cref="LingerSeconds"/>) changed and the channel implementation can be reconfigured in place.
    /// </para>
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    /// <returns>False to recreate the implementation, true to reconfigure it in place, null when unchanged.</returns>
    protected override bool? DoCanDynamicReconfigureWith( CommunicationChannelConfiguration configuration )
    {
        if( configuration is not TcpChannelConfiguration o
            || Host != o.Host
            || Port != o.Port )
        {
            return false;
        }
        // Buffer sizes are best applied before connect: a change requires a full reopen.
        if( o.SendBufferSize != SendBufferSize
            || o.ReceiveBufferSize != ReceiveBufferSize )
        {
            return false;
        }
        // Keep-alive, NoDelay and Linger are live-settable: a change is a dynamic reconfigure.
        if( o.EnableTcpKeepalive == EnableTcpKeepalive
            && o.TcpKeepAliveIntervalSeconds == TcpKeepAliveIntervalSeconds
            && o.TcpKeepAliveRetryCount == TcpKeepAliveRetryCount
            && o.TcpKeepAliveTimeSeconds == TcpKeepAliveTimeSeconds
            && o.NoDelay == NoDelay
            && o.LingerSeconds == LingerSeconds )
        {
            return null;
        }
        return true;
    }

    /// <summary>
    /// The Host must be defined and the Port mus be between 1 and 65535.
    /// </summary>
    /// <param name="monitor">The monitor to log errors or warnings or information.</param>
    /// <param name="currentSuccess">Whether the base <see cref="CommunicationChannelConfiguration"/> is valid or not.</param>
    /// <returns>True if this configuration is valid, false otherwise.</returns>
    protected override bool DoCheckValid( IActivityMonitor monitor, bool currentSuccess )
    {
        bool success = true;
        if( Port <= 0 || Port > 65535 )
        {
            success = false;
            monitor.Error( $"The specified '{nameof( Port )}' is invalid: '{Port}' (Required: 1 to 65535; Default: 2101)." );
        }
        if( string.IsNullOrEmpty( Host ) )
        {
            success = false;
            monitor.Error( $"The '{nameof( Host )}' property is required and was not specified." );
        }

        if( EnableTcpKeepalive )
        {
            if( TcpKeepAliveTimeSeconds <= 0 )
            {
                success = false;
                monitor.Error( $"The '{nameof( TcpKeepAliveTimeSeconds )}' property is invalid: " +
                               $"{TcpKeepAliveTimeSeconds} (should be positive), " +
                               $"and {nameof( EnableTcpKeepalive )} is enabled." );
            }
            if( TcpKeepAliveIntervalSeconds <= 0 )
            {
                success = false;
                monitor.Error( $"The '{nameof( TcpKeepAliveIntervalSeconds )}' property is invalid: " +
                               $"{TcpKeepAliveIntervalSeconds} (should be positive), " +
                               $"and {nameof( EnableTcpKeepalive )} is enabled." );
            }
            if( TcpKeepAliveRetryCount <= 0 )
            {
                success = false;
                monitor.Error( $"The '{nameof( TcpKeepAliveRetryCount )}' property is invalid: " +
                               $"{TcpKeepAliveRetryCount} (should be positive), " +
                               $"and {nameof( EnableTcpKeepalive )} is enabled." );
            }
        }

        if( SendBufferSize.HasValue && SendBufferSize.Value <= 0 )
        {
            success = false;
            monitor.Error( $"The '{nameof( SendBufferSize )}' property is invalid: {SendBufferSize.Value} (must be positive, or null for the OS default)." );
        }
        if( ReceiveBufferSize.HasValue && ReceiveBufferSize.Value <= 0 )
        {
            success = false;
            monitor.Error( $"The '{nameof( ReceiveBufferSize )}' property is invalid: {ReceiveBufferSize.Value} (must be positive, or null for the OS default)." );
        }
        if( LingerSeconds.HasValue && LingerSeconds.Value < 0 )
        {
            success = false;
            monitor.Error( $"The '{nameof( LingerSeconds )}' property is invalid: {LingerSeconds.Value} (must be 0 or positive, or null for the OS default)." );
        }

        return success;
    }


    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="r">The reader.</param>
    public TcpChannelConfiguration( ICKBinaryReader r )
        : base( r )
    {
        int v = r.ReadByte(); // version.
        Host = r.ReadNullableString();
        Port = r.ReadInt32();

        if( v >= 1 )
        {
            EnableTcpKeepalive = r.ReadBoolean();
            TcpKeepAliveTimeSeconds = r.ReadInt32();
            TcpKeepAliveIntervalSeconds = r.ReadInt32();
            TcpKeepAliveRetryCount = r.ReadInt32();
        }
        if( v >= 2 )
        {
            NoDelay = r.ReadBoolean();
            LingerSeconds = r.ReadNullableInt32();
            SendBufferSize = r.ReadNullableInt32();
            ReceiveBufferSize = r.ReadNullableInt32();
        }
    }

    /// <summary>
    /// Writes this configuration.
    /// </summary>
    /// <param name="w">The writer.</param>
    public override void Write( ICKBinaryWriter w )
    {
        base.Write( w );
        w.Write( (byte)2 );
        w.WriteNullableString( Host );
        w.Write( Port );

        // v1
        w.Write( EnableTcpKeepalive );
        w.Write( TcpKeepAliveTimeSeconds );
        w.Write( TcpKeepAliveIntervalSeconds );
        w.Write( TcpKeepAliveRetryCount );

        // v2
        w.Write( NoDelay );
        w.WriteNullableInt32( LingerSeconds );
        w.WriteNullableInt32( SendBufferSize );
        w.WriteNullableInt32( ReceiveBufferSize );
    }

    /// <summary>
    /// Overridden to return the host:port and base default timeouts.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"TCP: {Host}:{Port} ({base.ToString()})";

}

