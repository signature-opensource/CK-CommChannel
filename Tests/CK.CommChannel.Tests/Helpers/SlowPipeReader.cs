using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

sealed class SlowPipeReader : PipeReader
{
    readonly PipeReader _inner;

    public SlowPipeReader( PipeReader inner )
    {
        _inner = inner;
    }

    public int Delay { get; set; }

    public override void AdvanceTo( SequencePosition consumed ) => _inner.AdvanceTo( consumed );

    public override void AdvanceTo( SequencePosition consumed, SequencePosition examined ) => _inner.AdvanceTo( consumed, examined );

    public override void CancelPendingRead() => _inner.CancelPendingRead();

    public override void Complete( Exception? exception = null ) => _inner.Complete( exception );

    public override async ValueTask<ReadResult> ReadAsync( CancellationToken cancellationToken = default )
    {
        await Task.Delay( Delay, cancellationToken );
        return await _inner.ReadAsync( cancellationToken );
    }

    public override bool TryRead( out ReadResult result ) => _inner.TryRead( out result );
}

