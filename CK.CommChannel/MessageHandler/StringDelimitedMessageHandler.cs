using System;
using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Base class handler of lines delimited by a start byte and one or more end bytes.
/// At least <see cref="MessageHandler{T}.HandleMessageAsync(T, System.Threading.CancellationToken)"/>
/// must be overridden.
/// </summary>
public abstract class StringDelimitedMessageHandler : MessageHandler<string?>
{
    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageHandler"/>.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="endDelimiter">Ending delimiter.</param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="endDelimiter"/> in message.
    /// By default delimiters are trimmed.
    /// </param>
    public StringDelimitedMessageHandler( PipeReader reader,
                                          Encoding encoding,
                                          byte startDelimiter,
                                          byte endDelimiter,
                                          bool removeDelimiters = true )
        : base( new StringDelimitedMessageReader( reader, encoding, startDelimiter, endDelimiter, removeDelimiters ))
    {
    }

    /// <summary>
    /// Initializes a new <see cref="StringDelimitedMessageHandler"/> with 2 or more ending delimiters.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="multiEndDelimiter">Two or more ending delimiters. The last byte must not be the same as <paramref name="startDelimiter"/>.</param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="multiEndDelimiter"/> in message.
    /// By default the message submitted to <see cref="MessageReaderBase.Convert(in System.Buffers.ReadOnlySequence{byte})"/> is trimmed
    /// of the delimiters.
    /// </param>
    public StringDelimitedMessageHandler( PipeReader reader,
                                          Encoding encoding,
                                          byte startDelimiter,
                                          ReadOnlyMemory<byte> multiEndDelimiter,
                                          bool removeDelimiters = true )
        : base( new StringDelimitedMessageReader( reader, encoding, startDelimiter, multiEndDelimiter, removeDelimiters ))
    {
    }
}
