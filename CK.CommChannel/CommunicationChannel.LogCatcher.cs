using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CK.CommChannel;


public sealed partial class CommunicationChannel
{
    sealed class LogCatcher : IActivityMonitorClient
    {
        readonly CommunicationChannel _channel;
        readonly ActivityMonitorExternalLogData[] _runningBuffer;
        int _runningBufferCount;
        int _runningBufferRollIndex;

        readonly CommunicationChannelLogEntry[] _capture;
        int _captureCount;
        bool _captureMode;

        public LogCatcher( CommunicationChannel channel, IActivityMonitorOutput output )
        {
            _runningBuffer = new ActivityMonitorExternalLogData[CommunicationChannel.ErrorContextRunningCount];
            _channel = channel;
            _capture = new CommunicationChannelLogEntry[CommunicationChannel.ErrorContextMaxCount];
            output.RegisterClient( this );
        }

        public ImmutableArray<CommunicationChannelLogEntry> OnConnectionChange( ConnectionAvailability status )
        {
            if( status == ConnectionAvailability.Connected )
            {
                if( _captureMode ) LeaveCapture();
                return ImmutableArray<CommunicationChannelLogEntry>.Empty;
            }
            if( !_captureMode )
            {
                EnterCapture();
            }
            return ImmutableArray.Create( _capture, 0, _captureCount );
        }

        void EnterCapture()
        {
            Throw.DebugAssert( !_captureMode );
            _captureMode = true;
            for( int i = 0; i < _runningBufferCount; ++i )
            {
                var d = _runningBuffer[(i + _runningBufferRollIndex) % _runningBuffer.Length];
                _capture[i] = new CommunicationChannelLogEntry( d.Tags, d.Depth, d.MaskedLevel, d.Text, d.LogTime, d.ExceptionData );
                d.Release();
            }
            _captureCount = _runningBufferCount;
            _runningBufferCount = 0;
            _runningBufferRollIndex = 0;
            Array.Clear( _runningBuffer );
        }

        void LeaveCapture()
        {
            Throw.DebugAssert( _captureMode );
            Array.Clear( _capture );
            _captureCount = 0;
            _captureMode = false;
        }

        void IActivityMonitorClient.OnUnfilteredLog( ref ActivityMonitorLogData data ) => OnLog( ref data );

        void IActivityMonitorClient.OnOpenGroup( IActivityLogGroup group ) => OnLog( ref group.Data );

        void OnLog( ref ActivityMonitorLogData data )
        {
            if( _channel.ConnectionStatus == ConnectionAvailability.Connected )
            {
                if( _captureMode ) LeaveCapture();
                if( _runningBufferCount < _runningBuffer.Length )
                {
                    _runningBuffer[_runningBufferCount++] = data.AcquireExternalData();
                }
                else
                {
                    if( _runningBufferRollIndex == _runningBuffer.Length ) _runningBufferRollIndex = -1;
                    _runningBuffer[++_runningBufferRollIndex].Release();
                    _runningBuffer[_runningBufferRollIndex] = data.AcquireExternalData();
                }
            }
            else
            {
                if( !_captureMode ) EnterCapture();
                if( _captureCount < _capture.Length )
                {
                    _capture[_captureCount++] = new CommunicationChannelLogEntry( data.Tags, data.Depth, data.MaskedLevel, data.Text, data.LogTime, data.ExceptionData );
                }
            }
        }

        void IActivityMonitorClient.OnGroupClosing( IActivityLogGroup group, ref List<ActivityLogGroupConclusion>? conclusions )
        {
        }

        void IActivityMonitorClient.OnGroupClosed( IActivityLogGroup group, IReadOnlyList<ActivityLogGroupConclusion> conclusions )
        {
        }

        void IActivityMonitorClient.OnTopicChanged( string newTopic, string? fileName, int lineNumber )
        {
        }

        void IActivityMonitorClient.OnAutoTagsChanged( CKTrait newTrait )
        {
        }
    }

}
