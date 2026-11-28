using System.Globalization;
using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Trip;

namespace I76.Cli;

/// <summary>
/// i76 remix salvage --run N [--picks A,Bb,C] [--car FILE.vcf | --gear E.S.B.W.G] [--legacy] [--draws D] [--player FILE.vcf]
/// i76 remix salvage --run-file run.yaml [--legacy] [--draws D]
/// Per job of a run (docs/remix-missions.md "Rosters and salvage"): the enemies fielded (name, car file, ladder level and strength) against
/// the player's gear, and the field salvage a clean win offers (every hostile destroyed early, the pool fed as the game feeds it, thinned
/// over D draws of the shell's rand): how many items, the best part's level, how often a part at or above the player's gear in its slot
/// comes in a usable condition, and one draw's list. Without --picks each job takes the first offer and is won. --run-file reads a run
/// file (its number, its picks, its car's gear from its inventory) and measures the job it played last. --legacy rolls and levels the
/// salvage as builder v6 did (the stock pools, the trip mission's tier clamp, the stock thinning) for a before | after.
/// i76 remix salvage --survey N --players a.vcf,b.vcf,run.yaml [--sim K] [--csv FILE]: N rolled jobs (seed 1..N, job 1 + seed % 4 at the
/// run's challenge for it) for each player car (a car file, or a run file: its garage's car): the roster's levels against the gear, the
/// job's top car, the cars' count, the director's challenge against the player's own car (its measured strength in Piranhas), the best
/// part a clean win can leave; with --sim K the first K jobs each are played headless for the field's real conditions; the same seeds
/// rolled as builder v6 did beside them; every car fielded checked to be in the install.
/// </summary>
public static partial class RemixSalvageCommand
{
    /// <summary>The gear a command line asks for: --gear E.S.B.W.G, else --car FILE (its ladder entry), else --run-file's inventory, else null.</summary>
    public static string? GearArg(List<string> argv)
    {
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        if (Opt("--gear") is { } g) return RemixRoster.Gear.Parse(g)?.ToString() ?? throw new RemixError($"--gear {g}: E.S.B.W.G, trip levels 1-16");
        if (Opt("--car") is { } c) return RemixRoster.GearOfCar(c).ToString();
        if (Opt("--run-file") is { } f && File.Exists(f)) return RemixRun.Parse(File.ReadAllText(f)).Gear;
        return null;
    }

    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string name) => argv.IndexOf(name) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        if (Opt("--survey") is { } sv) return Survey(install, argv, int.Parse(sv, inv));
        bool legacy = argv.Contains("--legacy");
        int draws = Opt("--draws") is { } dr ? int.Parse(dr, inv) : 200;
        string player = Opt("--player") ?? "vppirna1.vcf";
        string? gearArg = GearArg(argv);
        var jobs = new List<(RemixRun Run, RunOffer Offer, RemixSpec Spec)>();
        if (Opt("--run-file") is { } file)
        {
            var kept = RemixRun.Parse(File.ReadAllText(file));
            // the picks to the job it played last: each leg's pick (a b after a bust), then the last
            var picks = string.Join(",", kept.Legs.Select((l, i) => (i < kept.Picks.Count ? kept.Picks[i] : 0).ToString(inv) + (l.Outcome == "bust" ? "b" : "")));
            if (kept.Phase == "playing" && kept.Picks.Count > kept.Legs.Count) picks += (picks.Length > 0 ? "," : "") + kept.Picks[kept.Legs.Count].ToString(inv);
            else if (kept.Legs.Count > 0) { var ps = picks.Split(','); ps[^1] = ps[^1].TrimEnd('b'); picks = string.Join(",", ps); }
            jobs.Add(RemixRun.Walk(kept.Number, picks, gear: legacy ? "" : gearArg));
            Console.WriteLine($"run file: run {kept.Number:000000}, picks {picks}, car {kept.Car}, gear {kept.Gear}");
        }
        else if (Opt("--run") is { } runArg)
        {
            int number = int.Parse(runArg, inv);
            if (Opt("--picks") is { } picks)
            {
                var steps = picks.Split(',');
                for (int k = 1; k <= steps.Length; k++) jobs.Add(RemixRun.Walk(number, string.Join(",", steps.Take(k)), gear: legacy ? "" : gearArg));
            }
            else
                for (int k = 1; k <= RemixRun.Jobs; k++) jobs.Add(RemixRun.Walk(number, string.Join(",", Enumerable.Repeat("0", k)), gear: legacy ? "" : gearArg));
        }
        else { Console.Error.WriteLine("usage: i76 remix salvage --run N [--picks A,Bb,C] [--car FILE.vcf | --gear E.S.B.W.G] [--legacy] [--draws D] | --run-file run.yaml [--legacy]"); return 2; }

        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-salvage-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        var gear = RemixRoster.Gear.Parse(gearArg) ?? RemixRoster.GearOfCar(player);
        Console.WriteLine($"player gear {gear} (level {gear.Level:0.00}){(legacy ? "; LEGACY: builder v6's stock pools, tier clamp and thinning" : "")}");
        foreach (var (run, offer, spec) in jobs)
        {
            var r = Measure(install, spec, gear, player, legacy, draws);
            Console.WriteLine();
            Console.WriteLine(string.Create(inv, $"JOB {run.Legs.Count + 1}/{RemixRun.Jobs} #{offer.Seed} {offer.Map} ({offer.Name}) {offer.Template} challenge {offer.Challenge:0.0}{(offer.Gear.Length > 0 ? " gear " + offer.Gear : "")}"));
            Console.Write(r.Text);
            // --write-run OUT: the run file with its field salvage replaced by this draw's (a capture of the FIELD SALVAGE screen)
            if (Opt("--write-run") is { } outRun && Opt("--run-file") is { } src && r.First is { } field)
            {
                var kept = RemixRun.Parse(File.ReadAllText(src));
                var vfs = new AssetVfs(install);
                var tinv = TripInventory.Load(kept.Inventory!, PartsDatabase.Build(vfs), vfs) ?? throw new InvalidDataException(src);
                tinv.LeaveField();
                tinv.Nodes.AddRange(field);
                kept.Inventory = tinv.Save();
                File.WriteAllText(outRun, kept.ToYaml());
                Console.WriteLine($"wrote {outRun}: its field the {field.Count} parts of draw 1");
            }
        }
        return 0;
    }

    /// <summary>One job's report and its numbers.</summary>
    public sealed record Report(string Text, int Cars, double MeanLevel, double MeanStrength, int Destroyed, double MeanItems, int MinItems, int MaxItems, double BestLevel, double FloorShare, double RedShare)
    {
        /// <summary>The first draw's field, as the FIELD SALVAGE screen would list it.</summary>
        public List<PartNode>? First { get; init; }
    }

    public static Report Measure(GameInstall install, RemixSpec spec, RemixRoster.Gear gear, string player, bool legacy, int draws)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        RemixBuilder.Build(install, spec).WriteTo(GameInstall.UserMissionDir!);
        var vfs = new AssetVfs(install);
        var db = PartsDatabase.Build(vfs);
        var win = RemixSalvage.CleanWin(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), spec.Id, spec, player, legacy);
        var levels = new List<double>();
        var strengths = new List<double>();
        foreach (var c in win.Cars)
        {
            var lad = RemixRoster.Of(c.Vcf);
            double lv = lad?.Level ?? 0;
            if (lad is not null) { levels.Add(lv); strengths.Add(lad.Strength); }
            sb.AppendLine(string.Create(inv, $"  {c.Name,-6} {c.Vcf,-9} {(lad is null ? "  (lead/police, not on the ladder)" : $"level {lv,5:0.00} ({(lv - gear.Level >= 0 ? "+" : "")}{lv - gear.Level:0.00}) strength {lad.Strength:0.00}  E{lad.Engine} S{lad.Suspension} B{lad.Brakes} W{lad.Wheels} G{lad.Gun} {lad.Best}")}"));
        }
        if (spec.Prize is { Count: > 0 } pz) sb.AppendLine($"  prize on a win: {string.Join(", ", pz.Select(k => $"{db.ByStem(k)?.Id ?? k} ({k}, level {PartTiers.Level(k)})"))}");
        sb.AppendLine(string.Create(inv, $"  {win.Cars.Count} hostile cars, {win.Destroyed} destroyed; the pool holds {win.Pool.Items.Count()} parts"));
        var counts = new List<int>();
        double best = 0;
        int floor = 0, red = 0, all = 0;
        List<PartNode>? first = null;
        for (int d = 1; d <= draws; d++)
        {
            var offered = RemixSalvage.Offered(win, db, d, legacy);
            first ??= offered;
            counts.Add(offered.Count);
            best += offered.Select(n => RemixSalvage.Level(n.Part)).DefaultIfEmpty(0).Max();
            if (offered.Any(n => RemixSalvage.AtGear(n, gear) && RemixSalvage.Usable(n))) floor++;
            red += offered.Count(n => !RemixSalvage.Usable(n));
            all += offered.Count;
        }
        double mean = counts.Count == 0 ? 0 : counts.Average();
        sb.AppendLine(string.Create(inv, $"  offered over {draws} draws: {mean:0.0} items ({counts.DefaultIfEmpty(0).Min()}-{counts.DefaultIfEmpty(0).Max()}), best part level {best / Math.Max(1, draws):0.0}, a usable part at or above the gear in {100.0 * floor / Math.Max(1, draws):0} %, in the red {100.0 * red / Math.Max(1, all):0} %"));
        if (first is not null)
        {
            sb.AppendLine("  draw 1:");
            foreach (var g in first.GroupBy(n => TripInventory.ListOf(n.Part.Type)).OrderBy(g => g.Key))
                sb.AppendLine("    " + string.Join("; ", g.Select(n => string.Create(inv, $"{n.Part.Id.Replace("  ", " ")} L{RemixSalvage.Level(n.Part)} {n.Health}/{n.Part.MaxHp}{(RemixSalvage.Usable(n) ? PartsDatabase.Severity(n.Health, n.Part.MaxHp) == 1 ? " green" : " yellow" : " red")}{(RemixSalvage.AtGear(n, gear) ? " *" : "")}"))));
        }
        return new Report(sb.ToString(), win.Cars.Count, levels.DefaultIfEmpty(0).Average(), strengths.DefaultIfEmpty(0).Average(), win.Destroyed, mean, counts.DefaultIfEmpty(0).Min(), counts.DefaultIfEmpty(0).Max(), best / Math.Max(1, draws), (double)floor / Math.Max(1, draws), (double)red / Math.Max(1, all)) { First = first };
    }
}
