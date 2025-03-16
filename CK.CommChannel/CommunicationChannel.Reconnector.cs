using CK.Core;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

public sealed partial class CommunicationChannel
{
    sealed class Reconnector
    {
        readonly Timer _timer;
        readonly CommunicationChannel _channel;
        int _attempt;
        bool _alreadyIn;

        public Reconnector( CommunicationChannel channel, int initialWaitMS )
        {
            _channel = channel;
            _timer = new Timer( Reconnect, null, initialWaitMS, Timeout.Infinite );
        }

        void Reconnect( object? _ )
        {
            if( !_alreadyIn )
            {
                _alreadyIn = true;
                _ = _channel.OnReconnectorCallAsync( ++_attempt )
                            .ContinueWith( t => { _alreadyIn = false; }, TaskScheduler.Default );
            }
        }

        internal void Dispose()
        {
            _timer.Dispose();
        }

        internal bool PlanifyNext( IActivityMonitor monitor )
        {
            Throw.DebugAssert( _attempt >= 1 );
            if( _channel._disposed == 0 && _channel.AutoReconnect )
            {
                uint d = _attempt switch
                {
                    1 => 100,
                    2 => 150,
                    <= 4 => 250,
                    <= 10 => 500,
                    _ => 1000
                };
                monitor.Info( $"Automatic reconnection for {_channel.Name} will occur in {d} ms." );
                _timer.Change( d, unchecked((uint)Timeout.Infinite) );
                return true;
            }
            else
            {
                monitor.Debug( $"{_channel.Name} - IsDisposed: {_channel.IsDisposed}, AutoReconnect: {_channel.AutoReconnect}: no more Reconnector needed." );
            }
            return false;
        }
    }
}
