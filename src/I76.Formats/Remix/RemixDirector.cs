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
    public static readonly string[] Templates = ["deliver", "tail", "intercept", "defend", "escort", "raid"];

    /// <summary>The most cars a job sets on the player (the budget stops there).</summary>
    public const int MaxCars = 14;

    // The radio, by what it says (clips of the install by stem: never shipped, only named). Each step's instruction.
    private const string GoThere = "01sta63", GoNorth = "01sta20", Rendezvous = "14sta06", GetThem = "01sta26", Found = "01sta24",
        Company = "05sgc05", GotAway = "gdsgc74", Problem = "gdsta14", GotIt = "14sta05", UhOh = "07sgc23",
        LastOfThem = "gdsgc32", Clean = "10sta06", TookLong = "11sta08", OhNo = "12sgc08", RightOn = "gdsgc16";

    /// <summary>
    /// The incoming-enemies cue (the owner: "just saying 'incoming bandits' is probably more subtle"): Groove's "Yeah, I dig. Dig
    /// incoming bandits." cut to its last two words (1625-2895 ms, the splice work's alignment), not the whole stuttering line.
    /// The good-rated "More creepers coming from the east." is the Nitro Pack's words spliced, and names a direction the job does
    /// not know; Taurus's "incoming bandits" takes are all from a panicked line the owner rated bad.
    /// </summary>
    public const string Bandits = "06sgc15@1625-2895";

    /// <summary>What each clip says, for the radio's text fallback (an install without the clip).</summary>
    public static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        [GoThere] = "Get over there.", [GoNorth] = "That's just north of here.", [Rendezvous] = "Get over to the rendezvous.", [GetThem] = "Pick your shots.",
        [Found] = "There they are.", [Company] = "Looks like we got company.", [Bandits] = "Incoming bandits.", [GotAway] = "We got away.", [Problem] = "We got a problem.",
        [GotIt] = "Got it.", [UhOh] = "Uh oh.", [LastOfThem] = "That's the last of them.", [Clean] = "We're clean.", [TookLong] = "Took long enough.", [OhNo] = "Oh no.",
        [RightOn] = "Right on.", ["gdsta14"] = "We got a problem.",
    };

    /// <summary>A job's description from a seed, over the node tables (the built-in ones by default), at a challenge level.</summary>
    public static RemixSpec Roll(int seed, IReadOnlyList<RemixNodes>? tables = null, string id = JobStem, int level = RemixChallenge.DefaultLevel) =>
        Roll(seed, new JobAsk { Tables = tables, Id = id, Challenge = RemixChallenge.Levels[Math.Clamp(level, 0, RemixChallenge.Levels.Length - 1)] });

    /// <summary>What a roll is asked for: the tables, the stem, the challenge (Back to Spanner's 1.0), and optionally the map and the template.</summary>
    public sealed record JobAsk
    {
        public IReadOnlyList<RemixNodes>? Tables { get; init; }
        public string Id { get; init; } = JobStem;
        public double Challenge { get; init; } = 1.0;
        /// <summary>Only this map (its base), or any.</summary>
        public string? Map { get; init; }
        /// <summary>Only this template, or any the map allows.</summary>
        public string? Template { get; init; }
        /// <summary>Whether the Nitro Pack's maps may be used (its install is the game's).</summary>
        public bool Nitro { get; init; }
        /// <summary>What the first objective line and the notes call the job ("Job 637742"; a run's "Road trip 12, job 2").</summary>
        public string? Label { get; init; }
    }

    /// <summary>A job's description from a seed and what is asked of it (a run picks the map, the template and the challenge).</summary>
    public static RemixSpec Roll(int seed, JobAsk ask)
    {
        var tables = (ask.Tables ?? RemixNodes.Builtin()).Where(t => ask.Nitro || !t.Nitro).ToList();
        string id = ask.Id;
        var r = new Rng(seed);
        // every (map, template) the tables allow, in table and template order; the seed picks one
        var choices = tables.Where(t => ask.Map is null || t.Base.Equals(ask.Map, StringComparison.OrdinalIgnoreCase))
            .SelectMany(t => Templates.Where(k => (ask.Template is null || k == ask.Template) && Allows(t, k)).Select(k => (Table: t, Template: k))).ToList();
        if (choices.Count == 0) throw new RemixError($"no node table allows {ask.Template ?? "any template"}{(ask.Map is null ? "" : " on " + ask.Map)}");
        var (map, template) = choices[r.Next(choices.Count)];
        int heat = r.Next(3);   // 0..2: the pools the cars come from
        string num = seed.ToString(CultureInfo.InvariantCulture);
        var spec = new RemixSpec { Id = id, Title = $"JOB {num}", Base = map.Base, Seed = seed, Hour = map.Hour };
        spec.Gates.Start.AddRange(map.Gates);
        // the start is placed near the job's first place (docs/remix-missions.md "Pacing"), not at the map's stock start: from the
        // table's starts and its nodes, the places inside or under structures preferred; each case below sets it
        var spots = Spots(map);
        var start = new StartNode { Name = "", At = [0, 0], Called = "" };
        void UseStart(Spot sp, double[] face)
        {
            double toward = (Bearing(sp.At, face) + 360) % 360;
            double hd = sp.Fixed ? sp.Heading : sp.Road && Math.Abs(((toward - sp.Heading + 540) % 360) - 180) < 100 ? sp.Heading : toward;
            start = new StartNode { Name = sp.Name, At = sp.At, Heading = hd, Called = sp.Called };
            spec.Start = new PoseSpec { At = sp.At, Heading = hd, Out = sp.Node?.Out };
            Inside(sp.Node);
            Friend(sp.Node);   // a start in a hideout: Skeeter's van parked beside the player
        }
        // a place inside or under a structure the job uses: its doors off, its wall section out, checked by driving out of it
        void Inside(PointNode? n)
        {
            if (n is not { Inside: not null } || spec.Inside.Any(x => x.Name == n.Name)) return;
            spec.Inside.Add(new InsideSpec { Name = n.Name, At = n.At, Heading = n.Heading ?? 0, Out = n.Out ?? n.At, Called = n.Called });
            foreach (var op in n.Open) if (!spec.Open.Any(x => x.Object == op.Object && x.Nth == op.Nth)) spec.Open.Add(op);
            foreach (var rm in n.Remove) if (!spec.Remove.Any(x => x.Object == rm.Object && x.Nth == rm.Nth)) spec.Remove.Add(rm);
        }
        // a friendly car parked at a hideout (Skeeter's van in the barn): the player's side, never started
        void Friend(PointNode? n)
        {
            if (n?.Friend is not { } f || spec.Cars.Values.Any(c => c.At.SequenceEqual(f.At))) return;
            spec.Cars[$"friend{spec.Cars.Count + 1}"] = new CarSpec { Vcf = f.Vcf, At = f.At, Heading = f.Heading, Team = 1 };
        }
        // the places with a start spot min..max metres from them (all of them when none has)
        List<T> WithStart<T>(List<T> l, Func<T, double[]> at, double min, double max, Func<T, PointNode?>? self = null)
        {
            var ok = l.Where(x => spots.Any(sp => (self is null || sp.Node is null || sp.Node != self(x)) && Far(sp.At, at(x), min, max))).ToList();
            return ok.Count > 0 ? ok : l;
        }
        // a spot near a place (min..max metres; the nearest beyond 300 m when none is), not the place itself
        Spot NearStart(double[] target, double min, double max, Func<Spot, bool>? ok = null, PointNode? not = null)
        {
            bool Fits(Spot sp) => sp.Node is null || sp.Node != not;
            var near = spots.Where(sp => Fits(sp) && Far(sp.At, target, min, max) && (ok is null || ok(sp))).ToList();
            if (near.Count == 0) near = spots.Where(sp => Fits(sp) && Far(sp.At, target, min, max)).ToList();
            if (near.Count == 0) near = [spots.Where(sp => Fits(sp) && Dist(sp.At, target) >= 300).OrderBy(sp => Dist(sp.At, target)).FirstOrDefault() ?? spots[0]];
            return r.Weighted(map, near, sp => sp.At, sp => sp.Node?.IsSpecial == true ? InsideWeight : sp.Node is null ? 2 : 1);
        }
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
            double len = Dist(a, b), fx = (b[0] - a[0]) / len, fz = (b[1] - a[1]) / len, hd = DetMath.Atan2(fx, fz) * 180 / Math.PI;
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
        string job = ask.Label ?? $"Job {num}";
        const string left = "You can't just leave.";
        string Where(double[] at, string called) => $"{called}, {Distance(start.At, at)} {Compass(start.At, at)} of the start";
        string Go(double[] at) => Math.Abs(Bearing(start.At, at)) < 35 && Dist(start.At, at) < 2500 ? GoNorth : GoThere;
        // the groups the budget grows, each a way to add one more car of the right kind
        var grow = new List<(string Name, Func<int> Count, Action Add)>();
        switch (template)
        {
            case "deliver":
            {
                var pickup = r.Weighted(map, WithStart(map.Of("pickup").ToList(), n => n.At, MinFirst, MaxFirst, n => n), n => n.At, n => n.IsSpecial ? InsideWeight : 1);
                UseStart(NearStart(pickup.At, MinFirst, MaxFirst, not: pickup), pickup.At);
                Inside(pickup);
                // home: the start (T05's way) or another home node a short drive from the pickup (not an end-to-end haul)
                var homes = map.Of("home").Where(n => Far(n.At, pickup.At, MinNext, MaxNext) && Dist(n.At, start.At) > 150).ToList();
                var homeNode = homes.Count > 0 && r.Next(2) == 0 ? r.Weighted(map, homes, n => n.At, n => n.IsSpecial ? InsideWeight : 1) : null;
                Inside(homeNode);
                Friend(homeNode);
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
                // (a pickup inside a building: the guards stand at its door, outside)
                double[] guardAt = pickup.IsInside && pickup.Out is { } po ? po : pickup.At;
                void Guard()
                {
                    double a = (r.Next(4) * 45 + 202.5) * Math.PI / 180, d = 6 + 5 * o.Sleepers.Count;   // the west side: the wrecks are east
                    var g = NewCar(Heat[heat], [guardAt[0] + d * DetMath.Sin(a), guardAt[1] + d * DetMath.Cos(a)], r.Next(360));
                    o.Sleepers.Add(new SleeperSpec { Car = g, Wake = 220 });
                }
                Guard();
                grow.Add(("guards", () => o.Sleepers.Count, Guard));
                // the goods are in a pile of wrecks beside the road at the pickup
                if (!pickup.IsInside) Prop("akwreck3", 2, [pickup.At[0] + 12, pickup.At[1] + 3], r.Next(360));   // on the shoulder: further out it stood on the bank
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
                var hides = map.Of("hideout").Where(n => HasSafe(map, n)).ToList();
                var hide = r.Weighted(map, WithStart(hides.Count > 0 ? hides : map.Of("hideout").ToList(), n => n.At, 900, 1500, n => n), n => n.At, n => n.IsSpecial ? InsideWeight : 1);
                // the start: far enough from the hideout to lose them on the way, with an entry behind it for the pursuit
                UseStart(NearStart(hide.At, 900, 1500, sp => map.Entries.Any(e => EntryNear(map, e, sp.At, 250, 700)), hide), hide.At);
                Inside(hide);
                Friend(hide);
                var safes = map.Of("hideout").Concat(map.Of("home")).Where(n => n != hide && Far(n.At, hide.At, MinNext, MaxNext)).Distinct().ToList();
                if (safes.Count == 0) safes = map.Of("hideout").Concat(map.Of("home")).Where(n => n != hide).OrderBy(n => Dist(n.At, hide.At)).Take(1).ToList();
                var safe = r.Weighted(map, safes, n => n.At, n => n.IsSpecial ? InsideWeight : 1);
                Inside(safe);
                var behind = map.Entries.Where(e => EntryNear(map, e, start.At, 250, 700)).ToList();
                var entry = behind.Count > 0 ? r.Pick(behind) : NearestEntry(map, start.At, 250, 900, r);
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
                var route = r.Quiet(map, WithStart(map.Routes.ToList(), RouteStart, MinFirst, MaxFirst), RouteStart);
                UseStart(NearStart(RouteStart(route), MinFirst, MaxFirst), RouteStart(route));
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
                    escorts.Add(NewCar(Heat[heat], [park[0] - DetMath.Sin(h) * back + DetMath.Cos(h) * side, park[1] - DetMath.Cos(h) * back - DetMath.Sin(h) * side], route.Heading));
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
                // home: the start, or a home node nearer where the convoy is caught (the middle of its run), whichever is nearer
                double[] caught = [(park[0] + exit[0]) / 2, (park[1] + exit[1]) / 2];
                var backNode = map.Of("home").Concat(map.Of("hideout")).Where(n => Dist(n.At, caught) + 200 < Dist(start.At, caught) && Dist(n.At, caught) > 400).OrderBy(n => Dist(n.At, caught)).FirstOrDefault();
                Inside(backNode);
                Friend(backNode);
                o.Finish = new ZoneSpec { Place = Place("home", backNode?.At ?? start.At), Radius = 100 };
                o.FinishClear = 300;
                o.FinishAfter = re.Delay + 20;
                string backWords = backNode is null ? "where you started" : backNode.Called;
                var near = map.Nodes.OrderBy(n => Dist(n.At, exit)).FirstOrDefault();
                string toward = near is not null && Dist(near.At, exit) < 600 ? $"toward {near.Called}" : $"{Compass(park, exit)}";
                spec.Radio.Start.Add(Found);
                spec.Radio.Steps.AddRange([[Go(park)], [GetThem], [Rendezvous]]);
                spec.Radio.Wave.Add(OhNo);
                spec.Radio.Trap.Add(RightOn);
                spec.Radio.Reinforce.Add(Bandits);
                spec.Radio.Win.Add(LastOfThem);
                string what = LeadCalled[li];
                spec.Text.Objectives.AddRange([$"{job}: {gang}'s {what} is pulling out. Stop it before it gets away.", "(hidden)Take out its escorts.", $"(hidden)Get back to {backWords}, with nobody on your tail."]);
                spec.Text.Notes.AddRange([$"The {what}: parked {Distance(start.At, park)} {Compass(start.At, park)} of the start; it runs {toward}, {Distance(park, exit)} off."]);
                spec.Text.Deaths.AddRange([$"You were killed before you could stop the {what}.", $"{gang}'s escorts got you.", "You were killed on the way back."]);
                spec.Text.Failures.AddRange([Deaths0, left, "They got away. Too late now."]);
                break;
            }
            case "escort":
            {
                // the escorted car waits at the route's start; the player starts 35 m behind it, facing the way it will go
                var route = r.Quiet(map, map.Routes.Where(rt => rt.Escort && rt.Points.Count >= 3).ToList(), rt => rt.Points[^1]);
                // (a long route is cut where the drive to the goal would pass about four minutes at the escorted car's pace)
                route = Shortened(route, 12.0 * 240 / 1.2);
                double[] p0 = route.Points[0], p1 = route.Points[1];
                double hd = Bearing(p0, p1), hr = hd * Math.PI / 180;
                int li = r.Next(Leads.Length);
                string what = LeadCalled[li];
                spec.Start = new PoseSpec { At = [p0[0] - 35 * DetMath.Sin(hr), p0[1] - 35 * DetMath.Cos(hr)], Heading = (hd + 360) % 360 };
                start = new StartNode { Name = "escort", At = spec.Start.At, Heading = spec.Start.Heading, Called = $"behind the {what}" };
                var van = $"car{++carNo}";
                spec.Cars[van] = new CarSpec { Vcf = Leads[li], At = p0, Heading = (hd + 360) % 360 };
                var mid = route.Points[route.Points.Count / 2];
                var goal = route.Points[^1];
                var ec = new EscortSpec
                {
                    Car = van, Route = Place("route", [.. route.Points]), Mid = new ZoneSpec { Place = Place("mid", mid), Radius = 60 },
                    Goal = new ZoneSpec { Place = Place("goal", goal), Radius = 60 }, Speed = 13 + heat, Wait = 220, Resume = 110,
                };
                o.Escort = ec;
                // a wingman alongside the player on the heavier jobs (the player's side, firing at the nearest enemy)
                if (heat >= 1)
                {
                    var wing = $"car{++carNo}";
                    spec.Cars[wing] = new CarSpec { Vcf = r.Pick(Heat[1]), At = [spec.Start.At[0] + 8 * DetMath.Cos(hr), spec.Start.At[1] - 8 * DetMath.Sin(hr)], Heading = spec.Start.Heading };
                    ec.Wingmen.Add(wing);
                }
                // an ambush parked on the way, a third of the way to the goal (the first contact in a minute or two), woken as they come
                var lie = Along(route.Points, RouteLength(route.Points) / 3);
                void Ambusher()
                {
                    double a = (r.Next(8) * 45) * Math.PI / 180, d = 8 + 6 * o.Sleepers.Count;
                    var g = NewCar(Heat[heat], [lie[0] + d * DetMath.Sin(a), lie[1] + d * DetMath.Cos(a)], r.Next(360));
                    o.Sleepers.Add(new SleeperSpec { Car = g, Wake = 260 });
                }
                Ambusher();
                grow.Add(("ambush", () => o.Sleepers.Count, Ambusher));
                // the twist: as the second leg opens, a crew comes in for the escorted car itself
                var cut = NearestEntry(map, lie, 300, 1500, r);
                var re = new ReinforceSpec { Entry = Entry(cut), AtStep = 2, Delay = 10, Speed = 25, Target = "escort" };
                re.Cars.Add(NewCar(Heat[heat]));
                o.Reinforce.Add(re);
                grow.Add(("hit squad", () => re.Cars.Count, () => re.Cars.Add(NewCar(Heat[heat]))));
                o.Finish = new ZoneSpec { Place = Place("home", goal), Radius = 100 };
                o.FinishClear = 300;
                o.FinishAfter = 10;
                spec.Radio.Start.Add(Found);
                spec.Radio.Steps.AddRange([[GoThere], [Problem], [GetThem]]);
                spec.Radio.Trap.Add(GotIt);
                spec.Radio.Reinforce.Add(Company);
                spec.Radio.Win.Add(Clean);
                string midWords = $"{Distance(start.At, mid)} {Compass(start.At, mid)} of the start";
                spec.Text.Objectives.AddRange([$"{job}: see the {what} halfway, {midWords}. {gang} wants it.", $"(hidden)Get the {what} to {route.Called}.", "(hidden)See it in: nobody on its tail."]);
                spec.Text.Notes.AddRange([$"The {what}: just ahead of you. It drives only while you stay with it.", $"Goal: {Where(goal, route.Called)}."]);
                spec.Text.Deaths.AddRange([$"You were killed before the {what} got halfway.", $"You were killed with the {what} still on the road.", $"{Crew()} got you at the end of the road."]);
                spec.Text.Failures.AddRange([Deaths0, left, $"The {what} is wrecked. The job's off."]);
                break;
            }
            case "raid":
            {
                var targets = WithStart(RaidTargets(map), x => x.At, MinFirst, MaxFirst, x => x.Node);
                var tg = r.Quiet(map, targets, x => x.At);
                UseStart(NearStart(tg.At, MinFirst, MaxFirst, not: tg.Node), tg.At);
                string what;
                if (tg.Cluster is { } cl)
                {
                    for (int i = 0; i < Math.Min(6, cl.Objects.Count); i++) { spec.Bind[$"t{i}"] = cl.Objects[i]; o.Targets.Add($"t{i}"); }
                    what = cl.Called.StartsWith("the ", StringComparison.Ordinal) ? cl.Called[4..] : cl.Called;   // "Cloaker's pumps in the middle of the mesa"
                    o.Arrive = new ZoneSpec { Place = Place("target", cl.At), Radius = cl.Radius };
                }
                else
                {
                    // a convoy parked at the yard: three trucks in a row on the road there, never started
                    var yard = tg.Node!;
                    int li = r.Next(Leads.Length);
                    double yh = r.Next(4) * 90, yr = yh * Math.PI / 180;
                    for (int i = 0; i < 3; i++)
                    {
                        var t = $"car{++carNo}";
                        spec.Cars[t] = new CarSpec { Vcf = Leads[(li + i) % Leads.Length], At = [yard.At[0] + (i - 1) * 14 * DetMath.Sin(yr), yard.At[1] + (i - 1) * 14 * DetMath.Cos(yr)], Heading = yh };
                        o.Targets.Add(t);
                    }
                    what = $"trucks parked at {yard.Called}";
                    o.Arrive = new ZoneSpec { Place = Place("target", yard.At), Radius = 150 };
                }
                // guards parked by the target on the road in, woken as he comes
                var gate = NearestEntry(map, tg.At, 0, 700, r);
                double[] gp = gate.At.Count > 0 ? gate.At[0] : tg.At;
                void Guard()
                {
                    double a = (r.Next(8) * 45) * Math.PI / 180, d = 6 + 5 * o.Sleepers.Count;
                    var g = NewCar(Heat[heat], [gp[0] + d * DetMath.Sin(a), gp[1] + d * DetMath.Cos(a)], r.Next(360));
                    o.Sleepers.Add(new SleeperSpec { Car = g, Wake = 300 });
                }
                Guard();
                grow.Add(("guards", () => o.Sleepers.Count, Guard));
                // out: a home or hideout away from the target, else the start
                var outs = map.Of("home").Concat(map.Of("hideout")).Where(n => Far(n.At, tg.At, MinNext, MaxNext) && Dist(n.At, start.At) > 150).Distinct().ToList();
                var outNode = outs.Count > 0 ? r.Weighted(map, outs, n => n.At, n => n.IsSpecial ? InsideWeight : 1) : null;
                Inside(outNode);
                Friend(outNode);
                double[] escAt = outNode?.At ?? start.At;
                string escCalled = outNode is null ? $"where you started ({start.Called})" : Where(escAt, outNode.Called);
                int secs = (int)Math.Round(Dist(tg.At, escAt) * 1.3 / 15 + 45);
                o.Escape = new ZoneSpec { Place = Place("escape", escAt), Radius = 100, Seconds = secs };
                // the alarm: a crew comes in as the hit starts; then they close the roads on the way out
                var alarm = NearestEntry(map, tg.At, 400, 1500, r);
                var re = new ReinforceSpec { Entry = Entry(alarm), AtStep = 2, Delay = 20, Speed = 25 };
                re.Cars.Add(NewCar(Heat[heat]));
                o.Reinforce.Add(re);
                grow.Add(("alarm", () => re.Cars.Count, () => re.Cars.Add(NewCar(Heat[heat]))));
                var close = NearestEntry(map, escAt, 300, 1500, r);
                var re2 = new ReinforceSpec { Entry = Entry(close), AtStep = 3, Delay = Math.Max(10, secs / 3), Speed = 28 };
                Roadblock(close);
                re2.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)]));
                o.Reinforce.Add(re2);
                grow.Add(("roadblock", () => re2.Cars.Count, () => re2.Cars.Add(NewCar(Heat[Math.Min(2, heat + 1)]))));
                spec.Radio.Start.Add(Found);
                spec.Radio.Steps.AddRange([[Go(tg.At)], [GetThem], [GotAway]]);
                spec.Radio.Trap.Add(RightOn);
                spec.Radio.Reinforce.Add(Bandits);
                spec.Radio.Win.Add(TookLong);
                spec.Text.Objectives.AddRange([$"{job}: hit {gang}'s {what}.", $"(hidden)Take out every last one of them.", $"(hidden)Get out to {(outNode?.Called ?? "where you started")} before they close the roads."]);
                spec.Text.Notes.AddRange([$"Target: {Where(tg.At, tg.Called)}.", $"Way out: {escCalled}; {secs} s once the last one falls."]);
                spec.Text.Deaths.AddRange([$"You were killed on the way to {gang}'s {what}.", $"You were killed in the middle of the hit.", "You were killed on the way out."]);
                spec.Text.Failures.AddRange([Deaths0, left, "Too slow. They closed the roads."]);
                break;
            }
            case "defend":
            {
                var cl = r.Pick(map.Clusters.Where(x => x.Serves("defend")).ToList());
                UseStart(NearStart(cl.At, MinFirst, MaxFirst), cl.At);
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
        double budget = RemixChallenge.Spanner * ask.Challenge;
        for (int guard = 0; guard < 40 && grow.Count > 0; guard++)
        {
            if (Score(spec) >= budget * 0.95 || RemixRoles.Hostiles(spec).Count >= MaxCars) break;
            var g = grow.OrderBy(x => x.Count()).ThenBy(x => grow.IndexOf(x)).First();
            g.Add();
        }
        spec.Text.Notes.Add($"{job}: {map.Base.ToUpperInvariant()}, challenge {Score(spec) / RemixChallenge.Spanner:0.0} (Back to Spanner 1.0).");
        spec.Landmarks.AddRange(Landmarks(map, spec));
        foreach (var clip in spec.Radio.All) if (Words.TryGetValue(clip, out var w)) spec.Radio.Words[clip] = w;
        return spec;
    }

    /// <summary>
    /// One or two landmarks for a job's map (docs/remix-missions.md "The map"): the map's named clusters (a town's street, an oil
    /// field) and places of note (a fort, a barn, a bridge), the two nearest the job's places that are not one of its marks.
    /// </summary>
    private static List<LandmarkSpec> Landmarks(RemixNodes map, RemixSpec spec)
    {
        var used = spec.Places.Values.Where(p => p.Count == 1).Select(p => p[0]).Append(spec.Start?.At ?? [0, 0]).ToList();
        double cx = used.Average(p => p[0]), cz = used.Average(p => p[1]);
        // the clusters and places of note first, then any named node (a far corner's road end names little)
        var cands = map.Clusters.Where(c => c.Called.Length > 0).Select(c => (Name: c.Called, c.At, Rank: 0))
            .Concat(map.Nodes.Where(n => n.IsSpecial && n.Called.Length > 0).Select(n => (Name: n.Called, n.At, Rank: 0)))
            .Concat(map.Nodes.Where(n => !n.IsSpecial && n.Called.Length > 0).Select(n => (Name: n.Called, n.At, Rank: 1)))
            .Where(c => used.All(u => Dist(u, c.At) > 250)).GroupBy(c => c.Name).Select(g => g.First())
            .OrderBy(c => c.Rank).ThenBy(c => Dist(c.At, [cx, cz])).Take(2);
        return [.. cands.Select(c => new LandmarkSpec { Name = Upper(c.Name.StartsWith("the ", StringComparison.Ordinal) ? c.Name[4..] : c.Name), At = c.At })];
    }

    private static string Upper(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

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

    /// <summary>What a raid can hit on a map: the clusters that serve one, and the yards where a convoy can be parked.</summary>
    private sealed record RaidTarget(double[] At, string Called, ClusterNode? Cluster, PointNode? Node);
    private static List<RaidTarget> RaidTargets(RemixNodes t) =>
        [.. t.Clusters.Where(c => c.Serves("raid")).Select(c => new RaidTarget(c.At, c.Called, c, null)), .. t.Of("yard").Select(n => new RaidTarget(n.At, n.Called, null, n))];

    /// <summary>Whether a table has what a template needs (the start is then placed near the job's first place).</summary>
    public static bool Allows(RemixNodes t, string template) => t.Lot.Length == 2 && Spots(t).Count > 1 && template switch
    {
        "deliver" => t.Of("pickup").Any() && t.Entries.Any(e => e.At.Count > 0),
        "tail" => t.Of("hideout").Any(h => HasSafe(t, h)) && t.Entries.Any(e => e.At.Count > 0),
        "intercept" => t.Routes.Count > 0,
        "defend" => t.Clusters.Any(c => c.Serves("defend")),
        "escort" => t.Routes.Any(rt => rt.Escort && rt.Points.Count >= 3),
        "raid" => RaidTargets(t).Count > 0 && t.Entries.Any(e => e.At.Count > 0),
        _ => false,
    };

    /// <summary>A hideout with a safe house (another hideout or a home) a short drive from it.</summary>
    private static bool HasSafe(RemixNodes t, PointNode h) => t.Of("hideout").Concat(t.Of("home")).Any(n => n != h && Far(n.At, h.At, 500, 3000));

    // ---- pacing (docs/remix-missions.md "Pacing"): metres in a straight line; the roads add about a third ----

    /// <summary>The start from the job's first place: about 40-100 s of driving.</summary>
    private const double MinFirst = 350, MaxFirst = 1300;
    /// <summary>From one event to the next (a pickup to home, a hideout to the safe house, a target to the way out).</summary>
    private const double MinNext = 600, MaxNext = 1500;
    /// <summary>How much more likely a place inside or under a structure is than a plain road point.</summary>
    public const int InsideWeight = 6;

    /// <summary>A place a job may start the player at: a table's start (on a road, its heading along it) or a node (facing the job's first place, or out of its building).</summary>
    private sealed record Spot(string Name, double[] At, double Heading, bool Road, bool Fixed, string Called, PointNode? Node);

    private static List<Spot> Spots(RemixNodes t) =>
    [
        .. t.Starts.Select(s => new Spot(s.Name, s.At, s.Heading, true, false, s.Called, null)),
        .. t.Nodes.Where(n => n.Kinds.Any(k => k is "start" or "pickup" or "home" or "hideout") && (!n.IsInside || n.Kinds.Contains("start")))
            .Select(n => new Spot(n.Name, n.At, n.Heading ?? 0, false, n.Heading is not null && n.IsInside, n.Called, n)),
    ];

    /// <summary>A route cut short at a length (its points up to there, the last one on the line), for an escort that would drive too long.</summary>
    private static RouteNode Shortened(RouteNode rt, double max)
    {
        if (RouteLength(rt.Points) <= max) return rt;
        var pts = new List<double[]> { rt.Points[0] };
        double d = 0;
        for (int i = 1; i < rt.Points.Count; i++)
        {
            double seg = Dist(rt.Points[i - 1], rt.Points[i]);
            if (d + seg >= max) { pts.Add(Along([rt.Points[i - 1], rt.Points[i]], max - d)); break; }
            d += seg;
            pts.Add(rt.Points[i]);
        }
        return new RouteNode { Name = rt.Name, Points = pts, Escort = rt.Escort, Called = $"the road {(rt.Called.Length > 0 ? "toward " + rt.Called : "ahead")}", Heading = rt.Heading };
    }

    private static double RouteLength(List<double[]> p)
    {
        double d = 0;
        for (int i = 1; i < p.Count; i++) d += Dist(p[i - 1], p[i]);
        return d;
    }

    /// <summary>The point a distance along a polyline.</summary>
    private static double[] Along(List<double[]> p, double at)
    {
        double d = 0;
        for (int i = 1; i < p.Count; i++)
        {
            double seg = Dist(p[i - 1], p[i]);
            if (d + seg >= at && seg > 0) { double f = (at - d) / seg; return [p[i - 1][0] + f * (p[i][0] - p[i - 1][0]), p[i - 1][1] + f * (p[i][1] - p[i - 1][1])]; }
            d += seg;
        }
        return p[^1];
    }

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
    public static double Bearing(double[] from, double[] to) => DetMath.Atan2(to[0] - from[0], to[1] - from[1]) * 180 / Math.PI;

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
        public T Quiet<T>(RemixNodes map, IReadOnlyList<T> l, Func<T, double[]> at) => Weighted(map, l, at, _ => 1);

        /// <summary>The quiet-corner pick with each item's weight multiplied by a factor of its own (a place inside a structure: <see cref="InsideWeight"/>).</summary>
        public T Weighted<T>(RemixNodes map, IReadOnlyList<T> l, Func<T, double[]> at, Func<T, int> factor)
        {
            if (l.Count == 0) throw new RemixError("the director drew from an empty list");
            var w = l.Select(x => factor(x) * (int)Math.Round(100 * (1 + 8 * DetMath.Pow(Math.Min(1, map.Quiet(at(x)) / 1200), (double)(2))))).ToList();
            int k = Next(w.Sum());
            for (int i = 0; i < l.Count; i++) { if (k < w[i]) return l[i]; k -= w[i]; }
            return l[^1];
        }
    }
}
