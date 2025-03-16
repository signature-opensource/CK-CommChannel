using CK.Core;
using System;

namespace CK.CommChannel;

public sealed partial class CommunicationChannel
{
    sealed class Behavior : IStablePipeBehavior
    {
        readonly IStablePipe _pipe;
        readonly CommunicationChannel _c;
        IStablePipeBehavior _implBehavior;

        public Behavior( CommunicationChannel c, Func<Behavior,IStablePipe> pipeCtor, IStablePipeBehavior initial )
        {
            _c = c;
            _implBehavior = initial;
            _pipe = pipeCtor( this );
        }

        public IStablePipe Pipe => _pipe;

        public void SetImplBehavior( IStablePipeBehavior b ) => _implBehavior = b;

        void IStablePipeBehavior.OnSwallowedException( string reason, Exception exception )
        {
            // If we use _c._monitor.ParallelLogger here, our LogCatcher won't see the log.
            // This is interesting in CaptureMode but may be also before an error occurred.
            // Taking the lock on the channel is risky here, it is safer to rely on the
            // CentralMonitor to defer the lock.
            if( _c._monitor.ShouldLogLine( LogLevel.Warn, null, out var tags ) )
            {
                var m = $"Swallowed error on {_c.Name} {(_pipe == _c.Reader ? "reader" : "writer")} at '{DateTime.UtcNow:O}'.";
                CentralMonitor.Log( _c, LogLevel.Warn, tags, m, exception );
            }
            _implBehavior.OnSwallowedException( reason, exception );
        }

        void IStablePipeBehavior.OnOperationCanceled( OperationCanceledException ex )
        {
            // See above.
            if( _c._monitor.ShouldLogLine( LogLevel.Trace, null, out var tags ) )
            {
                var m = $"Operation canceled on {_c.Name} {(_pipe == _c.Reader ? "reader" : "writer")} at '{DateTime.UtcNow:O}'.";
                CentralMonitor.Log( _c, LogLevel.Trace, tags, m, null );
            }
            _implBehavior.OnOperationCanceled( ex );
        }

        OnErrorAction IStablePipeBehavior.OnError( Exception exception )
        {
            // If the ChannelImpl behavior wants to Retry or Cancel, let's trust it.
            var a = _implBehavior.OnError( exception );
            if( a != OnErrorAction.Throw ) return a;
            // OnErrorAction.Throw is the default but before throwing...
            // Should we really throw?
            if( exception is TimeoutMessageException or TimeoutException )
            {
                // Timeout is (for the moment) not our concern: this is bound to the user side.
                // But here (or above - on the behavior or call site), we may introduce a behavior that
                // would consider timeout as communication errors and forces a reconnection.
                return OnErrorAction.Throw;
            }
            // Preventively check disposal and AutoReconnect here.
            // There's no real race condition here because:
            //  - The AutoReconnect property is what it is, no concurrency integrity is required for it.
            //  - If the Communication is disposed once the async lock is acquired, nothing will be done:
            //    we'll simply lose the log. That is by design.
            if( _c._disposed != 0 || !_c.AutoReconnect )
            {
                _c._monitor.ParallelLogger.Error( $"Error on {_c.Name} {(_pipe == _c.Reader ? "reader" : "writer")}. Exception is thrown since AutoReconnect is false.", exception );
                return OnErrorAction.Throw;
            }
            // This fires and forget but the call enters the async lock and
            // NEVER throws.
            _ = _c.OnPipeErrorAsync( _pipe, exception );
            return OnErrorAction.Retry;
        }

        OnInnerCompletedAction IStablePipeBehavior.OnInnerCompleted()
        {
            // The default behavior here is to Complete but
            // we (normally) don't want to complete our stable pipes.
            // However, if the ChannelImpl behavior wants to Retry or Throw, let's trust it.
            var a = _implBehavior.OnInnerCompleted();
            if( a != OnInnerCompletedAction.Complete ) return a;
            // Default behavior.
            // When we are not AutoConnecting, we have one alternative when the inner pipe
            // has been completed:
            //
            //  1 - We complete the Stable pipe wrapper (by returning OnInnerCompletedAction.Complete here).
            //      The channel is in a "funny" state with one of its pipe
            //      completed and the other one still available, a "half duplex" configuration.
            //      
            //  2 - We close the ChannelImpl and not reopening it.
            //
            // A full implementation would require a enum HalfDuplexMode
            // {
            //    None, // choice 2
            //    ReadOnly,
            //    WriteOnly,
            //    AllowCompletedWrite,
            //    AllowCompletedRead,
            //    AllowOneCompleted // choice 1
            //  }
            //  Currently implementing the n°2 seems the best approach.
            //  
            // Even if the Retry closes the culprit, we must preventively close both here
            // since the reconnection may happen quickly.

            // Immediately temporary close the stable reader and writer.
            _c._writer.Close( false );
            _c._reader.Close( false );

            // This fires and forget but the call enters the async lock and
            // NEVER throws.
            // Since we cannot wait for the outcome, we decide here and now the AutoConnect status.
            bool autoReconnect = _c.AutoReconnect;
            _ = _c.OnInnerCompletedAsync( _pipe, autoReconnect );
            return autoReconnect ? OnInnerCompletedAction.Retry : OnInnerCompletedAction.Complete;
        }
    }

}
