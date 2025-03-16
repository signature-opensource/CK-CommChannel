using System;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;



static class MessageSender
{
    public static void WriteLine( PipeWriter w, string text, string delimiter = "\r\n" )
    {
        Encoding.ASCII.GetBytes( text.AsSpan(), w );
        Encoding.ASCII.GetBytes( delimiter.AsSpan(), w );
    }

    public static async Task SendLineAsync( PipeWriter w, string text, bool bytePerByte = false, string delimiter = "\r\n" )
    {
        if( bytePerByte )
        {
            text += delimiter;
            for( int i = 0; i < text.Length; ++i )
            {
                Encoding.ASCII.GetBytes( text.AsSpan( i, 1 ), w );
                await w.FlushAsync();
                await Task.Delay( 15 );
            }
        }
        else
        {
            WriteLine( w, text, delimiter );
            await w.FlushAsync();
        }
    }

    public static async Task SendDelimitedMessagesAsync( PipeWriter w,
                                                         int messageCount = 5,
                                                         SendGarbageMode garbage = SendGarbageMode.None,
                                                         bool bytePerByte = false,
                                                         char startDelimiter = '#',
                                                         string endDelimiter = "\r\n" )
    {
        string DoGarbage( string msg )
        {
            return garbage switch
            {
                SendGarbageMode.WithGarbage => $" garbage {msg} <other garbage...> ",
                SendGarbageMode.WithViciousGarbage => $" g{startDelimiter}a{startDelimiter}rbage{startDelimiter} {msg} {endDelimiter} {endDelimiter}other garbage{endDelimiter}...{endDelimiter} * ",
                _ => msg
            };
        }

        void Send( string text )
        {
            var buffer = w.GetSpan( 100 );
            int len = Encoding.ASCII.GetBytes( DoGarbage( text ).AsSpan(), buffer );
            w.Advance( len );
        }

        async Task SendBytePerByteAsync( string text )
        {
            var msg = Encoding.ASCII.GetBytes( DoGarbage( text ) );
            for( int i = 0; i < msg.Length; i++ )
            {
                await w.WriteAsync( msg.AsMemory( i, 1 ) );
                await Task.Delay( 15 );
            }
        }

        if( bytePerByte )
        {
            for( int i = 0; i < messageCount; ++i )
            {
                await SendBytePerByteAsync( $"{startDelimiter}Message {i}{endDelimiter}" );
            }
        }
        else
        {
            for( int i = 0; i < messageCount; ++i )
            {
                Send( $"{startDelimiter}Message {i}{endDelimiter}" );
            }
            await w.FlushAsync();
        }
    }

}
