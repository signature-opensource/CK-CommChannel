using System.Buffers;
using System.IO.Pipelines;

namespace CK.CommChannel;

/// <summary>
/// Sealed <see cref="IOutgoingMessage"/> writer.
/// </summary>
/// <typeparam name="T">The type of the message.</typeparam>
public sealed class MessageWriter<T> : MessageWriterBase<T> where T : class, IOutgoingMessage
{
    /// <summary>
    /// Initializes a new <see cref="MessageWriter{T}"/>.
    /// </summary>
    /// <param name="writer">
    /// The pipe writer. Can be a <see cref="StablePipeWriter"/> (and in such case, its <see cref="StablePipeReader.PipeBehavior"/>
    /// is automatically used if <paramref name="behavior"/> is null.
    /// </param>
    public MessageWriter( PipeWriter writer, IStablePipeBehavior? behavior )
        : base( writer, behavior )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MessageWriter{T}"/> on a <see cref="StablePipeWriter"/>.
    /// The <see cref="StablePipeWriter.PipeBehavior"/> is used.
    /// </summary>
    /// <param name="writer">The stable pipe reader.</param>
    public MessageWriter( StablePipeWriter writer )
        : base( writer )
    {
    }

    /// <summary>
    /// Calls <see cref="IOutgoingMessage.Write(IBufferWriter{byte})"/>.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="buffer">The target buffer.</param>
    protected override void WriteMessage( in T message, IBufferWriter<byte> buffer ) => message.Write( buffer );
}
