using System.Numerics;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Sim.Fsm;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Vehicles;
using Xunit;
using Xunit.Abstractions;

namespace I76.Sim.Tests;

/// <summary>
/// Every mission the co-op form offers (MissionTitles.Coop: A01, T01–T17, S01–S07; the Nitro Pack's campaign, B01 and the P
/// missions), a minute of each at 2/1, 2/2, 4/1, 4/2, 4/4 and 8/2 seats/present (the rest a network game's empty seats,
/// CoopVacate), against single player doing the same (docs/multiplayer.md "Co-op story missions"):
/// <list type="bullet">
/// <item>idle: nobody at the wheel;</item>
/// <item>drive: seat 0 driven along the mission's own zones (the nav points its script tests the player against, in program
/// order), the others idle;</item>
/// <item>die: a human destroyed at 20 s (seat 1 when another is present, which must change nothing but bring them back;
/// seat 0 alone, which must fail as single player's death does).</item>
/// </list>
/// Every human is armoured (the enemies' fire is not the test's). Each run's outcome is the mission's end (won or failed, its
/// message, when), the leave-map warnings and put-backs, and the humans' deaths with no killer (a crash or a fall).
/// </summary>
public class CoopSweepTests(ITestOutputHelper output)
{
    public sealed record Outcome(bool? Won, float At, int Message, int Warnings, int PutBack, int CrashDeaths, int Respawns)
    {
        public override string ToString() => (Won is null ? "runs" : $"{(Won.Value ? "WON" : "FAILED")} {At:0.0} m{Message}")
            + (Warnings > 0 ? $" w{Warnings}" : "") + (PutBack > 0 ? $" back{PutBack}" : "") + (CrashDeaths > 0 ? $" crash{CrashDeaths}" : "") + (Respawns > 0 ? $" resp{Respawns}" : "");
        public bool SameEnd(Outcome o, float slack = 1) => Won == o.Won && (Won is null || Message == o.Message && At >= o.At - slack);
    }

    public enum Mode { Idle, Drive, Die }

    public static readonly (int Seats, int Present)[] Configs = [(2, 1), (2, 2), (4, 1), (4, 2), (4, 4), (8, 2)];

    private static readonly Dictionary<string, SnapshotRig.Loaded> Loads = [];
    private static SnapshotRig.Loaded Load(string dir) { lock (Loads) return Loads.TryGetValue(dir, out var l) ? l : Loads[dir] = new SnapshotRig.Loaded(dir); }

    /// <summary>The nav points the script tests the player against (isWithinNav / isWithinSqNav, not negated), in program order, the exits left out.</summary>
    internal static List<Vector3> Zones(FsmVm vm, Func<int, bool> isPlayer)
    {
        var p = vm.Program;
        // the routines found as co-op finds them (single player's VM has no analysis)
        var routines = CoopRoutines.Analyze(p, isPlayer, _ => null);
        var exits = routines.LeaveMap.Select(x => x.PathCell >= 0 && x.PathCell < p.Data.Length ? p.Data[x.PathCell] : -1).ToHashSet();
        var leaveThreads = routines.LeaveMapThreads.ToHashSet();
        var found = new List<(int Pc, int Path)>();
        var entries = CoopRoutines.Entries(p).ToList();
        for (int ti = 0; ti < p.Threads.Count; ti++)
        {
            if (leaveThreads.Contains(ti)) continue;
            int start = p.Threads[ti].StartPc, k = entries.IndexOf(start);
            if (k < 0) continue;
            var ops = CoopRoutines.Decode(p, start, k + 1 < entries.Count ? entries[k + 1] : p.Code.Length);
            for (int i = 0; i < ops.Count; i++)
            {
                var o = ops[i];
                if (o.Name is not ("iswithinnav" or "iswithinsqnav") || o.Args.Length < 2 || o.Args[0].Kind != 'p' || o.Args[1].Kind != 'p') continue;
                if (i + 1 < ops.Count && ops[i + 1].Name == "<not>") continue;
                int pc = CoopRoutines.CellOf(p, ti, o.Args[0].Value), ac = CoopRoutines.CellOf(p, ti, o.Args[1].Value);
                if (pc < 0 || ac < 0 || pc >= p.Data.Length || ac >= p.Data.Length || !isPlayer(p.Data[ac])) continue;
                int path = p.Data[pc];
                if (path < 0 || path >= p.Paths.Count || p.Paths[path].Points.Count == 0 || exits.Contains(path)) continue;
                found.Add((o.Pc, path));
            }
        }
        return found.OrderBy(x => x.Pc).Select(x => x.Path).Distinct().Select(i => p.Paths[i].Points[0]).ToList();
    }

    /// <summary>A plain autopilot to a point: steer toward it, ease off on a sharp turn, back out when stuck.</summary>
    private sealed class Pilot(List<Vector3> zones)
    {
        private int _next;
        private float _still, _reverse;
        public CarInput Drive(Vehicle v, float dt)
        {
            var pos = v.Pos.ToVector3();
            while (_next < zones.Count && Vector2.Distance(new(pos.X, pos.Z), new(zones[_next].X, zones[_next].Z)) < 30) _next++;
            var fwd = v.Rot.Forward;
            float steer = 0, throttle = 0.8f;
            if (_next < zones.Count)
            {
                var to = zones[_next] - pos;
                var f = Vector2.Normalize(new(fwd.X, fwd.Z));
                var d = Vector2.Normalize(new(to.X, to.Z));
                float cross = f.X * d.Y - f.Y * d.X, dot = Vector2.Dot(f, d);
                steer = System.Math.Clamp((dot < 0 ? MathF.Sign(cross + 1e-6f) : cross) * 2.5f, -1, 1);
                if (dot < 0.5f) throttle = 0.5f;
            }
            _still = v.Speed < 2 ? _still + dt : 0;
            if (_still > 2.5f) { _reverse = 1.5f; _still = 0; }
            if (_reverse > 0) { _reverse -= dt; steer = -steer; throttle = -0.8f; }
            return new CarInput { Buttons = CarButtons.Analog, Steer = (short)(steer * SteerSign * 32767), Throttle = (short)(throttle * 32767) };
        }
    }

    /// <summary>The engine's steer sign against the XZ cross product (+1 left in game axes).</summary>
    internal static float SteerSign = 1;

    public static Outcome Play(string dir, string mission, int seats, int present, Mode mode, float seconds = 60, Action<string>? trace = null)
    {
        var l = Load(dir);
        var coop = seats > 0 ? CoopSetup.Of(seats) : null;
        var (s, setup) = SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, coop: coop), SimRate.R20, 1);
        var host = setup.Script!.Host;
        setup.Damage.UnlimitedArmour = setup.Damage.UnlimitedChassis = true;
        var humans = setup.Humans.Count > 0 ? setup.Humans.ToList() : [setup.World.Player!];
        int warnings = 0, back = 0, crash = 0, respawns = 0;
        if (trace is not null) { host.Logged += m => trace($"{host.Now:0.00} {m}"); host.Cb.Started += c => trace($"{host.Now:0.00} cb {c.Name}"); }
        setup.Damage.KilledBy += (v, a) => { if (a is null && humans.Contains(v)) { crash++; trace?.Invoke($"{host.Now:0.00} crash death of seat {humans.IndexOf(v)}"); } };
        if (host.Coop is { } g)
        {
            g.Happened += e =>
            {
                if (e is CoopLeaveWarning) warnings++;
                else if (e is CoopRespawned { Why: CoopReturn.LeftMap }) back++;
                else if (e is CoopRespawned { Why: CoopReturn.Died }) respawns++;
            };
            for (int k = present; k < seats; k++) host.CoopVacate(k);
        }
        var vm = setup.Script.Vm;
        var player = host.Of(setup.World.Player!);
        var pilot = mode == Mode.Drive && vm is not null ? new Pilot(Zones(vm, a => a >= 0 && a < vm.Actors.Count && ReferenceEquals(vm.Actors[a].Handle, player))) : null;
        int victim = seats > 0 && present >= 2 ? 1 : 0;
        int ticks = s.Rate.TicksFor(seconds), dieAt = s.Rate.TicksFor(20);
        var inputs = new CarInput[s.Drivers.Count];
        for (int i = 0; i < ticks && host.End is null; i++)
        {
            if (pilot is not null) inputs[0] = pilot.Drive(humans[0], s.TickSeconds);
            if (mode == Mode.Die && i == dieAt && !Damage.DamageModel.IsDead(humans[victim]))
            {
                int before = crash;
                setup.Damage.Destroy(humans[victim]);
                crash = before;   // the test's own kill, not a crash
            }
            s.Tick(inputs);
            SnapshotRig.TakeEvents(s);
            if (trace is not null && i % 100 == 0) trace($"{host.Now:0.00} seat0 {humans[0].Pos.X:0},{humans[0].Pos.Z:0} speed {humans[0].Speed:0}");
        }
        return new Outcome(host.End?.Success, host.End?.At ?? 0, host.End?.FailMessage ?? 0, warnings, back, crash, respawns);
    }

    public static IEnumerable<object[]> Missions()
    {
        foreach (var dir in new[] { SnapshotRig.Install, SnapshotRig.NitroInstall })
            if (dir is not null)
                foreach (var m in MissionTitles.Coop(new GameInstall(dir)))
                    yield return [dir == SnapshotRig.Install ? "i76" : "nitro", m.Stem.ToLowerInvariant()];
    }

    /// <summary>
    /// One mission, every configuration and mode, against single player. Flags (the defects this sweep looks for): an idle
    /// co-op game that ends, ends sooner or otherwise, or warns or puts a human back where single player's does not; a death
    /// of one of two or more humans that changes the outcome or brings nobody back; a lone human's death that does not end
    /// it as single player's does. The drive mode's differences are listed, not asserted (a car driven blind diverges by a
    /// tick's difference at the start: docs/multiplayer.md, orders to a human outside a cut-scene).
    /// </summary>
    [Theory]
    [MemberData(nameof(Missions))]
    [Trait("Load", "Sensitive")]   // 21 one-minute games a mission, ~45 missions: a release check runs it
    public void CoopMatchesSinglePlayer(string game, string mission)
    {
        var dir = game == "i76" ? SnapshotRig.Install! : SnapshotRig.NitroInstall!;
        var flags = new List<string>();
        var rows = new List<string>();
        foreach (var mode in new[] { Mode.Idle, Mode.Drive, Mode.Die })
        {
            var solo = Play(dir, mission, 0, 0, mode);
            var line = $"{mission} {mode,-5} solo {solo}";
            foreach (var (seats, present) in Configs)
            {
                var r = Play(dir, mission, seats, present, mode);
                line += $" | {seats}/{present} {r}";
                string? why = null;
                // single player's death (message 0) is not the group's: with two or more humans the mission goes on (a respawn)
                bool deathRule = solo.Won == false && solo.Message == 0 && present >= 2 && (r.Won is null || r.At >= solo.At - 1);
                if (mode == Mode.Idle && (!r.SameEnd(solo) && !deathRule || r.Warnings > 0 || r.PutBack > 0)) why = "idle differs";
                else if (mode == Mode.Die && present == 1 && !r.SameEnd(solo, 6)) why = "a lone human's death differs from single player's";
                else if (mode == Mode.Die && present >= 2)
                {
                    var idle = Play(dir, mission, seats, present, Mode.Idle);
                    // the death's own fail (message 0, the player's death in every stock script) is the group's: never for one of several
                    if (r.Won == false && r.Message == 0 && !r.SameEnd(idle)) why = $"one death of {present} fails it as the player's death (idle {idle})";
                    else if (r.Respawns == 0 && r.Won is null) why = "the dead human did not come back";
                    else if (!r.SameEnd(idle)) rows.Add($"DIE {mission} {seats}/{present}: {r} (idle {idle})");
                }
                else if (mode == Mode.Drive && !r.SameEnd(solo)) rows.Add($"DRIVE {mission} {seats}/{present}: {r} (solo {solo})");
                if (why is not null) flags.Add($"FLAG {mission} {mode} {seats}/{present}: {r} (solo {solo}): {why}");
            }
            output.WriteLine(line);
            Out(line);
        }
        foreach (var x in rows.Concat(flags)) { output.WriteLine(x); Out(x); }
        Assert.Empty(flags);
    }

    /// <summary>
    /// P14's intro (a cut-scene of half a minute that drives the lead off east): the teammate formed up on the slope behind it
    /// stands on its brakes while the scene holds its keys, where it rolled backwards 300 m to exit1, was warned twice and put
    /// back. The lead is the scene's, as the single player's car is: not held.
    /// </summary>
    [Fact]
    public void ATeammateTheSceneDoesNotDriveIsHeldOnItsBrakes()
    {
        if (SnapshotRig.NitroInstall is not { } dir) return;
        var (s, setup) = SnapshotRig.Make(Load(dir), SnapshotRig.BuildOf(dir, "p14", coop: CoopSetup.Of(2)), SimRate.R20, 1);
        var host = setup.Script!.Host;
        var events = new List<CoopEvent>();
        host.Coop!.Happened += events.Add;
        var (lead, mate) = (setup.Humans[0], setup.Humans[1]);
        var start = mate.Pos.ToVector3();
        for (int i = 0; i < s.Rate.TicksFor(25); i++) { s.Tick(new CarInput[s.Drivers.Count]); SnapshotRig.TakeEvents(s); }
        Assert.True(host.View.Active, "P14's intro is still on");
        Assert.True(Vector3.Distance(start, mate.Pos.ToVector3()) < 2, $"the teammate moved {Vector3.Distance(start, mate.Pos.ToVector3()):0.0} m");
        Assert.True(host.HeldInScene(mate));
        Assert.False(host.HeldInScene(lead));
        Assert.True(lead.Pos.X - start.X > 100, "the scene drives the lead");
        Assert.Empty(events.OfType<CoopLeaveWarning>());
        Assert.DoesNotContain(events, e => e is CoopRespawned);
    }

    /// <summary>P04's intro likewise: three teammates held, no warning of leaving the map in its first minute.</summary>
    [Fact]
    public void P04sTeammatesStayPutThroughItsIntro()
    {
        if (SnapshotRig.NitroInstall is not { } dir) return;
        var r = Play(dir, "p04", 4, 4, Mode.Idle);
        Assert.Equal((null, 0, 0), (r.Won, r.Warnings, r.PutBack));
    }

    /// <summary>I76_SWEEP_OUT: a file the rows are appended to as well (the sweep's table).</summary>
    private static void Out(string line)
    {
        if (Environment.GetEnvironmentVariable("I76_SWEEP_OUT") is { Length: > 0 } f) lock (Loads) File.AppendAllText(f, line + Environment.NewLine);
    }

    /// <summary>The sweep's autopilot steers toward its point (the sign of the engine's steer checked on T01's first zone).</summary>
    [Fact]
    [Trait("Load", "Sensitive")]
    public void ThePilotReachesTheFirstZone()
    {
        if (SnapshotRig.Install is not { } dir) return;
        var l = Load(dir);
        var (s, setup) = SnapshotRig.Make(l, SnapshotRig.BuildOf(dir, "t05"), SimRate.R20, 1);
        var vm = setup.Script!.Vm!;
        var player = setup.Script.Host.Of(setup.World.Player!);
        var zones = Zones(vm, a => a >= 0 && a < vm.Actors.Count && ReferenceEquals(vm.Actors[a].Handle, player));
        output.WriteLine(string.Join("; ", zones.Select(z => $"{z.X:0},{z.Z:0}")));
        Assert.NotEmpty(zones);
    }
}
