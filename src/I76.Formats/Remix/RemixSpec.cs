using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// A remix description (docs/remix-missions.md): which stock mission lends its map, what changes on it, which objective
/// template drives the new script and with what. <see cref="RemixBuilder"/> turns one into a playable mission file and its
/// objective text. Everything is plain data: points are world x, z (game units, the files' frame), headings degrees from
/// +Z toward +X (the --player-at convention).
/// </summary>
public sealed class RemixSpec
{
    /// <summary>The generated mission's stem (1-8 of A-Z a-z 0-9 _); not a stock mission's.</summary>
    public string Id { get; set; } = "";
    /// <summary>The name the lists show (the WRLD arena name, at most 15 characters).</summary>
    public string Title { get; set; } = "";
    /// <summary>The stock mission whose terrain, roads, placements, look and library routines it uses (t07, t05...).</summary>
    public string Base { get; set; } = "";
    /// <summary>Reserved for the run director's draws; nothing the build makes depends on it yet.</summary>
    public int Seed { get; set; }
    /// <summary>The WRLD hour (0-23; it picks the light preset), or null to keep the stock one.</summary>
    public int? Hour { get; set; }
    /// <summary>The player's placement moved (its car is the one the scenario form gives), or null to keep it.</summary>
    public PoseSpec? Start { get; set; }
    /// <summary>
    /// New nav paths appended to the script's table (labelled rx_KEY): a zone is one point (its centre), an entry two (where a
    /// teleport puts a car and the way it faces), a route more.
    /// </summary>
    public Dictionary<string, List<double[]>> Places { get; set; } = [];
    /// <summary>New parked cars (ODEF placements of type 1), by actor name: their .vcf stem and where they wait.</summary>
    public Dictionary<string, CarSpec> Cars { get; set; } = [];
    /// <summary>Existing placements (structures, cars) the script needs, by actor name: the object name and which of its copies.</summary>
    public Dictionary<string, BindSpec> Bind { get; set; } = [];
    /// <summary>Stock objects removed (by object name and copy), such as a gate that would close a road.</summary>
    public List<BindSpec> Remove { get; set; } = [];
    /// <summary>The stock leave-map threads dropped, by their exit path's label (a start beside an exit).</summary>
    public List<string> DropExits { get; set; } = [];
    /// <summary>
    /// The stock cars no kept thread and no template names (the base's mission threads drove them): remove (the default; they
    /// would only stand where the base parked them) or keep (as decor).
    /// </summary>
    public string IdleCars { get; set; } = "remove";
    /// <summary>Gates (actor names: a stock actor or a bound placement) triggered (each trigger slides it the other way) at the template's moments.</summary>
    public GateSpec Gates { get; set; } = new();
    /// <summary>CB clips (sound file stems, the install's) played at the template's moments; the library's own pools (taunts, hit chatter, exits) run anyway.</summary>
    public RadioSpec Radio { get; set; } = new();
    public ObjectiveSpec Objective { get; set; } = new();
    public TextSpec Text { get; set; } = new();

    public static RemixSpec Parse(string yaml)
    {
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        try { return d.Deserialize<RemixSpec>(yaml) ?? throw new RemixError("an empty remix description"); }
        catch (YamlDotNet.Core.YamlException ex) { throw new RemixError($"line {ex.Start.Line}: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    public static RemixSpec Load(string path) => Parse(File.ReadAllText(path));
}

public sealed class PoseSpec
{
    public double[] At { get; set; } = [];
    public double Heading { get; set; }
}

public sealed class CarSpec
{
    public string Vcf { get; set; } = "";
    public double[] At { get; set; } = [];
    public double Heading { get; set; }
}

public sealed class BindSpec
{
    public string Object { get; set; } = "";
    /// <summary>Which placement of that name, in file order from 0.</summary>
    public int Nth { get; set; }
}

/// <summary>A zone: a place and a radius (metres; a square's half side for the exits' kind of test).</summary>
public sealed class ZoneSpec
{
    public string Place { get; set; } = "";
    public int Radius { get; set; } = 60;
    /// <summary>For an arrival: the seconds allowed (0: no deadline).</summary>
    public int Seconds { get; set; }
}

/// <summary>A car parked until something wakes it: the player within its radius, or (for a pickup's guards) the pickup made.</summary>
public sealed class SleeperSpec
{
    public string Car { get; set; } = "";
    public int Wake { get; set; } = 250;
    /// <summary>A police car's siren on when it wakes.</summary>
    public bool Siren { get; set; }
}

public sealed class PursuitSpec
{
    public List<string> Cars { get; set; } = [];
    /// <summary>The entry place they are teleported to (each a little behind the one before).</summary>
    public string Entry { get; set; } = "";
    /// <summary>Seconds after the trigger (the pickup, or the start when there is none).</summary>
    public int Delay { get; set; } = 3;
    public int Speed { get; set; } = 20;
    /// <summary>Police cars' sirens on as they come in (tail).</summary>
    public bool Siren { get; set; }
}

/// <summary>The objective template and its parameters (docs/remix-missions.md lists each template's).</summary>
public sealed class ObjectiveSpec
{
    /// <summary>
    /// defend (attackers by several entries pick structures; lose when they are gone), deliver (an optional pickup, then home
    /// with nobody near), tail (lose the pursuers, then the hideout) or intercept (stop a convoy before its exit).
    /// </summary>
    public string Template { get; set; } = "";
    // ---- defend ----
    public List<string> Defend { get; set; } = [];
    public List<string> Entries { get; set; } = [];
    /// <summary>The structures that may be lost before the mission is: Defend's count − 1 (all gone) by default.</summary>
    public int? MayLose { get; set; }
    /// <summary>The attackers in waves: each wave's cars start once its seconds (from the start) have passed and its kill count is reached.</summary>
    public List<WaveSpec> Waves { get; set; } = [];
    /// <summary>Seconds an attacker keeps a target, once near it, before it picks again (P11: 10, from the pick).</summary>
    public int Retarget { get; set; } = 10;
    /// <summary>How near its target an attacker must be before the retarget clock runs (the port's: the farms are far apart).</summary>
    public int Near { get; set; } = 150;
    public ZoneSpec? Arrive { get; set; }
    // ---- deliver ----
    public ZoneSpec? Pickup { get; set; }
    public ZoneSpec? Home { get; set; }
    /// <summary>The arrival needs no enemy within this many metres (0: none).</summary>
    public int Clear { get; set; }
    public List<SleeperSpec> Sleepers { get; set; } = [];
    public PursuitSpec? Pursuit { get; set; }
    /// <summary>Seconds allowed (0: none), counted from <see cref="DeadlineFrom"/>.</summary>
    public int Deadline { get; set; }
    /// <summary>pickup (the return leg only, T05's clock; the default with a pickup) or start (the whole run).</summary>
    public string? DeadlineFrom { get; set; }
    // ---- tail ----
    /// <summary>tail: the hideout, reached once the pursuers are lost; arriving with an enemy within <see cref="Clear"/> fails.</summary>
    public ZoneSpec? Hideout { get; set; }
    /// <summary>tail: lost means nobody hostile within the radius for the seconds.</summary>
    public LostSpec? Lost { get; set; }
    // ---- intercept ----
    public ConvoySpec? Convoy { get; set; }
    // ---- every template ----
    /// <summary>The enemies' skill tier (1 best .. 5), both driving and shooting.</summary>
    public int Skill { get; set; } = 2;
    public int Aggression { get; set; } = 1;
}

/// <summary>tail: when the pursuers count as lost.</summary>
public sealed class LostSpec
{
    public int Radius { get; set; } = 500;
    public int Seconds { get; set; } = 15;
}

/// <summary>intercept: a lead car driving a route to an exit, escorts keeping station on it, both turning on the player when alerted.</summary>
public sealed class ConvoySpec
{
    /// <summary>The car that must not reach the exit (a cars or bind entry, or a stock actor).</summary>
    public string Lead { get; set; } = "";
    public List<string> Escorts { get; set; } = [];
    /// <summary>The route the lead drives (a place of several points, or a stock path).</summary>
    public string Route { get; set; } = "";
    /// <summary>The zone whose reaching by the lead fails the mission (on a place: its first point).</summary>
    public ZoneSpec? Exit { get; set; }
    /// <summary>The route's speed, and once alerted (the player within <see cref="Alert"/> metres, or any of them shot).</summary>
    public int Speed { get; set; } = 18;
    public int AlertSpeed { get; set; } = 30;
    public int Alert { get; set; } = 200;
    /// <summary>Seconds from the start before it rolls.</summary>
    public int Delay { get; set; } = 5;
    /// <summary>Metres between the escorts' stations behind the lead.</summary>
    public int Spacing { get; set; } = 20;
    /// <summary>The escorts must die too (objective 2); false: the lead alone.</summary>
    public bool KillEscorts { get; set; } = true;
}

public sealed class WaveSpec
{
    public List<string> Cars { get; set; } = [];
    public int AtSeconds { get; set; }
    public int AtKills { get; set; }
}

/// <summary>Gates triggered at the start, at the trap moment (defend: the arrival; deliver: the pickup) and at the win.</summary>
public sealed class GateSpec
{
    /// <summary>
    /// Seconds between two triggers of one gate: a gate ignores a trigger while it slides (GateMotion), so each gate's triggers
    /// are queued and sent this far apart (longer than any stock gate's slide).
    /// </summary>
    public int Settle { get; set; } = 30;
    public List<string> Start { get; set; } = [];
    public List<string> Trap { get; set; } = [];
    public List<string> Win { get; set; } = [];
}

/// <summary>CB clips by moment: the start, the trap moment (arrival or pickup), each later wave (defend) or the pursuers' arrival (deliver), the win.</summary>
public sealed class RadioSpec
{
    public List<string> Start { get; set; } = [];
    public List<string> Trap { get; set; } = [];
    public List<string> Wave { get; set; } = [];
    public List<string> Win { get; set; } = [];
    public IEnumerable<string> All => Start.Concat(Trap).Concat(Wave).Concat(Win).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The objective text (the .npt): up to six objective lines, a line starting "(hidden)" shown later, then the failure lines.
/// Failure 1 is the player's death and 2 leaving the map (the stock leave-map routine shows line 2); the template's own follow.
/// </summary>
public sealed class TextSpec
{
    public List<string> Objectives { get; set; } = [];
    public List<string> Failures { get; set; } = [];
}

public sealed class RemixError(string message) : Exception(message);
