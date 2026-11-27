using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// A roguetrip (docs/remix-missions.md "Roguetrip"): a run of four generated jobs across neighbouring maps of the region, the
/// challenge rising 0.8, 1.0, 1.2, 1.4. Before each job the player picks one of two or three offers (a map next to the one he is
/// on, a template, its challenge and a line naming the gang); the car's damage carries from job to job, and after each win the
/// trip's field salvage and garage (the job's wrecks, the van, the repair order; armour patched from scrap, the roguetrip's
/// own). The player's death ends the run; a job failed otherwise (the target lost, the convoy through, too slow, the escort
/// wrecked, walked away from) is a BUST: no salvage, and the next job one step harder. Everything a run offers comes
/// from its number and the choices made (a SplitMix64 stream of its own, outside the simulation): the same number and the same
/// picks give the same offers and the same jobs. Kept in the user folder between jobs (run.yaml), so quitting the game keeps it.
/// </summary>
public sealed class RemixRun
{
    /// <summary>The jobs a run has, and the challenge of each in turn (Back to Spanner's 1.0).</summary>
    public const int Jobs = 4;
    /// <summary>The challenge of each job in turn; a bust moves the run one step further along (the gangs are warned).</summary>
    public static readonly double[] Challenges = [0.8, 1.0, 1.2, 1.4, 1.6, 1.8];
    /// <summary>The stem a run's job is built under (one at a time).</summary>
    public const string Stem = "xrun";
    /// <summary>The run's file in the generated missions' folder.</summary>
    public const string FileName = "run.yaml";

    public int Number { get; set; }
    /// <summary>The builder's version the run was made by (RemixBuilder.Version): a run of another version rolls other jobs.</summary>
    public int Builder { get; set; } = RemixBuilder.Version;
    /// <summary>
    /// choose (offers on the screen), playing (a job launched), debrief (a job's end on its debrief page), salvage (a won job's
    /// field salvage and garage), over (the player killed), done (four jobs played).
    /// </summary>
    public string Phase { get; set; } = "choose";
    /// <summary>Jobs busted (failed with the player alive).</summary>
    public int Busts { get; set; }
    /// <summary>The map the run is on (the last job's), empty before the first.</summary>
    public string Map { get; set; } = "";
    public int Won { get; set; }
    public int Kills { get; set; }
    public int Bonus { get; set; }
    /// <summary>The player's car: its file, the driver's name and the make and model the forms show (and their list index).</summary>
    public string Car { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Model { get; set; } = "";
    public int ModelIndex { get; set; } = -1;
    /// <summary>The car's damage and rounds carried into the next job (null: as built).</summary>
    public RunCar? State { get; set; }
    /// <summary>The car's state as the job being played began (the developer's RETRY JOB puts it back).</summary>
    public RunCar? JobStart { get; set; }
    /// <summary>The trip's inventory of the run (the car's parts, the van's stock, the repair order; TripInventory.Save), or null before the first salvage.</summary>
    public string? Inventory { get; set; }
    /// <summary>The inventory as the job being played began (for RETRY JOB).</summary>
    public string? JobStartInventory { get; set; }
    /// <summary>The jobs played, in order.</summary>
    public List<RunLeg> Legs { get; set; } = [];
    /// <summary>Each job's reproduction line as it was launched (LaunchLine), oldest first.</summary>
    public List<string> Log { get; set; } = [];
    /// <summary>The job being played (Phase playing), or the one just played (reward, over).</summary>
    public RunOffer? Current { get; set; }

    [YamlIgnore] public bool Over => Phase is "over" or "done";
    /// <summary>The challenge of the next job: one step a job played, and one more for each bust.</summary>
    [YamlIgnore] public double NextChallenge => Challenges[Math.Clamp(Legs.Count + Busts, 0, Challenges.Length - 1)];
    /// <summary>The job being played or last played, from 1.</summary>
    [YamlIgnore] public int JobIndex => Phase == "playing" || Current is null ? Legs.Count + 1 : Legs.Count;

    public static RemixRun Start(int number, string car, string driver, string model, int modelIndex) =>
        new() { Number = number, Car = car, Driver = driver, Model = model, ModelIndex = modelIndex };

    /// <summary>
    /// The offers for the next job: two or three (map, template) pairs on the maps next to the one the run is on (the first job: on
    /// the run's home map, picked by its number, and its neighbours), each a different map where it can be, a different template
    /// where it can be, with the job's seed and a line of flavour from the job itself. A pure function of the run's number and its
    /// state (the maps visited, the jobs won).
    /// </summary>
    public List<RunOffer> Offers(IReadOnlyList<RemixNodes> tables, bool nitro = false)
    {
        var usable = tables.Where(t => (nitro || !t.Nitro) && RemixDirector.Templates.Any(k => RemixDirector.Allows(t, k))).OrderBy(t => t.Base, StringComparer.Ordinal).ToList();
        if (usable.Count == 0) return [];
        // (the jobs played: a run without a bust has as many as it has wins, as before busts were)
        var rng = new Mix(unchecked(Number * 0x2545F491 + Legs.Count * 0x9E3779B9 + Hash(Map)));
        var visited = Legs.Select(l => l.Map).ToHashSet(StringComparer.OrdinalIgnoreCase);
        RemixNodes? here = usable.FirstOrDefault(t => t.Base.Equals(Map, StringComparison.OrdinalIgnoreCase));
        if (here is null) here = usable[rng.Next(usable.Count)];
        var near = here.Neighbours.Select(n => usable.FirstOrDefault(t => t.Base.Equals(n, StringComparison.OrdinalIgnoreCase))).OfType<RemixNodes>().ToList();
        // the first job may be on the home map itself; later ones go on to a neighbour, one not visited when there is one
        var maps = Map.Length == 0 ? [here, .. near] : near.Where(t => !visited.Contains(t.Base)).ToList();
        if (maps.Count == 0) maps = near.Count > 0 ? near : [here];
        maps = [.. maps.OrderBy(_ => rng.Next(1 << 30))];
        int want = Math.Min(3, Math.Max(2, maps.Count));
        var o = new List<RunOffer>();
        var usedTemplates = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; o.Count < want && i < want * 3; i++)
        {
            var t = maps[i % maps.Count];
            var allowed = RemixDirector.Templates.Where(k => RemixDirector.Allows(t, k)).ToList();
            var fresh = allowed.Where(k => !usedTemplates.Contains(k)).ToList();
            var pick = (fresh.Count > 0 ? fresh : allowed)[rng.Next((fresh.Count > 0 ? fresh : allowed).Count)];
            if (o.Any(x => x.Map == t.Base && x.Template == pick)) continue;
            usedTemplates.Add(pick);
            int seed = 1 + rng.Next(999_999_998);
            var offer = new RunOffer { Map = t.Base, Name = t.Name.Length > 0 ? t.Name : t.Base.ToUpperInvariant(), Template = pick, Seed = seed, Challenge = NextChallenge };
            // the flavour: the job's own first objective, without its label ("Roguetrip 12, job 2: ")
            try { var first = Spec(offer, tables).Text.Objectives[0]; offer.Flavour = first.IndexOf(": ", StringComparison.Ordinal) is var k and > 0 ? first[(k + 2)..] : first; offer.Flavour = char.ToUpperInvariant(offer.Flavour[0]) + offer.Flavour[1..]; }
            catch (RemixError) { continue; }
            o.Add(offer);
        }
        return o;
    }

    /// <summary>A job's description: the director's roll for the offer's seed on its map and template at its challenge.</summary>
    public RemixSpec Spec(RunOffer offer, IReadOnlyList<RemixNodes>? tables = null) =>
        RemixDirector.Roll(offer.Seed, new RemixDirector.JobAsk
        {
            Tables = tables, Id = Stem, Challenge = offer.Challenge, Map = offer.Map, Template = offer.Template, Nitro = true,
            Label = $"Roguetrip {Number.ToString(CultureInfo.InvariantCulture)}, job {(Legs.Count + 1).ToString(CultureInfo.InvariantCulture)}",
        }) is var s ? Retitle(s) : null!;

    private RemixSpec Retitle(RemixSpec s)
    {
        s.Title = $"ROGUETRIP {Math.Min(Jobs, Legs.Count + 1)}/{Jobs}";
        return s;
    }

    /// <summary>The player took an offer: the job is being played (the car's state kept as it begins, for RETRY JOB).</summary>
    public void Take(RunOffer offer)
    {
        // the pick's index among the offers (before the phase moves on), for the reproduction line
        int pick = Phase == "choose" ? PickOf(offer) : -1;
        while (Picks.Count > Legs.Count) Picks.RemoveAt(Picks.Count - 1);
        Picks.Add(pick);
        Current = offer;
        JobStart = State;
        JobStartInventory = Inventory;
        Phase = "playing";
    }

    /// <summary>How a job ended: won, bust (failed with the player alive: the run goes on), dead (the run over).</summary>
    public enum Outcome { Won, Bust, Dead }

    /// <summary>
    /// How a roguetrip's job ended, from the mission's end (docs/remix-missions.md "Busts"): a win; the player's death (failure line
    /// 1, or a death line: the lines after the failures, one an objective) ends the run; any other failure (leaving the map, the
    /// job's own: the target lost, the convoy through, too slow, the escorted car wrecked) or walking away from the job (Abort)
    /// is a bust. failMessage is the 0-based failure line the mission ended on (-1: none, an abort), failures the description's count.
    /// </summary>
    public static Outcome Classify(bool won, int failMessage, int failures, bool playerDead)
    {
        if (won) return Outcome.Won;
        if (playerDead || failMessage == 0 || (failures > 0 && failMessage >= failures)) return Outcome.Dead;
        return Outcome.Bust;
    }

    /// <summary>
    /// The job is over: won (the car's state carried; its salvage next, or the run done), bust (the car's state carried, no salvage,
    /// the next job a step harder) or dead (the run over). Each goes to its debrief page first; why is the line it ended on.
    /// </summary>
    public void Finish(Outcome outcome, int kills, RunCar? car, string why = "")
    {
        if (Current is not { } c) return;
        Kills += kills;
        Legs.Add(new RunLeg
        {
            Map = c.Map, Name = c.Name, Template = c.Template, Seed = c.Seed, Challenge = c.Challenge, Won = outcome == Outcome.Won, Kills = kills,
            Outcome = outcome.ToString().ToLowerInvariant(), Why = why,
        });
        Map = c.Map;
        if (car is not null && outcome != Outcome.Dead) State = car;
        if (outcome == Outcome.Won) Won++;
        if (outcome == Outcome.Bust) Busts++;
        Phase = "debrief";
    }

    /// <summary>Won or not (a loss as the player's death), straight past the debrief: for tools and old callers.</summary>
    public void Finish(bool won, int kills, RunCar? car)
    {
        Finish(won ? Outcome.Won : Outcome.Dead, kills, car);
        Debriefed();
    }

    /// <summary>The last job's outcome (debrief and after), or null.</summary>
    [YamlIgnore] public Outcome? Last => Legs.Count == 0 ? null : Legs[^1].Outcome switch { "won" => Outcome.Won, "bust" => Outcome.Bust, "dead" => Outcome.Dead, _ => Legs[^1].Won ? Outcome.Won : Outcome.Dead };

    /// <summary>
    /// Past the debrief page: a won job to its salvage (or the run done after the last), a bust to the next choice (or the run
    /// done), a death to the run's end.
    /// </summary>
    public void Debriefed()
    {
        if (Phase != "debrief") return;
        Phase = Last switch
        {
            Outcome.Dead => "over",
            Outcome.Won => Legs.Count >= Jobs ? "done" : "salvage",
            _ => Legs.Count >= Jobs ? "done" : "choose",
        };
    }

    /// <summary>The salvage and garage done: the inventory kept, the car's armour and chassis as patched, the next choice.</summary>
    public void Salvaged(string? inventory, RunCar? car)
    {
        if (Phase != "salvage") return;
        Inventory = inventory;
        if (car is not null) State = car;
        Phase = "choose";
    }

    /// <summary>
    /// The developer's RETRY JOB: the last job undone (its leg, kills, win or bust) and the car as it began, the same offer to play
    /// again. Null when there is none to retry.
    /// </summary>
    public RunOffer? Retry()
    {
        if (Legs.Count == 0 || Current is null || Phase is "choose" or "playing") return null;
        var l = Legs[^1];
        Legs.RemoveAt(Legs.Count - 1);
        Kills -= l.Kills;
        if (l.Outcome == "won" || (l.Outcome.Length == 0 && l.Won)) Won--;
        if (l.Outcome == "bust") Busts--;
        Map = Legs.Count > 0 ? Legs[^1].Map : "";
        State = JobStart;
        Inventory = JobStartInventory;
        Phase = "playing";
        return Current;
    }

    /// <summary>A won job's pick (round four's, kept for its run files): repair, rearm or bonus.</summary>
    public void Reward(string pick)
    {
        if (Phase != "reward") return;
        switch (pick)
        {
            case "repair": State = State?.Repaired(); break;
            case "rearm": State = State?.Rearmed(); break;
            case "bonus": Bonus++; break;
            default: throw new RemixError($"reward '{pick}': repair, rearm or bonus");
        }
        Phase = "choose";
    }

    /// <summary>A run whose job was never finished (the game closed during it) goes back to its choice: the same offers again.</summary>
    public void Resume()
    {
        if (Phase == "playing") { Phase = "choose"; Current = null; }
    }

    public string ToYaml() =>
        "# a roguetrip in progress (docs/remix-missions.md \"Roguetrip\"): the run's number, the jobs played, the car's damage\n"
        + new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).DisableAliases().Build().Serialize(this);

    public static RemixRun Parse(string yaml)
    {
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
        try { return d.Deserialize<RemixRun>(yaml) ?? throw new RemixError("an empty run file"); }
        catch (YamlDotNet.Core.YamlException ex) { throw new RemixError($"run file, line {ex.Start.Line}: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    /// <summary>The run kept in a folder, or null (none, or unreadable).</summary>
    public static RemixRun? Load(string dir)
    {
        var p = Path.Combine(dir, FileName);
        try { return File.Exists(p) ? Parse(File.ReadAllText(p)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RemixError) { return null; }
    }

    /// <summary>Writes the run (beside it first, then moved over the old one: a crash leaves the old or the new, never half).</summary>
    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, FileName);
        File.WriteAllText(p + ".new", ToYaml());
        File.Move(p + ".new", p, overwrite: true);
    }

    /// <summary>
    /// The stem a roguetrip job's salvage pool is levelled by: the trip mission whose map it is (T12's own writes no salvage and
    /// T04's is a dream with none, so those take the trip mission before them).
    /// </summary>
    public static string SalvageStem(string map) => map.ToLowerInvariant() switch { "t04" => "t03", "t12" => "t11", var m => m };

    /// <summary>
    /// The armour and chassis the garage patches from scrap (the roguetrip's own: docs/deviations.md): every salvage item left in
    /// the field is broken up for plate, a tenth of the car's full armour each, armour first, then the chassis with what is left.
    /// </summary>
    public static RunCar Patch(RunCar car, int[] armourMax, int[] chassisMax, int scrapped)
    {
        var c = new RunCar
        {
            Armour = [.. car.Armour], Chassis = [.. car.Chassis], Engine = car.Engine, Brake = car.Brake, Suspension = car.Suspension, Wheels = [.. car.Wheels],
            Weapons = [.. car.Weapons.Select(w => w.ToArray())], ArmourLeft = car.ArmourLeft, ChassisLeft = car.ChassisLeft, AmmoLeft = car.AmmoLeft,
            ArmourMax = [.. armourMax], ChassisMax = [.. chassisMax],
        };
        int plate = (int)Math.Round(armourMax.Sum() * 0.1 * scrapped);
        for (int pass = 0; pass < 2 && plate > 0; pass++)
        {
            var have = pass == 0 ? c.Armour : c.Chassis;
            var max = pass == 0 ? armourMax : chassisMax;
            // the weakest side first, a point at a time (the same plate the same way on every machine)
            while (plate > 0)
            {
                int k = -1, worst = int.MaxValue;
                for (int z = 0; z < 4 && z < have.Length && z < max.Length; z++)
                {
                    int now = have[z] < 0 ? max[z] : have[z];
                    if (now < max[z] && now * 1000L / Math.Max(1, max[z]) < worst) { worst = (int)(now * 1000L / Math.Max(1, max[z])); k = z; }
                }
                if (k < 0) break;
                int cur = have[k] < 0 ? max[k] : have[k];
                int step = Math.Min(plate, Math.Max(1, max[k] / 20));
                step = Math.Min(step, max[k] - cur);
                have[k] = cur + step;
                plate -= step;
            }
        }
        int Pct(int[] h, int[] m) => m.Sum() <= 0 ? 100 : (int)Math.Round(100.0 * h.Select((x, i) => x < 0 && i < m.Length ? m[i] : x).Sum() / m.Sum());
        c.ArmourLeft = Pct(c.Armour, armourMax);
        c.ChassisLeft = Pct(c.Chassis, chassisMax);
        return c;
    }

    /// <summary>
    /// The line a roguetrip job's launch writes to the game log and the run file (docs/remix-missions.md "Reproducing a job"):
    /// enough to roll that exact job again headless (i76 remix roll --run N --job K --picks ...).
    /// </summary>
    public static string LaunchLine(RemixRun run, RunOffer offer, RemixSpec spec, string car, string commit) =>
        string.Create(CultureInfo.InvariantCulture,
            $"remix: RUN {run.Number:000000} JOB {run.Legs.Count + 1}/{Jobs} #{offer.Seed} builder v{RemixBuilder.Version} map {offer.Map} ({offer.Name}) template {offer.Template} challenge {offer.Challenge:0.0} car {car} commit {commit} (i76 remix roll --run {run.Number} --job {run.Legs.Count + 1} --picks {run.PicksLine(offer)})");

    /// <summary>
    /// The picks that lead to a job, for i76 remix roll --run: the offer taken at each job, a b after one that was busted (a bust
    /// moves the challenge on), the offer to be played last: "0,2b,1".
    /// </summary>
    public string PicksLine(RunOffer offer)
    {
        var o = new List<string>();
        for (int i = 0; i < Legs.Count; i++) o.Add((i < Picks.Count ? Picks[i] : 0).ToString(CultureInfo.InvariantCulture) + (Legs[i].Outcome == "bust" ? "b" : ""));
        int last = Picks.Count > Legs.Count ? Picks[Legs.Count] : PickOf(offer);
        o.Add(last.ToString(CultureInfo.InvariantCulture));
        return string.Join(",", o);
    }

    /// <summary>
    /// A run walked to one of its jobs (i76 remix roll --run): the picks as PicksLine writes them, each job before the last won (or
    /// busted, with a b), the last one's offer and description. Throws RemixError for a pick that is not offered.
    /// </summary>
    public static (RemixRun Run, RunOffer Offer, RemixSpec Spec) Walk(int number, string picks, IReadOnlyList<RemixNodes>? tables = null, bool nitro = false)
    {
        tables ??= RemixNodes.Builtin();
        var run = Start(number, "vppirna1.vcf", "", "", 0);
        var steps = picks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (steps.Length == 0) throw new RemixError("--picks: at least the offer to play (0, 1 or 2)");
        for (int i = 0; i < steps.Length; i++)
        {
            bool bust = steps[i].EndsWith('b');
            int k = int.Parse(bust ? steps[i][..^1] : steps[i], CultureInfo.InvariantCulture);
            var offers = run.Offers(tables, nitro);
            if (k < 0 || k >= offers.Count) throw new RemixError($"job {i + 1}: offer {k} of {offers.Count}");
            if (i == steps.Length - 1) { run.Take(offers[k]); return (run, offers[k], run.Spec(offers[k], tables)); }
            run.Take(offers[k]);
            run.Finish(bust ? Outcome.Bust : Outcome.Won, 0, null);
            run.Debriefed();
            if (run.Phase == "salvage") run.Salvaged(null, null);
        }
        throw new RemixError("unreachable");
    }

    /// <summary>The two lines the pause pad shows for a generated job: "RUN 004242 JOB 2/4 #637742" and "(t07 the farms on 380, deliver) v5".</summary>
    public static (string Top, string Bottom) Label(RemixRun? run, RunOffer offer, RemixSpec spec) =>
        (string.Create(CultureInfo.InvariantCulture, $"{(run is null ? "" : $"RUN {run.Number:000000} JOB {Math.Min(Jobs, run.Legs.Count + 1)}/{Jobs} ")}#{offer.Seed}"),
         string.Create(CultureInfo.InvariantCulture, $"({offer.Map.ToLowerInvariant()} {offer.Name}, {JobWord(offer.Template).ToLowerInvariant()}) v{RemixBuilder.Version}"));

    /// <summary>The offer index taken at each job so far (for the reproduction line's --picks).</summary>
    public List<int> Picks { get; set; } = [];

    /// <summary>The index of an offer among the run's offers now (-1 when it is not one of them).</summary>
    public int PickOf(RunOffer offer, IReadOnlyList<RemixNodes>? tables = null)
    {
        if (Phase != "choose") return Picks.Count > Legs.Count ? Picks[Legs.Count] : -1;
        var o = Offers(tables ?? RemixNodes.Builtin(), nitro: false);
        return o.FindIndex(x => x.Seed == offer.Seed && x.Map == offer.Map && x.Template == offer.Template);
    }

    /// <summary>What a template is called on the choice screen.</summary>
    public static string JobWord(string template) => template switch
    {
        "deliver" => "DELIVERY", "tail" => "SHAKE THE TAIL", "intercept" => "CONVOY HIT", "ambush" => "AMBUSH", "defend" => "DEFENCE",
        "escort" => "ESCORT", "raid" => "RAID", _ => template.ToUpperInvariant(),
    };

    private static int Hash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char c in s.ToLowerInvariant()) h = h * 31 + c;
            return h;
        }
    }

    /// <summary>SplitMix64, the run's own stream (as the director's): the same on every machine.</summary>
    private sealed class Mix(long seed)
    {
        private ulong _s = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL);

        public int Next(int n)
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % (ulong)Math.Max(1, n));
        }
    }
}

/// <summary>One offer of a run's choice: the map (its base and its name), the template, the job's seed, its challenge and a line of flavour.</summary>
public sealed class RunOffer
{
    public string Map { get; set; } = "";
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
    public int Seed { get; set; }
    public double Challenge { get; set; }
    public string Flavour { get; set; } = "";
}

/// <summary>A job played on a run, for the summary.</summary>
public sealed class RunLeg
{
    public string Map { get; set; } = "";
    public string Name { get; set; } = "";
    public string Template { get; set; } = "";
    public int Seed { get; set; }
    public double Challenge { get; set; }
    public bool Won { get; set; }
    public int Kills { get; set; }
    /// <summary>won, bust or dead (empty in a round-four run file: Won says).</summary>
    public string Outcome { get; set; } = "";
    /// <summary>The line the job ended on (a failure's, or the debrief's own words).</summary>
    public string Why { get; set; } = "";
}

/// <summary>
/// The car's damage carried between a run's jobs, in the car's own units: each armour and chassis side, the engine, brakes,
/// suspension and the six wheels, and each gun's hit points and rounds by hardpoint (-1: as built). Unlike the trip's carried
/// state (the original's: no armour, full guns from the shell) a run carries all of it: the run is the port's (docs/deviations.md).
/// </summary>
public sealed class RunCar
{
    public int[] Armour { get; set; } = [-1, -1, -1, -1];
    public int[] Chassis { get; set; } = [-1, -1, -1, -1];
    public int Engine { get; set; } = -1;
    public int Brake { get; set; } = -1;
    public int Suspension { get; set; } = -1;
    public int[] Wheels { get; set; } = [-1, -1, -1, -1, -1, -1];
    /// <summary>Each gun: [hardpoint, hit points, rounds].</summary>
    public List<int[]> Weapons { get; set; } = [];
    /// <summary>For the choice screen: how much armour, chassis and ammunition is left, in percent of full.</summary>
    public int ArmourLeft { get; set; } = 100;
    public int ChassisLeft { get; set; } = 100;
    public int AmmoLeft { get; set; } = 100;
    /// <summary>Full armour and chassis on each side, as built (for the garage's patching from scrap); empty in older run files.</summary>
    public int[] ArmourMax { get; set; } = [];
    public int[] ChassisMax { get; set; } = [];

    public RunCar Repaired() => new()
    {
        Weapons = [.. Weapons.Select(w => new[] { w[0], -1, w[2] })], AmmoLeft = AmmoLeft,
    };

    public RunCar Rearmed() => new()
    {
        Armour = [.. Armour], Chassis = [.. Chassis], Engine = Engine, Brake = Brake, Suspension = Suspension, Wheels = [.. Wheels],
        Weapons = [.. Weapons.Select(w => new[] { w[0], w[1], -1 })], ArmourLeft = ArmourLeft, ChassisLeft = ChassisLeft, AmmoLeft = 100,
    };
}
