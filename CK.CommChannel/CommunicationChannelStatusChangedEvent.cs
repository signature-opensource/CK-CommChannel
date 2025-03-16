using CK.Core;
using System.Collections.Immutable;

namespace CK.CommChannel;

/// <summary>
/// Event raised by <see cref="CommunicationChannel.ConnectionStatusChanged"/>.
/// </summary>
public sealed class CommunicationChannelStatusChangedEvent
{
    /// <summary>
    /// Gets the channel.
    /// </summary>
    public CommunicationChannel Channel { get; }

    /// <summary>
    /// Gets whether the connection status.
    /// </summary>
    public ConnectionAvailability ConnectionStatus { get; }

    /// <summary>
    /// Gets the context of the error. This is empty when <see cref="ConnectionStatus"/>
    /// is <see cref="ConnectionAvailability.Connected"/>.
    /// <para>
    /// When connection is lost, this contains the first log entries (no more than <see cref="CommunicationChannel.ErrorContextMaxCount"/>)
    /// that occured at the time the connection was lost.
    /// The very first entries are before the lost of the connexion (there is at most <see cref="CommunicationChannel.ErrorContextRunningCount"/>
    /// such entries).
    /// </para>
    /// </summary>
    public ImmutableArray<CommunicationChannelLogEntry> ErrorContext { get; }

    internal CommunicationChannelStatusChangedEvent( CommunicationChannel channel,
                                                     ConnectionAvailability connectStatus,
                                                     ImmutableArray<CommunicationChannelLogEntry> errorContext )
    {
        Channel = channel;
        ConnectionStatus = connectStatus;
        ErrorContext = errorContext;
    }
}
