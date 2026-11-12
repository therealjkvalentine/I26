using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using LiteNetLib.Layers;

namespace I76.Net.Tests.Anet;

/// <summary>
/// The punch tests' network, on one thread and its own clock: the fake server, the NAT boxes and the peers' LiteNetLib managers
/// (in manual mode) are pumped from the test, and the clock moves on only when every datagram sent between their sockets has
/// been taken in. Real UDP on the loopback still carries them, but no thread waits for the scheduler and no timeout runs on
/// the wall clock, so a loaded machine changes how long a test takes, never how it ends (they failed under load when each hop
/// was a thread of its own and a handshake had five seconds of wall time).
/// </summary>
public sealed class TestNet
{
    /// <summary>One step of the clock: the pause between two of a peer's updates.</summary>
    public const double Tick = 0.005;

    private readonly Dictionary<IPEndPoint, int> _pending = [];
    private readonly List<Action> _pumps = [];
    private readonly List<Action<double>> _tickers = [];

    public double Now { get; private set; }
    public Func<double> Clock => () => Now;
    /// <summary>Something outside it answers in real time (the real anet server, a browser on its own threads): the clock keeps pace with the wall.</summary>
    public bool RealPace { get; set; }
    /// <summary>Datagrams given up as lost after the wait for them (diagnostics; loopback does not lose them).</summary>
    public int Lost { get; private set; }

    public static IPEndPoint Normal(IPEndPoint ep) =>
        ep.Address.IsIPv4MappedToIPv6 ? new IPEndPoint(ep.Address.MapToIPv4(), ep.Port) : new IPEndPoint(ep.Address, ep.Port);

    public static IPEndPoint LocalOf(UdpClient c) => Normal((IPEndPoint)c.Client.LocalEndPoint!);

    /// <summary>A socket of this network: datagrams to it are waited for.</summary>
    public void Add(IPEndPoint local) => _pending.TryAdd(Normal(local), 0);
    public void Remove(IPEndPoint local) => _pending.Remove(Normal(local));

    public void Sent(IPEndPoint to)
    {
        var k = Normal(to);
        if (_pending.TryGetValue(k, out int n)) _pending[k] = n + 1;
    }

    public void Arrived(IPEndPoint at)
    {
        var k = Normal(at);
        if (_pending.TryGetValue(k, out int n) && n > 0) _pending[k] = n - 1;
    }

    private bool InFlight => _pending.Values.Any(n => n > 0);

    /// <summary>Called on every pass: a socket's receive and its owner's update.</summary>
    public void Pump(Action pump) => _pumps.Add(pump);
    public void Unpump(Action pump) => _pumps.Remove(pump);
    /// <summary>Called as the clock moves, with the step in seconds (LiteNetLib's manual update).</summary>
    public void Ticker(Action<double> t) => _tickers.Add(t);
    public void Unticker(Action<double> t) => _tickers.Remove(t);

    /// <summary>Pumps until nothing sent is still on its way. A datagram that never comes (only a lost one) is given up after a while, as the network would lose it.</summary>
    public void Settle()
    {
        var wait = Stopwatch.StartNew();
        int idle = 0;
        while (true)
        {
            foreach (var p in _pumps.ToArray()) p();
            if (!InFlight) return;
            if (wait.Elapsed.TotalSeconds > 10)
            {
                Lost += _pending.Values.Sum();
                foreach (var k in _pending.Keys.ToList()) _pending[k] = 0;
                return;
            }
            // on its way through the loopback: let the stack deliver it
            if (++idle > 2000) Thread.Sleep(1); else if (idle > 50) Thread.Sleep(0); else Thread.Yield();
        }
    }

    public void Advance()
    {
        Now += Tick;
        foreach (var t in _tickers.ToArray()) t(Tick);
        if (RealPace) Thread.Sleep((int)(Tick * 1000));
    }

    /// <summary>Runs the network until <paramref name="done"/> or <paramref name="seconds"/> of its clock have passed.</summary>
    public bool Until(Func<bool> done, double seconds)
    {
        double end = Now + seconds;
        while (true)
        {
            Settle();
            if (done()) return true;
            if (Now >= end) return false;
            Advance();
        }
    }
}

/// <summary>Counts a LiteNetLib manager's datagrams for its <see cref="TestNet"/>, below everything else (what goes out is what the socket sends).</summary>
internal sealed class CountingLayer(TestNet net, PacketLayerBase? inner) : PacketLayerBase(inner?.ExtraPacketSizeForLayer ?? 0)
{
    public IPEndPoint? Local;

    public override void ProcessInboundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int length)
    {
        if (Local is not null) net.Arrived(Local);
        inner?.ProcessInboundPacket(ref endPoint, ref data, ref length);
    }

    public override void ProcessOutBoundPacket(ref IPEndPoint endPoint, ref byte[] data, ref int offset, ref int length)
    {
        inner?.ProcessOutBoundPacket(ref endPoint, ref data, ref offset, ref length);
        if (endPoint is not null) net.Sent(endPoint);
    }
}

internal static class UdpPump
{
    /// <summary>
    /// Every datagram waiting on <paramref name="sock"/>, without blocking. Socket errors are skipped as the threads skipped
    /// them: an ICMP "port unreachable" from an earlier send, a send from the handler to a network the loopback has not.
    /// </summary>
    public static void Drain(UdpClient sock, Action<IPEndPoint, byte[]> handle)
    {
        while (true)
        {
            try
            {
                if (sock.Available == 0) return;
                var from = new IPEndPoint(IPAddress.Any, 0);
                var d = sock.Receive(ref from);
                handle(from, d);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }
}
