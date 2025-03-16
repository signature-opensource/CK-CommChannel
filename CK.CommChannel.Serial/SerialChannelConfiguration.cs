using CK.Core;
using System;
using System.IO.Ports;

namespace CK.CommChannel;

/// <summary>
/// Configuration for serial channel.
/// </summary>
public sealed class SerialChannelConfiguration : CommunicationChannelConfiguration
{
    /// <summary>
    /// Initializes a new empty serial port configuration.
    /// At least <see cref="PortName"/> must be specified.
    /// </summary>
    public SerialChannelConfiguration()
    {
    }

    /// <summary>Gets or sets the port for communications, including but not limited to all available COM ports.</summary>
    /// <returns>The communications port.</returns>
    /// <seealso cref="System.IO.Ports.SerialPort.PortName"/>
    public string? PortName { get; set; }

    /// <summary>Gets or sets the serial baud rate.</summary>
    /// <returns>The baud rate. Defaults to 19200.</returns>
    /// <seealso cref="System.IO.Ports.SerialPort.BaudRate"/>
    public int BaudRate { get; set; } = 19200;

    /// <summary>Gets or sets the parity-checking protocol.</summary>
    /// <returns>
    /// One of the enumeration values that represents the parity-checking protocol.
    /// The default is <see cref="System.IO.Ports.Parity.None" />.
    /// </returns>
    /// <seealso cref="SerialPort.Parity"/>
    public Parity Parity { get; set; } = Parity.None;

    /// <summary>Gets or sets the standard number of stop bits per byte.</summary>
    /// <returns>One of the <see cref="System.IO.Ports.StopBits" /> values.</returns>
    /// <seealso cref="SerialPort.StopBits"/>
    public StopBits StopBits { get; set; } = StopBits.One;

    /// <summary>Gets or sets the standard length of data bits per byte. Valid values are 5 to 8 inclusive.</summary>
    /// <returns>The data bits length.</returns>
    /// <seealso cref="SerialPort.DataBits"/>
    public int DataBits { get; set; } = 8;

    /// <summary>
    /// Gets or sets the handshaking protocol for serial port transmission of data
    /// using a value from <see cref="System.IO.Ports.Handshake" />.
    /// </summary>
    /// <returns>
    /// One of the <see cref="System.IO.Ports.Handshake" /> values.
    /// The default is <see langword="None" />.
    /// </returns>
    /// <seealso cref="SerialPort.Handshake"/>
    public Handshake Handshake { get; set; } = Handshake.None;

    /// <summary>
    /// Gets or sets a value indicating whether the Request to Send (RTS)
    /// signal is enabled during serial communication.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> to enable Request to Transmit (RTS);
    /// otherwise, <see langword="false" />.
    /// The default is <see langword="false" />.
    /// </returns>
    /// <seealso cref="SerialPort.RtsEnable"/>
    public bool RtsEnable { get; set; }

    /// <summary>
    /// Gets or sets a value that enables the Data Terminal Ready (DTR) signal during serial communication.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> to enable Data Terminal Ready (DTR);
    /// otherwise, <see langword="false" />.
    /// The default is <see langword="false" />.
    /// </returns>
    /// <seealso cref="SerialPort.DtrEnable"/>
    public bool DtrEnable { get; set; }

    protected override ChannelImpl DoCreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection ) => new SerialChannel( this );

    /// <summary>
    /// Gets whether all properties are the same and returns null (identical configuration, there's nothing to do).
    /// Returns false otherwise.
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    /// <returns>Null if it's the same, false otherwise.</returns>
    protected override bool? DoCanDynamicReconfigureWith( CommunicationChannelConfiguration configuration )
    {
        if( configuration is not SerialChannelConfiguration o ) throw new ArgumentException( "Must be a SerialChannelConfiguration.", nameof( configuration ) );
        return PortName == o.PortName
               && BaudRate == o.BaudRate
               && DataBits == o.DataBits
               && DtrEnable == o.DtrEnable
               && Handshake == o.Handshake
               && Parity == o.Parity
               && RtsEnable == o.RtsEnable
               && StopBits == o.StopBits
               ? null
               : false;
    }

    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="r">The reader.</param>
    public SerialChannelConfiguration( ICKBinaryReader r )
        : base( r )
    {
        r.ReadByte(); // Version.
        PortName = r.ReadNullableString();
        BaudRate = r.ReadInt32();
        Parity = (Parity)r.ReadInt32();

        StopBits = (StopBits)r.ReadInt32();
        Handshake = (Handshake)r.ReadInt32();

        DataBits = r.ReadNonNegativeSmallInt32();
        RtsEnable = r.ReadBoolean();
        DtrEnable = r.ReadBoolean();
    }

    /// <summary>
    /// Serializes this configuration.
    /// </summary>
    /// <param name="w">The target writer.</param>
    public override void Write( ICKBinaryWriter w )
    {
        base.Write( w );
        w.Write( (byte)0 );

        w.WriteNullableString( PortName );
        w.Write( BaudRate );
        w.Write( (int)Parity );

        w.Write( (int)StopBits );
        w.Write( (int)Handshake );

        w.WriteNonNegativeSmallInt32( DataBits );
        w.Write( RtsEnable );
        w.Write( DtrEnable );
    }
    /// <summary>
    /// The PortName must not be null or whitespace and DataBits must be in [5,8].
    /// </summary>
    /// <param name="monitor">The monitor to log errors or warnings or information.</param>
    /// <param name="currentSuccess">Whether the base <see cref="CommunicationChannelConfiguration"/> is valid or not.</param>
    /// <returns>True if this configuration is valid, false otherwise.</returns>
    protected override bool DoCheckValid( IActivityMonitor monitor, bool currentSuccess )
    {
        bool success = true;
        if( String.IsNullOrWhiteSpace( PortName ) )
        {
            monitor.Error( "Missing PortName." );
            success = false;
        }
        if( DataBits < 5 || DataBits > 8 )
        {
            monitor.Error( $"DataBits invalid value '{DataBits}': must be between 5 and 8." );
            success = false;
        }
        return success;
    }

    /// <summary>
    /// Overridden to return the configuration.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"Serial: {PortName ?? "<null>"}, " +
                                         $"{BaudRate} bauds " +
                                         $"({base.ToString()}, Parity: {Parity}, StopBits: {StopBits}, DataBits: {DataBits}, " +
                                         $"Handshake: {Handshake}, RtsEnable: {RtsEnable}, DtrEnable: {DtrEnable})";

}
