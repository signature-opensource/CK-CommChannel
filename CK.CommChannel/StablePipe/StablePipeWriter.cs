using CK.Core;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;


/// <summary>
/// A StablePipeWriter is a <see cref="PipeWriter"/> decorator that can forget or change
/// its inner writer in a thread safe manner and provides a <see cref="DefaultTimeout"/>
/// a <see cref="RetryWriteCount"/> and a <see cref="OnDataWritten"/> event.
/// </summary>
public sealed class StablePipeWriter : PipeWriter, IStablePipe
{
    PipeWriter? _current;
    readonly ByteArrayWriter _buffer;
    // Ensures that the buffer is only written once
    // per inner writer.
    PipeWriter? _flushedWriter;
    int _flushedWriterPos;

    readonly object _lock;
    TaskCompletionSource? _waiter;
    Exception? _completeException;
    CancellationTokenSource? _ctsTimeout;
    readonly IStablePipeBehavior _behavior;

    int _writing;
    int _defaultTimeout;
    int _retryWriteCount;
    bool _completeWhenDone;

    /// <summary>
    /// Initializes a new <see cref="StablePipeWriter"/> with no <see cref="PipeBehavior"/>.
    /// </summary>
    public StablePipeWriter()
    {
        _lock = new object();
        _waiter = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        _buffer = new ByteArrayWriter();
        _behavior = IStablePipeBehavior.Default;
    }

    /// <summary>
    /// Initializes a new <see cref="StablePipeWriter"/> bound to a <see cref="IStablePipeBehavior"/>.
    /// </summary>
    /// <param name="behavior">The behavior to use.</param>
    public StablePipeWriter( IStablePipeBehavior behavior )
        : this()
    {
        Throw.CheckNotNullArgument( behavior );
        _behavior = behavior;
    }

    /// <inheritdoc />
    public IStablePipeBehavior PipeBehavior => _behavior;

    /// <summary>
    /// Gets or sets the write timeout in milliseconds that will be ultimately used
    /// when writing without a true <see cref="CancellationToken.CanBeCanceled"/>.
    /// <para>
    /// It must be positive to be effective: when -1 and <see cref="CancellationToken.None"/> is used, this
    /// suppresses any timeout.
    /// </para>
    /// <para>
    /// This must be -1 or a positive value.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no write timeout by default.
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

    /// <summary>
    /// Gets or sets the maximum number of times to retry a writing
    /// when a timeout occurs. Defaults to 0.
    /// <para>
    /// To be effective, a positive <see cref="DefaultTimeout"/> must also be specified
    /// otherwise this is ignored.
    /// </para>
    /// </summary>
    public int RetryWriteCount
    {
        get => _retryWriteCount;
        set
        {
            Throw.CheckOutOfRangeArgument( value >= 0 );
            _retryWriteCount = value;
        }
    }

    /// <inheritdoc />
    public Task StateChange => _waiter?.Task ?? Task.CompletedTask;

    /// <inheritdoc />
    public bool IsCompleted => _waiter == null;

    /// <inheritdoc />
    public bool IsAvailable => _current != null;

    /// <summary>
    /// Gets the inner writer. This should be used if, for any reason, this stable pipe writer
    /// (and its internal buffer) must not or cannot be used. By using this, you lose all the guaranties
    /// offered by this writer.
    /// </summary>
    public PipeWriter? CurrentInnerWriter => _current;

    /// <summary>
    /// Closes this writer, either temporarily or completes it definitely.
    /// </summary>
    /// <param name="complete">True to complete this reader.</param>
    /// <returns>True is this closed the writer, false if it was already closed.</returns>
    public bool Close( bool complete )
    {
        PipeWriter? previous = null;
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
            previous.CancelPendingFlush();
            if( completeWhenDone ) previous.Complete();
            return true;
        }
        return false;
    }

    PipeWriter? DoCloseOrSet( bool complete, PipeWriter? writer )
    {
        Throw.DebugAssert( "complete => writer == null", !complete || writer == null );
        Throw.DebugAssert( "If a new writer is set, it is not the same as the current one.", writer == null || writer != _current );
        var waiter = _waiter;
        Throw.DebugAssert( Monitor.IsEntered( _lock ) && waiter != null );
        PipeWriter? previous = _current;
        _current = writer;
        if( complete )
        {
            _waiter = null;
            if( _writing == 0 && _ctsTimeout != null )
            {
                _ctsTimeout.Dispose();
                _ctsTimeout = null;
            }
            waiter.SetResult();
            // See StablePipeReader.DoClose.
            Interlocked.Exchange( ref _writing, -1 );
        }
        else if( previous != writer )
        {
            _waiter = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
            waiter.SetResult();
        }
        return previous;
    }

    /// <summary>
    /// Sets a new writer. The current writer (if any) receives a <see cref="PipeWriter.CancelPendingFlush"/>
    /// and is completed if and only if the <paramref name="completeWhenDone"/> was true when it
    /// has been set.
    /// </summary>
    /// <param name="writer">The writer to use (can be the current one).</param>
    /// <param name="completeWhenDone">
    /// True to complete this <paramref name="writer"/> when replaced by another one or when this <see cref="StablePipeWriter"/>
    /// is completed.
    /// </param>
    /// <returns>True on success, false if the reader cannot be set because this StablePipeWriter is completed.</returns>
    public bool SetWriter( PipeWriter writer, bool completeWhenDone = false )
    {
        Throw.CheckNotNullArgument( writer );
        PipeWriter? previous = null;
        bool shouldCompletePrevious;
        lock( _lock )
        {
            if( _waiter == null ) return false;
            Throw.DebugAssert( _waiter != null );
            if( writer == _current )
            {
                _completeWhenDone = completeWhenDone;
                return true;
            }
            shouldCompletePrevious = _completeWhenDone;
            _completeWhenDone = completeWhenDone;
            previous = DoCloseOrSet( false, writer );
        }
        if( previous != null )
        {
            previous.CancelPendingFlush();
            if( shouldCompletePrevious ) previous.Complete();
        }
        return true;
    }

    void IStablePipe.CancelPendingOperation() => CancelPendingFlush();

    /// <inheritdoc />
    public override sealed void CancelPendingFlush() => _current?.CancelPendingFlush();

    /// <inheritdoc />
    public Exception? CompleteException => _completeException;

    /// <summary>
    /// Definitely completes this writer.
    /// </summary>
    /// <param name="exception">
    /// An optional exception that will be available in <see cref="CompleteException"/>
    /// if this is the first completion.
    /// </param>
    public override sealed void Complete( Exception? exception = null )
    {
        if( Close( true ) ) _completeException = exception;
    }

    /// <summary>
    /// Raised each time <see cref="FlushAsync(CancellationToken)"/> succeeds with the current content
    /// of the internal buffer.
    /// <para>
    /// This can typically be used for logging outgoing messages.
    /// </para>
    /// </summary>
    public event ReadOnlySpanAction<byte,StablePipeWriter>? OnDataWritten;

    /// <summary>Flushes all the bytes written so far.</summary>
    /// <param name="cancellationToken">
    /// The token to monitor for cancellation requests. When the token is not cancelable (<c>default</c> for instance),
    /// the <see cref="DefaultTimeout"/> is used and this may throw a <see cref="TimeoutException"/>.
    /// </param>
    /// <returns>A task that represents and wraps the asynchronous flush operation.</returns>
    public sealed override async ValueTask<FlushResult> FlushAsync( CancellationToken cancellationToken = default )
    {
        if( _buffer.Position == 0 ) return new FlushResult( false, IsCompleted );
        int writing = Interlocked.CompareExchange( ref _writing, 1, 0 );
        if( writing == 1 ) Throw.InvalidOperationException( "Already writing." );
        if( writing == -1 )
        {
            return new FlushResult( false, true );
        }
        Throw.DebugAssert( _writing == 1 );
        PipeWriter? writer;
        Exception? timeoutOrCompletedEx;
        try
        {
            // First wait for an available writer or a completion of this StablePipeWriter.
            retry:
            while( (writer = _current) == null )
            {
                // Inner writer is gone: if we are completed, we have nothing more to do.
                // If we are still alive, we wait for the writer to be available with the provided token
                // (that may throw an OperationCanceledException).
                Task waiter;
                lock( _lock )
                {
                    if( _waiter == null ) return new FlushResult( false, true );
                    waiter = StateChange;
                }
                await waiter.WaitAsync( cancellationToken ).ConfigureAwait( false );
            }
            timeoutOrCompletedEx = null;
            try
            {
                // Second, writes the current buffer.
                var startWrite = _flushedWriter != writer
                                    ? 0
                                    : _flushedWriterPos;
                if( startWrite != _buffer.Position )
                {
                    try
                    {
                        writer.Write( _buffer.WrittenSpan.Slice( startWrite ) );
                    }
                    catch( InvalidOperationException ex )
                    {
                        // This should only happen on a completed pipe.
                        _behavior.OnSwallowedException( "The inner PipeWriter should have been completed.", ex );
                        if( IsCompleted ) return new FlushResult( false, true );
                        // Same as the handling of a Completed result below except that
                        // the InvalidOperationException has the inner exception.
                        switch( _behavior.OnInnerCompleted() )
                        {
                            case OnInnerCompletedAction.Retry:
                                Close( false );
                                goto retry;
                            case OnInnerCompletedAction.Throw:
                                timeoutOrCompletedEx = new InvalidOperationException( "The inner PipeWriter should have been completed outside of the StablePipeWriter control.", ex );
                                break;
                            default:
                                _buffer.Clear( false );
                                Close( true );
                                return new FlushResult( false, true );
                        }
                    }
                    _flushedWriter = writer;
                    _flushedWriterPos = _buffer.Position;
                }
                if( timeoutOrCompletedEx == null )
                {
                    // Then calls DoFlushAsync with the provided token xor our timeout token when no retry must be done
                    // or with the token + timeout if we have a retry count.
                    int retryCount = 0;
                    int timeout = _defaultTimeout;
                    if( timeout != 0 ) retryCount = _retryWriteCount;
#pragma warning disable CA2012 // Use ValueTasks correctly: this is correct.
                    var (rr, timeEx) = await (retryCount == 0
                                                ? FlushWithoutRetryAsync( writer, timeout, cancellationToken )
                                                : FlushWithRetryAsync( writer, timeout, retryCount, cancellationToken )).ConfigureAwait( false );
#pragma warning restore CA2012 
                    if( (timeoutOrCompletedEx = timeEx) == null )
                    {
                        if( rr is null ) goto retry;
                        if( rr.Value.IsCompleted )
                        {
                            if( IsCompleted ) return new FlushResult( false, true );
                            // The inner writer has been completed and we are not completed.
                            // By default we complete this StablePipeWriter.
                            switch( _behavior.OnInnerCompleted() )
                            {
                                case OnInnerCompletedAction.Retry:
                                    Close( false );
                                    goto retry;
                                case OnInnerCompletedAction.Throw:
                                    timeoutOrCompletedEx = new InvalidOperationException( "The inner PipeWriter has been completed outside of the StablePipeWriter control." );
                                    break;
                                default:
                                    Close( true );
                                    return new FlushResult( false, true );
                            }
                        }
                        if( timeoutOrCompletedEx == null )
                        {
                            // IsCanceled: to follow the PipeWriter IsCanceled way of working,
                            // we keep the written data: if the caller retries to FlushAsync
                            // on canceled, he doesn't have to write the data again.
                            if( !rr.Value.IsCanceled )
                            {
                                OnDataWritten?.Invoke( _buffer.WrittenSpan, this );
                                DiscardBuffer();
                            }
                            return rr.Value;
                        }
                    }
                }
                DiscardBuffer();
            }
            catch( OperationCanceledException ex )
            {
                // If the operation has been canceled (it's not a communication timeout),
                // we must always let the exception bubble up.
                // We warn the controller if we have not been closed or another inner writer has been set.
                if( _current == writer )
                {
                    _behavior.OnOperationCanceled( ex );
                }
                DiscardBuffer();
                throw;
            }
            catch( Exception ex )
            {
                // If we have been closed or another inner writer has been set after the StartRead then we
                // silently swallow any exception and wait for reader availability or completion.
                if( _current != writer )
                {
                    _behavior.OnSwallowedException( "The PipeWriter for FlushAsync is no more the current one.", ex );
                    goto retry;
                }
                switch( _behavior.OnError( ex ) )
                {
                    case OnErrorAction.Retry: goto retry;
                    case OnErrorAction.Cancel:
                        DiscardBuffer();
                        return new FlushResult( true, IsCompleted );
                }
                DiscardBuffer();
                throw;
            }
            throw timeoutOrCompletedEx;
        }
        finally
        {
            // Same as StablePipeReader.
            writing = Interlocked.Exchange( ref _writing, 0 );
            if( writing == -1 )
            {
                Throw.DebugAssert( _waiter == null );
                _ctsTimeout?.Dispose();
                _ctsTimeout = null;
            }
        }
    }

    void DiscardBuffer()
    {
        _buffer.Clear( false );
        _flushedWriterPos = 0;
    }

    async ValueTask<(FlushResult?, TimeoutException?)> FlushWithoutRetryAsync( PipeWriter writer,
                                                                               int timeout,
                                                                               CancellationToken cancellationToken )
    {
        FlushResult result;
        // If there is a really cancelable token, then we use it.
        // But if it's a non cancelable one, then we try to use our non 0 DefaultTimeout.
        if( cancellationToken.CanBeCanceled || timeout == 0 )
        {
            result = await writer.FlushAsync( cancellationToken ).ConfigureAwait( false );
        }
        else
        {
            if( _ctsTimeout == null ) _ctsTimeout = new CancellationTokenSource( timeout );
            else _ctsTimeout.CancelAfter( timeout );
            try
            {
                result = await writer.FlushAsync( _ctsTimeout.Token ).ConfigureAwait( false );
                if( !_ctsTimeout.TryReset() ) _ctsTimeout = null;
            }
            catch( OperationCanceledException ex ) when( ex.CancellationToken == _ctsTimeout!.Token )
            {
                _ctsTimeout = null;
                var timeoutEx = new TimeoutException( $"StablePipeWriter timeout ({timeout} ms).", ex );
                switch( _behavior.OnError( timeoutEx ) )
                {
                    case OnErrorAction.Retry:
                        return (null, null);
                    case OnErrorAction.Cancel:
                        return (new FlushResult( true, false ), null);
                    default:
                        return (null, timeoutEx);
                }
            }
        }
        // If the inner writer has a canceled result and is not completed, we
        // return it or retry if the behavior prevents inner canceled result.
        // Handling result.IsCompleted is done above.
        if( result.IsCanceled
            && !result.IsCompleted
            && !_behavior.ReturnInnerCanceledResult() )
        {
            return (null, null);
        }
        return (result, null);
    }

    async ValueTask<(FlushResult?, TimeoutException?)> FlushWithRetryAsync( PipeWriter writer,
                                                                            int timeout,
                                                                            int retryCount,
                                                                            CancellationToken cancellationToken )
    {
        Throw.DebugAssert( timeout > 0 && retryCount > 0 );
        FlushResult result;

        int rCount = retryCount;
        retry:
        CancellationTokenRegistration reg = default;
        if( _ctsTimeout == null ) _ctsTimeout = new CancellationTokenSource( timeout );
        else _ctsTimeout.CancelAfter( timeout );
        if( cancellationToken.CanBeCanceled )
        {
            reg = cancellationToken.UnsafeRegister( _ => _ctsTimeout.Cancel(), null );
        }
        try
        {
            result = await writer.FlushAsync( _ctsTimeout.Token ).ConfigureAwait( false );
            ReleaseTimeout( reg, ref _ctsTimeout );
        }
        catch( OperationCanceledException ex )
        {
            ReleaseTimeout( reg, ref _ctsTimeout! );
            // External cancellation: throw.
            if( cancellationToken.IsCancellationRequested ) throw;
            if( --rCount == 0 )
            {
                goto retry;
            }
            var timeoutEx = new TimeoutException( $"StablePipeWriter timeout ({timeout} ms, RetryCount: {retryCount} ).", ex );
            switch( _behavior.OnError( timeoutEx ) )
            {
                case OnErrorAction.Retry:
                    return (null, null);
                case OnErrorAction.Cancel:
                    return (new FlushResult( true, false ), null);
                default:
                    return (null, timeoutEx);
            }
        }
        // If the inner writer has a canceled result and is not completed, we
        // return it or retry if the behavior prevents inner canceled result.
        // Handling result.IsCompleted is done above.
        if( result.IsCanceled
            && !result.IsCompleted
            && !_behavior.ReturnInnerCanceledResult() )
        {
            return (null, null);
        }
        return (result, null);
    }

    static void ReleaseTimeout( in CancellationTokenRegistration reg, [DisallowNull] ref CancellationTokenSource? ctsTimeout )
    {
        reg.Dispose();
        if( !ctsTimeout.TryReset() )
        {
            ctsTimeout.Dispose();
            ctsTimeout = null;
        }
    }

    /// <inheritdoc cref="ByteArrayWriter.Advance(int)"/>
    public override void Advance( int bytes ) => _buffer.Advance( bytes );

    /// <inheritdoc cref="ByteArrayWriter.GetMemory(int)"/>
    public override Memory<byte> GetMemory( int sizeHint = 0 ) => _buffer.GetMemory( sizeHint );

    /// <inheritdoc cref="ByteArrayWriter.GetSpan(int)"/>
    public override Span<byte> GetSpan( int sizeHint = 0 ) => _buffer.GetSpan( sizeHint );
}
