using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// A road trip (docs/remix-missions.md "Road trip"): a run of four generated jobs across neighbouring maps of the region, the
/// challenge rising 0.8, 1.0, 1.2, 1.4. Before each job the player picks one of two or three offers (a map next to the one he is
/// on, a template, its challenge and a line naming the gang); the car's damage carries from job to job and each win grants one
/// pick: a full repair, full ammunition, or nothing for a bonus mark. A failed job ends the run. Everything a run offers comes
/// from its number and the choices made (a SplitMix64 stream of its own, outside the simulation): the same number and the same
/// picks give the same offers and the same jobs. Kept in the user folder between jobs (run.yaml), so quitting the game keeps it.
/// </summary>
public sealed class RemixRun
{
    /// <summary>The jobs a run has, and the challenge of each in turn (Back to Spanner's 1.0).</summary>
    public const int Jobs = 4;
    public static readonly double[] Challenges = [0.8, 1.0, 1.2, 1.4];
    /// <summary>The stem a run's job is built under (one at a time).</summary>
    public const string Stem = "xrun";
    /// <summary>The run's file in the generated missions' folder.</summary>
    public const string FileName = "run.yaml";

    public int Number { get; set; }
    /// <summary>choose (offers on the screen), playing (a job launched), reward (a won job's pick), over (a job lost), done (four won).</summary>
    public string Phase { get; set; } = "choose";
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
    /// <summary>The jobs played, in order.</summary>
    public List<RunLeg> Legs { get; set; } = [];
    /// <summary>The job being played (Phase playing), or the one just played (reward, over).</summary>
    public RunOffer? Current { get; set; }

    [YamlIgnore] public bool Over => Phase is "over" or "done";
    /// <summary>The challenge of the next job.</summary>
    [YamlIgnore] public double NextChallenge => Challenges[Math.Clamp(Won, 0, Jobs - 1)];

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
        var rng = new Mix(unchecked(Number * 0x2545F491 + Won * 0x9E3779B9 + Hash(Map)));
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
            // the flavour: the job's own first objective, without its label ("Road trip 12, job 2: ")
            try { var first = Spec(offer, tables).Text.Objectives[0]; offer.Flavour = first.IndexOf(": ", StringComparison.Ordinal) is var k and > 0 ? first[(k + 2)..] : first; }
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
            Label = $"Road trip {Number.ToString(CultureInfo.InvariantCulture)}, job {(Legs.Count + 1).ToString(CultureInfo.InvariantCulture)}",
        }) is var s ? Retitle(s) : null!;

    private RemixSpec Retitle(RemixSpec s)
    {
        s.Title = $"ROAD TRIP {Math.Min(Jobs, Legs.Count + 1)}/{Jobs}";
        return s;
    }

    /// <summary>The player took an offer: the job is being played.</summary>
    public void Take(RunOffer offer)
    {
        Current = offer;
        Phase = "playing";
    }

    /// <summary>The job is over: won (the car's state carried, the reward to pick next, or the run done) or lost (the run over).</summary>
    public void Finish(bool won, int kills, RunCar? car)
    {
        if (Current is not { } c) return;
        Kills += kills;
        Legs.Add(new RunLeg { Map = c.Map, Name = c.Name, Template = c.Template, Seed = c.Seed, Challenge = c.Challenge, Won = won, Kills = kills });
        Map = c.Map;
        if (!won) { Phase = "over"; return; }
        Won++;
        if (car is not null) State = car;
        Phase = Won >= Jobs ? "done" : "reward";
    }

    /// <summary>A won job's pick: repair (every part and armour to full), rearm (every gun's rounds to full) or bonus (nothing, a mark).</summary>
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
        "# a road trip in progress (docs/remix-missions.md \"Road trip\"): the run's number, the jobs played, the car's damage\n"
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

    /// <summary>What a template is called on the choice screen.</summary>
    public static string JobWord(string template) => template switch
    {
        "deliver" => "DELIVERY", "tail" => "SHAKE THE TAIL", "intercept" => "CONVOY HIT", "defend" => "DEFENCE",
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
