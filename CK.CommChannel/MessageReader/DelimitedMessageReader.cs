using CK.Core;
using System;
using System.Buffers;
using System.IO.Pipelines;

namespace CK.CommChannel;

/// <summary>
/// Template pattern for protocols based on messages delimited by 1 starting byte and 1 or more ending bytes.
/// This handles invalid messages (noise, garbage): only segments that are between delimiters
/// are submitted to <see cref="MessageReaderBase.Convert(ReadOnlySequence{byte})"/>.
/// </summary>
public abstract class DelimitedMessageReader<T> : MessageReaderBase<T>
{
    readonly bool _removeDelimiters;
    readonly byte _startDelimiter;
    readonly byte _endDelimiter;
    readonly ReadOnlyMemory<byte> _multiEndDelimiter;
    bool _inMessage;

    /// <summary>
    /// Initializes a new <see cref="DelimitedMessageReader{T}"/>  with a single byte ending delimiter.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="endDelimiter">Ending delimiter.</param>
    /// <param name="emptyMessage">
    /// The message returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="endDelimiter"/> in message.
    /// By default the message submitted to <see cref="MessageReaderBase.Convert(ReadOnlySequence{byte})"/> is trimmed
    /// of the delimiters.
    /// </param>
    public DelimitedMessageReader( PipeReader reader,
                                   byte startDelimiter,
                                   byte endDelimiter,
                                   T emptyMessage,
                                   bool removeDelimiters = true )
        : base( reader, emptyMessage, null )
    {
        Throw.CheckArgument( "Parameters start & end delimiters must not be the same.", startDelimiter != endDelimiter );
        _startDelimiter = startDelimiter;
        _endDelimiter = endDelimiter;
        _removeDelimiters = removeDelimiters;
    }

    /// <summary>
    /// Initializes a new <see cref="DelimitedMessageReader{T}"/> with 2 or more ending delimiters.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="startDelimiter">Starting delimiter.</param>
    /// <param name="multiEndDelimiter">Two or more ending delimiters. The last byte must not be the same as <paramref name="startDelimiter"/>.</param>
    /// <param name="emptyMessage">
    /// The message returned when the pipe reader has been completed
    /// or <see cref="PipeReader.CancelPendingRead()"/> has been called.
    /// </param>
    /// <param name="removeDelimiters">
    /// False to keep the <paramref name="startDelimiter"/> and <paramref name="multiEndDelimiter"/> in message.
    /// By default the message submitted to <see cref="MessageReaderBase.Convert(ReadOnlySequence{byte})"/> is trimmed
    /// of the delimiters.
    /// </param>
    public DelimitedMessageReader( PipeReader reader,
                                   byte startDelimiter,
                                   ReadOnlyMemory<byte> multiEndDelimiter,
                                   T emptyMessage,
                                   bool removeDelimiters = true )
        : base( reader, emptyMessage, null )
    {
        Throw.CheckArgument( multiEndDelimiter.Length >= 2 );
        _multiEndDelimiter = multiEndDelimiter;
        _startDelimiter = startDelimiter;
        _endDelimiter = multiEndDelimiter.Span[multiEndDelimiter.Length - 1];
        Throw.CheckArgument( "Parameters startDelimiter must not be the same as the last multiEndDelimiter byte.", startDelimiter != _endDelimiter );
        _removeDelimiters = removeDelimiters;
    }

    /// <summary>
    /// Isolates a line.
    /// </summary>
    /// <param name="buffer">The buffer to consume.</param>
    /// <param name="message">the next line.</param>
    /// <returns>True if a line has been found (and the buffer has been sliced), false otherwise.</returns>
    protected override bool TryParseMessage( ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message )
    {
        for(; ; )
        {
            if( _inMessage )
            {
                // In message: looking for the ending delimiter.
                if( FindEndDelimiter( buffer, out var pEnd, out var pAfterEnd ) )
                {
                    // Any starting delimiter appearing in the potential message, is skipped.
                    SequencePosition? pBegBefore;
                    while( (pBegBefore = buffer.Slice( 1, pEnd ).PositionOf( _startDelimiter )) != null )
                    {
                        buffer = buffer.Slice( pBegBefore.Value );
                    }
                    // The ending delimiter belongs to the message: increment the position so it will be skipped.
                    message = _removeDelimiters
                                ? buffer.Slice( 1, pEnd )
                                : buffer.Slice( 0, pAfterEnd );
                    buffer = buffer.Slice( pAfterEnd );
                    _inMessage = false;
                    return true;
                }
                message = ReadOnlySequence<byte>.Empty;
                return false;
            }
            // Out of message: looking for the starting delimiter.
            SequencePosition? pBeg = buffer.PositionOf( _startDelimiter );
            if( !pBeg.HasValue )
            {
                // No start: forget everything.
                buffer = buffer.Slice( buffer.End );
                message = ReadOnlySequence<byte>.Empty;
                return false;
            }
            // Keep the starting '#' in the buffer, forget everything before.
            buffer = buffer.Slice( pBeg.Value );
            _inMessage = true;
        }
    }

    bool FindEndDelimiter( ReadOnlySequence<byte> buffer, out SequencePosition pEnd, out SequencePosition pAfterEnd )
    {
        if( _multiEndDelimiter.IsEmpty )
        {
            SequencePosition? pE = buffer.PositionOf( _endDelimiter );
            if( pE != null )
            {
                pEnd = pE.Value;
                pAfterEnd = buffer.GetPosition( 1, pE.Value );
                return true;
            }
            pEnd = default;
            pAfterEnd = default;
            return false;
        }
        Span<byte> del = stackalloc byte[_multiEndDelimiter.Length];

        SequencePosition? p;
        while( (p = buffer.PositionOf( _multiEndDelimiter.Span[0] )) != null )
        {
            buffer = buffer.Slice( p.Value );
            if( buffer.Length < _multiEndDelimiter.Length )
            {
                break;
            }
            buffer.Slice( 0, _multiEndDelimiter.Length ).CopyTo( del );
            if( del.SequenceEqual( _multiEndDelimiter.Span ) )
            {
                pEnd = p.Value;
                pAfterEnd = buffer.GetPosition( _multiEndDelimiter.Length, pEnd );
                return true;
            }
            buffer = buffer.Slice( 1 );
        }
        pEnd = default;
        pAfterEnd = default;
        return false;
    }

}
