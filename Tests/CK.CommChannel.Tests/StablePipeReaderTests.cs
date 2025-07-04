using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class StablePipeReaderTests
{
    [TestCase( true )]
    [TestCase( false )]
    public async Task writing_and_reading_messages_Async( bool bytePerByte )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();
        reader.SetReader( pipe.Reader );
        var writeStop = new CancellationTokenSource();

        var w = StartWritingMessagesAsync( pipe.Writer, writeStop, bytePerByte ? -1 : 15, onEnd: () => reader.Close( true ) );
        var r = StartReadingMessagesAsync( reader );
        await Task.Delay( 1000 );
        TestHelper.Monitor.Info( "Signaling the stop CTS." );
        writeStop.Cancel();
        (await w).ShouldBeNull();
        (await r).ShouldBeNull();
    }

    [Test]
    public async Task timeout_throws_TimeoutException_Async()
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();
        reader.DefaultTimeout = 35;

        reader.SetReader( pipe.Reader );
        await Util.Awaitable( () => reader.ReadAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();

        await Util.Awaitable( () => reader.ReadAsync( default ).AsTask() ).ShouldThrowAsync<TimeoutException>();

        await MessageSender.SendLineAsync( pipe.Writer, "Hello" );
        await Util.Awaitable( async () =>
        {
            var r = await reader.ReadAsync( default );
            reader.AdvanceTo( r.Buffer.End );
        }
        ).ShouldNotThrowAsync();
        await reader.CompleteAsync();
    }


    [Test]
    public async Task TimeoutException_just_like_any_exception_can_return_a_CanceledResult_Async()
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var slow = new SlowPipeReader( pipe.Reader ) { Delay = 100 };
        var behavior = new ConfigurableStablePipeBehavior { OnErrorAction = OnErrorAction.Cancel };
        var reader = new StablePipeReader( behavior ) { DefaultTimeout = 30 };
        reader.SetReader( slow );

        _ = MessageSender.SendLineAsync( pipe.Writer, "Line 1" );
        _ = MessageSender.SendLineAsync( pipe.Writer, "Line 2" );

        var lineReader = new StringLineMessageReader( reader, Encoding.ASCII );

        (await lineReader.ReadNextAsync()).ShouldBeNull( "The EmptyMessage of the StringLineMessageReader is null." );

        slow.Delay = 0;
        (await lineReader.ReadNextAsync()).ShouldBe( "Line 1" );
        (await lineReader.ReadNextAsync()).ShouldBe( "Line 2" );
    }


    [Test]
    public async Task completing_the_inner_pipe_completes_by_default_Async()
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();
        reader.SetReader( pipe.Reader );

        var rT = reader.ReadAsync( default );
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
        var r = await rT;
        r.IsCompleted.ShouldBeTrue();
        reader.IsCompleted.ShouldBeTrue();

        r = await reader.ReadAsync( default );
        r.IsCompleted.ShouldBeTrue();
    }

    [TestCase( true, 0 )]
    [TestCase( false, 0 )]
    public async Task closing_and_setting_reader_Async( bool bytePerByte, int seed )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();
        reader.SetReader( pipe.Reader );
        var writeStop = new CancellationTokenSource();

        var w = StartWritingMessagesAsync( pipe.Writer, writeStop, bytePerByte ? -1 : 15, onEnd: () => reader.Close( true ) );
        var r = StartReadingMessagesAsync( reader );
        var k = StartCloserReopenerLoopAsync( reader, pipe.Reader, seed );
        await Task.Delay( 2000 );
        TestHelper.Monitor.Info( "Signaling the stop CTS." );
        writeStop.Cancel();
        (await w).ShouldBeNull();
        (await r).ShouldBeNull();
        (await k).ShouldBeNull();
    }

    [TestCase( true, 42 )]
    [TestCase( false, 3712 )]
    public async Task closing_setting_and_cancelling_reader_Async( bool bytePerByte, int seed )
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();
        reader.SetReader( pipe.Reader );
        var writeStop = new CancellationTokenSource();

        var w = StartWritingMessagesAsync( pipe.Writer, writeStop, bytePerByte ? -1 : 15, onEnd: () => reader.Close( true ) );
        var r = StartReadingMessagesAsync( reader );
        var k = StartCloserReopenerLoopAsync( reader, pipe.Reader, seed );
        var c = StartCancelLoopAsync( reader, seed );
        await Task.Delay( 2000 );
        TestHelper.Monitor.Info( "Signaling the stop CTS." );
        writeStop.Cancel();
        (await w).ShouldBeNull();
        (await r).ShouldBeNull();
        (await k).ShouldBeNull();
        (await c).ShouldBeNull();
    }

    [Test]
    public async Task reading_from_a_closed_reader_Async()
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();

        var writeStop = new CancellationTokenSource();
        var w = StartWritingMessagesAsync( pipe.Writer, writeStop, onEnd: () => reader.Close( true ) );
        var r = StartReadingMessagesAsync( reader );

        await Task.Delay( 250 );
        reader.SetReader( pipe.Reader );

        TestHelper.Monitor.Info( "Signaling the stop CTS." );
        writeStop.Cancel();
        (await w).ShouldBeNull();
        (await r).ShouldBeNull();
    }

    [Test]
    public async Task never_reading_from_a_closed_reader_Async()
    {
        var pipe = new Pipe( new PipeOptions( useSynchronizationContext: false ) );
        var reader = new StablePipeReader();

        var writeStop = new CancellationTokenSource();
        var w = StartWritingMessagesAsync( pipe.Writer, writeStop, onEnd: () => reader.Close( true ) );
        await Task.Delay( 50 );
        var r = StartReadingMessagesAsync( reader );

        TestHelper.Monitor.Info( "Signaling the stop CTS." );
        writeStop.Cancel();
        (await w).ShouldBeNull();
        (await r).ShouldBeNull();
    }

    static Task<Exception?> StartReadingMessagesAsync( StablePipeReader reader, bool receiveAllMessages = true )
    {
        return Task.Run( async () =>
        {
            var monitor = new ActivityMonitor( "Reading loop." );
            try
            {
                var lineReader = new StringLineMessageReader( reader, Encoding.ASCII );
                var expectedNum = 0;
                monitor.Info( "Starting Read loop." );
                while( !lineReader.IsCompleted )
                {
                    var result = await lineReader.ReadNextAsync();
                    if( result != null )
                    {
                        result.ShouldStartWith( "Message " );
                        monitor.Trace( $"Received line: '{result}'." );

                        var num = int.Parse( result.AsSpan( 8 ) );
                        if( num != expectedNum )
                        {
                            var m = $"Expected {expectedNum} but got {num}.";
                            if( receiveAllMessages )
                            {
                                Throw.InvalidDataException( m );
                            }
                            monitor.Warn( m );
                        }
                        expectedNum = num + 1;
                    }
                    else
                    {
                        monitor.Info( $"Got a null line." );
                    }
                }
                monitor.Info( "Read loop stopped." );
                return null;
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return ex;
            }
        } );
    }

    /// <summary>
    /// Sends "Message {i++}" from i = 0 until <paramref name="stop"/> is signaled.
    /// Negative delay sends the chars byte per byte.
    /// </summary>
    /// <param name="w">The writer.</param>
    /// <param name="stop">Stop token.</param>
    /// <param name="delay">Delay between lines. -1 to send lines byte per byte.</param>
    /// <param name="onEnd">Optional action at the end of the loop.</param>
    /// <returns>The exception that stopped the loop if any.</returns>
    static Task<Exception?> StartWritingMessagesAsync( PipeWriter w, CancellationTokenSource stop, int delay = 23, Action? onEnd = null )
    {
        return Task.Run( async () =>
        {
            var monitor = new ActivityMonitor( "Writing loop." );
            int i = 0;
            try
            {
                while( !stop.IsCancellationRequested )
                {
                    var line = $"Message {i++}";
                    monitor.Info( $"Sending '{line}'." );
                    await MessageSender.SendLineAsync( w, line, delay < 0 );
                    if( delay > 0 ) await Task.Delay( delay );
                }
                monitor.Info( "Stopping Write loop." );
                return null;
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return ex;
            }
            finally
            {
                onEnd?.Invoke();
            }
        } );
    }

    static Task<Exception?> StartCloserReopenerLoopAsync( StablePipeReader reader, PipeReader inner, int seed )
    {
        return Task.Run( async () =>
        {
            var rnd = seed == 0 ? Random.Shared : new Random( seed );
            var monitor = new ActivityMonitor( "Closer/Opener loop." );
            try
            {
                while( !reader.IsCompleted )
                {
                    await Task.Delay( rnd.Next( 50 ) );
                    monitor.Info( "Closing reader." );
                    reader.Close( false );
                    await Task.Delay( rnd.Next( 50 ) );
                    monitor.Info( "Restoring reader." );
                    reader.SetReader( inner );
                }
                monitor.Info( "Stopping Closer/Opener loop." );
                return null;
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return ex;
            }
        } );
    }

    static Task<Exception?> StartCancelLoopAsync( StablePipeReader reader, int seed )
    {
        return Task.Run( async () =>
        {
            var rnd = seed == 0 ? Random.Shared : new Random( seed*2 );
            var monitor = new ActivityMonitor( "Canceler loop." );
            try
            {
                while( !reader.IsCompleted )
                {
                    await Task.Delay( rnd.Next( 150 ) );
                    monitor.Info( "Canceling reader." );
                    reader.CancelPendingRead();
                }
                monitor.Info( "Stopping Canceler loop." );
                return null;
            }
            catch( Exception ex )
            {
                monitor.Error( ex );
                return ex;
            }
        } );
    }
}

