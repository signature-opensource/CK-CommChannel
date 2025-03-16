using System;

namespace CK.CommChannel;

/// <summary>
/// Returned value of <see cref="IStablePipeBehavior.OnError(Exception)"/>.
/// </summary>
public enum OnErrorAction
{
    /// <summary>
    /// Throws the exception.
    /// This does not complete the reader or writer nor revoke the current inner reader or writer.
    /// This is the default behavior when no <see cref="IStablePipeBehavior"/> is available.
    /// </summary>
    Throw,

    /// <summary>
    /// Ask the reader or writer to retry, potentially waiting for a renewed inner reader
    /// or writer.
    /// </summary>
    Retry,

    /// <summary>
    /// Ask the reader or writer to return a canceled result.
    /// This does not complete the reader or writer nor revoke the current inner reader or writer.
    /// </summary>
    Cancel
}
