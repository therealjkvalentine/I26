using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix build SPEC.yaml...|--builtin [--out DIR]: each remix description (or the ones the port ships) built into a mission
/// file, its objective text and its script's source in DIR (default: --remix-dir, else ./remix), never the install
/// (docs/remix-missions.md). Run one with i76 --remix-dir DIR run STEM.
/// </summary>
public static class RemixCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        if (argv.Count >= 3 && argv[1] == "map") return RemixMapDump.Run(install, argv);
        if (argv.Count >= 2 && argv[1] == "roll") return Roll(install, argv);
        if (argv.Count >= 2 && argv[1] == "check") return Check(install, argv);
        if (argv.Count >= 2 && argv[1] == "score") return Score(install, argv);
        if (argv.Count >= 2 && argv[1] == "pace") return Pace(install, argv);
        if (argv.Count >= 2 && argv[1] == "trip") return Trip(install, argv);
        if (argv.Count >= 3 && argv[1] == "structures") return RemixStructures.Run(install, argv);
        if (argv.Count >= 4 && argv[1] == "driveout") return RemixDriveOutCommand.Run(install, argv);
        if (argv.Count >= 3 && argv[1] == "roadsurvey") return RemixRoadSurvey.Run(install, argv);
        if (argv.Count < 3 || argv[1] != "build") { Console.Error.WriteLine("usage: i76 remix build SPEC.yaml...|--builtin [--out DIR] | map BASE [--json FILE]"); return 2; }
        int oi = argv.IndexOf("--out");
        string outDir = oi >= 0 ? argv[oi + 1] : GameInstall.UserMissionDir ?? "remix";
        var specs = new List<(string Name, string Yaml)>();
        for (int i = 2; i < argv.Count; i++)
        {
            if (i == oi) { i++; continue; }
            if (argv[i] == "--builtin") specs.AddRange(RemixCatalogue.Builtin());
            else specs.Add((argv[i], File.ReadAllText(argv[i])));
        }
        int bad = 0;
        foreach (var (name, yaml) in specs)
        {
            try
            {
                var output = RemixBuilder.Build(install, RemixSpec.Parse(yaml));
                foreach (var f in output.WriteTo(outDir)) Console.WriteLine($"wrote {f}");
                Console.WriteLine(output.Stamp);
            }
            catch (RemixError ex) { Console.Error.WriteLine($"{name}: {ex.Message}"); bad++; }
        }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// i76 remix check [--builtin] [--seeds A-B] [--out DIR]: each shipped remix and each rolled job built and loaded into the sim,
    /// then held to its description and its map (RemixCheck: hostile cars on the enemy's side, where they are parked and marked,
    /// on the ground, radar contacts the target keys reach once out). Prints a line per problem; exit 1 when any.
    /// </summary>
    private static int Check(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        if (argv.Contains("--nodes")) return CheckNodes(install, argv);
        string outDir = Opt("--out") ?? Path.Combine(Path.GetTempPath(), "i26-remix-check-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        var jobs = new List<(string Name, RemixSpec Spec)>();
        if (argv.Contains("--builtin")) foreach (var (nm, y) in RemixCatalogue.Builtin()) jobs.Add((nm, RemixSpec.Parse(y)));
        if (Opt("--seeds") is { } range)
        {
            var ab = range.Split('-');
            int a = int.Parse(ab[0], System.Globalization.CultureInfo.InvariantCulture), b = ab.Length > 1 ? int.Parse(ab[1], System.Globalization.CultureInfo.InvariantCulture) : a;
            for (int seed = a; seed <= b; seed++) jobs.Add(($"seed {seed}", RemixDirector.Roll(seed, Ask(argv, "xchk", 1.0))));
        }
        int bad = 0, n = 0;
        var kinds = new Dictionary<string, int>();
        foreach (var (name, spec) in jobs)
        {
            n++;
            List<string> problems;
            try
            {
                var built = RemixBuilder.Build(install, spec);
                built.WriteTo(outDir);
                // a VFS of its own: the generated folder is listed as it is made, and this job was written after any earlier one
                var vfs = new AssetVfs(install);
                var asmb = new I76.Formats.Assembly.ModelAssembler(vfs);
                problems = I76.Sim.Missions.RemixCheck.Run(vfs, asmb, spec.Id, spec, RemixMarks.Parse(built.Marks ?? ""));
                // the drives it asks for, against its pacing budget, and every place inside a structure driven out of
                problems.AddRange(RemixPacing.Measure(install, spec).Problems);
                problems.AddRange(I76.Sim.Missions.RemixDriveOut.Check(vfs, asmb, spec));
            }
            catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException) { problems = [$"build: {ex.Message}"]; }
            string what = $"{name} ({spec.Base} {spec.Objective.Template})";
            if (problems.Count == 0) { if (argv.Contains("--verbose")) Console.WriteLine($"{what}: ok"); continue; }
            bad++;
            foreach (var p in problems) { Console.WriteLine($"{what}: {p}"); var k = System.Text.RegularExpressions.Regex.Replace(p, @"[0-9.()\-]+", "#"); kinds[k] = kinds.GetValueOrDefault(k) + 1; }
        }
        Console.WriteLine($"{n} checked, {bad} with problems");
        foreach (var (k, c) in kinds.OrderByDescending(k => k.Value).Take(20)) Console.WriteLine($"  {c,4}  {k}");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// i76 remix check --nodes [BASE|FILE.yaml...] [--verbose]: each node table (the shipped ones, or those named: a base's shipped
    /// table or a table file) held to its map (RemixNodeCheck: on the ground, on a road or drivable ground, clear of the exits,
    /// reachable from the first start). Prints a line per problem (and per place with --verbose); exit 1 when any.
    /// </summary>
    private static int CheckNodes(GameInstall install, List<string> argv)
    {
        var named = argv.Skip(2).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        var tables = new List<RemixNodes>();
        if (named.Count == 0) tables.AddRange(RemixNodes.Builtin().Where(t => install.FindMission(t.Base) is not null));
        foreach (var n in named)
            if (File.Exists(n)) tables.Add(RemixNodes.Parse(File.ReadAllText(n)));
            else tables.AddRange(RemixNodes.Builtin().Where(t => t.Base.Equals(n, StringComparison.OrdinalIgnoreCase)));
        int bad = 0;
        foreach (var t in tables)
        {
            var (problems, places) = RemixNodeCheck.Run(install, t);
            // each place inside or under a structure: a job started there (its doors off, its wall section out, its friendly car
            // parked) built and driven out of toward its way out
            foreach (var n in t.Nodes.Where(n => n.IsInside))
            {
                var r = I76.Sim.Missions.RemixDriveOut.Node(install, t, n);
                places.Add($"inside {n.Name} ({n.Called}): {r}");
                if (!r.Passed) problems.Add($"inside {n.Name} ({n.Called}): the car can't drive out: {r}");
            }
            if (argv.Contains("--verbose")) foreach (var p in places) Console.WriteLine($"{t.Base}: {p}");
            foreach (var p in problems) Console.WriteLine($"{t.Base}: PROBLEM {p}");
            Console.WriteLine($"{t.Base}: {t.Starts.Count} starts, {t.Nodes.Count} nodes, {t.Entries.Count} entries, {t.Routes.Count} routes, {t.Clusters.Count} clusters: {(problems.Count == 0 ? "ok" : $"{problems.Count} problems")}");
            if (problems.Count > 0) bad++;
        }
        return bad == 0 ? 0 : 1;
    }

    /// <summary>
    /// i76 remix score [--builtin] [--seeds A-B] [--level N]: each job's challenge (RemixChallenge) with its cars' measured
    /// strengths, against Back to Spanner's.
    /// </summary>
    private static int Score(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-score-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        int level = Opt("--level") is { } lv ? int.Parse(lv, System.Globalization.CultureInfo.InvariantCulture) : RemixChallenge.DefaultLevel;
        var jobs = new List<(string Name, RemixSpec Spec)>();
        if (argv.Contains("--builtin")) foreach (var (nm, y) in RemixCatalogue.Builtin()) jobs.Add((nm, RemixSpec.Parse(y)));
        if (Opt("--seeds") is { } range)
        {
            var ab = range.Split('-');
            int a = int.Parse(ab[0], System.Globalization.CultureInfo.InvariantCulture), b = ab.Length > 1 ? int.Parse(ab[1], System.Globalization.CultureInfo.InvariantCulture) : a;
            for (int seed = a; seed <= b; seed++) jobs.Add(($"seed {seed}", RemixDirector.Roll(seed, Ask(argv, "xsco", Opt("--challenge") is { } ch ? double.Parse(ch, System.Globalization.CultureInfo.InvariantCulture) : RemixChallenge.Levels[Math.Clamp(level, 0, RemixChallenge.Levels.Length - 1)]))));
        }

        var all = new List<double>();
        foreach (var (name, spec) in jobs)
        {
            RemixBuilder.Build(install, spec).WriteTo(outDir);
            var vfs = new AssetVfs(install);
            var (parts, cars) = I76.Sim.Missions.RemixCheck.Score(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), spec.Id, spec);
            all.Add(parts.Total);
            Console.WriteLine($"{name} ({spec.Base} {spec.Objective.Template}): {parts}  ({parts.Total / RemixChallenge.Spanner:0.00} Spanner)");
            if (argv.Contains("--cars")) foreach (var (c, st) in cars) Console.WriteLine($"    {c} {(spec.Cars.TryGetValue(c, out var cs) ? cs.Vcf : "")} {st:0.00}");
        }
        if (all.Count > 1) Console.WriteLine($"{all.Count} jobs: min {all.Min():0.0}, median {all.Order().ElementAt(all.Count / 2):0.0}, max {all.Max():0.0}");
        return 0;
    }

    /// <summary>
    /// i76 remix pace [--builtin] [--seeds A-B] [--trips A-B] [--stock] [--verbose]: each job's drives timed on its map's roads
    /// (RemixPacing: the first contact, each leg, the mean) and held to its budget; --trips walks road trips (each run's first
    /// offer, each job won) and measures their jobs; --stock times the stock trip missions' own first contact (the player's
    /// placement to the nearest enemy car) and the drive between their enemy groups (cars within 300 m), nearest next first.
    /// Prints the distributions (median, quartiles, the share over budget).
    /// </summary>
    private static int Pace(GameInstall install, List<string> argv)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        (int A, int B) Range(string r) { var ab = r.Split('-'); int a = int.Parse(ab[0], inv); return (a, ab.Length > 1 ? int.Parse(ab[1], inv) : a); }
        var jobs = new List<(string Name, RemixSpec Spec)>();
        if (argv.Contains("--builtin")) foreach (var (nm, y) in RemixCatalogue.Builtin()) jobs.Add((nm, RemixSpec.Parse(y)));
        if (Opt("--seeds") is { } sr) { var (a, b) = Range(sr); for (int seed = a; seed <= b; seed++) jobs.Add(($"seed {seed}", RemixDirector.Roll(seed, Ask(argv, "xpace", 1.0)))); }
        if (Opt("--trips") is { } tr)
        {
            var tables = RemixNodes.Builtin();
            var (a, b) = Range(tr);
            for (int n = a; n <= b; n++)
            {
                var run = RemixRun.Start(n, "vppirna1.vcf", "", "", 0);
                for (int k = 0; k < RemixRun.Jobs && !run.Over; k++)
                {
                    if (run.Phase == "reward") run.Reward("bonus");
                    var offers = run.Offers(tables);
                    if (offers.Count == 0) break;
                    foreach (var of in offers) jobs.Add(($"run {n} job {k + 1} offer {of.Map}/{of.Template}", run.Spec(of, tables)));
                    run.Take(offers[0]);
                    run.Finish(true, 0, null);
                }
            }
        }
        if (argv.Contains("--stock")) return PaceStock(install);
        // the places of note a job uses (docs/remix-missions.md "Places"): its start or a zone at one, and how many are inside
        var tablesOf = RemixNodes.Builtin().ToDictionary(t => t.Base, StringComparer.OrdinalIgnoreCase);
        int noted = 0, inside = 0, notedStart = 0;
        var used = new Dictionary<string, int>();
        foreach (var (_, spec) in jobs)
        {
            if (!tablesOf.TryGetValue(spec.Base, out var tb)) continue;
            var pts = spec.Places.Values.Where(p => p.Count == 1).Select(p => p[0]).Append(spec.Start?.At ?? []).Where(p => p.Length == 2).ToList();
            var hit = tb.Nodes.Where(n => n.IsSpecial && pts.Any(p => System.Math.Abs(p[0] - n.At[0]) < 1 && System.Math.Abs(p[1] - n.At[1]) < 1)).ToList();
            if (hit.Count > 0) noted++;
            if (spec.Inside.Count > 0) inside++;
            if (spec.Start?.At is [var sx, var sz] && tb.Nodes.Any(n => n.IsSpecial && System.Math.Abs(sx - n.At[0]) < 1 && System.Math.Abs(sz - n.At[1]) < 1)) notedStart++;
            foreach (var n in hit) used[$"{spec.Base} {n.Name}"] = used.GetValueOrDefault($"{spec.Base} {n.Name}") + 1;
        }
        var firsts = new List<double>();
        var means = new List<double>();
        var legs = new Dictionary<string, List<double>>();
        int over = 0;
        var byTemplate = new Dictionary<string, (List<double> First, List<double> Mean, int Over, int N)>();
        foreach (var (name, spec) in jobs)
        {
            var rep = RemixPacing.Measure(install, spec);
            if (rep.Legs.Count == 0) { if (argv.Contains("--verbose")) Console.WriteLine($"{name} ({spec.Base} {spec.Objective.Template}): not measured (the stock start: the description moves no start)"); continue; }
            firsts.Add(rep.FirstContact);
            means.Add(rep.Mean);
            foreach (var t in rep.Legs) { if (!legs.TryGetValue(t.Leg.Name, out var l)) legs[t.Leg.Name] = l = []; l.Add(t.Seconds); }
            if (rep.Problems.Count > 0) over++;
            var k = spec.Objective.Template;
            if (!byTemplate.TryGetValue(k, out var bt)) bt = ([], [], 0, 0);
            bt.First.Add(rep.FirstContact); bt.Mean.Add(rep.Mean);
            byTemplate[k] = (bt.First, bt.Mean, bt.Over + (rep.Problems.Count > 0 ? 1 : 0), bt.N + 1);
            if (argv.Contains("--verbose")) Console.WriteLine($"{name} ({spec.Base} {spec.Objective.Template}): {(rep.Legs.Count == 0 ? "not measured (the stock start: the description moves no start)" : rep.ToString())}{(rep.Problems.Count > 0 ? "  OVER: " + string.Join("; ", rep.Problems) : "")}");
        }
        static string Q(List<double> v) { if (v.Count == 0) return "-"; var s = v.Order().ToList(); double P(double q) => s[(int)System.Math.Min(s.Count - 1, System.Math.Round(q * (s.Count - 1)))]; return $"median {P(0.5):0} s (quartiles {P(0.25):0}-{P(0.75):0}, 90% {P(0.9):0}, max {s[^1]:0})"; }
        Console.WriteLine($"{jobs.Count} jobs; over budget: {over} ({100.0 * over / System.Math.Max(1, jobs.Count):0} %)");
        Console.WriteLine($"  places of note: {noted} jobs use one ({notedStart} start at one; {inside} use a place inside a structure); {used.Count} different places");
        if (argv.Contains("--places")) foreach (var (k, v) in used.OrderBy(k => k.Key)) Console.WriteLine($"    {k}: {v}");
        Console.WriteLine($"  first contact: {Q(firsts)}");
        Console.WriteLine($"  mean between events: {Q(means)}");
        foreach (var (k, v) in byTemplate.OrderBy(k => k.Key)) Console.WriteLine($"  {k,-9} n {v.N,3}: first {Q(v.First)}; mean {Q(v.Mean)}; over {v.Over}");
        foreach (var (k, v) in legs.OrderBy(k => k.Key)) Console.WriteLine($"  leg {k,-10} {Q(v)}");
        return 0;
    }

    /// <summary>The stock trip missions' own pacing: the player's placement to the nearest enemy car, and between their enemy groups.</summary>
    private static int PaceStock(GameInstall install)
    {
        var firsts = new List<double>();
        var gaps = new List<double>();
        foreach (var t in RemixNodes.Builtin())
        {
            var path = install.FindMission(t.Base);
            if (path is null) continue;
            var doc = I76.Formats.Mission.Edit.MapDocument.Load(path);
            var cars = doc.Objects.Where(o => o.ObjectType == 1).ToList();
            var me = cars.FirstOrDefault(o => ((o.ObjectFlags >> 16) & 7) == 1 && (o.ObjectFlags & 0x10) != 0) ?? cars.FirstOrDefault(o => ((o.ObjectFlags >> 16) & 7) == 1);
            var foes = cars.Where(o => ((o.ObjectFlags >> 16) & 7) == 2).Select(o => new[] { (double)o.Position.X, (double)o.Position.Z }).ToList();
            if (me is null || foes.Count == 0) { Console.WriteLine($"{t.Base}: no player or no enemy cars"); continue; }
            var roads = RemixPacing.RoadsOf(install, t.Base);
            double[] at = [me.Position.X, me.Position.Z];
            // enemy groups: cars within 300 m of a group's first
            var groups = new List<double[]>();
            foreach (var f in foes) if (!groups.Any(g => System.Math.Sqrt((g[0] - f[0]) * (g[0] - f[0]) + (g[1] - f[1]) * (g[1] - f[1])) < 300)) groups.Add(f);
            var left = groups.ToList();
            var chain = new List<double>();
            var here = at;
            while (left.Count > 0)
            {
                var next = left.MinBy(g => roads.Metres(here, g))!;
                roads.Drive(here, next, out double secs);
                chain.Add(secs);
                left.Remove(next);
                here = next;
            }
            firsts.Add(chain[0]);
            gaps.AddRange(chain.Skip(1));
            Console.WriteLine($"{t.Base} {t.Name}: first contact {chain[0]:0} s; {groups.Count} enemy groups; then {string.Join(", ", chain.Skip(1).Select(c => $"{c:0}"))} s");
        }
        static double Med(List<double> v) => v.Count == 0 ? 0 : v.Order().ElementAt(v.Count / 2);
        Console.WriteLine($"stock: first contact median {Med(firsts):0} s (max {(firsts.Count > 0 ? firsts.Max() : 0):0}); between enemy groups median {Med(gaps):0} s, mean {(gaps.Count > 0 ? gaps.Average() : 0):0} s");
        return 0;
    }

    /// <summary>A roll's ask from the command line: --template T and --map BASE narrow it (a run's choice), --challenge the budget.</summary>
    private static RemixDirector.JobAsk Ask(List<string> argv, string id, double challenge)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        return new RemixDirector.JobAsk { Id = id, Challenge = challenge, Template = Opt("--template"), Map = Opt("--map") };
    }

    private static int Roll(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        RemixSpec spec;
        int seed = 0;
        // a road trip's job (docs/remix-missions.md "Reproducing a job"): the run's number and the picks the game log's line gives
        if (Opt("--run") is { } runArg)
        {
            int number = int.Parse(runArg, System.Globalization.CultureInfo.InvariantCulture);
            string picks = Opt("--picks") ?? "0";
            try
            {
                var (run, offer, s) = RemixRun.Walk(number, picks);
                if (Opt("--job") is { } jk && int.Parse(jk, System.Globalization.CultureInfo.InvariantCulture) != run.Legs.Count + 1)
                    Console.Error.WriteLine($"note: --picks {picks} leads to job {run.Legs.Count + 1}, not job {jk}");
                Console.Error.WriteLine($"run {number:000000} job {run.Legs.Count + 1}/{RemixRun.Jobs}: #{offer.Seed} {offer.Map} ({offer.Name}) {offer.Template} challenge {offer.Challenge:0.0} builder v{RemixBuilder.Version}");
                spec = s;
                seed = offer.Seed;
            }
            catch (RemixError ex) { Console.Error.WriteLine($"run {number}: {ex.Message}"); return 1; }
        }
        else
        {
            if (!int.TryParse(Opt("--seed"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out seed))
            {
                Console.Error.WriteLine("usage: i76 remix roll --seed N | --run N --job K --picks A,Bb,C [--id STEM] [--yaml FILE] [--build [--out DIR]]");
                return 2;
            }
            try { spec = RemixDirector.Roll(seed, Ask(argv, Opt("--id") ?? RemixDirector.JobStem, Opt("--challenge") is { } ch ? double.Parse(ch, System.Globalization.CultureInfo.InvariantCulture) : 1.0)); }
            catch (RemixError ex) { Console.Error.WriteLine($"seed {seed}: {ex.Message}"); return 1; }
        }
        var yaml = RemixDirector.ToYaml(spec);
        if (Opt("--yaml") is { } file) { File.WriteAllText(file, yaml); Console.WriteLine($"wrote {file}"); }
        else Console.Write(yaml);
        if (!argv.Contains("--build")) return 0;
        try
        {
            var output = RemixBuilder.Build(install, RemixSpec.Parse(yaml));
            foreach (var f in output.WriteTo(Opt("--out") ?? GameInstall.UserMissionDir ?? "remix")) Console.WriteLine($"wrote {f}");
            return 0;
        }
        catch (RemixError ex) { Console.Error.WriteLine($"seed {seed}: {ex.Message}"); return 1; }
    }

    /// <summary>
    /// i76 remix trip --number N [--picks A,B,...] [--rewards repair,rearm,...] [--lose K] [--damage F] [--write DIR]: a road trip walked
    /// by its number (RemixRun): the offers at each step, the pick taken (offer A, then B...; each job won, but the K-th lost), the reward
    /// after each win; --damage F (0..1) has each won job leave the car with that share of its armour, chassis, engine and rounds gone
    /// (the run's carry read off the built job's player car, as the game does); --write leaves the run's file in DIR as the game keeps it.
    /// </summary>
    private static int Trip(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        int number = int.Parse(Opt("--number") ?? "1", inv);
        var picks = (Opt("--picks") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.Parse(x, inv)).ToList();
        var rewards = (Opt("--rewards") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        int lose = int.Parse(Opt("--lose") ?? "0", inv);
        double damage = double.Parse(Opt("--damage") ?? "0", inv);
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-trip-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        var tables = RemixNodes.Builtin();
        var run = RemixRun.Start(number, "vppirna1.vcf", "Unnamed", "Piranha", 0);
        for (int step = 0; step < picks.Count && !run.Over; step++)
        {
            if (run.Phase == "reward") { var rw = step - 1 < rewards.Count ? rewards[step - 1] : "bonus"; run.Reward(rw); Console.WriteLine($"  reward: {rw}"); }
            var offers = run.Offers(tables);
            Console.WriteLine($"job {run.Legs.Count + 1} (from {(run.Map.Length == 0 ? "the start" : run.Map)}):");
            for (int i = 0; i < offers.Count; i++) Console.WriteLine($"  {i}: {offers[i].Map} {offers[i].Name} {RemixRun.JobWord(offers[i].Template)} {offers[i].Challenge:0.0} seed {offers[i].Seed}: {offers[i].Flavour}");
            var pick = offers[Math.Clamp(picks[step], 0, offers.Count - 1)];
            run.Take(pick);
            RunCar? car = null;
            if (damage > 0)
            {
                // the job built and its player car loaded, damaged by the share asked, and read off as the game does at a job's end
                var built = RemixBuilder.Build(install, run.Spec(pick, tables));
                built.WriteTo(outDir);
                var vfs = new AssetVfs(install);
                var asm = new I76.Formats.Assembly.ModelAssembler(vfs);
                var setup = I76.Sim.Missions.MissionSetup.Build(vfs, new I76.Formats.Assembly.MissionWorld(vfs, asm, install.FindMission(RemixRun.Stem)!), asm, run.Car, rand: new I76.Sim.Rng.CrtRand(1));
                var p = setup.World.Player!;
                if (run.State is { } carried) I76.Sim.Trip.RunCarry.Apply(carried, p);
                var d = p.Health!;
                for (int z = 0; z < 4; z++) { d.Armour[z] = (int)(d.Armour[z] * (1 - damage)); d.Chassis[z] = (int)(d.Chassis[z] * (1 - damage / 2)); }
                if (d.Engine is { } en) en.Hp = (int)(en.Hp * (1 - damage / 2));
                foreach (var w in p.Armory?.LoadOrder ?? []) w.Ammo = (int)(w.Ammo * (1 - damage));
                car = I76.Sim.Trip.RunCarry.Capture(p);
            }
            bool won = step + 1 != lose;
            run.Finish(won, 3, car);
            Console.WriteLine($"  took {pick.Map} {pick.Template}: {(won ? "won" : "lost")}; phase {run.Phase}{(run.State is { } st ? $"; car armour {st.ArmourLeft}% chassis {st.ChassisLeft}% ammo {st.AmmoLeft}%" : "")}");
        }
        if (!run.Over && run.Phase == "choose")
        {
            var offers = run.Offers(tables);
            Console.WriteLine($"job {run.Legs.Count + 1} offers:");
            for (int i = 0; i < offers.Count; i++) Console.WriteLine($"  {i}: {offers[i].Map} {RemixRun.JobWord(offers[i].Template)} {offers[i].Challenge:0.0}: {offers[i].Flavour}");
        }
        if (Opt("--write") is { } dir) { run.Save(dir); Console.WriteLine($"wrote {Path.Combine(dir, RemixRun.FileName)} ({run.Phase})"); }
        return 0;
    }
}
