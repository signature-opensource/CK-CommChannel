using CK.Core;
using System;
using System.Buffers;
using System.IO.Pipelines;

namespace CK.CommChannel;

/// <summary>
/// </summary>
public abstract class LineMessageReader<T> : MessageReaderBase<T>
{
    readonly ReadOnlyMemory<byte> _delimiter;

    /// <summary>
    /// Initializes a new <see cref="LineMessageReader{T}"/>.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="delimiter">Delimiter. Must not be empty.</param>
    /// <param name="emptyMessage">
    /// The message returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </param>
    public LineMessageReader( PipeReader reader, ReadOnlyMemory<byte> delimiter, T emptyMessage )
        : base( reader, emptyMessage, null )
    {
        Throw.CheckArgument( delimiter.Length > 0 );
        _delimiter = delimiter;
    }

    /// <summary>
    /// Gets the encoded line delimiter used by this line reader.
    /// </summary>
    protected ReadOnlyMemory<byte> Delimiter => _delimiter;

    /// <summary>
    /// Isolates a line.
    /// </summary>
    /// <param name="buffer">The buffer to consume.</param>
    /// <param name="message">the next line.</param>
    /// <returns>True if a line has been found (and the buffer has been sliced), false otherwise.</returns>
    protected override bool TryParseMessage( ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message )
    {
        return TryParseLine( ref buffer, _delimiter.Span, out message );
    }

    internal static bool TryParseLine( ref ReadOnlySequence<byte> buffer,
                                       ReadOnlySpan<byte> delimiter,
                                       out ReadOnlySequence<byte> message )
    {
        if( buffer.IsSingleSegment )
        {
            var span = buffer.FirstSpan;
            if( span.Length > 0 )
            {
                var idx = span.IndexOf( delimiter );
                if( idx >= 0 )
                {
                    if( idx == 0 )
                    {
                        message = ReadOnlySequence<byte>.Empty;
                        buffer = buffer.Slice( delimiter.Length );
                        return true;
                    }
                    message = buffer.Slice( 0, idx );
                    buffer = buffer.Slice( idx + delimiter.Length );
                    return true;
                }
            }
        }
        else
        {
            var sR = new SequenceReader<byte>( buffer );
            if( sR.TryReadTo( out message, delimiter ) )
            {
                buffer = buffer.Slice( sR.Position );
                return true;
            }
        }
        message = ReadOnlySequence<byte>.Empty;
        return false;
    }
}
