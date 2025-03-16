using System;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// Captures common aspects of <see cref="StablePipeReader"/> and <see cref="StablePipeWriter"/>.
/// </summary>
public interface IStablePipe
{
    /// <summary>
    /// Gets or sets the default timeout in milliseconds to read or flush
    /// when the provided cancellation token is not cancelable.
    /// </summary>
    int DefaultTimeout { get; set; }

    /// <summary>
    /// Gets whether this reader or writer is definitely completed.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Gets whether this reader or writer is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the associated <see cref="IStablePipeBehavior"/>.
    /// </summary>
    IStablePipeBehavior PipeBehavior { get; }

    /// <summary>
    /// Gets a task that is resolved whenever this reader or writer
    /// state changes (<see cref="IsCompleted"/> or <see cref="IsAvailable"/>).
    /// This is always completed when <see cref="IsCompleted"/> is true.
    /// </summary>
    Task StateChange { get; }

    /// <summary>
    /// Simple relay to <see cref="StablePipeReader.CancelPendingRead()"/> or <see cref="StablePipeWriter.CancelPendingFlush()"/>.
    /// </summary>
    void CancelPendingOperation();

    /// <summary>
    /// Closes this reader or writer, either temporarily or completes it definitely.
    /// </summary>
    /// <param name="complete">True to definitely complete.</param>
    /// <returns>True is this actually closed this reader or writer, false if it was already closed.</returns>
    bool Close( bool complete );

    /// <summary>
    /// Gets the first exception provided to <see cref="Complete(Exception?)"/>.
    /// </summary>
    Exception? CompleteException { get; }

    /// <summary>
    /// Definitely completes this reader or writer.
    /// </summary>
    /// <param name="exception">
    /// An optional exception that will be available in <see cref="CompleteException"/>
    /// if this is the first completion.
    /// </param>
    void Complete( Exception? exception = null );
}
