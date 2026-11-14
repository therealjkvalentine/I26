using System.Diagnostics;
using I76.Net;
using I76.Server;
using I76.Sim.Session;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// The I26 play fork's co-op flow (docs/multiplayer.md "Waiting for the others"): each part between fork builds, behind its
/// <see cref="PeerFeatures"/> bit, and each checked against a peer that lacks it (an upstream build), which must play as before.
/// </summary>
public class CoopFlowTests(ITestOutputHelper output)
{
    /// <summary>A hosted co-op game in its lobby, and its players' clients (two simulated games on this machine).</summary>
    internal sealed class Game : IDisposable
    {
        public NetMeleeServer Server { get; }
        public readonly List<(string Name, MeleeClient Client)> Players = [];
        private readonly ITestOutputHelper _out;
        public readonly List<string> Log = [];

        public Game(string install, ITestOutputHelper output, string mission = "t01", double loadSeconds = 30, PeerFeatures? hostFeatures = null, bool listenHost = true)
        {
            _out = output;
            Server = new NetMeleeServer(new ServerOptions
            {
                Install = install, Arena = mission, Coop = mission, MaxPlayers = 4, Port = 0, Seed = 7, Name = "co-op", ListenHost = listenHost,
                Lobby = true, LoadSeconds = loadSeconds, Features = hostFeatures ?? Protocol.Features,
            });
            Server.Log += m => { lock (Log) Log.Add(m); output.WriteLine($"[server] {m}"); };
            Server.Start();
        }

        /// <summary>A player joins the lobby; <paramref name="upstream"/> plays an upstream build (it says none of the fork's features).</summary>
        public MeleeClient Join(string name, bool upstream = false, string car = Protocol.MissionCar)
        {
            var c = new MeleeClient { Features = upstream ? PeerFeatures.None : Protocol.Features };
            c.Log += m => _out.WriteLine($"[{name}] {m}");
            c.Connect("127.0.0.1", Server.Port, name, car);
            Players.Add((name, c));
            Assert.True(Wait(() => c.State == ClientState.InLobby && c.Lobby!.Members.Any(m => m.Name == name)), $"{name} did not reach the lobby: {c.State} {c.Reason}");
            return c;
        }

        /// <summary>The host starts the mission with everyone, ready or not; every game has its accept.</summary>
        public void Start()
        {
            Assert.Null(Server.StartLobby(force: true));
            Assert.True(Wait(() => Players.All(p => p.Client.State == ClientState.Joined)), string.Join(", ", Players.Select(p => $"{p.Name} {p.Client.State}")));
        }

        public bool Wait(Func<bool> done, int ms = 10000)
        {
            var t = Stopwatch.StartNew();
            while (!done() && t.ElapsedMilliseconds < ms)
            {
                foreach (var (_, c) in Players) c.Poll();
                Thread.Sleep(5);
            }
            return done();
        }

        /// <summary>Waits while the given games send an input every few milliseconds (their missions are built and running).</summary>
        public bool WaitDriving(Func<bool> done, IEnumerable<MeleeClient> driving, int ms = 10000)
        {
            var d = driving.ToList();
            return Wait(() => { foreach (var c in d) c.SendInput(new CarInput()); return done(); }, ms);
        }

        public void Pump(int ms) => Wait(() => false, ms);
        public uint Tick => Server.Invoke(h => (uint)h.Tick);

        public void Dispose()
        {
            foreach (var (_, c) in Players) c.Dispose();
            Server.Dispose();
        }
    }

    private static string? Install => HostTests.InstallDir;

    // ---- the start's wait notice ----

    [Fact]
    public void TheWaitTravelsOnTheWire()
    {
        var wait = new CoopWait(87, [("Ann", CoopStage.Briefing), ("Bob", CoopStage.Building), ("Cy", CoopStage.Unknown)]);
        var w = new NetWriter();
        wait.Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.CoopWait, (MsgType)r.U8());
        var back = CoopWait.Read(ref r);
        Assert.Equal(87, back.SecondsLeft);
        Assert.Equal(wait.Players, back.Players);
        // a stage this build does not know is read as unknown, not refused
        Assert.Equal(CoopStage.Unknown, CoopStages.Of(200));
        Assert.Equal(CoopStage.Ending, CoopStages.Of(5));
        // the fork's numbers sit apart from upstream's, and the bit is the fork's own
        Assert.True((byte)MsgType.CoopStage >= 60 && (byte)MsgType.CoopWait >= 60);
        Assert.Equal(PeerFeatures.CoopWait, Protocol.Features & PeerFeatures.CoopWait);
    }

    [Fact]
    public void TheNoticeNamesWhoIsWaitedForAndWhatTheyAreDoing()
    {
        var wait = new CoopWait(87, [("Ann", CoopStage.Briefing), ("Bob", CoopStage.Building), ("Cy", CoopStage.Unknown)]);
        Assert.Equal(["Waiting for 3 players", "Ann: watching the briefing", "Bob: loading", "Cy: loading or watching the briefing", "starting without them in 80 s"],
            wait.Lines(79.4));
        Assert.Equal(["Waiting for 1 player", "Ann: watching the ending"], new CoopWait(0, [("Ann", CoopStage.Ending)]).Lines(0));
        Assert.Empty(new CoopWait(10, []).Lines(10));
        // the seats that have sent an input are not waited for
        var seats = new[] { new CoopStart.Seat("Hal", true, CoopStage.Ready), new CoopStart.Seat("Ann", false, CoopStage.Briefing), new CoopStart.Seat("Cy", false, CoopStage.Unknown) };
        Assert.Equal([("Ann", CoopStage.Briefing), ("Cy", CoopStage.Unknown)], CoopStart.Waiting(seats));
    }

    /// <summary>Hal is in the mission and stands still; Ann watches the briefing: Hal's game hears whom it waits for, and that it is over when she comes.</summary>
    [Fact]
    public void TheStartSaysWhomItWaitsFor()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        Assert.True(hal.Has(PeerFeatures.CoopWait));
        hal.SendCoopStage(CoopStage.Ready);
        ann.SendCoopStage(CoopStage.Briefing);
        Assert.True(g.WaitDriving(() => hal.StartWait is { Players: [("Ann", CoopStage.Briefing)] }, [hal]), $"Hal's notice: {Describe(hal.StartWait)}");
        Assert.InRange(hal.StartWait!.SecondsLeft, 1, 30);
        output.WriteLine($"Hal sees: {string.Join(" / ", hal.StartWait.Lines(hal.StartWait.SecondsLeft))}");
        // Ann's mission builds: the notice follows
        ann.SendCoopStage(CoopStage.Building);
        Assert.True(g.WaitDriving(() => hal.StartWait is { Players: [("Ann", CoopStage.Building)] }, [hal]), Describe(hal.StartWait));
        // her game is up: the start goes, and the notice with it
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => hal.StartWait is { Players.Count: 0 } && g.Tick > tick + 3, [hal, ann]), Describe(hal.StartWait));
        Assert.Contains(g.Log, l => l.Contains("the start waits for Ann (watching the briefing)"));
    }

    /// <summary>An upstream player (no CoopWait) is named without a stage, is told nothing, and the mission still waits for its first input as upstream's does.</summary>
    [Fact]
    public void AnUpstreamPlayerIsWaitedForUnnamedStageAndToldNothing()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output);
        var hal = g.Join("Hal");
        var old = g.Join("Old", upstream: true);
        g.Start();
        Assert.False(old.Has(PeerFeatures.CoopWait));
        old.SendCoopStage(CoopStage.Briefing);      // says nothing to the host: the feature is not on between them
        Assert.True(g.WaitDriving(() => hal.StartWait is { Players: [("Old", CoopStage.Unknown)] }, [hal]), Describe(hal.StartWait));
        uint tick = g.Tick;
        g.Pump(300);
        Assert.Equal(tick, g.Tick);
        Assert.Null(old.StartWait);
        Assert.True(g.WaitDriving(() => g.Tick > tick + 3, [hal, old]));
        Assert.Null(old.StartWait);
    }

    /// <summary>A fork player in an upstream host's game: nothing of the fork's is said either way, and the start is upstream's.</summary>
    [Fact]
    public void AnUpstreamHostSaysNothingAndHearsNothing()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, hostFeatures: PeerFeatures.Pistol | PeerFeatures.HandicapBoard | PeerFeatures.Migration);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        Assert.False(hal.Has(PeerFeatures.CoopWait));
        ann.SendCoopStage(CoopStage.Briefing);
        g.WaitDriving(() => false, [hal], 600);
        Assert.Null(hal.StartWait);
        Assert.DoesNotContain(g.Log, l => l.StartsWith("co-op: Ann is", StringComparison.Ordinal));
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 3, [hal, ann]));
    }

    // ---- the briefing for everyone ----

    [Fact]
    public void OneSkipEndsTheBriefingsOnlyOnceEveryGameHasLoaded()
    {
        CoopStart.Seat S(string n, bool input, CoopStage st, bool skipped = false) => new(n, input, st, skipped);
        // Hal skipped and plays; Ann watches over her built mission: hers ends, Hal's name on it
        Assert.Equal("Hal", CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready, true), S("Ann", false, CoopStage.Briefing)]));
        // Bob still builds: nobody's briefing is cut short until he has loaded
        Assert.Null(CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready, true), S("Ann", false, CoopStage.Briefing), S("Bob", false, CoopStage.Building)]));
        // an upstream game says nothing: loaded only once its inputs come
        Assert.Null(CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready, true), S("Ann", false, CoopStage.Briefing), S("Old", false, CoopStage.Unknown)]));
        Assert.Equal("Hal", CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready, true), S("Ann", false, CoopStage.Briefing), S("Old", true, CoopStage.Unknown)]));
        // nobody skipped, or alone: nothing ends
        Assert.Null(CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready), S("Ann", false, CoopStage.Briefing)]));
        Assert.Null(CoopStart.SkippedBy([S("Hal", false, CoopStage.Skipped, true)]));
        // a game still watching the last mission's ending has not loaded this one
        Assert.Null(CoopStart.SkippedBy([S("Hal", true, CoopStage.Ready, true), S("Ann", false, CoopStage.Ending)]));
        var w = new NetWriter();
        new CoopSkip("Hal").Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.CoopSkip, (MsgType)r.U8());
        Assert.Equal("Hal skipped the briefing", CoopSkip.Read(ref r).Line);
    }

    /// <summary>Hal skips his briefing; Ann's mission still builds, so hers is not cut; once she watches it over her built mission, it ends.</summary>
    [Fact]
    public void ASkipEndsTheOthersBriefingOnceTheyHaveLoaded()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        Assert.True(hal.Has(PeerFeatures.CoopSkip));
        hal.SendCoopStage(CoopStage.Briefing);
        hal.SendCoopStage(CoopStage.Skipped);
        hal.SendCoopStage(CoopStage.Ready);
        ann.SendCoopStage(CoopStage.Building);
        g.WaitDriving(() => false, [hal], 700);
        Assert.Null(ann.SkippedBy);
        ann.SendCoopStage(CoopStage.Briefing);
        Assert.True(g.WaitDriving(() => ann.SkippedBy is not null, [hal]), "Ann's briefing was not ended");
        Assert.Equal("Hal", ann.SkippedBy!.Name);
        Assert.Null(hal.SkippedBy);
        // she comes in; the mission starts for both
        ann.SendCoopStage(CoopStage.Ready);
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 3, [hal, ann]));
        Assert.Contains(g.Log, l => l.Contains("Ann's briefing ends: everyone has loaded and Hal skipped theirs"));
    }

    /// <summary>An upstream player says nothing: the others' briefings wait for its inputs (it is loaded only then), and it is never sent the fork's word.</summary>
    [Fact]
    public void AnUpstreamPlayerIsNeverCutShortAndHoldsTheSkipUntilItsInputsCome()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        var old = g.Join("Old", upstream: true);
        g.Start();
        hal.SendCoopStage(CoopStage.Skipped);
        hal.SendCoopStage(CoopStage.Ready);
        ann.SendCoopStage(CoopStage.Briefing);
        g.WaitDriving(() => false, [hal], 700);
        Assert.Null(ann.SkippedBy);
        // the upstream game has built its mission (its inputs come): Ann's briefing ends
        Assert.True(g.WaitDriving(() => ann.SkippedBy is not null, [hal, old]), "Ann's briefing was not ended once the upstream game had loaded");
        Assert.Null(old.SkippedBy);
        Assert.DoesNotContain(g.Log, l => l.Contains("Old's briefing ends"));
    }

    /// <summary>A host without CoopSkip (upstream's): the fork's game keeps upstream's order (the briefing before the build) and hears no skip.</summary>
    [Fact]
    public void AnUpstreamHostEndsNoBriefing()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, hostFeatures: PeerFeatures.Pistol | PeerFeatures.HandicapBoard | PeerFeatures.Migration | PeerFeatures.CoopWait);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        Assert.False(hal.Has(PeerFeatures.CoopSkip));
        Assert.True(hal.Has(PeerFeatures.CoopWait));
        hal.SendCoopStage(CoopStage.Skipped);
        hal.SendCoopStage(CoopStage.Ready);
        ann.SendCoopStage(CoopStage.Briefing);
        g.WaitDriving(() => false, [hal], 800);
        Assert.Null(ann.SkippedBy);
        // its Briefing is before its build there: the notice says so without claiming she has loaded
        Assert.True(g.WaitDriving(() => hal.StartWait is { Players: [("Ann", CoopStage.Unknown)] }, [hal]), Describe(hal.StartWait));
    }

    internal static string Describe(CoopWait? w) => w is null ? "none" : $"{w.SecondsLeft} s: {string.Join(", ", w.Players.Select(p => $"{p.Name} {p.Stage}"))}";
}
