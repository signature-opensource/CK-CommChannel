using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Simple writer of string lines.
/// </summary>
public sealed class StringLineMessageWriter : LineMessageWriterBase<string>
{
    /// <summary>
    /// Initializes a new <see cref="StringLineMessageWriter"/>.
    /// </summary>
    /// <param name="writer">The pipe reader.</param>
    /// <param name="encoding">The encoding used to encode the string into bytes.</param>
    /// <param name="delimiter">Delimiter. Must not be empty.</param>
    public StringLineMessageWriter( PipeWriter writer, Encoding encoding, string delimiter = "\r\n" )
        : base( writer, StringLineMessageReader.GetDelimiter( encoding, delimiter ) )
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
    protected override void WriteMessagePayload( in string message, IBufferWriter<byte> buffer ) => Encoding.GetBytes( message.AsSpan(), buffer );

}
