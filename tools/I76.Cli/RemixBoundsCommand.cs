using System.Globalization;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;

namespace I76.Cli;

/// <summary>
/// i76 remix bounds [--seeds A-B] [--grid N] [--places] [--seats N] [--threads K] [--verbose] [--tsv FILE]: how near each generated job
/// puts its players to the ways a mission ends by position (RemixBounds: the stock leave-map exits, the map's edge) and how packed its
/// marks are (RemixSpacing). --seeds rolls jobs, --grid N rolls N jobs of every (map, template) pair the tables allow, --places starts a
/// job at every start and place of note of every table. Prints the at-risk and packed jobs and the counts.
/// </summary>
public static class RemixBoundsCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        int seats = Opt("--seats") is { } s ? int.Parse(s, inv) : 8;
        int threads = Opt("--threads") is { } th ? int.Parse(th, inv) : Math.Max(1, Environment.ProcessorCount / 2);
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-bounds-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        var tables = RemixNodes.Builtin().Where(t => install.FindMission(t.Base) is not null && !t.Nitro).ToList();
        var jobs = new List<(string Name, Func<string, RemixSpec> Make)>();
        if (argv.Contains("--exits"))
        {
            foreach (var t in tables)
            {
                var p = install.FindMission(t.Base)!;
                var all = (new I76.Formats.Mission.MissionFile(p).ReadFsmProgram()?.Paths ?? []).Where(x => x.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase)).Select(x => x.Name);
                var watched = RemixBounds.Exits(p).Select(e => e.Name).ToList();
                Console.WriteLine($"{t.Base}: watched {string.Join(",", watched)}; exit paths not watched {string.Join(",", all.Except(watched))}");
            }
            return 0;
        }
        if (Opt("--seeds") is { } range)
        {
            var ab = range.Split('-');
            int a = int.Parse(ab[0], inv), b = ab.Length > 1 ? int.Parse(ab[1], inv) : a;
            for (int seed = a; seed <= b; seed++) { int sd = seed; jobs.Add(($"seed {sd}", id => RemixDirector.Roll(sd, new RemixDirector.JobAsk { Id = id, Challenge = 1.0 }))); }
        }
        if (Opt("--runs") is { } runs)
        {
            var ab = runs.Split('-');
            int a = int.Parse(ab[0], inv), b = ab.Length > 1 ? int.Parse(ab[1], inv) : a;
            for (int n = a; n <= b; n++)
            {
                var run = CoopRun.Start(n);
                var offers = run.Offers();
                for (int k = 0; k < offers.Count; k++)
                {
                    var job = run.Job(offers[k], 2);
                    jobs.Add(($"run {n} offer {k}", id => { var sp = job.Spec(); sp.Id = id; return sp; }));
                }
            }
        }
        if (Opt("--grid") is { } g)
        {
            int n = int.Parse(g, inv);
            foreach (var t in tables)
                foreach (var k in RemixDirector.Templates.Where(k => RemixDirector.Allows(t, k)))
                    for (int i = 1; i <= n; i++)
                    {
                        int sd = 7919 * i + 104729 * (k.GetHashCode() & 0xff) + 31 * tables.IndexOf(t);
                        string map = t.Base, tk = k;
                        jobs.Add(($"grid {map} {tk} #{sd}", id => RemixDirector.Roll(sd, new RemixDirector.JobAsk { Id = id, Challenge = 1.0, Map = map, Template = tk })));
                    }
        }
        foreach (var f in argv.Skip(2).Where(a => a.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && File.Exists(a)))
        {
            string file = f;
            jobs.Add((Path.GetFileName(file), id => { var sp = RemixSpec.Parse(File.ReadAllText(file)); sp.Id = id; return sp; }));
        }
        if (argv.Contains("--places"))
            foreach (var t in tables)
            {
                var spots = t.Starts.Select(x => (x.Name, x.At, x.Heading)).Concat(t.Nodes.Where(x => x.IsSpecial || x.Kinds.Contains("start")).Select(x => (x.Name, x.At, x.Heading ?? 0))).ToList();
                foreach (var (name, at, heading) in spots)
                {
                    string map = t.Base;
                    var k = RemixDirector.Templates.First(k => RemixDirector.Allows(t, k));
                    jobs.Add(($"place {map} {name}", id =>
                    {
                        var spec = RemixDirector.Roll(1, new RemixDirector.JobAsk { Id = id, Challenge = 1.0, Map = map, Template = k });
                        spec.Start = new PoseSpec { At = [.. at], Heading = heading };
                        return spec;
                    }));
                }
            }
        var results = new (string Name, string Base, string Template, RemixBounds.Measure? M, RemixSpacing.Report? Sp, string? Error)[jobs.Count];
        int done = 0;
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, () => $"xb{Environment.CurrentManagedThreadId % 1000:000}", (i, _, id) =>
        {
            var (name, make) = jobs[i];
            try
            {
                var spec = make(id);
                var built = RemixBuilder.Build(install, spec);
                built.WriteTo(outDir);
                var vfs = new AssetVfs(install);
                var asm = new I76.Formats.Assembly.ModelAssembler(vfs);
                results[i] = (name, spec.Base, spec.Objective.Template, RemixBounds.Of(vfs, asm, spec.Id, spec, seats), RemixSpacing.Of(spec), null);
            }
            catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException or ArgumentException) { results[i] = (name, "", "", null, null, ex.Message); }
            int d = Interlocked.Increment(ref done);
            if (d % 200 == 0) Console.Error.WriteLine($"{d}/{jobs.Count}");
            return id;
        }, _ => { });

        int risk = 0, idleWarn = 0, idleFail = 0, lineWarn = 0, lineFail = 0, driftFail = 0, driftOff = 0, placeWarn = 0, packed = 0, errors = 0;
        var byMap = new Dictionary<string, int>();
        using var tsv = Opt("--tsv") is { } tf ? new StreamWriter(tf) : null;
        tsv?.WriteLine("job\tbase\ttemplate\tstart_exit\tstart_edge\tseat_exit\tseat_edge\tplace_exit\tplace_edge\tline_exit\tline_edge\tdrift_exit\tdrift_off\tmin_pair\tpair\tstart_first\tpacked");
        foreach (var (name, b, k, m, sp, err) in results)
        {
            if (m is null || sp is null) { errors++; Console.WriteLine($"{name}: ERROR {err}"); continue; }
            if (m.IdleWarns) idleWarn++;
            if (m.IdleFails) idleFail++;
            if (m.LineWarns) lineWarn++;
            if (m.LineFails) lineFail++;
            if (m.DriftExit < RemixBounds.Fail) driftFail++;
            if (m.DriftOff) driftOff++;
            if (m.PlaceExit < RemixBounds.Warn) placeWarn++;
            var why = RemixBounds.Problems(m);
            bool exitRisk = why.Count > 0;
            if (exitRisk) { risk++; byMap[b] = byMap.GetValueOrDefault(b) + 1; }
            if (sp.Packed) packed++;
            tsv?.WriteLine(string.Join('\t', name, b, k, F(m.StartExit), F(m.StartEdge), F(m.SeatExit), F(m.SeatEdge), F(m.PlaceExit), F(m.PlaceEdge), F(m.LineExit), F(m.LineEdge), F(m.DriftExit), m.DriftOff ? 1 : 0, F((float)sp.MinPair), sp.Pair, F((float)sp.StartFirst), sp.Packed ? 1 : 0));
            if (exitRisk || sp.Packed || argv.Contains("--verbose"))
                Console.WriteLine($"{name} ({b} {k}): {(exitRisk ? "AT RISK (" + string.Join("; ", why) + ") " : "")}{(sp.Packed ? "PACKED " : "")}{m}; {sp}");
        }
        int n2 = results.Length - errors;
        Console.WriteLine($"{n2} jobs measured ({errors} not built), seats {seats}: at risk by an exit {risk} ({100.0 * risk / Math.Max(1, n2):0.0} %): idle warns {idleWarn}, idle fails {idleFail}, line warns {lineWarn}, line fails {lineFail}, wander fails {driftFail}, a place inside the warning {placeWarn}; the map's edge within a 200 m wander {driftOff}; packed {packed} ({100.0 * packed / Math.Max(1, n2):0.0} %)");
        foreach (var (m, c) in byMap.OrderByDescending(x => x.Value)) Console.WriteLine($"  {m}: {c} at risk");
        return risk + packed == 0 ? 0 : 1;

        static string F(float v) => float.IsInfinity(v) ? "inf" : v.ToString("0", CultureInfo.InvariantCulture);
    }
}
