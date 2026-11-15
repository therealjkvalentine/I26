using System.Net;
using LiteNetLib;

namespace I76.Net;

/// <summary>
/// Where this process's UDP sockets listen. The game, the dedicated server and the session browser accept every address by
/// default (a LAN player must reach a host; a browser must hear LAN broadcasts). The test assemblies set <see cref="Loopback"/>
/// once at start-up so that no test ever listens beyond 127.0.0.1 / ::1 (a non-loopback listener makes Windows Defender
/// Firewall prompt for every new test host exe).
/// </summary>
public static class NetBind
{
    /// <summary>The IPv4 address new sockets bind to (default: every address).</summary>
    public static IPAddress V4 { get; set; } = IPAddress.Any;
    /// <summary>The IPv6 address new sockets bind to (default: every address).</summary>
    public static IPAddress V6 { get; set; } = IPAddress.IPv6Any;

    /// <summary>Binds new sockets to loopback only.</summary>
    public static void Loopback() { V4 = IPAddress.Loopback; V6 = IPAddress.IPv6Loopback; }

    /// <summary>Same as <see cref="NetManager.Start(int)"/> (port 0 = any free port), on <see cref="V4"/> / <see cref="V6"/>.</summary>
    public static bool Start(NetManager net, int port = 0) => net.Start(V4, V6, port);

    /// <summary>Same as <see cref="NetManager.StartInManualMode(int)"/>, on <see cref="V4"/> / <see cref="V6"/>.</summary>
    public static bool StartInManualMode(NetManager net, int port = 0) => net.StartInManualMode(V4, V6, port);
}
