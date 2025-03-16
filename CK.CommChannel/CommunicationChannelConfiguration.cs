using CK.Core;
using System.Threading;

namespace CK.CommChannel;

/// <summary>
/// Base class for any <see cref="CommunicationChannel"/> configuration.
/// </summary>
public abstract class CommunicationChannelConfiguration : ICKSimpleBinarySerializable
{
    /// <summary>
    /// Initializes a new default configuration with null <see cref="DefaultReadTimeout"/>, <see cref="DefaultWriteTimeout"/>
    /// <see cref="DefaultRetryWriteCount"/> and <see cref="AutoReconnect"/>.
    /// </summary>
    protected CommunicationChannelConfiguration()
    {
    }

    /// <summary>
    /// Checks whether a <see cref="ChannelImpl"/> that currently uses this configuration can be dynamically
    /// reconfigured with the provided configuration.
    /// <para>
    /// When this method returns null, it means that this configuration and the new one are exactly the same:
    /// the reconfiguration is useless.
    /// </para>
    /// <para>
    /// When this method returns false, reconfiguration is not possible: the channel implementation should be
    /// disposed and a new one with the new configuration should be created.
    /// </para>
    /// <para>
    /// When this method returns true on the current channel configuration, <see cref="ChannelImpl.DynamicReconfigureAsync"/> can
    /// be called and must succeed.
    /// </para>
    /// </summary>
    /// <param name="configuration">The future configuration to apply.</param>
    /// <returns>
    /// Null if nothing changed, true if a channel implementation with this configuration can be dynamically reconfigured with the new one. False otherwise.
    /// </returns>
    public bool? CanDynamicReconfigureWith( CommunicationChannelConfiguration configuration )
    {
        Throw.CheckNotNullArgument( configuration );
        bool? r = DoCanDynamicReconfigureWith( configuration );
        if( r == null
            && (DefaultReadTimeout != configuration.DefaultReadTimeout
                || DefaultWriteTimeout != configuration.DefaultWriteTimeout
                || DefaultRetryWriteCount != configuration.DefaultRetryWriteCount
                || AutoReconnect != configuration.AutoReconnect) )
        {
            r = true;
        }
        return r;
    }

    /// <summary>
    /// Must implement <see cref="CanDynamicReconfigureWith(CommunicationChannelConfiguration)"/>, ignoring <see cref="DefaultReadTimeout"/>,
    /// <see cref="DefaultWriteTimeout"/>, <see cref="DefaultRetryWriteCount"/> and <see cref="AutoReconnect"/>
    /// <para>
    /// It's up to the caller to ensure that <see cref="CheckValid(IActivityMonitor)"/> has been successfully called on the new
    /// configuration. No check is done here (in the Ck-DeviceModel, this check is automatically done since a communication configuration
    /// belongs to the device configuration that is checked before being applied).
    /// </para>
    /// </summary>
    /// <param name="configuration">The future configuration to apply.</param>
    /// <returns>
    /// Null if nothing changed, true if <see cref="ChannelImpl.DynamicReconfigureAsync"/> must be called,
    /// and false otherwise: the whole <see cref="ChannelImpl"/> must be closed and a new one must be reopened.
    /// </returns>
    protected abstract bool? DoCanDynamicReconfigureWith( CommunicationChannelConfiguration configuration );

    /// <summary>
    /// Deserialization constructor.
    /// Every specialized configuration MUST define its own deserialization
    /// constructor (that must call its base) and the <see cref="Write(ICKBinaryWriter)"/>
    /// method must be overridden.
    /// </summary>
    /// <param name="r">The reader.</param>
    protected CommunicationChannelConfiguration( ICKBinaryReader r )
    {
        var v = (int)r.ReadByte(); // Version
        if( v == 0 )
        {
            DefaultReadTimeout = r.ReadNonNegativeSmallInt32();
            DefaultWriteTimeout = r.ReadNonNegativeSmallInt32();
        }
        else 
        {
            DefaultReadTimeout = r.ReadNullableInt32();
            DefaultWriteTimeout = r.ReadNullableInt32();
            DefaultRetryWriteCount = r.ReadNullableInt32();
            AutoReconnect = r.ReadNullableBool();
        }
    }

    /// <summary>
    /// Writes this configuration to the binary writer.
    /// This method MUST be overridden and MUST start with:
    /// <list type="bullet">
    ///     <item>A call to <c>base.Write( w );</c></item>
    ///     <item>Writing its version number (typically a byte).</item>
    /// </list>
    /// </summary>
    /// <param name="w">The writer.</param>
    public virtual void Write( ICKBinaryWriter w )
    {
        w.Write( (byte)1 ); // Version
        w.WriteNullableInt32( DefaultReadTimeout );
        w.WriteNullableInt32( DefaultWriteTimeout );
        w.WriteNullableInt32( DefaultRetryWriteCount );
        w.WriteNullableBool( AutoReconnect );
    }

    /// <summary>
    /// Gets or sets the read timeout in milliseconds that will be ultimately used
    /// when reading without a true <see cref="CancellationToken.CanBeCanceled"/>.
    /// <para>
    /// 0 is invalid and it must be positive to be effective: when -1, <see cref="CancellationToken.None"/>
    /// is used and this removes any time check
    /// </para>
    /// <para>
    /// Note: 2100000000 ms is around 24 days and 8 hours.
    /// </para>
    /// </summary>
    public int? DefaultReadTimeout { get; set; }

    /// <summary>
    /// Gets or sets the write timeout in milliseconds that will be ultimately used
    /// when writing without a true <see cref="CancellationToken.CanBeCanceled"/>.
    /// <para>
    /// 0 is invalid and it must be positive to be effective: when -1, <see cref="CancellationToken.None"/>
    /// is used and this removes any time check.
    /// </para>
    /// <para>
    /// Note: 2100000000 ms is around 24 days and 8 hours.
    /// </para>
    /// </summary>
    public int? DefaultWriteTimeout { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of times to retry a writing
    /// when a timeout occurs.
    /// <para>
    /// To be effective, a positive <see cref="DefaultWriteTimeout"/> must also be specified
    /// otherwise this is ignored.
    /// </para>
    /// </summary>
    public int? DefaultRetryWriteCount { get; set; }

    /// <summary>
    /// Gets or sets whether the channel must attempt to automatically reconnect
    /// whenever a communication error occurs.
    /// </summary>
    public bool? AutoReconnect { get; set; }

    /// <summary>
    /// Checks whether this configuration is valid.
    /// This checks that non null <see cref="DefaultReadTimeout"/> and <see cref="DefaultWriteTimeout"/>
    /// are positive (or -1) and always calls the protected <see cref="DoCheckValid(IActivityMonitor, bool)"/>
    /// that can handle specialized checks.
    /// </summary>
    /// <param name="monitor">The monitor to log errors or warnings or information.</param>
    /// <returns>True if this configuration is valid, false otherwise.</returns>
    public bool CheckValid( IActivityMonitor monitor )
    {
        Throw.CheckNotNullArgument( monitor );
        bool success = true;
        if( DefaultReadTimeout.HasValue && (DefaultReadTimeout.Value <= -2 || DefaultReadTimeout.Value == 0) )
        {
            monitor.Error( $"DefaultReadTimeout must be positive (or -1 to suppress read timeout)." );
            success = false;
        }
        if( DefaultWriteTimeout.HasValue && (DefaultWriteTimeout.Value <= -2 || DefaultWriteTimeout.Value == 0) )
        {
            monitor.Error( $"DefaultWriteTimeout must be positive (or -1 to suppress write timeout)." );
            success = false;
        }
        if( DefaultRetryWriteCount.HasValue && DefaultRetryWriteCount.Value < 0 )
        {
            monitor.Error( $"DefaultRetryWriteCount must be 0 or positive." );
            success = false;
        }
        return DoCheckValid( monitor, success ) && success;
    }

    /// <summary>
    /// Optional extension point to check for validity that is aways called by CheckValid (even if
    /// <see cref="DefaultWriteTimeout"/> or <see cref="DefaultReadTimeout"/> are invalid) so that the whole
    /// configuration can be checked.
    /// <para>
    /// Always returns true by default.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to log error, warnings or other.</param>
    /// <param name="currentSuccess">Current success status from the base configuration.</param>
    /// <returns>True if this configuration is valid, false otherwise.</returns>
    protected virtual bool DoCheckValid( IActivityMonitor monitor, bool currentSuccess ) => true;

    internal ChannelImpl CreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection )
    {
        var impl = DoCreateChannelImpl( monitor, canOpenConnection );
        Throw.CheckState( "When canOpenConnection is false, no initial connection should be made.",
                            canOpenConnection || impl.Input == null );
        return impl;
    }

    /// <summary>
    /// Factory method of channel implementation.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="canOpenConnection">
    /// Specify whether the channel can be opened directly.
    /// When false, even if it is possible, no <see cref="ChannelImpl.Input"/> nor Output
    /// must be provided.
    /// </param>
    /// <returns>
    /// The channel implementation. Note that it is most often not functional: <see cref="ChannelImpl.Input"/>
    /// and Output can be null.
    /// </returns>
    protected abstract ChannelImpl DoCreateChannelImpl( IActivityMonitor monitor, bool canOpenConnection );

    /// <summary>
    /// Overridden to return the values of these properties.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString()
    {
        return $"AutoReconnect: {AutoReconnect}, DefaultReadTimeout: {DefaultReadTimeout}, DefaultWriteTimeout: {DefaultWriteTimeout}, DefaultRetryWriteCount: {DefaultRetryWriteCount}";
    }
}
