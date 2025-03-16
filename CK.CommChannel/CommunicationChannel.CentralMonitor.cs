using CK.Core;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace CK.CommChannel;

public sealed partial class CommunicationChannel
{
    /// <summary>
    /// Singleton that emits CommunicationChannel status changed events.
    /// </summary>
    static class CentralMonitor
    {
        static readonly IActivityMonitor _monitor;
        static readonly Channel<CommunicationChannelStatusChangedEvent> _qEvent;
        static readonly Channel<DeferredLog> _qError;

        internal record class DeferredLog( CommunicationChannel C, LogLevel Level, CKTrait T, string Text, Exception? Ex );

        // Called by CommunicationChannel static initializer to early initialize this type.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void StaticInitialization() { }

        static CentralMonitor()
        {
            _monitor = new ActivityMonitor( "CommunicationChannel.CentralMonitor" );
            _qEvent = Channel.CreateUnbounded<CommunicationChannelStatusChangedEvent>( new UnboundedChannelOptions() { SingleReader = true } );
            _qError = Channel.CreateUnbounded<DeferredLog>( new UnboundedChannelOptions() { SingleReader = true } );
            _ = Task.Run( RunEventLoopAsync );
            _ = Task.Run( RunLogLoopAsync );
        }

        public static void Log( CommunicationChannel c, LogLevel warn, CKTrait tags, string text, Exception? exception )
        {
            _qError.Writer.TryWrite( new DeferredLog(c, warn, tags, text, exception) );
        }

        public static void Send( CommunicationChannelStatusChangedEvent e ) => _qEvent.Writer.TryWrite( e );

        static async Task RunEventLoopAsync()
        {
            for(; ; )
            {
                var e = await _qEvent.Reader.ReadAsync();
                await e.Channel._statusChanged.SafeRaiseAsync( _monitor, e );
            }
        }

        static async Task RunLogLoopAsync()
        {
            for(; ; )
            {
                var e = await _qError.Reader.ReadAsync();
                var c = e.C;
                try
                {
                    if( await c.EnterLockAsync().ConfigureAwait( false ) )
                    {
                        c._monitor.Log( e.Level | LogLevel.IsFiltered, e.T, e.Text, e.Ex );
                        c._semaphore.Release();
                    }
                }
                catch( Exception ex )
                {
                    ActivityMonitor.StaticLogger.Error( ActivityMonitor.Tags.ToBeInvestigated, $"Unexpected error while deferring log for {c.Name}.", ex );
                }
            }
        }

    }

}
