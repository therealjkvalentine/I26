using System.Globalization;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix traffic [--table] [--check]: each node table's routes with their length and share of paved road (RemixTraffic.PavedShare),
/// and whether civilian traffic may use them (1200 m or longer, 85 % on paved road: the convoy routes are written as waypoints, and a highway's bends take its straight legs off the centre line); --table prints the C# rows of
/// RemixTraffic.Paved (route names only); --check compares the built-in table with the install and exits 1 on a difference.
/// </summary>
public static class RemixTrafficCommand
{
    public const double MinShare = 0.85, MinLength = 1200;

    public static Dictionary<string, List<(string Route, double Length, double Share)>> Measure(GameInstall install)
    {
        var o = new Dictionary<string, List<(string, double, double)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in RemixNodes.Builtin().Where(t => install.FindMission(t.Base) is not null))
        {
            var roads = RemixTraffic.Roads(new MissionFile(install.FindMission(t.Base)!));
            o[t.Base] = [.. t.Routes.Where(r => r.Points.Count >= 2).Select(r => (r.Name, RemixTraffic.Length(r.Points), RemixTraffic.PavedShare(roads, r.Points)))];
        }
        return o;
    }

    public static int Run(GameInstall install, List<string> argv)
    {
        if (argv.Contains("--seeds")) return RemixTrafficRun.Run(install, argv);
        if (argv.Contains("--retrace")) return RemixTrafficRetrace.Run(install, argv);
        var inv = CultureInfo.InvariantCulture;
        var m = Measure(install);
        bool Ok((string Route, double Length, double Share) r) => r.Share >= MinShare && r.Length >= MinLength;
        if (argv.Contains("--table"))
        {
            foreach (var (b, rs) in m.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (rs.Any(Ok)) Console.WriteLine($"        [\"{b}\"] = [{string.Join(", ", rs.Where(Ok).Select(r => $"\"{r.Route}\""))}],");
            return 0;
        }
        if (argv.Contains("--check"))
        {
            int bad = 0;
            foreach (var (b, rs) in m)
            {
                var want = rs.Where(Ok).Select(r => r.Route).Order(StringComparer.Ordinal).ToList();
                var have = (RemixTraffic.Paved.GetValueOrDefault(b) ?? []).Order(StringComparer.Ordinal).ToList();
                if (!want.SequenceEqual(have)) { Console.WriteLine($"{b}: table [{string.Join(", ", have)}], install [{string.Join(", ", want)}]"); bad++; }
            }
            Console.WriteLine(bad == 0 ? "the paved routes match the install" : $"{bad} maps differ");
            return bad == 0 ? 0 : 1;
        }
        foreach (var (b, rs) in m.OrderBy(k => k.Key, StringComparer.Ordinal))
            foreach (var r in rs) Console.WriteLine(string.Create(inv, $"{b} {r.Route,-22} {r.Length,6:0} m  paved {100 * r.Share,3:0} %  {(Ok(r) ? "traffic" : "-")}"));
        return 0;
    }
}

/// <summary>
/// i76 remix traffic --seeds A-B [--seconds S] [--every S]: each rolled job with traffic built and run headless (the player parked at
/// the start), each civilian's place, speed and distance to its route's start and end printed every few seconds, its laps counted
/// (a lap: it came within 60 m of the route's end, then was put back at its start), and the sim's milliseconds a tick with and
/// without the civilians (the same job built with its traffic taken out).
/// </summary>
public static class RemixTrafficRun
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string n) => argv.IndexOf(n) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        var ab = (Opt("--seeds") ?? "1-20").Split('-');
        int a = int.Parse(ab[0], inv), b = ab.Length > 1 ? int.Parse(ab[1], inv) : a;
        int seconds = int.Parse(Opt("--seconds") ?? "420", inv), every = int.Parse(Opt("--every") ?? "30", inv);
        _near = Opt("--near") is { } nr ? nr.Split(',').Select(x => double.Parse(x, inv)).ToArray() : null;
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-traffic-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        int jobs = 0, cars = 0, lapped = 0;
        double withMs = 0, withoutMs = 0;
        for (int seed = a; seed <= b; seed++)
        {
            var spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xtraf", Challenge = 1.0 });
            if (spec.Traffic is not { Count: > 0 } traffic) continue;
            jobs++;
            Console.WriteLine($"seed {seed} ({spec.Base} {spec.Objective.Template}): {traffic.Count} civilians, {I76.Formats.Remix.RemixRoles.Hostiles(spec).Count} hostile cars");
            var (laps, ms) = Drive(install, spec, seconds, every, true);
            cars += traffic.Count; lapped += laps.Count(l => l > 0);
            var bare = I76.Formats.Remix.RemixSpec.Parse(RemixDirector.ToYaml(spec));
            foreach (var t in bare.Traffic!) bare.Cars.Remove(t.Car);
            bare.Traffic = null;
            var (_, ms0) = Drive(install, bare, seconds, every, false);
            withMs += ms; withoutMs += ms0;
            Console.WriteLine(string.Create(inv, $"  laps {string.Join(" ", laps)}; sim {ms:0.000} ms a tick with traffic, {ms0:0.000} without"));
        }
        Console.WriteLine(string.Create(inv, $"{jobs} jobs with traffic, {cars} civilians, {lapped} completed a lap in {seconds} s; sim {withMs / Math.Max(1, jobs):0.000} ms a tick with, {withoutMs / Math.Max(1, jobs):0.000} without"));
        return 0;
    }

    /// <summary>--near X,Z: print the tick each civilian first comes within 30 m of that point (to time a capture of it passing).</summary>
    private static double[]? _near;
    private static readonly HashSet<(string, int)> _passed = [];

    private static (List<int> Laps, double Ms) Drive(GameInstall install, I76.Formats.Remix.RemixSpec spec, int seconds, int every, bool print)
    {
        var inv = CultureInfo.InvariantCulture;
        RemixBuilder.Build(install, spec).WriteTo(GameInstall.UserMissionDir!);
        var vfs = new AssetVfs(install);
        var asm = new I76.Formats.Assembly.ModelAssembler(vfs);
        var path = install.FindMission(spec.Id)!;
        var setup = I76.Sim.Missions.MissionSetup.Build(vfs, new I76.Formats.Assembly.MissionWorld(vfs, asm, path), asm, "vppirna1.vcf", rand: new I76.Sim.Rng.CrtRand(1), seed: 1);
        var s = new I76.Sim.Session.SimSession(setup, I76.Sim.Session.SessionOptions.SinglePlayer, I76.Sim.Session.SimRate.Default);
        foreach (var h in setup.Humans) s.AddDriver(h);
        var vm = setup.Script!.Vm!;
        var keys = I76.Formats.Fsm.MissionScriptDecompiler.Keys(vm.Actors.Select(x => x.Label));
        var traffic = spec.Traffic ?? [];
        I76.Sim.Vehicles.Vehicle? CarOf(string name)
        {
            int i = Array.IndexOf(keys, RemixBuilder.PathPrefix + name);
            return i >= 0 && vm.Actors[i].Handle is I76.Sim.Missions.Entity { Vehicle: { } v } ? v : null;
        }
        var laps = traffic.Select(_ => 0).ToList();
        var nearEnd = traffic.Select(_ => false).ToList();
        int hz = I76.Sim.Session.SimRate.Default.Label;
        var sw = new System.Diagnostics.Stopwatch();
        for (int t = 0; t < seconds * hz; t++)
        {
            sw.Start();
            s.Tick([default]);
            sw.Stop();
            for (int k = 0; k < traffic.Count; k++)
            {
                if (CarOf(traffic[k].Car) is not { } v || !spec.Places.TryGetValue(traffic[k].Route, out var pts)) continue;
                double dEnd = Dist(v, pts[^1]), dStart = Dist(v, pts[0]);
                if (print && _near is [var nx, var nz] && Math.Sqrt((v.Pos.X - nx) * (v.Pos.X - nx) + (v.Pos.Z - nz) * (v.Pos.Z - nz)) < 30 && !_passed.Contains((traffic[k].Car, laps[k])))
                {
                    _passed.Add((traffic[k].Car, laps[k]));
                    Console.WriteLine(string.Create(inv, $"  {traffic[k].Car} {spec.Cars[traffic[k].Car].Vcf} within 30 m of ({nx:0},{nz:0}) at tick {t + 1} ({(t + 1) / (double)hz:0.0} s), lap {laps[k]}"));
                }
                if (dEnd < 60) nearEnd[k] = true;
                else if (nearEnd[k] && dStart < 40) { laps[k]++; nearEnd[k] = false; }
                if (print && t % (every * hz) == 0 && t > 0)
                    Console.WriteLine(string.Create(inv, $"  [{t / hz,4} s] {traffic[k].Car} {spec.Cars[traffic[k].Car].Vcf}: at ({v.Pos.X:0},{v.Pos.Z:0}) {v.Speed:0.0} m/s, {dStart:0} m from its start, {dEnd:0} m from its end of {RemixTraffic.Length(pts):0}, laps {laps[k]}{(I76.Sim.Damage.DamageModel.IsDead(v) ? " DEAD" : "")}"));
            }
        }
        return (laps, sw.Elapsed.TotalMilliseconds / (seconds * hz));
    }

    private static double Dist(I76.Sim.Vehicles.Vehicle v, double[] p) => Math.Sqrt((v.Pos.X - p[0]) * (v.Pos.X - p[0]) + (v.Pos.Z - p[1]) * (v.Pos.Z - p[1]));
}

/// <summary>
/// i76 remix traffic --retrace BASE ROUTE [--step M]: the node table's route laid onto the paved road's centre line: each leg between two
/// of its points whose ends lie on the same paved road segment follows that segment's centre line (a point every M metres, default
/// 150), other legs stay straight; the first and last points are snapped onto the road. Prints the new points (the table's YAML form)
/// and the route's paved share before and after. For hand-editing the node tables (docs/remix-missions.md "Civilian traffic").
/// </summary>
public static class RemixTrafficRetrace
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        int at = argv.IndexOf("--retrace");
        string b = argv[at + 1], name = argv[at + 2];
        double step = argv.IndexOf("--step") is var si and >= 0 ? double.Parse(argv[si + 1], inv) : 150;
        var table = RemixNodes.Builtin().First(t => t.Base.Equals(b, StringComparison.OrdinalIgnoreCase));
        var route = table.Routes.First(r => r.Name == name);
        var msn = new MissionFile(install.FindMission(b)!);
        // each segment's centre line at its own points, densified every 5 m
        var segs = new List<(int Kind, List<(double X, double Z)> P)>();
        foreach (var r in msn.RoadSegments())
        {
            var c = r.Points.Select(q => q.Raw.Length >= 24 ? (X: (double)(I76.Formats.Bin.F32(q.Raw, 0) + I76.Formats.Bin.F32(q.Raw, 12)) / 2, Z: (double)(I76.Formats.Bin.F32(q.Raw, 8) + I76.Formats.Bin.F32(q.Raw, 20)) / 2) : (X: (double)q.X, Z: (double)q.Z)).ToList();
            var d = new List<(double, double)>();
            for (int i = 0; i + 1 < c.Count; i++)
            {
                double len = Math.Sqrt((c[i + 1].X - c[i].X) * (c[i + 1].X - c[i].X) + (c[i + 1].Z - c[i].Z) * (c[i + 1].Z - c[i].Z));
                int n = Math.Max(1, (int)(len / 5));
                for (int k = 0; k < n; k++) d.Add((c[i].X + (c[i + 1].X - c[i].X) * k / n, c[i].Z + (c[i + 1].Z - c[i].Z) * k / n));
            }
            if (c.Count > 0) d.Add(c[^1]);
            segs.Add(((int)(r.Kind % 3), d));
        }
        (int S, int I, double D) Nearest(double[] p)
        {
            (int, int, double) best = (-1, -1, double.MaxValue);
            for (int s = 0; s < segs.Count; s++)
            {
                if (segs[s].Kind != 0) continue;
                for (int i = 0; i < segs[s].P.Count; i++)
                {
                    double d = (segs[s].P[i].X - p[0]) * (segs[s].P[i].X - p[0]) + (segs[s].P[i].Z - p[1]) * (segs[s].P[i].Z - p[1]);
                    if (d < best.Item3) best = (s, i, d);
                }
            }
            return (best.Item1, best.Item2, Math.Sqrt(best.Item3));
        }
        var pts = route.Points;
        var o = new List<double[]>();
        void Add(double x, double z) { var q = new[] { Math.Round(x), Math.Round(z) }; if (o.Count == 0 || o[^1][0] != q[0] || o[^1][1] != q[1]) o.Add(q); }
        var first = Nearest(pts[0]);
        Add(segs[first.S].P[first.I].X, segs[first.S].P[first.I].Z);
        for (int k = 0; k + 1 < pts.Count; k++)
        {
            var a = Nearest(pts[k]);
            var e = Nearest(pts[k + 1]);
            if (a.S == e.S && a.D < 60 && e.D < 60)
            {
                int dir = e.I >= a.I ? 1 : -1;
                double walked = 0;
                for (int i = a.I + dir; i != e.I; i += dir)
                {
                    var p0 = segs[a.S].P[i - dir];
                    var p1 = segs[a.S].P[i];
                    walked += Math.Sqrt((p1.X - p0.X) * (p1.X - p0.X) + (p1.Z - p0.Z) * (p1.Z - p0.Z));
                    if (walked >= step) { Add(p1.X, p1.Z); walked = 0; }
                }
                Add(segs[e.S].P[e.I].X, segs[e.S].P[e.I].Z);
            }
            else Add(e.D < 60 ? segs[e.S].P[e.I].X : pts[k + 1][0], e.D < 60 ? segs[e.S].P[e.I].Z : pts[k + 1][1]);
        }
        // the legs that stayed straight (their ends on different segments): every 25 m snapped to the nearest centre line within 80 m
        var dense = new List<double[]>();
        for (int k = 0; k + 1 < o.Count; k++)
        {
            double len = Math.Sqrt((o[k + 1][0] - o[k][0]) * (o[k + 1][0] - o[k][0]) + (o[k + 1][1] - o[k][1]) * (o[k + 1][1] - o[k][1]));
            int n = Math.Max(1, (int)(len / 25));
            for (int j = 0; j < n; j++)
            {
                double[] q = [o[k][0] + (o[k + 1][0] - o[k][0]) * j / n, o[k][1] + (o[k + 1][1] - o[k][1]) * j / n];
                var near = Nearest(q);
                dense.Add(near.D < 80 ? [segs[near.S].P[near.I].X, segs[near.S].P[near.I].Z] : q);
            }
        }
        dense.Add(o[^1]);
        // kept a point every `step` metres along it and at every bend of more than 12 degrees
        var kept = new List<double[]> { dense[0] };
        double run = 0;
        for (int k = 1; k + 1 < dense.Count; k++)
        {
            run += Math.Sqrt((dense[k][0] - dense[k - 1][0]) * (dense[k][0] - dense[k - 1][0]) + (dense[k][1] - dense[k - 1][1]) * (dense[k][1] - dense[k - 1][1]));
            double h0 = Math.Atan2(dense[k][0] - kept[^1][0], dense[k][1] - kept[^1][1]), h1 = Math.Atan2(dense[k + 1][0] - dense[k][0], dense[k + 1][1] - dense[k][1]);
            double turn = Math.Abs(((h1 - h0) * 180 / Math.PI + 540) % 360 - 180);
            if (run >= step || (turn > 12 && run >= 40)) { kept.Add([Math.Round(dense[k][0]), Math.Round(dense[k][1])]); run = 0; }
        }
        kept.Add([Math.Round(dense[^1][0]), Math.Round(dense[^1][1])]);
        o = kept;
        var roads = RemixTraffic.Roads(msn);
        Console.WriteLine(string.Create(inv, $"# {b} {name}: {pts.Count} points, {RemixTraffic.Length(pts):0} m, paved {100 * RemixTraffic.PavedShare(roads, pts):0} % -> {o.Count} points, {RemixTraffic.Length(o):0} m, paved {100 * RemixTraffic.PavedShare(roads, o):0} %"));
        Console.WriteLine("points: [" + string.Join(", ", o.Select(p => string.Create(inv, $"[{p[0]:0}, {p[1]:0}]"))) + "]");
        return 0;
    }
}
