using CK.Core;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

/// <summary>
/// A <see cref="PipeWriter"/> that blocks in <see cref="FlushAsync(CancellationToken)"/> until
/// <see cref="Release"/> is called.
/// <para>
/// This is an explicit rendezvous: awaiting <see cref="FlushEntered"/> guarantees that a writer is
/// currently inside a flush, so a second, concurrent call can be issued deterministically.
/// </para>
/// <para>
/// Warning: starting several tasks and expecting them to overlap on their own tests nothing in
/// particular. It is only ever "very likely", and what it exercises changes with the machine.
/// </para>
/// </summary>
sealed class GatedPipeWriter : PipeWriter
{
    readonly PipeWriter _inner;
    readonly TaskCompletionSource _entered;
    readonly TaskCompletionSource _released;

    public GatedPipeWriter( PipeWriter inner )
    {
        _inner = inner;
        _entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        _released = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
    }

    /// <summary>
    /// Completes as soon as a <see cref="FlushAsync(CancellationToken)"/> is entered and blocked.
    /// </summary>
    public Task FlushEntered => _entered.Task;

    /// <summary>
    /// Unblocks the pending (and any subsequent) <see cref="FlushAsync(CancellationToken)"/>.
    /// </summary>
    public void Release() => _released.TrySetResult();

    public override Memory<byte> GetMemory( int sizeHint = 0 ) => _inner.GetMemory( sizeHint );

    public override Span<byte> GetSpan( int sizeHint = 0 ) => _inner.GetSpan( sizeHint );

    public override void Advance( int bytes ) => _inner.Advance( bytes );

    public override void CancelPendingFlush() => _inner.CancelPendingFlush();

    public override void Complete( Exception? exception = null ) => _inner.Complete( exception );

    public override async ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
    {
        _entered.TrySetResult();
        await _released.Task.WaitAsync( cancellationToken ).ConfigureAwait( false );
        return await _inner.FlushAsync( cancellationToken ).ConfigureAwait( false );
    }
}
