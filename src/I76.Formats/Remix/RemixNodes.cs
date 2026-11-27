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

    /// <summary>The nodes of a kind (pickup, home, hideout).</summary>
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
    /// <summary>What it can serve as: pickup, home, hideout.</summary>
    public List<string> Kinds { get; set; } = [];
    public double[] At { get; set; } = [];
    /// <summary>What it is, in the port's words ("downtown Tatum, by the bank"), for the notepad.</summary>
    public string Called { get; set; } = "";
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
}
