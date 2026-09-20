using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class LineMessageReaderTests
{
    [Test]
    [CancelAfter( 30_000 )]
    public async Task Pipe_simple_reading_lines_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        await ReadLinesTestAsync( ep, cancel );
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task NetworkStream_simple_reading_lines_Async( CancellationToken cancel )
    {
        await using var ep = await TestMemoryEndPoint.AllocateNetworkStreamAsync();
        await ReadLinesTestAsync( ep, cancel );
    }

    static async Task ReadLinesTestAsync( TestMemoryEndPoint endPoint, CancellationToken cancel )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = endPoint.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        MessageSender.WriteLine( endPoint.Input, "Message 1", "\r" );
        MessageSender.WriteLine( endPoint.Input, "Message 2" );
        MessageSender.WriteLine( endPoint.Input, "Message 3", "\r" );
        MessageSender.WriteLine( endPoint.Input, "Message 4" );
        await endPoint.Input.FlushAsync( cancel );

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 1" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 2" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 3" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 4" );
        await MessageSender.SendLineAsync( endPoint.Input, "Message 5" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 5" );
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task Pipe_CancelPendingRead_returns_the_EmptyMessage_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        await CancelPendingReadTestAsync( ep, cancel );
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task NetworkStream_CancelPendingRead_returns_the_EmptyMessage_Async( CancellationToken cancel )
    {
        await using var ep = await TestMemoryEndPoint.AllocateNetworkStreamAsync();
        await CancelPendingReadTestAsync( ep, cancel );
    }


    static async Task CancelPendingReadTestAsync( TestMemoryEndPoint endPoint, CancellationToken cancel )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = endPoint.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        // Nothing has been sent, so the read can only be released by CancelPendingRead: issuing the
        // read and cancelling it from here needs no delay at all.
        // Warning: cancelling from a task that first waits a fixed delay is a bet that the read has
        // started by then, and nothing here guarantees it.
        var read = reader.ReadNextAsync( cancel ).AsTask();
        channel.Reader.CancelPendingRead();
        (await read).ShouldBeNull();

        await MessageSender.SendLineAsync( endPoint.Input, "Message 1" );

        (await reader.ReadNextAsync( cancel )).ShouldBe( "Message 1" );

        reader.IsCompleted.ShouldBeFalse();

        read = reader.ReadNextAsync( cancel ).AsTask();
        channel.Reader.CancelPendingRead();
        (await read).ShouldBeNull();
        reader.IsCompleted.ShouldBeFalse();

        await endPoint.DeallocateAsync();

        // If AutoReconnect is true, we'll wait indefinitely since there's no timeout.
        channel.AutoReconnect = false;
        await Util.Awaitable( () => reader.ReadNextAsync( cancel ).AsTask() ).ShouldThrowAsync<InvalidOperationException>();
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task Pipe_completing_returns_the_EmptyMessage_Async( CancellationToken cancel )
    {
        await using var ep = TestMemoryEndPoint.AllocatePipe();
        await CompleteOrDisposeTestAsync( ep, cancel );
    }

    [Test]
    [CancelAfter( 30_000 )]
    public async Task NetworkStream_disposing_returns_the_EmptyMessage_Async( CancellationToken cancel )
    {
        await using var ep = await TestMemoryEndPoint.AllocateNetworkStreamAsync();
        await CompleteOrDisposeTestAsync( ep, cancel );
    }

    static async Task CompleteOrDisposeTestAsync( TestMemoryEndPoint endPoint, CancellationToken cancel )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = endPoint.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );
        MessageSender.WriteLine( endPoint.Input, "1" );
        MessageSender.WriteLine( endPoint.Input, "2" );
        MessageSender.WriteLine( endPoint.Input, "3" );
        await endPoint.Input.FlushAsync( cancel );

        TestHelper.Monitor.Info( "Waiting for ReadNextAsync to be Message 1." );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "1" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "2" );
        (await reader.ReadNextAsync( cancel )).ShouldBe( "3" );

        // If AutoReconnect is true, this will wait indefinitely since we have no timeout.
        channel.AutoReconnect = false;

        // The read is issued before the endpoint is closed. ReadNextAsync runs synchronously up to the
        // inner ReadAsync, so the read is provably pending on an empty pipe when the close happens,
        // and everything sent has been consumed: nothing is buffered for the "read on a ROSequence
        // obtained before the close" race to trigger on.
        // Warning: closing from a task that first waits a fixed delay reintroduces both races.
        TestHelper.Monitor.Info( "Waiting for ReadNextAsync to be null (the empty message)." );
        var read = reader.ReadNextAsync( cancel ).AsTask();
        TestHelper.Monitor.Info( "Closing the end point." );
        await endPoint.DeallocateAsync();

        (await read).ShouldBeNull();
        reader.IsCompleted.ShouldBeTrue();

        (await reader.ReadNextAsync( cancel )).ShouldBeNull();
        reader.IsCompleted.ShouldBeTrue();
    }

}
