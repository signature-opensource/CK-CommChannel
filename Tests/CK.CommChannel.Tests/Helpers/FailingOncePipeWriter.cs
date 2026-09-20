using System;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

/// <summary>
/// A <see cref="PipeWriter"/> whose first <see cref="FlushAsync(CancellationToken)"/> calls fail,
/// so that an <see cref="IStablePipeBehavior"/> returning <see cref="OnErrorAction.Retry"/> actually
/// gets to retry.
/// </summary>
sealed class FailingOncePipeWriter : PipeWriter
{
    readonly PipeWriter _inner;
    int _remainingFailures;

    public FailingOncePipeWriter( PipeWriter inner, int failureCount = 1 )
    {
        _inner = inner;
        _remainingFailures = failureCount;
    }

    /// <summary>
    /// Gets how many times <see cref="FlushAsync(CancellationToken)"/> has been called.
    /// </summary>
    public int FlushCount { get; private set; }

    public override Memory<byte> GetMemory( int sizeHint = 0 ) => _inner.GetMemory( sizeHint );

    public override Span<byte> GetSpan( int sizeHint = 0 ) => _inner.GetSpan( sizeHint );

    public override void Advance( int bytes ) => _inner.Advance( bytes );

    public override void CancelPendingFlush() => _inner.CancelPendingFlush();

    public override void Complete( Exception? exception = null ) => _inner.Complete( exception );

    public override async ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
    {
        ++FlushCount;
        if( _remainingFailures > 0 )
        {
            --_remainingFailures;
            // An IOException is what a broken link actually looks like here.
            throw new IOException( "FailingOncePipeWriter: simulated write failure." );
        }
        return await _inner.FlushAsync( cancellationToken ).ConfigureAwait( false );
    }
}
