using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;
[TestFixture]
public class AutoReconnectionTests
{
    public enum FailureConfiguration
    {
        Never,
        Low,
        High,
    }

    /// <summary>
    /// Creates the random generator that paces one loop: seed 0 means "not reproducible"
    /// (<see cref="Random.Shared"/>), any other seed gives a reproducible sequence.
    /// <para>
    /// Warning: each loop must call this for itself. <see cref="Random.Shared"/> is thread safe,
    /// a seeded <see cref="Random"/> is not, so sharing one instance across the loops would corrupt
    /// its state.
    /// </para>
    /// </summary>
    static Random CreateRandom( int seed ) => seed == 0 ? Random.Shared : new Random( seed );

    /// <summary>
    /// Derives the seed of one loop from the test's seed, preserving the meaning of 0.
    /// </summary>
    static int SeedFor( int seed, int loop ) => seed == 0 ? 0 : seed + loop;

    // The message loops below are paced with Task.Delay so that the failure injector gets the chance
    // to cut the connection mid-stream: the run is intrinsically a few seconds long and every one of
    // those delays stretches on a loaded machine.
    // Warning: this budget is a guard against a genuine hang, not an expectation about how fast the
    // run is. Tightening it to a duration close to a normal run makes the test fail on scheduling.
    [CancelAfter( 120_000 )]
    [TestCase( 3712, "DelimitedMessages", FailureConfiguration.Never, "UsePipe" )]
    [TestCase( 3712, "LineMessages", FailureConfiguration.Never, "UseNetworkStream" )]
    [TestCase( 3712, "DelimitedMessages", FailureConfiguration.Low, "UseNetworkStream" )]
    // Seed 0 on purpose: this one runs on fresh randomness every time, which is what makes it a
    // standing fuzz of the reconnection path rather than one fixed scenario.
    [TestCase( 0, "DelimitedMessages", FailureConfiguration.High, "UseNetworkStream" )]
    public async Task AutoReconnection_works_Async( int seed, string delimitedMessages, FailureConfiguration failure, string channelType, CancellationToken cancel )
    {
        bool usePipe = channelType == "UsePipe";

        await using var ep = await TestMemoryEndPoint.AllocateAsync( usePipe );

        MemoryChannelConfiguration config1 = new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true };
        var channel1 = CommunicationChannel.Create( TestHelper.Monitor, config1 );
        // Registered before the wait: registering after it is a race against the initial Connected
        // event, which is raised just after ConnectionStatus flips.
        var tracker1 = new ConnectionAvailabilityTracker( channel1 );
        await channel1.WaitForConnectionStatusAsync( ConnectionAvailability.Connected, cancel );

        MemoryChannelConfiguration config2 = new MemoryChannelConfiguration { EndPointName = ep.Name, AutoReconnect = true, Reverted = true };
        var channel2 = CommunicationChannel.Create( TestHelper.Monitor, config2 );
        var tracker2 = new ConnectionAvailabilityTracker( channel2 );
        await channel2.WaitForConnectionStatusAsync( ConnectionAvailability.Connected, cancel );

        try
        {
            var readDone = new CancellationTokenSource();

            Task? failureTask = null;
            if( failure != FailureConfiguration.Never )
            {
                failureTask = Task.Run( async () =>
                {
                    var rnd = CreateRandom( seed );
                    while( !readDone.IsCancellationRequested )
                    {
                        await Task.Delay( rnd.Next( 50 ), cancel );
                        if( rnd.Next( 100 ) < (failure == FailureConfiguration.High ? 40 : 10) )
                        {
                            ActivityMonitor.StaticLogger.Debug( $"Cutting and restoring the '{ep.Name}' MemoryChannel." );
                            await ep.ReconnectAsync( usePipe );
                            await Task.Delay( 200, cancel );
                        }
                    }
                }, cancel );
            }

            bool useDelimited = delimitedMessages == "DelimitedMessages";
            // A distinct seed per loop: with a single one the four loops would all follow the very
            // same delay sequence, which is not what the paced loops are here for.
            var receive2 = ReadMessagesAsync( channel2, SeedFor( seed, 1 ), useDelimited, cancel );
            var send1 = SendMessagesAsync( channel1, SeedFor( seed, 2 ), useDelimited, receive2, cancel );
            var receive1 = ReadMessagesAsync( channel1, SeedFor( seed, 3 ), useDelimited, cancel );
            var send2 = SendMessagesAsync( channel2, SeedFor( seed, 4 ), useDelimited, receive1, cancel );

            await send1;
            await send2;
            var messages1 = await receive1;
            var messages2 = await receive2;
            await readDone.CancelAsync();
            if( failureTask != null ) await failureTask;

            var expectedMessages = Enumerable.Range( 0, 100 ).Select( i => $"Message {i}" ).Append( "End of Messages" ).ToArray();

            using( TestHelper.Monitor.OpenInfo( "Conclusions" ) )
            {
                int dupCount = CheckMissingMessagesAndGetDuplicateMessageCount( messages1, expectedMessages, channel1 );
                CheckEvents( tracker1.Events, failure );

                dupCount += CheckMissingMessagesAndGetDuplicateMessageCount( messages2, expectedMessages, channel2 );
                CheckEvents( tracker2.Events, failure );

                dupCount.ShouldBe( 0, "There must never be any duplicates." );
            }

            static void CheckEvents( ConnectionAvailability[] events, FailureConfiguration f )
            {
                if( f != FailureConfiguration.Never )
                {
                    events.ShouldContain( e => e != ConnectionAvailability.Connected, "Injected failures must have degraded the connection." );
                }
                else
                {
                    // Whether the initial Connected event is seen depends on how quickly the tracker
                    // is registered, so only degradations are asserted on: without injected failures
                    // there must be none.
                    events.ShouldAllBe( e => e == ConnectionAvailability.Connected );
                }
            }

        }
        finally
        {
            await ep.DeallocateAsync();
            await channel1.DisposeAsync();
            await channel2.DisposeAsync();
        }

        static int CheckMissingMessagesAndGetDuplicateMessageCount( List<string?> messages, string[] expectedMessages, CommunicationChannel c )
        {
            int emptyCount = messages.RemoveWhereAndReturnsRemoved( m => m == null ).Count();
            if( emptyCount > 0 )
            {
                TestHelper.Monitor.Warn( $"{c.Name} received {emptyCount} empty messages." );
            }
            var duplicates = messages.GroupBy( Util.FuncIdentity )
                                     .Where( g => g.Count() > 1 )
                                     .Select( g => $"{g.Key} ({g.Count()})" )
                                     .ToList();
            if( duplicates.Count > 0 )
            {
                TestHelper.Monitor.Warn( $"{c.Name} received duplicate messages: {duplicates.Concatenate()}" );
            }
            var missed = expectedMessages.Except( messages ).ToList();
            if( missed.Count != 0 )
            {
                TestHelper.Monitor.Warn( $"Missed messages by {c.Name}: {missed.Concatenate()}" );
            }
            else
            {
                TestHelper.Monitor.Warn( $"All messages have been received by {c.Name}." );
            }
            return duplicates.Count;
        }
    }

    /// <summary>
    /// Sends 100 messages, then repeats 'End of Messages' until the peer's reader has seen it.
    /// </summary>
    static Task SendMessagesAsync( CommunicationChannel c,
                                   int seed,
                                   bool useDelimited,
                                   Task<List<string?>> receive1,
                                   CancellationToken cancel )
    {
        MessageWriterBase<string> writer = useDelimited
                                            ? new StringDelimitedMessageWriter( c.Writer, Encoding.ASCII, (byte)'#', (byte)';' )
                                            : new StringLineMessageWriter( c.Writer, Encoding.ASCII );
        var writeLoopAsync = Task.Run( async () =>
        {
            try
            {
                var rnd = CreateRandom( seed );
                int mNum = 0;
                while( mNum < 100 )
                {
                    var message = $"Message {mNum++}";
                    await writer.WriteAsync( message, cancel );
                    ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: {message}" );
                    await Task.Delay( rnd.Next( 40 ), cancel );
                }

                ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: 'End of Messages'" );
                // Warning: these two writes must honor the cancellation token. Neither the loop
                // condition below nor a WriteAsync on a non cancelable token blocks once the test has
                // been canceled, so passing 'default' here turns this retry loop into a hot spin that
                // floods the logs and outlives the [CancelAfter] budget.
                await writer.WriteAsync( $"End of Messages", cancel );
                while( !await receive1.WaitForTaskCompletionAsync( 100, cancel ) )
                {
                    ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: resending 'End of Messages'" );
                    await writer.WriteAsync( $"End of Messages", cancel );
                }
            }
            catch( Exception ex )
            {
                ActivityMonitor.StaticLogger.Error( $"{c.Name} - Error while writing messages.", ex );
                throw;
            }
        }, cancel );
        return writeLoopAsync;
    }

    static async Task<List<string?>> ReadMessagesAsync( CommunicationChannel c, int seed, bool useDelimited, CancellationToken cancel )
    {
        var messages = new List<string?>();
        MessageReaderBase<string?> reader = useDelimited
                                                ? new StringDelimitedMessageReader( c.Reader, Encoding.ASCII, (byte)'#', (byte)';' )
                                                : new StringLineMessageReader( c.Reader, Encoding.ASCII, "\r\n" );
        var readLoopAsync = Task.Run( async () =>
        {
            try
            {
                var rnd = CreateRandom( seed );
                string? message;
                while( (message = await reader.ReadNextAsync( cancel )) != "End of Messages" )
                {
                    ActivityMonitor.StaticLogger.Trace( $"{c.Name} reader: {message}" );
                    messages.Add( message );
                    await Task.Delay( rnd.Next( 40 ), cancel );
                }
                ActivityMonitor.StaticLogger.Trace( $"{c.Name} reader: {message}" );
                messages.Add( message );
            }
            catch( Exception ex )
            {
                ActivityMonitor.StaticLogger.Error( $"{c.Name} - Error while reading messages.", ex );
                throw;
            }
        }, cancel );
        await readLoopAsync;
        return messages;
    }
}
