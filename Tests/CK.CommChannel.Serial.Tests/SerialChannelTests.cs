using System;
using System.Buffers;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;
using CK.Core;

namespace CK.CommChannel.Serial.Tests;

/// <summary>
/// <para>
/// Note: The <see cref="SerialPortChannel"/> test require a virtual COM port pair to be installed.
/// </para>
/// <para>
/// The COM ports used by the tests are retrieved from environment variables
/// CK_COMMCHANNEL_TESTS_SERIAL_PORT_A, and CK_COMMCHANNEL_TESTS_SERIAL_PORT_B.
/// </para>
/// <para>
/// If unset, the tests will in inconclusive.
/// </para>
/// </summary>
public class SerialPortChannelTests
{
    static readonly byte[] HelloBuffer = Encoding.ASCII.GetBytes( "Hello" );
    static readonly byte[] WorldBuffer = Encoding.ASCII.GetBytes( "World" );

    [Test]
    public async Task SerialPort_channel_can_open_Async()
    {
        if( !TryGetTestPorts( out string portA, out string portB ) )
        {
            Assert.Inconclusive( $"This test requires a virtual COM port pair to be configured on the machine. See the {nameof( SerialPortChannelTests )} class." );
            return;
        }

        var (testedPortConfiguration, pairedPort) = ConfigurePorts( portA, portB );

        var channel = CommunicationChannel.Create( TestHelper.Monitor, testedPortConfiguration );

        await channel.DisposeAsync();
    }

    [Test]
    public async Task SerialPort_channel_can_read_Async()
    {
        if( !TryGetTestPorts( out string portA, out string portB ) )
        {
            Assert.Inconclusive( $"This test requires a virtual COM port pair to be configured on the machine. See the {nameof( SerialPortChannelTests )} class." );
            return;
        }

        ReadResult result;

        var (testedPortConfiguration, pairedPort) = ConfigurePorts( portA, portB );

        // Open A
        await using var channel = CommunicationChannel.Create( TestHelper.Monitor, testedPortConfiguration );
        // Open B
        using( pairedPort )
        {
            pairedPort.Open();

            // Write on B
            await pairedPort.BaseStream.WriteAsync( HelloBuffer );
            // Read on A
            result = await channel.Reader.ReadAsync();

            result.Buffer.Length.ShouldBe( HelloBuffer.Length, $"There should have been {HelloBuffer.Length} bytes written on {nameof( pairedPort )}." );
            result.Buffer.ToArray().ShouldBe( HelloBuffer, $"Hello should have been written on {nameof( pairedPort )}" );
            channel.Reader.AdvanceTo( result.Buffer.End );

            // Write on B
            await pairedPort.BaseStream.WriteAsync( WorldBuffer );
            // Read on A
            result = await channel.Reader.ReadAsync();

            result.Buffer.Length.ShouldBe( WorldBuffer.Length, $"There should have been {WorldBuffer.Length} bytes written on {nameof( pairedPort )}." );
            result.Buffer.ToArray().ShouldBe( WorldBuffer, $"World should have been written on {nameof( pairedPort )}" );
            channel.Reader.AdvanceTo( result.Buffer.End );
        }
    }

    [Test]
    public async Task SerialPort_channel_can_write_Async()
    {
        if( !TryGetTestPorts( out string portA, out string portB ) )
        {
            Assert.Inconclusive( $"This test requires a virtual COM port pair to be configured on the machine. See the {nameof( SerialPortChannelTests )} class." );
            return;
        }

        byte[] buffer = new byte[32];

        var (testedPortConfiguration, pairedPort) = ConfigurePorts( portA, portB );

        // Open A
        await using var channel = CommunicationChannel.Create( TestHelper.Monitor, testedPortConfiguration );
        // Open B
        using( pairedPort )
        {
            pairedPort.Open();

            // Write on A
            await channel.Writer.WriteAsync( HelloBuffer );
            await channel.Writer.FlushAsync();
            // Read on B
            int readBytes = await pairedPort.BaseStream.ReadAsync( buffer.AsMemory( 0, buffer.Length ) );

            readBytes.ShouldBe( HelloBuffer.Length, $"There should have been {HelloBuffer.Length} bytes written on {nameof( channel )}." );
            buffer[..HelloBuffer.Length].ShouldBe( HelloBuffer, $"Hello should have been written on {nameof( channel )}" );

            // Write on A
            await channel.Writer.WriteAsync( WorldBuffer );
            await channel.Writer.FlushAsync();
            // Read on B
            readBytes = await pairedPort.BaseStream.ReadAsync( buffer.AsMemory( 0, buffer.Length ) );

            readBytes.ShouldBe( WorldBuffer.Length, $"There should have been {WorldBuffer.Length} bytes written on {nameof( channel )}." );
            buffer[..WorldBuffer.Length].ShouldBe( WorldBuffer, $"World should have been written on {nameof( channel )}" );
        }
    }

    [Test]
    public async Task SerialPort_channel_can_timeout_on_read_Async()
    {
        if( !TryGetTestPorts( out string portA, out string portB ) )
        {
            Assert.Inconclusive( $"This test requires a virtual COM port pair to be configured on the machine. See the {nameof( SerialPortChannelTests )} class." );
            return;
        }

        var (testedPortConfiguration, pairedPort) = ConfigurePorts( portA, portB );

        // Open A
        await using var channel = CommunicationChannel.Create( TestHelper.Monitor, testedPortConfiguration );
        // Don't Open B
        using( pairedPort )
        {
            var cts = new CancellationTokenSource( TimeSpan.FromMilliseconds( 500 ) );
            // Read on A
            await Util.Awaitable( () => channel.Reader.ReadAsync( cts.Token ).AsTask() ).ShouldThrowAsync<OperationCanceledException>();
        }
    }

    (SerialChannelConfiguration testedPortConfiguration, SerialPort pairedPort) ConfigurePorts( string portA, string portB )
    {
        var portConfig = new SerialChannelConfiguration()
        {
            PortName = portA,
            BaudRate = 9600,
            Parity = Parity.None,
            DefaultReadTimeout = 500,
            DefaultWriteTimeout = 500
        };
        var pairedPort = new SerialPort( portB, 9600, Parity.None );

        return (portConfig, pairedPort);
    }

    bool TryGetTestPorts( out string portA, out string portB )
    {
        string? envPortA = Environment.GetEnvironmentVariable( "CK_COMMCHANNEL_TESTS_SERIAL_PORT_A" );
        string? envPortB = Environment.GetEnvironmentVariable( "CK_COMMCHANNEL_TESTS_SERIAL_PORT_B" );

        if( string.IsNullOrEmpty( envPortA ) || string.IsNullOrEmpty( envPortB ) )
        {
            portA = string.Empty;
            portB = string.Empty;
            return false;
        }

        portA = envPortA;
        portB = envPortB;
        return true;
    }

}
