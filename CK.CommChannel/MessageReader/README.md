# Message readers

A message reader is plugged on a `PipeReader` and extracts messages from it.
These are easy to use helpers that encapsulate once for all not so easy to write code
that deals with [`ReadOnlySequence<byte>`](https://docs.microsoft.com/en-us/dotnet/api/system.buffers.readonlysequence-1)
and [PipeReader.AdvanceTo](https://docs.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipereader.advanceto) calls.

Readers _pulls_ messages from a `PipeReader`. See also [MessageHandlers](../MessageHandler/README.md) 
that are _pull to push_ adapters.

To write messages, see [MessageWriters](../MessageWriter/README.md) helpers.

## Common features
The abstract [`MessageReaderBase<T>`](MessageReaderBase.cs) where `T` is the type of the messages
implements core algorithms and features.

The 2 abstract methods must implement the message parsing:
```csharp
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
protected abstract T Convert( ReadOnlySequence<byte> message );
```

The message type can be as simple as a `string` (see `StringLineMessageReader` and `StringDelimitedMessageReader` below)
or be an abstract base class for a family of messages (or even an interface).

Among the possible messages, one must be the `EmptyMessage`. This empty message will returned when
the `PipeReader` has been completed or `CancelPendingRead()` has been called (either on the reader or on
the `PipeReader`). This special message must be provided to the constructor:

```csharp
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
```

The available API is simple:
```csharp
/// <summary>
/// Gets or sets the default read timeout of a message in milliseconds.
/// <para>
/// This must be -1 or a positive value.
/// </para>
/// </summary>
public int DefaultMessageReceiveTimeout { get; set; }

/// <summary>
/// Gets whether the end of the data stream has been reached.
/// ReadNext methods will always return the <see cref="EmptyMessage"/>.
/// </summary>
public bool IsCompleted { get; }

/// <summary>
/// Gets the cancel message that is returned when the pipe reader has been completed
/// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
/// </summary>
public T EmptyMessage { get; }

/// <summary>
/// Cancels the pending <see cref="ReadNextAsync(CancellationToken, int)" /> operation without causing it to throw and without
/// completing.
/// If there is no pending operation, this cancels the next operation.
/// </summary>
/// <remarks>The canceled operation returns the <see cref="EmptyMessage/>.</remarks>
public void CancelPendingRead();

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
public async ValueTask<T> ReadNextAsync( CancellationToken cancellationToken, int timeout = 0, Func<T, bool>? messageFilter = null );
```

Only `IStablePipeBehavior.OnError(Exception)` is called by the reader when:

 - A timeout occurs (the special `TimeoutMessageException` is used). 
 - Any other exception occurs in `TryParseMessage` or `Convert`.

This behavior is implicitly the `StablePipeReader.PipeBehavior` if the `PipeReader` is
a stable one.

When a reader becomes useless, it can be forgotten (there is nothing to dispose). Another one
may be used on the same `PipeReader` without issues (this can be used to dynamically change the
communication protocol).

> *Important:* A message reader is NOT thread safe. It must be used by a single activity (concurrent calls
> to `ReadAsync` throws an `InvalidOperationException`) whereas a [MessageWriter](../MessageWriter/README.md)
> with `MultipleWriters` set to true automatically serialize its calls.

## String message readers

These readers are the most easy to use since they basically read strings from a `PipeReader` using
an `Encoding`.
The string readers use the `null` string for the `EmptyMessage` (they specialize `MessageReaderBase<string?>`),
allowing an empty line (`String.Empty`) to be a valid message.

### StringLineMessageReader
This is the simplest to use and understand: it reads lines based on a line delimiter.

```csharp
var lineReader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );
string? firstLine = await lineReader.ReadNextAsync();
```

The line delimiter can be specified and can be any char or string:
```csharp
/// <summary>
/// Initializes a new <see cref="StringLineMessageReader"/>.
/// </summary>
/// <param name="reader">The pipe reader.</param>
/// <param name="encoding">The encoding used to decode the bytes to string.</param>
/// <param name="delimiter">Delimiter. Must not be empty.</param>
public StringLineMessageReader( PipeReader reader, Encoding encoding, string delimiter = "\r\n" )
```

The `null` string (the `EmptyMessage` for string readers) is returned when `PipeReader.CancelPendingRead()`
has been called and/or the `PipeReader` has been completed (no more data can be read from it).
A loop that would read all the lines from a `PipeReader` can be written like this:
```csharp
var lineReader = new StringLineMessageReader( pipeReader, Encoding.ASCII );
while( !lineReader.IsCompleted )
{
  string? l = await lineReader.ReadNextAsync();
  if( l != null )
  {
    Console.WriteLine( l );  
  }
}
```


### StringDelimitedMessageReader

This reader is able to read strings delimited by a starting byte and one or more ending bytes.
Protocols that use delimited messages instead of simple line endings can more easily handle
noise in the incoming data: only the bytes that appear between the start and end delimiters
are considered.

> **Note:** Currently, only a single start byte is implemented.

This reader is as easy to use as the `StringLineMessageReader`: only its constructors differ
to specify the starting and ending delimiters.

