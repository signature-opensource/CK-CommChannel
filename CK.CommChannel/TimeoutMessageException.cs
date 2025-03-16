using System;

namespace CK.CommChannel;

/// <summary>
/// Specialized <see cref="TimeoutException"/> raised by <see cref="MessageReaderBase{T}"/>
/// and <see cref="MessageWriterBase{T}"/>.
/// </summary>
public sealed class TimeoutMessageException : TimeoutException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutMessageException"/>.
    /// </summary>
    public TimeoutMessageException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeoutMessageException"/> with the specified
    /// error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public TimeoutMessageException( string? message )
        : base( message )
    {
    }

    /// <summary>
    /// Initializes a new instance of the System.TimeoutException class with the specified
    /// error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">Optional inner exception.</param>
    public TimeoutMessageException( string? message, Exception? innerException )
        : base( message, innerException )
    {
    }

}
