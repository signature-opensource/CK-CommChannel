using CK.Core;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// Base class for message handling.
/// This is a Template Method Pattern where <see cref="TryParseMessage(ref ReadOnlySequence{byte}, out ReadOnlySequence{byte})"/>
/// has the responsibility to isolate the messages and <see cref="Convert(in ReadOnlySequence{byte})"/> creates the message.
/// </summary>
public abstract class MessageReaderBase<T>
{
    readonly PipeReader _reader;
    readonly IStablePipeBehavior? _behavior;
    int _receiveTimeout;
    int _reading;
    CancellationTokenSource? _ctsTimeout;
    StringBuilder? _logStringBuilder; // Keep it to avoid reallocations when LogGate is open
    bool _isCompleted;

    /// <summary>
    /// The tag to use in logs when <see cref="CommChannelMessageLogGates.MessageReaderLogGate"/>
    /// is Open.
    /// Defaults to the tag "MessageReader".
    /// </summary>
    public CKTrait LogTag { get; set; }

    /// <summary>
    /// Static log gate for read buffers,
    /// with the display name "CK.CommChannel.MessageReader".
    /// When Open, logs all read buffers to the StaticLogger,
    /// as Debug log lines with <see cref="LogTag"/>.
    /// Closed by default.
    /// </summary>
    [Obsolete( "Use CommChannelMessageLogGates.MessageReaderLogGate instead." )]
    // ReSharper disable once StaticMemberInGenericType
    public static StaticGate LogGate => CommChannelMessageLogGates.MessageReaderLogGate;

    /// <summary>
    /// Initializes a new <see cref="MessageReaderBase{T}"/>.
    /// </summary>
    /// <param name="reader">
    /// The pipe reader. Can be a <see cref="StablePipeReader"/> (and in such case, its <see cref="StablePipeReader.PipeBehavior"/>
    /// is automatically used if <paramref name="behavior"/> is null.
    /// </param>
    /// <param name="cancelMessage">
    /// The message returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </param>
    /// <param name="behavior">Optional behavior.</param>
    protected MessageReaderBase( PipeReader reader, T cancelMessage, IStablePipeBehavior? behavior )
    {
        Throw.CheckNotNullArgument( reader );
        LogTag = ActivityMonitor.Tags.Register( "MessageReader" );
        EmptyMessage = cancelMessage;
        _reader = reader;
        if( (_behavior = behavior) == null && reader is StablePipeReader p )
        {
            _behavior = p.PipeBehavior;
        }
    }

    /// <summary>
    /// Initializes a new <see cref="MessageReaderBase{T}"/> on a <see cref="StablePipeReader"/>.
    /// The <see cref="StablePipeReader.PipeBehavior"/> is used.
    /// </summary>
    /// <param name="reader">The managed pipe reader.</param>
    /// <param name="emptyMessage">
    /// The message returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </param>
    protected MessageReaderBase( StablePipeReader reader, T emptyMessage )
        : this( reader, emptyMessage, reader.PipeBehavior )
    {
    }

    /// <summary>
    /// Gets or sets the default read timeout of a message in milliseconds.
    /// <para>
    /// This must be -1 or a positive value.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no read timeout by default.
    /// </para>
    /// </summary>
    public int DefaultMessageReceiveTimeout
    {
        get => _receiveTimeout == 0 ? -1 : _receiveTimeout;
        set
        {
            Throw.CheckArgument( value == -1 || value > 0 );
            _receiveTimeout = value == -1 ? 0 : value;
        }
    }

    /// <summary>
    /// Gets whether the end of the data stream has been reached.
    /// ReadNext methods will always return the <see cref="EmptyMessage"/>.
    /// </summary>
    public bool IsCompleted => _isCompleted;


    void SetCompleted()
    {
        _isCompleted = true;
        _ctsTimeout?.Dispose();
    }

    /// <summary>
    /// Gets the cancel message that is returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </summary>
    public T EmptyMessage { get; }

    /// <summary>
    /// Cancels the pending ReadNextAsync operation without causing it to throw and without
    /// completing.
    /// If there is no pending operation, this cancels the next operation.
    /// </summary>
    /// <remarks>The canceled operation returns the <see cref="EmptyMessage"/>.</remarks>
    public void CancelPendingRead()
    {
        if( !IsCompleted && Interlocked.CompareExchange( ref _reading, 2, 0 ) == 1 )
        {
            _reader.CancelPendingRead();
        }
    }

    /// <summary>
    /// Reads the next message.
    /// On timeout, a <see cref="TimeoutMessageException"/> is thrown.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeout">
    /// By default, <see cref="DefaultMessageReceiveTimeout"/> is used.
    /// Use a negative value to disable any read timeout or a positive value in milliseconds for an explicit timeout.
    /// </param>
    /// <param name="messageFilter">
    /// Optional message filter that can ignore messages while keeping the timeout unchanged: a message that satisfies the filter
    /// must be received before the timeout or a TimeoutMessageException is thrown.
    /// </param>
    /// <returns>The message read. Can be empty.</returns>
    public async ValueTask<T> ReadNextAsync( CancellationToken cancellationToken, int timeout = 0,
        Func<T, bool>? messageFilter = null )
    {
        // When IsCompleted is true or CancelPendingRead has been called, we may kindly handle concurrent
        // calls without throwing the InvalidOperationException here. This is acceptable.
        if( IsCompleted ) return EmptyMessage;
        int rState;
        if( (rState = Interlocked.CompareExchange( ref _reading, 1, 0 )) == 1 )
        {
            Throw.InvalidOperationException( "A ReadNextAsync is already executing." );
        }

        try
        {
            if( rState == 2 ) return EmptyMessage;
            TimeoutMessageException? timeoutEx = null;
            ExceptionDispatchInfo? toThrow = null;

            retry:
            Throw.DebugAssert( "They are here to be thrown.", timeoutEx == null && toThrow == null );

            if( timeout == 0 ) timeout = _receiveTimeout;
            CancellationToken t = cancellationToken;
            CancellationTokenRegistration reg = default;
            if( timeout > 0 )
            {
                if( _ctsTimeout == null ) _ctsTimeout = new CancellationTokenSource( timeout );
                else _ctsTimeout.CancelAfter( timeout );
                if( cancellationToken.CanBeCanceled )
                {
                    reg = cancellationToken.UnsafeRegister( _ => _ctsTimeout.Cancel(), null );
                }

                t = _ctsTimeout.Token;
            }

            try
            {
                for( ;; )
                {
                    var result = await _reader.ReadAsync( t ).ConfigureAwait( false );
                    ReadOnlySequence<byte> buffer = result.Buffer;
                    CommChannelMessageLogGates.MessageReaderLogGate.StaticLogger?.Debug( LogTag,
                        $"Read buffer ({buffer.Length}b): {DumpBufferToString( buffer )}" );
                    if( TryParseMessage( ref buffer, out var message ) )
                    {
                        T m = Convert( message );
                        _reader.AdvanceTo( buffer.Start );
                        if( result.IsCompleted )
                        {
                            if( _ctsTimeout != null ) ReleaseTimeout( reg, ref _ctsTimeout );
                            SetCompleted();
                            return messageFilter != null && messageFilter( m ) ? m : EmptyMessage;
                        }

                        if( messageFilter != null && !messageFilter( m ) ) continue;
                        if( _ctsTimeout != null ) ReleaseTimeout( reg, ref _ctsTimeout );
                        return m;
                    }

                    _reader.AdvanceTo( buffer.Start, buffer.End );
                    if( result.IsCompleted )
                    {
                        SetCompleted();
                        return EmptyMessage;
                    }

                    if( result.IsCanceled )
                    {
                        if( _ctsTimeout != null ) ReleaseTimeout( reg, ref _ctsTimeout );
                        return EmptyMessage;
                    }
                }
            }
            catch( OperationCanceledException ex )
            {
                bool hasTimedout = false;
                if( _ctsTimeout != null )
                {
                    hasTimedout = _ctsTimeout.IsCancellationRequested;
                    ReleaseTimeout( reg, ref _ctsTimeout );
                }

                if( cancellationToken.IsCancellationRequested || !hasTimedout )
                {
                    toThrow = ExceptionDispatchInfo.Capture( ex );
                }
                else
                {
                    timeoutEx = new TimeoutMessageException( $"{GetType().FullName} timeout ({timeout} ms).", ex );
                    switch( _behavior?.OnError( timeoutEx ) )
                    {
                        case OnErrorAction.Retry:
                            timeoutEx = null;
                            goto retry;
                        case OnErrorAction.Cancel:
                            return EmptyMessage;
                    }
                }
            }
            catch( Exception ex )
            {
                if( _ctsTimeout != null ) ReleaseTimeout( reg, ref _ctsTimeout );
                switch( _behavior?.OnError( ex ) )
                {
                    case OnErrorAction.Retry: goto retry;
                    case OnErrorAction.Cancel: return EmptyMessage;
                }

                throw;
            }

            if( toThrow != null ) toThrow.Throw();
            Throw.DebugAssert( timeoutEx != null );
            throw timeoutEx;
        }
        finally
        {
            Interlocked.Exchange( ref _reading, 0 );
        }

        static void ReleaseTimeout( in CancellationTokenRegistration reg,
            [DisallowNull] ref CancellationTokenSource? ctsTimeout )
        {
            reg.Dispose();
            if( !ctsTimeout.TryReset() )
            {
                ctsTimeout.Dispose();
                ctsTimeout = null;
            }
        }
    }

    string DumpBufferToString( ReadOnlySequence<byte> sequence )
    {
        if( _logStringBuilder == null )
        {
            _logStringBuilder = new StringBuilder();
        }
        else
        {
            _logStringBuilder.Clear();
        }

        _logStringBuilder.AppendBufferSequence( sequence );
        return _logStringBuilder.ToString();
    }

    /// <summary>
    /// Reads the next message.
    /// </summary>
    /// <param name="timeout">
    /// By default, <see cref="DefaultMessageReceiveTimeout"/> is used.
    /// Use a negative value to disable any read timeout, a positive value in milliseconds for an explicit timeout.
    /// </param>
    /// <param name="messageFilter">
    /// Optional message filter that can ignore messages while keeping the timeout unchanged: a message that satisfies the filter
    /// must be received before the timeout or a TimeoutMessageException is thrown.
    /// </param>
    /// <returns>The message read. Can be empty.</returns>
    public ValueTask<T> ReadNextAsync( int timeout = 0, Func<T, bool>? messageFilter = null ) =>
        ReadNextAsync( default, timeout );

    /// <summary>
    /// Isolates a message.
    /// </summary>
    /// <param name="buffer">The buffer that must be sliced as much as possible.</param>
    /// <param name="message">The resulting message. Can be empty.</param>
    /// <returns>True if something has been parsed (and the buffer has been sliced), false otherwise.</returns>
    protected abstract bool TryParseMessage( ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message );

    /// <summary>
    /// Converts an isolated sequence of bytes to <typeparamref name="T"/>.
    /// </summary>
    /// <param name="message">The message bytes.</param>
    /// <returns>The message.</returns>
    protected abstract T Convert( in ReadOnlySequence<byte> message );
}
