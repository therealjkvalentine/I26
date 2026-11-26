using System.Globalization;
using System.Text.Json;
using I76.Formats.Fsm;
using I76.Formats.Mission;
using I76.Formats.Mission.Edit;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix map BASE [--json FILE]: what a remix description can name on a stock mission's map, read from the install: the
/// player's start, every car placement with the base script's actor key, the structures, the nav paths (label, points), and the
/// roads' centre lines thinned to about 100 m (the JSON). For writing descriptions and node tables; nothing is kept.
/// </summary>
public static class RemixMapDump
{
    public static int Run(GameInstall install, List<string> argv)
    {
        if (argv.Count < 3) { Console.Error.WriteLine("usage: i76 remix map BASE [--json FILE]"); return 2; }
        var path = install.FindMission(argv[2]) ?? throw new FileNotFoundException(argv[2]);
        var bytes = File.ReadAllBytes(path);
        var doc = MapDocument.Parse(bytes);
        var src = MissionScriptFile.Decompile(bytes, argv[2].ToUpperInvariant()).Script;
        string Key(MapObject o) => src.Actors.FirstOrDefault(a => a.Id is { } id && id.AsSpan().SequenceEqual(o.Payload.AsSpan(0, 8)))?.Key ?? "-";
        var inv = CultureInfo.InvariantCulture;
        string P(System.Numerics.Vector3 v) => string.Create(inv, $"({v.X:0},{v.Z:0})");
        var nth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var objects = new List<object>();
        foreach (var o in doc.Objects)
        {
            int n = nth.GetValueOrDefault(o.Name); nth[o.Name] = n + 1;
            if (o.ObjectType is 1 or 2 or 3 or 9 or 11 or 12)
            {
                Console.WriteLine($"obj  type {o.ObjectType,2} {o.Name,-9} nth {n} key {Key(o),-12} at {P(o.Position)}{(o.IsPlayer ? " PLAYER" : "")}");
                objects.Add(new { o.Name, Nth = n, Type = o.ObjectType, Key = Key(o), X = o.Position.X, Z = o.Position.Z, Player = o.IsPlayer });
            }
        }
        var keys = MissionScriptDecompiler.Keys(doc.Paths.Select(p => p.Name));
        var paths = new List<object>();
        for (int i = 0; i < doc.Paths.Count; i++)
        {
            var p = doc.Paths[i];
            Console.WriteLine($"path {keys[i],-12} {p.Points.Count,2} pts {P(p.Points[0])} -> {P(p.Points[^1])}");
            paths.Add(new { Key = keys[i], Points = p.Points.Select(v => new[] { v.X, v.Z }) });
        }
        var msn = new MissionFile(path);
        var roads = msn.RoadSegments().Where(r => r.Points.Count > 0).Select(r =>
        {
            var pts = r.Points.Select(q => q.Raw.Length >= 24 ? new[] { (I76.Formats.Bin.F32(q.Raw, 0) + I76.Formats.Bin.F32(q.Raw, 12)) / 2, (I76.Formats.Bin.F32(q.Raw, 8) + I76.Formats.Bin.F32(q.Raw, 20)) / 2 } : new[] { q.X, q.Z }).ToList();
            var thin = new List<float[]> { pts[0] };
            foreach (var q in pts.Skip(1))
                if (MathF.Abs(q[0] - thin[^1][0]) + MathF.Abs(q[1] - thin[^1][1]) > 100) thin.Add(q);
            if (thin[^1] != pts[^1]) thin.Add(pts[^1]);
            return new { r.Kind, Points = thin };
        }).ToList();
        Console.WriteLine($"roads {roads.Count}, {roads.Sum(r => r.Points.Count)} points thinned");
        int ji = argv.IndexOf("--json");
        if (ji >= 0) File.WriteAllText(argv[ji + 1], JsonSerializer.Serialize(new { Base = argv[2], Objects = objects, Paths = paths, Roads = roads }));
        return 0;
    }
}
