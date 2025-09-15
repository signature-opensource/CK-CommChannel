using CK.Core;
using System.ComponentModel;

namespace CK.IO.CommChannel;

/// <summary>
/// Defines the configuration settings for a serial-based communication channel.
/// </summary>
public interface ISerialChannelConfiguration : IPoco
{
    /// <summary>
    /// Gets or sets the port for communications, including but not limited to all available COM ports.
    /// The communications port.
    /// </summary>
    public string? PortName { get; set; }

    /// <summary>
    /// Gets or sets the serial baud rate. The baud rate. Defaults to 19200.
    /// </summary>
    [DefaultValue( 19200 )]
    public int BaudRate { get; set; }

    /// <summary>
    /// Gets or sets the parity-checking protocol.
    /// One of the enumeration values that represents the parity-checking protocol.
    /// The default is System.IO.Ports.Parity.None.
    /// </summary>
    [DefaultValue( Parity.None )]
    public Parity Parity { get; set; }

    /// <summary>
    /// Gets or sets the standard number of stop bits per byte.
    /// Returns One of the System.IO.Ports.StopBits values.
    /// </summary>
    [DefaultValue( StopBits.One )]
    public StopBits StopBits { get; set; }

    /// <summary>
    /// Gets or sets the standard length of data bits per byte. Valid values are 5 to 8 inclusive.
    /// Returns the data bits length.
    /// </summary>
    [DefaultValue( 8 )]
    public int DataBits { get; set; }

    /// <summary>
    /// Gets or sets the handshaking protocol for serial port transmission of data using a value from System.IO.Ports.Handshake.
    /// Returns One of the System.IO.Ports.Handshake values. The default is None.
    /// </summary>
    [DefaultValue( Handshake.None )]
    public Handshake Handshake { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Request to Send (RTS) signal is enabled during serial communication.
    /// Returns: true to enable Request to Transmit (RTS); otherwise, false. The default is false.
    /// </summary>
    [DefaultValue( false )]
    public bool RtsEnable { get; set; }

    /// <summary>
    /// Gets or sets a value that enables the Data Terminal Ready (DTR) signal during serial communication.
    /// Returns: true to enable Data Terminal Ready (DTR); otherwise, false. The default is false.
    /// </summary>
    [DefaultValue( false )]
    public bool DtrEnable { get; set; }
}
