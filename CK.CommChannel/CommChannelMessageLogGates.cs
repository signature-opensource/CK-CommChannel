using CK.Core;

namespace CK.CommChannel;

public static class CommChannelMessageLogGates
{
    /// <summary>
    /// Static log gate for read buffers,
    /// with the display name "CK.CommChannel.MessageReader".
    /// When Open, logs all read buffers to the StaticLogger,
    /// as Debug log lines with <see cref="MessageReaderBase{T}.LogTag"/>.
    /// Closed by default.
    /// </summary>
    // ReSharper disable once StaticMemberInGenericType
    public static readonly StaticGate MessageReaderLogGate = new StaticGate( "CK.CommChannel.MessageReader", false );

    /// <summary>
    /// Static log gate for write buffers,
    /// with the display name "CK.CommChannel.MessageWriter".
    /// When Open, logs all written messages to the StaticLogger,
    /// as Debug log lines with <see cref="MessageWriterBase{T}.LogTag"/>.
    /// Closed by default.
    /// </summary>
    // ReSharper disable once StaticMemberInGenericType
    public static readonly StaticGate MessageWriterLogGate = new StaticGate( "CK.CommChannel.MessageWriter", false );
}
