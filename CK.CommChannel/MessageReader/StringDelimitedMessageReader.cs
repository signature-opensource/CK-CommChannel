using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Simple reader of delimited strings.
/// A null string is the <see cref="MessageReaderBase{T}.EmptyMessage"/>.
/// </summary>
public sealed class StringDelimitedMessageReader : DelimitedMessageReader<string?>
{
    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageReader"/>.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="endDelimiter">Ending delimiter.</param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="endDelimiter"/> in message.
    /// By default delimiters are trimmed.
    /// </param>
    public StringDelimitedMessageReader( PipeReader reader,
                                         Encoding encoding,
                                         byte startDelimiter,
                                         byte endDelimiter,
                                         bool removeDelimiters = true )
        : base( reader, startDelimiter, endDelimiter, null, removeDelimiters )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageReader"/> with 2 or more ending delimiters.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="multiEndDelimiter">Two or more ending delimiters. The last byte must not be the same as <paramref name="startDelimiter"/>.</param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="multiEndDelimiter"/> in message.
    /// By default the message submitted to <see cref="MessageReaderBase.Convert(ReadOnlySequence{byte})"/> is trimmed
    /// of the delimiters.
    /// </param>
    public StringDelimitedMessageReader( PipeReader reader,
                                         Encoding encoding,
                                         byte startDelimiter,
                                         ReadOnlyMemory<byte> multiEndDelimiter,
                                         bool removeDelimiters = true )
        : base( reader, startDelimiter, multiEndDelimiter, null, removeDelimiters )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Gets the encoding used to decode the bytes to string.
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// Converts the message to a string using the <see cref="Encoding"/>.
    /// </summary>
    /// <param name="message">The bytes message.</param>
    /// <returns>The string message.</returns>
    protected override string? Convert( in ReadOnlySequence<byte> message ) => Encoding.GetString( message );

}
