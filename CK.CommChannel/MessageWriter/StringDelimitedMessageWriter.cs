using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Writer of delimited strings.
/// </summary>
public sealed class StringDelimitedMessageWriter : DelimitedMessageWriterBase<string>
{
    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageWriter"/> with a single byte ending delimiter.
    /// Note that opposed to the <see cref="StringDelimitedMessageReader"/>, the start and end delimiter may be the same.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="encoding">The encoding used to encode the string into bytes.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="endDelimiter">Ending delimiter.</param>
    public StringDelimitedMessageWriter( PipeWriter writer,
                                         Encoding encoding,
                                         byte startDelimiter,
                                         byte endDelimiter )

        : base( writer, startDelimiter, endDelimiter )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageWriter"/> with 2 or more bytes ending delimiter.
    /// Note that opposed to the <see cref="StringDelimitedMessageReader"/>, the start byte may be the same as the last byte of the ending delimiter.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="multiEndDelimiter">Two or more ending delimiters.</param>
    public StringDelimitedMessageWriter( PipeWriter writer,
                                         Encoding encoding,
                                         byte startDelimiter,
                                         ReadOnlyMemory<byte> multiEndDelimiter )

        : base( writer, startDelimiter, multiEndDelimiter )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageWriter"/>.
    /// Note that opposed to the <see cref="StringDelimitedMessageReader"/>, the start byte may be the same as the last byte of the ending delimiter.
    /// </summary>
    /// <param name="writer">The pipe writer.</param>
    /// <param name="startDelimiters">Starting delimiter. Must not be empty.</param>
    /// <param name="multiEndDelimiter">Ending delimiters. Must not be empty.</param>
    public StringDelimitedMessageWriter( PipeWriter writer,
                                         Encoding encoding,
                                         ReadOnlyMemory<byte> startDelimiters,
                                         ReadOnlyMemory<byte> endDelimiters )

        : base( writer, startDelimiters, endDelimiters )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Gets the encoding used to encode the string into bytes.
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// Writes the encoded string.
    /// </summary>
    /// <param name="message">The string message.</param>
    /// <param name="buffer">The target buffer.</param>
    /// <returns>Always true.</returns>
    protected override bool WriteMessagePayload( in string message, IBufferWriter<byte> buffer )
    {
        Encoding.GetBytes( message.AsSpan(), buffer );
        return true;
    }

}
