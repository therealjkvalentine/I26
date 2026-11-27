using I76.Formats.Mission;
using I76.Formats.Mission.Edit;
using I76.Formats.Vfs;

namespace I76.Formats.Remix;

/// <summary>
/// The check of a node table against its map (docs/remix-missions.md "Node tables"; i76 remix check --nodes): every place a job
/// may use is on the map's ground, on a road or on drivable ground (no slope steeper than <see cref="MaxSlope"/> within 10 m), a
/// place the player must reach (a start, a node, a cluster) is <see cref="ExitClearance"/> m or more from a leave-map exit, and
/// every place is reachable from the table's first start over drivable ground or roads (a flood over a 10 m grid: the roads are
/// always passable, so a bridge counts). An entry's two points are near each other, a route's points lie on roads, a cluster's
/// structures exist where it says, and the lot is out of every job's way. Reads the install; writes nothing.
/// </summary>
public static class RemixNodeCheck
{
    /// <summary>Degrees: the steepest ground a car is held to climb at a node or on the way to one.</summary>
    public const double MaxSlope = 32;
    /// <summary>Metres from a road's centre line that count as on the road.</summary>
    public const double OnRoad = 22;
    /// <summary>Metres a place the player must reach keeps from a leave-map exit (the library warns from 275 m).</summary>
    public const double ExitClearance = 300;
    private const double Cell = 10;

    /// <summary>The problems found, a line each (empty: the table is sound), and a line per place saying how it stands.</summary>
    public static (List<string> Problems, List<string> Places) Run(GameInstall install, RemixNodes t)
    {
        var bad = new List<string>();
        var info = new List<string>();
        var path = install.FindMission(t.Base) ?? throw new RemixError($"no mission '{t.Base}' in the install");
        var msn = new MissionFile(path);
        var terPath = Assembly.MissionWorld.FindTerrain(install, msn) ?? throw new RemixError($"{t.Base} has no terrain");
        var ter = TerrainTiles.Load(msn, terPath);
        var doc = MapDocument.Parse(File.ReadAllBytes(path));
        // the roads' centre lines, every 10 m
        var road = new List<(double X, double Z)>();
        foreach (var r in msn.RoadSegments())
        {
            var c = r.Points.Select(q => q.Raw.Length >= 24 ? (X: (double)(Bin.F32(q.Raw, 0) + Bin.F32(q.Raw, 12)) / 2, Z: (double)(Bin.F32(q.Raw, 8) + Bin.F32(q.Raw, 20)) / 2) : (X: (double)q.X, Z: (double)q.Z)).ToList();
            for (int i = 0; i + 1 < c.Count; i++)
            {
                double len = Math.Sqrt((c[i + 1].X - c[i].X) * (c[i + 1].X - c[i].X) + (c[i + 1].Z - c[i].Z) * (c[i + 1].Z - c[i].Z));
                int n = Math.Max(1, (int)(len / 10));
                for (int k = 0; k < n; k++) road.Add((c[i].X + (c[i + 1].X - c[i].X) * k / n, c[i].Z + (c[i + 1].Z - c[i].Z) * k / n));
            }
            if (c.Count > 0) road.Add(c[^1]);
        }
        var roadGrid = road.GroupBy(p => ((int)Math.Floor(p.X / 50), (int)Math.Floor(p.Z / 50))).ToDictionary(g => g.Key, g => g.ToList());
        double RoadDist(double x, double z)
        {
            double best = 1e9;
            int gx = (int)Math.Floor(x / 50), gz = (int)Math.Floor(z / 50);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (roadGrid.TryGetValue((gx + dx, gz + dz), out var l))
                        foreach (var p in l) best = Math.Min(best, Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z)));
            return best;
        }
        double? H(double x, double z)
        {
            int sx = (int)Math.Floor(x / MissionFile.WorldScale), sz = (int)Math.Floor(z / MissionFile.WorldScale);
            if (ter.Height(sx, sz) is null || ter.Height(sx + 1, sz) is null || ter.Height(sx, sz + 1) is null || ter.Height(sx + 1, sz + 1) is null) return null;
            return RemixBuilder.GroundAt(ter, (float)x, (float)z);
        }
        // the steepest slope (degrees) between a point and the ground 5 m round it
        double Slope(double x, double z)
        {
            if (H(x, z) is not { } h0) return 90;
            double worst = 0;
            foreach (var (dx, dz) in new[] { (5.0, 0.0), (-5.0, 0.0), (0.0, 5.0), (0.0, -5.0), (3.5, 3.5), (-3.5, 3.5), (3.5, -3.5), (-3.5, -3.5) })
            {
                if (H(x + dx, z + dz) is not { } h) return 90;
                worst = Math.Max(worst, DetMath.Atan2(Math.Abs(h - h0), (double)(5)) * 180 / Math.PI);
            }
            return worst;
        }
        bool Drivable(double x, double z) => RoadDist(x, z) <= OnRoad || (H(x, z) is not null && Slope(x, z) <= MaxSlope);
        var exits = (msn.ReadFsmProgram()?.Paths ?? []).Where(p => p.Name.StartsWith("exit", StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Points.Select(q => (p.Name, X: (double)q.X, Z: (double)q.Z))).ToList();
        (string Name, double D) Exit(double x, double z) =>
            exits.Count == 0 ? ("", 1e9) : exits.Select(e => (e.Name, D: Math.Sqrt((e.X - x) * (e.X - x) + (e.Z - z) * (e.Z - z)))).MinBy(e => e.D);

        // reachable: a flood over a 10 m grid from the first start, through drivable cells and road cells
        if (t.Starts.Count == 0) { bad.Add("no starts"); return (bad, info); }
        var all = Points(t).ToList();
        double minX = all.Min(p => p.At[0]) - 3000, minZ = all.Min(p => p.At[1]) - 3000;
        int w = (int)((all.Max(p => p.At[0]) + 3000 - minX) / Cell), hgt = (int)((all.Max(p => p.At[1]) + 3000 - minZ) / Cell);
        var state = new sbyte[w * hgt];   // 0 unknown, 1 passable, -1 not, 2 reached
        bool Pass(int i, int j)
        {
            ref sbyte s = ref state[j * w + i];
            if (s == 0) { double x = minX + (i + 0.5) * Cell, z = minZ + (j + 0.5) * Cell; s = (sbyte)(Drivable(x, z) ? 1 : -1); }
            return s > 0;
        }
        (int I, int J) CellOf(double[] p) => ((int)((p[0] - minX) / Cell), (int)((p[1] - minZ) / Cell));
        var (si, sj) = CellOf(t.Starts[0].At);
        var queue = new Queue<(int, int)>();
        if (Pass(si, sj)) { state[sj * w + si] = 2; queue.Enqueue((si, sj)); }
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int a = i + di, b = j + dj;
                if (a < 0 || b < 0 || a >= w || b >= hgt || state[b * w + a] == 2) continue;
                if (Pass(a, b)) { state[b * w + a] = 2; queue.Enqueue((a, b)); }
            }
        }
        bool Reached(double[] p)
        {
            var (i, j) = CellOf(p);
            for (int di = -2; di <= 2; di++)
                for (int dj = -2; dj <= 2; dj++)
                    if (i + di >= 0 && j + dj >= 0 && i + di < w && j + dj < hgt && state[(j + dj) * w + i + di] == 2) return true;
            return false;
        }

        foreach (var (what, at, mustReach, clear) in all)
        {
            if (at is not [var x, var z]) { bad.Add($"{what}: a point is [x, z]"); continue; }
            double rd = RoadDist(x, z), sl = Slope(x, z);
            var (ex, ed) = Exit(x, z);
            string how = H(x, z) is null ? "OFF THE MAP" : rd <= OnRoad ? $"on a road ({rd:0} m)" : $"off-road, {rd:0} m from a road, slope {sl:0}";
            info.Add($"{what} ({x:0}, {z:0}): {how}; exit {(ed < 1e8 ? $"{ex} {ed:0} m" : "none")}");
            if (H(x, z) is null) { bad.Add($"{what} ({x:0}, {z:0}): not on the map's ground"); continue; }
            if (rd > OnRoad && sl > MaxSlope) bad.Add($"{what} ({x:0}, {z:0}): off the road on a {sl:0} degree slope");
            if (clear && ed < ExitClearance) bad.Add($"{what} ({x:0}, {z:0}): {ed:0} m from the exit {ex} (the leave-map warning starts at 275 m)");
            if (mustReach && !Reached(at)) bad.Add($"{what} ({x:0}, {z:0}): not reachable from the start {t.Starts[0].Name} over drivable ground");
        }
        foreach (var e in t.Entries)
        {
            if (e.Path is { } stock) { if (!doc.Paths.Any(p => p.Name.Equals(stock, StringComparison.OrdinalIgnoreCase))) bad.Add($"entry {e.Name}: {t.Base} has no path {stock}"); continue; }
            if (e.At.Count != 2) { bad.Add($"entry {e.Name}: two points (where, and the way it faces)"); continue; }
            double d = Math.Sqrt(DetMath.Pow(e.At[1][0] - e.At[0][0], (double)(2)) + DetMath.Pow(e.At[1][1] - e.At[0][1], (double)(2)));
            if (d is < 20 or > 400) bad.Add($"entry {e.Name}: its two points are {d:0} m apart (20-400)");
        }
        foreach (var rt in t.Routes)
        {
            if (rt.Path is { } stock) { if (!doc.Paths.Any(p => p.Name.Equals(stock, StringComparison.OrdinalIgnoreCase))) bad.Add($"route {rt.Name}: {t.Base} has no path {stock}"); continue; }
            if (rt.Points.Count < 2) bad.Add($"route {rt.Name}: two points or more");
            for (int i = 0; i < rt.Points.Count; i++)
                if (rt.Points[i] is [var x, var z] && RoadDist(x, z) > OnRoad * 2) bad.Add($"route {rt.Name} point {i} ({x:0}, {z:0}): {RoadDist(x, z):0} m from a road");
        }
        var nth = doc.Objects.GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in t.Clusters)
        {
            if (c.Objects.Count == 0) bad.Add($"cluster {c.Name}: no structures");
            foreach (var o in c.Objects)
            {
                if (!nth.TryGetValue(o.Object, out var l) || o.Nth < 0 || o.Nth >= l.Count) { bad.Add($"cluster {c.Name}: {t.Base} has no {o.Object} number {o.Nth}"); continue; }
                var p = l[o.Nth].Position;
                double d = Math.Sqrt((p.X - c.At[0]) * (p.X - c.At[0]) + (p.Z - c.At[1]) * (p.Z - c.At[1]));
                if (d > c.Radius * 2) bad.Add($"cluster {c.Name}: {o.Object}/{o.Nth} is {d:0} m from its centre (radius {c.Radius}: at most twice that)");
            }
            foreach (var en in c.Entries) if (!t.Entries.Any(e => e.Name == en)) bad.Add($"cluster {c.Name}: no entry {en}");
        }
        if (t.Lot is [var lx, var lz])
        {
            var near = Points(t).Where(p => p.MustReach && p.At is [_, _]).Select(p => (p.What, D: Math.Sqrt((p.At[0] - lx) * (p.At[0] - lx) + (p.At[1] - lz) * (p.At[1] - lz)))).MinBy(p => p.D);
            if (near.D < 350) bad.Add($"lot ({lx:0}, {lz:0}): {near.D:0} m from {near.What}: new cars wait there hidden, out of every job's way");
            if (H(lx, lz) is null) bad.Add($"lot ({lx:0}, {lz:0}): not on the map's ground");
        }
        else bad.Add("no lot");
        foreach (var r in t.Routes.Where(r => r.Escort && r.Called.Length == 0)) bad.Add($"route {r.Name}: an escort route needs words for where it ends (called)");
        foreach (var c in t.Clusters) foreach (var u in c.Uses) if (u is not ("defend" or "raid")) bad.Add($"cluster {c.Name}: use {u}: defend or raid");
        foreach (var n in t.Neighbours) if (n.Equals(t.Base, StringComparison.OrdinalIgnoreCase)) bad.Add("neighbours: the map itself");
        if (t.Name.Length == 0) bad.Add("no name (the map's words on a run's choice screen)");
        foreach (var c in t.Cast) if (c.Side is not ("gang" or "police")) bad.Add($"cast {c.Name}: side {c.Side}: gang or police");
        foreach (var n in t.Nodes) foreach (var k in n.Kinds) if (!Kinds.Contains(k)) bad.Add($"node {n.Name}: kind {k}: {string.Join(", ", Kinds)}");
        foreach (var n in t.Nodes.Concat<object>(t.Starts)) if ((n as PointNode)?.Called is "" || (n as StartNode)?.Called is "") bad.Add($"{(n is PointNode pn ? "node " + pn.Name : "start " + ((StartNode)n).Name)}: no words for it (called)");
        return (bad, info);
    }

    /// <summary>The kinds a node may serve as.</summary>
    public static readonly string[] Kinds = ["pickup", "home", "hideout", "yard", "start"];

    /// <summary>Every point of a table: what it is, where, whether the player must reach it, whether it keeps clear of the exits.</summary>
    private static IEnumerable<(string What, double[] At, bool MustReach, bool Clear)> Points(RemixNodes t)
    {
        foreach (var s in t.Starts) yield return ($"start {s.Name}", s.At, true, true);
        foreach (var n in t.Nodes) yield return ($"node {n.Name}", n.At, true, true);
        foreach (var c in t.Clusters) yield return ($"cluster {c.Name}", c.At, true, true);
        foreach (var e in t.Entries) for (int i = 0; i < e.At.Count; i++) yield return ($"entry {e.Name} point {i}", e.At[i], true, false);
        foreach (var r in t.Routes)
        {
            for (int i = 0; i < r.Points.Count; i++) yield return ($"route {r.Name} point {i}", r.Points[i], true, false);
            if (r.Park is { } p) yield return ($"route {r.Name} park", p, true, false);
            if (r.Exit is { } x) yield return ($"route {r.Name} exit", x, true, false);
            if (r.Escort && r.Points.Count > 1) { yield return ($"escort route {r.Name} start", r.Points[0], true, true); yield return ($"escort route {r.Name} end", r.Points[^1], true, true); }
        }
    }
}
