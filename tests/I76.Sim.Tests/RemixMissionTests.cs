using System.Numerics;
using System.Security.Cryptography;
using I76.Formats.Fsm;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Save;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The remix generator's missions (docs/remix-missions.md), built from the install into a scratch folder: the build is
/// repeatable and round-trips through the script compiler, never touches the install, and each mission's win and fail
/// paths fire when played headless; a run is deterministic and a mid-mission snapshot restores into a rebuilt mission.
/// </summary>
public class RemixMissionTests
{
    private static readonly object Gate = new();
    private static string? _dir;

    /// <summary>The shipped descriptions built once into a scratch folder, which GameInstall.UserMissionDir then names.</summary>
    private static string? Built()
    {
        if (SnapshotRig.Install is not { } inst) return null;
        lock (Gate)
        {
            if (_dir is not null) return _dir;
            var dir = Path.Combine(Path.GetTempPath(), "i26-remix-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = dir;
            var install = new GameInstall(inst);
            foreach (var (_, yaml) in RemixCatalogue.Builtin()) RemixBuilder.Build(install, RemixSpec.Parse(yaml)).WriteTo(dir);
            return _dir = dir;
        }
    }

    private static (SimSession S, MissionSetup Setup) Make(string mission, uint seed = 1)
    {
        var l = new SnapshotRig.Loaded(SnapshotRig.Install!);
        return SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, "vppirna1.vcf"), SimRate.R20, seed);
    }

    private static void Step(SimSession s, int ticks = 1)
    {
        for (int i = 0; i < ticks && s.Script!.Result is null; i++) { s.Tick([new CarInput()]); SnapshotRig.TakeEvents(s); }
    }

    private static void MovePlayer(MissionSetup setup, float x, float z)
    {
        var p = setup.World.Player!;
        p.Pos = new Double3(x, setup.Ground.BareHeight(x, z), z);
        p.Vel = default;
    }

    /// <summary>The first point of a path of a generated mission, by its label.</summary>
    private static Vector3 PathStart(string mission, string label)
    {
        var prog = new MissionFile(GameInstall.InUserMissionDir(mission + ".msn")!).ReadFsmProgram()!;
        var path = prog.Paths.FirstOrDefault(p => p.Name.Equals(label, StringComparison.OrdinalIgnoreCase));
        Assert.True(path is not null, $"no path {label}");
        return path!.Points[0];
    }

    private static int FailLine(SimSession s) => s.Script!.Host.End is { FailMessage: var m } ? m + 1 : 0;

    [Fact]
    public void EveryShippedRemixBuildsTheSameBytesTwiceRoundTripsAndLeavesTheInstallAlone()
    {
        if (SnapshotRig.Install is not { } inst || Built() is null) return;
        var install = new GameInstall(inst);
        foreach (var (name, yaml) in RemixCatalogue.Builtin())
        {
            var spec = RemixSpec.Parse(yaml);
            var basePath = install.FindMission(spec.Base)!;
            var before = SHA256.HashData(File.ReadAllBytes(basePath));
            var a = RemixBuilder.Build(install, spec);
            var b = RemixBuilder.Build(install, spec);
            Assert.Equal(a.Mission, b.Mission);
            Assert.Equal(a.Text, b.Text);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(basePath)));
            Assert.StartsWith(Path.GetFullPath(GameInstall.UserMissionDir!), Path.GetFullPath(install.FindMission(spec.Id)!), StringComparison.OrdinalIgnoreCase);
            // the generated script decompiles and compiles back byte for byte, like a stock one
            var src = MissionScriptFile.Decompile(a.Mission, spec.Id).Script;
            Assert.Equal(a.Mission, MissionScriptFile.Compile(src, a.Mission));
            // its own threads plus (some of) the base's library ones, and nothing of the base's own threads
            var stock = MissionScriptFile.Decompile(File.ReadAllBytes(basePath), spec.Base).Script;
            var own = stock.Threads.Count(t => int.Parse(t.Script[6..]) >= RemixBuilder.LibraryRoutines);
            Assert.True(own > 0, name);
            int library = src.Threads.Count - RemixThreads(a.Source);
            Assert.InRange(library, 1, stock.Threads.Count - own);
            Assert.All(MissionScriptYaml.Read(a.Source).Threads.Where(t => !t.Name.StartsWith("rx_", StringComparison.Ordinal)),
                t => Assert.True(int.Parse(t.Script[6..].Split('+')[0]) < RemixBuilder.LibraryRoutines, t.Name));
            // the text the build wrote is the text the game reads
            var text = ObjectiveText.Parse(a.Text);
            Assert.Equal(spec.Text.Objectives.Count, text.Objectives.Count);
            Assert.Equal(spec.Text.Failures, text.Failures);
            // no briefing or outro movie, its own text, its listed name
            var w = WorldSettings.Read(new MissionFile(spec.Id + ".msn", a.Mission))!;
            Assert.Equal("", w.Briefing);
            Assert.Equal("", w.Outro);
            Assert.Equal(spec.Id + ".npt", w.Text);
            Assert.Equal(spec.Title, WorldSettings.ArenaName(new MissionFile(spec.Id + ".msn", a.Mission)));
        }
    }

    private static int RemixThreads(string source) =>
        MissionScriptYaml.Read(source).Threads.Count(t => t.Name.StartsWith("rx_", StringComparison.Ordinal));

    [Fact]
    public void ABuildIsStaleOnlyWhenItsDescriptionOrBaseChanges()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        var specs = RemixCatalogue.Builtin();
        var (built, errors) = RemixBuilder.BuildAll(install, specs, dir);
        Assert.Empty(errors);
        Assert.Empty(built);   // all current
        var one = specs.First(x => x.Yaml.Contains("skill: 2"));
        var edited = one.Yaml.Replace("skill: 2", "skill: 3");
        var spec = RemixSpec.Parse(edited);
        Assert.NotEqual(RemixBuilder.Stamp(RemixSpec.Parse(one.Yaml), File.ReadAllBytes(install.FindMission(spec.Base)!)),
            RemixBuilder.Stamp(spec, File.ReadAllBytes(install.FindMission(spec.Base)!)));
        Assert.Equal(specs.Count, RemixBuilder.Listed(dir).Count);
    }

    [Fact]
    public void FarmDefenceIsWonWhenTheRaidersAreDead()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x07farm");
        var h = s.Script!.Host;
        var zone = PathStart("x07farm", "rx_farms");
        Step(s, 20);
        MovePlayer(setup, zone.X + 20, zone.Z);
        Step(s, 40);
        Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);   // arrived
        var raiders = new[] { "vccameo1", "vfpalmn1", "vcgcour1", "vcmanta1", "vcvan1", "vfpalmn2" };
        // each starts in turn (two by time, then one per kill): kill whoever is awake until none is left
        for (int t = 0; t < 20 * 400 && s.Script.Result is null; t += 20)
        {
            foreach (var e in setup.Entities.Where(e => e.Vehicle is not null && !e.Dead && raiders.Contains(e.Name) && e.Order is { } o && o != I76.Sim.Fsm.AiOrder.Hide))
                h.Destroy(e);
            Step(s, 20);
        }
        Step(s, 200);
        Assert.True(s.Script.Result);
        Assert.All(new[] { 0, 1, 2 }, i => Assert.NotEqual(0, h.Objectives.Flags[i] & Objectives.Succeeded));
    }

    [Fact]
    public void FarmDefenceFailsWhenTheFarmsAreGoneOrThePlayerIsLate()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x07farm");
        var zone = PathStart("x07farm", "rx_farms");
        Step(s, 20);
        MovePlayer(setup, zone.X + 20, zone.Z);
        Step(s, 40);
        foreach (var name in new[] { "bhihous1", "bhbarn1", "ashack1", "awatert1", "bhhouse1" })
            foreach (var e in setup.Entities.Where(e => e.Static is { Destroyed: false } && e.Name == name).ToList())
                Wreck(setup, e.Static!);
        Step(s, 400);
        Assert.False(s.Script!.Result);
        Assert.Equal(3, FailLine(s));

        var (late, _) = Make("x07farm");
        Step(late, 20 * 170);
        Assert.False(late.Script!.Result);
        Assert.Equal(4, FailLine(late));
    }

    /// <summary>Every node and then the root hit by a round larger than its hit points (gunfire's way; destroy leaves a type 2 root standing).</summary>
    private static void Wreck(MissionSetup setup, I76.Sim.Collision.StaticObject o)
    {
        for (int n = -1; n < o.Nodes.Count; n++)
        {
            var p = new I76.Sim.Weapons.DamagePacket { Attacker = setup.World.Player, Mask = 2, Kind = I76.Sim.Damage.DamageKind.Round };
            p.Ch[1] = (n < 0 ? o.Hp : o.Nodes[n].Hp) + 1;
            setup.Damage.StructureSlot(o, n, p);
        }
    }

    [Fact]
    public void GrabAndGoWinsAtHomeOnlyWithNobodyNear()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x05grab");
        var h = s.Script!.Host;
        var pickup = PathStart("x05grab", "rx_wreck");
        var home = PathStart("x05grab", "rx_home");
        var chase = PathStart("x05grab", "rx_chase");
        Step(s, 20);
        Assert.Equal(Objectives.Hidden, h.Objectives.Flags[1] & Objectives.Hidden);
        MovePlayer(setup, pickup.X, pickup.Z);
        Step(s, 5);
        Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);
        Assert.Equal(0, h.Objectives.Flags[1] & Objectives.Hidden);   // the way home revealed
        Step(s, 20 * 5);
        // the pursuers were teleported in by the chase entry
        var pursuers = setup.Entities.Where(e => e.Name.StartsWith("t05cc0") || e.Name == "t05fp01").ToList();
        Assert.Equal(4, pursuers.Count);
        Assert.All(pursuers, p => Assert.True(Vector3.Distance(p.Position with { Y = 0 }, chase with { Y = 0 }) < 400, $"{p.Name} at {p.Position}"));
        // home with a pursuer beside the player: no win
        MovePlayer(setup, home.X, home.Z);
        var tail = pursuers[0].Vehicle!;
        tail.Pos = new Double3(home.X + 60, setup.Ground.BareHeight(home.X + 60, home.Z), home.Z);
        tail.Vel = default;
        Step(s, 20 * 3);
        Assert.Null(s.Script.Result);
        // nobody left awake: the win
        var me = h.Of(setup.World.Player!)!;
        foreach (var e in setup.Entities.Where(e => e.Vehicle is not null && !e.Dead && e != me && e.Name.StartsWith("t05")).ToList())
            if (Vector3.Distance(e.Position with { Y = 0 }, home with { Y = 0 }) < 400) h.Destroy(e);
        MovePlayer(setup, home.X, home.Z);
        Step(s, 20 * 10);
        Assert.True(s.Script.Result);
    }

    /// <summary>T05's clock on the way back only: no deadline before the pickup, 175 s after it.</summary>
    [Fact]
    public void GrabAndGosClockRunsOnTheReturnLeg()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x05grab");
        Step(s, 20 * 200);
        Assert.Null(s.Script!.Result);   // 200 s and no pickup: no deadline yet
        var pickup = PathStart("x05grab", "rx_wreck");
        MovePlayer(setup, pickup.X, pickup.Z);
        Step(s, 5);
        var h = s.Script.Host;
        Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);
        // keep the player alive and parked at the pickup; the clock runs out
        for (int i = 0; i < 20 * 200 && s.Script.Result is null; i += 20)
        {
            foreach (var e in setup.Entities.Where(e => e.Vehicle is not null && !e.Dead && e.Name.StartsWith("t05")).ToList()) h.Destroy(e);
            Step(s, 20);
        }
        Assert.False(s.Script.Result);
        Assert.Equal(3, FailLine(s));
    }

    /// <summary>The farm's trap: T07's east fence opens at the start and shuts behind the player at the farms.</summary>
    [Fact]
    public void TheFarmsTrapShutsTheEastFenceOnArrival()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x07farm");
        var gate = setup.Entities.Where(e => e.Name == "afence5" && e.Static is not null).OrderByDescending(e => e.Position.X).First().Static!;
        Step(s, 20 * 30);   // the fence slides open over some seconds; a trigger while it slides does nothing
        var g = gate.Nodes.First(n => n.Gate is not null).Gate!;
        Assert.True(g.X == 1 && !g.Moving, $"open: x {g.X} moving {g.Moving}");   // slid open (to its far end) at the start
        var zone = PathStart("x07farm", "rx_farms");
        MovePlayer(setup, zone.X, zone.Z);
        Step(s, 40);
        Assert.NotEqual(0, s.Script!.Host.Objectives.Flags[0] & Objectives.Succeeded);
        Assert.True(g.Moving && g.X < 1, $"shutting: x {g.X} moving {g.Moving}");   // sliding back shut behind the player
    }

    [Fact]
    public void BackToSpannerFailsOnDeathAndWinsAtTheTruckstop()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x14back");
        var h = s.Script!.Host;
        Step(s, 20);
        h.Destroy(h.Of(setup.World.Player!)!);
        Step(s, 20 * 10);
        Assert.False(s.Script.Result);
        Assert.Equal(1, FailLine(s));

        var (w, ws) = Make("x14back");
        var goal = PathStart("x14back", "rx_truckstop");
        Step(w, 20);
        MovePlayer(ws, goal.X, goal.Z);
        // the truckstop's police wake as the player comes: none of them left near
        Step(w, 20);
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && !e.Dead && e.Name.StartsWith("t14c")).ToList()) w.Script!.Host.Destroy(e);
        Step(w, 20 * 10);
        Assert.True(w.Script!.Result);
    }

    [Fact]
    public void ARemixRunIsDeterministicAndASnapshotRestoresIntoARebuiltMission()
    {
        if (Built() is null) return;
        foreach (var mission in new[] { "x07farm", "x05grab", "x10tail", "x02conv" })
        {
            var l = new SnapshotRig.Loaded(SnapshotRig.Install!);
            var b = SnapshotRig.BuildOf(l.Dir, mission, "vppirna1.vcf");
            var (a, _) = SnapshotRig.Make(l, b, SimRate.R20, 5);
            byte[]? snap = null;
            while (a.Ticks < 1400 && a.Script!.Result is null)
            {
                SnapshotRig.Tick(a);
                SnapshotRig.TakeEvents(a);
                if (a.Ticks == 200) snap = a.Snapshot();
            }
            var end = a.StateHash();
            int ticks = a.Ticks;

            var (again, _) = SnapshotRig.Make(new SnapshotRig.Loaded(SnapshotRig.Install!), b, SimRate.R20, 5);
            while (again.Ticks < ticks) { SnapshotRig.Tick(again); SnapshotRig.TakeEvents(again); }
            Assert.Equal(end, again.StateHash());

            var (fresh, _) = SnapshotRig.Make(new SnapshotRig.Loaded(SnapshotRig.Install!), b, SimRate.R20, 99);
            fresh.Restore(snap!);
            while (fresh.Ticks < ticks) { SnapshotRig.Tick(fresh); SnapshotRig.TakeEvents(fresh); }
            Assert.Equal(end, fresh.StateHash());
        }
    }

    private static I76.Sim.Collision.GateMotion FarmFence(MissionSetup setup) =>
        setup.Entities.Where(e => e.Name == "afence5" && e.Static is not null).OrderByDescending(e => e.Position.X).First().Static!
            .Nodes.First(n => n.Gate is not null).Gate!;

    /// <summary>
    /// Bug 6: a gate ignores a trigger while it slides (the original's GateMotion). Arriving at the farms while the east fence is
    /// still opening from the start used to leave it open; the template's gate queue sends the trap's trigger once the slide is over.
    /// </summary>
    [Fact]
    public void ATrapTriggeredWhileTheGateStillSlidesIsQueuedNotLost()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x07farm");
        var g = FarmFence(setup);
        var zone = PathStart("x07farm", "rx_farms");
        Step(s, 20 * 3);
        Assert.True(g.Moving, "the fence is still opening");
        MovePlayer(setup, zone.X, zone.Z);
        Step(s, 20);
        Assert.NotEqual(0, s.Script!.Host.Objectives.Flags[0] & Objectives.Succeeded);   // arrived while it slides
        Step(s, 20 * 60);
        Assert.True(g.X == 0 && !g.Moving, $"shut behind the player: x {g.X} moving {g.Moving}");
    }

    /// <summary>Bug 8: the stock cars nothing drives any more are taken out; the ones a template names stay.</summary>
    [Fact]
    public void IdleStockCarsAreRemovedAndTheNamedOnesKept()
    {
        if (Built() is null) return;
        var (_, farm) = Make("x07farm");
        Assert.DoesNotContain(farm.Entities, e => e.Vehicle is not null && e.Name is "vcpolic3" or "t07cp01");   // T07's parked police
        Assert.Equal(6, farm.Entities.Count(e => e.Vehicle is not null && e.Name is "vccameo1" or "vfpalmn1" or "vcgcour1" or "vcmanta1" or "vcvan1" or "vfpalmn2"));
        var (_, back) = Make("x14back");
        Assert.DoesNotContain(back.Entities, e => e.Vehicle is not null && e.Name is "vxbus1" or "vgoon1" or "vxktrac1");
        Assert.Equal(7, back.Entities.Count(e => e.Vehicle is not null && e.Name.StartsWith("t14cp")));   // the sleepers
    }

    /// <summary>
    /// Bug 11: a raider dead to friendly fire or its own mines is not the player's kill: it never starts a wave before the player
    /// has arrived; once he has, a wave whose earlier waves are all dead starts however they died.
    /// </summary>
    [Fact]
    public void ARaidersOwnDeathStartsNoWaveWithoutThePlayer()
    {
        if (Built() is null) return;
        var (s, setup) = Make("x07farm");
        var h = s.Script!.Host;
        Step(s, 20 * 26);   // the first wave is out (25 s)
        var first = setup.Entities.Where(e => e.Name is "vccameo1" or "vfpalmn1").ToList();
        var second = setup.Entities.Where(e => e.Name is "vcgcour1" or "vcmanta1").ToList();
        Assert.All(first, e => Assert.NotEqual(I76.Sim.Fsm.AiOrder.Hide, e.Order));
        foreach (var e in first) h.Destroy(e);   // nobody's kill
        Step(s, 20 * 10);
        Assert.All(second, e => Assert.Equal(I76.Sim.Fsm.AiOrder.Hide, e.Order));   // no wave without the player
        var zone = PathStart("x07farm", "rx_farms");
        MovePlayer(setup, zone.X, zone.Z);
        Step(s, 20 * 2);
        Assert.All(second, e => Assert.NotEqual(I76.Sim.Fsm.AiOrder.Hide, e.Order));   // he is there and nobody is left: the next wave

        // the player's kills: two of them start the second wave (at_kills 2)
        var (k, ks) = Make("x07farm");
        Step(k, 20 * 26);
        var a = ks.Entities.First(e => e.Name == "vccameo1");
        a.KilledByPlayer = true;
        k.Script!.Host.Destroy(a);
        Step(k, 20 * 5);
        Assert.All(ks.Entities.Where(e => e.Name is "vcgcour1" or "vcmanta1"), e => Assert.Equal(I76.Sim.Fsm.AiOrder.Hide, e.Order));
        var b = ks.Entities.First(e => e.Name == "vfpalmn1");
        b.KilledByPlayer = true;
        k.Script.Host.Destroy(b);
        Step(k, 20 * 2);
        Assert.All(ks.Entities.Where(e => e.Name is "vcgcour1" or "vcmanta1"), e => Assert.NotEqual(I76.Sim.Fsm.AiOrder.Hide, e.Order));
    }

    /// <summary>Bug 5: each remix carries the marks the port's map draws (its start and the template's places), and is known as a remix.</summary>
    [Fact]
    public void EveryRemixCarriesItsMapMarks()
    {
        if (Built() is null) return;
        foreach (var (stem, labels) in new[] { ("x07farm", "START|DEFEND"), ("x05grab", "START / HOME|PICKUP"), ("x14back", "START|HOME"),
            ("x10tail", "START|HIDEOUT"), ("x02conv", "START|CONVOY|EXIT") })
        {
            Assert.True(RemixMarks.IsRemix(stem), stem);
            var m = RemixMarks.For(stem)!;
            Assert.Equal(labels.Split('|'), m.Points.Select(p => p.Label));
            var round = RemixMarks.Parse(m.ToText());
            Assert.Equal(m.Points.Select(p => (p.Label, p.At)), round.Points.Select(p => (p.Label, p.At)));
        }
        Assert.Single(RemixMarks.For("x02conv")!.Lines);   // the convoy's route
        Assert.False(RemixMarks.IsRemix("t07"));
    }

    /// <summary>Lose the tail: the hideout counts only once the pursuers are lost; arriving with one near fails (led them home).</summary>
    [Fact]
    public void TatumTailIsWonOnlyOnceThePursuitIsLost()
    {
        if (Built() is null) return;
        var hide = PathStart("x10tail", "rx_tatum");
        var (s, setup) = Make("x10tail");
        Step(s, 20 * 6);   // the pursuers are in, behind the start
        var cops = setup.Entities.Where(e => e.Vehicle is not null && e.Name is "t10cp02" or "t10cp03" && e.Order != I76.Sim.Fsm.AiOrder.Hide).ToList();
        Assert.Equal(3, cops.Count);
        // at the hideout with one of them alongside: led them home (failure 4)
        MovePlayer(setup, hide.X, hide.Z);
        cops[0].Vehicle!.Pos = new Double3(hide.X + 80, setup.Ground.BareHeight(hide.X + 80, hide.Z), hide.Z);
        cops[0].Vehicle!.Vel = default;
        Step(s, 20 * 10);
        Assert.False(s.Script!.Result);
        Assert.Equal(4, FailLine(s));

        // the pursuers dead: lost (objective 1) after the quiet seconds, then the hideout wins
        var (w, ws) = Make("x10tail");
        var wh = w.Script!.Host;
        Step(w, 20 * 6);
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && e.Name.StartsWith("t10cp") && e.Order != I76.Sim.Fsm.AiOrder.Hide).ToList()) wh.Destroy(e);
        Step(w, 20 * 5);
        Assert.Equal(0, wh.Objectives.Flags[0] & Objectives.Succeeded);   // not yet: 12 quiet seconds
        Step(w, 20 * 12);
        Assert.NotEqual(0, wh.Objectives.Flags[0] & Objectives.Succeeded);
        MovePlayer(ws, hide.X, hide.Z);
        Step(w, 20 * 10);
        Assert.True(w.Script.Result);
    }

    /// <summary>Intercept: the convoy reaching its exit fails (line 3); the lead and its escorts dead win.</summary>
    [Fact]
    public void LevellandRunFailsWhenTheConvoyGetsThroughAndWinsWhenItDies()
    {
        if (Built() is null) return;
        var exit = PathStart("x02conv", "rx_levelland");
        var (s, setup) = Make("x02conv");
        Step(s, 20 * 8);
        var lead = setup.Entities.First(e => e.Name == "t02fr03");
        Assert.Equal(I76.Sim.Fsm.AiOrder.Goto, lead.Order);   // rolling after its delay
        lead.Vehicle!.Pos = new Double3(exit.X, setup.Ground.BareHeight(exit.X, exit.Z), exit.Z);
        lead.Vehicle!.Vel = default;
        Step(s, 20 * 10);
        Assert.False(s.Script!.Result);
        Assert.Equal(3, FailLine(s));

        var (w, ws) = Make("x02conv");
        var h = w.Script!.Host;
        Step(w, 20 * 8);
        h.Destroy(ws.Entities.First(e => e.Name == "t02fr03"));
        Step(w, 20);
        Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);
        Assert.Null(w.Script.Result);   // the wingmen still to go
        foreach (var e in ws.Entities.Where(e => e.Name is "t02fy01" or "t02fy03").ToList()) h.Destroy(e);
        Step(w, 20 * 10);
        Assert.True(w.Script.Result);
    }
}
