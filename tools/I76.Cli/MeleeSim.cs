using System.Globalization;
using I76.Formats.Vfs;
using I76.Server;
using I76.Sim.Missions;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;

namespace I76.Cli;

/// <summary>
/// i76 melee-sim: a headless network melee of bots of deliberately different skill, Handicaps off against Auto, over several
/// seeds (docs/multiplayer.md "Auto handicap"), printing each bot's kills and deaths, the spread of kills (max − min and the
/// standard deviation), the leader's and the trailing bot's kills, and how often levels moved. Each bot's AI tier
/// (1 best … 5 worst, the setSkill tables) is set on its entity after the game is built, a test-only knob; the game itself is the
/// server's, so a run repeats for a seed. The tier only gates when the AI fires (its rounds fly true), which separates the bots
/// little, so each bot also has a strength, a factor on the damage its rounds, splash and flame land (a better player lands more damage
/// a minute), applied in a decorator over the game's damage sink: the other test-only knob.
///   i76 melee-sim [--arena m05] [--minutes 10] [--seeds 1,2,3,4,5] [--tiers 1,1,3,3,5,5] [--strength 1.5,1.5,1,1,0.6,0.6] [--cars a.vcf,b.vcf,...] [--mode off|auto|both]
///                 [--ai N] [--threads 2] [--log]
/// </summary>
public static class MeleeSim
{
    private sealed record Result(string Mode, int Seed, string[] Names, int[] Tiers, int[] Kills, int[] Deaths, int[] Levels, int Changes, int Events);

    public static int Run(GameInstall install, List<string> argv)
    {
        string Opt(string name, string d) { int i = argv.IndexOf(name); return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : d; }
        var arena = Opt("--arena", "m05");
        double minutes = double.Parse(Opt("--minutes", "10"), CultureInfo.InvariantCulture);
        var seeds = Opt("--seeds", "1,2,3,4,5").Split(',').Select(int.Parse).ToArray();
        var tiers = Opt("--tiers", "1,1,3,3,5,5").Split(',').Select(int.Parse).ToArray();
        var strengths = Opt("--strength", "1.5,1.5,1,1,0.6,0.6").Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        var cars = Opt("--cars", "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var modes = Opt("--mode", "both") switch { "off" => new[] { HandicapMode.Off }, "auto" => [HandicapMode.Auto], _ => [HandicapMode.Off, HandicapMode.Auto] };
        int threads = int.Parse(Opt("--threads", "2"), CultureInfo.InvariantCulture);
        int ai = int.Parse(Opt("--ai", "0"), CultureInfo.InvariantCulture);
        bool log = argv.Contains("--log");
        Console.WriteLine($"melee-sim: {arena}, {tiers.Length} bots (tiers {string.Join(",", tiers)}, strengths {string.Join(",", strengths)}), {ai} AI fill, {minutes} min, seeds {string.Join(",", seeds)}; auto: step {Handicaps.AutoStep}, band {Handicaps.AutoBand}, cap {Handicaps.AutoCap}");

        var jobs = modes.SelectMany(m => seeds.Select(s => (m, s))).ToList();
        var results = new Result[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, i =>
        {
            var (mode, seed) = jobs[i];
            results[i] = Play(install.Root, arena, minutes, seed, tiers, strengths, cars, ai, mode, log ? Console.Out : null);
            lock (results) Console.WriteLine(Line(results[i]));
        });

        Console.WriteLine();
        Console.WriteLine("| mode | seed | " + string.Join(" | ", results[0].Names.Select((n, k) => $"{Short(n)} t{results[0].Tiers[k]}")) + " | spread | sd | leader | runner-up | median | last | margin | strong ranks | strong first | changes |");
        Console.WriteLine("|---|---|" + string.Concat(Enumerable.Repeat("---|", results[0].Names.Length)) + "---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results.OrderBy(r => r.Mode).ThenBy(r => r.Seed)) Console.WriteLine(Row(r));
        foreach (var mode in modes)
        {
            var rs = results.Where(r => r.Mode == mode.ToString()).ToList();
            Console.WriteLine($"| {mode} | mean | " + string.Join(" | ", Enumerable.Range(0, tiers.Length).Select(k => $"{rs.Average(r => r.Kills[k]):0.0}"))
                + $" | {rs.Average(r => Spread(r)):0.0} | {rs.Average(r => Sd(r)):0.00} | {rs.Average(r => Sorted(r)[0]):0.0} | {rs.Average(r => Sorted(r)[1]):0.0} | {rs.Average(Median):0.0} | {rs.Average(r => r.Kills.Min()):0.0} | {rs.Average(Margin):0.0} | {rs.Average(r => StrongRank(r)):0.0} | {rs.Count(StrongFirst)}/{rs.Count} | {rs.Average(r => r.Changes):0.0} |");
        }
        return 0;
    }

    /// <summary>One game: the bots in tier order (the first bot seat is the highest slot), the mode, stepped for the minutes; the counts at the end.</summary>
    private static Result Play(string install, string arena, double minutes, int seed, int[] tiers, float[] strengths, string[] cars, int ai, HandicapMode mode, TextWriter? log)
    {
        var o = new ServerOptions
        {
            Install = install, Arena = arena, Seed = seed, Bots = tiers.Length, MaxPlayers = tiers.Length + 2, Handicaps = mode,
            TimeLimit = (float)(minutes * 60) + 30, BotChoices = cars, Ai = ai,
        };
        var host = new MeleeHost(o);
        if (log is not null) host.Log += m => log.WriteLine($"[{mode} {seed}] {m}");
        // the bots by seat (the first bot takes the highest slot), each on its tier (SetSkill's tables: driving and shooting)
        var bots = host.Bots.OrderByDescending(b => b.Slot).ToList();
        var strength = new Dictionary<Vehicle, float>();
        for (int k = 0; k < bots.Count; k++)
        {
            var p = bots[k].Player;
            host.Setup.Script!.Host.SetSkill(p.Entity, tiers[k], tiers[k]);
            p.Entity.Aggression = tiers[k] - 1;
            strength[p.Car] = k < strengths.Length ? strengths[k] : 1f;     // the car is renewed in place: the same object all game
        }
        host.Setup.Combat.Damage = new Strength(host.Setup.Combat.Damage!, a => a is Vehicle v && strength.TryGetValue(v, out float f) ? f : 1f);
        int changes = 0, events = 0;
        host.Game.HandicapChanged += _ => changes++;
        host.Setup.Damage.KilledBy += (_, _) => events++;
        int ticks = (int)(minutes * 60 / host.Session.TickSeconds);
        for (int t = 0; t < ticks && !host.Over; t++) host.Step();
        return new Result(mode.ToString(), seed, [.. bots.Select(b => b.Name)], tiers, [.. bots.Select(b => b.Player.Kills)], [.. bots.Select(b => b.Player.Deaths)],
            [.. bots.Select(b => b.Player.Car.Handicap)], changes, events);
    }

    /// <summary>The strength knob: what a bot's rounds, splash and flame land, scaled by its strength, in a copy of the packet (a splash packet may go to several cars).</summary>
    private sealed class Strength(IDamageSink inner, Func<object?, float> of) : IDamageSink
    {
        private static DamagePacket Scaled(DamagePacket p, float f)
        {
            if (f == 1f) return p;
            var q = new DamagePacket { Attacker = p.Attacker, Mask = p.Mask, Flags = p.Flags, Kind = p.Kind };
            for (int c = 0; c < 4; c++) q.Ch[c] = (int)(p.Ch[c] * f);
            return q;
        }
        public void Apply(object victim, DamagePacket p, System.Numerics.Vector3 hitLocal) => inner.Apply(victim, Scaled(p, of(p.Attacker)), hitLocal);
        public void Apply(object victim, DamagePacket p, System.Numerics.Vector3 hitLocal, int node) => inner.Apply(victim, Scaled(p, of(p.Attacker)), hitLocal, node);
        public void CaltropTyreHit(Vehicle v, System.Numerics.Vector3 at) => inner.CaltropTyreHit(v, at);
    }

    private static string Short(string name) => name.Replace(BotNames.Suffix, "").Split(' ')[0];
    private static int[] Sorted(Result r) => [.. r.Kills.OrderByDescending(k => k)];
    /// <summary>The leader's kills over the runner-up's.</summary>
    private static int Margin(Result r) { var s = Sorted(r); return s[0] - s[1]; }
    private static double Median(Result r) { var s = Sorted(r); int n = s.Length; return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0; }
    /// <summary>The bot with the most kills is one of the best tier (ties count).</summary>
    private static bool StrongFirst(Result r) { int best = r.Tiers.Min(), top = r.Kills.Max(); return Enumerable.Range(0, r.Tiers.Length).Any(i => r.Tiers[i] == best && r.Kills[i] == top); }
    private static int Spread(Result r) => r.Kills.Max() - r.Kills.Min();
    private static double Sd(Result r) { double m = r.Kills.Average(); return Math.Sqrt(r.Kills.Average(k => (k - m) * (k - m))); }
    /// <summary>The mean rank (1 the most kills) of the best-tier bots.</summary>
    private static double StrongRank(Result r)
    {
        int best = r.Tiers.Min();
        var order = r.Kills.Select((k, i) => (k, i)).OrderByDescending(x => x.k).Select(x => x.i).ToList();
        return Enumerable.Range(0, r.Tiers.Length).Where(i => r.Tiers[i] == best).Average(i => order.IndexOf(i) + 1);
    }
    private static string Line(Result r) =>
        $"{r.Mode,-6} seed {r.Seed}: " + string.Join("  ", r.Names.Select((n, k) => $"{Short(n)}(t{r.Tiers[k]}) {r.Kills[k]}k/{r.Deaths[k]}d {Handicaps.Label(r.Levels[k])}"))
        + $"  | spread {Spread(r)} sd {Sd(r):0.00} changes {r.Changes} kills {r.Events}";
    private static string Row(Result r) =>
        $"| {r.Mode} | {r.Seed} | " + string.Join(" | ", r.Kills.Select((k, i) => $"{k}/{r.Deaths[i]} {Handicaps.Label(r.Levels[i])}"))
        + $" | {Spread(r)} | {Sd(r):0.00} | {Sorted(r)[0]} | {Sorted(r)[1]} | {Median(r):0.#} | {r.Kills.Min()} | {Margin(r)} | {StrongRank(r):0.0} | {(StrongFirst(r) ? "yes" : "no")} | {r.Changes} |";
}
