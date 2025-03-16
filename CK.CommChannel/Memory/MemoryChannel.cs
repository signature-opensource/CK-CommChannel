using CK.Core;
using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel;

/// <summary>
/// Supports <see cref="CommunicationChannel"/> that are directly bound to either
/// a read-write stream (a <see cref="NetworkStream"/> from a local TCP server) or a simple
/// memory <see cref="Pipe"/>.
/// <para>
/// Lifetime of these local endpoints is under control of this MemoryChannel static methods:
/// <list type="bullet">
///     <item>
///     Use <see cref="AllocateNetworkStreamChannelAsync(string, IStablePipeBehavior?, IStablePipeBehavior?)"/> or
///     <see cref="AllocatePipeChannel(string, PipeOptions?, PipeOptions?, IStablePipeBehavior?, IStablePipeBehavior?)"/>
///     to open an endpoint.
///     </item>
///     <item>
///     Disposing the ICommunicationChannel (actually the <see cref="ChannelImpl"/>) is a no-op as it simply
///     disconect itself from the endpoint.
///     To release the endpoint, <see cref="DeallocateAsync(string, bool)"/> method must be used.
///     </item>
/// </list>
/// </para>
/// </summary>
public sealed partial class MemoryChannel : ChannelImpl
{
    static readonly ConcurrentDictionary<string, IAsyncDisposable> _endpoints = new();

    internal MemoryChannel()
        : base( IStablePipeBehavior.Default, IStablePipeBehavior.Default )
    {
    }

    /// <inheritdoc />
    protected override async Task DoInitialOpenAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration, CancellationToken cancel )
    {
        var c = (MemoryChannelConfiguration)configuration;
        Throw.DebugAssert( "CheckValid did this.", c.EndPointName != null );
        if( !_endpoints.TryGetValue( c.EndPointName, out var endPoint ) )
        {
            await Task.Delay( c.ConnectionTimeout, cancel ).ConfigureAwait( false );
            // This throws a KeyNotFoundException if not found and this is fine.
            endPoint = _endpoints[c.EndPointName];
        }
        if( endPoint is PipeChannel p )
        {
            OnInitialOpen( c.Reverted ? p._output.Reader : p._input.Reader,
                           c.Reverted ? p._input.Writer : p._output.Writer,
                           p.ReaderBehavior,
                           p.WriterBehavior );
        }
        else 
        {
            var n = (NetworkStreamChannel)endPoint;
            OnInitialOpen( PipeReader.Create( c.Reverted ? n._endPointStream : n._channelStream, _pipeReaderOptions ),
                           PipeWriter.Create( c.Reverted ? n._endPointStream : n._channelStream, _pipeWriterOptions ),
                           n.ReaderBehavior,
                           n.WriterBehavior );
        }
    }

    /// <inheritdoc />
    protected internal override ValueTask DynamicReconfigureAsync( IActivityMonitor monitor, CommunicationChannelConfiguration configuration ) => default;

    /// <inheritdoc />
    public override ValueTask DisposeAsync( IActivityMonitor monitor ) => default;

    /// <summary>
    /// Gets the default options used by <see cref="AllocatePipeChannel"/>:
    /// <see cref="PipeOptions.UseSynchronizationContext"/> is false.
    /// </summary>
    public static readonly PipeOptions DefaultPipeOptions = new PipeOptions( useSynchronizationContext: false );

    /// <summary>
    /// Allocates a <see cref="PipeChannel"/> endpoint and returns it.
    /// Throws an <see cref="InvalidOperationException"/> if the endpoint is already allocated.
    /// </summary>
    /// <param name="name">The name of the endpoint.</param>
    /// <param name="inputOptions">Options for the <see cref="PipeChannel.Input"/>. Defaults to <see cref="DefaultPipeOptions"/>.</param>
    /// <param name="outputOptions">Options for the <see cref="PipeChannel.Output"/>. Defaults to <see cref="DefaultPipeOptions"/>.</param>
    /// <param name="readerBehavior">
    /// Optional reader behavior for any <see cref="CommunicationChannel"/>'s <see cref="StablePipeReader"/>
    /// bound to this <paramref name="name"/>.
    /// Defaults to <see cref="IStablePipeBehavior.Default"/>.
    /// </param>
    /// <param name="writerBehavior">
    /// Optional writer behavior for the <see cref="CommunicationChannel"/>'s <see cref="StablePipeWriter"/>
    /// bound to this <paramref name="name"/>.
    /// Defaults to <see cref="IStablePipeBehavior.Default"/>.
    /// </param>
    /// <returns>A pipe channel.</returns>
    public static IPipeChannel AllocatePipeChannel( string name,
                                                    PipeOptions? inputOptions = null,
                                                    PipeOptions? outputOptions = null,
                                                    IStablePipeBehavior? readerBehavior = null,
                                                    IStablePipeBehavior? writerBehavior = null )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        var e = new PipeChannel( inputOptions ?? DefaultPipeOptions,
                                 outputOptions ?? DefaultPipeOptions,
                                 readerBehavior ?? IStablePipeBehavior.Default,
                                 writerBehavior ?? IStablePipeBehavior.Default );
        if( !_endpoints.TryAdd( name, e ) )
        {
            Throw.InvalidOperationException( $"Endpoint named '{name}' is already allocated." );
        }
        return e;
    }

    /// <summary>
    /// Allocates endpoint bound to a local TCP server.
    /// Throws an <see cref="InvalidOperationException"/> if the endpoint is already allocated.
    /// </summary>
    /// <param name="name">The name of the endpoint.</param>
    /// <param name="readerBehavior">
    /// Optional reader behavior for any <see cref="CommunicationChannel"/>'s <see cref="StablePipeReader"/>
    /// bound to this <paramref name="name"/>.
    /// Defaults to <see cref="IStablePipeBehavior.Default"/>.
    /// </param>
    /// <param name="writerBehavior">
    /// Optional writer behavior for the <see cref="CommunicationChannel"/>'s <see cref="StablePipeWriter"/>
    /// bound to this <paramref name="name"/>.
    /// Defaults to <see cref="IStablePipeBehavior.Default"/>.
    /// </param>
    public static async Task<IPipeChannel> AllocateNetworkStreamChannelAsync( string name,
                                                                              IStablePipeBehavior? readerBehavior = null,
                                                                              IStablePipeBehavior? writerBehavior = null )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        var c = await NetworkStreamChannel.CreateAsync( readerBehavior ?? IStablePipeBehavior.Default, writerBehavior ?? IStablePipeBehavior.Default ).ConfigureAwait( false );
        if( !_endpoints.TryAdd( name, c ) )
        {
            await c.DisposeAsync().ConfigureAwait( false );
            Throw.InvalidOperationException( $"Endpoint named '{name}' is already allocated." );
        }
        return c;
    }

    /// <summary>
    /// Deallocate an existing end point if it exists. Does nothing if the endpoint is not (or no more) allocated.
    /// </summary>
    /// <param name="name">The name of the endpoint.</param>
    /// <param name="release">
    /// False to not complete the <see cref="IPipeChannel"/>'s pipes or dispose the network stream channel.
    /// </param>
    public static ValueTask DeallocateAsync( string name, bool release = true )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        if( _endpoints.TryRemove( name, out var obj ) && release )
        {
            return obj.DisposeAsync();
        }
        return default;
    }
}
