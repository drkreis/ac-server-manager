using System.Net;
using System.Net.Sockets;

namespace ServerManager.Core;

public static class ServerPorts
{
    /// <summary>Probe the same IPv4 wildcard bindings used by the server, rather than relying on a stale OS table.</summary>
    public static string[] FindConflicts(int tcp, int http, int udp)
    {
        var conflicts = new List<string>();
        foreach (var port in new[] { tcp, http }.Distinct())
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try { socket.Bind(new IPEndPoint(IPAddress.Any, port)); }
            catch (SocketException) { conflicts.Add("TCP " + port); }
        }
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true })
        {
            try { socket.Bind(new IPEndPoint(IPAddress.Any, udp)); }
            catch (SocketException) { conflicts.Add("UDP " + udp); }
        }
        return conflicts.ToArray();
    }
}
