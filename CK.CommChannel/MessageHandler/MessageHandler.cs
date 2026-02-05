using CK.Core;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// A message handler adapts the pull model of a <see cref="MessageReaderBase{T}"/> into
/// a push model by calling <see cref="HandleMessageAsync(T, CancellationToken)"/> in a loop.
/// <para>
/// Read timeout can be suspended or set at any time by any thread.
/// </para>
/// </summary>
/// <typeparam name="T">The type of the message.</typeparam>
public abstract class MessageHandler<T>
{
    readonly MessageReaderBase<T> _reader;
    Task<MessageHandlerCompletionReason> _stoppedReason;
    CancellationTokenSource? _cts;
    CancellationTokenSource? _ctsHandling;
    readonly object _lockCTS;
    int _handlingTimeout;
    int _defaultTimeout;
    int _running;
    int _maxMessage;
    int _activeTimeout;

    /// <summary>
    /// The tag to use in logs when <see cref="CommChannelMessageLogGates.MessageReaderLogGate"/>
    /// is Open on the embedded MessageReader.
    /// Defaults to the tag "MessageReader".
    /// </summary>
    /// <remarks>
    /// Forwarded to <see cref="MessageReaderBase{T}.LogTag"/>.
    /// </remarks>
    public CKTrait LogTag {
        get => _reader.LogTag;
        set => _reader.LogTag = value;
    }

    /// <summary>
    /// Initializes a new message handler.
    /// </summary>
    /// <param name="reader">The message reader to use.</param>
    protected MessageHandler( MessageReaderBase<T> reader )
    {
        Throw.CheckNotNullArgument( reader );
        _reader = reader;
        _lockCTS = new object();
        _stoppedReason = Task.FromResult( MessageHandlerCompletionReason.None );
        AutoApplyTimeout = true;
    }

    /// <summary>
    /// Gets or sets whether <see cref="MessageReaderBase{T}.EmptyMessage"/> must be
    /// submitted to <see cref="HandleMessageAsync(T, CancellationToken)"/>.
    /// Defaults to false.
    /// </summary>
    public bool HandleCancelMessages { get; set; }

    /// <summary>
    /// Gets or sets the timeout of a message handling by <see cref="HandleMessageAsync(T, CancellationToken)"/>.
    /// <para>
    /// This must be -1 or a positive value.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no timeout by default on the message handling step.
    /// </para>
    /// </summary>
    public int MessageHandlingTimeout
    {
        get => _handlingTimeout == 0 ? -1 : _handlingTimeout;
        set
        {
            Throw.CheckArgument( value == -1 || value > 0 );
            _handlingTimeout = value == -1 ? 0 : value;
        }
    }

    /// <summary>
    /// Gets or sets the timeout in milliseconds used
    /// by <see cref="SetReadTimeout(int)"/> when no explicit timeout is used,
    /// or before starting to read a new message if <see cref="AutoApplyTimeout"/> is true.
    /// <para>
    /// This must be -1 or a positive value.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no read timeout by default.
    /// </para>
    /// </summary>
    public int DefaultReadTimeout
    {
        get => _defaultTimeout == 0 ? -1 : _defaultTimeout;
        set
        {
            Throw.CheckArgument( value == -1 || value > 0 );
            _defaultTimeout = value;
        }
    }

    /// <summary>
    /// Gets or sets whether <see cref="DefaultReadTimeout"/> should automatically be applied
    /// for each new message.
    /// Defaults to true.
    /// <para>
    /// When set to false, since <see cref="SuspendReadTimeout"/> is systematically called after
    /// each read, <see cref="SetReadTimeout(int)"/> must be called each time to reactivate the timeout.
    /// </para>
    /// </summary>
    public bool AutoApplyTimeout { get; set; }

    /// <summary>
    /// Gets the bounded reader.
    /// <para>
    /// This is not exposed publicly and should be used only to get or set default timeout.
    /// There is no real reason to interfere with it otherwise.
    /// </para>
    /// </summary>
    protected MessageReaderBase<T> Reader => _reader;

    /// <summary>
    /// Sets (or suspends when <paramref name="timeout"/> is -1) the read timeout
    /// in milliseconds for the currently incoming message.
    /// </summary>
    /// <param name="timeout">
    /// The timeout duration that applies from now on.
    /// By default, <see cref="DefaultReadTimeout"/> is used.
    /// </param>
    public void SetReadTimeout( int timeout = 0 )
    {
        if( timeout == 0 ) timeout = _defaultTimeout;
        if( timeout <= 0 )
        {
            SuspendReadTimeout();
        }
        else
        {
            lock( _lockCTS )
            {
                _activeTimeout = timeout;
                if( _cts != null ) SetRunningTimeout();
            }
        }
    }

    /// <summary>
    /// Deactivates the read timeout (same as calling <see cref="SetReadTimeout(int)"/> with -1).
    /// This is systematically called after a message is received and before its handling.
    /// </summary>
    public void SuspendReadTimeout()
    {
        lock( _lockCTS )
        {
            if( _activeTimeout > 0 )
            {
                _activeTimeout = 0;
                if( _cts != null && !_cts.TryReset() )
                {
                    // Too late: this will apply to the next read.
                    _cts.Dispose();
                    _cts = new CancellationTokenSource();
                }
            }
        }
    }

    void SetRunningTimeout()
    {
        Throw.DebugAssert( _cts != null && Monitor.IsEntered( _lockCTS ) && _activeTimeout > 0 );
        _cts.CancelAfter( _activeTimeout );
        // Too late: this will apply to the next read.
        if( _cts.IsCancellationRequested )
        {
            _cts.Dispose();
            _cts = new CancellationTokenSource( _activeTimeout );
        }
    }

    /// <summary>
    /// Starts this message handler.
    /// </summary>
    /// <param name="maxMessage">The maximum number of messages to read. Defaults to 0: the loop will continue to handle messages.</param>
    /// <returns>True if this actually started the loop, false otherwise.</returns>
    public bool StartReadLoop( int maxMessage = 0 )
    {
        if( Interlocked.CompareExchange( ref _running, 1, 0 ) == 0 )
        {
            _maxMessage = maxMessage;
            _stoppedReason = Task.Run( LoopAsync );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets a task that is resolved each time this handler's loop is stopped
    /// with the reason of the stop.
    /// </summary>
    public Task<MessageHandlerCompletionReason> StoppedReason => _stoppedReason;

    /// <summary>
    /// Sends a signal to stop this message handler.
    /// When this returns true, <see cref="StoppedReason"/> can be awaited to wait for the
    /// end of the loop.
    /// </summary>
    /// <param name="cancelPendingRead">
    /// False to not call <see cref="MessageReaderBase{T}.CancelPendingRead"/>: the current read operation
    /// will wait for the next message or cancellation before the loop can terminate.
    /// </param>
    /// <returns>True if this actually stopped the loop, false otherwise.</returns>
    public bool StopReadLoop( bool cancelPendingRead = true )
    {
        if( Interlocked.CompareExchange( ref _running, 0, 1 ) == 1 )
        {
            if( cancelPendingRead ) _reader.CancelPendingRead();
            return true;
        }
        return false;
    }

    async Task<MessageHandlerCompletionReason> LoopAsync()
    {
        Throw.DebugAssert( _cts == null );
        // Ensures that _cts is available: we'll use its Token.
        lock( _lockCTS )
        {
            _cts = new CancellationTokenSource();
        }
        try
        {
            while( _running == 1 )
            {
                try
                {
                    lock( _lockCTS )
                    {
                        // If a timeout is already set, consider it.
                        int timeout;
                        if( _activeTimeout == 0 && AutoApplyTimeout && (timeout = _defaultTimeout) > 0 )
                        {
                            _activeTimeout = timeout;
                        }
                        if( _activeTimeout > 0 ) SetRunningTimeout();
                    }
                    var m = await _reader.ReadNextAsync( _cts.Token, -1 ).ConfigureAwait( false );
                    SuspendReadTimeout();
                    if( EqualityComparer<T>.Default.Equals( m, _reader.EmptyMessage ) )
                    {
                        if( _reader.IsCompleted )
                        {
                            return MessageHandlerCompletionReason.OnReaderComplete;
                        }
                        // If not running continue: this will end the loop.
                        if( !HandleCancelMessages || _running == 0 ) continue;
                    }
                    // Processing the message is done with or without a timeout.
                    int handlingTimeout = _handlingTimeout;
                    if( handlingTimeout <= 0 )
                    {
                        if( !await HandleMessageAsync( m, default ).ConfigureAwait( false ) )
                        {
                            return MessageHandlerCompletionReason.ProcessMessage;
                        }
                    }
                    else
                    {
                        if( _ctsHandling == null ) _ctsHandling = new CancellationTokenSource( handlingTimeout );
                        else _ctsHandling.CancelAfter( handlingTimeout );
                        try
                        {
                            if( !await HandleMessageAsync( m, _ctsHandling.Token ).ConfigureAwait( false ) )
                            {
                                return MessageHandlerCompletionReason.ProcessMessage;
                            }
                        }
                        catch( OperationCanceledException ex ) when( ex.CancellationToken == _ctsHandling.Token )
                        {
                            if( !await OnHandlingMessageTimeoutAsync( m, handlingTimeout ).ConfigureAwait( false ) )
                            {
                                return MessageHandlerCompletionReason.UnhandledError;
                            }
                        }
                        if( !_ctsHandling.TryReset() )
                        {
                            _ctsHandling.Dispose();
                            _ctsHandling = null;
                        }

                    }
                    if( _maxMessage > 0 && --_maxMessage == 0 )
                    {
                        return MessageHandlerCompletionReason.MaxMessageNumber;
                    }
                }
                // The OperationCanceledException may NOT be associated to our token (SerialPort case for instance).
                // We must also check that our cancellation has not been requested.
                catch( OperationCanceledException ex ) when( ex.CancellationToken == _cts.Token || (_cts != null && _cts.IsCancellationRequested) )
                {
                    SuspendReadTimeout();
                    if( !await OnReadTimeoutAsync().ConfigureAwait( false ) )
                    {
                        return MessageHandlerCompletionReason.ReadTimeout;
                    }
                }
                catch( Exception ex )
                {
                    SuspendReadTimeout();
                    if( !await OnUnhandledExceptionAsync( ExceptionDispatchInfo.Capture( ex ) ).ConfigureAwait( false ) )
                    {
                        return MessageHandlerCompletionReason.UnhandledError;
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange( ref _running, 0 );
            _ctsHandling?.Dispose();
            lock( _lockCTS )
            {
                _cts.Dispose();
                _cts = null;
            }
        }
        return MessageHandlerCompletionReason.StoppedLoop;
    }

    /// <summary>
    /// Called before at the start of the read loop.
    /// This does nothing at this level.
    /// </summary>
    /// <param name="maxNumber">The number of messages to read. Defaults to -1 (no limit).</param>
    /// <returns>The awaitable.</returns>
    protected virtual ValueTask OnReadLoopStartAsync( int maxNumber ) => default;

    /// <summary>
    /// Called for each message.
    /// When returning false, the read loop is terminated.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancel">The cancellation token driven by the <see cref="MessageHandlingTimeout"/>.</param>
    /// <returns>True to continue the read loop. False to leave it.</returns>
    protected abstract ValueTask<bool> HandleMessageAsync( T message, CancellationToken cancel );

    /// <summary>
    /// Called when <see cref="HandleMessageAsync(T, CancellationToken)"/> has been canceled because
    /// of <see cref="MessageHandlingTimeout"/>.
    /// When returning false, the read loop is terminated.
    /// This default implementation does nothing and returns true to continue the loop.
    /// </summary>
    /// <param name="message">The message that failed to be handled in time.</param>
    /// <param name="handlingTimeout">The <see cref="MessageHandlingTimeout"/> value.</param>
    /// <returns>True to continue the read loop. False to leave it.</returns>
    protected virtual ValueTask<bool> OnHandlingMessageTimeoutAsync( T message, int handlingTimeout ) => new ValueTask<bool>( true );

    /// <summary>
    /// Called when a read timeout occurred.
    /// When returning false, the read loop is terminated.
    /// This default implementation does nothing and returns true to continue the loop.
    /// </summary>
    /// <returns>True to continue the read loop. False to leave it.</returns>
    protected virtual ValueTask<bool> OnReadTimeoutAsync() => new ValueTask<bool>( true );

    /// <summary>
    /// Called when an unhandled exception occurred.
    /// <list type="bullet">
    ///     <item>
    ///     When returning true, the read loop continues. This is the default.
    ///     </item>
    ///     <item>
    ///     When returning false, the read loop is terminated with <see cref="MessageHandlerCompletionReason.UnhandledError"/>.
    ///     </item>
    ///     <item>
    ///     Calling <see cref="ExceptionDispatchInfo.Throw()"/> ends the loop with the exception: the <see cref="StoppedReason"/> is <see cref="TaskStatus.Faulted"/>.
    ///     </item>
    /// </list>
    /// When returning false, the read loop is terminated with <see cref="MessageHandlerCompletionReason.UnhandledError"/>.
    /// <para>
    /// If an OperationCanceledException is due to a read timeout, then <see cref="OnReadTimeoutAsync"/> is called.
    /// This MAY be called with an OperationCanceledException that is not due to a read timeout.
    /// </para>
    /// </summary>
    /// <returns>True to continue the loop, false to leave it.</returns>
    protected virtual ValueTask<bool> OnUnhandledExceptionAsync( ExceptionDispatchInfo ex ) => new ValueTask<bool>( true );

    /// <summary>
    /// Called at the end of the read loopAsync.
    /// This does nothing at this level.
    /// </summary>
    /// <param name="reason">The completion reason.</param>
    /// <returns>The awaitable.</returns>
    protected virtual ValueTask OnReadLoopStopAsync( MessageHandlerCompletionReason reason ) => default;

}
