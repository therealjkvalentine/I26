using System.Globalization;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Trip;

namespace I76.Cli;

/// <summary>i76 remix salvage --survey (see RemixSalvageCommand): rosters, challenge and salvage over many rolled jobs for several player cars.</summary>
public static partial class RemixSalvageCommand
{
    /// <summary>A player car for the survey: a car file, or a run file's garage car (served from memory as the game serves vehrun.vcf).</summary>
    private static (string Vcf, RemixRoster.Gear Gear, string Label) PlayerCar(AssetVfs vfs, PartsDatabase db, string arg)
    {
        if (arg.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            var run = RemixRun.Parse(File.ReadAllText(arg));
            var tinv = TripInventory.Load(run.Inventory!, db, vfs) ?? throw new InvalidDataException(arg);
            AssetVfs.Addon["vehrun.vcf"] = tinv.ToCar().ToVcf();
            return ("vehrun.vcf", RemixRoster.Gear.Parse(run.Gear)!, $"{Path.GetFileName(arg)} ({run.Car} as its garage left it)");
        }
        return (arg, RemixRoster.GearOfCar(arg), arg);
    }

    private static double Pct(IEnumerable<double> v, double q)
    {
        var l = v.Order().ToList();
        return l.Count == 0 ? 0 : l[Math.Clamp((int)Math.Round(q * (l.Count - 1)), 0, l.Count - 1)];
    }

    public static int Survey(GameInstall install, List<string> argv, int n)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        int sims = Opt("--sim") is { } k ? int.Parse(k, inv) : 0;
        var vfs = new AssetVfs(install);
        var db = PartsDatabase.Build(vfs);
        var me = I76.Sim.Vehicles.VehicleFactory.Create(vfs, "vppirna1.vcf");
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-survey-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        using var csv = Opt("--csv") is { } cf ? new StreamWriter(cf) : null;
        csv?.WriteLine("player,gear_level,seed,job,challenge,template,map,builder,cars,mean_rel,top_rel,rel_challenge,best_level,prize_level,sim_items,sim_floor,sim_red");
        int missing = 0;
        foreach (var arg in (Opt("--players") ?? "vppt01.vcf,vppirna1.vcf").Split(','))
        {
            var (vcf, gear, label) = PlayerCar(vfs, db, arg);
            double p = I76.Sim.Missions.RemixCheck.Strength(I76.Sim.Vehicles.VehicleFactory.Create(vfs, vcf), me);
            Console.WriteLine();
            Console.WriteLine(string.Create(inv, $"== {label}: gear {gear} (level {gear.Level:0.00}), strength {p:0.00} Piranhas, budget x{RemixRoster.BudgetScale(gear):0.00}"));
            foreach (bool legacy in new[] { true, false })
            {
                var rel = new List<double>(); var topRel = new List<double>(); var count = new List<double>(); var relCh = new List<double>(); var best = new List<double>();
                var simItems = new List<double>(); var simFloor = new List<double>(); var simRed = new List<double>();
                int above = 0, at = 0, cars = 0, prizes = 0, atOne = 0;
                for (int seed = 1; seed <= n; seed++)
                {
                    int job = 1 + seed % RemixRun.Jobs;
                    double ch = RemixRun.Challenges[job - 1];
                    RemixSpec spec;
                    try { spec = RemixDirector.Roll(seed, new RemixDirector.JobAsk { Id = "xsurv", Challenge = ch, Job = job, Gear = legacy ? null : gear.ToString() }); }
                    catch (RemixError) { continue; }
                    var hostiles = RemixRoles.Hostiles(spec).Select(r => r.Car).Distinct().Where(spec.Cars.ContainsKey).ToList();
                    var lads = new List<RemixRoster.Car>();
                    foreach (var h in hostiles)
                    {
                        var v = spec.Cars[h].Vcf;
                        if (!vfs.Exists(v + ".vcf")) { missing++; Console.WriteLine($"  seed {seed}: {h} {v}.vcf is not in the install"); }
                        if (RemixRoster.Of(v) is { Enemy: true } c) lads.Add(c);
                    }
                    var rs = lads.Select(c => c.Level - gear.Level).ToList();
                    rel.AddRange(rs);
                    cars += rs.Count; above += rs.Count(x => x > 0); at += rs.Count(x => x >= -1 && x <= 0);
                    count.Add(hostiles.Count);
                    double top = rs.Count > 0 ? rs.Max() : 0;
                    topRel.Add(top);
                    double score = RemixDirector.ScoreWith(spec, v => (RemixRoster.Of(v) is { Enemy: true } c ? c.Strength : RemixChallenge.PoolStrength(v)) / p) / RemixChallenge.Spanner;
                    relCh.Add(score / ch);
                    int bestLv = hostiles.Select(h => RemixRoster.Of(spec.Cars[h].Vcf)).OfType<RemixRoster.Car>().Select(c => Math.Max(Math.Max(c.Engine, c.Suspension), Math.Max(c.Brakes, c.Gun))).DefaultIfEmpty(0).Max();
                    int prizeLv = spec.Prize?.Select(PartTiers.Level).DefaultIfEmpty(0).Max() ?? 0;
                    if (ch >= 1.0) { atOne++; if (prizeLv > 0) prizes++; }
                    best.Add(legacy ? Math.Min(bestLv, LegacyCap(spec.Base)) : Math.Max(bestLv, prizeLv));
                    string sim = ",,";
                    if (seed <= sims)
                    {
                        try
                        {
                            var r = Measure(install, spec, gear, vcf, legacy, 20);
                            simItems.Add(r.MeanItems); simFloor.Add(r.FloorShare); simRed.Add(r.RedShare);
                            sim = string.Create(inv, $"{r.MeanItems:0.0},{r.FloorShare:0.00},{r.RedShare:0.00}");
                        }
                        catch (Exception ex) when (ex is RemixError or IOException or InvalidDataException) { Console.WriteLine($"  seed {seed}: not played ({ex.Message})"); }
                    }
                    csv?.WriteLine(string.Create(inv, $"{Path.GetFileName(arg)},{gear.Level:0.00},{seed},{job},{ch:0.0},{spec.Objective.Template},{spec.Base},{(legacy ? "v6" : "v7")},{hostiles.Count},{(rs.Count > 0 ? rs.Average() : 0):0.00},{top:0.00},{score / ch:0.00},{best[^1]},{prizeLv},{sim}"));
                }
                Console.WriteLine(string.Create(inv, $"  {(legacy ? "builder v6 (stock pools)" : "builder v7 (roster)     ")}: {count.Count} jobs, hostile cars {count.Average():0.0} ({count.Min()}-{count.Max()})"));
                Console.WriteLine(string.Create(inv, $"    enemy level - player's: median {Pct(rel, 0.5):+0.00;-0.00}, quartiles {Pct(rel, 0.25):+0.00;-0.00} .. {Pct(rel, 0.75):+0.00;-0.00}, 5-95 % {Pct(rel, 0.05):+0.00;-0.00} .. {Pct(rel, 0.95):+0.00;-0.00}; above the player {100.0 * above / Math.Max(1, cars):0} %, at or up to a level under {100.0 * at / Math.Max(1, cars):0} %"));
                Console.WriteLine(string.Create(inv, $"    the job's top car - player's: median {Pct(topRel, 0.5):+0.00;-0.00} ({Pct(topRel, 0.05):+0.00;-0.00} .. {Pct(topRel, 0.95):+0.00;-0.00})"));
                Console.WriteLine(string.Create(inv, $"    challenge against this car / the job's nominal: median {Pct(relCh, 0.5):0.00}, quartiles {Pct(relCh, 0.25):0.00} .. {Pct(relCh, 0.75):0.00}, 5-95 % {Pct(relCh, 0.05):0.00} .. {Pct(relCh, 0.95):0.00}"));
                Console.WriteLine(string.Create(inv, $"    best part level a clean win can leave: median {Pct(best, 0.5):0}, 5-95 % {Pct(best, 0.05):0} .. {Pct(best, 0.95):0}; a prize at challenge >= 1.0: {prizes} of {atOne}"));
                if (simItems.Count > 0)
                    Console.WriteLine(string.Create(inv, $"    played ({simItems.Count} jobs, 20 draws each): items median {Pct(simItems, 0.5):0.0} ({Pct(simItems, 0.05):0.0} .. {Pct(simItems, 0.95):0.0}); a usable part at or above the gear in {100 * simFloor.Average():0} % of won jobs; in the red {100 * simRed.Average():0} %"));
            }
        }
        Console.WriteLine($"cars not in the install: {missing}");
        return missing == 0 ? 0 : 1;
    }

    /// <summary>Builder v6's clamp: the best part level a job on this map could leave (the trip mission's level + 1, T04 and T12 as T03 and T11).</summary>
    private static int LegacyCap(string map) => new SalvagePool(RemixRun.SalvageStem(map)).Level;
}

/// <summary>A job's roster and its salvage at a glance, for i76 remix check (no play: every fielded car's parts as a clean win would leave them).</summary>
public static class RemixSalvageGlance
{
    /// <summary>
    /// The roster against the gear (each car's ladder level, the mean and the top), the prize, the field a clean win offers on average (every
    /// wreck's parts, the trip's thinning's expectation: wheels four a group, specials one, else 3, 1 or 2 by halves and quarters) and its best
    /// part's level.
    /// </summary>
    public static string Line(AssetVfs vfs, RemixSpec spec, RemixRoster.Gear? gear)
    {
        var inv = CultureInfo.InvariantCulture;
        var hostiles = RemixRoles.Hostiles(spec).Select(r => r.Car).Distinct().Where(spec.Cars.ContainsKey).Select(h => spec.Cars[h].Vcf).ToList();
        var levels = hostiles.Select(RemixRoster.Of).OfType<RemixRoster.Car>().Select(c => c.Level).ToList();
        var pool = gear is null ? new SalvagePool(RemixRun.SalvageStem(spec.Base)) : SalvagePool.Roguetrip(RemixRun.SalvageStem(spec.Base));
        foreach (var v in hostiles)
        {
            try
            {
                var (keys, _) = RemixRoster.Read(vfs, v + ".vcf");
                foreach (var k in keys) if (PartTiers.TypeOf(k) is 0) { for (int w = 0; w < 4; w++) pool.Add(k, 1, false); } else pool.Add(k, 1, false);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { }
        }
        double expect = pool.Items.GroupBy(i => (SalvagePool.TypeOf(i.Name), i.Name)).Sum(g => g.Key.Item1 switch
        {
            0 => Math.Min(4, g.Count()), 5 => 1, _ => 0.5 * Math.Min(3, g.Count()) + 0.25 * 1 + 0.25 * Math.Min(2, g.Count()),
        }) + (spec.Prize?.Count ?? 0);
        int best = pool.Items.Select(i => PartTiers.Level(i.Name)).Concat(spec.Prize?.Select(PartTiers.Level) ?? []).DefaultIfEmpty(0).Max();
        string g = gear is null ? "" : string.Create(inv, $" against gear {gear} (level {gear.Level:0.00})");
        return string.Create(inv, $"roster: {hostiles.Count} cars, {levels.Count} on the ladder, level mean {(levels.Count > 0 ? levels.Average() : 0):0.00} top {(levels.Count > 0 ? levels.Max() : 0):0.00}{g}; salvage of a clean win: about {expect:0} items, best part level {best}{(spec.Prize is { Count: > 0 } p ? ", prize " + string.Join(",", p) : "")}");
    }
}
