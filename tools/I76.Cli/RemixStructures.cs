using System.Globalization;
using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 remix structures BASE [--near X,Z,R] [--min M]: the structures a stock map places, as the sim builds them (each placement's
/// name and copy, its ODEF type, its collision's footprint in the world, and each node's name, type and footprint), for finding
/// places a car can be inside or beside (docs/remix-missions.md "Places"): a barn's floor, a fort's gate nodes, a hangar's mouth.
/// Reads the install; writes nothing.
/// </summary>
public static class RemixStructures
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string n) => argv.IndexOf(n) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        var vfs = new AssetVfs(install);
        var asm = new ModelAssembler(vfs);
        var path = install.FindMission(argv[2]) ?? throw new FileNotFoundException(argv[2]);
        var setup = I76.Sim.Missions.MissionSetup.Build(vfs, new MissionWorld(vfs, asm, path), asm, "vppirna1.vcf", rand: new I76.Sim.Rng.CrtRand(1));
        float[]? near = Opt("--near")?.Split(',').Select(x => float.Parse(x, inv)).ToArray();
        float min = Opt("--min") is { } m ? float.Parse(m, inv) : 0;
        var nth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in setup.Statics.Objects)
        {
            int k = nth.GetValueOrDefault(o.Name);
            nth[o.Name] = k + 1;
            static IEnumerable<Vector3> Corners(I76.Sim.Collision.NodeBox b)
            {
                for (int c = 0; c < 8; c++)
                    yield return b.Origin + b.Rot.ToWorld(new Vector3((c & 1) != 0 ? b.Max.X : b.Min.X, (c & 2) != 0 ? b.Max.Y : b.Min.Y, (c & 4) != 0 ? b.Max.Z : b.Min.Z));
            }
            var pts = o.Polys.SelectMany(p => p.V).Concat(o.Boxes.SelectMany(Corners)).Concat(o.MeshBounds.SelectMany(Corners)).ToList();
            if (pts.Count == 0) continue;
            float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
            if (System.Math.Max(x1 - x0, z1 - z0) < min) continue;
            if (near is [var nx, var nz, var nr] && (System.Math.Abs(o.Origin.X - nx) > nr || System.Math.Abs(o.Origin.Z - nz) > nr)) continue;
            Console.WriteLine(string.Create(inv, $"{o.Name} nth {k} type {o.Type} at ({o.Origin.X:0},{o.Origin.Z:0}) x {x0:0}..{x1:0} z {z0:0}..{z1:0} y {y0:0}..{y1:0} ({x1 - x0:0} x {z1 - z0:0} x {y1 - y0:0} m) nodes {o.Nodes.Count}"));
            if (!argv.Contains("--nodes")) continue;
            for (int i = 0; i < o.Nodes.Count; i++)
            {
                var n = o.Nodes[i];
                var q = o.Polys.Where(p => p.Node == i).SelectMany(p => p.V).Concat(o.Boxes.Where(b => b.Node == i).SelectMany(Corners)).Concat(o.MeshBounds.Where(b => b.Node == i).SelectMany(Corners)).ToList();
                string fp = q.Count == 0 ? "no polys" : string.Create(inv, $"x {q.Min(p => p.X):0.0}..{q.Max(p => p.X):0.0} z {q.Min(p => p.Z):0.0}..{q.Max(p => p.Z):0.0} y {q.Min(p => p.Y):0.0}..{q.Max(p => p.Y):0.0} ({o.Polys.Count(p => p.Node == i)} polys, {o.Boxes.Count(b => b.Node == i)} boxes, {o.MeshBounds.Count(b => b.Node == i)} meshes)");
                Console.WriteLine($"    node {i} {n.Name} type {n.Type} flags 0x{n.Flags:x} parent {n.Parent} hp {n.MaxHp}{(n.Gate is not null ? " GATE" : "")}: {fp}");
            }
            int root = o.Polys.Count(p => p.Node < 0);
            if (root > 0) Console.WriteLine($"    root polys {root}");
        }
        return 0;
    }
}

/// <summary>
/// i76 remix driveout SPEC.yaml X,Z [SECONDS]: the description built and its player's car driven from its start toward the way out
/// (RemixDriveOut), the track printed: whether a start inside a building can be driven out of.
/// </summary>
public static class RemixDriveOutCommand
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        var spec = I76.Formats.Remix.RemixSpec.Load(argv[2]);
        var at = argv[3].Split(',').Select(x => double.Parse(x, inv)).ToArray();
        float secs = argv.Count > 4 ? float.Parse(argv[4], inv) : 14;
        string outDir = Path.Combine(Path.GetTempPath(), "i26-remix-driveout-" + Environment.ProcessId);
        GameInstall.UserMissionDir = outDir;
        I76.Formats.Remix.RemixBuilder.Build(install, spec).WriteTo(outDir);
        var vfs = new AssetVfs(install);
        var r = I76.Sim.Missions.RemixDriveOut.Run(vfs, new ModelAssembler(vfs), spec.Id, at, secs);
        Console.WriteLine(r);
        return r.Passed ? 0 : 1;
    }
}

/// <summary>
/// i76 remix roadsurvey BASE: candidates for the open-air places (docs/remix-missions.md "Places"): the highest road points
/// (overlooks, mountain roads: 300 m apart, with their height over the map's median road), and the road ends no other road
/// meets (dead-end spurs), with their distance from the nearest leave-map exit.
/// </summary>
public static class RemixRoadSurvey
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var path = install.FindMission(argv[2]) ?? throw new FileNotFoundException(argv[2]);
        var msn = new I76.Formats.Mission.MissionFile(path);
        var ter = I76.Formats.Mission.TerrainTiles.Load(msn, I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn)!);
        var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Points).ToList();
        double Exit(double x, double z) => exits.Count == 0 ? 1e9 : exits.Min(e => Math.Sqrt((e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z)));
        var segs = msn.RoadSegments().Select(s => (s.Kind, Pts: s.Points.Select(q => q.Raw.Length >= 24 ? ((double)(I76.Formats.Bin.F32(q.Raw, 0) + I76.Formats.Bin.F32(q.Raw, 12)) / 2, (double)(I76.Formats.Bin.F32(q.Raw, 8) + I76.Formats.Bin.F32(q.Raw, 20)) / 2) : ((double)q.X, (double)q.Z)).ToList())).ToList();
        var all = segs.SelectMany(s => s.Pts.Select(p => (s.Kind, X: p.Item1, Z: p.Item2, H: (double)I76.Formats.Remix.RemixBuilder.GroundAt(ter, (float)p.Item1, (float)p.Item2)))).ToList();
        double med = all.Select(a => a.H).Order().ElementAt(all.Count / 2);
        Console.WriteLine($"{argv[2]}: {segs.Count} roads, {all.Count} points, median road height {med:0.0}");
        var picked = new List<(double X, double Z)>();
        foreach (var a in all.OrderByDescending(a => a.H))
        {
            if (picked.Count >= 6) break;
            if (picked.Any(p => Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Z - a.Z) * (p.Z - a.Z)) < 300) || Exit(a.X, a.Z) < 300) continue;
            picked.Add((a.X, a.Z));
            Console.WriteLine($"  high ({a.X:0}, {a.Z:0}) kind {a.Kind % 3} height {a.H:0.0} (+{a.H - med:0.0} over the median road); exit {Exit(a.X, a.Z):0} m");
        }
        foreach (var s in segs)
            foreach (var e in new[] { s.Pts[0], s.Pts[^1] })
            {
                bool met = segs.Any(o => !ReferenceEquals(o.Pts, s.Pts) && o.Pts.Any(q => Math.Sqrt((q.Item1 - e.Item1) * (q.Item1 - e.Item1) + (q.Item2 - e.Item2) * (q.Item2 - e.Item2)) < 40));
                if (!met && Exit(e.Item1, e.Item2) > 300) Console.WriteLine($"  end ({e.Item1:0}, {e.Item2:0}) kind {s.Kind % 3}; exit {Exit(e.Item1, e.Item2):0} m");
            }
        return 0;
    }
}

/// <summary>
/// i76 remix mapsketch (--seed N | --run N --picks P | SPEC.yaml) OUT.png [--scale 2]: a job's map as the game draws it, without
/// the lettering (that is the game's font): the whole map's sketch on the sheet (ArenaSketch.Whole: roads by kind), each mark
/// ringed, each landmark a small square. For checking a job's map headless.
/// </summary>
public static class RemixMapSketch
{
    public static int Run(GameInstall install, List<string> argv)
    {
        var inv = CultureInfo.InvariantCulture;
        string? Opt(string n) => argv.IndexOf(n) is var i and >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
        I76.Formats.Remix.RemixSpec spec;
        if (Opt("--seed") is { } sd) spec = I76.Formats.Remix.RemixDirector.Roll(int.Parse(sd, inv), new I76.Formats.Remix.RemixDirector.JobAsk { Id = "xmap", Template = Opt("--template"), Map = Opt("--map") });
        else if (Opt("--run") is { } rn) spec = I76.Formats.Remix.RemixRun.Walk(int.Parse(rn, inv), Opt("--picks") ?? "0").Spec;
        else spec = I76.Formats.Remix.RemixSpec.Load(argv[2]);
        string outPng = argv.Last(a => a.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        int scale = Opt("--scale") is { } sc ? int.Parse(sc, inv) : 2;
        string dir = Path.Combine(Path.GetTempPath(), "i26-remix-map-" + Environment.ProcessId);
        GameInstall.UserMissionDir = dir;
        var built = I76.Formats.Remix.RemixBuilder.Build(install, spec);
        built.WriteTo(dir);
        var marks = I76.Formats.Remix.RemixMarks.Parse(built.Marks ?? "");
        var path = install.FindMission(spec.Id)!;
        var msn = new I76.Formats.Mission.MissionFile(path);
        var ter = I76.Formats.Mission.TerrainTiles.Load(msn, MissionWorld.FindTerrain(install, msn)!);
        var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase)).SelectMany(p => p.Points).Select(p => new System.Numerics.Vector2(p.X, p.Z)).ToList();
        var sk = I76.Formats.Mission.ArenaSketch.Whole(msn, ter, [.. marks.Points.Select(p => p.At)], [.. marks.Lines.Select(l => l.Points)], exits, 560, 392)!;
        var ink = sk.Render(scale, aliased: scale == 1);
        int W = 640 * scale, H = 480 * scale;
        var px = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { px[i * 4] = 232; px[i * 4 + 1] = 222; px[i * 4 + 2] = 196; px[i * 4 + 3] = 255; }
        int bw = 560 * scale, bh = 392 * scale, ox = 40 * scale, oy = 56 * scale;
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
            {
                int s = (y * bw + x) * 4, d = ((oy + y) * W + ox + x) * 4;
                float a = ink[s + 3] / 255f;
                for (int c = 0; c < 3; c++) px[d + c] = (byte)(px[d + c] * (1 - a) + ink[s + c] * a);
            }
        void Dot(System.Numerics.Vector2 world, int r, bool ring, (byte, byte, byte) col)
        {
            var u = sk.InBox(world);
            int cx = ox + (int)(u.X * bw), cy = oy + (int)(u.Y * bh);
            for (int y = -r - 2; y <= r + 2; y++)
                for (int x = -r - 2; x <= r + 2; x++)
                {
                    double dd = Math.Sqrt(x * x + y * y);
                    bool on = ring ? Math.Abs(dd - r) < 1.2 * scale : Math.Max(Math.Abs(x), Math.Abs(y)) <= r;
                    int X = cx + x, Y = cy + y;
                    if (!on || X < 0 || Y < 0 || X >= W || Y >= H) continue;
                    int d = (Y * W + X) * 4;
                    px[d] = col.Item1; px[d + 1] = col.Item2; px[d + 2] = col.Item3;
                }
        }
        foreach (var (l, at) in marks.Points) Dot(at, 9 * scale, true, (40, 30, 18));
        foreach (var (l, at) in marks.Landmarks) Dot(at, 3 * scale, false, (92, 58, 26));
        File.WriteAllBytes(outPng, I76.Formats.Export.PngWriter.Encode(W, H, px, true));
        Console.WriteLine($"{spec.Base} {spec.Objective.Template}: marks {string.Join(", ", marks.Points.Select(p => p.Label))}; landmarks {string.Join(", ", marks.Landmarks.Select(p => p.Label))}; steps {string.Join(", ", marks.Steps.Select(s => s.Objective))}; wrote {outPng}");
        return 0;
    }
}
