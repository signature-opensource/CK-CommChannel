using System.IO.Pipelines;

namespace CK.CommChannel;

/// <summary>
/// Pipe channel allocated by <see cref="MemoryChannel.AllocatePipeChannel"/>
/// and <see cref="MemoryChannel.AllocateNetworkStreamChannelAsync"/>.
/// This is the same as the <see cref="IDuplexPipe"/> but reverted so that the <see cref="Input"/>
/// is a writer and <see cref="Output"/> is a reader.
/// </summary>
public interface IPipeChannel
{
    /// <summary>
    /// Writer to the input of the communication channel (the <see cref="CommunicationChannel.Reader"/>).
    /// </summary>
    PipeWriter Input { get; }

    /// <summary>
    /// Reader of the output of the communication channel (the <see cref="CommunicationChannel.Writer"/>).
    /// </summary>
    PipeReader Output { get; }
}
