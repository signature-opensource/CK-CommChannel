using CK.Core;
using System;
using System.Buffers;
using System.IO.Pipelines;

namespace CK.CommChannel;

/// <summary>
/// Writer that appends a delimiter after each <see cref="WriteMessage(in T, IBufferWriter{byte})"/>.
/// </summary>
public abstract class LineMessageWriterBase<T> : MessageWriterBase<T>
{
    readonly ReadOnlyMemory<byte> _delimiter;

    /// <summary>
    /// Initializes a new <see cref="MessageHandlerBase"/>.
    /// </summary>
    /// <param name="writer">The pipe reader.</param>
    /// <param name="delimiter">Delimiter. Must not be empty.</param>
    public LineMessageWriterBase( PipeWriter writer, ReadOnlyMemory<byte> delimiter )
        : base( writer, null )
    {
        Throw.CheckArgument( delimiter.Length > 0 );
        _delimiter = delimiter;
    }

    /// <summary>
    /// Calls <see cref="WriteMessagePayload(in T, IBufferWriter{byte})"/> and writes the delimiter.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="buffer">The target buffer.</param>
    protected sealed override void WriteMessage( in T message, IBufferWriter<byte> buffer )
    {
        WriteMessagePayload( message, buffer );
        buffer.Write( _delimiter.Span );
    }

    /// <summary>
    /// Must writes the message to the buffer.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="buffer">The buffer.</param>
    protected abstract void WriteMessagePayload( in T message, IBufferWriter<byte> buffer );
}
