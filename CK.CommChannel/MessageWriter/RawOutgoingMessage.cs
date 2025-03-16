using System;
using System.Buffers;

namespace CK.CommChannel;

/// <summary>
/// Basic outgoing message with any content.
/// Should obviously be used with care.
/// </summary>
public sealed class RawOutgoingMessage : IOutgoingMessage
{
    readonly ReadOnlyMemory<byte> _content;

    /// <summary>
    /// Initialize a new message with its content.
    /// </summary>
    /// <param name="content">The content.</param>
    public RawOutgoingMessage( ReadOnlyMemory<byte> content )
    {
        _content = content;
    }

    /// <inheritdoc />
    public void Write( IBufferWriter<byte> buffer ) => buffer.Write( _content.Span );
}
