using CK.Core;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CK.CommChannel.Tests;

static class ChannelTestExtensions
{
    /// <summary>
    /// Default budget for "this must eventually happen" waits. It is deliberately generous: it is a
    /// guard against a hang, not a measure of how fast the channel is. A loaded CI agent must not
    /// turn a correct implementation into a failure.
    /// </summary>
    public const int DefaultWaitTimeoutMS = 20_000;

    /// <summary>
    /// Waits until <see cref="CommunicationChannel.ConnectionStatus"/> reaches <paramref name="expected"/>.
    /// <para>
    /// Warning: <c>await Task.Delay( someGuessedDuration )</c> followed by an assertion on the status
    /// is not an alternative to this. Such a delay is either too short (the test fails on a loaded
    /// machine) or too long (every run pays for it), whereas polling is correct in both cases.
    /// </para>
    /// </summary>
    public static Task WaitForConnectionStatusAsync( this CommunicationChannel c,
                                                     ConnectionAvailability expected,
                                                     CancellationToken cancel = default )
    {
        return WaitForConnectionStatusAsync( c, expected, DefaultWaitTimeoutMS, cancel );
    }

    /// <inheritdoc cref="WaitForConnectionStatusAsync(CommunicationChannel, ConnectionAvailability, CancellationToken)"/>
    public static Task WaitForConnectionStatusAsync( this CommunicationChannel c,
                                                     ConnectionAvailability expected,
                                                     int timeoutMS,
                                                     CancellationToken cancel = default )
    {
        return WaitForAsync( () => c.ConnectionStatus == expected,
                             $"{c.Name}: ConnectionStatus to be {expected} (it is {c.ConnectionStatus})",
                             timeoutMS,
                             cancel );
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it is true, throwing a <see cref="TimeoutException"/>
    /// that describes <paramref name="what"/> was awaited if it doesn't happen in time.
    /// </summary>
    public static async Task WaitForAsync( Func<bool> condition,
                                           string what,
                                           int timeoutMS = DefaultWaitTimeoutMS,
                                           CancellationToken cancel = default )
    {
        var sw = Stopwatch.StartNew();
        while( !condition() )
        {
            if( sw.ElapsedMilliseconds > timeoutMS )
            {
                throw new TimeoutException( $"Waited {sw.ElapsedMilliseconds} ms for {what}." );
            }
            await Task.Delay( 20, cancel ).ConfigureAwait( false );
        }
    }
}
