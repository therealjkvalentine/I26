using System.Diagnostics;
using System.Net;
using I76.Net.Anet;
using LiteNetLib;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests.Anet;

/// <summary>
/// The internet roster's search of the whole address book (<see cref="ServerBrowser"/>): every entry at once, the games
/// grouped by the entry that found them, each entry's own state, and a silent or unknown one holding up none of the
/// others. Against in-process <see cref="FakeAnetServer"/>s and a bare game answering queries, all on loopback, and all on
/// the test's <see cref="TestNet"/>: one thread, LiteNetLib in manual mode, and a clock that moves on only when every datagram
/// between their sockets has arrived (the seconds below are that clock's). The browser's own answer wait, a game's expiry
/// and the anet links' timeouts run on it too, so a loaded machine makes a test slower, never failed (it flaked under load
/// when the browser, its managers and the fake servers ran on threads of their own against the wall clock).
/// </summary>
public class ServerBrowserTests(ITestOutputHelper log)
{
    private readonly TestNet _net = new();

    private static byte[] Tag(string arena) => new I26SessionTag(Protocol.Version, false, false, false, false, I76.Sim.Missions.MeleeKind.Combat, 2,
        I76.Sim.Missions.MeleeGoal.None, arena).Write();

    /// <summary>A game that answers the session query at its address and nothing else (no anet listing).</summary>
    private sealed class DirectGame : IDisposable
    {
        private readonly EventBasedNetListener _listener = new();
        private readonly NetManager _net;
        private readonly TestNet _world;
        private readonly IPEndPoint _local;
        private readonly Action _pump;
        private readonly Action<double> _tick;
        public int Port => _net.LocalPort;

        public DirectGame(string name, TestNet world)
        {
            _world = world;
            var counting = new CountingLayer(world, null);
            _net = new NetManager(_listener, counting) { UnconnectedMessagesEnabled = true, IPv6Enabled = false, AutoRecycle = true };
            var w = new NetWriter(SessionInfo.AnswerBytes);
            new SessionInfo(0x1234, Protocol.Version, name, "m05", 1, 8, false, false, 0, 0, false, [new SessionDriver("Taurus", "vppirna3.vcf", 0)]).Write(w);
            var answer = w.ToArray();
            _listener.NetworkReceiveUnconnectedEvent += (ep, r, t) => { if (Discovery.IsQuery(r.GetRemainingBytesSpan())) _net.SendUnconnectedMessage(answer, ep); };
            _net.StartInManualMode(IPAddress.Loopback, IPAddress.IPv6Loopback, 0);
            _local = new IPEndPoint(IPAddress.Loopback, _net.LocalPort);
            counting.Local = _local;
            world.Add(_local);
            _pump = () => _net.PollEvents();
            _tick = dt => _net.ManualUpdate((float)(dt * 1000));
            world.Pump(_pump);
            world.Ticker(_tick);
        }

        public void Dispose()
        {
            _net.Stop();
            _world.Unpump(_pump);
            _world.Unticker(_tick);
            _world.Remove(_local);
        }
    }

    /// <summary>
    /// One entry's browser, on the test's network: pumped and stepped by it until the roster disposes of it. No name on this
    /// network is found (the "nosuch.invalid" entry; the system's DNS would answer in its own time, off the clock).
    /// </summary>
    private SessionBrowser NewBrowser()
    {
        var counting = new CountingLayer(_net, null);
        var b = new SessionBrowser(counting, _net.Clock, manual: true) { Resolve = _ => Task.FromResult<IPAddress?>(null) };
        var local = new IPEndPoint(IPAddress.Loopback, b.LocalPort);
        counting.Local = local;
        _net.Add(local);
        Action? pump = null;
        Action<double> tick = dt => { if (b.Running) b.ManualUpdate(dt); };
        pump = () =>
        {
            if (b.Running) { b.Poll(); return; }
            _net.Unpump(pump!);
            _net.Unticker(tick);
            _net.Remove(local);
        };
        _net.Pump(pump);
        _net.Ticker(tick);
        return b;
    }

    private ServerBrowser Roster(double answerWait = 6) => new(_net.Clock, NewBrowser) { AnswerWait = answerWait };

    /// <summary>A socket on loopback that takes everything and answers nothing (a server down behind a firewall: no refusal comes back either).</summary>
    private static System.Net.Sockets.UdpClient Silent() => new(new IPEndPoint(IPAddress.Loopback, 0));
    private static int PortOf(System.Net.Sockets.UdpClient u) => ((IPEndPoint)u.Client.LocalEndPoint!).Port;

    private static ServerSource AnetAt(string name, int port) => new(name, $"127.0.0.1:{port}", ("127.0.0.1", port), null);

    /// <summary>Runs the network and the browser (asking again every half second) until <paramref name="done"/> or <paramref name="seconds"/> of the network's clock.</summary>
    private bool Loop(ServerBrowser b, Func<bool> done, double seconds)
    {
        var wall = Stopwatch.StartNew();
        double from = _net.Now, end = _net.Now + seconds, asked = double.NegativeInfinity;
        bool ok;
        while (true)
        {
            if (_net.Now - asked >= 0.5) { asked = _net.Now; b.Refresh(); }
            _net.Settle();
            b.Poll();
            if (ok = done()) break;
            if (_net.Now >= end) break;
            _net.Advance();
        }
        log.WriteLine($"(network clock {from:0.000} -> {_net.Now:0.000} s in {wall.Elapsed.TotalSeconds:0.00} s; {_net.Lost} datagrams given up)");
        return ok;
    }

    private TestPeer Host(string name, params int[] servers)
    {
        var p = new TestPeer(_net);
        p.Host = new AnetHost(p.Transport, [.. servers.Select(s => ("127.0.0.1", s))], () => new AnetGame(name, 8, 1, true, Tag("m05")), p.Send, clock: _net.Clock);
        return p;
    }

    [Fact]
    public void AnEntryIsAServerAGameOrBoth()
    {
        Assert.Equal(new ServerSource("A", "alink.example.net", ("alink.example.net", AnetWire.ServerPort), ("alink.example.net", Protocol.DefaultPort)),
            ServerSource.Parse("A", "alink.example.net"));
        Assert.Equal(new ServerSource("B", "alink.example.net:21157", ("alink.example.net", AnetWire.ServerPort), null), ServerSource.Parse("B", "alink.example.net:21157"));
        Assert.Equal(new ServerSource("C", "10.0.0.5:7680", null, ("10.0.0.5", 7680)), ServerSource.Parse("C", "10.0.0.5:7680"));
        var bad = ServerSource.Parse("D", "no such host");
        Assert.False(bad.Valid);
        Assert.False(ServerSource.Parse("E", "").Valid);
        Assert.Equal("no address", new ServerGroup(bad, ServerState.NoAddress, []).Status);
    }

    /// <summary>
    /// Two servers with a game each, a server nobody answers for, a name that is not found and a game at its own address:
    /// each group lists its own games at once, the silent and unknown ones say so on their own rows, and a game listed on
    /// both servers is under both, each copy joined through its own server.
    /// </summary>
    [Fact]
    public void EveryEntryIsSearchedAtOnceAndTheGamesAreGroupedByIt()
    {
        using var one = new FakeAnetServer(_net);
        using var two = new FakeAnetServer(_net);
        using var alpha = Host("Alpha", one.Port);
        using var bravo = Host("Bravo", two.Port);
        using var both = Host("Both", one.Port, two.Port);
        using var direct = new DirectGame("Direct", _net);
        var hosts = new[] { alpha, bravo, both };
        Assert.True(_net.Until(() => hosts.All(h => h.Host!.Servers.All(s => s.Listed)), 5), "hosts not listed");

        using var silent = Silent();
        using var b = Roster(answerWait: 2);
        b.Watch([AnetAt("One", one.Port), AnetAt("Dead", PortOf(silent)), new ServerSource("Nowhere", "nosuch.invalid:7690", null, ("nosuch.invalid", 7690)),
            AnetAt("Two", two.Port), new ServerSource("Lan box", $"127.0.0.1:{direct.Port}", null, ("127.0.0.1", direct.Port)), ServerSource.Parse("Typo", "")]);
        string Names(int g) => string.Join(",", b.Groups[g].Games.Select(f => f.Info.Name).Order());
        Assert.True(Loop(b, () => Names(0) == "Alpha,Both" && Names(3) == "Both,Bravo" && Names(4) == "Direct", 6),
            string.Join("; ", b.Groups.Select(g => $"{g.Source.Name}: {g.Status} [{string.Join(",", g.Games.Select(f => f.Info.Name))}]")));
        // the silent server has not answered yet, and held up nobody
        Assert.NotEqual(ServerState.Online, b.Groups[1].State);
        Assert.Equal(["One", "Dead", "Nowhere", "Two", "Lan box", "Typo"], b.Groups.Select(g => g.Source.Name));
        Assert.Equal((ServerState.Online, "2 games"), (b.Groups[0].State, b.Groups[0].Status));
        Assert.Equal("1 game", b.Groups[4].Status);
        Assert.Equal(("no address", ServerState.NoAddress), (b.Groups[5].Status, b.Groups[5].State));
        Assert.Equal(5, b.Games.Count());

        // each copy of the game two servers list goes through its own server
        var bothHere = b.Groups[0].Games.Single(f => f.Info.Name == "Both");
        var bothThere = b.Groups[3].Games.Single(f => f.Info.Name == "Both");
        Assert.Equal(("127.0.0.1", one.Port), (bothHere.Anet!.Server, bothHere.Anet.Port));
        Assert.Equal(("127.0.0.1", two.Port), (bothThere.Anet!.Server, bothThere.Anet.Port));
        // the game at its own address is joined there, not through a server
        var d = b.Groups[4].Games[0];
        Assert.Null(d.Anet);
        Assert.Equal(direct.Port, d.EndPoint.Port);
        Assert.Equal("Taurus", d.Info.Drivers.Single().Name);

        // the silent server and the unknown name come to say so; the others keep their games meanwhile
        Assert.True(Loop(b, () => b.Groups[1].State == ServerState.NotAnswering && b.Groups[2].State == ServerState.NotFound, 20),
            $"{b.Groups[1].Status}, {b.Groups[2].Status}");
        log.WriteLine(string.Join("; ", b.Groups.Select(g => $"{g.Source.Name}: {g.Status}")));
        Assert.Equal(("not answering", "not found"), (b.Groups[1].Status, b.Groups[2].Status));
        Assert.Equal("Alpha,Both", Names(0));
        Assert.Equal("Both,Bravo", Names(3));

        // a game that goes leaves its group alone; the server stays online with no games
        bravo.Host!.Dispose();
        alpha.Host!.Dispose();
        Assert.True(Loop(b, () => Names(3) == "Both" && Names(0) == "Both", 5), $"{Names(0)} / {Names(3)}");
        both.Host!.Dispose();
        Assert.True(Loop(b, () => b.Groups[0].Games.Count == 0, 5), Names(0));
        Assert.Equal((ServerState.Online, "no games"), (b.Groups[0].State, b.Groups[0].Status));
    }

    /// <summary>The book edited (an entry added, one removed, the rest reordered and renamed): the running searches go on without logging in again.</summary>
    [Fact]
    public void EditingTheBookKeepsTheSearchesThatStay()
    {
        using var one = new FakeAnetServer(_net);
        using var two = new FakeAnetServer(_net);
        using var alpha = Host("Alpha", one.Port);
        using var bravo = Host("Bravo", two.Port);
        using var b = Roster();
        b.Watch([AnetAt("One", one.Port)]);
        Assert.True(Loop(b, () => b.Groups[0].Games.Count == 1, 6));
        Assert.Equal(2, one.Logins);   // (the host's own login, and the browser's)

        b.Watch([AnetAt("Two", two.Port), AnetAt("Server one", one.Port)]);
        Assert.Equal(["Two", "Server one"], b.Groups.Select(g => g.Source.Name));
        // the kept entry still has its game at once, from the same link
        Assert.Equal("Alpha", b.Groups[1].Games.Single().Info.Name);
        Assert.True(Loop(b, () => b.Groups[0].Games.Count == 1, 6));
        Assert.Equal("Bravo", b.Groups[0].Games[0].Info.Name);
        Assert.Equal(2, one.Logins);

        b.Watch([AnetAt("Two", two.Port)]);
        Assert.Single(b.Groups);
        Assert.Equal(["Bravo"], b.Games.Select(f => f.Info.Name));
    }
}
