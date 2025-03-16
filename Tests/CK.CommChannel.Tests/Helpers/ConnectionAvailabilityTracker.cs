using CK.Core;
using System.Collections.Generic;

namespace CK.CommChannel.Tests;

sealed class ConnectionAvailabilityTracker
{
    readonly CommunicationChannel _channel;
    readonly List<ConnectionAvailability> _events;


    public ConnectionAvailabilityTracker( CommunicationChannel channel )
    {
        _channel = channel;
        _channel.ConnectionStatusChanged.Sync += OnEvent;
        _events = new List<ConnectionAvailability>();
    }

    void OnEvent( IActivityMonitor monitor, CommunicationChannelStatusChangedEvent e )
    {
        monitor.Trace( $"{_channel.Name}: {e.ConnectionStatus}" );
        lock( _events ) _events.Add( e.ConnectionStatus );
    }

    public ConnectionAvailability[] Events
    {
        get
        {
            lock( _events )
            {
                return _events.ToArray();
            }
        }
    }
}
