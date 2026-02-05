using CK.Core;
using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// Base class for message sending.
/// This is a Template Method Pattern that is basically a writer of <typeparamref name="T"/> into
/// the IBufferWriter&lt;byte&gt; of the <see cref="PipeWriter"/>.
/// </summary>
public abstract class MessageWriterBase<T>
{
    readonly PipeWriter _writer;
    readonly IStablePipeBehavior? _behavior;
    int _sendTimeout;
    int _writing;
    CancellationTokenSource? _ctsTimeout;
    ArrayBufferWriter<byte>? _logArrayBuffer; // Keep it to avoid reallocations when LogGate is open
    StringBuilder? _logStringBuilder; // Keep it to avoid reallocations when LogGate is open
    SemaphoreSlim? _lock;
    bool _isCompleted;
    long _lastSentMessageTick;


    /// <summary>
    /// Gets the last <see cref="Environment.TickCount64"/> at which the last message has been sent.
    /// </summary>
    public long LastSentMessageTick => _lastSentMessageTick;

    /// <summary>
    /// The tag to use in logs when <see cref="CommChannelMessageLogGates.MessageWriterLogGate"/>
    /// is Open.
    /// Defaults to the tag "MessageWriter".
    /// </summary>
    public CKTrait LogTag { get; set; }

    /// <summary>
    /// Static log gate for read buffers,
    /// with the display name "CK.CommChannel.MessageWriter".
    /// When Open, logs all written messages to the StaticLogger,
    /// as Debug log lines with <see cref="LogTag"/>.
    /// Closed by default.
    /// </summary>
    [Obsolete( "Use CommChannelMessageLogGates.MessageWriterLogGate instead." )]
    // ReSharper disable once StaticMemberInGenericType
    public static StaticGate LogGate => CommChannelMessageLogGates.MessageWriterLogGate;

    /// <summary>
    /// Initializes a new <see cref="MessageWriterBase{T}"/>.
    /// </summary>
    /// <param name="writer">
    /// The pipe writer. Can be a <see cref="StablePipeWriter"/> (and in such case, its <see cref="StablePipeReader.PipeBehavior"/>
    /// is automatically used if <paramref name="behavior"/> is null.
    /// </param>
    /// <param name="behavior">
    /// Optional stable pipe behavior that will handle error (see <see cref="IStablePipeBehavior.OnError(Exception)"/>.
    /// </param>
    protected MessageWriterBase( PipeWriter writer, IStablePipeBehavior? behavior )
    {
        Throw.CheckNotNullArgument( writer );
        LogTag = ActivityMonitor.Tags.Register( "MessageWriter" );
        _writer = writer;
        if( (_behavior = behavior) == null && writer is StablePipeWriter p )
        {
            _behavior = p.PipeBehavior;
        }
    }

    /// <summary>
    /// Initializes a new <see cref="MessageWriterBase{T}"/> on a <see cref="StablePipeWriter"/>.
    /// The <see cref="StablePipeWriter.PipeBehavior"/> is used.
    /// </summary>
    /// <param name="writer">The stable pipe reader.</param>
    protected MessageWriterBase( StablePipeWriter writer )
        : this( writer, writer.PipeBehavior )
    {
    }

    /// <summary>
    /// Gets or sets the default send timeout of a message in milliseconds.
    /// <para>
    /// This must be -1 or a positive value.
    /// </para>
    /// <para>
    /// Defaults to -1: there is no send timeout by default.
    /// </para>
    /// </summary>
    public int DefaultMessageSendTimeout
    {
        get => _sendTimeout == 0 ? -1 : _sendTimeout;
        set
        {
            Throw.CheckArgument( value == -1 || value > 0 );
            _sendTimeout = value == -1 ? 0 : value;
        }
    }

    /// <summary>
    /// Gets whether this writer is closed.
    /// <see cref="WriteAsync(T, CancellationToken, int)"/> will always return false.
    /// </summary>
    public bool IsCompleted => _isCompleted;

    /// <summary>
    /// Gets or sets whether this writer allows concurrent write.
    /// When set, an async lock is used to serialize the calls to <see cref="WriteAsync(T, CancellationToken, int)"/>.
    /// By default, concurrent use of this writer throws an <see cref="InvalidOperationException"/>.
    /// <para>
    /// When multiple writers compete, the timeout is applied while waiting: the timeout or cancellation
    /// token is always honored.
    /// </para>
    /// </summary>
    public bool MultipleWriters
    {
        get => _lock != null;
        set
        {
            // No need to call Dispose since we don't use the WaitHandle.
            if( value )
            {
                // Sets a new lock only if it is currently null.
                Interlocked.CompareExchange( ref _lock, new SemaphoreSlim( 1, 1 ), null );
            }
            else
            {
                Interlocked.Exchange( ref _lock, null );
            }
        }
    }


    /// <summary>
    /// Writes a message.
    /// </summary>
    /// <remarks>
    /// Once the message has been written to the inner pipe, there's no way to know if it has been "sent" or not.
    /// </remarks>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="timeout">
    /// By default, <see cref="DefaultMessageSendTimeout"/> is used.
    /// Use a negative value to disable any timeout or a positive value in milliseconds for an explicit timeout.
    /// </param>
    /// <returns>False if <see cref="IsCompleted"/> is true, true otherwise. See remarks.</returns>
    public async ValueTask<bool> WriteAsync( T message, CancellationToken cancellationToken, int timeout = 0 )
    {
        if( IsCompleted ) return false;
        SemaphoreSlim? alock = _lock;

        if( Interlocked.CompareExchange( ref _writing, 1, 0 ) == 1 && alock == null )
        {
            Throw.InvalidOperationException( "A WriteAsync is already executing." );
        }

        try
        {
            TimeoutMessageException? timeoutEx = null;
            ExceptionDispatchInfo? toThrow = null;

            bool firstTry = true;
            retry:
            Throw.DebugAssert( "They are here to be thrown.", timeoutEx == null && toThrow == null );

            if( timeout == 0 ) timeout = _sendTimeout;
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

            retryWithCurrentTimeout:
            try
            {
                if( firstTry )
                {
                    if( alock != null ) await alock.WaitAsync( t ).ConfigureAwait( false );
                    if( CommChannelMessageLogGates.MessageWriterLogGate.IsOpen ) WriteMessageToStaticLogger( message );
                    // If writing the message throws, this is not a communication error.
                    WriteMessage( message, _writer );
                    firstTry = false;
                }

                FlushResult result = await _writer.FlushAsync( t ).ConfigureAwait( false );
                _lastSentMessageTick = Environment.TickCount64;
                if( result.IsCompleted )
                {
                    _isCompleted = true;
                    if( _ctsTimeout != null ) DefinitelyReleaseTimeout( reg, ref _ctsTimeout );
                    return false;
                }

                return true;
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
                    timeoutEx = new TimeoutMessageException( $"{GetType().FullName} timeout.", ex );
                    switch( _behavior?.OnError( timeoutEx ) )
                    {
                        case OnErrorAction.Retry:
                            timeoutEx = null;
                            goto retry;
                        case OnErrorAction.Cancel:
                            return !_isCompleted;
                    }
                }
            }
            catch( Exception ex )
            {
                // If firstTry is false, WriteMessage failed: we throw.
                if( !firstTry )
                {
                    switch( _behavior?.OnError( ex ) )
                    {
                        case OnErrorAction.Retry: goto retryWithCurrentTimeout;
                        case OnErrorAction.Cancel: return !_isCompleted;
                    }
                }

                throw;
            }
            finally
            {
                if( _ctsTimeout != null ) ReleaseTimeout( reg, ref _ctsTimeout );
                if( alock != null ) alock.Release();
            }

            toThrow?.Throw();
            Throw.DebugAssert( timeoutEx != null );
            throw timeoutEx;
        }
        finally
        {
            Interlocked.Exchange( ref _writing, 0 );
        }
    }

    void WriteMessageToStaticLogger( T message )
    {
        if( _logArrayBuffer is null )
        {
            _logArrayBuffer = new ArrayBufferWriter<byte>();
        }
        else
        {
            _logArrayBuffer.Clear();
        }

        if( _logStringBuilder is null )
        {
            _logStringBuilder = new StringBuilder();
        }
        else
        {
            _logStringBuilder.Clear();
        }

        WriteMessage( message, _logArrayBuffer );

        _logStringBuilder.AppendBufferSpan( _logArrayBuffer.WrittenSpan );

        CommChannelMessageLogGates.MessageWriterLogGate.StaticLogger?.Debug( LogTag,
            $"Written message ({_logArrayBuffer.WrittenCount}b): {_logStringBuilder.ToString()}" );
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

    static void DefinitelyReleaseTimeout( in CancellationTokenRegistration reg,
        [DisallowNull] ref CancellationTokenSource? ctsTimeout )
    {
        reg.Dispose();
        ctsTimeout.Dispose();
        ctsTimeout = null;
    }

    /// <summary>
    /// Must write the message to the buffer.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="buffer">The buffer.</param>
    protected abstract void WriteMessage( in T message, IBufferWriter<byte> buffer );
}
