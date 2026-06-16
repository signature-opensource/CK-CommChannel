using System.Buffers;

namespace CK.CommChannel;

/// <summary>
/// A "message" is anything that can write itself into a <see cref="IBufferWriter{T}"/> of bytes.
/// </summary>
public interface IOutgoingMessage
{
    /// <summary>
    /// Writes this message into the writer.
    /// </summary>
    /// <param name="buffer">The buffer writer.</param>
    void Write( IBufferWriter<byte> buffer );
}
