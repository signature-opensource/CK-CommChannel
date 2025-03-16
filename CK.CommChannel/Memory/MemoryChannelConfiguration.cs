using CK.Core;

namespace CK.CommChannel;

/// <summary>
/// Configuration for <see cref="MemoryChannel"/>.
/// </summary>
public class MemoryChannelConfiguration : CommunicationChannelConfiguration
{
    /// <summary>
    /// Initializes a new empty configuration.
    /// </summary>
    public MemoryChannelConfiguration()
    {
    }

    /// <summary>
    /// Gets or sets the name of the endpoint.
    /// </summary>
    public string? EndPointName { get; set; }

    /// <summary>
    /// Gets or sets the maximal time to wait for the endpoint to be found when
    /// trying to connect.
    /// Defaults to 0.
    /// </summary>
    public int ConnectionTimeout { get; set; }

    /// <summary>
    /// Gets whether this channel reverts the <see cref="IPipeChannel.Input"/> and <see cref="IPipeChannel.Output"/>.
    /// </summary>
    public bool Reverted { get; set; }

    /// <inheritdoc />
    protected override ChannelImpl DoCreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection ) => new MemoryChannel();

    /// <summary>
    /// When <see cref="EndPointName"/> or <see cref="Reverted"/> differs, there cannot be any reconfiguration: this returns false.
    /// When the same EndPoint is targeted, if <see cref="ConnectionTimeout"/> are the same then null is returned
    /// (identical configuration, there's nothing to do), and if connection timeout differs, this returns true
    /// because even if the configuration actually changed, an already opened channel doesn't care anymore
    /// and we can accept the no-op reconfiguration.
    /// </summary>
    /// <param name="configuration">The new configuration to apply.</param>
    /// <returns>Null if it's the exact same endpoint and connection timeout, true if endpoint are the same, false otherwise.</returns>
    protected override bool? DoCanDynamicReconfigureWith( CommunicationChannelConfiguration configuration )
    {
        return configuration is MemoryChannelConfiguration o
               && EndPointName == o.EndPointName
               && Reverted == o.Reverted
                    ? (ConnectionTimeout == o.ConnectionTimeout
                        ? null
                        : true)
                    : false;
    }

    /// <summary>
    /// Validates that the <see cref="EndPointName"/> is not null or empty and that <see cref="ConnectionTimeout"/> is not negative.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="currentSuccess">Whether the base configuration with default timeouts is valid.</param>
    /// <returns>True if this configuration is valid. False otherwise.</returns>
    protected override bool DoCheckValid( IActivityMonitor monitor, bool currentSuccess )
    {
        bool success = true;
        if( string.IsNullOrEmpty( EndPointName ) )
        {
            success = false;
            monitor.Error( $"The '{nameof( EndPointName )}' property is required and was not specified." );
        }
        if( ConnectionTimeout < 0 )
        {
            success = false;
            monitor.Error( $"The '{nameof( ConnectionTimeout )}' must be 0 or positive." );
        }
        return success;
    }

    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="r">The reader.</param>
    public MemoryChannelConfiguration( ICKBinaryReader r )
        : base( r )
    {
        r.ReadByte(); // version.
        EndPointName = r.ReadNullableString();
        ConnectionTimeout = r.ReadNonNegativeSmallInt32();
        Reverted = r.ReadBoolean();
    }

    /// <summary>
    /// Writes this configuration.
    /// </summary>
    /// <param name="w">The writer.</param>
    public override void Write( ICKBinaryWriter w )
    {
        base.Write( w );
        w.Write( (byte)0 );
        w.WriteNullableString( EndPointName );
        w.WriteNonNegativeSmallInt32( ConnectionTimeout );
        w.Write( Reverted );
    }

    /// <summary>
    /// Overridden to return the endpoint name and base default timeouts.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"Memory: {EndPointName} ({base.ToString()})";

}
