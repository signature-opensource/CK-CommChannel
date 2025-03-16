using CK.Core;
using FluentAssertions;
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

    [CancelAfter( 10000 )]
    [TestCase( 3712, "DelimitedMessages", FailureConfiguration.Never, "UsePipe" )]
    [TestCase( 3712, "LineMessages", FailureConfiguration.Never, "UseNetworkStream" )]
    [TestCase( 3712, "DelimitedMessages", FailureConfiguration.Low, "UseNetworkStream" )]
    [TestCase( 0, "DelimitedMessages", FailureConfiguration.High, "UseNetworkStream" )]
    public async Task AutoReconnection_works_Async( int seed, string delimitedMessages, FailureConfiguration failure, string channelType, CancellationToken cancel )
    {
        bool usePipe = channelType == "UsePipe";
        using var gLog = TestHelper.Monitor.OpenInfo( $"{nameof( AutoReconnection_works_Async )}({seed},{delimitedMessages},{failure},{channelType})" );

        var endPoint = usePipe
                        ? MemoryChannel.AllocatePipeChannel( "Test" )
                        : await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );

        MemoryChannelConfiguration config1 = new MemoryChannelConfiguration { EndPointName = "Test", AutoReconnect = true };
        var channel1 = CommunicationChannel.Create( TestHelper.Monitor, config1 );
        await Task.Delay( 100, cancel );
        channel1.ConnectionStatus.Should().Be( ConnectionAvailability.Connected );
        var tracker1 = new ConnectionAvailabilityTracker( channel1 );

        MemoryChannelConfiguration config2 = new MemoryChannelConfiguration { EndPointName = "Test", AutoReconnect = true, Reverted = true };
        var channel2 = CommunicationChannel.Create( TestHelper.Monitor, config2 );
        await Task.Delay( 100, cancel );
        channel2.ConnectionStatus.Should().Be( ConnectionAvailability.Connected );
        var tracker2 = new ConnectionAvailabilityTracker( channel2 );

        try
        {
            var readDone = new CancellationTokenSource();

            Task? failureTask = null;
            if( failure != FailureConfiguration.Never )
            {
                failureTask = Task.Run( async () =>
                {
                    var rnd = seed == 0 ? new Random( seed ) : Random.Shared;
                    while( !readDone.IsCancellationRequested )
                    {
                        await Task.Delay( rnd.Next( 50 ), cancel );
                        if( rnd.Next( 100 ) < (failure == FailureConfiguration.High ? 40 : 10) )
                        {
                            ActivityMonitor.StaticLogger.Debug( "Deallocating Test MemoryChannel." );
                            await MemoryChannel.DeallocateAsync( "Test" );
                            //await Task.Delay( rnd.Next( 50 ) );
                            ActivityMonitor.StaticLogger.Debug( "Restoring Test MemoryChannel." );
                            endPoint = usePipe
                                        ? MemoryChannel.AllocatePipeChannel( "Test" )
                                        : await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );
                            await Task.Delay( 200 );
                        }
                    }
                }, cancel );
            }

            bool useDelimited = delimitedMessages == "DelimitedMessages";
            var receive2 = ReadMessagesAsync( channel2, seed, useDelimited, cancel );
            var send1 = SendMessagesAsync( channel1, seed, useDelimited, receive2, cancel );
            var receive1 = ReadMessagesAsync( channel1, seed, useDelimited, cancel );
            var send2 = SendMessagesAsync( channel2, seed, useDelimited, receive1, cancel );

            await send1;
            await send2;
            var messages1 = await receive1;
            var messages2 = await receive2;
            readDone.Cancel();
            if( failureTask != null ) await failureTask;

            var expectedMessages = Enumerable.Range( 0, 100 ).Select( i => $"Message {i}" ).Append( "End of Messages" ).ToArray();

            using( TestHelper.Monitor.OpenInfo( "Conclusions" ) )
            {
                int dupCount = CheckMissingMessagesAndGetDuplicateMessageCount( messages1, expectedMessages, channel1 );
                CheckEvents( tracker1.Events, failure );

                dupCount += CheckMissingMessagesAndGetDuplicateMessageCount( messages2, expectedMessages, channel2 );
                CheckEvents( tracker2.Events, failure );

                dupCount.Should().Be( 0, "There must never be any duplicates." );
            }

            static void CheckEvents( ConnectionAvailability[] events, FailureConfiguration f )
            {
                if( f != FailureConfiguration.Never )
                {
                    events.Should().NotBeEmpty();
                }
                else
                {
                    events.Should().BeEmpty();
                }
            }

        }
        finally
        {
            await MemoryChannel.DeallocateAsync( "Test" );
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

    static Task SendMessagesAsync( CommunicationChannel c, int seed, bool useDelimited, Task<List<string?>> receive1, CancellationToken cancel )
    {
        MessageWriterBase<string> writer = useDelimited
                                            ? new StringDelimitedMessageWriter( c.Writer, Encoding.ASCII, (byte)'#', (byte)';' )
                                            : new StringLineMessageWriter( c.Writer, Encoding.ASCII );
        var writeLoopAsync = Task.Run( async () =>
        {
            try
            {
                var rnd = seed == 0 ? new Random( seed ) : Random.Shared;
                int mNum = 0;
                while( mNum < 100 )
                {
                    var message = $"Message {mNum++}";
                    await writer.WriteAsync( message, cancel );
                    ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: {message}" );
                    await Task.Delay( rnd.Next( 40 ), cancel );
                }
                ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: 'End of Messages'" );
                await writer.WriteAsync( $"End of Messages", default );
                while( !await receive1.WaitForTaskCompletionAsync( 100, cancel ) )
                {
                    ActivityMonitor.StaticLogger.Trace( $"{c.Name} writer: resending 'End of Messages'" );
                    await writer.WriteAsync( $"End of Messages", default );
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
                var rnd = seed == 0 ? new Random( seed ) : Random.Shared;
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
