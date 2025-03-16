# Message handlers

While readers _pulls_ messages from a `PipeReader`, it is sometimes convenient to
have the messages _pushed_ to your code like events.

The abstract [`MessageHandler<T>`](MessageHandler.cs) where `T` is the type of the messages implements
this once for all thanks to a loop on a [MessageReader](../MessageReader/README.md) that can be started
or stopped easily:
```csharp
/// <summary>
/// Initializes a new message handler.
/// </summary>
/// <param name="reader">The message reader to use.</param>
protected MessageHandler( MessageReaderBase<T> reader ) { ... }

/// <summary>
/// Starts this message handler.
/// </summary>
/// <param name="maxNumber">The maximum number of messages to read. Defaults to 0: the loop will continue to handle messages.</param>
/// <returns>True if this actually started the loop, false otherwise.</returns>
public bool StartReadLoop( int maxMessage = 0 ) { ... }

/// <summary>
/// Gets a task that is resolved each time this handler's loop is stopped
/// with the reason of the stop.
/// </summary>
public Task<MessageHandlerCompletionReason> StoppedReason { get; }

/// <summary>
/// Sends a signal to stop this message handler.
/// When this returns true, <see cref="StoppedReason"/> can be awaited to wait for the
/// end of the loop.
/// </summary>
/// <returns>True if this actually stopped the loop, false otherwise.</returns>
public bool StopReadLoop() { ... }
```
## Message handling
The heart of the message processing is done by implementing:
```csharp
/// <summary>
/// Called for each message.
/// When returning false, the read loop is terminated.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="cancel">The cancellation token driven by the <see cref="MessageHandlingTimeout"/>.</param>
/// <returns>True to continue the read loop. False to leave it.</returns>
protected abstract ValueTask<bool> HandleMessageAsync( T message, CancellationToken cancel );
```
Since handling a message _may_ take a too long time, an optional timeout, specific to this handling, is available:
```csharp
/// <summary>
/// Gets or sets the timeout of a message handling by <see cref="HandleMessageAsync(T, CancellationToken)"/>.
/// <para>
/// This must be -1 or a positive value.
/// </para>
/// </summary>
public int MessageHandlingTimeout { get; set; }
```
By default, the reader's `EmptyMessage` (that is the result of a `CancelPendingRead()` call or a completion
of the reader) is silently ignored. This can be changed thanks to:
```csharp
/// <summary>
/// Gets or sets whether <see cref="MessageReaderBase{T}.EmptyMessage"/> must be
/// submitted to <see cref="HandleMessageAsync(T, CancellationToken)"/>.
/// Defaults to false.
/// </summary>
public bool HandleCancelMessages { get; set; }
```


## Read timeout for a continuous loop?
When running, the loop continuously calls `MessageReaderBase<T>.ReadNextAsync()`, waiting for a
message to be available. What timeout should the loop use for this call?
Except in the edge case of a continuous stream of messages, most of the time a message will
be available in response of a request, or because a recurring process is at work somewhere in the
system (and often remotely).

The fact is that the loop must be able to adjust its timeout at any time. `MessageHandler<T>`
exposes the following API to support this capability:
```csharp
/// <summary>
/// Gets or sets the timeout in milliseconds used 
/// by <see cref="SetReadTimeout(int)"/> when no explicit timeout is used,
/// or before starting to read a new message if <see cref="AutoApplyTimeout"/> is true.
/// </summary>
public int DefaultReadTimeout { get; set; }

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
/// Sets (or suspends when <paramref name="timeout"/> is -1) the read timeout
/// in milliseconds for the currently incoming message.
/// </summary>
/// <param name="timeout">
/// The timeout duration that applies from now on.
/// By default, <see cref="DefaultReadTimeout"/> is used.
/// </param>
public void SetReadTimeout( int timeout = 0 ) { ... }

/// <summary>
/// Deactivates the read timeout (same as calling <see cref="SetReadTimeout(int)"/> with -1).
/// This is systematically called after a message is received and before its handling.
/// </summary>
public void SuspendReadTimeout() { ... }
```

Thanks to this, the `MessageHandler<T>` behavior can be dynamically adjusted ay any time
based on external conditions, actions or events.

## Extension points
The following extension points can be used to tune the `MessageHandler<T>` behavior.
```csharp
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
```
