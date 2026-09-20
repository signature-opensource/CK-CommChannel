using System;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

/// <summary>
/// A <see cref="MemoryChannel"/> endpoint allocated under a name that is unique to the test that
/// created it, and deallocated whatever happens.
/// <para>
/// Warning: do not go back to a shared endpoint name, and do not deallocate on the happy path only.
/// <see cref="MemoryChannel"/> endpoints live in a process wide dictionary, so a name left allocated
/// by a failing test makes every later test that allocates it fail with "Endpoint named '...' is
/// already allocated." — one failure then cascades into a fixture-wide one, and the tests that report
/// it are not the ones that are broken. Unique names remove the coupling, <c>await using</c> removes
/// the leak.
/// </para>
/// </summary>
sealed class TestMemoryEndPoint : IAsyncDisposable
{
    static int _count;

    TestMemoryEndPoint( string name, IPipeChannel endPoint )
    {
        Name = name;
        EndPoint = endPoint;
    }

    /// <summary>
    /// Gets the unique endpoint name to use in <see cref="MemoryChannelConfiguration.EndPointName"/>.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the allocated endpoint. Replaced by <see cref="ReconnectAsync(bool)"/>.
    /// </summary>
    public IPipeChannel EndPoint { get; private set; }

    /// <summary>
    /// Gets the <see cref="IPipeChannel.Input"/> of the <see cref="EndPoint"/>.
    /// </summary>
    public PipeWriter Input => EndPoint.Input;

    public static TestMemoryEndPoint AllocatePipe( PipeOptions? inputOptions = null, PipeOptions? outputOptions = null )
    {
        var name = NewName();
        return new TestMemoryEndPoint( name, MemoryChannel.AllocatePipeChannel( name, inputOptions, outputOptions ) );
    }

    public static async Task<TestMemoryEndPoint> AllocateNetworkStreamAsync()
    {
        var name = NewName();
        return new TestMemoryEndPoint( name, await MemoryChannel.AllocateNetworkStreamChannelAsync( name ) );
    }

    /// <summary>
    /// Allocates a pipe or a network stream endpoint.
    /// </summary>
    public static async Task<TestMemoryEndPoint> AllocateAsync( bool usePipe )
    {
        return usePipe ? AllocatePipe() : await AllocateNetworkStreamAsync();
    }

    /// <summary>
    /// Creates a configuration bound to this endpoint.
    /// </summary>
    public MemoryChannelConfiguration CreateConfiguration( bool reverted = false )
    {
        return new MemoryChannelConfiguration { EndPointName = Name, Reverted = reverted };
    }

    /// <summary>
    /// Deallocates and allocates again under the same <see cref="Name"/>: this is how a test
    /// simulates a disconnection followed by a reconnection.
    /// </summary>
    public async Task ReconnectAsync( bool usePipe )
    {
        await DeallocateAsync();
        EndPoint = usePipe
                    ? MemoryChannel.AllocatePipeChannel( Name )
                    : await MemoryChannel.AllocateNetworkStreamChannelAsync( Name );
    }

    /// <summary>
    /// Deallocates the endpoint. Idempotent: tests that deallocate explicitly (to simulate a
    /// disconnection) can still be wrapped in an <c>await using</c>.
    /// </summary>
    public ValueTask DeallocateAsync() => MemoryChannel.DeallocateAsync( Name );

    public ValueTask DisposeAsync() => DeallocateAsync();

    static string NewName() => $"Test-{Interlocked.Increment( ref _count )}";
}
