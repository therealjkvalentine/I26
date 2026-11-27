using System.Numerics;
using I76.Formats.Fsm;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Math;
using I76.Sim.Missions;
using I76.Sim.Session;
using I76.Sim.Trip;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// Round four of the remix missions (docs/remix-missions.md): the node tables held to their maps, the escort and raid templates
/// and defence on the new maps played headless on every win and fail path, and a roguetrip's offers reproduced by its number.
/// </summary>
[Collection("remix")]
public class RemixRound4Tests
{
    private static readonly object Gate = new();
    private static string? _dir;

    private static string? Dir()
    {
        if (SnapshotRig.Install is null) return null;
        lock (Gate)
        {
            _dir ??= Path.Combine(Path.GetTempPath(), "i26-remix4-tests-" + Environment.ProcessId);
            GameInstall.UserMissionDir = _dir;
            return _dir;
        }
    }

    /// <summary>A rolled job of a template on a map, built as <paramref name="stem"/>; <paramref name="pick"/> chooses among seeds.</summary>
    private static RemixSpec Job(string stem, string template, string? map, Func<RemixSpec, bool>? pick = null, double challenge = 1.0)
    {
        var dir = Dir()!;
        for (int seed = 1; seed < 400; seed++)
        {
            var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = stem, Template = template, Map = map, Challenge = challenge });
            if (pick is not null && !pick(spec)) continue;
            RemixBuilder.Build(new GameInstall(SnapshotRig.Install!), spec).WriteTo(dir);
            return spec;
        }
        throw new InvalidOperationException($"no {template} job on {map} to the test's liking");
    }

    private static (SimSession S, MissionSetup Setup) Make(string mission, uint seed = 1)
    {
        var l = new SnapshotRig.Loaded(SnapshotRig.Install!);
        return SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, mission, "vppirna1.vcf"), SimRate.R20, seed);
    }

    private static void Step(SimSession s, int ticks = 1, Action? before = null)
    {
        for (int i = 0; i < ticks && s.Script!.Result is null; i++) { before?.Invoke(); s.Tick([new CarInput()]); SnapshotRig.TakeEvents(s); }
    }

    /// <summary>The entity a description's name stands for (a cars or bind entry: rx_NAME; else a stock actor).</summary>
    private static Entity Of(SimSession s, RemixSpec spec, string name)
    {
        var vm = s.Script!.Vm!;
        var keys = MissionScriptDecompiler.Keys(vm.Actors.Select(a => a.Label));
        int i = Array.IndexOf(keys, spec.Cars.ContainsKey(name) || spec.Bind.ContainsKey(name) ? RemixBuilder.PathPrefix + name : name);
        Assert.True(i >= 0, $"no actor {name}");
        return (Entity)vm.Actors[i].Handle!;
    }

    private static double[] At(RemixSpec spec, ZoneSpec z) => spec.Places[z.Place][0];

    private static void Put(MissionSetup setup, Vehicles.Vehicle v, double x, double z)
    {
        v.Pos = new Double3(x, setup.Ground.BareHeight((float)x, (float)z) + 0.5, z);
        v.Vel = default;
    }

    /// <summary>Every car the job turned on the player that is out (started, not parked or hidden) is killed as the player's kill.</summary>
    private static void KillTheOut(SimSession s, MissionSetup setup, RemixSpec spec, bool clear = false, bool asPlayer = true)
    {
        foreach (var r in RemixRoles.Hostiles(spec))
        {
            var e = Of(s, spec, r.Car);
            if (!e.Dead && e.Vehicle is { } v && Weapons.Combat.IsLive(v) && e.Order is { } o && o != Fsm.AiOrder.Hide)
            {
                e.KilledByPlayer = asPlayer;
                s.Script!.Host.Destroy(e);
                // (an escort's test: the wreck taken off the road, out of the column, as a fight that ended off the road would leave it)
                if (clear && RemixNodes.Builtin().FirstOrDefault(t => t.Base == spec.Base)?.Lot is [var lx, var lz]) Put(setup, v, lx - 60, lz - 60);
            }
        }
    }

    private static int FailLine(SimSession s) => s.Script!.Host.End is { FailMessage: var m } ? m + 1 : 0;
    private static string FailText(SimSession s) => s.Script!.Host.End is { FailMessage: var m } && s.Script.Host.Objectives.Text.Failures is var f && m >= 0 && m < f.Count ? f[m] : "";

    /// <summary>Metres from a point to a route's polyline.</summary>
    private static double OffRoute(List<double[]> pts, double x, double z)
    {
        double best = double.MaxValue;
        for (int i = 1; i < pts.Count; i++)
        {
            double ax = pts[i - 1][0], az = pts[i - 1][1], bx = pts[i][0], bz = pts[i][1];
            double dx = bx - ax, dz = bz - az, len = dx * dx + dz * dz;
            double t = len <= 0 ? 0 : System.Math.Clamp(((x - ax) * dx + (z - az) * dz) / len, 0, 1);
            double px = ax + t * dx - x, pz = az + t * dz - z;
            best = System.Math.Min(best, System.Math.Sqrt(px * px + pz * pz));
        }
        return best;
    }

    /// <summary>Every node table: on the map's ground, on a road or drivable ground, clear of the exits, reachable (RemixNodeCheck).</summary>
    [Fact]
    public void EveryNodeTableHoldsToItsMap()
    {
        if (SnapshotRig.Install is not { } inst) return;
        var install = new GameInstall(inst);
        var tables = RemixNodes.Builtin();
        Assert.True(tables.Count >= 15, $"{tables.Count} node tables");
        var problems = tables.SelectMany(t => RemixNodeCheck.Run(install, t).Problems.Select(p => $"{t.Base}: {p}")).ToList();
        Assert.Empty(problems);
        // and the check finds a bad one: a node off the map, one in an exit's stub, an entry with its points 2 km apart
        var bad = RemixNodes.Parse("""
            base: t01
            name: Seagraves
            lot: [2290, 34685]
            starts:
              - {name: highway_south, at: [3488, 36116], heading: 30, called: on the highway south of town}
            nodes:
              - {name: nowhere, kinds: [pickup], at: [6500, 37000], called: off the map}
              - {name: stub, kinds: [home], at: [2948, 35278], called: the road's end by the south exit}
            entries:
              - {name: wide, at: [[3488, 36116], [3488, 38116]]}
            """);
        var found = RemixNodeCheck.Run(install, bad).Problems;
        Assert.Contains(found, p => p.Contains("not on the map's ground"));
        Assert.Contains(found, p => p.Contains("from the exit"));
        Assert.Contains(found, p => p.Contains("apart"));
        // every neighbour named is a table, and the region joins up: from T01 a run can reach T17
        var bases = tables.Select(t => t.Base).ToHashSet();
        Assert.All(tables, t => Assert.All(t.Neighbours, n => Assert.Contains(n, bases)));
        var seen = new HashSet<string> { "t01" };
        var q = new Queue<string>(seen);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var n in tables.First(t => t.Base == cur).Neighbours) if (seen.Add(n)) q.Enqueue(n);
        }
        Assert.Equal(bases.Order(), seen.Order());
    }

    /// <summary>
    /// Escort (the owner's complaint about the stock escorts: helpers that do not keep up): the escorted car drives its route to the goal
    /// while the player keeps up (here put 50 m behind it), never far from the route; the wingman rides with it;
    /// it waits when he falls behind; the job is won at the goal with nobody near. The van wrecked fails (line 3); a death names the step.
    /// </summary>
    [Fact]
    public void EscortsFollowTheirRouteWithTheWingmanAlongsideAndWinAtTheGoal()
    {
        if (Dir() is null) return;
        var spec = Job("xesc", "escort", "t08", s => s.Objective.Escort!.Wingmen.Count > 0);
        var e = spec.Objective.Escort!;
        var route = spec.Places[e.Route];
        var (s, setup) = Make("xesc");
        var h = s.Script!.Host;
        var van = Of(s, spec, e.Car);
        var wing = Of(s, spec, e.Wingmen[0]);
        var me = h.Of(setup.World.Player!)!;
        Assert.Equal(1, s.Combat!.Team(van.Vehicle!));     // the player's side
        Assert.Equal(1, s.Combat.Team(wing.Vehicle!));
        // the player held back: the van waits for him (it never runs ahead alone)
        Step(s, 20 * 3);
        Put(setup, setup.World.Player!, route[0][0] - 400, route[0][1]);
        Step(s, 20 * 20, () => h.Order(me, null, Fsm.AiOrder.Sit, null, 0));
        var waited = van.Vehicle!.Pos;
        Step(s, 20 * 10, () => h.Order(me, null, Fsm.AiOrder.Sit, null, 0));
        Assert.True(Vector2.Distance(new((float)waited.X, (float)waited.Z), new((float)van.Vehicle.Pos.X, (float)van.Vehicle.Pos.Z)) < 15, "the van drove on without the player");
        // the player keeps up (a fresh start; put 50 m behind the van and 30 m off its right every two seconds, near enough for it to drive on and out of
        // the column the wingman drives): the van drives the route, the wingman with it; every car that comes out is killed
        (s, setup) = Make("xesc");
        h = s.Script!.Host;
        van = Of(s, spec, e.Car);
        wing = Of(s, spec, e.Wingmen[0]);
        // (the player's car kept whole, the cheat's way: he is put beside a fight every two seconds and fires no shot)
        if (setup.Damage is Damage.DamageModel dm) { dm.UnlimitedArmour = true; dm.UnlimitedChassis = true; }
        double worstOff = 0; int samples = 0, wingNear = 0;
        var trail = new System.Text.StringBuilder();
        for (int t = 0; t < 20 * 700 && s.Script.Result is null; t += 40)
        {
            if (!van.Dead && t > 20 * 4)
            {
                var fw = van.Vehicle!.Rot.Forward;
                double fl = System.Math.Max(1e-3, System.Math.Sqrt(fw.X * fw.X + fw.Z * fw.Z)), ux = fw.X / fl, uz = fw.Z / fl;
                Put(setup, setup.World.Player!, van.Vehicle.Pos.X - 50 * ux + 30 * uz, van.Vehicle.Pos.Z - 50 * uz - 30 * ux);
            }
            Step(s, 40);
            if (!van.Dead) worstOff = System.Math.Max(worstOff, OffRoute(route, van.Vehicle.Pos.X, van.Vehicle.Pos.Z));
            if (!wing.Dead)
            {
                samples++;
                if (!van.Dead && Vector3.Distance(wing.Vehicle!.Pos.ToVector3(), van.Vehicle.Pos.ToVector3()) < 150) wingNear++;
                if (samples % 6 == 0) trail.Append($" {h.Now:0}s d{Vector3.Distance(wing.Vehicle!.Pos.ToVector3(), van.Vehicle.Pos.ToVector3()):0} v{wing.Vehicle.Speed:0} {wing.Order} {string.Join(">", wing.Stack.Select(h.Ptable.NameOf))} ({wing.Vehicle.Pos.X:0},{wing.Vehicle.Pos.Z:0});");
            }
            KillTheOut(s, setup, spec, clear: true);
        }
        Assert.True(s.Script.Result, $"not won ({s.Script.Result}, {FailText(s)}, {h.Now:0} s): {string.Join(", ", h.Objectives.Flags.Select(Objectives.Describe))}; van {OffRoute(route, van.Vehicle.Pos.X, van.Vehicle.Pos.Z):0} m off its route");
        Assert.True(worstOff < 100, $"the van strayed {worstOff:0} m from its route");   // (a dodge round a wreck or a junction cut: 85 m seen)
        Assert.True(samples > 0 && wingNear >= samples * 0.75, $"the wingman was with the van {wingNear} of {samples} samples:{trail}");

        // the van wrecked: failure 3
        var (f, fs) = Make("xesc");
        Step(f, 20 * 5);
        f.Script!.Host.Destroy(Of(f, spec, e.Car));
        Step(f, 20 * 12);   // a failure ends the mission a few seconds on
        Assert.False(f.Script.Result);
        Assert.Equal(3, FailLine(f));

        // the player killed on the second leg: the death line names it
        var (d, ds) = Make("xesc");
        var mid = At(spec, e.Mid!);
        var dvan = Of(d, spec, e.Car);
        Step(d, 20 * 2);
        Put(ds, dvan.Vehicle!, mid[0], mid[1]);
        Step(d, 20 * 3);
        Assert.NotEqual(0, d.Script!.Host.Objectives.Flags[0] & Objectives.Succeeded);
        d.Script.Host.Destroy(d.Script.Host.Of(ds.World.Player!)!);
        Step(d, 20 * 10);
        Assert.False(d.Script.Result);
        Assert.Contains("still on the road", FailText(d));
    }

    /// <summary>
    /// Raid: the targets (structures of a cluster, or trucks parked at a yard) down opens the way out; out in time wins; too slow fails
    /// (line 3: the roads closed); a death names the step; driving off the map fails (line 2, the stock library's).
    /// </summary>
    [Fact]
    public void RaidsWinWhenTheTargetsFallAndTheWayOutIsMadeInTime()
    {
        if (Dir() is null) return;
        foreach (var (map, clusterJob) in new[] { ("t09", true), ("t01", false) })
        {
            var spec = Job("xraid", "raid", map, s => s.Bind.Count > 0 == clusterJob);
            var o = spec.Objective;
            var target = At(spec, o.Arrive!);
            var esc = At(spec, o.Escape!);
            void Fell(SimSession s, MissionSetup setup)
            {
                foreach (var t in o.Targets)
                {
                    var e = Of(s, spec, t);
                    if (e.Static is { } st) RemixCheck.Wreck(setup, st);
                    else if (!e.Dead) { e.KilledByPlayer = true; s.Script!.Host.Destroy(e); }
                }
            }
            // win: there, the targets down, out in time with nobody near
            var (s, setup) = Make("xraid");
            var h = s.Script!.Host;
            Step(s, 20 * 2);
            Put(setup, setup.World.Player!, target[0], target[1]);
            Step(s, 20 * 3);
            Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);
            Fell(s, setup);
            Step(s, 20 * 3);
            Assert.NotEqual(0, h.Objectives.Flags[1] & Objectives.Succeeded);
            Assert.Equal(0, h.Objectives.Flags[2] & Objectives.Hidden);
            Put(setup, setup.World.Player!, esc[0], esc[1]);
            for (int t = 0; t < 20 * 120 && s.Script.Result is null; t += 20) { Step(s, 20); KillTheOut(s, setup, spec); }
            Assert.True(s.Script.Result, $"{map}: {string.Join(", ", h.Objectives.Flags.Select(Objectives.Describe))}");

            // too slow: the roads closed
            var (w, ws) = Make("xraid");
            Step(w, 20 * 2);
            Put(ws, ws.World.Player!, target[0], target[1]);
            Step(w, 20 * 3);
            Fell(w, ws);
            Step(w, 20 * (o.Escape!.Seconds + 15), () => KillTheOut(w, ws, spec));
            Assert.False(w.Script!.Result);
            Assert.Equal(3, FailLine(w));
            Assert.Contains("closed the roads", FailText(w));

            // killed in the middle of the hit
            var (d, dsetup) = Make("xraid");
            Step(d, 20 * 2);
            Put(dsetup, dsetup.World.Player!, target[0], target[1]);
            Step(d, 20 * 3);
            d.Script!.Host.Destroy(d.Script.Host.Of(dsetup.World.Player!)!);
            Step(d, 20 * 10);
            Assert.False(d.Script.Result);
            Assert.Contains("middle of the hit", FailText(d));
        }
        // off the map by an exit: the library's leave-map failure (line 2)
        var spec2 = Job("xraid", "raid", "t09", null);
        var exitPath = new MissionFile(GameInstall.InUserMissionDir("xraid.msn")!).ReadFsmProgram()!.Paths.First(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase));
        var (x, xs) = Make("xraid");
        Step(x, 20 * 2);
        for (int t = 0; t < 20 * 60 && x.Script!.Result is null; t += 20)
        {
            Put(xs, xs.World.Player!, exitPath.Points[0].X, exitPath.Points[0].Z);
            Step(x, 20);
        }
        Assert.False(x.Script!.Result);
        Assert.Equal(2, FailLine(x));
    }

    /// <summary>Defence beyond T07: the oil field's pumps, Seagraves' main street, Plains' warehouses: won with the raiders dead, lost with the place gone or arriving too late.</summary>
    [Fact]
    public void DefenceOnTheNewMapsWinsAndFails()
    {
        if (Dir() is null) return;
        foreach (var map in new[] { "t09", "t01", "t08" })
        {
            var spec = Job("xdef", "defend", map);
            var o = spec.Objective;
            var zone = At(spec, o.Arrive!);
            var (s, setup) = Make("xdef");
            var h = s.Script!.Host;
            Step(s, 20 * 2);
            Put(setup, setup.World.Player!, zone[0], zone[1]);
            Step(s, 20 * 2);
            Assert.NotEqual(0, h.Objectives.Flags[0] & Objectives.Succeeded);
            for (int t = 0; t < 20 * 500 && s.Script.Result is null; t += 20) { Step(s, 20); KillTheOut(s, setup, spec); }
            Assert.True(s.Script.Result, $"{map}: {string.Join(", ", h.Objectives.Flags.Select(Objectives.Describe))}");

            // the place gone: failure 3
            var (l, ls) = Make("xdef");
            Step(l, 20 * 2);
            foreach (var b in o.Defend) if (Of(l, spec, b).Static is { } st) RemixCheck.Wreck(ls, st);
            Step(l, 20 * 15);
            Assert.False(l.Script!.Result);
            Assert.Equal(3, FailLine(l));

            // too late to arrive: failure 4
            var (late, lsetup) = Make("xdef");
            // the first wave crashed as it comes (nobody's kills: no later wave starts without the player), the place kept standing:
            // only the clock can fail it
            Step(late, 20 * (o.Arrive!.Seconds + 15), () => KillTheOut(late, lsetup, spec, asPlayer: false));
            Assert.True(late.Script!.Result == false, $"{map}: {late.Script.Host.Now:0} s of {o.Arrive.Seconds}: {string.Join(", ", late.Script.Host.Objectives.Flags.Select(Objectives.Describe))}; start {Vector2.Distance(new((float)spec.Start!.At[0], (float)spec.Start.At[1]), new((float)zone[0], (float)zone[1])):0} m from the zone (radius {o.Arrive.Radius})");
            Assert.Equal(4, FailLine(late));
        }
    }

    /// <summary>
    /// A roguetrip: the same number and the same picks give the same offers and the same jobs; offers are on the map's neighbours (the
    /// first on the home map or next to it), two or three, the challenge rising 0.8, 1.0, 1.2, 1.4; a win carries the car and asks for a
    /// pick; a loss ends the run; the run survives its file.
    /// </summary>
    [Fact]
    public void ARoguetripsNumberReproducesItsOffersAndItsJobs()
    {
        var tables = RemixNodes.Builtin();
        string Walk(int number)
        {
            var run = RemixRun.Start(number, "vppirna1.vcf", "Groove", "Piranha", 0);
            var log = new System.Text.StringBuilder();
            for (int job = 0; job < RemixRun.Jobs; job++)
            {
                var offers = run.Offers(tables);
                Assert.InRange(offers.Count, 2, 3);
                Assert.All(offers, of => Assert.Equal(RemixRun.Challenges[job], of.Challenge));
                if (run.Map.Length > 0)
                    Assert.All(offers, of => Assert.Contains(of.Map, tables.First(t => t.Base == run.Map).Neighbours));
                foreach (var of in offers) log.Append($"{of.Map} {of.Template} {of.Seed} {of.Flavour}|");
                var pick = offers[(number + job) % offers.Count];
                var spec = run.Spec(pick, tables);
                Assert.Equal(pick.Map, spec.Base);
                Assert.Equal(pick.Template, spec.Objective.Template);
                Assert.StartsWith($"Roguetrip {number}, job {job + 1}:", spec.Text.Objectives[0]);
                log.Append(RemixDirector.ToYaml(spec).GetHashCode(StringComparison.Ordinal)).Append('\n');
                run.Take(pick);
                // the run kept in its file and read back between jobs
                run = RemixRun.Parse(run.ToYaml());
                run.Finish(true, 2, new RunCar { Armour = [10, 20, 30, 40], ArmourLeft = 50 });
                if (run.Phase == "reward") run.Reward(job % 3 == 0 ? "repair" : job % 3 == 1 ? "rearm" : "bonus");
            }
            Assert.Equal("done", run.Phase);
            Assert.Equal(4, run.Won);
            Assert.Equal(8, run.Kills);
            return log.ToString();
        }
        Assert.Equal(Walk(1234), Walk(1234));
        Assert.NotEqual(Walk(1234), Walk(1235));
        // a loss ends the run; a job the game closed during comes back to the same choice
        var r = RemixRun.Start(77, "vppirna1.vcf", "Groove", "Piranha", 0);
        var first = r.Offers(tables);
        r.Take(first[0]);
        r.Resume();
        Assert.Equal("choose", r.Phase);
        Assert.Equal(first.Select(o => o.Seed), r.Offers(tables).Select(o => o.Seed));
        r.Take(first[1]);
        r.Finish(false, 1, null);
        Assert.True(r.Over);
        Assert.Equal("over", r.Phase);
        // the picks: a repair leaves the rounds, a rearm the damage
        var car = new RunCar { Armour = [1, 2, 3, 4], Engine = 5, Weapons = [[1, 50, 30]], ArmourLeft = 10, AmmoLeft = 20 };
        Assert.Equal([-1, -1, -1, -1], car.Repaired().Armour);
        Assert.Equal(30, car.Repaired().Weapons[0][2]);
        Assert.Equal([1, 2, 3, 4], car.Rearmed().Armour);
        Assert.Equal(-1, car.Rearmed().Weapons[0][2]);
    }

    /// <summary>The car's damage read off at a job's end goes back on at the next job's load, armour, parts and rounds alike.</summary>
    [Fact]
    public void ARoguetripCarriesTheCarsDamageFromJobToJob()
    {
        if (Dir() is null) return;
        Job("xcar", "deliver", "t01");
        var (s, setup) = Make("xcar");
        var p = setup.World.Player!;
        var d = p.Health!;
        d.Armour[0] = d.ArmourMax[0] / 3;
        d.Chassis[3] = d.ChassisMax[3] / 2;
        d.Engine!.Hp = d.Engine.Max / 4;
        var gun = p.Armory!.LoadOrder[0];
        gun.Ammo /= 2;
        var carried = RunCarry.Capture(p);
        Assert.True(carried.ArmourLeft < 100);
        var (n, nsetup) = Make("xcar");
        var q = nsetup.World.Player!;
        RunCarry.Apply(RemixRun.Parse(new RemixRun { State = carried }.ToYaml()).State!, q);
        Assert.Equal(d.Armour, q.Health!.Armour);
        Assert.Equal(d.Chassis, q.Health.Chassis);
        Assert.Equal(d.Engine.Hp, q.Health.Engine!.Hp);
        Assert.Equal(gun.Ammo, q.Armory!.LoadOrder[0].Ammo);
        // a repair puts the parts back as built and keeps the rounds
        var (r, rsetup) = Make("xcar");
        var rp = rsetup.World.Player!;
        RunCarry.Apply(carried.Repaired(), rp);
        Assert.Equal(rp.Health!.ArmourMax, rp.Health.Armour);
        Assert.Equal(gun.Ammo, rp.Armory!.LoadOrder[0].Ammo);
    }
}
