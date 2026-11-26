using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// The first, small director (docs/remix-missions.md "The director"): from a seed it picks a map (a node table), a template that
/// map's nodes allow, the nodes, the cars (from heat pools of the base game's own car files) and their counts, and writes a
/// remix description the builder takes as it takes a hand-written one. Pure and outside the simulation: the same seed and
/// tables give the same description (its own SplitMix64 stream, never the sim's or the wall clock's), and the seed is said in
/// the objective text ("Job N") so a job can be rolled again.
/// </summary>
public static class RemixDirector
{
    /// <summary>The stem a rolled job is built under (one at a time: the menu's RANDOM JOB rolls over it).</summary>
    public const string JobStem = "xjob";

    /// <summary>The car pools by heat: the base game's generic cars (their .vcf stems), lighter to heavier.</summary>
    public static readonly string[][] Heat =
    [
        ["vccameo1", "vfpalmn1", "vcgcour1", "vfypony1", "vazamz1", "vfcoupe1", "vdstag1"],
        ["vcmanta1", "vcvan1", "vfpalmn2", "vfratlr1", "vccameo2", "vcgcour2", "vfypony2", "valepre2"],
        ["vfratlr2", "vcmanta2", "vmxmarx2", "vavikea2", "vcroyal2", "vastryd2", "vcvan2"],
    ];
    /// <summary>The police, for a tail.</summary>
    public static readonly string[] Police = ["vcpolic1", "vcpolic2", "vcpolic3"];
    /// <summary>A convoy's lead: something worth stopping.</summary>
    public static readonly string[] Leads = ["vxmilk1", "vxmilk2", "vcvan2", "vxbus1"];

    /// <summary>The templates in the order they are drawn from.</summary>
    public static readonly string[] Templates = ["deliver", "tail", "intercept", "defend"];

    /// <summary>A job's description from a seed, over the node tables (the built-in ones by default).</summary>
    public static RemixSpec Roll(int seed, IReadOnlyList<RemixNodes>? tables = null, string id = JobStem)
    {
        tables ??= RemixNodes.Builtin();
        var r = new Rng(seed);
        // every (map, template) the tables allow, in table and template order; the seed picks one
        var choices = tables.SelectMany(t => Templates.Where(k => Allows(t, k)).Select(k => (Table: t, Template: k))).ToList();
        if (choices.Count == 0) throw new RemixError("no node table allows any template");
        var (map, template) = choices[r.Next(choices.Count)];
        int heat = r.Next(3);   // 0..2
        var spec = new RemixSpec
        {
            Id = id, Title = $"JOB {seed.ToString(CultureInfo.InvariantCulture)}", Base = map.Base, Seed = seed, Hour = map.Hour,
        };
        spec.Gates.Start.AddRange(map.Gates);
        var start = r.Pick(map.Starts.Where(s => StartAllows(map, template, s)).ToList());
        spec.Start = new PoseSpec { At = start.At, Heading = start.Heading };
        double[] Lot(int i) => [map.Lot[0] + 12 * (i % 6), map.Lot[1] + 12 * (i / 6)];
        int carNo = 0;
        string NewCar(string[] pool, double[]? at = null, double heading = 0)
        {
            var name = $"car{++carNo}";
            spec.Cars[name] = new CarSpec { Vcf = r.Pick(pool), At = at ?? Lot(carNo), Heading = heading };
            return name;
        }
        string Place(string name, params double[][] pts)
        {
            spec.Places[name] = [.. pts];
            return name;
        }
        string Entry(EntryNode e)
        {
            if (e.Path is { } stock) return stock;
            return Place("in_" + e.Name, [.. e.At]);
        }
        var o = spec.Objective;
        o.Template = template;
        o.Skill = 3 - heat;   // 3 (the lightest jobs) .. 1 (the best drivers)
        string job = $"Job {seed.ToString(CultureInfo.InvariantCulture)}";
        const string death = "You should be more careful... try to stay alive.", left = "You can't just leave.";
        switch (template)
        {
            case "deliver":
            {
                var pickup = r.Pick(map.Of("pickup").Where(n => Far(n.At, start.At, 700, 4000)).ToList());
                // home: the start (T05's way) or another home node away from the pickup
                var homes = map.Of("home").Where(n => Far(n.At, pickup.At, 700, 4500)).ToList();
                double[] home = homes.Count > 0 && r.Next(2) == 0 ? r.Pick(homes).At : start.At;
                o.Pickup = new ZoneSpec { Place = Place("pickup", pickup.At), Radius = 40 };
                o.Home = new ZoneSpec { Place = Place("home", home), Radius = 80 };
                o.Clear = 300;
                o.Deadline = (int)Math.Round(Dist(pickup.At, home) / 12 + 75);
                var entry = NearestEntry(map, pickup.At, 250, 1500, r);
                int n = 2 + heat + r.Next(2);
                o.Pursuit = new PursuitSpec { Entry = Entry(entry), Delay = 3, Speed = 25 };
                for (int i = 0; i < n; i++) o.Pursuit.Cars.Add(NewCar(Heat[Math.Min(2, heat + (i == 0 ? 1 : 0))]));
                // a guard or two parked by the pickup
                for (int i = 0; i < heat; i++)
                {
                    double a = r.Next(360) * Math.PI / 180;
                    var g = NewCar(Heat[heat], [pickup.At[0] + 60 * Math.Sin(a), pickup.At[1] + 60 * Math.Cos(a)], r.Next(360));
                    o.Sleepers.Add(new SleeperSpec { Car = g, Wake = 220 });
                }
                spec.Radio.Trap.Add("14sta05");
                spec.Radio.Wave.Add("07sgc23");
                spec.Radio.Win.Add("11sta08");
                spec.Text.Objectives.AddRange([$"{job}: drive out to the pickup and grab the goods.", "(hidden)Bring them home, and hurry!", "(hidden)Don't lead anybody home."]);
                spec.Text.Failures.AddRange([death, left, "Too slow. The deal's off."]);
                break;
            }
            case "tail":
            {
                var hide = r.Pick(map.Of("hideout").Where(n => Far(n.At, start.At, 2000, 99999)).ToList());
                var entry = r.Pick(map.Entries.Where(e => EntryNear(map, e, start.At, 250, 800)).ToList());
                o.Hideout = new ZoneSpec { Place = Place("hideout", hide.At), Radius = 120 };
                o.Lost = new LostSpec { Radius = 450 + 50 * heat, Seconds = 10 + 3 * heat };
                o.Clear = o.Lost.Radius;
                o.Deadline = (int)Math.Round(Dist(start.At, hide.At) / 10 + 150);
                o.Pursuit = new PursuitSpec { Entry = Entry(entry), Delay = 5, Speed = 25, Siren = true };
                for (int i = 0, n = 2 + heat + r.Next(2); i < n; i++) o.Pursuit.Cars.Add(NewCar(Police));
                spec.Radio.Start.Add("07sgc23");
                spec.Radio.Trap.Add("gdsgc74");
                spec.Radio.Win.Add("14sta06");
                spec.Text.Objectives.AddRange([$"{job}: shake the cops off your tail.", "(hidden)Get to the hideout without leading anybody in."]);
                spec.Text.Failures.AddRange([death, left, "Too slow. The hideout's been cleared out.", "You led them right to us."]);
                break;
            }
            case "intercept":
            {
                var route = r.Pick(map.Routes.Where(rt => Far(RouteStart(rt), start.At, 500, 2500)).ToList());
                string routePlace = route.Path ?? Place("route", [.. route.Points]);
                var exit = route.Exit ?? route.Points[^1];
                var park = route.Park ?? (route.Points.Count > 0 ? route.Points[0] : throw new RemixError($"{map.Base} route {route.Name}: a stock path needs park"));
                double h = route.Heading * Math.PI / 180;
                var lead = NewCar(Leads, park, route.Heading);
                int n = 1 + heat + r.Next(2);
                var escorts = new List<string>();
                for (int i = 0; i < n; i++)
                    escorts.Add(NewCar(Heat[heat], [park[0] - Math.Sin(h) * 25 * (i + 1), park[1] - Math.Cos(h) * 25 * (i + 1)], route.Heading));
                o.Convoy = new ConvoySpec
                {
                    Lead = lead, Escorts = escorts, Route = routePlace, Exit = new ZoneSpec { Place = Place("exit", exit), Radius = 120 },
                    Speed = 18 + 2 * heat, AlertSpeed = 30 + 2 * heat, Alert = 250, Delay = 6,
                };
                spec.Radio.Start.Add("01sgc07");
                spec.Radio.Wave.Add("12sgc08");
                spec.Radio.Trap.Add("gdsgc16");
                spec.Radio.Win.Add("gdsgc32");
                spec.Text.Objectives.AddRange([$"{job}: stop the lead truck before it gets away.", "(hidden)Take out its escorts."]);
                spec.Text.Failures.AddRange([death, left, "They got away. Too late now."]);
                break;
            }
            case "defend":
            {
                var cl = r.Pick(map.Clusters);
                for (int i = 0; i < cl.Objects.Count; i++) { spec.Bind[$"s{i}"] = cl.Objects[i]; o.Defend.Add($"s{i}"); }
                foreach (var en in cl.Entries) o.Entries.Add(Entry(map.Entries.First(e => e.Name == en)));
                o.Arrive = new ZoneSpec { Place = Place("defend", cl.At), Radius = cl.Radius, Seconds = (int)Math.Round(Dist(start.At, cl.At) / 12 + 90) };
                int waves = 2 + (heat > 0 ? 1 : 0), per = 2, kills = 0;
                for (int w = 0; w < waves; w++)
                {
                    var wave = new WaveSpec { AtSeconds = w == 0 ? 20 + r.Next(15) : 0, AtKills = kills };
                    for (int i = 0; i < per; i++) wave.Cars.Add(NewCar(Heat[Math.Min(2, heat + (w == waves - 1 ? 1 : 0))]));
                    o.Waves.Add(wave);
                    kills += per;
                }
                o.Retarget = 20;
                spec.Radio.Trap.Add("gdsta14");
                spec.Radio.Wave.AddRange(["12sgc08", "07sgc23"]);
                spec.Radio.Win.AddRange(["gdsgc32", "10sta06"]);
                string called = cl.Called.Length > 0 ? cl.Called : "the place";
                spec.Text.Objectives.AddRange([$"{job}: get to {called} before the raiders do.", $"Keep {called} standing.", $"Take out all {waves * per} raiders."]);
                spec.Text.Failures.AddRange([death, left, $"Nothing left of {called} to save.", "Too late. The raiders got there first."]);
                break;
            }
        }
        return spec;
    }

    /// <summary>Whether a table has what a template needs.</summary>
    public static bool Allows(RemixNodes t, string template) => t.Lot.Length == 2 && t.Starts.Any(s => StartAllows(t, template, s));

    /// <summary>Whether a template can start there: a pickup, a hideout, a route within reach.</summary>
    private static bool StartAllows(RemixNodes t, string template, StartNode s) => template switch
    {
        "deliver" => t.Of("pickup").Any(p => Far(p.At, s.At, 700, 4000)) && t.Entries.Any(e => e.At.Count > 0),
        "tail" => t.Of("hideout").Any(h => Far(h.At, s.At, 2000, 99999)) && t.Entries.Any(e => EntryNear(t, e, s.At, 250, 800)),
        "intercept" => t.Routes.Any(rt => Far(RouteStart(rt), s.At, 500, 2500)),
        "defend" => t.Clusters.Count > 0,
        _ => false,
    };

    /// <summary>The description as YAML (the builder's own format; empty parts left out), with a line saying where it came from.</summary>
    public static string ToYaml(RemixSpec spec) =>
        $"# rolled by the remix director (docs/remix-missions.md) from seed {spec.Seed.ToString(CultureInfo.InvariantCulture)} on {spec.Base}: i76 remix roll --seed {spec.Seed.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}"
        + new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build().Serialize(spec);

    // ---- helpers ----

    private static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]));
    private static bool Far(double[] a, double[] b, double min, double max) => Dist(a, b) is var d && d >= min && d <= max;
    private static double[] RouteStart(RouteNode rt) => rt.Park ?? (rt.Points.Count > 0 ? rt.Points[0] : [double.NaN, double.NaN]);

    private static bool EntryNear(RemixNodes t, EntryNode e, double[] at, double min, double max) =>
        e.At.Count > 0 && Far(e.At[0], at, min, max);

    /// <summary>An entry between min and max metres from a point (the nearest when none is).</summary>
    private static EntryNode NearestEntry(RemixNodes t, double[] at, double min, double max, Rng r)
    {
        var ok = t.Entries.Where(e => EntryNear(t, e, at, min, max)).ToList();
        if (ok.Count > 0) return r.Pick(ok);
        return t.Entries.Where(e => e.At.Count > 0).OrderBy(e => Dist(e.At[0], at)).FirstOrDefault()
            ?? throw new RemixError($"{t.Base}: no entry with points");
    }

    /// <summary>SplitMix64: the director's own stream, the same on every machine and runtime.</summary>
    private sealed class Rng(int seed)
    {
        private ulong _s = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL);

        public int Next(int n)
        {
            if (n <= 0) throw new RemixError("the director drew from nothing");
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)n);
        }

        public T Pick<T>(IReadOnlyList<T> l) => l.Count > 0 ? l[Next(l.Count)] : throw new RemixError("the director drew from an empty list");
    }
}
