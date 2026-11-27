using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace I76.Formats.Remix;

/// <summary>
/// The road trip's region (docs/remix-missions.md "The choice map"): the port's own drawing of west Texas and southeast New Mexico,
/// typed by hand from real geography (Remix/Region/region.yaml, embedded): the state line, towns, highways as chains of towns, and
/// where each trip map lies. The road trip's choice screen draws it with the offered jobs as points of interest. No game data.
/// </summary>
public sealed class RemixRegion
{
    public RegionBounds Bounds { get; set; } = new();
    public List<RegionLabel> States { get; set; } = [];
    public List<List<double[]>> Lines { get; set; } = [];
    public List<RegionLabel> Towns { get; set; } = [];
    public List<RegionHighway> Highways { get; set; } = [];
    public Dictionary<string, double[]> Maps { get; set; } = [];

    private static RemixRegion? _builtin;

    /// <summary>The region the port ships.</summary>
    public static RemixRegion Builtin()
    {
        if (_builtin is not null) return _builtin;
        var asm = typeof(RemixRegion).Assembly;
        using var s = asm.GetManifestResourceStream("remix-region/region.yaml") ?? throw new RemixError("no region table");
        var d = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        return _builtin = d.Deserialize<RemixRegion>(new StreamReader(s).ReadToEnd());
    }

    /// <summary>
    /// Where a point (latitude, longitude) falls in a box of w x h, keeping the region's shape (a degree of longitude shortened by
    /// the cosine of the middle latitude), centred; (0, 0) the box's top left, north up.
    /// </summary>
    public (double X, double Y) Project(double[] latLon, double w, double h)
    {
        var b = Bounds;
        double mid = (b.North + b.South) / 2 * Math.PI / 180;
        double k = DetMath.Cos(mid);
        double rw = (b.East - b.West) * k, rh = b.North - b.South;
        double s = Math.Min(w / rw, h / rh);
        double ox = (w - rw * s) / 2, oy = (h - rh * s) / 2;
        return (ox + (latLon[1] - b.West) * k * s, oy + (b.North - latLon[0]) * s);
    }

    /// <summary>A highway's points: its towns' in order.</summary>
    public IEnumerable<double[]> Route(RegionHighway h) => h.Towns.Select(n => Towns.FirstOrDefault(t => t.Name == n)?.At).OfType<double[]>();
}

public sealed class RegionBounds
{
    public double South { get; set; }
    public double North { get; set; }
    public double West { get; set; }
    public double East { get; set; }
}

public sealed class RegionLabel
{
    public string Name { get; set; } = "";
    public double[] At { get; set; } = [];
}

public sealed class RegionHighway
{
    public string Name { get; set; } = "";
    public List<string> Towns { get; set; } = [];
}
