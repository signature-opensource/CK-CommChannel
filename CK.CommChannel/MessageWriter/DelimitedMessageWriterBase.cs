using CK.Core;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;

namespace CK.CommChannel;

/// <summary>
/// Template pattern for protocols based on messages delimited by 1 or more starting and ending bytes.
/// </summary>
public abstract class DelimitedMessageWriterBase<T> : MessageWriterBase<T>
{
    readonly ReadOnlyMemory<byte> _startDelimiters;
    readonly ReadOnlyMemory<byte> _endDelimiters;

    /// <summary>
    /// Initializes a new <see cref="DelimitedMessageWriterBase{T}"/> with a single byte ending delimiter.
    /// Note that opposed to the <see cref="DelimitedMessageReader{T}"/>, the start and end delimiter may be the same.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="endDelimiter">Ending delimiter.</param>
    public DelimitedMessageWriterBase( PipeWriter writer,
                                   byte startDelimiter,
                                   byte endDelimiter )
        : base( writer, null )
    {
        _startDelimiters = new byte[] { startDelimiter };
        _endDelimiters = new byte[] { endDelimiter };
    }

    /// <summary>
    /// Initializes a new <see cref="DelimitedMessageWriterBase{T}"/> with 2 or more bytes ending delimiter.
    /// Note that opposed to the <see cref="DelimitedMessageReader{T}"/>, the start byte may be the same as the last byte of the ending delimiter.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="multiEndDelimiter">Two or more ending delimiters.</param>
    public DelimitedMessageWriterBase( PipeWriter writer,
                                   byte startDelimiter,
                                   ReadOnlyMemory<byte> multiEndDelimiter )
        : base( writer, null )
    {
        Throw.CheckArgument( multiEndDelimiter.Length >= 2 );
        _startDelimiters = new byte[] { startDelimiter };
        _endDelimiters = multiEndDelimiter;
    }

    /// <summary>
    /// Initializes a new <see cref="DelimitedMessageWriterBase{T}"/>.
    /// Note that opposed to the <see cref="DelimitedMessageReader{T}"/>, the start byte may be the same as the last byte of the ending delimiter.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="startDelimiters">Starting delimiter. Must not be empty.</param>
    /// <param name="multiEndDelimiter">Ending delimiters. Must not be empty.</param>
    public DelimitedMessageWriterBase( PipeWriter writer,
                                   ReadOnlyMemory<byte> startDelimiters,
                                   ReadOnlyMemory<byte> endDelimiters )
        : base( writer, null )
    {
        Throw.CheckArgument( startDelimiters.Length >= 1 );
        Throw.CheckArgument( endDelimiters.Length >= 1 );
        _startDelimiters = startDelimiters;
        _endDelimiters = endDelimiters;
    }

    /// <summary>
    /// Writes the start delimiter, calls <see cref="WriteMessagePayload(in T, IBufferWriter{byte})"/> and writes the ending delimiter.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="buffer">The target buffer.</param>
    protected sealed override void WriteMessage( in T message, IBufferWriter<byte> buffer )
    {
        buffer.Write( _startDelimiters.Span );
        WriteMessagePayload( message, buffer );
        buffer.Write( _endDelimiters.Span );
    }

    /// <summary>
    /// Must writes the message to the buffer.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="buffer">The buffer.</param>
    /// <returns>True on success, false on error (<see cref="MessageWriterBase{T}.WriteAsync(T, CancellationToken, int)"/> will return false.</returns>
    protected abstract bool WriteMessagePayload( in T message, IBufferWriter<byte> buffer );

}
