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
[Collection("remix")]   // GameInstall.UserMissionDir is one for the process: the remix test classes take turns
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

    /// <summary>The failure line's text the game shows for the mission's end.</summary>
    private static string FailText(SimSession s) => s.Script!.Host.End is { FailMessage: var m } && s.Script.Host.Objectives.Text.Failures is var f && m >= 0 && m < f.Count ? f[m] : "";

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
            Assert.Equal([.. spec.Text.Failures, .. RemixBuilder.Deaths(spec)], text.Failures);   // then a death line per objective
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
        Assert.Equal(specs.Count, RemixBuilder.Listed(dir).Count(l => specs.Any(sp => RemixSpec.Parse(sp.Yaml).Id == l.Stem)));   // (rolled jobs share the folder)
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
        Assert.Equal(4, FailLine(s));   // the death line of objective 1 (after the three failures): it names what was failed
        Assert.Contains("Spanner", FailText(s));

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
            ("x10tail", "START|HIDEOUT|SAFE HOUSE"), ("x02conv", "START / HOME|CONVOY|EXIT") })
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
        var hide = PathStart("x10tail", "rx_hideout");
        var safe = PathStart("x10tail", "rx_safe");
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
        Step(w, 20 * 10);   // caught up: the lost clock runs from here
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && e.Name.StartsWith("t10cp") && e.Order != I76.Sim.Fsm.AiOrder.Hide).ToList()) wh.Destroy(e);
        Step(w, 20 * 5);
        Assert.Equal(0, wh.Objectives.Flags[0] & Objectives.Succeeded);   // not yet: 12 quiet seconds
        Step(w, 20 * 12);
        Assert.NotEqual(0, wh.Objectives.Flags[0] & Objectives.Succeeded);
        MovePlayer(ws, hide.X, hide.Z);
        Step(w, 20 * 3);
        // the hideout is watched: the safe house's step opens (objective 3), and a death now names it
        Assert.NotEqual(0, wh.Objectives.Flags[1] & Objectives.Succeeded);
        Assert.Equal(0, wh.Objectives.Flags[2] & Objectives.Hidden);
        Assert.Null(w.Script.Result);
        MovePlayer(ws, safe.X, safe.Z);
        Step(w, 20 * 25);   // the stakeout comes in at 15 s: killed, then the safe house counts
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && !e.Dead && e.Vehicle != ws.World.Player).ToList()) wh.Destroy(e);
        Step(w, 20 * 20);
        Assert.True(w.Script.Result);

        // killed on the way to the safe house: the failure line says so
        var (d, ds) = Make("x10tail");
        var dh = d.Script!.Host;
        Step(d, 20 * 10);
        foreach (var e in ds.Entities.Where(e => e.Vehicle is not null && e.Name.StartsWith("t10cp") && e.Order != I76.Sim.Fsm.AiOrder.Hide).ToList()) dh.Destroy(e);
        Step(d, 20 * 18);
        MovePlayer(ds, hide.X, hide.Z);
        Step(d, 20 * 3);
        dh.Destroy(dh.Of(ds.World.Player!)!);
        Step(d, 20 * 10);
        Assert.False(d.Script.Result);
        Assert.Contains("safe house", FailText(d));
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
        foreach (var e in ws.Entities.Where(e => e.Name is "t02fy01" or "t02fy03" or "vcmanta1" or "vcgcour1" && e.Vehicle is not null).ToList()) h.Destroy(e);
        Step(w, 20 * 3);
        // the escorts down: the ride home opens (objective 3), with Gas Bandit's crew on its way; nobody near at home wins
        Assert.NotEqual(0, h.Objectives.Flags[1] & Objectives.Succeeded);
        Assert.Equal(0, h.Objectives.Flags[2] & Objectives.Hidden);
        Assert.Null(w.Script.Result);
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && !e.Dead && e.Vehicle != ws.World.Player).ToList()) h.Destroy(e);
        var home = PathStart("x02conv", "rx_home");
        MovePlayer(ws, home.X, home.Z);
        Step(w, 20 * 10);
        Assert.Null(w.Script.Result);   // not done by standing at the start: the finish counts 30 s after it opens
        Step(w, 20 * 15);
        foreach (var e in ws.Entities.Where(e => e.Vehicle is not null && !e.Dead && e.Vehicle != ws.World.Player).ToList()) h.Destroy(e);
        Step(w, 20 * 20);
        Assert.True(w.Script.Result);
    }

    /// <summary>
    /// The director: 200 seeds each roll a description the builder takes (its validation included) on the player's install, every
    /// car of it a car file of the install; the same seed gives the same file; the four templates and every node table are used.
    /// </summary>
    [Fact]
    public void TwoHundredRolledJobsAllBuild()
    {
        if (SnapshotRig.Install is not { } inst || Built() is null) return;
        var install = new GameInstall(inst);
        var vfs = new AssetVfs(install);
        var tables = RemixNodes.Builtin();
        Assert.True(tables.Count >= 5, $"{tables.Count} node tables");
        var templates = new HashSet<string>();
        var maps = new HashSet<string>();
        var errors = new List<string>();
        for (int seed = 1; seed <= 200; seed++)
        {
            var yaml = RemixDirector.ToYaml(RemixDirector.Roll(seed, tables));
            Assert.Equal(yaml, RemixDirector.ToYaml(RemixDirector.Roll(seed, tables)));   // the same seed, the same file
            var spec = RemixSpec.Parse(yaml);
            Assert.Equal(seed, spec.Seed);
            Assert.Contains($"Job {seed}", spec.Text.Objectives[0]);
            templates.Add(spec.Objective.Template);
            maps.Add(spec.Base);
            foreach (var car in spec.Cars.Values)
                if (!vfs.Exists(car.Vcf + ".vcf")) errors.Add($"seed {seed}: no car file {car.Vcf}");
            try { RemixBuilder.Build(install, spec); }
            catch (RemixError ex) { errors.Add($"seed {seed} ({spec.Base} {spec.Objective.Template}): {ex.Message}"); }
        }
        Assert.Empty(errors);
        Assert.Equal(RemixDirector.Templates.Order(), templates.Order());
        Assert.Equal(tables.Select(t => t.Base).Order(), maps.Order());
        Assert.NotEqual(RemixDirector.ToYaml(RemixDirector.Roll(1, tables)), RemixDirector.ToYaml(RemixDirector.Roll(2, tables)));
    }

    /// <summary>A rolled job of each template plays: built, loaded into the sim and run half a minute with its script going.</summary>
    [Fact]
    public void RolledJobsOfEveryTemplateRun()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 60 && seen.Count < RemixDirector.Templates.Length; seed++)
        {
            var spec = RemixDirector.Roll(seed, id: $"xj{seed}");
            if (!seen.Add(spec.Objective.Template)) continue;
            RemixBuilder.Build(install, spec).WriteTo(dir);
            var (s, setup) = Make($"xj{seed}");
            Assert.True(setup.Entities.Count(e => e.Vehicle is not null) > spec.Cars.Count, $"seed {seed}: its cars");
            Step(s, 20 * 30);
            Assert.True(s.Script!.Result is null or false, $"seed {seed}");
            Assert.True(s.Script.Host.Objectives.Flags.Length >= 2, $"seed {seed}");
        }
        Assert.Equal(RemixDirector.Templates.Length, seen.Count);
    }
    /// <summary>
    /// The owner's play-test (remix-3): hostile cars a target key never reached, a convoy not where the map marked it, a job that
    /// failed the moment the player got to a place by a map exit. RemixCheck holds every shipped remix and 500 rolled jobs to their
    /// description and map: every car turned on the player is the enemy's and a radar contact the next-target key reaches once
    /// it is out; each stands where it is parked, on the ground; the convoy's lead starts at its CONVOY mark; every mark is the
    /// place its script tests; every place the player must reach is clear of the leave-map exits; every structure set down is a
    /// collidable static where the description puts it.
    /// </summary>
    [Fact]
    public void EveryRemixAndFiveHundredJobsKeepTheirCarsWhereTheMapSaysAndTargetable()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        var problems = new List<string>();
        var jobs = RemixCatalogue.Builtin().Select(b => RemixSpec.Parse(b.Yaml)).ToList();
        for (int seed = 1; seed <= 500; seed++) jobs.Add(RemixDirector.Roll(seed, id: "xchk"));
        foreach (var spec in jobs)
        {
            var built = RemixBuilder.Build(install, spec);
            built.WriteTo(dir);
            var vfs = new AssetVfs(install);   // its own: the folder as it is now
            var asm = new I76.Formats.Assembly.ModelAssembler(vfs);
            foreach (var p in RemixCheck.Run(vfs, asm, spec.Id, spec, RemixMarks.Parse(built.Marks ?? "")))
                problems.Add($"{spec.Title} ({spec.Base} {spec.Objective.Template}): {p}");
            // round five: the pacing budget on the map's roads, and every place inside a structure driven out of
            foreach (var p in RemixPacing.Measure(install, spec).Problems.Concat(RemixDriveOut.Check(vfs, asm, spec)))
                problems.Add($"{spec.Title} ({spec.Base} {spec.Objective.Template}): {p}");
        }
        Assert.Empty(problems);
    }

    /// <summary>The builder puts every car the template turns on the player on the enemy's side (team 2), new cars and stock ones alike.</summary>
    [Fact]
    public void NewHostileCarsAreTheEnemysAndOthersNobodys()
    {
        if (SnapshotRig.Install is not { } inst || Built() is null) return;
        var install = new GameInstall(inst);
        var spec = RemixSpec.Parse(RemixCatalogue.Builtin().First(b => b.Name.Contains("x07farm")).Yaml);
        spec.Cars["idle"] = new CarSpec { Vcf = "vccameo1", At = [100, 49300] };
        var doc = I76.Formats.Mission.Edit.MapDocument.Parse(RemixBuilder.Build(install, spec).Mission);
        foreach (var o in doc.Objects.Where(o => o.ObjectType == 1 && o.Position.X == 100 && o.Position.Z == 49300))
            Assert.Equal(0u, (o.ObjectFlags >> 16) & 7);   // nobody's car: the description names it in no hostile part
        var raiders = spec.Objective.Waves.SelectMany(w => w.Cars).Select(c => spec.Cars[c].Vcf).ToHashSet();
        foreach (var o in doc.Objects.Where(o => o.ObjectType == 1 && raiders.Contains(o.Name) && o.Position.X != 100))
            Assert.Equal((uint)RemixRoles.EnemyTeam, (o.ObjectFlags >> 16) & 7);
    }

    /// <summary>The same job number builds the same files and plays the same: the same state hash after half a minute.</summary>
    [Fact]
    public void AJobNumberRebuildsTheSameJobExactly()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        foreach (int seed in new[] { 59933, 637742, 7 })
        {
            var a = RemixBuilder.Build(install, RemixDirector.Roll(seed, id: "xsame"));
            var b = RemixBuilder.Build(install, RemixSpec.Parse(RemixDirector.ToYaml(RemixDirector.Roll(seed, id: "xsame"))));
            Assert.Equal(a.Mission, b.Mission);
            Assert.Equal(a.Text, b.Text);
            Assert.Equal(a.Marks, b.Marks);
            Assert.Equal(a.Source, b.Source);
            Assert.Equal(a.Stamp, b.Stamp);
            ulong? hash = null;
            foreach (var built in new[] { a, b })
            {
                built.WriteTo(dir);
                var (s, _) = Make("xsame", 3);
                Step(s, 20 * 30);
                var h = s.StateHash();
                if (hash is null) hash = h; else Assert.Equal(hash, h);
            }
            // another job number builds another job
            Assert.NotEqual(a.Stamp, RemixBuilder.Build(install, RemixDirector.Roll(seed + 1, id: "xsame")).Stamp);
        }
    }

    /// <summary>
    /// A rolled job: three objective steps or more, a death line naming each, where its places are in words (distance and compass
    /// from the start), an instruction on the radio at the start, its job number in the first line; Levelland Run and Tatum Tail
    /// three steps too.
    /// </summary>
    [Fact]
    public void RolledJobsHaveThreeStepsAndSayWhereThePlacesAre()
    {
        string[] compass = ["north", "east", "south", "west"];
        for (int seed = 1; seed <= 300; seed++)
        {
            var spec = RemixDirector.Roll(seed);
            int steps = System.Math.Max(RemixTemplates.OwnObjectives(spec), RemixTemplates.FinishObjective(spec));
            Assert.True(steps >= 3, $"seed {seed} ({spec.Objective.Template}): {steps} steps");
            Assert.Equal(spec.Text.Objectives.Count, RemixBuilder.Deaths(spec).Count);
            Assert.All(spec.Text.Deaths, d => Assert.DoesNotContain("Still to do", d));   // the director writes each
            Assert.Contains(spec.Text.Notes, n => (n.Contains(" km ") || n.Contains(" m ")) && compass.Any(n.Contains));
            Assert.True(spec.Radio.Start.Count + (spec.Radio.Steps.Count > 0 ? spec.Radio.Steps[0].Count : 0) > 0, $"seed {seed}: no instruction at the start");
            Assert.StartsWith($"Job {seed}:", spec.Text.Objectives[0]);
            Assert.True(spec.Objective.Reinforce.Count > 0 || spec.Objective.Waves.Count >= 3, $"seed {seed}: no twist");
        }
        foreach (var name in new[] { "x02conv", "x10tail" })
        {
            var spec = RemixSpec.Parse(RemixCatalogue.Builtin().First(b => b.Name.Contains(name)).Yaml);
            Assert.True(RemixTemplates.FinishObjective(spec) >= 3, name);
            Assert.NotEmpty(spec.Objective.Reinforce);
        }
        Assert.Equal("3 km", RemixDirector.Distance([0, 0], [0, 3100]));
        Assert.Equal("800 m", RemixDirector.Distance([0, 0], [800, 0]));
        Assert.Equal("north", RemixDirector.Compass([0, 0], [0, 100]));   // +Z north, +X east (docs: the gazetteer)
        Assert.Equal("west", RemixDirector.Compass([0, 0], [-100, 0]));
    }

    /// <summary>
    /// The challenge: Back to Spanner's measured score is the calibration (RemixChallenge.Spanner); rolled jobs at the default
    /// level come out near it (median within 15 %), the levels scale it, and Levelland Run and Tatum Tail now match it too.
    /// </summary>
    [Fact]
    public void JobsAreBuiltToBackToSpannersChallenge()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        double Measured(RemixSpec spec)
        {
            RemixBuilder.Build(install, spec).WriteTo(dir);
            var vfs = new AssetVfs(install);
            return RemixCheck.Score(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), spec.Id, spec).Parts.Total;
        }
        RemixSpec Builtin(string name) => RemixSpec.Parse(RemixCatalogue.Builtin().First(b => b.Name.Contains(name)).Yaml);
        Assert.InRange(Measured(Builtin("x14back")), RemixChallenge.Spanner - 0.3, RemixChallenge.Spanner + 0.3);
        Assert.InRange(Measured(Builtin("x02conv")) / RemixChallenge.Spanner, 0.85, 1.2);
        Assert.InRange(Measured(Builtin("x10tail")) / RemixChallenge.Spanner, 0.85, 1.2);
        var scores = Enumerable.Range(1, 40).Select(seed => Measured(RemixDirector.Roll(seed, id: "xsco")) / RemixChallenge.Spanner).Order().ToList();
        Assert.InRange(scores[scores.Count / 2], 0.85, 1.15);
        double Mean(int level) => Enumerable.Range(1, 60).Average(seed => RemixDirector.Score(RemixDirector.Roll(seed, level: level)));
        Assert.True(Mean(0) < Mean(2) && Mean(2) < Mean(4), "the levels scale the challenge");
    }

    /// <summary>The director prefers the corners the stock mission never visits: its starts sit further from the stock action than the tables' starts do.</summary>
    [Fact]
    public void TheDirectorPrefersTheQuietCorners()
    {
        var tables = RemixNodes.Builtin();
        double picked = Enumerable.Range(1, 400).Select(seed => RemixDirector.Roll(seed, tables)).Average(spec => tables.First(t => t.Base == spec.Base).Quiet(spec.Start!.At));
        double all = tables.SelectMany(t => t.Starts.Select(st => t.Quiet(st.At))).Average();
        Assert.True(picked > all * 1.1, $"picked starts {picked:0} m from the stock action, the tables' {all:0} m");
    }

    /// <summary>A rolled job's map follows the job: the build's stamp (the map picture's key with the stem) differs between two rolls built as xjob.</summary>
    [Fact]
    public void EachRollOfXjobHasItsOwnStampAndMarks()
    {
        if (SnapshotRig.Install is not { } inst || Built() is not { } dir) return;
        var install = new GameInstall(inst);
        RemixBuilder.Build(install, RemixDirector.Roll(637742)).WriteTo(dir);
        string s1 = RemixMarks.StampOf("xjob"), m1 = RemixMarks.For("xjob")!.ToText();
        RemixBuilder.Build(install, RemixDirector.Roll(59933)).WriteTo(dir);
        Assert.NotEqual(s1, RemixMarks.StampOf("xjob"));
        Assert.NotEqual(m1, RemixMarks.For("xjob")!.ToText());
        Assert.Contains(RemixMarks.For("xjob")!.Notes, n => n.Contains("Job 59933"));
    }
}
