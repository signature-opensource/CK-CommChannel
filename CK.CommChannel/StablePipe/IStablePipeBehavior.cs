using System;

namespace CK.CommChannel;


/// <summary>
/// Interface that drives the <see cref="StablePipeReader"/> or <see cref="StablePipeWriter"/> behavior.
/// The <see cref="Default"/> instance has basically no effects.
/// </summary>
public interface IStablePipeBehavior
{
    sealed class Def : IStablePipeBehavior { }

    /// <summary>
    /// Gets a default behavior.
    /// </summary>
    public static readonly IStablePipeBehavior Default = new Def();

    /// <summary>
    /// Called for any communication failure.
    /// Defaults to <see cref="OnErrorAction.Throw"/>.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns>The action to apply.</returns>
    OnErrorAction OnError( Exception exception ) => OnErrorAction.Throw;

    /// <summary>
    /// Called when an exception has been ignored.
    /// Does nothing by default.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <param name="exception">The ignored exception.</param>
    void OnSwallowedException( string reason, Exception exception ) { }

    /// <summary>
    /// Called whenever an operation is canceled.
    /// Does nothing by default.
    /// </summary>
    /// <param name="ex">The exception.</param>
    void OnOperationCanceled( OperationCanceledException ex ) { }

    /// <summary>
    /// Called whenever the inner reader or writer returned a Completed result.
    /// Defaults to <see cref="OnInnerCompletedAction.Complete"/>.
    /// </summary>
    /// <returns>The action to apply.</returns>
    OnInnerCompletedAction OnInnerCompleted() => OnInnerCompletedAction.Complete;

    /// <summary>
    /// Called when a canceled result is obtained from the inner reader or writer.
    /// Defaults to true.
    /// </summary>
    /// <returns>True to return the canceled result, false to ignore it and retry.</returns>
    bool ReturnInnerCanceledResult() => true;
}
