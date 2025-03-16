namespace CK.CommChannel;

/// <summary>
/// Returned value of <see cref="IStablePipeBehavior.OnInnerCompleted"/>.
/// </summary>
public enum OnInnerCompletedAction
{
    /// <summary>
    /// Ask the stable reader or writer to complete itself and return a Completed result.
    /// This is the default behavior when no <see cref="IStablePipeBehavior"/> is available.
    /// </summary>
    Complete,

    /// <summary>
    /// Ask the reader or writer to forget the current inner reader or writer and to wait for a new one.
    /// </summary>
    Retry,

    /// <summary>
    /// Ask the reader or writer to forget the current inner reader or writer and to throw an InvalidOperationException.
    /// This does not complete the stable reader or writer.
    /// </summary>
    Throw
}
