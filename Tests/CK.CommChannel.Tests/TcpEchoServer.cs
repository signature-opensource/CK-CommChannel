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
    int _stopped;

    /// <summary>
    /// Initializes a new echo server. Port 0 (the default) lets the OS pick a free port that
    /// <see cref="Port"/> exposes once <see cref="Start"/> has been called.
    /// <para>
    /// Warning: prefer port 0 to an explicit port number. A hard coded port fails the test whenever
    /// anything else on the machine (including a concurrent run of this suite) happens to use it.
    /// </para>
    /// </summary>
    public TcpEchoServer( IPAddress ipAddress, int port = 0 )
    {
        _listener = new TcpListener( ipAddress, port );
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Gets the port this server listens to. Only meaningful once <see cref="Start"/> has been called.
    /// </summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public void Start()
    {
        _listener.Start();
        _serverTask = Task.Run( async () =>
        {
            try
            {
                while( !_cts.IsCancellationRequested )
                {
                    var client = await _listener.AcceptTcpClientAsync( _cts.Token );
                    _ = HandleClientAsync( client );
                }
            }
            // Stopping the listener is the normal way out of this loop.
            // Warning: all three are needed. Which one the pending accept ends with depends on the
            // platform and on the exact instant Stop() lands, and any that is not caught here faults
            // _serverTask, which StopAsync then rethrows.
            catch( OperationCanceledException ) { }
            catch( ObjectDisposedException ) { }
            catch( SocketException ) { }
        } );
    }

    public async Task StopAsync()
    {
        if( Interlocked.Exchange( ref _stopped, 1 ) == 1 ) return;
        await _cts.CancelAsync();
        _listener.Stop();
        if( _serverTask is not null )
        {
            await _serverTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
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
                // The client went away: nothing to do.
            }
            catch( OperationCanceledException )
            {
                // The server is stopping.
            }
            catch( ObjectDisposedException )
            {
                // The server is stopping.
            }
        }
    }
}
