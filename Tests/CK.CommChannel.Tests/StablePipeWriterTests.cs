using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public partial class StablePipeWriterTests
{
    [Test]
    public async Task completing_the_inner_pipe_completes_by_default_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( completing_the_inner_pipe_completes_by_default_Async ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var writer = new StablePipeWriter();
        writer.SetWriter( pipe.Writer );

        MessageSender.WriteLine( writer, "Line" );
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();

        var r = await writer.FlushAsync( default );
        r.IsCompleted.ShouldBeTrue();
        writer.IsCompleted.ShouldBeTrue();

        r = await writer.FlushAsync( default );
        r.IsCompleted.ShouldBeTrue();
    }

    [Test]
    public async Task timeout_throws_TimeoutException_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( timeout_throws_TimeoutException_Async ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var slow = new SlowPipeWriter( pipe.Writer ) { Delay = 100 };
        var writer = new StablePipeWriter() { DefaultTimeout = 30 };
        writer.SetWriter( slow );

        MessageSender.WriteLine( writer, "Line..." );
        await Util.Awaitable( () => writer.FlushAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();

        MessageSender.WriteLine( writer, "...will be here." );
        await Util.Awaitable( () => writer.FlushAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();

        MessageSender.WriteLine( writer, "Because the inner buffer is kept." );
        await Util.Awaitable( () => writer.FlushAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();

        slow.Delay = 0;
        await MessageSender.SendLineAsync( writer, "Hello!" );
        await Util.Awaitable( () => writer.FlushAsync( default ).AsTask() ).ShouldNotThrowAsync();

        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync()).ShouldBe( "Line..." );
        (await lineReader.ReadNextAsync()).ShouldBe( "...will be here." );
        (await lineReader.ReadNextAsync()).ShouldBe( "Because the inner buffer is kept." );
        (await lineReader.ReadNextAsync()).ShouldBe( "Hello!" );
    }

    [Test]
    public async Task TimeoutException_just_like_any_exception_can_return_a_CanceledResult_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( TimeoutException_just_like_any_exception_can_return_a_CanceledResult_Async ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var slow = new SlowPipeWriter( pipe.Writer ) { Delay = 100 };
        var errorHandler = new ConfigurableStablePipeBehavior { OnErrorAction = OnErrorAction.Cancel };
        var writer = new StablePipeWriter( errorHandler ) { DefaultTimeout = 30 };
        writer.SetWriter( slow );

        MessageSender.WriteLine( writer, "Line..." );
        var r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeTrue();

        MessageSender.WriteLine( writer, "...will be here." );
        r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeTrue();

        MessageSender.WriteLine( writer, "Because the inner buffer is kept." );
        r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeTrue();

        slow.Delay = 0;
        await MessageSender.SendLineAsync( writer, "Hello!" );
        r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeFalse();

        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync()).ShouldBe( "Line..." );
        (await lineReader.ReadNextAsync()).ShouldBe( "...will be here." );
        (await lineReader.ReadNextAsync()).ShouldBe( "Because the inner buffer is kept." );
        (await lineReader.ReadNextAsync()).ShouldBe( "Hello!" );
    }

    [Test]
    public async Task by_default_CancelPendingFlush_simply_waits_for_next_call_to_FlushAsync()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( by_default_CancelPendingFlush_simply_waits_for_next_call_to_FlushAsync ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var writer = new StablePipeWriter();
        writer.SetWriter( pipe.Writer );

        MessageSender.WriteLine( writer, "Line..." );
        writer.CancelPendingFlush();
        var r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeTrue();

        r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeFalse();

        _ = MessageSender.SendLineAsync( writer, "<End>" );

        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync()).ShouldBe( "Line..." );
        (await lineReader.ReadNextAsync()).ShouldBe( "<End>" );
    }

    [Test]
    public async Task CancelPendingFlush_can_be_skipped_by_ReturnInnerCanceledResult_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( CancelPendingFlush_can_be_skipped_by_ReturnInnerCanceledResult_Async ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var writer = new StablePipeWriter( new ConfigurableStablePipeBehavior { InnerCanceledResult = false } );
        writer.SetWriter( pipe.Writer );

        MessageSender.WriteLine( writer, "Line..." );
        writer.CancelPendingFlush();
        // One wait is enough and it is not canceled.
        var r = await writer.FlushAsync( default );
        r.IsCanceled.ShouldBeFalse();

        _ = MessageSender.SendLineAsync( writer, "<End>" );

        var lineReader = new StringLineMessageReader( pipe.Reader, Encoding.ASCII );
        (await lineReader.ReadNextAsync()).ShouldBe( "Line..." );
        (await lineReader.ReadNextAsync()).ShouldBe( "<End>" );
    }

    [Test]
    public async Task OnDataWritten_event_Async()
    {
        using var gLog = TestHelper.Monitor.OpenInfo( nameof( OnDataWritten_event_Async ) );
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var writer = new StablePipeWriter( new ConfigurableStablePipeBehavior { InnerCanceledResult = false } );
        writer.SetWriter( pipe.Writer );
        var linesSent = new List<string>();
        writer.OnDataWritten += OnDataWritten;

        await MessageSender.SendLineAsync( writer, "Line...in one write." );
        await MessageSender.SendLineAsync( writer, "ABCDEFGH", bytePerByte: true );

        linesSent.ShouldBe( new[] { "Line...in one write.\r\n", "A", "B", "C", "D", "E", "F", "G", "H", "\r", "\n" } );

        void OnDataWritten( ReadOnlySpan<byte> span, StablePipeWriter arg )
        {
            string line = Encoding.ASCII.GetString( span );
            TestHelper.Monitor.Info( $"Data Written: {line}" );
            linesSent.Add( line );
        }

    }

}

