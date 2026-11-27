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
                problems = I76.Sim.Missions.RemixCheck.Run(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), spec.Id, spec, RemixMarks.Parse(built.Marks ?? ""));
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

    /// <summary>A roll's ask from the command line: --template T and --map BASE narrow it (a run's choice), --challenge the budget.</summary>
    private static RemixDirector.JobAsk Ask(List<string> argv, string id, double challenge)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        return new RemixDirector.JobAsk { Id = id, Challenge = challenge, Template = Opt("--template"), Map = Opt("--map") };
    }

    private static int Roll(GameInstall install, List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        if (!int.TryParse(Opt("--seed"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int seed))
        {
            Console.Error.WriteLine("usage: i76 remix roll --seed N [--id STEM] [--yaml FILE] [--build [--out DIR]]");
            return 2;
        }
        RemixSpec spec;
        try { spec = RemixDirector.Roll(seed, Ask(argv, Opt("--id") ?? RemixDirector.JobStem, Opt("--challenge") is { } ch ? double.Parse(ch, System.Globalization.CultureInfo.InvariantCulture) : 1.0)); }
        catch (RemixError ex) { Console.Error.WriteLine($"seed {seed}: {ex.Message}"); return 1; }
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
}
