using CK.Core;
using System;

namespace CK.CommChannel;

/// <summary>
/// Element of the <see cref="LogCatcher.Entries">Entries</see>.
/// </summary>
public sealed class CommunicationChannelLogEntry
{
    /// <summary>
    /// The tags of the log entry.
    /// </summary>
    public readonly CKTrait Tags;

    /// <summary>
    /// The log level of the log entry.
    /// </summary>
    public readonly LogLevel Level;

    /// <summary>
    /// Time of the log entry.
    /// </summary>
    public readonly DateTimeStamp LogTime;

    /// <summary>
    /// Depth of the log entry.
    /// </summary>
    public readonly int Depth;

    /// <summary>
    /// The text of the log entry.
    /// </summary>
    public readonly string Text;

    /// <summary>
    /// The exception of the log entry if any.
    /// </summary>
    public readonly CKExceptionData? Exception;

    internal CommunicationChannelLogEntry( CKTrait tags, int depth, LogLevel level, string text, DateTimeStamp logTime, CKExceptionData? ex )
    {
        Throw.DebugAssert( (level & LogLevel.IsFiltered) == 0 );
        Tags = tags;
        Depth = depth;
        Level = level;
        LogTime = logTime;
        Text = text;
        Exception = ex;
    }

    /// <summary>
    /// Overridden to return the <see cref="Text"/> of this element.
    /// </summary>
    /// <returns>This <see cref="Text"/> property.</returns>
    public override string ToString() => Text;
}
