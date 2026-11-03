using I76.Formats.Mission;

namespace I76.Formats.Assembly;

/// <summary>
/// The boundary walls of a map, for Graphic Detail's Map Edge at Hide (modern look; drawing only, the heightfield and
/// its collision are unchanged). A level's walls are its own heightfield: measured over the 114 stock and Nitro maps,
/// the cells steeper than 60° are thin terrace steps of 5-40 m (one to three samples wide) and behind them a gentle
/// plateau runs on to the tiles' edge, which is often 400 m or more past the crest; only 11% of the steep cells lie
/// within 50 m of the edge, and the sample's no-go flag marks interior cliffs as well. What tells the walls from the
/// mesas and canyons inside is reachability: the walls, and the plateau behind them, are ground no car reaches that is
/// joined to the map's edge without crossing drivable ground (<see cref="ITerrainSurface.Open"/>: the cells gentler
/// than 36° flood-filled from the cars, roads and script paths); a cliff enclosed by drivable ground is scenery and
/// stays, and so does a ridge between two drivable regions (it joins the walls somewhere, but looking straight away
/// from its nearest drivable ground there is drivable ground again within 600 m, where a wall has the plateau or the edge),
/// unless it is a thin barrier: no more than 250 m from one side's drivable ground to the other's, carrying the walls' cliffs
/// (steps of 15 m or more in a sample) and with its crest 10 m or more over the drivable ground nearest it, the fences of
/// the terraces that line roads and split a map's valleys (T05's either side of its start; over the stock maps they are
/// most of what the ridge test kept, and the rest of it outer walls with a valley beyond), which goes with the walls,
/// unless a road runs at its foot (<see cref="RoadShare"/> of it within <see cref="RoadNear"/> of a road: T05's mountain road,
/// cut along the ridge south of its start 100 m up, against T05's start road, 30-95 m off its fences), from which it is seen
/// close up: such a barrier stays.
/// Over that ground the drawn surface is held down to the plain at its foot: each sample's foot is the nearest
/// drivable sample's height, blurred away from the drivable ground so the plain runs level, and the ground is drawn at
/// the lower of its own height and the foot, so land already below the foot (a drop beyond a crest) keeps its shape. Where
/// hidden ground meets a kept ridge (one plateau, cut by the test) the clamp fades in over <see cref="Ramp"/> samples, so the
/// plain rises to the ridge on a slope rather than ending in a cliff as high as it.
/// </summary>
public sealed class EdgeWalls
{
    /// <summary>The sample grid's low corner and size (sample coordinates), the tiles' bounds.</summary>
    public int X0 { get; }
    public int Z0 { get; }
    public int W { get; }
    public int H { get; }
    /// <summary>Per sample: the plain's height, metres.</summary>
    public float[] Foot { get; }
    /// <summary>Per sample: on the walls or the ground behind them (no drivable cell about it, an outer cell about it, not a ridge).</summary>
    public bool[] Hidden { get; }
    /// <summary>Per sample: how far the drawn ground goes to the foot, 1 over hidden ground, fading to 0 over <see cref="Ramp"/> samples toward kept outer ground, 0 elsewhere.</summary>
    public float[] Mask { get; }
    /// <summary>Per sample: outer ground kept as a ridge (drivable ground on both sides).</summary>
    public bool[] Ridge { get; }
    /// <summary>Samples hidden.</summary>
    public int HiddenCount { get; }
    /// <summary>Per sample: hidden as a thin barrier (see Build); null when Build was told not to look for them.</summary>
    public bool[]? Thin { get; private init; }
    /// <summary>Samples hidden as thin barriers.</summary>
    public int ThinCount { get; private init; }
    /// <summary>Per sample: a thin barrier kept for a road at its foot (see Build); null when Build was given no roads.</summary>
    public bool[]? RoadKept { get; private init; }
    /// <summary>Samples of thin barriers kept for a road at their foot.</summary>
    public int RoadKeptCount { get; private init; }

    /// <summary>The blur's radius in samples (three box passes: a bell about 4.5 samples wide), and how far from the drivable ground the blurred foot takes over.</summary>
    public const int BlurRadius = 4;
    public const float Blend = 4f;
    /// <summary>The ridge test's rays: the circle's division (the two side rays are a sixteenth off the one straight away), how far they look (samples: 600 m) and their step.</summary>
    public const int Rays = 16, Reach = 120, RayStep = 2;
    /// <summary>Samples over which the clamp fades in from kept outer ground (200 m).</summary>
    public const int Ramp = 40;
    /// <summary>A ridge is at least this many samples wide (45 m): a spur of wall between two arms of the floor is not one.</summary>
    public const int RidgeWidth = 9;
    /// <summary>
    /// A thin barrier (see Build): a kept ridge whose way across, from the drivable ground on one side to the other's, is at
    /// most this many samples (250 m) at the median of its samples, and at least <see cref="CliffShare"/> of whose samples
    /// stand on a step of <see cref="CliffStep"/> metres or more to the next sample (steeper than 70°).
    /// </summary>
    public const int ThinAcross = 50;
    public const float CliffStep = 15f, CliffShare = 0.03f;
    /// <summary>A thin barrier's crest (the 95th percentile of its heights) stands at least this far above the drivable ground about it, metres.</summary>
    public const float TopClear = 10f;
    /// <summary>Samples of other ground across which two scraps of ridge are judged as one (15 m).</summary>
    public const int ThinJoin = 3;
    /// <summary>
    /// A road runs at a thin barrier's foot when at least <see cref="RoadShare"/> of its samples, and <see cref="RoadAlong"/> or
    /// more, lie within <see cref="RoadNear"/> samples (30 m) of a road. Over the stock maps the share is 0.11 and 0.14 for T05's
    /// mountain road and 0.03-0.04 for T05's start road against its fences.
    /// </summary>
    public const int RoadNear = 6, RoadAlong = 20;
    public const float RoadShare = 0.06f;

    private EdgeWalls(int x0, int z0, int w, int h, float[] foot, bool[] hidden, float[] mask, bool[] ridge, int count)
    {
        (X0, Z0, W, H, Foot, Hidden, Mask, Ridge, HiddenCount) = (x0, z0, w, h, foot, hidden, mask, ridge, count);
    }

    /// <summary>
    /// The walls of <paramref name="tiles"/> as <paramref name="surface"/> draws them; null for a map with no tiles. With
    /// <paramref name="keepRidges"/> off, a ridge between two drivable regions is hidden like a wall (for comparison).
    /// <paramref name="roads"/>: the map's roads as polylines in sample coordinates (<see cref="Roads"/>), for the thin barriers
    /// with a road at their foot; null, none is kept for a road.
    /// </summary>
    public static EdgeWalls? Build(TerrainTiles tiles, ITerrainSurface surface, bool keepRidges = true, bool thinBarriers = true, IEnumerable<IReadOnlyList<System.Numerics.Vector2>>? roads = null)
    {
        var (r0, r1, c0, c1) = tiles.Bounds();
        if (r1 < r0) return null;
        const int T = TerrainTiles.Tile;
        int x0 = c0 * T, z0 = r0 * T, w = (c1 - c0 + 1) * T, h = (r1 - r0 + 1) * T, n = w * h;
        var height = new float[n];
        var present = new bool[n];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (tiles.Sample(x0 + x, z0 + z) is { } v) { present[z * w + x] = true; height[z * w + x] = (v & MissionFile.HeightMask) * (float)MissionWorld.HeightScale; }
        // cells (indexed like their low-corner sample; the last row and column unused): with a tile under all four corners, and drivable
        int cw = w - 1, ch = h - 1;
        var cell = new bool[n];
        var open = new bool[n];
        for (int z = 0; z < ch; z++)
            for (int x = 0; x < cw; x++)
            {
                int i = z * w + x;
                cell[i] = present[i] && present[i + 1] && present[i + w] && present[i + w + 1];
                open[i] = cell[i] && surface.Open(x0 + x, z0 + z);
            }
        // the outer ground: cells no car reaches, joined four-ways to the edge (a side with no tile beyond it, or the bounds)
        var outer = new bool[n];
        var queue = new Queue<int>();
        for (int z = 0; z < ch; z++)
            for (int x = 0; x < cw; x++)
            {
                int i = z * w + x;
                if (!cell[i] || open[i]) continue;
                bool edge = x == 0 || z == 0 || x == cw - 1 || z == ch - 1 || !cell[i - 1] || !cell[i + 1] || !cell[i - w] || !cell[i + w];
                if (edge) { outer[i] = true; queue.Enqueue(i); }
            }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, z = i / w;
            void Visit(int j) { if (cell[j] && !open[j] && !outer[j]) { outer[j] = true; queue.Enqueue(j); } }
            if (x > 0) Visit(i - 1);
            if (x < cw - 1) Visit(i + 1);
            if (z > 0) Visit(i - w);
            if (z < ch - 1) Visit(i + w);
        }
        // samples: an anchor is a corner of a drivable cell; hidden ones have no anchor's cell about them and an outer cell about them
        var anchor = new bool[n];
        var hidden = new bool[n];
        for (int z = 0; z < ch; z++)
            for (int x = 0; x < cw; x++)
            {
                int i = z * w + x;
                if (open[i]) anchor[i] = anchor[i + 1] = anchor[i + w] = anchor[i + w + 1] = true;
            }
        for (int z = 0; z < ch; z++)
            for (int x = 0; x < cw; x++)
            {
                int i = z * w + x;
                if (!outer[i]) continue;
                foreach (int j in (ReadOnlySpan<int>)[i, i + 1, i + w, i + w + 1])
                    if (!anchor[j]) hidden[j] = true;
            }
        // the foot: the nearest anchor's height (a breadth-first fill, eight ways), and how far it is in steps
        var foot = new float[n];
        var nearest = new int[n];
        if (!anchor.Any(a => a)) return new EdgeWalls(x0, z0, w, h, height, new bool[n], new float[n], new bool[n], 0);   // nowhere to drive: nothing to hide
        var dist = Steps(anchor, w, h, (from, to) => { foot[to] = foot[from]; nearest[to] = nearest[from]; }, i => { foot[i] = height[i]; nearest[i] = i; });
        // a ridge between two drivable regions touches the outer walls somewhere, so it is outer ground too, but it is scenery
        // seen from both sides, not a wall: looking straight away from its nearest drivable ground (and a little to either side), a
        // ridge's sample finds drivable ground again within Reach; a wall's finds the plateau, or the edge
        var wall = new bool[n];
        var across = new float[n];   // a ridge sample's way across: to its nearest drivable ground and on to the far side's, samples
        Parallel.For(0, h, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (!hidden[i]) continue;
                wall[i] = true;
                if (!keepRidges) continue;
                double dx = x - nearest[i] % w, dz = z - nearest[i] / w, len = Math.Sqrt(dx * dx + dz * dz);
                if (len == 0) continue;
                double away = Math.Atan2(dz, dx);
                for (int r = -1; r <= 1 && wall[i]; r++)
                {
                    double a = away + r * Math.Tau / Rays, cs = Math.Cos(a), sn = Math.Sin(a);
                    for (int t = RayStep; t <= Reach; t += RayStep)
                    {
                        int xx = x + (int)Math.Round(cs * t), zz = z + (int)Math.Round(sn * t);
                        if (xx < 0 || zz < 0 || xx >= w || zz >= h || !present[zz * w + xx]) break;
                        if (anchor[zz * w + xx]) { wall[i] = false; across[i] = (float)(len + t); break; }
                    }
                }
            }
        });
        // a lone decision either way is noise: the majority over the 5x5 about it, and a ridge is a band at least RidgeWidth
        // wide, not specks or spurs along a wall's foot (a sample in a notch of the floor looks back onto it): the mask is opened
        var ridge = new bool[n];   // outer ground kept as a ridge
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                if (!hidden[i]) continue;
                int yes = 0, all = 0;
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= w || zz >= h || !hidden[zz * w + xx]) continue;
                        all++; if (wall[zz * w + xx]) yes++;
                    }
                ridge[i] = 2 * yes < all;
            }
        ridge = Grown(Eroded(ridge, w, h, RidgeWidth / 2), w, h, RidgeWidth / 2);
        // a thin barrier is a ridge only by the test's letter: a band no wider than ThinAcross from one drivable side to the
        // other that carries the cliffs of the walls (steps of CliffStep in one sample, the terraces' fences), not a hill or a
        // massif between two valleys; it goes with the walls
        bool[]? road = null;
        if (roads is not null)
        {
            road = new bool[n];
            foreach (var line in roads)
                for (int k = 0; k + 1 < line.Count; k++)
                {
                    var a = line[k];
                    var b = line[k + 1];
                    int steps = Math.Max(1, (int)Math.Ceiling(System.Numerics.Vector2.Distance(a, b) * 2));
                    for (int s = 0; s <= steps; s++)
                    {
                        var p = System.Numerics.Vector2.Lerp(a, b, s / (float)steps);
                        int xx = (int)MathF.Floor(p.X) - x0, zz = (int)MathF.Floor(p.Y) - z0;
                        if (xx >= 0 && zz >= 0 && xx < w && zz < h) road[zz * w + xx] = true;
                    }
                }
        }
        var roadKept = road is not null && thinBarriers ? new bool[n] : null;
        var thin = thinBarriers ? ThinBarriers(ridge, across, height, present, foot, w, h, road, roadKept) : null;
        if (thin is not null)
            for (int i = 0; i < n; i++)
            {
                if (!thin[i]) continue;
                ridge[i] = false;
                if (!hidden[i]) thin[i] = false;
            }
        for (int i = 0; i < n; i++) if (ridge[i]) hidden[i] = false;
        // where the hidden ground meets a ridge that is kept (the same plateau, cut by the test) the plain would end in a
        // cliff as high as the ridge: the clamp fades in over Ramp samples from kept outer ground, so it rises to the ridge on a slope
        var toRidge = Chamfer(ridge, w, h);
        var mask = new float[n];
        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (!hidden[i]) continue;
            float t = Math.Clamp(toRidge[i] / (3f * Ramp), 0, 1);
            mask[i] = t * t * (3 - 2 * t);
            if (mask[i] > 0) count++; else hidden[i] = false;
        }
        // blurred away from the anchors, so the plain runs level where the nearest anchor changes
        var blurred = (float[])foot.Clone();
        var tmp = new float[n];
        for (int pass = 0; pass < 3; pass++) { BoxRows(blurred, tmp, w, h); BoxColumns(tmp, blurred, w, h); }
        for (int i = 0; i < n; i++)
        {
            float t = Math.Clamp(dist[i] / Blend, 0, 1);
            foot[i] += (blurred[i] - foot[i]) * t * t * (3 - 2 * t);
        }
        int thinCount = 0, roadCount = 0;
        if (thin is not null) for (int i = 0; i < n; i++) if (thin[i] &= hidden[i]) thinCount++;
        if (roadKept is not null) for (int i = 0; i < n; i++) if (roadKept[i]) roadCount++;
        return new EdgeWalls(x0, z0, w, h, foot, hidden, mask, ridge, count) { Thin = thin, ThinCount = thinCount, RoadKept = roadKept, RoadKeptCount = roadCount };
    }

    /// <summary>
    /// The kept ridges that are thin barriers (<see cref="ThinAcross"/>, <see cref="CliffStep"/>, <see cref="TopClear"/>), judged
    /// by parts: ridge samples joined eight ways or across a gap of up to <see cref="ThinJoin"/> samples of other ground (the
    /// test leaves a barrier's crest and its back slope as separate scraps). A part with a road at its foot (<see cref="RoadShare"/>)
    /// is not one; its ridge samples go into <paramref name="roadKept"/>.
    /// </summary>
    private static bool[] ThinBarriers(bool[] ridge, float[] across, float[] height, bool[] present, float[] foot, int w, int h, bool[]? road, bool[]? roadKept)
    {
        int n = w * h;
        // each sample's steps to the nearest road
        var toRoad = road is null ? null : Steps(road, w, h);
        var joined = Grown(ridge, w, h, ThinJoin);
        var thin = new bool[n];
        var seen = new bool[n];
        var queue = new Queue<int>();
        var part = new List<int>();
        var widths = new List<float>();
        var tops = new List<float>();
        for (int s0 = 0; s0 < n; s0++)
        {
            if (!joined[s0] || seen[s0]) continue;
            part.Clear(); widths.Clear(); tops.Clear();
            int byRoad = 0;
            seen[s0] = true; queue.Enqueue(s0);
            int cliffs = 0;
            float reached = float.MinValue;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue(), x = i % w, z = i / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= w || zz >= h) continue;
                        int j = zz * w + xx;
                        if (joined[j] && !seen[j]) { seen[j] = true; queue.Enqueue(j); }
                    }
                if (!ridge[i]) continue;
                part.Add(i);
                if (across[i] > 0) widths.Add(across[i]);
                float step = 0;
                if (x + 1 < w && present[i + 1]) step = MathF.Max(step, MathF.Abs(height[i + 1] - height[i]));
                if (z + 1 < h && present[i + w]) step = MathF.Max(step, MathF.Abs(height[i + w] - height[i]));
                if (present[i] && step >= CliffStep) cliffs++;
                if (present[i]) tops.Add(height[i]);
                reached = MathF.Max(reached, foot[i]);
                if (toRoad is not null && toRoad[i] >= 0 && toRoad[i] <= RoadNear) byRoad++;
            }
            if (widths.Count == 0 || tops.Count == 0 || cliffs < CliffShare * part.Count) continue;
            widths.Sort();
            if (widths[widths.Count / 2] > ThinAcross) continue;
            // its top no car reaches: the drivable ground nearest any of it stays TopClear below its crest (a terrace whose top is
            // drivable ground, a canyon's rim, is scenery seen from below and above, not a barrier)
            tops.Sort();
            if (reached > tops[(int)(tops.Count * 0.95)] - TopClear) continue;
            // a road at its foot: it is seen close up from the road, as the scenery the road runs under
            if (byRoad >= RoadAlong && byRoad >= RoadShare * part.Count)
            {
                if (roadKept is not null) foreach (int i in part) roadKept[i] = true;
                continue;
            }
            foreach (int i in part) thin[i] = true;
        }
        return thin;
    }

    /// <summary>The roads of <paramref name="world"/> as polylines in sample coordinates, for <see cref="Build"/>.</summary>
    public static List<IReadOnlyList<System.Numerics.Vector2>> Roads(MissionWorld world)
    {
        var k = (float)MissionWorld.SampleScale;
        return [.. world.Mission.RoadSegments().Select(seg => (IReadOnlyList<System.Numerics.Vector2>)seg.Points.Select(p => new System.Numerics.Vector2(p.X, p.Z) * k).ToList())];
    }

    /// <summary>Thirds of a sample from each sample to the nearest source (a 3-4 chamfer: within 8% of the Euclidean distance), far with no source.</summary>
    private static int[] Chamfer(bool[] source, int w, int h)
    {
        const int Far = int.MaxValue / 2;
        var d = new int[source.Length];
        for (int i = 0; i < d.Length; i++) d[i] = source[i] ? 0 : Far;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x, v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (z > 0)
                {
                    v = Math.Min(v, d[i - w] + 3);
                    if (x > 0) v = Math.Min(v, d[i - w - 1] + 4);
                    if (x < w - 1) v = Math.Min(v, d[i - w + 1] + 4);
                }
                d[i] = v;
            }
        for (int z = h - 1; z >= 0; z--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = z * w + x, v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + 3);
                if (z < h - 1)
                {
                    v = Math.Min(v, d[i + w] + 3);
                    if (x < w - 1) v = Math.Min(v, d[i + w + 1] + 4);
                    if (x > 0) v = Math.Min(v, d[i + w - 1] + 4);
                }
                d[i] = v;
            }
        return d;
    }

    /// <summary>True where every sample within <paramref name="r"/> (a square) is true.</summary>
    private static bool[] Eroded(bool[] a, int w, int h, int r)
    {
        var o = new bool[a.Length];
        Parallel.For(0, h, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool all = a[z * w + x];
                for (int dz = -r; dz <= r && all; dz++)
                    for (int dx = -r; dx <= r && all; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        all = xx >= 0 && zz >= 0 && xx < w && zz < h && a[zz * w + xx];
                    }
                o[z * w + x] = all;
            }
        });
        return o;
    }

    /// <summary>True where any sample within <paramref name="r"/> (a square) is true.</summary>
    private static bool[] Grown(bool[] a, int w, int h, int r)
    {
        var o = new bool[a.Length];
        Parallel.For(0, h, z =>
        {
            for (int x = 0; x < w; x++)
            {
                bool any = false;
                for (int dz = -r; dz <= r && !any; dz++)
                    for (int dx = -r; dx <= r && !any; dx++)
                    {
                        int xx = x + dx, zz = z + dz;
                        any = xx >= 0 && zz >= 0 && xx < w && zz < h && a[zz * w + xx];
                    }
                o[z * w + x] = any;
            }
        });
        return o;
    }

    /// <summary>
    /// Steps (eight ways) from each sample to the nearest source, -1 with no source; <paramref name="carry"/> is told of each step
    /// (from, to) and <paramref name="start"/> of each source, so a value can ride along.
    /// </summary>
    private static int[] Steps(bool[] source, int w, int h, Action<int, int>? carry = null, Action<int>? start = null)
    {
        int n = w * h;
        var dist = new int[n];
        Array.Fill(dist, -1);
        var queue = new Queue<int>();
        for (int i = 0; i < n; i++) if (source[i]) { dist[i] = 0; start?.Invoke(i); queue.Enqueue(i); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, z = i / w;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, zz = z + dz;
                    if (xx < 0 || zz < 0 || xx >= w || zz >= h) continue;
                    int j = zz * w + xx;
                    if (dist[j] >= 0) continue;
                    dist[j] = dist[i] + 1; carry?.Invoke(i, j);
                    queue.Enqueue(j);
                }
        }
        return dist;
    }

    private static void BoxRows(float[] src, float[] dst, int w, int h)
    {
        Parallel.For(0, h, z =>
        {
            int row = z * w;
            float sum = 0;
            for (int x = -BlurRadius; x <= BlurRadius; x++) sum += src[row + Math.Clamp(x, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                dst[row + x] = sum / (2 * BlurRadius + 1);
                sum += src[row + Math.Clamp(x + BlurRadius + 1, 0, w - 1)] - src[row + Math.Clamp(x - BlurRadius, 0, w - 1)];
            }
        });
    }

    private static void BoxColumns(float[] src, float[] dst, int w, int h)
    {
        Parallel.For(0, w, x =>
        {
            float sum = 0;
            for (int z = -BlurRadius; z <= BlurRadius; z++) sum += src[Math.Clamp(z, 0, h - 1) * w + x];
            for (int z = 0; z < h; z++)
            {
                dst[z * w + x] = sum / (2 * BlurRadius + 1);
                sum += src[Math.Clamp(z + BlurRadius + 1, 0, h - 1) * w + x] - src[Math.Clamp(z - BlurRadius, 0, h - 1) * w + x];
            }
        });
    }

    private int Idx(int sx, int sz) => Math.Clamp(sz - Z0, 0, H - 1) * W + Math.Clamp(sx - X0, 0, W - 1);

    /// <summary>
    /// The ground drawn at game (x, z) under Hide, given its height <paramref name="ground"/> as drawn otherwise: over hidden samples
    /// the lower of that and the foot, blended across the sample between an anchor and the first hidden one.
    /// </summary>
    public double Ground(double x, double z, double ground)
    {
        double fx = x * MissionWorld.SampleScale, fz = z * MissionWorld.SampleScale;
        int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
        double tx = fx - ix, tz = fz - iz;
        double f = 0, m = 0;
        foreach (var (dx, dz, wgt) in (ReadOnlySpan<(int, int, double)>)[(0, 0, (1 - tx) * (1 - tz)), (1, 0, tx * (1 - tz)), (0, 1, (1 - tx) * tz), (1, 1, tx * tz)])
        {
            int i = Idx(ix + dx, iz + dz);
            f += Foot[i] * wgt;
            m += Mask[i] * wgt;
        }
        return ground + (Math.Min(ground, f) - ground) * m;
    }

    /// <summary>The lowest the drawn ground can be over a square of samples under Hide, or null when none of them is hidden.</summary>
    public float? Lowest(int sx, int sz, int size)
    {
        float lo = float.MaxValue;
        bool any = false;
        for (int z = sz; z <= sz + size; z++)
            for (int x = sx; x <= sx + size; x++)
            {
                if (x < X0 || z < Z0 || x >= X0 + W || z >= Z0 + H) continue;
                int i = (z - Z0) * W + x - X0;
                if (!Hidden[i]) continue;
                any = true;
                lo = Math.Min(lo, Foot[i]);
            }
        return any ? lo : null;
    }

    /// <summary>The foot (R) and the mask (G) of every sample, row by row along Z, as a float pair a sample: the shader's texture.</summary>
    public float[] Texels()
    {
        var t = new float[W * H * 2];
        for (int i = 0; i < W * H; i++) { t[i * 2] = Foot[i]; t[i * 2 + 1] = Mask[i]; }
        return t;
    }
}
