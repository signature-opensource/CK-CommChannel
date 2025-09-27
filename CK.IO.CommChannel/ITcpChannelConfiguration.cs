using CK.Core;
using System.ComponentModel;

namespace CK.IO.CommChannel;

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
}
