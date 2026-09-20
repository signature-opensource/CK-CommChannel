using CK.Core;
using Shouldly;
using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace CK.CommChannel.Tests;

[TestFixture]
public class DelimitedMessageHandlerTests
{

    class ExpectedNumberedMessagesHandler : StringDelimitedMessageHandler
    {
        public int Number { get; private set; }

        public ExpectedNumberedMessagesHandler( PipeReader reader )
            : base( reader,
                    Encoding.ASCII,
                    (byte)'#',
                    (byte)';',
                    removeDelimiters: true )
        {
        }

        public ExpectedNumberedMessagesHandler( PipeReader reader, byte startDelimiter, ReadOnlyMemory<byte> endDelimiter )
            : base( reader,
                    Encoding.ASCII,
                    startDelimiter,
                    endDelimiter,
                    removeDelimiters: true )
        {
        }

        protected override ValueTask<bool> HandleMessageAsync( string? message, CancellationToken cancel )
        {
            Debug.Assert( message != null, "HandleCancelMessages is false by default." );
            Debug.Assert( message.Length > 0, "Vicious garbage MUST NOT introduce empty messages." );
            if( message == "Stop Loop!" )
            {
                ++Number;
                return new ValueTask<bool>( false );
            }
            message.ShouldBe( $"Message {Number++}" );
            return new ValueTask<bool>( true );
        }

        // For tests, it's okay to rethrow. IRL this should be logged and stop the device.
        protected override ValueTask<bool> OnUnhandledExceptionAsync( ExceptionDispatchInfo ex )
        {
            ex.Throw();
            return ValueTask.FromResult( false );
        }
    }

    [TestCase( SendGarbageMode.None, false )]
    [TestCase( SendGarbageMode.None, true )]
    [TestCase( SendGarbageMode.WithGarbage, false )]
    [TestCase( SendGarbageMode.WithGarbage, true )]
    [TestCase( SendGarbageMode.WithViciousGarbage, false )]
    [TestCase( SendGarbageMode.WithViciousGarbage, true )]
    [CancelAfter( 30_000 )]
    public async Task handling_messages_with_garbage_between_messages_Async( SendGarbageMode garbageMode, bool bytePerByte, CancellationToken cancel )
    {
        await using var endPoint = TestMemoryEndPoint.AllocatePipe();
        var config = new MemoryChannelConfiguration() { EndPointName = endPoint.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        await MessageSender.SendDelimitedMessagesAsync( endPoint.Input, 4, garbageMode, bytePerByte, startDelimiter:'#', endDelimiter: ";" );

        var loop = new ExpectedNumberedMessagesHandler( channel.Reader );

        loop.StartReadLoop( maxMessage: 4 );
        (await loop.StoppedReason.WaitAsync( cancel )).ShouldBe( MessageHandlerCompletionReason.MaxMessageNumber );

        loop.Number.ShouldBe( 4 );

        await MessageSender.SendLineAsync( endPoint.Input, "#Stop Loop!", bytePerByte, ";" );

        loop.StartReadLoop();
        (await loop.StoppedReason.WaitAsync( cancel )).ShouldBe( MessageHandlerCompletionReason.ProcessMessage );
        loop.Number.ShouldBe( 5 );

    }

    [TestCase( SendGarbageMode.None, true )]
    [TestCase( SendGarbageMode.WithGarbage, true )]
    [TestCase( SendGarbageMode.WithViciousGarbage, true )]
    [TestCase( SendGarbageMode.None, false )]
    [TestCase( SendGarbageMode.WithGarbage, false )]
    [TestCase( SendGarbageMode.WithViciousGarbage, false )]
    [CancelAfter( 30_000 )]
    public async Task multiEnd_with_garbage_between_messages_Async( SendGarbageMode garbageMode, bool bytePerByte, CancellationToken cancel )
    {
        await using var endPoint = TestMemoryEndPoint.AllocatePipe();
        var config = new MemoryChannelConfiguration() { EndPointName = endPoint.Name, DefaultReadTimeout = -1, DefaultWriteTimeout = -1 };
        var channel = CommunicationChannel.Create( TestHelper.Monitor, config );

        await MessageSender.SendDelimitedMessagesAsync( endPoint.Input, 4, garbageMode, bytePerByte );

        var loop = new ExpectedNumberedMessagesHandler( channel.Reader, startDelimiter: (byte)'#', endDelimiter: StringLineMessageReader.CRLF );

        loop.StartReadLoop( maxMessage: 4 );
        (await loop.StoppedReason.WaitAsync( cancel )).ShouldBe( MessageHandlerCompletionReason.MaxMessageNumber );

        loop.Number.ShouldBe( 4 );

        await MessageSender.SendLineAsync( endPoint.Input, "#Stop Loop!" );

        loop.StartReadLoop();
        (await loop.StoppedReason.WaitAsync( cancel )).ShouldBe( MessageHandlerCompletionReason.ProcessMessage );
        loop.Number.ShouldBe( 5 );

    }

}
