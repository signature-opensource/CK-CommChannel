using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace CK.CommChannel;


public sealed partial class MemoryChannel
{
    /// <summary>
    /// Implements Pipe bridges to <see cref="ChannelImpl"/>.
    /// </summary>
    sealed class PipeChannel : IPipeChannel, IAsyncDisposable
    {
        internal Pipe _output;
        internal Pipe _input;

        public PipeWriter Input => _input.Writer;

        public PipeReader Output => _output.Reader;

        public IStablePipeBehavior ReaderBehavior { get; }

        public IStablePipeBehavior WriterBehavior { get; }

        internal PipeChannel( PipeOptions inputOptions,
                              PipeOptions outputOptions,
                              IStablePipeBehavior readerBehavior,
                              IStablePipeBehavior writerBehavior )
        {
            _input = new Pipe( inputOptions );
            _output = new Pipe( outputOptions );
            ReaderBehavior = readerBehavior;
            WriterBehavior = writerBehavior;
        }

        public async ValueTask DisposeAsync()
        {
            await _input.Writer.CompleteAsync().ConfigureAwait( false );
            await _input.Reader.CompleteAsync().ConfigureAwait( false );
            await _output.Writer.CompleteAsync().ConfigureAwait( false );
            await _output.Reader.CompleteAsync().ConfigureAwait( false );
        }
    }
}
