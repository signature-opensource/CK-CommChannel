using System.IO.Pipelines;
using System.Text;

namespace CK.CommChannel;

/// <summary>
/// Base class handler of delimited lines.
/// At least <see cref="MessageHandler{T}.HandleMessageAsync(T, System.Threading.CancellationToken)"/>
/// must be overridden.
/// </summary>
public abstract class StringLineMessageHandler : MessageHandler<string?>
{
    /// <summary>
    /// Initializes a new <see cref="StringLineMessageHandler"/> with an encoding and a non empty line delimiter.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="delimiter">Delimiter. Must not be empty.</param>
    public StringLineMessageHandler( PipeReader reader,
                                     Encoding encoding,
                                     string delimiter )
        : base( new StringLineMessageReader( reader, encoding, delimiter ) )
    {
    }

    /// <summary>
    /// Initializes a new <see cref="StringLineMessageHandler"/> with an encoding and the standard CR ('\r') or CRLF ('\r\n')
    /// line delimiter.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="encoding">The encoding used to decode the bytes to string.</param>
    /// <param name="skipLineFeed">False to consider only '\r' delimiter: any '\n' will appear at the start of the messages.</param>
    public StringLineMessageHandler( PipeReader reader, Encoding encoding, bool skipLineFeed = true )
        : base( new StringLineMessageReader( reader, encoding, skipLineFeed ) )
    {
    }

}
