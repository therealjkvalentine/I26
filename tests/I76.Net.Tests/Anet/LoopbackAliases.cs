using System.Net;
using System.Net.Sockets;
using Xunit;

namespace I76.Net.Tests.Anet;

/// <summary>
/// Whether this machine's loopback answers on 127.0.0.2 to 127.0.0.7 as well as 127.0.0.1. Linux and Windows give the whole 127/8 block
/// to the loopback; macOS gives it 127.0.0.1 alone (the others need `sudo ifconfig lo0 alias 127.0.0.N`), and binding one fails with
/// "can't assign requested address". The NAT emulation (<see cref="NatBox"/>: a public address per box) and the LAN search's second
/// "network" need them, so those tests skip where they are missing.
/// </summary>
internal static class LoopbackAliases
{
    public const string Why = "this machine's loopback has no 127.0.0.2-127.0.0.7 (macOS: sudo ifconfig lo0 alias 127.0.0.N adds them)";

    public static readonly bool Available = Probe();

    private static bool Probe()
    {
        try
        {
            for (byte n = 2; n <= 7; n++) using (new UdpClient(new IPEndPoint(new IPAddress([127, 0, 0, n]), 0))) { }
            return true;
        }
        catch (SocketException) { return false; }
    }
}

/// <summary>A fact that is skipped where the loopback has no 127.0.0.N addresses (<see cref="LoopbackAliases"/>).</summary>
public sealed class LoopbackAliasFactAttribute : FactAttribute
{
    public LoopbackAliasFactAttribute() { if (!LoopbackAliases.Available) Skip = LoopbackAliases.Why; }
}

/// <summary>A theory that is skipped where the loopback has no 127.0.0.N addresses (<see cref="LoopbackAliases"/>).</summary>
public sealed class LoopbackAliasTheoryAttribute : TheoryAttribute
{
    public LoopbackAliasTheoryAttribute() { if (!LoopbackAliases.Available) Skip = LoopbackAliases.Why; }
}
