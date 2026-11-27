using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// A stock map's node table (docs/remix-missions.md "Node tables"): the places on it a generated job may use, written by hand
/// as coordinates (world x, z, read off the map with i76 remix map) and names, never data of the game's: where the player can
/// start, the points that serve as a pickup, a home or a hideout, the entries a car is put in by (two points: where and which
/// way), the routes a convoy can drive (a stock path by its label, or points), the clusters of the base's own structures that
/// can be defended, the lot where new cars wait out of sight, and the stock gates to open at the start. The director
/// (<see cref="RemixDirector"/>) picks from these.
/// </summary>
public sealed class RemixNodes
{
    /// <summary>The stock mission lending the map.</summary>
    public string Base { get; set; } = "";
    /// <summary>What the map is called on a run's choice screen, in the port's words (a town or a road: "Seagraves", "the Pecos bridge").</summary>
    public string Name { get; set; } = "";
    /// <summary>
    /// The maps next to this one in the region (their bases), for a run's route (docs/remix-missions.md "Roguetrip"): written by hand
    /// from the roguelike gazetteer's road and neighbour links, kept to maps that have a table.
    /// </summary>
    public List<string> Neighbours { get; set; } = [];
    /// <summary>The map is a Nitro Pack mission's: a job on it is offered only with the Nitro install.</summary>
    public bool Nitro { get; set; }
    /// <summary>The light preset (WRLD hour), or null for the stock one.</summary>
    public int? Hour { get; set; }
    /// <summary>Where new cars wait, hidden, until a template starts them: away from every node.</summary>
    public double[] Lot { get; set; } = [];
    /// <summary>Stock gates (the base script's actor keys) opened at the start: a fence across a road the jobs use.</summary>
    public List<string> Gates { get; set; } = [];
    public List<StartNode> Starts { get; set; } = [];
    public List<PointNode> Nodes { get; set; } = [];
    public List<EntryNode> Entries { get; set; } = [];
    public List<RouteNode> Routes { get; set; } = [];
    public List<ClusterNode> Clusters { get; set; } = [];
    /// <summary>Named characters of the game's own cast whose crews (gang) or patrols (police) a job on this map can set on the player.</summary>
    public List<CastNode> Cast { get; set; } = [];
    /// <summary>
    /// Where the stock mission's own action is (its cars and its script's paths, thinned to a point every 300 m; computed from the
    /// base by the node tool and written in): the director prefers places away from it, the corners the stock mission never visits.
    /// </summary>
    public List<double[]> StockAction { get; set; } = [];

    /// <summary>Metres from a point to the nearest of the stock mission's action (large when the table names none).</summary>
    public double Quiet(double[] at) =>
        StockAction.Count == 0 ? 2000 : StockAction.Min(q => Math.Sqrt((q[0] - at[0]) * (q[0] - at[0]) + (q[1] - at[1]) * (q[1] - at[1])));

    public static RemixNodes Parse(string yaml)
    {
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        try { return d.Deserialize<RemixNodes>(yaml) ?? throw new RemixError("an empty node table"); }
        catch (YamlDotNet.Core.YamlException ex) { throw new RemixError($"node table, line {ex.Start.Line}: {ex.InnerException?.Message ?? ex.Message}"); }
    }

    /// <summary>The node tables that ship with the port (src/I76.Formats/Remix/Nodes, embedded), by base.</summary>
    public static IReadOnlyList<RemixNodes> Builtin()
    {
        var asm = typeof(RemixNodes).Assembly;
        var o = new List<RemixNodes>();
        foreach (var n in asm.GetManifestResourceNames().Where(n => n.StartsWith("remix-nodes/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(n)!;
            o.Add(Parse(new StreamReader(s).ReadToEnd()));
        }
        return o;
    }

    /// <summary>The nodes of a kind (pickup, home, hideout, yard: where a raid's convoy parks).</summary>
    public IEnumerable<PointNode> Of(string kind) => Nodes.Where(n => n.Kinds.Contains(kind, StringComparer.Ordinal));
}

public sealed class StartNode
{
    public string Name { get; set; } = "";
    public double[] At { get; set; } = [];
    /// <summary>Degrees from +Z toward +X.</summary>
    public double Heading { get; set; }
    /// <summary>Where it is, in the port's words ("on 380 at the east end"), for the notepad.</summary>
    public string Called { get; set; } = "";
}

public sealed class PointNode
{
    public string Name { get; set; } = "";
    /// <summary>What it can serve as: pickup, home, hideout, yard, start (a place a job may start the player at).</summary>
    public List<string> Kinds { get; set; } = [];
    public double[] At { get; set; } = [];
    /// <summary>What it is, in the port's words ("downtown Tatum, by the bank"), for the notepad.</summary>
    public string Called { get; set; } = "";
    /// <summary>The way a car there faces (degrees from +Z toward +X): an inside place's way out. Null: a start there faces the job's first place.</summary>
    public double? Heading { get; set; }
    /// <summary>
    /// The structure the place is inside or under (docs/remix-missions.md "Places": a barn, a fort's yard, a gas station's canopy),
    /// by object name and copy; null for a place in the open.
    /// </summary>
    public BindSpec? Inside { get; set; }
    /// <summary>What kind of place it is (fort, barn, stable, yard, canopy, awning, compound, underpass, overlook, spur, riverbed), for the words and the weights.</summary>
    public string Sort { get; set; } = "";
    /// <summary>A point outside the structure the car drives out to (the check drives it there from the place, at Heading).</summary>
    public double[]? Out { get; set; }
    /// <summary>Structure nodes taken away before the start (a fort's doors), by placement and node name.</summary>
    public List<OpenSpec> Open { get; set; } = [];
    /// <summary>Placements taken away before the start (a compound's wall section, its gate).</summary>
    public List<BindSpec> Remove { get; set; } = [];
    /// <summary>A friendly car parked there for a hideout (Skeeter's van in the barn): its .vcf stem, its point and heading.</summary>
    public CarSpec? Friend { get; set; }
    /// <summary>Whether a car can be put here as a start or a parked friend: inside or under a structure.</summary>
    [YamlDotNet.Serialization.YamlIgnore] public bool IsInside => Inside is not null;
    /// <summary>A place of note (inside or under a structure, an overlook, a dead-end spur, a riverbed): the director prefers it over a plain road point.</summary>
    [YamlDotNet.Serialization.YamlIgnore] public bool IsSpecial => Sort.Length > 0 || Inside is not null;
}

public sealed class CastNode
{
    public string Name { get; set; } = "";
    /// <summary>gang (a crew the player fights) or police.</summary>
    public string Side { get; set; } = "gang";
}

public sealed class EntryNode
{
    public string Name { get; set; } = "";
    /// <summary>Two points: where a car is put, and the way it faces.</summary>
    public List<double[]> At { get; set; } = [];
    /// <summary>Or a stock path by its label (its first two points).</summary>
    public string? Path { get; set; }
}

public sealed class RouteNode
{
    public string Name { get; set; } = "";
    /// <summary>The route's points (the convoy starts at the first), or a stock path by its label.</summary>
    public List<double[]> Points { get; set; } = [];
    public string? Path { get; set; }
    /// <summary>Where it ends (the convoy's exit); the last point when left out.</summary>
    public double[]? Exit { get; set; }
    /// <summary>Where the convoy's cars are parked to start (the route's first point when left out), and the way they face.</summary>
    public double[]? Park { get; set; }
    public double Heading { get; set; }
    /// <summary>The route can carry an escort (its end a place the player must reach, clear of the exits; the first point where the escorted car waits).</summary>
    public bool Escort { get; set; }
    /// <summary>Where the route ends, in the port's words ("the dirt road east of town"), for an escort's objective.</summary>
    public string Called { get; set; } = "";
}

public sealed class ClusterNode
{
    public string Name { get; set; } = "";
    /// <summary>The arrival zone's centre and radius.</summary>
    public double[] At { get; set; } = [];
    public int Radius { get; set; } = 300;
    /// <summary>The base's structures to defend, by object name and copy.</summary>
    public List<BindSpec> Objects { get; set; } = [];
    /// <summary>The entries (of this table) the attackers come in by.</summary>
    public List<string> Entries { get; set; } = [];
    /// <summary>What the objective calls it ("the farms").</summary>
    public string Called { get; set; } = "";
    /// <summary>The templates it serves: defend (raiders come for it), raid (the player comes for it). Defend only when left out.</summary>
    public List<string> Uses { get; set; } = [];
    public bool Serves(string template) => Uses.Count == 0 ? template == "defend" : Uses.Contains(template, StringComparer.Ordinal);
}
