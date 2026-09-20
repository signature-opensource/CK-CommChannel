using System.Net;
using System.Net.Sockets;

namespace CK.CommChannel.Tests;

static class TestNetwork
{
    /// <summary>
    /// Gets a port that is free right now on the loopback interface.
    /// <para>
    /// Hard coded port numbers make tests fail whenever something else on the machine (or a concurrent
    /// run of the same test suite) happens to use them. Asking the OS for an ephemeral port and
    /// releasing it immediately is not airtight, but it removes the systematic collisions.
    /// </para>
    /// </summary>
    public static int GetFreePort()
    {
        var l = new TcpListener( IPAddress.Loopback, 0 );
        l.Start();
        try
        {
            return ((IPEndPoint)l.LocalEndpoint).Port;
        }
        finally
        {
            l.Stop();
        }
    }
}
