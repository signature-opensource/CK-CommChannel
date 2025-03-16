using CK.Core;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Reader of string lines with a delimiter.
/// A null string is the <see cref="MessageReaderBase{T}.EmptyMessage"/>.
/// </summary>
public sealed class StringLineMessageReader : LineMessageReader<string?>
{
    readonly bool _skipLF;

    /// <summary>
    /// Standard line delimiter for Windows and Internet protocols.
    /// </summary>
    public static readonly ReadOnlyMemory<byte> CRLF = new byte[] { 13, 10 };

    /// <summary>
    /// Initializes a new <see cref="StringLineMessageReader"/> with an encoding and a non empty line delimiter.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="delimiter">Delimiter. Must not be empty.</param>
    public StringLineMessageReader( PipeReader reader, Encoding encoding, string delimiter )
        : base( reader, GetDelimiter( encoding, delimiter ), emptyMessage: null )
    {
        Encoding = encoding;
    }

    /// <summary>
    /// Initializes a new <see cref="StringLineMessageReader"/> with an encoding and the standard CR ('\r') or CRLF ('\r\n')
    /// line delimiter.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="skipLineFeed">False to consider only '\r' delimiter: any '\n' will appear at the start of the messages.</param>
    public StringLineMessageReader( PipeReader reader, Encoding encoding, bool skipLineFeed = true )
        : base( reader, CRLF.Slice( 0, 1 ), emptyMessage: null )
    {
        Throw.CheckNotNullArgument( encoding );
        Encoding = encoding;
        if( encoding != Encoding.UTF8 && encoding != Encoding.UTF8 && encoding != Encoding.Latin1 )
        {
            var standard = encoding.GetBytes( "\r\n" );
            if( !CRLF.Span.SequenceEqual( standard ) )
            {
                Throw.ArgumentException( $"The encoding '{encoding.EncodingName}' cannot be used with the standard end-of-line delimiter. The delimiter must be explicitly configured." );
            }
        }
        _skipLF = skipLineFeed;
    }

    internal static byte[] GetDelimiter( Encoding encoding, string delimiter )
    {
        Throw.CheckNotNullArgument( encoding );
        Throw.CheckNotNullOrEmptyArgument( delimiter );
        return encoding.GetBytes( delimiter );
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
    protected override string Convert( in ReadOnlySequence<byte> message )
    {
        if( _skipLF )
        {
            var r = message.FirstSpan;
            if( r.Length > 0 && r[0] == '\n' )
            {
                return Encoding.GetString( message.Slice( 1 ) );
            }
        }
        return Encoding.GetString( message );
    }
}
