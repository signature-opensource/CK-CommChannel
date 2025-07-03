using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class LineMessageReaderTests
{
    [Test]
    public async Task Pipe_simple_reading_lines_Async()
    {
        var endPoint = MemoryChannel.AllocatePipeChannel( "Test" );
        try
        {
            await ReadLinesTestAsync( endPoint );
        }
        finally
        {
            await MemoryChannel.DeallocateAsync( "Test" );
        }
    }

    [Test]
    public async Task NetworkStream_simple_reading_lines_Async()
    {
        var endPoint = await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );
        try
        {
            await ReadLinesTestAsync( endPoint );
        }
        finally
        {
            await MemoryChannel.DeallocateAsync( "Test" );
        }
    }

    static async Task ReadLinesTestAsync( IPipeChannel endPoint )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = "Test", DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        MessageSender.WriteLine( endPoint.Input, "Message 1", "\r" );
        MessageSender.WriteLine( endPoint.Input, "Message 2" );
        MessageSender.WriteLine( endPoint.Input, "Message 3", "\r" );
        MessageSender.WriteLine( endPoint.Input, "Message 4" );
        await endPoint.Input.FlushAsync();

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        (await reader.ReadNextAsync()).ShouldBe( "Message 1" );
        (await reader.ReadNextAsync()).ShouldBe( "Message 2" );
        (await reader.ReadNextAsync()).ShouldBe( "Message 3" );
        (await reader.ReadNextAsync()).ShouldBe( "Message 4" );
        await MessageSender.SendLineAsync( endPoint.Input, "Message 5" );
        (await reader.ReadNextAsync()).ShouldBe( "Message 5" );
    }

    [Test]
    public async Task Pipe_CancelPendingRead_returns_the_EmptyMessage_Async()
    {
        var endPoint = MemoryChannel.AllocatePipeChannel( "Test" );
        await CancelPendingReadTestAsync( endPoint );
    }

    [Test]
    public async Task NetworkStream_CancelPendingRead_returns_the_EmptyMessage_Async()
    {
        var endPoint = await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );
        await CancelPendingReadTestAsync( endPoint );
    }


    static async Task CancelPendingReadTestAsync( IPipeChannel endPoint )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = "Test", DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );

        _ = Task.Run( async () =>
        {
            await Task.Delay( 100 );
            channel.Reader.CancelPendingRead();
        } );

        (await reader.ReadNextAsync()).ShouldBeNull();

        await MessageSender.SendLineAsync( endPoint.Input, "Message 1" );

        (await reader.ReadNextAsync()).ShouldBe( "Message 1" );

        reader.IsCompleted.ShouldBeFalse();

        _ = Task.Run( async () =>
        {
            await Task.Delay( 100 );
            channel.Reader.CancelPendingRead();
        } );

        (await reader.ReadNextAsync()).ShouldBeNull();
        reader.IsCompleted.ShouldBeFalse();

        await MemoryChannel.DeallocateAsync( "Test" );

        // If AutoReconnect is true, we'll wait indefinitely since there's no timeout.
        channel.AutoReconnect = false;
        await Util.Awaitable( () => reader.ReadNextAsync().AsTask() ).ShouldThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Pipe_completing_returns_the_EmptyMessage_Async()
    {
        var endPoint = MemoryChannel.AllocatePipeChannel( "Test" );
        await CompleteOrDisposeTestAsync( endPoint );
    }

    [Test]
    public async Task NetworkStream_disposing_returns_the_EmptyMessage_Async()
    {
        var endPoint = await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );
        await CompleteOrDisposeTestAsync( endPoint );
    }

    static async Task CompleteOrDisposeTestAsync( IPipeChannel endPoint )
    {
        var config = new MemoryChannelConfiguration() { EndPointName = "Test", DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        var reader = new StringLineMessageReader( channel.Reader, Encoding.ASCII );
        MessageSender.WriteLine( endPoint.Input, "1" );
        MessageSender.WriteLine( endPoint.Input, "2" );
        MessageSender.WriteLine( endPoint.Input, "3" );
        await endPoint.Input.FlushAsync();

        TestHelper.Monitor.Info( "Waiting for ReadNextAsync to be Message 1." );
        (await reader.ReadNextAsync()).ShouldBe( "1" );
        (await reader.ReadNextAsync()).ShouldBe( "2" );
        (await reader.ReadNextAsync()).ShouldBe( "3" );
        TestHelper.Monitor.Info( "Closing the end point in 150 ms." );
        _ = Task.Run( async () =>
        {
            await Task.Delay( 100 );
            await MemoryChannel.DeallocateAsync( "Test" );
        } );

        // If AutoReconnect is true, this will wait indefinitely since we have no timeout.
        channel.AutoReconnect = false;
        TestHelper.Monitor.Info( "Waiting for ReadNextAsync to be null (the empty message)." );
        // Note: Race condition here.
        // This MAY throw if the read is done on a ROSequence obtained before the close because
        // AutoReconnect is false here.
        // This is unfortunate (for this test) but "normal": when AutoReconnect is false, read or write
        // exceptions bubble up to the caller.
        (await reader.ReadNextAsync()).ShouldBeNull();
        reader.IsCompleted.ShouldBeTrue();

        (await reader.ReadNextAsync()).ShouldBeNull();
        reader.IsCompleted.ShouldBeTrue();
    }

}
