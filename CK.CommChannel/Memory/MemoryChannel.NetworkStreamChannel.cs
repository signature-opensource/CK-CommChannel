using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace CK.CommChannel;

public sealed partial class MemoryChannel 
{
    static readonly StreamPipeReaderOptions _pipeReaderOptions = new StreamPipeReaderOptions( leaveOpen: true );
    static readonly StreamPipeWriterOptions _pipeWriterOptions = new StreamPipeWriterOptions( leaveOpen: true );

    sealed class NetworkStreamChannel : IPipeChannel, IAsyncDisposable
    {
        readonly TcpClient _server;
        readonly TcpClient _client;
        internal readonly NetworkStream _channelStream;
        internal readonly NetworkStream _endPointStream;

        public NetworkStreamChannel( TcpClient server,
                                     TcpClient client,
                                     NetworkStream channelStream,
                                     NetworkStream endPointStream,
                                     IStablePipeBehavior readerBehavior,
                                     IStablePipeBehavior writerBehavior )
        {
            _server = server;
            _client = client;
            _channelStream = channelStream;
            _endPointStream = endPointStream;
            ReaderBehavior = readerBehavior;
            WriterBehavior = writerBehavior;
            Input = PipeWriter.Create( _endPointStream, _pipeWriterOptions );
            Output = PipeReader.Create( _endPointStream, _pipeReaderOptions );
        }

        public async ValueTask DisposeAsync()
        {
            await _endPointStream.DisposeAsync().ConfigureAwait( false );
            await _endPointStream.DisposeAsync().ConfigureAwait( false );
            // This is required.
            _client.Dispose();
            _server.Dispose();
        }

        public PipeWriter Input { get; }

        public PipeReader Output { get; }

        public IStablePipeBehavior ReaderBehavior { get; }

        public IStablePipeBehavior WriterBehavior { get; }

        public static async Task<NetworkStreamChannel> CreateAsync( IStablePipeBehavior readerBehavior, IStablePipeBehavior writerBehavior )
        {
            // The port is a dynamic port that is free.
            var listener = new TcpListener( IPAddress.Loopback, 0 );
            listener.Start();
            var endPoint = (IPEndPoint)listener.LocalEndpoint;
            Task<TcpClient> server = listener.AcceptTcpClientAsync();
            // Don't use the TcpClient( endPoint ) constructor.
            // See https://stackoverflow.com/questions/4319660/connecting-to-a-tcplistener-on-a-different-thread-same-process 
            TcpClient client = new TcpClient();
            await client.ConnectAsync( endPoint ).ConfigureAwait( false );
            var channelStream = client.GetStream();
            var s = await server.ConfigureAwait( false );
            var endPointStream = s.GetStream();
            // No need to accept new connections: the client is connected.
            listener.Stop();

            return new NetworkStreamChannel( s,
                                             client,
                                             channelStream,
                                             endPointStream,
                                             readerBehavior,
                                             writerBehavior );
        }

    }
}
