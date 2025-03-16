# Message writers

A message writer is plugged on a `PipeWriter` and writes messages into it.

Like [MessageReaders](../MessageReader/README.md), this is a Template Method pattern: the
abstract [`MessageReaderBase<T>`](MessageWriterBase.cs) where `T` is the type of the messages
implements timeout management and exceptions can be handled
by [`IStablePipeBehavior`](../StablePipe/IStablePipeBehavior.cs) if the writer is a [`StablePipeWriter`](../StablePipe/StablePipeWriter.cs).

> *Note:* As opposed to readers, a message writer can be used concurrently when `MultipleWriters` is set to true.

A message writer has a rather simple API:
```csharp
/// <summary>
/// Gets or sets the default send timeout of a message in milliseconds.
/// <para>
/// This must be -1 or a positive value.
/// </para>
/// </summary>
public int DefaultMessageSendTimeout { get; set; }

/// <summary>
/// Gets whether this writer is closed.
/// <see cref="WriteAsync(T, CancellationToken, int)"/> will always return false.
/// </summary>
public bool IsCompleted { get; }

/// <summary>
/// Gets or sets whether this pipe allows multiple writers.
/// When set, an async lock is used to serialize the calls to <see cref="WriteAsync(T, CancellationToken, int)"/>.
/// By default, concurrent use of this writer throws an <see cref="InvalidOperationException"/>.
/// </summary>
public bool MultipleWriters { get; set; }

/// <summary>
/// Writes a message.
/// </summary>
/// <remarks>
/// Once the message has been written to the inner pipe, there's no way to know if it has been "sent" or not.
/// </remarks>
/// <param name="cancellationToken">Cancellation token.</param>
/// <param name="timeout">
/// By default, <see cref="DefaultMessageSendTimeout"/> is used.
/// Use a negative value to disable any timeout or a positive value in milliseconds for an explicit timeout.
/// </param>
/// <returns>False if <see cref="IsCompleted"/> is true, true otherwise. See remarks.</returns>
public async ValueTask<bool> WriteAsync( T message, CancellationToken cancellationToken, int timeout = 0 );
```

The only abstract method to implement is:
```csharp
/// <summary>
/// Must write the message to the buffer.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="buffer">The buffer.</param>
protected abstract void WriteMessage( in T message, IBufferWriter<byte> buffer );
```

Two concrete writers are available (that responds to their message readers peers):
[`StringLineMessageWriter`](StringLineMessageWriter.cs) and [`StringDelimitedMessageWriter`](StringDelimitedMessageWriter.cs).
