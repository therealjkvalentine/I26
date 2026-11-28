using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Sim.Missions;

/// <summary>
/// How near a generated job puts its players to the ways a mission ends by position (docs/remix-missions.md "Boundaries"): the stock
/// leave-the-map routine's exits (a square test on the exit path's first point: the warning at 275 and 175, the mission failed at 50;
/// Nitro 150 and 60) and the map's own edge (the last samples of its tiles, impassable, and past them the ground off the map, where a
/// drop of more than 35 m kills the car). Measured for the start, every co-op seat's spawn, every place the player must reach, the line
/// from the start to the first event, and a wander off that line toward the nearest boundary.
/// </summary>
public static class RemixBounds
{
    /// <summary>The stock leave-map routine's square half-sizes (isWithinSqNav): its first warning, its second, and the fail.</summary>
    public const float Warn = 275, Warn2 = 175, Fail = 50;

    /// <summary>A live exit: the leave-map routine's trigger point (the exit path's first point) and the path's points.</summary>
    public sealed record Exit(string Name, Vector2 At, IReadOnlyList<Vector2> Path);

    /// <summary>
    /// The exits a mission's leave-map routine watches (CoopRoutines.Analyze: the threads that run it on the player), each by its exit
    /// path: the trigger point (the path's first point) and the path. A built job keeps only the threads of the exits it does not drop.
    /// </summary>
    public static List<Exit> Exits(string msnPath, RemixSpec? spec = null)
    {
        var mf = new MissionFile(msnPath);
        if (mf.ReadFsmProgram() is not { Code.Length: > 0 } p) return [];
        var objs = mf.PlacedObjects();
        PlacedObject? Of(int a) => a < 0 || a >= p.Actors.Count ? null
            : objs.LastOrDefault(o => o.Raw.Length >= 8 && o.Raw.AsSpan(0, 8).SequenceEqual(p.Actors[a].InstanceId.AsSpan(0, System.Math.Min(8, p.Actors[a].InstanceId.Length))));
        var r = Fsm.CoopRoutines.Analyze(p, a => Of(a)?.IsPlayer == true, a => Of(a) is { } o ? (int)((o.ObjectFlags >> 16) & 7) : null);
        var o = new List<Exit>();
        foreach (var lm in r.LeaveMap)
        {
            if (lm.PathCell < 0 || lm.PathCell >= p.Data.Length || p.Data[lm.PathCell] is var pi && (pi < 0 || pi >= p.Paths.Count)) continue;
            var path = p.Paths[pi];
            if (path.Points.Count == 0 || o.Any(e => e.Name == path.Name)) continue;
            if (spec is not null && spec.DropExits.Contains(path.Name, StringComparer.OrdinalIgnoreCase)) continue;
            o.Add(new Exit(path.Name, new Vector2(path.Points[0].X, path.Points[0].Z), [.. path.Points.Select(q => new Vector2(q.X, q.Z))]));
        }
        return o;
    }

    /// <summary>The routine's own measure: the larger of the two axis distances to the nearest exit's point (a square, not a circle).</summary>
    public static float ExitDistance(IReadOnlyList<Exit> exits, Vector2 p) =>
        exits.Count == 0 ? float.PositiveInfinity : exits.Min(e => MathF.Max(MathF.Abs(e.At.X - p.X), MathF.Abs(e.At.Y - p.Y)));

    public static Exit? NearestExit(IReadOnlyList<Exit> exits, Vector2 p) =>
        exits.Count == 0 ? null : exits.MinBy(e => MathF.Max(MathF.Abs(e.At.X - p.X), MathF.Abs(e.At.Y - p.Y)));

    /// <summary>The map's edge: a sample with no tile under it (where the ground falls to the plain at 0 off the map).</summary>
    public static bool OffMap(TerrainTiles t, float x, float z)
    {
        int sx = (int)MathF.Floor(0.2f * x + 0.5f), sz = (int)MathF.Floor(0.2f * z + 0.5f);
        return t.Sample(sx, sz) is null;
    }

    /// <summary>Metres to the nearest edge sample (searched on a 5 m grid out to <paramref name="max"/>; max when none is that near).</summary>
    public static float EdgeDistance(TerrainTiles t, Vector2 p, float max = 600)
    {
        if (OffMap(t, p.X, p.Y)) return 0;
        float best = max;
        int n = (int)(max / 5);
        for (int dz = -n; dz <= n; dz++)
            for (int dx = -n; dx <= n; dx++)
            {
                float d = 5 * MathF.Sqrt(dx * dx + dz * dz);
                if (d >= best) continue;
                if (OffMap(t, p.X + 5 * dx, p.Y + 5 * dz)) best = d;
            }
        return best;
    }


    /// <summary>
    /// What a job breaks of the boundaries' rules (docs/remix-missions.md "Boundaries"): the start within <see cref="RemixBoundsTable.PlaceMargin"/>
    /// of an exit (the routine's square), a co-op seat or a place the player must reach inside the first warning, the drive to the first
    /// event through the warning, a 200 m wander off it into the fail, the start or a seat within <see cref="RemixBoundsTable.EdgeMargin"/>
    /// of the map's edge.
    /// </summary>
    public static List<string> Problems(Measure m)
    {
        var o = new List<string>();
        string F(float v) => v.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        if (m.StartExit < RemixBoundsTable.PlaceMargin) o.Add($"the start {F(m.StartExit)} m from an exit (at least {F(RemixBoundsTable.PlaceMargin)})");
        if (m.SeatExit < Warn) o.Add($"a co-op seat of {m.Seats} {F(m.SeatExit)} m from an exit: inside the leave-map warning ({F(Warn)})");
        if (m.PlaceExit < RemixBoundsTable.RouteMargin) o.Add($"{m.PlaceNearest} {F(m.PlaceExit)} m from an exit (at least {F(RemixBoundsTable.RouteMargin)})");
        if (m.LineExit < Warn) o.Add($"the drive to the first event passes {F(m.LineExit)} m from the exit {m.LineExitName}: inside the leave-map warning ({F(Warn)})");
        if (m.DriftExit < Fail) o.Add($"a 200 m wander off the drive to the first event comes {F(m.DriftExit)} m from an exit: the mission fails at {F(Fail)}");
        if (MathF.Min(m.StartEdge, m.SeatEdge) < RemixBoundsTable.EdgeMargin) o.Add($"the start or a seat {F(MathF.Min(m.StartEdge, m.SeatEdge))} m from the map's edge (at least {F(RemixBoundsTable.EdgeMargin)})");
        return o;
    }
    /// <summary>Where the job's first event is: the first step's place, else a convoy's lead, else the start.</summary>
    public static Vector2 FirstPlace(RemixSpec spec)
    {
        for (int k = 1; k <= 2; k++)
            if (RemixCheck.StepPlace(spec, k) is [var x, var z]) return new((float)x, (float)z);
        if (spec.Objective.Convoy is { } c && spec.Cars.TryGetValue(c.Lead, out var lead) && lead.At is [var lx, var lz]) return new((float)lx, (float)lz);
        return spec.Start?.At is [var sx, var sz] ? new((float)sx, (float)sz) : default;
    }

    /// <summary>What a job measures: each kind of position's nearest exit (the routine's square measure) and edge, and what would fire.</summary>
    public sealed record Measure(
        float StartExit, float StartEdge, float SeatExit, float SeatEdge, float PlaceExit, float PlaceEdge, string PlaceNearest,
        float LineExit, float LineEdge, float DriftExit, bool DriftOff, int Seats, string LineExitName = "")
    {
        /// <summary>The smallest exit distance of the start and the seats (what an idle crew sits at).</summary>
        public float IdleExit => MathF.Min(StartExit, SeatExit);
        public float IdleEdge => MathF.Min(StartEdge, SeatEdge);
        /// <summary>Something position-based fires: idle (the start or a seat inside the warning or the fail), on the line, or on a wander.</summary>
        public bool IdleWarns => IdleExit < Warn;
        public bool IdleFails => IdleExit < Fail || IdleEdge <= 0;
        public bool LineWarns => LineExit < Warn;
        public bool LineFails => LineExit < Fail || LineEdge <= 0;
        public bool DriftFails => DriftExit < Fail || DriftOff;
        public bool AtRisk => IdleWarns || IdleFails || LineWarns || LineFails || DriftFails || PlaceExit < Warn;
        public override string ToString() => System.FormattableString.Invariant(
            $"start {StartExit:0}/{StartEdge:0} seats({Seats}) {SeatExit:0}/{SeatEdge:0} places {PlaceExit:0}/{PlaceEdge:0} ({PlaceNearest}) line {LineExit:0}/{LineEdge:0} drift {DriftExit:0}{(DriftOff ? " off-map" : "")} (exit square m / edge m)");
    }

    /// <summary>
    /// Measures a built job (<paramref name="mission"/> found in the generated missions' folder): the start, the seats of a co-op crew of
    /// <paramref name="seats"/> (their formation spawn, built as the server builds it), every place the player must reach, the line from
    /// the start to the first event (its first 1.3 km: a minute at 22 m/s), and from each point of it a wander of 100, 150 and 200 m toward
    /// the nearest exit and toward the nearest edge.
    /// </summary>
    public static Measure Of(AssetVfs vfs, ModelAssembler asm, string mission, RemixSpec spec, int seats = 1, RemixPacing.Roads? roads = null)
    {
        var path = vfs.Install.FindMission(mission) ?? throw new FileNotFoundException(mission);
        var world = new MissionWorld(vfs, asm, path);
        var t = world.Terrain;
        var exits = Exits(path, spec);
        var start = spec.Start?.At is [var sx, var sz] ? new Vector2((float)sx, (float)sz) : default;
        float seatExit = float.PositiveInfinity, seatEdge = float.PositiveInfinity;
        // the crew as the mission builds it (one: the player's car; more: a co-op formation); a description that moves no start (a shipped
        // scenario on its stock start) is measured from where the build puts the player
        var coop = seats > 1 ? new CoopSetup(Enumerable.Repeat<string?>(null, seats).ToList()) { RespawnDelay = 1e6f } : null;
        var setup = MissionSetup.Build(vfs, world, asm, "vppirna1.vcf", rand: new Rng.CrtRand(1), coop: coop);
        var humans = coop is null ? (setup.World.Player is { } pl ? [pl] : []) : setup.Humans.ToList();
        if (spec.Start is null && humans.Count > 0) start = new Vector2((float)humans[0].Pos.X, (float)humans[0].Pos.Z);
        if (seats > 1)
            foreach (var h in humans)
            {
                var p = new Vector2((float)h.Pos.X, (float)h.Pos.Z);
                seatExit = MathF.Min(seatExit, ExitDistance(exits, p));
                seatEdge = MathF.Min(seatEdge, EdgeDistance(t, p));
            }
        float placeExit = float.PositiveInfinity, placeEdge = float.PositiveInfinity;
        string nearest = "";
        foreach (var (label, at) in RemixCheck.PlayerPlaces(spec))
        {
            if (label == "START") continue;
            var p = new Vector2((float)at[0], (float)at[1]);
            float e = ExitDistance(exits, p);
            if (e < placeExit) { placeExit = e; nearest = label; }
            placeEdge = MathF.Min(placeEdge, EdgeDistance(t, p));
        }
        var first = FirstPlace(spec);
        float lineExit = float.PositiveInfinity, lineEdge = float.PositiveInfinity, driftExit = float.PositiveInfinity;
        string lineName = "";
        bool driftOff = false;
        // the natural line: the drive to the first event along the roads where that is quicker (RemixPacing's), its first 1.3 km
        roads ??= RemixPacing.Roads.From(new MissionFile(path));
        var line = roads.Line([start.X, start.Y], [first.X, first.Y]).Select(q => new Vector2((float)q[0], (float)q[1])).ToList();
        float walked = 0;
        for (int i = 0; i < line.Count && walked <= 1300; i++)
        {
            var a = line[i];
            var b = i + 1 < line.Count ? line[i + 1] : a;
            float seg = Vector2.Distance(a, b);
            for (float s = 0; s <= seg && walked + s <= 1300; s += 25)
            {
                var p = seg < 1 ? a : a + (b - a) * (s / seg);
                if (ExitDistance(exits, p) < lineExit) { lineExit = ExitDistance(exits, p); lineName = NearestExit(exits, p)?.Name ?? ""; }
                float edge = EdgeDistance(t, p, 250);
                lineEdge = MathF.Min(lineEdge, edge);
                foreach (float d in new[] { 100f, 150f, 200f })
                {
                    if (NearestExit(exits, p) is { } ex && Vector2.Distance(ex.At, p) > 1)
                    {
                        var q = p + Vector2.Normalize(ex.At - p) * MathF.Min(d, Vector2.Distance(ex.At, p));
                        driftExit = MathF.Min(driftExit, ExitDistance(exits, q));
                    }
                    // toward the edge: there only if the wander is longer than the way to it
                    if (edge < d) driftOff = true;
                }
                if (seg < 1) break;
            }
            walked += seg;
        }
        return new Measure(ExitDistance(exits, start), EdgeDistance(t, start), seatExit, seatEdge, placeExit, placeEdge, nearest,
            lineExit, lineEdge, driftExit, driftOff, seats, lineName);
    }
}

/// <summary>The margins a node table keeps from the map's boundaries (docs/remix-missions.md "Boundaries"), and its check.</summary>
public static class RemixBoundsTable
{
    /// <summary>
    /// A place a job may start at or send the player to (a start, a node, a cluster, an escort route's ends): this far from every exit's
    /// trigger point on the routine's own square measure. The first warning is at 275; 75 more holds a co-op crew's formation (its
    /// farthest seat of eight stands about 45 m from the lead) and a turn on the spot outside it.
    /// </summary>
    public const float PlaceMargin = 350;
    /// <summary>A convoy's road (each point, its parking and its end): this far, so a chase along it, and a 200 m wander off it, is never failed.</summary>
    public const float RouteMargin = 300;
    /// <summary>A start or place from the end of the map's tiles (where a drive off a high edge is a fatal drop).</summary>
    public const float EdgeMargin = 100;

    /// <summary>Every point of a table that breaks a margin: what it is, where, its distance to the nearest exit and to the map's edge.</summary>
    public static List<string> Check(GameInstall install, RemixNodes t)
    {
        var bad = new List<string>();
        if (install.FindMission(t.Base) is not { } path) return [$"{t.Base}: no mission"];
        var msn = new MissionFile(path);
        var exits = RemixBounds.Exits(path);
        var ter = MissionWorld.FindTerrain(install, msn);
        var tiles = ter is null ? null : TerrainTiles.Load(msn, ter);
        var paths = msn.ReadFsmProgram()?.Paths ?? [];
        void Hold(string what, double[] at, float margin, bool edge)
        {
            if (at is not [var x, var z]) return;
            var p = new Vector2((float)x, (float)z);
            float d = RemixBounds.ExitDistance(exits, p);
            if (d < margin) bad.Add(System.FormattableString.Invariant($"{what} ({x:0}, {z:0}): {d:0} m from the exit {RemixBounds.NearestExit(exits, p)?.Name} on the leave-map routine's square (at least {margin:0})"));
            if (edge && tiles is not null && RemixBounds.EdgeDistance(tiles, p, EdgeMargin) < EdgeMargin)
                bad.Add(System.FormattableString.Invariant($"{what} ({x:0}, {z:0}): within {EdgeMargin:0} m of the map's edge"));
        }
        foreach (var s in t.Starts) Hold($"start {s.Name}", s.At, PlaceMargin, true);
        foreach (var n in t.Nodes) Hold($"node {n.Name}", n.At, PlaceMargin, true);
        foreach (var c in t.Clusters) Hold($"cluster {c.Name}", c.At, PlaceMargin, true);
        foreach (var r in t.Routes)
        {
            var pts = r.Points.Count > 0 ? r.Points
                : paths.FirstOrDefault(p => p.Name.Equals(r.Path, StringComparison.OrdinalIgnoreCase)) is { } sp ? [.. sp.Points.Select(q => new double[] { q.X, q.Z })] : [];
            for (int i = 0; i < pts.Count; i++) Hold($"route {r.Name} point {i}", pts[i], r.Escort && (i == 0 || i == pts.Count - 1) ? PlaceMargin : RouteMargin, false);
            if (r.Park is { } pk) Hold($"route {r.Name} park", pk, RouteMargin, false);
            if (r.Exit is { } ex) Hold($"route {r.Name} exit", ex, RouteMargin, false);
        }
        return bad;
    }
}
