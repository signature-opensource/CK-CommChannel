using CK.Core;
using System.ComponentModel;
using System.Net.Sockets;

namespace CK.IO.CommChannel;

/// <summary>
/// Describes the data shape of a TCP communication channel configuration: the remote
/// <see cref="Host"/>/<see cref="Port"/>, TCP keep-alive settings, and the configurable
/// socket options. This is an IO contract (no runtime dependency) that mirrors the
/// runtime TcpChannelConfiguration.
/// </summary>
public interface ITcpChannelConfiguration : IPoco
{
    /// <summary>
    /// Gets or sets the IP address (must be valid) or a host name.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    /// Gets or sets the IP port.
    /// Defaults to 2101.
    /// </summary>
    public int Port { get; set; }

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
    [DefaultValue( 7200 )]
    public int TcpKeepAliveTimeSeconds { get; set; }

    /// <summary>
    /// The number of seconds a TCP connection will wait for a KeepAlive response before sending another KeepAlive probe.
    /// <para>Defaults to 1 second.</para>
    /// <para>No effect if <see cref="EnableTcpKeepalive"/> is false.</para>
    /// <para><seealso cref="SocketOptionName.TcpKeepAliveInterval"/></para>
    /// </summary>
    [DefaultValue( 1 )]
    public int TcpKeepAliveIntervalSeconds { get; set; }

    /// <summary>
    /// <para>The number of TCP keep alive probes that will be sent before the connection is terminated.</para>
    /// <para>Defaults to 10.</para>
    /// <para>No effect if <see cref="EnableTcpKeepalive"/> is false.</para>
    /// <para><seealso cref="SocketOptionName.TcpKeepAliveRetryCount"/></para>
    /// </summary>
    [DefaultValue( 10 )]
    public int TcpKeepAliveRetryCount { get; set; }

    /// <summary>
    /// Disables Nagle's algorithm (TCP_NODELAY) when true. Defaults to false.
    /// </summary>
    public bool NoDelay { get; set; }

    /// <summary>
    /// SO_LINGER timeout in seconds. Null = OS default (graceful close); 0 = abortive close (RST);
    /// a positive value lingers up to that many seconds. Defaults to null.
    /// </summary>
    public int? LingerSeconds { get; set; }

    /// <summary>
    /// SO_SNDBUF send buffer size in bytes. Null = OS default. Defaults to null.
    /// </summary>
    public int? SendBufferSize { get; set; }

    /// <summary>
    /// SO_RCVBUF receive buffer size in bytes. Null = OS default. Defaults to null.
    /// </summary>
    public int? ReceiveBufferSize { get; set; }
}
