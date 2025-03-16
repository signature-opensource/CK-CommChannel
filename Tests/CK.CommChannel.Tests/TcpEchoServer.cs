using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

public class TcpEchoServer : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _cts;
    Task? _serverTask;

    public TcpEchoServer( IPAddress ipAddress, int port )
    {
        _listener = new TcpListener( ipAddress, port );
        _cts = new CancellationTokenSource();
    }

    public void Start()
    {
        _listener.Start();
        _serverTask = Task.Run( async () =>
        {
            while( !_cts.Token.IsCancellationRequested )
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync( client );
                }
                catch( SocketException )
                {
                    // Handle socket exceptions here
                }
            }
        } );
    }

    public async Task StopAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if( _serverTask is not null )
        {
            await _serverTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _listener.Stop();
        _cts.Dispose();
    }

    public void Dispose()
    {
#pragma warning disable VSTHRD002
        StopAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        _listener.Stop();
        _cts.Dispose();
    }

    private async Task HandleClientAsync( TcpClient client )
    {
        using( client )
        {
            try
            {
                await using var networkStream = client.GetStream();
                Memory<byte> buffer = new byte[1024];
                int bytesRead;

                while( (bytesRead = await networkStream.ReadAsync( buffer, _cts.Token )) > 0 )
                {
                    await networkStream.WriteAsync( buffer[..bytesRead], _cts.Token );
                }
            }
            catch( IOException )
            {
                // Handle IO exceptions here
            }
        }
    }
}
