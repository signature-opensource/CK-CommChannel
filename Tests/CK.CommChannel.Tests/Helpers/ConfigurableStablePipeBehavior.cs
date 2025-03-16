using CK.Core;
using System;

namespace CK.CommChannel.Tests;

class ConfigurableStablePipeBehavior : IStablePipeBehavior
{
    public ConfigurableStablePipeBehavior()
    {
        InnerCanceledResult = true;
    }

    public OnErrorAction OnErrorAction { get; set; }

    public OnInnerCompletedAction OnInnerCompletedAction { get; set; }

    public bool InnerCanceledResult { get; set; }

    public virtual OnErrorAction OnError( Exception exception )
    {
        ActivityMonitor.StaticLogger.Error( $"ConfigurableStablePipeBehavior: OnError. Returning {OnErrorAction}.", exception );
        return OnErrorAction;
    }

    public virtual void OnSwallowedException( string reason, Exception exception )
    {
        ActivityMonitor.StaticLogger.Warn( $"ConfigurableStablePipeBehavior: Swallowed Exception: '{reason}'.", exception );
    }

    public virtual void OnOperationCanceled( OperationCanceledException ex )
    {
        ActivityMonitor.StaticLogger.Info( $"ConfigurableStablePipeBehavior: OnOperationCanceled.", ex );
    }

    public virtual OnInnerCompletedAction OnInnerCompleted()
    {
        ActivityMonitor.StaticLogger.Info( $"ConfigurableStablePipeBehavior: OnInnerCompleted. Returning {OnInnerCompletedAction}." );
        return OnInnerCompletedAction;
    }

    public virtual bool ReturnInnerCanceledResult()
    {
        ActivityMonitor.StaticLogger.Info( $"ConfigurableStablePipeBehavior: ReturnInnerCanceledResult. Returning {InnerCanceledResult}." );
        return InnerCanceledResult;
    }
}

