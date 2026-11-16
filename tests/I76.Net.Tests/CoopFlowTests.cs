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

        public Game(string install, ITestOutputHelper output, string mission = "t01", double loadSeconds = 30, PeerFeatures? hostFeatures = null, bool listenHost = true, double nextSeconds = 15, bool wrecksSolid = false)
        {
            _out = output;
            Server = new NetMeleeServer(new ServerOptions
            {
                Install = install, Arena = mission, Coop = mission, MaxPlayers = 4, Port = 0, Seed = 7, Name = "co-op", ListenHost = listenHost,
                Lobby = true, LoadSeconds = loadSeconds, Features = hostFeatures ?? Protocol.Features, CoopNextSeconds = nextSeconds, WrecksStaySolid = wrecksSolid,
            });
            Server.Log += m => { lock (Log) Log.Add(m); output.WriteLine($"[server] {m}"); };
            Server.Start();
        }

        /// <summary>
        /// A player joins the lobby; <paramref name="upstream"/> plays an upstream build (it says none of the fork's features). A fork game says it
        /// builds the mission once each accept is in, as the game does (<paramref name="building"/> false leaves that to the test).
        /// </summary>
        public MeleeClient Join(string name, bool upstream = false, string car = Protocol.MissionCar, bool building = true)
        {
            var c = new MeleeClient { Features = upstream ? PeerFeatures.None : Protocol.Features };
            if (!upstream && building) c.Joined += _ => c.SendCoopStage(CoopStage.Building);
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

        /// <summary>Everyone's game sends inputs until the mission runs (every game loaded), then the host's script ends it.</summary>
        public void PlayAndEnd(bool won)
        {
            uint tick = Tick;
            Assert.True(WaitDriving(() => Tick > tick + 5, Players.Select(p => p.Client)), "the mission did not start");
            Server.Invoke(h => h.Setup.Script!.Host.EndMission(won, 0, won ? -2 : 0));
        }
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

    // ---- on to the next mission ----

    [Fact]
    public void TheTripGoesOnInItsOrderAndStopsAfterItsLast()
    {
        var w = new NetWriter();
        new CoopPlan(CoopPlanKind.Next, "t06", 15).Write(w);
        var r = new NetReader(w.ToArray());
        Assert.Equal(MsgType.CoopNext, (MsgType)r.U8());
        Assert.Equal(new CoopPlan(CoopPlanKind.Next, "t06", 15), CoopPlan.Read(ref r));
        Assert.Equal(["Next mission: Scene 6: Out of Gas in 12 s", "the same players, cars and settings"], new CoopPlan(CoopPlanKind.Next, "t06", 15).Lines("Scene 6: Out of Gas", 11.2, host: false));
        Assert.Equal("MISSION COMPLETE", CoopPlan.Head(true));
        if (Install is not { } install) return;
        var i = new I76.Formats.Vfs.GameInstall(install);
        if (i.IsNitro) return;
        Assert.Equal("t06", I76.Formats.Mission.MissionTitles.NextCoop(i, "t05"));
        Assert.Equal("t17", I76.Formats.Mission.MissionTitles.NextCoop(i, "T16"));
        Assert.Null(I76.Formats.Mission.MissionTitles.NextCoop(i, "t17"));
        // no trip's: training and the scenarios end in the lobby
        Assert.Null(I76.Formats.Mission.MissionTitles.NextCoop(i, "a01"));
        Assert.Null(I76.Formats.Mission.MissionTitles.NextCoop(i, "s02"));
        Assert.Null(I76.Formats.Mission.MissionTitles.NextCoop(i, "m01"));
    }

    /// <summary>A won mission plans the next with a countdown; when it is due the players go on together: the same seats, cars and settings, no lobby.</summary>
    [Fact]
    public void AWonMissionGoesOnToTheNextWithTheSamePlayersAndCars()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 2);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann", car: "vcgcour1.vcf");
        g.Start();
        g.PlayAndEnd(won: true);
        Assert.True(g.WaitDriving(() => hal.Plan is not null && ann.Plan is not null, [hal, ann]), "no plan came");
        Assert.Equal(new CoopPlan(CoopPlanKind.Next, "t06", 2), ann.Plan);
        // when it is due: the next round, then the accept, on the same connection
        int rounds = hal.Rounds;
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.Rounds > rounds && p.Client.State == ClientState.Joined), 15000),
            string.Join(", ", g.Players.Select(p => $"{p.Name} {p.Client.State} round {p.Client.Rounds}")));
        Assert.All(g.Players, p => Assert.Equal("t06", p.Client.Accept!.Arena));
        Assert.Equal([0, 1], g.Players.Select(p => (int)p.Client.Accept!.Coop!.Seat));
        Assert.Equal("vcgcour1.vcf", ann.Accept!.Car);
        Assert.Null(hal.Lobby);
        Assert.Null(g.Server.CoopPlanNow);
        Assert.Equal(1, g.Server.Round);
        // the next mission's start waits for both again
        uint tick = g.Tick;
        g.WaitDriving(() => false, [hal], 400);
        Assert.Equal(tick, g.Tick);
        Assert.True(g.WaitDriving(() => g.Tick > tick + 3, [hal, ann]));
    }

    /// <summary>The host need not wait: Back to Lobby takes everyone back with their cars and ready marks, and the plain start goes again.</summary>
    [Fact]
    public void TheHostTakesEveryoneBackToTheLobbyReadyWithTheirCars()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05");
        var hal = g.Join("Hal");
        var ann = g.Join("Ann", car: "vcgcour1.vcf");
        g.Start();
        g.PlayAndEnd(won: true);
        Assert.True(g.WaitDriving(() => ann.Plan is not null, [hal, ann]));
        Assert.Equal(CoopPlanKind.Next, g.Server.CoopPlanNow!.Kind);
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Lobby));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.InLobby && p.Client.Lobby!.Members.Count == 2)),
            string.Join(", ", g.Players.Select(p => $"{p.Name} {p.Client.State}")));
        var members = hal.Lobby!.Members;
        Assert.Equal(["Hal", "Ann"], members.Select(m => m.Name));
        Assert.All(members, m => Assert.True(m.Ready));
        Assert.Equal("vcgcour1.vcf", members[1].Car);
        Assert.True(hal.Lobby.IsHost);
        Assert.Equal("t05", hal.Lobby.Mission);
        Assert.True(g.Server.InLobby);
        // everyone is ready: the plain start goes
        Assert.Null(g.Server.StartLobby());
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.Joined)));
        Assert.Equal("vcgcour1.vcf", ann.Accept!.Car);
    }

    /// <summary>The host's Next Mission goes at once, without waiting for the countdown.</summary>
    [Fact]
    public void TheHostsNextMissionGoesNow()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 60);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        g.PlayAndEnd(won: true);
        Assert.True(g.WaitDriving(() => ann.Plan is not null, [hal, ann]));
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Next));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.Joined && p.Client.Accept!.Arena == "t06")));
    }

    [Fact]
    public void AfterTheTripsLastMissionTheGroupIsBackInTheLobby()
    {
        if (Install is not { } install) return;
        if (new I76.Formats.Vfs.GameInstall(install).IsNitro) return;
        using var g = new Game(install, output, mission: "t17", nextSeconds: 1);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        g.PlayAndEnd(won: true);
        Assert.True(g.WaitDriving(() => ann.Plan is not null, [hal, ann]));
        Assert.Equal(CoopPlanKind.Lobby, ann.Plan!.Kind);
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.InLobby), 15000));
        Assert.All(hal.Lobby!.Members, m => Assert.True(m.Ready));
    }

    /// <summary>An upstream player hears no plan and is let go with the reason at the next mission (its game leaves a won mission anyway); the others go on.</summary>
    [Fact]
    public void AnUpstreamPlayerIsLetGoAtTheNextMission()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1);
        var hal = g.Join("Hal");
        var old = g.Join("Old", upstream: true);
        g.Start();
        g.PlayAndEnd(won: true);
        Assert.True(g.Wait(() => old.State == ClientState.Disconnected, 15000), $"{old.State}");
        Assert.Equal(NetMeleeServer.CoopLeftBehindText, old.Reason);
        Assert.Null(old.Plan);
        Assert.True(g.Wait(() => hal.State == ClientState.Joined && hal.Accept!.Arena == "t06", 15000));
        Assert.Equal(1, g.Server.Invoke(h => h.Seats.Count));
    }

    /// <summary>A host without CoopNext (upstream's): the won mission ends the game as before, its game over saying nothing follows.</summary>
    [Fact]
    public void AnUpstreamHostEndsTheGameAtTheMissionsEnd()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1,
            hostFeatures: PeerFeatures.Pistol | PeerFeatures.HandicapBoard | PeerFeatures.Migration | PeerFeatures.CoopWait | PeerFeatures.CoopSkip);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        g.PlayAndEnd(won: true);
        GameOverEvent? over = null;
        Assert.True(g.WaitDriving(() =>
        {
            while (hal.Events.TryDequeue(out var b)) over ??= b.Events.OfType<GameOverEvent>().FirstOrDefault();
            return over is not null;
        }, [hal, ann], 15000), "no game over");
        Assert.Equal(0, over!.NextRoundSeconds);
        g.Pump(1500);
        Assert.Null(hal.Plan);
        Assert.Equal(0, g.Server.Round);
        Assert.Null(g.Server.CoopPlanNow);
    }


    // ---- replay and back to the lobby ----

    /// <summary>A failed mission waits for the host (no countdown); its Replay Mission starts it again for everyone, the same seats and cars.</summary>
    [Fact]
    public void AFailedMissionWaitsForTheHostWhoReplaysItForEveryone()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann", car: "vcgcour1.vcf");
        g.Start();
        g.PlayAndEnd(won: false);
        Assert.True(g.WaitDriving(() => ann.Plan is not null, [hal, ann]));
        Assert.Equal(new CoopPlan(CoopPlanKind.Host, "t05", 0), ann.Plan);
        Assert.Equal(["Waiting for the host to choose"], ann.Plan!.Lines("Scene 5: Save Taurus", 0, host: false));
        // nothing happens by itself
        int rounds = hal.Rounds;
        g.Pump(2500);
        Assert.Equal(rounds, hal.Rounds);
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Replay));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.Rounds > rounds && p.Client.State == ClientState.Joined)));
        Assert.All(g.Players, p => Assert.Equal("t05", p.Client.Accept!.Arena));
        Assert.Equal([0, 1], g.Players.Select(p => (int)p.Client.Accept!.Coop!.Seat));
        Assert.Equal("vcgcour1.vcf", ann.Accept!.Car);
        Assert.Contains(g.Log, l => l.Contains("the host chooses to replay the mission"));
    }

    /// <summary>
    /// The old mission's inputs (in flight at the carry, or the old scene's before it reloads) do not count as the new mission's games loaded:
    /// a carried game's inputs are taken only once it says it builds the new mission.
    /// </summary>
    [Fact]
    public void TheLastMissionsInputsDoNotStartTheNext()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05");
        var hal = g.Join("Hal", building: false);
        var ann = g.Join("Ann", building: false);
        g.Start();
        hal.SendCoopStage(CoopStage.Building);
        ann.SendCoopStage(CoopStage.Building);
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]));
        // the host replays while both games keep driving through it, saying nothing of the new mission
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Replay));
        Assert.True(g.WaitDriving(() => g.Players.All(p => p.Client.Rounds == 1 && p.Client.State == ClientState.Joined), [hal, ann]));
        tick = g.Tick;
        g.WaitDriving(() => false, [hal, ann], 1500);
        Assert.Equal(tick, g.Tick);
        Assert.DoesNotContain(g.Log.ToList().SkipWhile(l => !l.Contains("again: t05")), l => l.Contains("every player has loaded"));
        // they say they build it: their inputs count, and it starts
        hal.SendCoopStage(CoopStage.Building);
        ann.SendCoopStage(CoopStage.Building);
        Assert.True(g.WaitDriving(() => g.Tick > tick + 3, [hal, ann]));
    }

    /// <summary>The host's Esc menu replays the mission, or takes everyone back to the lobby, while it is still being played.</summary>
    [Fact]
    public void TheHostReplaysOrGoesBackToTheLobbyFromTheMission()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05");
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]));
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Replay));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.Rounds == 1 && p.Client.State == ClientState.Joined)));
        // in the replay: back to the lobby, everyone kept and ready
        tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal, ann]));
        Assert.NotNull(g.Server.CoopChoose(CoopPlanKind.Next));      // no next mission in a mission still played
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Lobby));
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.State == ClientState.InLobby && p.Client.Lobby!.Members.Count == 2)));
        Assert.All(hal.Lobby!.Members, m => Assert.True(m.Ready));
        Assert.NotNull(g.Server.CoopChoose(CoopPlanKind.Replay));    // the lobby is the host's START now
    }

    /// <summary>An upstream player follows a replay of a failed mission and the lobby (its client reads a next round as a join), with no plan said to it.</summary>
    [Fact]
    public void AnUpstreamPlayerFollowsAReplayAndTheLobby()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05");
        var hal = g.Join("Hal");
        var old = g.Join("Old", upstream: true);
        g.Start();
        g.PlayAndEnd(won: false);
        Assert.True(g.WaitDriving(() => hal.Plan is not null, [hal, old]));
        Assert.Null(old.Plan);
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Replay));
        Assert.True(g.Wait(() => old.Rounds == 1 && old.State == ClientState.Joined && old.Accept!.Arena == "t05"), $"{old.State} {old.Reason}");
        Assert.Equal(2, g.Server.Invoke(h => h.Seats.Count));
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Lobby));
        Assert.True(g.Wait(() => old.State == ClientState.InLobby && old.Lobby!.Members.Count == 2), $"{old.State}");
    }

    /// <summary>A dedicated server has no host to choose: a failed mission goes back to the lobby after the hold, everyone ready, and starts itself.</summary>
    [Fact]
    public void ADedicatedServerGoesBackToTheLobbyAfterAFailure()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1, listenHost: false);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        g.PlayAndEnd(won: false);
        Assert.True(g.WaitDriving(() => ann.Plan is not null, [hal, ann]));
        Assert.Equal(CoopPlanKind.Lobby, ann.Plan!.Kind);
        // everyone is back and ready, so the dedicated server's lobby starts the mission again by itself
        Assert.True(g.Wait(() => g.Players.All(p => p.Client.Rounds >= 1 && p.Client.State == ClientState.Joined), 15000),
            string.Join(", ", g.Players.Select(p => $"{p.Name} {p.Client.State}")));
    }

    /// <summary>A host without CoopNext: a failure ends the game as before (each player's end menu; nothing follows).</summary>
    [Fact]
    public void AnUpstreamHostEndsAFailedGameAsBefore()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", hostFeatures: PeerFeatures.Pistol | PeerFeatures.HandicapBoard | PeerFeatures.Migration);
        var hal = g.Join("Hal");
        var ann = g.Join("Ann");
        g.Start();
        g.PlayAndEnd(won: false);
        g.WaitDriving(() => false, [hal, ann], 1500);
        Assert.Null(hal.Plan);
        Assert.Null(g.Server.CoopPlanNow);
        Assert.NotNull(g.Server.CoopChoose(CoopPlanKind.Replay));
    }

    /// <summary>
    /// The host's line names the key that goes on now (Enter, a melee's START NOW's) and the menu key for the rest; Esc is the menu's, never
    /// the skip (the owner: "esc to skip to next mission doesn't work that is for the menu"). Enter goes on only for the host, only to a next mission.
    /// </summary>
    [Fact]
    public void TheHostsNoticeNamesEnterForNextMissionAndEscForTheMenu()
    {
        var next = new CoopPlan(CoopPlanKind.Next, "t06", 15);
        Assert.Equal(["Next mission: Scene 6: Out of Gas in 12 s", "Enter: Next Mission now   Esc: menu (Replay, Lobby)"], next.Lines("Scene 6: Out of Gas", 11.2, host: true));
        // the menu key as the player has bound it
        Assert.Equal("Enter: Next Mission now   F1: menu (Replay, Lobby)", next.Lines("Scene 6: Out of Gas", 0, host: true, menu: "F1")[1]);
        Assert.Equal(["Esc: menu (Replay Mission or Back to Lobby)"], new CoopPlan(CoopPlanKind.Host, "t05", 0).Lines("Scene 5: Save Taurus", 0, host: true));
        Assert.All(new[] { next, new CoopPlan(CoopPlanKind.Host, "t05", 0) }, p => Assert.DoesNotContain(p.Lines("x", 0, host: false), l => l.Contains("Esc")));
        Assert.True(CoopPlan.NowKeyGoes(next, host: true));
        Assert.False(CoopPlan.NowKeyGoes(next, host: false));
        Assert.False(CoopPlan.NowKeyGoes(new CoopPlan(CoopPlanKind.Host, "t05", 0), host: true));
        Assert.False(CoopPlan.NowKeyGoes(new CoopPlan(CoopPlanKind.Lobby, "t17", 15), host: true));
        Assert.False(CoopPlan.NowKeyGoes(null, host: true));
    }

    // ---- one player (the owner: "okay fine on one player coop") ----

    /// <summary>
    /// The host alone: START needs nobody's ready mark, the mission waits only for the host's own game, no wait names anybody else, the won
    /// mission goes on to the trip's next with the host in seat 0, and that one starts too.
    /// </summary>
    [Fact]
    public void AHostAloneStartsPlaysAndGoesOnToTheNextMission()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05", nextSeconds: 1);
        var hal = g.Join("Hal");
        Assert.True(hal.Lobby!.IsHost);
        Assert.Single(hal.Lobby.Members);
        // the plain start (not forced): there is nobody to be ready
        Assert.Null(g.Server.StartLobby());
        Assert.True(g.Wait(() => hal.State == ClientState.Joined), $"{hal.State} {hal.Reason}");
        Assert.Equal(0, (int)hal.Accept!.Coop!.Seat);
        var waits = new List<string>();
        var named = new HashSet<string>();
        void Seen() { if (hal.StartWait is { } w) { waits.Add(Describe(w)); foreach (var p in w.Players) named.Add(p.Name); } }
        // the host's briefing over the built mission: the start waits for it alone
        hal.SendCoopStage(CoopStage.Briefing);
        uint tick = g.Tick;
        g.Wait(() => { Seen(); return false; }, 500);
        Assert.Equal(tick, g.Tick);
        hal.SendCoopStage(CoopStage.Skipped);
        hal.SendCoopStage(CoopStage.Ready);
        Assert.True(g.WaitDriving(() => { Seen(); return g.Tick > tick + 5; }, [hal]), "the mission did not start for the host alone");
        Assert.Contains(g.Log, l => l.Contains("every player has loaded: the mission starts"));
        // nobody else's briefing to end, and no wait for players who are not there
        Assert.DoesNotContain(g.Log, l => l.Contains("briefing ends"));
        output.WriteLine($"Hal's waits: {(waits.Count == 0 ? "none" : string.Join(" | ", waits.Distinct()))}");
        // the host's own game is never told to wait for itself: alone, nobody is named
        Assert.Empty(named);
        // won: the next mission, the host alone in seat 0, and its start goes when the host's game is up
        g.Server.Invoke(h => h.Setup.Script!.Host.EndMission(true, 0, -2));
        Assert.True(g.WaitDriving(() => hal.Plan is not null, [hal]), "no plan came");
        Assert.Equal(new CoopPlan(CoopPlanKind.Next, "t06", 1), hal.Plan);
        Assert.True(g.Wait(() => hal.Rounds == 1 && hal.State == ClientState.Joined, 15000), $"{hal.State} round {hal.Rounds}");
        Assert.Equal("t06", hal.Accept!.Arena);
        Assert.Equal(0, (int)hal.Accept.Coop!.Seat);
        Assert.Equal(1, g.Server.Invoke(h => h.Seats.Count));
        hal.SendCoopStage(CoopStage.Building);
        tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal]), "the next mission did not start for the host alone");
    }

    /// <summary>The host alone fails and replays the mission; a player who comes in later is seated beside the host, as a late joiner is.</summary>
    [Fact]
    public void AHostAloneReplaysAndAPlayerCanJoinLater()
    {
        if (Install is not { } install) return;
        using var g = new Game(install, output, mission: "t05");
        var hal = g.Join("Hal");
        Assert.Null(g.Server.StartLobby());
        Assert.True(g.Wait(() => hal.State == ClientState.Joined));
        g.PlayAndEnd(won: false);
        Assert.True(g.WaitDriving(() => hal.Plan is not null, [hal]));
        Assert.Equal(CoopPlanKind.Host, hal.Plan!.Kind);
        Assert.Null(g.Server.CoopChoose(CoopPlanKind.Replay));
        Assert.True(g.Wait(() => hal.Rounds == 1 && hal.State == ClientState.Joined && hal.Accept!.Arena == "t05"), $"{hal.State}");
        uint tick = g.Tick;
        Assert.True(g.WaitDriving(() => g.Tick > tick + 5, [hal]));
        // the game has started: a joiner is not put in a lobby but seated in the mission where it is (seat 1, beside the host)
        var ann = new MeleeClient { Features = Protocol.Features };
        g.Players.Add(("Ann", ann));
        ann.Connect("127.0.0.1", g.Server.Port, "Ann", Protocol.MissionCar);
        Assert.True(g.WaitDriving(() => ann.State == ClientState.Joined, [hal]), $"Ann: {ann.State} {ann.Reason}");
        Assert.Equal(1, (int)ann.Accept!.Coop!.Seat);
        Assert.Equal(2, g.Server.Invoke(h => h.Seats.Count));
    }

    // ---- the co-op list's lines ----

    /// <summary>Each co-op mission has a line saying what it is about: its first objective, in the mission's own words, read from the install.</summary>
    [Fact]
    public void EveryCoopMissionHasALineSayingWhatItIsAbout()
    {
        if (Install is not { } install) return;
        var i = new I76.Formats.Vfs.GameInstall(install);
        var vfs = new I76.Formats.Vfs.AssetVfs(i);
        var list = I76.Formats.Mission.MissionTitles.Coop(i);
        Assert.NotEmpty(list);
        int with = 0;
        foreach (var m in list)
        {
            string line = I76.Formats.Mission.MissionTitles.CoopSummary(i, m.Stem, vfs);
            output.WriteLine($"{m.Stem} {m.Title}: {line}");
            if (line.Length > 0) with++;
            Assert.DoesNotContain("(hidden)", line, StringComparison.OrdinalIgnoreCase);
        }
        // every stock story mission has objective text; a missing line is a reading fault
        Assert.Equal(list.Count, with);
        Assert.Same(I76.Formats.Mission.MissionTitles.CoopSummary(i, list[0].Stem), I76.Formats.Mission.MissionTitles.CoopSummary(i, list[0].Stem));
    }

    internal static string Describe(CoopWait? w) => w is null ? "none" : $"{w.SecondsLeft} s: {string.Join(", ", w.Players.Select(p => $"{p.Name} {p.Stage}"))}";
}
