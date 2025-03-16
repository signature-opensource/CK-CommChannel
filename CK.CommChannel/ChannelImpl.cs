using CK.Core;
using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// Disposable channel implementation: a CommunicationChannel disposes and recreates such
/// implementation when required.
/// <para>
/// Instantiating a ChannelImpl is synchronous. If possible, the <see cref="Input"/> and <see cref="Output"/>
/// can be provided but most often, the asynchronous <see cref="DoInitialOpenAsync(IActivityMonitor, CommunicationChannelConfiguration)"/>
/// obtains them.
/// </para>
/// </summary>
public abstract class ChannelImpl
{
    PipeReader? _input;
    IStablePipeBehavior _readerBehavior;

    PipeWriter? _output;
    IStablePipeBehavior _writerBehavior;

    /// <summary>
    /// Initializes a new ChannelImpl with ready to use reader and writer pipes.
    /// </summary>
    /// <param name="reader">Ready to use reader.</param>
    /// <param name="readerBehavior">Reader associated behavior.</param>
    /// <param name="writer">Ready to use writer.</param>
    /// <param name="writerBehavior">Writer associated behavior.</param>
    protected ChannelImpl( PipeReader reader, IStablePipeBehavior readerBehavior, PipeWriter writer, IStablePipeBehavior writerBehavior )
    {
        Throw.CheckArgument( reader != null && readerBehavior != null && writer != null && writerBehavior != null );
        _input = reader;
        _readerBehavior = readerBehavior;
        _output = writer;
        _writerBehavior = writerBehavior;
    }

    /// <summary>
    /// Initializes a new ChannelImpl with unavailable reader and writer pipes.
    /// </summary>
    /// <param name="readerBehavior">Reader associated behavior.</param>
    /// <param name="writerBehavior">Writer associated behavior.</param>
    protected ChannelImpl( IStablePipeBehavior readerBehavior, IStablePipeBehavior writerBehavior )
    {
        Throw.CheckArgument( readerBehavior != null && writerBehavior != null );
        _readerBehavior = readerBehavior;
        _writerBehavior = writerBehavior;
    }

    /// <summary>
    /// Gets the reader pipe.
    /// When this is null, <see cref="Output"/> is also null.
    /// </summary>
    public PipeReader? Input => _input;

    /// <summary>
    /// Gets the <see cref="Input"/> behavior.
    /// </summary>
    public IStablePipeBehavior ReaderBehavior => _readerBehavior;

    /// <summary>
    /// Gets the writer pipe.
    /// When this is null, <see cref="Input"/> is also null.
    /// </summary>
    public PipeWriter? Output => _output;

    /// <summary>
    /// Gets the <see cref="Output"/> behavior.
    /// </summary>
    public IStablePipeBehavior WriterBehavior => _writerBehavior;


    internal async Task<bool> InitialOpenAsync( IActivityMonitor monitor,
                                                string channelName,
                                                CommunicationChannelConfiguration configuration )
    {
        Throw.DebugAssert( _input == null && _output == null );
        using var cts = new CancellationTokenSource( 5000 );
        try
        {
            await DoInitialOpenAsync( monitor, configuration, cts.Token ).ConfigureAwait( false );
            if( _input != null ) return true;
            monitor.Error( $"Opening {channelName} failed without exception." );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While opening {channelName}.", ex );
        }
        return false;
    }

    /// <summary>
    /// Called when <see cref="Input"/> and <see cref="Output"/> are null.
    /// On success, <see cref="OnInitialOpen"/> must be called with the ready to use pipes.
    /// <para>
    /// This should throw if connection fails but if OnInitialOpen is not called, this is also
    /// considered as a failure.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor configuration.</param>
    /// <param name="configuration">The configuration to consider.</param>
    /// <param name="cancel">The cancellation token that should be honored.</param>
    /// <returns>The awaitable.</returns>
    protected abstract Task DoInitialOpenAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration, CancellationToken cancel );

    /// <summary>
    /// Must be called by <see cref="DoInitialOpenAsync(IActivityMonitor, CommunicationChannelConfiguration, CancellationToken)"/> on success.
    /// </summary>
    /// <param name="input">The input pipe.</param>
    /// <param name="output">The output pipe.</param>
    /// <param name="newReaderBehavior">Optional replacement of the reader behavior.</param>
    /// <param name="newWriterBehavior">Optional replacement of the writer behavior.</param>
    protected void OnInitialOpen( PipeReader input,
                                  PipeWriter output,
                                  IStablePipeBehavior? newReaderBehavior = null,
                                  IStablePipeBehavior? newWriterBehavior = null )
    {
        Throw.CheckArgument( input != null && output != null );
        _input = input;
        _output = output;
        if( newReaderBehavior != null ) _readerBehavior = newReaderBehavior;
        if( newWriterBehavior != null ) _writerBehavior = newWriterBehavior;
    }

    /// <summary>
    /// Dynamic reconfiguration of this channel implementation.
    /// This method is called only if <see cref="CommunicationChannelConfiguration.CanDynamicReconfigureWith(CommunicationChannelConfiguration)"/>
    /// returned true and it must succeed (or logs a warning).
    /// <para>
    /// <para>
    /// This method can be called while a read and/or a write is pending. Whether the reconfiguration interfere
    /// with any current read/write or should apply to the next operations is implementation dependent.
    /// </para>
    /// There is no cancellation token and this is by design: dynamic reconfiguration, when possible, should
    /// be handled quickly or not at all. If timeout may occur, it's up to the channel implementation
    /// to take care of it and decide the maximum amount of time that is acceptable.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The new configuration.</param>
    internal protected abstract ValueTask DynamicReconfigureAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration );

    /// <summary>
    /// Must release/close any external resources.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The awaitable.</returns>
    public abstract ValueTask DisposeAsync( IActivityMonitor monitor );
}
