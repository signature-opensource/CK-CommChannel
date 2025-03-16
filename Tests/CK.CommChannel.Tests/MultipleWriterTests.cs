using CK.Core;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class MultipleWriterTests
{
    [TestCase( "UsePipe", true )]
    [TestCase( "UseNetworkStream", true )]
    [TestCase( "UsePipe", false )]
    [TestCase( "UseNetworkStream", false )]
    public async Task MultipleWriter_stress_Async( string channelType, bool multiple )
    {
        bool usePipe = channelType == "UsePipe";
        using var gLog = TestHelper.Monitor.OpenInfo( $"{nameof( MultipleWriter_stress_Async )}({channelType},{multiple})" );

        var endPoint = usePipe
                        ? MemoryChannel.AllocatePipeChannel( "Test" )
                        : await MemoryChannel.AllocateNetworkStreamChannelAsync( "Test" );

        MemoryChannelConfiguration rConfig = new MemoryChannelConfiguration { EndPointName = "Test", AutoReconnect = true, ConnectionTimeout = 300, Reverted = true };
        var rChannel = CommunicationChannel.Create( TestHelper.Monitor, rConfig );

        MemoryChannelConfiguration config = new MemoryChannelConfiguration { EndPointName = "Test", AutoReconnect = true, ConnectionTimeout = 300 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        try
        {
            var writer = new StringLineMessageWriter( channel.Writer, Encoding.ASCII ) { MultipleWriters = multiple };

            var reader = Task.Run( async () =>
            {
                var r = new StringLineMessageReader( rChannel.Reader, Encoding.ASCII, "\r\n" );
                int expectedCount = Environment.ProcessorCount * 5 * 10;
                for( int i = 0; i < expectedCount; ++i )
                {
                    await r.ReadNextAsync();
                }
            } );

            var senders = Enumerable.Range( 0, Environment.ProcessorCount * 5 )
                                    .Select( iTask => Task.Run( async () =>
                                    {
                                        for( int i = 0; i < 10; i++ )
                                        {
                                            await writer.WriteAsync( $"{iTask}/{i}", default );
                                        }
                                    } ) )
                                    .ToArray();
            if( multiple )
            {
                await Task.WhenAll( senders );
                await reader;
            }
            else
            {
                await FluentActions.Awaiting( async () =>
                {
                    await Task.WhenAll( senders );
                } ).Should().ThrowAsync<InvalidOperationException>();
            }
        }
        finally
        {
            await MemoryChannel.DeallocateAsync( "Test" );
            await channel.DisposeAsync();
            await rChannel.DisposeAsync();
        }
    }
}
