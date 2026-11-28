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
    /// <summary>The director's choice of a spoken line at each radio moment (RemixVoice.Plan), for reading; not written to the file.</summary>
    [YamlIgnore] public List<VoiceChoice> Voice { get; set; } = [];
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
    /// <summary>Structures set down before the mission starts (a wreck at a pickup, a roadblock on a route).</summary>
    public List<PropSpec> Props { get; set; } = [];
    /// <summary>Stock objects removed (by object name and copy), such as a gate that would close a road.</summary>
    public List<BindSpec> Remove { get; set; } = [];
    /// <summary>
    /// Structure nodes taken away before the start: a fort's doors (docs/remix-missions.md "Places"). They are hidden from the
    /// picture (both looks) and the check holds that the way out past them can be driven; the sim's colliders are the base's.
    /// </summary>
    public List<OpenSpec> Open { get; set; } = [];
    /// <summary>Places inside or under structures the job uses (where, the way out, what), checked by driving out of each (i76 remix check).</summary>
    public List<InsideSpec> Inside { get; set; } = [];
    /// <summary>One or two places of the map lettered on the job's map for bearings (a town, a fort, a bridge): a name and a point.</summary>
    public List<LandmarkSpec> Landmarks { get; set; } = [];
    /// <summary>The stock leave-map threads dropped, by their exit path's label (a start beside an exit).</summary>
    public List<string> DropExits { get; set; } = [];
    /// <summary>
    /// A roguetrip job's prize (docs/remix-missions.md "Rosters and salvage"): salvage keys of its lead car's best parts, given on a win as a
    /// stock mission's scripted salvage (its .fsi) is, in a usable condition; null for any other job. Read by the game, not the build.
    /// </summary>
    public List<string>? Prize { get; set; }
    /// <summary>Civilian traffic (docs/remix-missions.md "Civilian traffic"): cars of nobody's side driving the roads; null for none.</summary>
    public List<TrafficSpec>? Traffic { get; set; }
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
    /// <summary>The job's pacing budget: the seconds of driving it allows to the first contact and between events (RemixPacing; i76 remix check).</summary>
    public PacingSpec Pacing { get; set; } = new();

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
    /// <summary>A start inside a structure: the point outside it the car must be able to drive out to (i76 remix check drives it there).</summary>
    public double[]? Out { get; set; }
}

/// <summary>Structure nodes taken away before the mission starts (a fort's doors): the placement by object name and copy, and its nodes by name.</summary>
public sealed class OpenSpec
{
    public string Object { get; set; } = "";
    public int Nth { get; set; }
    public List<string> Nodes { get; set; } = [];
}

/// <summary>A landmark lettered on a job's map: its name and where it is.</summary>
public sealed class LandmarkSpec
{
    public string Name { get; set; } = "";
    public double[] At { get; set; } = [];
}

/// <summary>A place of a job inside or under a structure: its point, the way a car there faces, the point outside it drives out to, and its words.</summary>
public sealed class InsideSpec
{
    public string Name { get; set; } = "";
    public double[] At { get; set; } = [];
    public double Heading { get; set; }
    public double[] Out { get; set; } = [];
    public string Called { get; set; } = "";
}

public sealed class CarSpec
{
    public string Vcf { get; set; } = "";
    public double[] At { get; set; } = [];
    public double Heading { get; set; }
    /// <summary>
    /// The side ((ODEF flags >> 16) &amp; 7): null for the template's choice, the enemy's (2) for every car it turns on the player,
    /// so the target keys cycle to it and the radar shows it as a stock mission's enemies; 0 nobody's, 1 the player's.
    /// </summary>
    public int? Team { get; set; }
}

/// <summary>
/// A structure set down before the mission starts (docs/remix-missions.md "Structures"): an object of the install by its
/// placement name (a building, a wreck, a fence, a roadblock's parts), the ODEF type it is placed as (2 building, 3 small object,
/// 4 scenery; the type the base game places that object as), where and which way. It collides as the base game's placements do.
/// </summary>
public sealed class PropSpec
{
    public string Object { get; set; } = "";
    public uint Type { get; set; } = 2;
    public double[] At { get; set; } = [];
    public double Heading { get; set; }
}

/// <summary>
/// A twist: more cars teleported in (from the lot) at an entry when an objective step opens, a few seconds after; they come for
/// the player. The step is the objective's number (2: when objective 2 opens).
/// </summary>
public sealed class ReinforceSpec
{
    public List<string> Cars { get; set; } = [];
    public string Entry { get; set; } = "";
    public int AtStep { get; set; } = 2;
    public int Delay { get; set; } = 5;
    public int Speed { get; set; } = 25;
    public bool Siren { get; set; }
    /// <summary>Whom they come for: the player (null), or the escorted car ("escort": an escort's ambush).</summary>
    public string? Target { get; set; }
    /// <summary>The clip its first car says on arrival, in place of the radio's reinforce clip (a generated job's own line for it).</summary>
    public string? Say { get; set; }
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
    /// with nobody near), tail (lose the pursuers, then the hideout), intercept (stop a convoy before its exit), escort (see a
    /// slow friendly through to a goal) or raid (destroy the targets, then get out before the roads close).
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
    // ---- escort ----
    public EscortSpec? Escort { get; set; }
    // ---- raid ----
    /// <summary>raid: what must be destroyed (structures by bind, or parked cars that never start), each a name.</summary>
    public List<string> Targets { get; set; } = [];
    /// <summary>raid: where to get out to once the targets are down, and the seconds allowed (from the last target's fall).</summary>
    public ZoneSpec? Escape { get; set; }
    // ---- every template ----
    /// <summary>Twists: more cars brought in as an objective step opens (reinforcements).</summary>
    public List<ReinforceSpec> Reinforce { get; set; } = [];
    /// <summary>
    /// A last step after the template's own win: get to this zone (with nobody within <see cref="FinishClear"/> metres when that
    /// is above 0); its objective line is the last of the text's objectives.
    /// </summary>
    public ZoneSpec? Finish { get; set; }
    public int FinishClear { get; set; }
    /// <summary>Seconds after the finish step opens before it can count (the twist's cars have come in; a finish at the start is not done by standing there).</summary>
    public int FinishAfter { get; set; }
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
    /// <summary>It rolls sooner when the player comes within this many metres of the lead (0: on its delay only).</summary>
    public int Roll { get; set; }
    /// <summary>Metres between the escorts' stations behind the lead.</summary>
    public int Spacing { get; set; } = 20;
    /// <summary>The escorts must die too (objective 2); false: the lead alone.</summary>
    public bool KillEscorts { get; set; } = true;
}

/// <summary>
/// escort: a slow friendly car driving a route from its first point to a goal, a waypoint on the way; it drives only while the
/// player keeps up (within <see cref="Wait"/> metres; it waits for him to come back within <see cref="Resume"/>), so it never runs
/// ahead alone (the owner's complaint about the stock escorts). Wingmen follow the player and fire at the nearest enemy.
/// </summary>
public sealed class EscortSpec
{
    /// <summary>The escorted car (a cars entry: the player's team).</summary>
    public string Car { get; set; } = "";
    /// <summary>The route it drives (a place of several points, or a stock path).</summary>
    public string Route { get; set; } = "";
    /// <summary>The halfway point (objective 1) and the goal (objective 2): zones the escorted car must reach.</summary>
    public ZoneSpec? Mid { get; set; }
    public ZoneSpec? Goal { get; set; }
    public int Speed { get; set; } = 14;
    public int Wait { get; set; } = 220;
    public int Resume { get; set; } = 120;
    /// <summary>Friendly cars that follow the player (cars entries, the player's team), each at a station beside him.</summary>
    public List<string> Wingmen { get; set; } = [];
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
    /// <summary>Clips by objective step (the first list as objective 2 opens, the next as 3 does...): the instruction for each beat.</summary>
    public List<List<string>> Steps { get; set; } = [];
    /// <summary>Clips for a reinforcement's arrival.</summary>
    public List<string> Reinforce { get; set; } = [];
    /// <summary>What a clip says, in words: lettered at the top of the screen when the install has no such clip (the radio's text fallback).</summary>
    public Dictionary<string, string> Words { get; set; } = [];
    /// <summary>The clock nearly out: a clip said <c>at</c> seconds after step <c>from</c> opens, while the open step is still <c>until</c> or before.</summary>
    public List<HurryCue> Hurry { get; set; } = [];
    /// <summary>A count of the template's group down ("Two down, one to go"): said as exactly <c>dead</c> of it are down.</summary>
    public List<CountCue> Counts { get; set; } = [];
    /// <summary>A convoy's lead, alive, within 600 m of its way out.</summary>
    public List<string> Getaway { get; set; } = [];
    /// <summary>A defence one structure from lost.</summary>
    public List<string> Wiped { get; set; } = [];
    [YamlIgnore] public IEnumerable<string> All => Start.Concat(Trap).Concat(Wave).Concat(Win).Concat(Steps.SelectMany(s => s)).Concat(Reinforce)
        .Concat(Hurry.Select(h => h.Clip)).Concat(Counts.Select(c => c.Clip)).Concat(Getaway).Concat(Wiped).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The objective text (the .npt): up to six objective lines, a line starting "(hidden)" shown later, then the failure lines.
/// Failure 1 is the player's death and 2 leaving the map (the stock leave-map routine shows line 2); the template's own follow.
/// </summary>
public sealed class TextSpec
{
    public List<string> Objectives { get; set; } = [];
    public List<string> Failures { get; set; } = [];
    /// <summary>
    /// The failure line for a death while each objective is the open one (in objective order); the builder appends them after
    /// <see cref="Failures"/>. Missing ones are made from the objective's own line ("Killed before ...").
    /// </summary>
    public List<string> Deaths { get; set; } = [];
    /// <summary>Notes added under the objectives on the notepad: where the places are, in words (the director writes them).</summary>
    public List<string> Notes { get; set; } = [];
}

/// <summary>A radio moment of a generated job, what was true of the job then, and the spoken line chosen (null: none fitted).</summary>
public sealed record VoiceChoice(string Moment, IReadOnlyCollection<string> Facts, string? Line);

public sealed class HurryCue
{
    public int From { get; set; } = 1;
    public int Until { get; set; } = 1;
    public int At { get; set; }
    public string Clip { get; set; } = "";
}

public sealed class CountCue
{
    public int Dead { get; set; }
    public string Clip { get; set; } = "";
}

public sealed class RemixError(string message) : Exception(message);
