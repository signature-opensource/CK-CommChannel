using CK.Core;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;


/// <summary>
/// A StablePipeReader is stable: it's a <see cref="PipeReader"/> decorator that can forget or change
/// its inner reader in a thread safe manner and provides a <see cref="DefaultTimeout"/>
/// that is used when a non cancelable token is provided to <see cref="ReadAsync(CancellationToken)"/>
/// (and generates a <see cref="TimeoutException"/> when the timeout expires).
/// </summary>
public sealed class StablePipeReader : PipeReader, IStablePipe
{
    PipeReader? _current;
    // The _readerForAdvance is necessary only in the edge case where the
    // same reader is set again after a close: this guaranties that the Advance
    // will be correctly done for the previously obtained ReadResult.
    PipeReader? _readerForAdvance;
    object _lock;
    TaskCompletionSource? _waiter;
    Exception? _completeException;
    CancellationTokenSource? _ctsTimeout;
    readonly IStablePipeBehavior _behavior;

    int _reading;
    int _defaultTimeout;
    bool _completeWhenDone;

    /// <summary>
    /// Initializes a new <see cref="StablePipeReader"/> with no <see cref="PipeBehavior"/>.
    /// </summary>
    public StablePipeReader()
    {
        _lock = new object();
        _waiter = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        _behavior = IStablePipeBehavior.Default;
    }

    /// <summary>
    /// Initializes a new <see cref="StablePipeReader"/> bound to a <see cref="IStablePipeBehavior"/>.
    /// </summary>
    /// <param name="behavior">The behavior to use.</param>
    public StablePipeReader( IStablePipeBehavior behavior )
        : this()
    {
        Throw.CheckNotNullArgument( behavior );
        _behavior = behavior;
    }

    /// <inheritdoc />
    public IStablePipeBehavior PipeBehavior => _behavior;

    /// <summary>
    /// Gets or sets the read timeout in milliseconds that will be ultimately used
    /// when reading without a true <see cref="CancellationToken.CanBeCanceled"/>.
    /// <para>
    /// It must be positive to be effective: when -1 and <see cref="CancellationToken.None"/> is used, this
    /// suppresses any timeout.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no read timeout by default.
    /// </para>
    /// </summary>
    public int DefaultTimeout
    {
        get => _defaultTimeout == 0 ? -1 : _defaultTimeout;
        set
        {
            Throw.CheckOutOfRangeArgument( value == -1 || value > 0 );
            _defaultTimeout = value == -1 ? 0 : value;
        }
    }

    /// <inheritdoc />
    public bool IsCompleted => _waiter == null;

    /// <inheritdoc />
    public bool IsAvailable => _current != null;

    /// <inheritdoc />
    public Task StateChange => _waiter?.Task ?? Task.CompletedTask;

    /// <summary>
    /// Closes this reader, either temporarily or completes it definitely.
    /// </summary>
    /// <param name="complete">True to complete this reader.</param>
    /// <returns>True is this closed the reader, false if it was already closed.</returns>
    public bool Close( bool complete )
    {
        PipeReader? previous = null;
        bool completeWhenDone = false;
        lock( _lock )
        {
            if( _waiter != null )
            {
                previous = DoCloseOrSet( complete, null );
                completeWhenDone = _completeWhenDone;
            }
        }
        if( previous != null )
        {
            previous.CancelPendingRead();
            if( completeWhenDone ) previous.Complete();
            return true;
        }
        return false;
    }

    PipeReader? DoCloseOrSet( bool complete, PipeReader? reader )
    {
        Throw.DebugAssert( !complete || reader == null );
        Throw.DebugAssert( "If a new reader is set, it is not the same as the current one.", reader == null || reader != _current );
        var waiter = _waiter;
        Throw.DebugAssert( Monitor.IsEntered( _lock ) && waiter != null );

        PipeReader? previous = _current;
        _current = reader;
        if( complete )
        {
            _waiter = null;
            // If we are not reading, we cleanup the cts.
            if( _reading == 0 && _ctsTimeout != null )
            {
                _ctsTimeout.Dispose();
                _ctsTimeout = null;
            }
            waiter.SetResult();
            // Setting -1 into reading enables ReadAsync()
            // to cleanup the cts in its finally block if completion occurred
            // during the read.
            Interlocked.Exchange( ref _reading, -1 );
        }
        else if( previous != reader)
        {
            _waiter = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            waiter.SetResult();
        }
        return previous;
    }

    /// <summary>
    /// Sets a new reader. The current reader (if any) receives a <see cref="CancelPendingRead"/>
    /// and is completed if and only if the <paramref name="completeWhenDone"/> was true when it
    /// has been set.
    /// </summary>
    /// <param name="reader">The reader to use (can be the current one).</param>
    /// <param name="completeWhenDone">True to complete this <paramref name="reader"/> when replaced by another one or when this <see cref="StablePipeReader"/> is completed.</param>
    /// <returns>True on success, false if the reader cannot be set because this StablePipeReader is completed.</returns>
    public bool SetReader( PipeReader reader, bool completeWhenDone = false )
    {
        Throw.CheckNotNullArgument( reader );
        PipeReader? previous = null;
        bool shouldCompletePrevious;
        lock( _lock )
        {
            if( _waiter == null ) return false;
            if( reader == _current )
            {
                _completeWhenDone = completeWhenDone;
                return true;
            }
            shouldCompletePrevious = _completeWhenDone;
            _completeWhenDone = completeWhenDone;
            previous = DoCloseOrSet( false, reader );
        }
        if( previous != null )
        {
            previous.CancelPendingRead();
            if( shouldCompletePrevious ) previous.Complete();
        }
        return true;
    }

    /// <inheritdoc />
    public override sealed void AdvanceTo( SequencePosition consumed )
    {
        // If AdvanceTo is called it is because some data has been read (a valid buffer
        // has been obtained).
        // If the current reader is no more the used one, we swallow any InvalidOperationException
        // but if it's still our reader that has thrown, it may mean that it has been completed
        // concurrently by an other activity.
        // Completing the reader outside of our control is an error.
        if( _readerForAdvance != null )
        {
            try
            {
                _readerForAdvance.AdvanceTo( consumed );
            }
            catch( InvalidOperationException ex )
            {
                if( _current == _readerForAdvance ) throw;
                _behavior.OnSwallowedException( "Reader for AdvanceTo is no more the current one.", ex );
            }
        }
    }

    /// <inheritdoc />
    public override sealed void AdvanceTo( SequencePosition consumed, SequencePosition examined )
    {
        // See above.
        if( _readerForAdvance != null )
        {
            try
            {
                _readerForAdvance.AdvanceTo( consumed, examined );
            }
            catch( InvalidOperationException ex )
            {
                if( _current == _readerForAdvance ) throw;
                _behavior.OnSwallowedException( "Reader for AdvanceTo is no more the current one.", ex );
            }
        }
    }

    void IStablePipe.CancelPendingOperation() => CancelPendingRead();

    /// <summary>
    /// Calls <see cref="PipeReader.CancelPendingRead"/> on the inner reader if there is one.
    /// </summary>
    public override sealed void CancelPendingRead() => _current?.CancelPendingRead();

    /// <inheritdoc />
    public Exception? CompleteException => _completeException;

    /// <summary>
    /// Definitely completes this reader.
    /// </summary>
    /// <param name="exception">
    /// An optional exception that will be available in <see cref="CompleteException"/>
    /// if this is the first completion.
    /// </param>
    public override sealed void Complete( Exception? exception = null )
    {
        if( Close( true ) ) _completeException = exception;
    }

    /// <inheritdoc />
    public override sealed bool TryRead( out ReadResult result )
    {
        int reading = Interlocked.CompareExchange( ref _reading, 1, 0 );
        if( reading == 1 ) Throw.InvalidOperationException( "Already reading." );
        if( reading == -1 )
        {
            result = new ReadResult( default, false, true );
            return false;
        }
        Throw.DebugAssert( reading == 0 );
        try
        {
            var reader = _current;
            if( reader == null )
            {
                result = new ReadResult( default, true, IsCompleted );
                return true;
            }
            // A race condition here is possible.
            // We need to protect this.
            try
            {
                if( !reader.TryRead( out result ) ) return false;
                _readerForAdvance = reader;
                // If the reader has been completed, we close this StablePipeReader
                // but not complete it: a renewed PipeReader may be obtained and set.
                if( result.IsCompleted )
                {
                    Close( false );
                }
                return true;
            }
            catch( InvalidOperationException ex )
            {
                // If the current reader is no more the used one, we swallow any InvalidOperationException
                // but if it's still our reader that has thrown, it may mean that it has been completed
                // concurrently by an other activity.
                // Completing the reader outside of our control is an error.
                if( _current == reader ) throw;
                _behavior.OnSwallowedException( "Reader for TryRead is no more the current one.", ex );
                result = new ReadResult( default, true, IsCompleted );
                return true;
            }
        }
        finally
        {
            Interlocked.Exchange( ref _reading, 0 );
        }

    }

    /// <summary>
    /// Asynchronously reads a sequence of bytes from the current <see cref="PipeReader" />.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. When the token is not cancelable (<c>default</c> for instance),
    /// the <see cref="DefaultTimeout"/> is used and this may throw a <see cref="TimeoutException"/>.
    /// </param>
    /// <returns>A <see cref="ValueTask{T}" /> representing the asynchronous read operation.</returns>
    public override sealed async ValueTask<ReadResult> ReadAsync( CancellationToken cancellationToken = default )
    {
        int reading = Interlocked.CompareExchange( ref _reading, 1, 0 );
        if( reading == 1 ) Throw.InvalidOperationException( "Already reading." );
        if( reading == -1 )
        {
            return new ReadResult( default, false, true );
        }
        Throw.DebugAssert( reading == 0 && _reading == 1 );
        try
        {
            PipeReader? reader;
            ReadResult result = default;
            Exception? timeoutOrCompletedEx = null;

            retry:
            Throw.DebugAssert( "It's here to be thrown.", timeoutOrCompletedEx == null );

            // First wait for an available reader or a completion of this StablePipeReader.
            while( (reader = _current) == null )
            {
                // Reader is gone: if it's completed, we have nothing more to do.
                // If we are still alive, we wait for the reader to be available with the provided token
                // (that may throw an OperationCanceledException: this not our concern).
                Task waiter;
                lock( _lock )
                {
                    if( _waiter == null ) return new ReadResult( default, false, true );
                    waiter = StateChange;
                }
                await waiter.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
            _readerForAdvance = reader;
            // Then calls ReadAsync with the provided token xor our timeout token.
            try
            {
                // If there is a really cancelable token, then we use it.
                // But if it's a non cancelable one, then we try to use our non 0 DefaultTimeout.
                int timeout;
                if( cancellationToken.CanBeCanceled || (timeout = _defaultTimeout) == 0 )
                {
                    result = await reader.ReadAsync( cancellationToken ).ConfigureAwait( false );
                }
                else
                {
                    if( _ctsTimeout == null ) _ctsTimeout = new CancellationTokenSource( timeout );
                    else _ctsTimeout.CancelAfter( timeout );
                    try
                    {
                        result = await reader.ReadAsync( _ctsTimeout.Token ).ConfigureAwait( false );
                    }
                    catch( OperationCanceledException ex ) when( ex.CancellationToken == _ctsTimeout.Token )
                    {
                        _ctsTimeout.Dispose();
                        _ctsTimeout = null;
                        timeoutOrCompletedEx = new TimeoutException( "StablePipeReader timeout.", ex );
                        switch( _behavior.OnError( timeoutOrCompletedEx ) )
                        {
                            case OnErrorAction.Retry:
                                // OnError should have call this Close (or even SetReader with a new one),
                                // and if it has not and simply wants us to swallow the exception, it's the same
                                // for us here: we retry the read.
                                timeoutOrCompletedEx = null;
                                goto retry;
                            case OnErrorAction.Cancel:
                                return IsCompleted
                                    ? new ReadResult( default, false, true )
                                    : new ReadResult( default, true, false );
                        }
                    }
                    finally
                    {
                        // DoClose( true ) doesn't dispose the _ctsTimeout when _running.
                        // Here we just reset or get rid of it.
                        if( _ctsTimeout != null && !_ctsTimeout.TryReset() )
                        {
                            _ctsTimeout.Dispose();
                            _ctsTimeout = null;
                        }
                    }
                }
                if( timeoutOrCompletedEx == null )
                {
                    // If we are completed, returns it.
                    if( IsCompleted ) return new ReadResult( result.Buffer, false, true );
                    // If the inner reader has been completed, we close this StablePipeReader
                    // but not necessarily complete it: a renewed PipeReader may be obtained and set.
                    // Note that the default behavior is to complete.
                    if( result.IsCompleted )
                    {
                        // The inner reader has been completed and we are not completed.
                        // If there is no error handler or it returns OnInnerCompletedAction.Complete we
                        // complete this StablePipeReader.
                        switch( _behavior.OnInnerCompleted() )
                        {
                            case OnInnerCompletedAction.Retry:
                                Close( false );
                                // If we have no data, we can retry immediately.
                                if( result.Buffer.IsEmpty )
                                {
                                    goto retry;
                                }
                                // If there is data, it needs to be consumed.
                                return new ReadResult( result.Buffer, true, false );
                            case OnInnerCompletedAction.Throw:
                                timeoutOrCompletedEx = new InvalidOperationException( "The inner PipeWriter has been completed outside of the StablePipeWriter control." );
                                break;
                            default:
                                Close( true );
                                return new ReadResult( result.Buffer, false, true );
                        }
                    }
                    if( timeoutOrCompletedEx == null )
                    {
                        // No data: we can retry immediately.
                        // If the result is canceled we ask the error handler before retrying:
                        // if the handler wants the canceled to be returned, do it.
                        if( result.Buffer.IsEmpty
                            && (!result.IsCanceled || !_behavior.ReturnInnerCanceledResult()) )
                        {
                            goto retry;
                        }
                        // Data is available or the handler wants the canceled result to be returned.
                        return result;
                    }
                }
            }
            catch( OperationCanceledException ex )
            {
                // If the operation has been canceled (it's not a communication timeout),
                // we must always let the exception bubble up.
                // We warn the controller if we have not been closed or another inner reader has been set.
                if( _current == reader )
                {
                    _behavior.OnOperationCanceled( ex );
                }
                throw;
            }
            catch( Exception ex )
            {
                // If we have been closed or another inner reader has been set after the StartRead then we
                // silently swallow any exception and wait for reader availability or completion.
                // Note that this could be a Timeout from the inner reader (out of our control). We consider it as
                // any other kind of exception.
                if( _current != reader )
                {
                    _behavior.OnSwallowedException( "The PipeReader used by ReadAsync is no longer the current one.", ex );
                    goto retry;
                }
                switch( _behavior.OnError( ex ) )
                {
                    case OnErrorAction.Retry: goto retry;
                    case OnErrorAction.Cancel:
                        return new ReadResult( default, true, false );
                }
                throw;
            }
            Throw.DebugAssert( timeoutOrCompletedEx != null );
            throw timeoutOrCompletedEx;
        }
        finally
        {
            reading = Interlocked.Exchange( ref _reading, 0 );
            // Close( true ) disposes the _ctsTimeout when _reading is 0.
            // But if completes happened while we were reading, we must ensure the final dispose here.
            if( reading == -1 )
            {
                Throw.DebugAssert( _waiter == null );
                _ctsTimeout?.Dispose();
                _ctsTimeout = null;
            }
        }
    }

}
