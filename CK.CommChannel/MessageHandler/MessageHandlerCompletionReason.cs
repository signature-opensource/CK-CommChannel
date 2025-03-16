using System.Runtime.ExceptionServices;

namespace CK.CommChannel;

/// <summary>
/// Describes the handler completion reason.
/// </summary>
public enum MessageHandlerCompletionReason
{
    /// <summary>
    /// <see cref="MessageHandler{T}.StartReadLoop(int)"/> has never been called.
    /// </summary>
    None,

    /// <summary>
    /// The <see cref="MessageHandler{T}.StopReadLoop(bool)"/> method has been called.
    /// </summary>
    StoppedLoop,

    /// <summary>
    /// The <see cref="MessageHandler{T}.HandleMessageAsync(T, System.Threading.CancellationToken)"/> returned false.
    /// </summary>
    ProcessMessage,

    /// <summary>
    /// The maximal number of messages (parameter of the <see cref="MessageHandler{T}.StartReadLoop(int)"/>).
    /// </summary>
    MaxMessageNumber,

    /// <summary>
    /// The message reader has been completed. See <see cref="MessageReaderBase{T}.IsCompleted"/>.
    /// </summary>
    OnReaderComplete,

    /// <summary>
    /// A read timeout occurred and <see cref="MessageHandler{T}.OnReadTimeoutAsync()"/>
    /// returned false.
    /// </summary>
    ReadTimeout,

    /// <summary>
    /// An unhandled exception has occurred and <see cref="MessageHandler{T}.OnUnhandledExceptionAsync(ExceptionDispatchInfo)"/>
    /// returned false.
    /// </summary>
    UnhandledError

}
