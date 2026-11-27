using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using I76.Formats.Vfs;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// One job of a co-op roguetrip (docs/remix-missions.md "Roguetrip in co-op"): everything the generator takes, so that the host and each
/// client build the same mission from their own installs rather than sending it. The same record on two machines of the same build gives
/// the same files; <see cref="Hash"/> is what the host sends for the clients to check theirs against.
/// </summary>
public sealed record CoopJob(int Run, int Builder, int Job, string Map, string Template, int Seed, double Challenge, int Players)
{
    /// <summary>The stem a co-op job is built under (never the single player's <see cref="RemixRun.Stem"/>: a guest's own run keeps its job).</summary>
    public const string Stem = "xcoop";

    /// <summary>
    /// The enemies grow with the crew (the port's rule): the job is rolled at its challenge times 1 + 0.75 a player after the first (two
    /// players 1.75, three 2.5, four 3.25), so the director's fill adds cars to its groups (at most <see cref="RemixDirector.MaxCars"/>).
    /// </summary>
    public static double Scale(int players) => 1 + 0.75 * (Math.Clamp(players, 1, 8) - 1);

    /// <summary>The job's description: the director's roll for the offer's seed on its map and template at the crew's challenge.</summary>
    public RemixSpec Spec(IReadOnlyList<RemixNodes>? tables = null)
    {
        var s = RemixDirector.Roll(Seed, new RemixDirector.JobAsk
        {
            Tables = tables, Id = Stem, Challenge = Challenge * Scale(Players), Map = Map, Template = Template, Nitro = true,
            Label = $"Roguetrip {Run.ToString(CultureInfo.InvariantCulture)}, job {Job.ToString(CultureInfo.InvariantCulture)}",
        });
        s.Title = $"ROGUETRIP {Math.Min(RemixRun.Jobs, Job)}/{RemixRun.Jobs}";
        return s;
    }

    /// <summary>Builds the job from <paramref name="install"/> (refused for another builder's job: it would roll another mission).</summary>
    public RemixOutput Build(GameInstall install, IReadOnlyList<RemixNodes>? tables = null)
    {
        if (Builder != RemixBuilder.Version) throw new RemixError(BuilderMismatch(Builder, RemixBuilder.Version));
        return RemixBuilder.Build(install, Spec(tables));
    }

    /// <summary>The files' hash (mission, text, marks): equal on two machines that built the same job alike.</summary>
    public static byte[] Hash(RemixOutput o)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(o.Mission);
        h.AppendData(Encoding.Latin1.GetBytes(o.Text));
        h.AppendData(Encoding.UTF8.GetBytes(o.Marks));
        return h.GetHashAndReset();
    }

    public static string BuilderMismatch(int host, int mine) =>
        $"The host's ROGUETRIP jobs are made by builder v{host} and this game's by v{mine}: update both games to the same build";

    /// <summary>The refusal when this game's build of the job is not the host's (another build or another install).</summary>
    public static string Mismatch(string hostBuild, string myBuild) =>
        $"This game's ROGUETRIP job differs from the host's (host build {(hostBuild.Length > 0 ? hostBuild : "unknown")}, this build {(myBuild.Length > 0 ? myBuild : "unknown")}): play on the same build of I26 and an untouched install";

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"run {Run} job {Job}/{RemixRun.Jobs} #{Seed} v{Builder} {Map} {Template} challenge {Challenge:0.0} x{Scale(Players):0.00} for {Players}");
}

/// <summary>A co-op player's car through the run, by the player's name: its file and its damage carried.</summary>
public sealed class CrewCar
{
    public string Name { get; set; } = "";
    /// <summary>The car file the player drove in the last job (a new car is a car as built).</summary>
    public string Car { get; set; } = "";
    public RunCar? State { get; set; }
    /// <summary>Killed in the last job: out of it, back for the next with the car repaired to the floor (<see cref="CoopRun.Floor"/>).</summary>
    public bool Out { get; set; }
}

/// <summary>An objective of the last job as it stood at its end (the debrief page's).</summary>
public sealed class CoopObjective
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public bool Failed { get; set; }
}

/// <summary>
/// A roguetrip played in co-op (docs/remix-missions.md "Roguetrip in co-op"): the host's run (<see cref="RemixRun"/>: the number, the
/// jobs, the busts, the offers' stream) and the crew's cars, kept by the host in the user folder as coop-run.yaml. The host chooses each
/// job on the route map; every player builds it from <see cref="CoopJob"/>. Pure: no Godot, no network, no sim.
/// </summary>
public sealed class CoopRun
{
    public const string FileName = "coop-run.yaml";
    /// <summary>Of a full armour side's plate patched back onto each surviving car after a won job (the trip's salvage left out in co-op).</summary>
    public const double WinPatch = 0.3;
    /// <summary>What a car killed in a job comes back with: each armour and chassis side at least this share of full.</summary>
    public const double FloorShare = 0.5;

    public RemixRun Run { get; set; } = new();
    public List<CrewCar> Crew { get; set; } = [];
    /// <summary>The offer the host has highlighted on the route map (what START plays).</summary>
    public int Selected { get; set; }
    /// <summary>The players at the last job's start (its enemies were scaled for them).</summary>
    public int Players { get; set; }
    public List<CoopObjective> LastObjectives { get; set; } = [];

    public static CoopRun Start(int number) => new() { Run = RemixRun.Start(number, "", "", "", -1) };

    /// <summary>The route map's offers (the choice only), from the run's number and its state.</summary>
    public List<RunOffer> Offers(IReadOnlyList<RemixNodes>? tables = null) => Run.Phase == "choose" ? Run.Offers(tables ?? RemixNodes.Builtin(), nitro: false) : [];

    /// <summary>The job an offer is, for <paramref name="players"/>.</summary>
    public CoopJob Job(RunOffer offer, int players) => new(Run.Number, RemixBuilder.Version, Run.Legs.Count + 1, offer.Map, offer.Template, offer.Seed, offer.Challenge, Math.Max(1, players));

    /// <summary>The job being played (phase playing), or null.</summary>
    public CoopJob? Playing => Run.Phase == "playing" && Run.Current is { } c ? new(Run.Number, Run.Builder, Run.Legs.Count + 1, c.Map, c.Template, c.Seed, c.Challenge, Math.Max(1, Players)) : null;

    public CrewCar? Of(string name) => Crew.FirstOrDefault(c => c.Name.Equals(name, StringComparison.Ordinal));

    /// <summary>
    /// The state a player's car starts the next job in: what it carried when it is the same car file, else as built (a new car, or a
    /// player new to the run).
    /// </summary>
    public RunCar? CarryFor(string name, string car) => Of(name) is { } c && c.Car.Equals(car, StringComparison.OrdinalIgnoreCase) ? c.State : null;

    /// <summary>A car killed in a job, for the next: every armour and chassis side up to <see cref="FloorShare"/> of full, the parts and the guns as built.</summary>
    public static RunCar Floor(RunCar dead)
    {
        int[] Up(int[] have, int[] max) => [.. have.Select((h, i) => i < max.Length ? Math.Max(h < 0 ? max[i] : h, (int)Math.Round(max[i] * FloorShare)) : h)];
        var c = new RunCar { ArmourMax = [.. dead.ArmourMax], ChassisMax = [.. dead.ChassisMax], AmmoLeft = 100 };
        if (dead.ArmourMax.Length == 4 && dead.ChassisMax.Length == 4)
        {
            c.Armour = Up(dead.Armour, dead.ArmourMax);
            c.Chassis = Up(dead.Chassis, dead.ChassisMax);
            c.ArmourLeft = Pct(c.Armour, c.ArmourMax);
            c.ChassisLeft = Pct(c.Chassis, c.ChassisMax);
        }
        return c;
    }

    /// <summary>A surviving car after a won job: <see cref="WinPatch"/> of its full armour patched on (the weakest side first, RemixRun.Patch's way) and the guns reloaded.</summary>
    public static RunCar AfterWin(RunCar car)
    {
        // (Patch with nothing scrapped is a copy of the car; the plate is laid on that)
        var c = car is { ArmourMax.Length: 4, ChassisMax.Length: 4 }
            ? PatchPlate(RemixRun.Patch(car, car.ArmourMax, car.ChassisMax, 0), (int)Math.Round(car.ArmourMax.Sum() * WinPatch)) : car;
        return new RunCar
        {
            Armour = [.. c.Armour], Chassis = [.. c.Chassis], Engine = c.Engine, Brake = c.Brake, Suspension = c.Suspension, Wheels = [.. c.Wheels],
            Weapons = [.. c.Weapons.Select(w => new[] { w[0], w[1], -1 })], ArmourMax = [.. c.ArmourMax], ChassisMax = [.. c.ChassisMax],
            ArmourLeft = c.ArmourLeft, ChassisLeft = c.ChassisLeft, AmmoLeft = 100,
        };
    }

    // (RemixRun.Patch counts plate in tenths of full armour a scrapped part; the co-op patch is a share, so its plate is laid on here the same way)
    private static RunCar PatchPlate(RunCar c, int plate)
    {
        for (int pass = 0; pass < 2 && plate > 0; pass++)
        {
            var have = pass == 0 ? c.Armour : c.Chassis;
            var max = pass == 0 ? c.ArmourMax : c.ChassisMax;
            while (plate > 0)
            {
                int k = -1;
                long worst = long.MaxValue;
                for (int z = 0; z < 4; z++)
                {
                    int now = have[z] < 0 ? max[z] : have[z];
                    if (now < max[z] && now * 1000L / Math.Max(1, max[z]) < worst) { worst = now * 1000L / Math.Max(1, max[z]); k = z; }
                }
                if (k < 0) break;
                int cur = have[k] < 0 ? max[k] : have[k];
                int step = Math.Min(Math.Min(plate, Math.Max(1, max[k] / 20)), max[k] - cur);
                have[k] = cur + step;
                plate -= step;
            }
        }
        c.ArmourLeft = Pct(c.Armour, c.ArmourMax);
        c.ChassisLeft = Pct(c.Chassis, c.ChassisMax);
        return c;
    }

    private static int Pct(int[] h, int[] m) => m.Sum() <= 0 ? 100 : (int)Math.Round(100.0 * h.Select((x, i) => x < 0 && i < m.Length ? m[i] : x).Sum() / m.Sum());

    /// <summary>
    /// The job's end for the crew: each player's car as it ended (null: as built), killed or not. A survivor carries its damage (a won job
    /// patches it, <see cref="AfterWin"/>); a car killed comes back for the next at the floor (<see cref="Floor"/>). Then the run's own
    /// <see cref="RemixRun.Finish(RemixRun.Outcome, int, RunCar?, string)"/>: the run ends only when the whole crew is dead (the outcome says).
    /// </summary>
    public void Finish(RemixRun.Outcome outcome, int kills, IEnumerable<(string Name, string Car, RunCar? State, bool Dead)> crew, string why, IEnumerable<CoopObjective> objectives)
    {
        foreach (var (name, car, state, dead) in crew)
        {
            var c = Of(name);
            if (c is null) Crew.Add(c = new CrewCar { Name = name });
            c.Car = car;
            c.Out = dead;
            c.State = state is null ? null : dead ? Floor(state) : outcome == RemixRun.Outcome.Won ? AfterWin(state) : state;
        }
        LastObjectives = [.. objectives];
        Run.Finish(outcome, kills, null, why);
    }

    /// <summary>Past the debrief: a won job's salvage is the patch above (done at the end), so a won job goes straight on to the next choice.</summary>
    public void Debriefed()
    {
        Run.Debriefed();
        if (Run.Phase == "salvage") Run.Salvaged(null, null);
        Selected = 0;
    }

    public string ToYaml() =>
        "# a co-op roguetrip in progress (docs/remix-missions.md \"Roguetrip in co-op\"): the host's run and the crew's cars\n"
        + new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).DisableAliases().Build().Serialize(this);

    public static CoopRun Parse(string yaml)
    {
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
        try { return d.Deserialize<CoopRun>(yaml) ?? throw new RemixError("an empty co-op run file"); }
        catch (YamlDotNet.Core.YamlException ex) { throw new RemixError($"co-op run file, line {ex.Start.Line}: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    /// <summary>The co-op run kept in a folder, or null (none, unreadable, or over). A job the game closed during comes back as its choice.</summary>
    public static CoopRun? Load(string dir)
    {
        var p = Path.Combine(dir, FileName);
        try
        {
            if (!File.Exists(p)) return null;
            var r = Parse(File.ReadAllText(p));
            r.Run.Resume();
            return r;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RemixError) { return null; }
    }

    public void Save(string dir)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, FileName);
        File.WriteAllText(p + ".new", ToYaml());
        File.Move(p + ".new", p, overwrite: true);
    }
}
