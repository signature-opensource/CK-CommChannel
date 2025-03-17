using System.IO.Pipelines;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CK.Core;
using Shouldly;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

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
public class ExplicitSerialTestsTests
{
    sealed class LogDumper : StringLineMessageHandler
    {
        int _messageNumber;

        public LogDumper( PipeReader reader )
            : base( reader, Encoding.ASCII )
        {
            HandleCancelMessages = true;
        }

        protected override ValueTask<bool> HandleMessageAsync( string? message, CancellationToken cancel )
        {
            message ??= "<<Canceled>>";
            ActivityMonitor.StaticLogger.Trace( $"Received n°{++_messageNumber}: {message}" );
            return ValueTask.FromResult( true );
        }

        protected override ValueTask<bool> OnReadTimeoutAsync()
        {
            ActivityMonitor.StaticLogger.Warn( "Timeout!" );
            return ValueTask.FromResult( false );
        }
    }

    [Explicit]
    [TestCase( "COM3", 20000, 20 )]
    public async Task run_with_AutoReconnect_Async( string port, int readTimeout, int maxMessages )
    {
        var config = new SerialChannelConfiguration()
        {
            AutoReconnect = true,
            PortName = port,
            BaudRate = 9600,
            Parity = Parity.None
        };
        await using var channel = CommunicationChannel.Create( TestHelper.Monitor, config );
        var dumper = new LogDumper( channel.Reader );
        dumper.DefaultReadTimeout = readTimeout;
        TestHelper.Monitor.Info( $"Starting read loop for {maxMessages} messages." );
        dumper.StartReadLoop( maxMessages );
        var r = await dumper.StoppedReason;
        TestHelper.Monitor.Info( $"Read loop stopped: {r}" );
        r.ShouldMatch( r => r == MessageHandlerCompletionReason.MaxMessageNumber || r == MessageHandlerCompletionReason.ReadTimeout );
    }
}
