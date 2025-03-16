using CK.Core;
using System;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

sealed class SerialChannel : ChannelImpl
{
    readonly SerialPort _port;

    sealed class FixedSerialReader : PipeReader
    {
        readonly PipeReader _reader;
        readonly SerialPort _port;  

        public FixedSerialReader( SerialPort port )
        {
            _reader = PipeReader.Create( port.BaseStream );
            _port = port;
        }

        public override void AdvanceTo( SequencePosition consumed ) => _reader.AdvanceTo( consumed );

        public override void AdvanceTo( SequencePosition consumed, SequencePosition examined ) => _reader.AdvanceTo( consumed, examined );

        public override void CancelPendingRead() => _reader.CancelPendingRead();

        public override void Complete( Exception? exception = null ) => _reader.Complete( exception );

        public override async ValueTask<ReadResult> ReadAsync( CancellationToken cancellationToken = default )
        {
            using( cancellationToken.UnsafeRegister( static p => ((SerialPort)p!).DiscardInBuffer(), _port ) )
            {
                return await _reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
            }
        }

        public override bool TryRead( out ReadResult result ) => _reader.TryRead( out result );
    }

    sealed class FixedSerialWriter : PipeWriter
    {
        readonly PipeWriter _writer;
        readonly SerialPort _port;

        public FixedSerialWriter( SerialPort port )
        {
            _writer = PipeWriter.Create( port.BaseStream );
            _port = port;
        }

        public override void Advance( int bytes ) => _writer.Advance( bytes );

        public override void CancelPendingFlush() => _writer.CancelPendingFlush();

        public override void Complete( Exception? exception = null ) => _writer.CancelPendingFlush();

        public override async ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
        {
            using( cancellationToken.UnsafeRegister( static p => ((SerialPort)p!).DiscardOutBuffer(), _port ) )
            {
                return await _writer.FlushAsync( cancellationToken ).ConfigureAwait( false );
            }
        }

        public override Memory<byte> GetMemory( int sizeHint = 0 ) => _writer.GetMemory( sizeHint );

        public override Span<byte> GetSpan( int sizeHint = 0 ) => _writer.GetSpan( sizeHint );
    }

    internal SerialChannel( SerialChannelConfiguration configuration )
        : base( IStablePipeBehavior.Default, IStablePipeBehavior.Default )
    {
        Throw.DebugAssert( "Configuration has been checked.", configuration.PortName != null );
        _port = new SerialPort( configuration.PortName, configuration.BaudRate, configuration.Parity, configuration.DataBits, configuration.StopBits );
        _port.Handshake = configuration.Handshake;
        _port.DtrEnable = configuration.DtrEnable;
        if( configuration.Handshake != Handshake.RequestToSend && configuration.Handshake != Handshake.RequestToSendXOnXOff )
        {
            _port.RtsEnable = configuration.RtsEnable;
        }
    }

    protected override Task DoInitialOpenAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration, CancellationToken cancel )
    {
        _port.Open();
        OnInitialOpen( new FixedSerialReader( _port ), new FixedSerialWriter( _port ) );
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync( IActivityMonitor monitor )
    {
        _port.Dispose();
        return default;
    }

    protected override ValueTask DynamicReconfigureAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration ) => default;
}
