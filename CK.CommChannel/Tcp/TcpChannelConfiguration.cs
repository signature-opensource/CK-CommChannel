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

    protected override ChannelImpl DoCreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection ) => new TcpChannel( monitor, this );

    /// <summary>
    /// If <paramref name="configuration"/> is not a <see cref="TcpChannelConfiguration"/> or it
    /// has with different <see cref="Host"/> and <see cref="Port"/> returns false (a new channel implementation must be obtained).
    /// Otherwise if something changed in keep alive configuration, returns true: the channel implementation MUST be reconfigured.
    /// If configuration is exactly the same, returns null.
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    /// <returns>Null if it's the same, false otherwise.</returns>
    protected override bool? DoCanDynamicReconfigureWith( CommunicationChannelConfiguration configuration )
    {
        if( configuration is not TcpChannelConfiguration o
            || Host != o.Host
            || Port != o.Port )
        {
            return false;
        }
        if( o.EnableTcpKeepalive == EnableTcpKeepalive
            && o.TcpKeepAliveIntervalSeconds == TcpKeepAliveIntervalSeconds
            && o.TcpKeepAliveRetryCount == TcpKeepAliveRetryCount
            && o.TcpKeepAliveTimeSeconds == TcpKeepAliveTimeSeconds )
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
    }

    /// <summary>
    /// Writes this configuration.
    /// </summary>
    /// <param name="w">The writer.</param>
    public override void Write( ICKBinaryWriter w )
    {
        base.Write( w );
        w.Write( (byte)1 );
        w.WriteNullableString( Host );
        w.Write( Port );

        // v1
        w.Write( EnableTcpKeepalive );
        w.Write( TcpKeepAliveTimeSeconds );
        w.Write( TcpKeepAliveIntervalSeconds );
        w.Write( TcpKeepAliveRetryCount );
    }

    /// <summary>
    /// Overridden to return the host:port and base default timeouts.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"TCP: {Host}:{Port} ({base.ToString()})";

}

