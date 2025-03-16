# Stable PipeReader and PipeWriter

> Understanding a minimum of [System.IO.Pipelines in .NET](https://docs.microsoft.com/en-us/dotnet/standard/io/pipelines) is necessary
> before reading this.

Stable pipes are decorators on `PipeReader` and `PipeWriter` that can forget or change
its inner reader in a thread safe manner and provides a `DefaultTimeout`.

This timeout is only used when `FlushAsync` (or `ReadAsync`) are called with a non cancelable
`CancellationToken`: this timeout currently acts as a safety net when the calling code doesn't
handle cancellations or timeouts at its level.

---
**Note:** This currently seems a simple and effective option, however if it appears that the timeout
should be useful even with a cancelable token, a `bool AlwaysUseDefaultTimeout` that can be 
changed dynamically at any time will be rather easy to implement.

---

These 2 stable pipes share the same pattern and can be used independently of the CommunicationChannel.
Both `StablePipeWriter` and `StablePipeWriter` implements the [`IStablePipe`](IStablePipe.cs). interface.


## StablePipeWriter

A [StablePipeWriter](StablePipeWriter.cs) is a specialized PipeWriter that wraps an actual, inner, PipeWriter (see [decorator pattern](https://en.wikipedia.org/wiki/Decorator_pattern)).

This decorator brings 3 functionalities to the basic PipeWriter:
 - The inner writer can be changed (or forgotten) at any time in a thread safe manner by calling either:
   - `SetWriter( PipeWriter writer, bool completeWhenDone = false )`
   - or `Close( bool complete )`.
 - A `DefaultTimeout` property is available that is used when the `CancellationToken` provided to [FlushAsync](https://docs.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipewriter.flushasync) 
 - is not cancelable (see [CancellationToken.CanBeCanceled](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.canbecanceled)).
   When `FlushAsync` fails because of this `DefaultTimeout`, a `System.TimeoutException` is thrown.
   It defaults to -1 and is disabled by default.
 - A `RetryWriteCount` property can be used to automatically retries `FlushAsync` calls that failed because 
   of a (positive) `DefaultTimeout`.
- The `event ReadOnlySpanAction<byte,StablePipeWriter> OnDataWritten` is available and raised on each successful `FlushAsync`. 
  This can typically be used for logging outgoing messages. 

**Important:** The StablePipeWriter holds an internal buffer of the bytes written (a simple [ByteArrayWriter](https://github.com/Invenietis/CK-Core/blob/develop/CK.Core/ByteArrayWriter.cs))
that grows dynamically and is reused between `FlushAsync` calls.
It is the `FlushAsync` method that does the real job of writing this buffer into the inner writer and flushing it. This guaranties that all or none of the
currently written data will be emitted to the same available inner writer: when a inner writer is closed or changed, the data has been written either to the
previous xor new one. 

A positive effect of this intermediate buffering is that on an instable connection, "messages" are defined by the calls to `FlushAsync` and are delivered **as
a whole or not at all**. Of course, this is not true from the receiver side: inner writer(s), network layer, or even the receiver itself can still fail in
the middle of a "message", but from the sender perspective, once FlushAsync has been called, the data has necessarily been emitted (and `OnDataWritten` event
is raised).  

A negative effect of this intermediate buffer is that it better fits a "message" pattern than a "stream" approach. Big data should either be split (by
calling `FlushAsync` regularly) or the StablePipeWriter can be "short circuited" and data can be written directly to the inner writer. The StablePipeWriter
exposes its `PipeWriter? CurrentInnerWriter { get; }` that *may* be used if (really) needed.


This class is sealed. More functionalities can be obtained thanks to an associated optional
[PipeBehavior](IStablePipeBehavior.cs) (see below).

## StablePipeReader

[StablePipeReader](StablePipeReader.cs) follows the same pattern as the writer:

 - The inner reader can be changed (or forgotten) at any time in a thread safe manner by calling either:
   - `SetReader( PipeReader reader, bool completeWhenDone = false )`
   - or `Close( bool complete )`.
 - A `DefaultTimeout` property is available that is used when the `CancellationToken` provided to [ReadAsync](https://docs.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipereader.readasync)
   is not cancelable (see [CancellationToken.CanBeCanceled](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken.canbecanceled)).
   When `ReadAsync` fails because of this `DefaultTimeout`, a `System.TimeoutException` is thrown.
   It defaults to -1 and is disabled by default.

There is (obviously) no retries for the reader and no need to expose the current inner reader.

Just like the writer, an optional `PipeBehavior` can be associated.

## Pipe behavior

The [IStablePipeBehavior](IStablePipeBehavior.cs) is an optional strategy that can drive the behavior
of a `StablePipeWriter` or `StablePipeReader`.

It can be used for logging but is mainly used to enable the CommunicationChannel layer's automatic
reconnection feature. This interface provides default implementations of its methods that makes its
`public static readonly IStablePipeBehavior Default` property a no-op behavior (a stable pipe reader
or writer associated to this default behavior behaves like a regular one).







