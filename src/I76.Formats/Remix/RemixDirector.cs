using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// The director (docs/remix-missions.md "The director"): from a seed it picks a map (a node table), a template that map's nodes
/// allow, the nodes (preferring the corners the stock mission never visits: away from the table's stock_action), a crew of the
/// game's own cast, and the cars, and writes a remix description the builder takes as it takes a hand-written one. Every job
/// has three steps or more (reach, fight or escape, return or finish, with a twist: reinforcements as a step opens), says where
/// each place is in words (what it is, how far and which way from the start), gives each step its radio instruction, and is
/// filled to a challenge budget (RemixChallenge: Back to Spanner's score times the level). Pure and outside the simulation:
/// the same seed, level and tables give the same description (its own SplitMix64 stream, never the sim's or the wall clock's),
/// and the job's number is its seed ("Job N"), so a job can be rolled again.
/// </summary>
public static class RemixDirector
{
    /// <summary>The stem a rolled job is built under (one at a time: the menu's RANDOM JOB rolls over it).</summary>
    public const string JobStem = "xjob";

    /// <summary>
    /// The car pools by heat: the base game's generic cars (their .vcf stems), in three tiers by their measured strength against
    /// the Piranha (i76 remix score --cars: hit points and guns' damage a second; about 0.3-0.6, 0.65-0.95 and 1.0-1.7).
    /// </summary>
    public static readonly string[][] Heat =
    [
        ["vastryd2", "valepre2", "vfratlr1", "vfcoupe1", "vfypony1", "vfratlr2", "vfypony2", "vazamz1", "vavikea2"],
        ["vccameo1", "vfpalmn1", "vccameo2", "vcmanta1", "vcgcour1", "vcvan1"],
        ["vcgcour2", "vcmanta2", "vfpalmn2", "vdstag1", "vcroyal2", "vmxmarx2"],
    ];
    /// <summary>The police, for a tail.</summary>
    public static readonly string[] Police = ["vcpolic1", "vcpolic2", "vcpolic3"];
    /// <summary>A convoy's lead: something worth stopping, and what the objective calls it.</summary>
    public static readonly string[] Leads = ["vxmilk1", "vxmilk2", "vcvan2", "vxbus1"];
    private static readonly string[] LeadCalled = ["milk truck", "milk truck", "van", "bus"];

    /// <summary>The templates in the order they are drawn from.</summary>
    public static readonly string[] Templates = ["deliver", "tail", "intercept", "defend"];

    /// <summary>The most cars a job sets on the player (the budget stops there).</summary>
    public const int MaxCars = 14;

    // The radio, by what it says (clips of the install by stem: never shipped, only named). Each step's instruction.
    private const string GoThere = "01sta63", GoNorth = "01sta20", Rendezvous = "14sta06", GetThem = "01sta26", Found = "01sta24",
        Company = "05sgc05", Bandits = "06sgc15", GotAway = "gdsgc74", Problem = "gdsta14", GotIt = "14sta05", UhOh = "07sgc23",
        LastOfThem = "gdsgc32", Clean = "10sta06", TookLong = "11sta08", OhNo = "12sgc08", RightOn = "gdsgc16";

    /// <summary>A job's description from a seed, over the node tables (the built-in ones by default), at a challenge level.</summary>
    public static RemixSpec Roll(int seed, IReadOnlyList<RemixNodes>? tables = null, string id = JobStem, int level = RemixChallenge.DefaultLevel)
    {
        tables ??= RemixNodes.Builtin();
        level = Math.Clamp(level, 0, RemixChallenge.Levels.Length - 1);
        var r = new Rng(seed);
        // every (map, template) the tables allow, in table and template order; the seed picks one
        var choices = tables.SelectMany(t => Templates.Where(k => Allows(t, k)).Select(k => (Table: t, Template: k))).ToList();
        if (choices.Count == 0) throw new RemixError("no node table allows any template");
        var (map, template) = choices[r.Next(choices.Count)];
        int heat = r.Next(3);   // 0..2: the pools the cars come from
        string num = seed.ToString(CultureInfo.InvariantCulture);
        var spec = new RemixSpec { Id = id, Title = $"JOB {num}", Base = map.Base, Seed = seed, Hour = map.Hour };
        spec.Gates.Start.AddRange(map.Gates);
        var start = r.Quiet(map, map.Starts.Where(s => StartAllows(map, template, s)).ToList(), s => s.At);
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
        string Entry(EntryNode e) => e.Path is { } stock ? stock : Place("in_" + e.Name, [.. e.At]);
        // structures set down before the start (docs/remix-missions.md "Structures"): wrecks beside the road, never across it
        void Prop(string obj, uint type, double[] at, double heading) { if (Dist(at, start.At) >= 60) spec.Props.Add(new PropSpec { Object = obj, Type = type, At = at, Heading = heading }); }
        // a roadblock's wrecks either side of an entry's road, a little ahead of where the cars come in
        void Roadblock(EntryNode e)
        {
            if (e.At.Count < 2) return;
            double[] a = e.At[0], b = e.At[1];
            double len = Dist(a, b), fx = (b[0] - a[0]) / len, fz = (b[1] - a[1]) / len, hd = Math.Atan2(fx, fz) * 180 / Math.PI;
            double[] mid = [a[0] + fx * 40, a[1] + fz * 40];
            Prop("akwreck2", 3, [mid[0] + fz * 8, mid[1] - fx * 8], hd + 70);
            Prop("akwreck2", 3, [mid[0] - fz * 8, mid[1] + fx * 8], hd - 110);
        }
        // the crew: a gang of the map's cast for a fight, its police for a tail
        var gangs = map.Cast.Where(c => c.Side == "gang").ToList();
        var cops = map.Cast.Where(c => c.Side == "police").ToList();
        string gang = gangs.Count > 0 ? r.Pick(gangs).Name : "the gang";
        string Crew() => gangs.Count > 0 ? $"{gang}'s crew" : "the gang";
        var o = spec.Objective;
        o.Template = template;
        o.Skill = 3 - heat;   // 3 (the lightest jobs) .. 1 (the best drivers)
        string job = $"Job {num}";
        const string left = "You can't just leave.";
        string Where(double[] at, string called) => $"{called}, {Distance(start.At, at)} {Compass(start.At, at)} of the start";
        string Go(double[] at) => Math.Abs(Bearing(start.At, at)) < 35 && Dist(start.At, at) < 2500 ? GoNorth : GoThere;
        // the groups the budget grows, each a way to add one more car of the right kind
        var grow = new List<(string Name, Func<int> Count, Action Add)>();
        switch (template)
        {
            case "deliver":
            {
                var pickup = r.Quiet(map, map.Of("pickup").Where(n => Far(n.At, start.At, 700, 4000)).ToList(), n => n.At);
                // home: the start (T05's way) or another home node away from the pickup
                var homes = map.Of("home").Where(n => Far(n.At, pickup.At, 700, 4500)).ToList();
                var homeNode = homes.Count > 0 && r.Next(2) == 0 ? r.Quiet(map, homes, n => n.At) : null;
                double[] home = homeNode?.At ?? start.At;
                string homeCalled = homeNode is null ? $"where you started ({start.Called})" : Where(home, homeNode.Called);
                o.Pickup = new ZoneSpec { Place = Place("pickup", pickup.At), Radius = 40 };
                o.Home = new ZoneSpec { Place = Place("home", home), Radius = 80 };
                o.Clear = 300;
                o.Deadline = (int)Math.Round(Dist(pickup.At, home) * 1.3 / 14 + 75);
                var entry = NearestEntry(map, pickup.At, 250, 1500, r);
                o.Pursuit = new PursuitSpec { Entry = Entry(entry), Delay = 3, Speed = 25 };
                o.Pursuit.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)]));
                grow.Add(("pursuit", () => o.Pursuit.Cars.Count, () => o.Pursuit.Cars.Add(NewCar(Heat[heat]))));
                // guards parked by the pickup, woken as he comes
                // on the road at the pickup itself (the node is on a road), a few metres apart: 60 m off in any direction put one on a
                // roof or a slope
                void Guard()
                {
                    double a = (r.Next(8) * 45 + 22.5) * Math.PI / 180, d = 6 + 5 * o.Sleepers.Count;
                    var g = NewCar(Heat[heat], [pickup.At[0] + d * Math.Sin(a), pickup.At[1] + d * Math.Cos(a)], r.Next(360));
                    o.Sleepers.Add(new SleeperSpec { Car = g, Wake = 220 });
                }
                Guard();
                grow.Add(("guards", () => o.Sleepers.Count, Guard));
                // the goods are in a pile of wrecks beside the road at the pickup
                Prop("akwreck3", 2, [pickup.At[0] + 22, pickup.At[1] + 6], r.Next(360));
                // the twist: they cut the way home as the return starts
                var cut = NearestEntry(map, home, 400, 1500, r);
                var re = new ReinforceSpec { Entry = Entry(cut), AtStep = 2, Delay = 15, Speed = 25 };
                re.Cars.Add(NewCar(Heat[heat]));
                o.Reinforce.Add(re);
                grow.Add(("cut-off", () => re.Cars.Count, () => re.Cars.Add(NewCar(Heat[heat]))));
                spec.Radio.Steps.AddRange([[Go(pickup.At)], [GotIt], []]);
                spec.Radio.Wave.Add(Company);
                spec.Radio.Reinforce.Add(Problem);
                spec.Radio.Win.Add(TookLong);
                spec.Text.Objectives.AddRange([$"{job}: grab the goods {Crew()} left at the pickup.", "(hidden)Bring them home, and hurry!", "(hidden)Don't lead anybody home."]);
                spec.Text.Notes.AddRange([$"Pickup: {Where(pickup.At, pickup.Called)}.", $"Home: {homeCalled}."]);
                spec.Text.Deaths.AddRange(["You were killed before you got to the pickup.", "You were killed bringing the goods home.", $"You were killed with {Crew()} right behind you."]);
                spec.Text.Failures.AddRange([Deaths0, left, "Too slow. The deal's off."]);
                break;
            }
            case "tail":
            {
                bool HasSafe(PointNode h) => map.Of("hideout").Concat(map.Of("home")).Any(n => n != h && Far(n.At, h.At, 700, 3000));
                var hide = r.Quiet(map, map.Of("hideout").Where(n => Far(n.At, start.At, 1500, 99999) && HasSafe(n)).ToList(), n => n.At);
                var safes = map.Of("hideout").Concat(map.Of("home")).Where(n => n != hide && Far(n.At, hide.At, 700, 3000)).Distinct().ToList();
                var safe = r.Quiet(map, safes, n => n.At);
                var entry = r.Pick(map.Entries.Where(e => EntryNear(map, e, start.At, 250, 550)).ToList());
                string police = cops.Count > 0 ? r.Pick(cops).Name : "The sheriff";
                o.Hideout = new ZoneSpec { Place = Place("hideout", hide.At), Radius = 120 };
                o.Lost = new LostSpec { Radius = 450 + 50 * heat, Seconds = 10 + 3 * heat };
                o.Clear = o.Lost.Radius;
                o.Deadline = (int)Math.Round((Dist(start.At, hide.At) + Dist(hide.At, safe.At)) * 1.3 / 12 + 200);
                o.Pursuit = new PursuitSpec { Entry = Entry(entry), Delay = 5, Speed = 25, Siren = true };
                o.Pursuit.Cars.Add(NewCar(Police));
                o.Pursuit.Cars.Add(NewCar(Police));
                grow.Add(("pursuit", () => o.Pursuit.Cars.Count, () => o.Pursuit.Cars.Add(NewCar(Police))));
                // the twist: more of them come in on the way to the hideout, then a stakeout's chase on the way to the safe house
                var block = NearestEntry(map, hide.At, 500, 1800, r);
                var re = new ReinforceSpec { Entry = Entry(block), AtStep = 2, Delay = 10, Speed = 25, Siren = true };
                Roadblock(block);
                re.Cars.Add(NewCar(Police));
                o.Reinforce.Add(re);
                grow.Add(("roadblock", () => re.Cars.Count, () => re.Cars.Add(NewCar(Police))));
                var stake = NearestEntry(map, hide.At, 300, 1500, r);
                var re2 = new ReinforceSpec { Entry = Entry(stake), AtStep = 3, Delay = 8, Speed = 25 };
                re2.Cars.Add(NewCar(Heat[heat]));
                o.Reinforce.Add(re2);
                grow.Add(("stakeout", () => re2.Cars.Count, () => re2.Cars.Add(NewCar(Heat[heat]))));
                o.Finish = new ZoneSpec { Place = Place("safe", safe.At), Radius = 100 };
                o.FinishClear = 300;
                o.FinishAfter = re2.Delay + 20;
                spec.Radio.Start.Add(UhOh);
                spec.Radio.Steps.AddRange([[], [GotAway, Rendezvous], [Problem]]);
                spec.Radio.Reinforce.Add(OhNo);
                spec.Radio.Win.Add(Clean);
                string sc = $"{safe.Called}, {Distance(hide.At, safe.At)} {Compass(hide.At, safe.At)} of the hideout";
                spec.Text.Objectives.AddRange([$"{job}: {police} and the law are on your tail. Shake them.", "(hidden)Get to the hideout without leading anybody in.", $"(hidden)The hideout's being watched. Make for the safe house at {sc}."]);
                spec.Text.Notes.Add($"Hideout: {Where(hide.At, hide.Called)}.");
                spec.Text.Deaths.AddRange([$"{police} caught up with you.", "You were killed on the way to the hideout.", "You were killed on the way to the safe house."]);
                spec.Text.Failures.AddRange([Deaths0, left, "Too slow. The hideout's been cleared out.", "You led them right to us."]);
                break;
            }
            case "intercept":
            {
                var route = r.Quiet(map, map.Routes.Where(rt => Far(RouteStart(rt), start.At, 500, 2500)).ToList(), RouteStart);
                string routePlace = route.Path ?? Place("route", [.. route.Points]);
                var exit = route.Exit ?? route.Points[^1];
                var park = route.Park ?? (route.Points.Count > 0 ? route.Points[0] : throw new RemixError($"{map.Base} route {route.Name}: a stock path needs park"));
                double h = route.Heading * Math.PI / 180;
                int li = r.Next(Leads.Length);
                var lead = $"car{++carNo}";
                spec.Cars[lead] = new CarSpec { Vcf = Leads[li], At = park, Heading = route.Heading };
                var escorts = new List<string>();
                // behind the lead in pairs side by side, 20 m a pair (a single file of five ran 125 m back, off the map)
                void Escort()
                {
                    int i = escorts.Count;
                    double back = 20 * (i / 2 + 1), side = i % 2 == 0 ? -3.5 : 3.5;
                    escorts.Add(NewCar(Heat[heat], [park[0] - Math.Sin(h) * back + Math.Cos(h) * side, park[1] - Math.Cos(h) * back - Math.Sin(h) * side], route.Heading));
                }
                Escort();
                grow.Add(("escorts", () => escorts.Count, Escort));
                // it rolls when he comes near, or once he has had time to get there
                o.Convoy = new ConvoySpec
                {
                    Lead = lead, Escorts = escorts, Route = routePlace, Exit = new ZoneSpec { Place = Place("exit", exit), Radius = 120 },
                    Speed = 18 + 2 * heat, AlertSpeed = 30 + 2 * heat, Alert = 250, Delay = (int)Math.Round(Dist(start.At, park) * 1.3 / 20 + 20), Roll = 600,
                };
                // the twist: the gang's backup comes for him once the lead is down; then home with nobody behind
                var back = NearestEntry(map, exit, 400, 2000, r);
                var re = new ReinforceSpec { Entry = Entry(back), AtStep = 2, Delay = 12, Speed = 25 };
                Roadblock(back);
                re.Cars.Add(NewCar(Heat[heat]));
                o.Reinforce.Add(re);
                grow.Add(("backup", () => re.Cars.Count, () => re.Cars.Add(NewCar(Heat[heat]))));
                o.Finish = new ZoneSpec { Place = Place("home", start.At), Radius = 100 };
                o.FinishClear = 300;
                o.FinishAfter = re.Delay + 20;
                var near = map.Nodes.OrderBy(n => Dist(n.At, exit)).FirstOrDefault();
                string toward = near is not null && Dist(near.At, exit) < 600 ? $"toward {near.Called}" : $"{Compass(park, exit)}";
                spec.Radio.Start.Add(Found);
                spec.Radio.Steps.AddRange([[Go(park)], [GetThem], [Rendezvous]]);
                spec.Radio.Wave.Add(OhNo);
                spec.Radio.Trap.Add(RightOn);
                spec.Radio.Reinforce.Add(Bandits);
                spec.Radio.Win.Add(LastOfThem);
                string what = LeadCalled[li];
                spec.Text.Objectives.AddRange([$"{job}: {gang}'s {what} is pulling out. Stop it before it gets away.", "(hidden)Take out its escorts.", "(hidden)Get back to where you started, with nobody on your tail."]);
                spec.Text.Notes.AddRange([$"The {what}: parked {Distance(start.At, park)} {Compass(start.At, park)} of the start; it runs {toward}, {Distance(park, exit)} off."]);
                spec.Text.Deaths.AddRange([$"You were killed before you could stop the {what}.", $"{gang}'s escorts got you.", "You were killed on the way back."]);
                spec.Text.Failures.AddRange([Deaths0, left, "They got away. Too late now."]);
                break;
            }
            case "defend":
            {
                var cl = r.Pick(map.Clusters);
                for (int i = 0; i < cl.Objects.Count; i++) { spec.Bind[$"s{i}"] = cl.Objects[i]; o.Defend.Add($"s{i}"); }
                foreach (var en in cl.Entries) o.Entries.Add(Entry(map.Entries.First(e => e.Name == en)));
                o.Arrive = new ZoneSpec { Place = Place("defend", cl.At), Radius = cl.Radius, Seconds = (int)Math.Round(Dist(start.At, cl.At) * 1.3 / 12 + 90) };
                int kills = 0;
                WaveSpec Wave(int w)
                {
                    var wave = new WaveSpec { AtSeconds = w == 0 ? 20 + r.Next(15) : 0, AtKills = kills };
                    wave.Cars.Add(NewCar(Heat[heat]));
                    wave.Cars.Add(NewCar(Heat[heat]));
                    o.Waves.Add(wave);
                    kills += 2;
                    return wave;
                }
                Wave(0);
                Wave(1);
                // the waves grow, and once they are full another follows
                grow.Add(("waves", () => o.Waves.Count * 2 + o.Waves.Sum(w => w.Cars.Count - 2), () =>
                {
                    var last = o.Waves[^1];
                    if (last.Cars.Count < 3) { last.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)])); RecountKills(o); }
                    else { Wave(o.Waves.Count); RecountKills(o); }
                }));
                // the twist: a heavier crew comes in by one of the entries a minute and a quarter into the fight
                var late = new ReinforceSpec { Entry = o.Entries[r.Next(o.Entries.Count)], AtStep = 2, Delay = 75, Speed = 25 };
                late.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)]));
                o.Reinforce.Add(late);
                grow.Add(("late crew", () => late.Cars.Count, () => late.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)]))));
                o.Retarget = 20;
                spec.Radio.Steps.AddRange([[Go(cl.At)], [GetThem], []]);
                spec.Radio.Trap.Add("gdsta14");
                spec.Radio.Wave.AddRange([Bandits, UhOh]);
                spec.Radio.Reinforce.Add(Company);
                spec.Radio.Win.AddRange([LastOfThem, Clean]);
                string called = cl.Called.Length > 0 ? cl.Called : "the place";
                spec.Text.Objectives.AddRange([$"{job}: get to {called} before {Crew()} does.", $"Keep {called} standing.", $"Take out every one of {gang}'s raiders."]);
                spec.Text.Notes.Add($"{char.ToUpperInvariant(called[0])}{called[1..]}: {Distance(start.At, cl.At)} {Compass(start.At, cl.At)} of the start.");
                spec.Text.Deaths.AddRange([$"You were killed on the way to {called}.", $"You were killed defending {called}.", $"You were killed before the last of {gang}'s raiders fell."]);
                spec.Text.Failures.AddRange([Deaths0, left, $"Nothing left of {called} to save.", "Too late. The raiders got there first."]);
                break;
            }
        }
        // fill to the budget: the smallest group grows by a car until the score reaches the level's share of Back to Spanner's
        double budget = RemixChallenge.Spanner * RemixChallenge.Levels[level];
        for (int guard = 0; guard < 40 && grow.Count > 0; guard++)
        {
            if (Score(spec) >= budget * 0.95 || RemixRoles.Hostiles(spec).Count >= MaxCars) break;
            var g = grow.OrderBy(x => x.Count()).ThenBy(x => grow.IndexOf(x)).First();
            g.Add();
        }
        spec.Text.Notes.Add($"Job {num}: {map.Base.ToUpperInvariant()}, challenge {Score(spec) / RemixChallenge.Spanner:0.0} (Back to Spanner 1.0).");
        return spec;
    }

    /// <summary>The death line before a job's own: failure line 1, as every template's (the templates show each step's own).</summary>
    private const string Deaths0 = "You should be more careful... try to stay alive.";

    /// <summary>A job's challenge with the pools' measured strengths (RemixChallenge.PoolStrength).</summary>
    public static double Score(RemixSpec spec) =>
        RemixChallenge.Score(spec, n => spec.Cars.TryGetValue(n, out var c) ? RemixChallenge.PoolStrength(c.Vcf) : 0.8, n => spec.Cars.TryGetValue(n, out var c) ? c.At : null).Total;

    /// <summary>Each wave's kill count: the cars of the waves before it.</summary>
    private static void RecountKills(ObjectiveSpec o)
    {
        int k = 0;
        foreach (var w in o.Waves) { w.AtKills = k; k += w.Cars.Count; }
    }

    /// <summary>Whether a table has what a template needs.</summary>
    public static bool Allows(RemixNodes t, string template) => t.Lot.Length == 2 && t.Starts.Any(s => StartAllows(t, template, s));

    /// <summary>Whether a template can start there: a pickup, a hideout (and a safe house), a route within reach.</summary>
    private static bool StartAllows(RemixNodes t, string template, StartNode s) => template switch
    {
        "deliver" => t.Of("pickup").Any(p => Far(p.At, s.At, 700, 4000)) && t.Entries.Any(e => e.At.Count > 0),
        "tail" => t.Of("hideout").Any(h => Far(h.At, s.At, 1500, 99999) && t.Of("hideout").Concat(t.Of("home")).Any(n => n != h && Far(n.At, h.At, 700, 3000)))
            && t.Entries.Any(e => EntryNear(t, e, s.At, 250, 550)),
        "intercept" => t.Routes.Any(rt => Far(RouteStart(rt), s.At, 500, 2500)),
        "defend" => t.Clusters.Count > 0,
        _ => false,
    };

    /// <summary>The description as YAML (the builder's own format; empty parts left out), with a line saying where it came from.</summary>
    public static string ToYaml(RemixSpec spec) =>
        $"# rolled by the remix director (docs/remix-missions.md) from seed {spec.Seed.ToString(CultureInfo.InvariantCulture)} on {spec.Base}: i76 remix roll --seed {spec.Seed.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}"
        + new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections).DisableAliases()
            .Build().Serialize(spec);

    // ---- words for places ----

    /// <summary>"3 km", "1.5 km", "800 m".</summary>
    public static string Distance(double[] a, double[] b)
    {
        double d = Dist(a, b);
        if (d < 950) return $"{Math.Max(100, Math.Round(d / 100) * 100).ToString(CultureInfo.InvariantCulture)} m";
        double km = Math.Round(d / 500) * 0.5;
        return $"{km.ToString("0.#", CultureInfo.InvariantCulture)} km";
    }

    /// <summary>Degrees from north (+Z) toward east (+X), -180..180.</summary>
    public static double Bearing(double[] from, double[] to) => Math.Atan2(to[0] - from[0], to[1] - from[1]) * 180 / Math.PI;

    /// <summary>The eight-point compass word from one point to another ("north", "southwest").</summary>
    public static string Compass(double[] from, double[] to)
    {
        string[] w = ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];
        double b = (Bearing(from, to) + 360) % 360;
        return w[(int)Math.Round(b / 45) % 8];
    }

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

        /// <summary>
        /// A pick weighted toward the quiet corners: each weighs 1 + 8 × (its distance from the stock mission's action, over
        /// 1.2 km, at most 1)², so a place the stock mission never goes near is nine times as likely as one in the middle of it.
        /// </summary>
        public T Quiet<T>(RemixNodes map, IReadOnlyList<T> l, Func<T, double[]> at)
        {
            if (l.Count == 0) throw new RemixError("the director drew from an empty list");
            var w = l.Select(x => (int)Math.Round(100 * (1 + 8 * Math.Pow(Math.Min(1, map.Quiet(at(x)) / 1200), 2)))).ToList();
            int k = Next(w.Sum());
            for (int i = 0; i < l.Count; i++) { if (k < w[i]) return l[i]; k -= w[i]; }
            return l[^1];
        }
    }
}
