using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

sealed class SlowPipeWriter : PipeWriter
{
    readonly PipeWriter _inner;

    public SlowPipeWriter( PipeWriter inner )
    {
        _inner = inner;
    }

    public int Delay { get; set; }

    public override Memory<byte> GetMemory( int sizeHint = 0 ) => _inner.GetMemory( sizeHint );

    public override Span<byte> GetSpan( int sizeHint = 0 ) => _inner.GetSpan( sizeHint );

    public override void Advance( int bytes ) => _inner.Advance( bytes );

    public override void CancelPendingFlush() => _inner.CancelPendingFlush();

    public override void Complete( Exception? exception = null ) => _inner.Complete( exception );

    public override async ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
    {
        await Task.Delay( Delay, cancellationToken );
        return await _inner.FlushAsync( cancellationToken );
    }

}

