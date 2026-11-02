using System.Numerics;

namespace I76.Formats.Mission;

/// <summary>
/// The host form's sketch of an arena (the port's; the original's form has none): the arena seen from above as a pen
/// drawing, the drivable floor left as paper, the ground that rises above it hatched with an inked edge, the roads as
/// thin lines and each start point an x. Built from the terrain's heights and the mission's placements on an analysis
/// grid two cells per shell pixel of the box, then drawn at any scale: anti-aliased for the modern look, or as hard
/// pixels for the 1997 look's 640x480 frame. Plain arrays, no engine.
/// </summary>
public sealed class ArenaSketch
{
    /// <summary>The ink colours: the pen's, the hatching's pencil and the roads' sepia.</summary>
    public static readonly (byte R, byte G, byte B) Ink = (40, 30, 18), Graphite = (58, 50, 44), Sepia = (92, 58, 26);
    /// <summary>The analysis grid's cells per shell pixel of the box.</summary>
    public const int Cells = 2;
    /// <summary>The ground beyond the arena fades out over this many shell pixels before the box's edge (no frame round the sketch).</summary>
    public const float Fade = 12f;

    /// <summary>The start points (melee spawns; a story mission's player starts when it has none).</summary>
    public int Starts { get; }
    /// <summary>The span of the start points and placements, in metres along X and Z.</summary>
    public float Width { get; }
    public float Depth { get; }
    public int BoxWidth { get; }
    public int BoxHeight { get; }

    private readonly int _gw, _gh;
    private readonly float[] _soft;
    private readonly Vector2[] _starts;
    private readonly List<Vector2[]> _roads;

    private ArenaSketch(int boxW, int boxH, float[] soft, Vector2[] starts, List<Vector2[]> roads, float width, float depth)
    {
        (BoxWidth, BoxHeight, _gw, _gh, _soft, _starts, _roads) = (boxW, boxH, boxW * Cells, boxH * Cells, soft, starts, roads);
        (Starts, Width, Depth) = (starts.Length, width, depth);
    }

    /// <summary>"1.0 x 1.0 km", the note the form writes by the sketch.</summary>
    public string SizeText => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Width / 1000f:0.0} x {Depth / 1000f:0.0} km");

    /// <summary>A mission's sketch for a box of boxW x boxH shell pixels; null when it has no placements on its terrain.</summary>
    public static ArenaSketch? Of(MissionFile msn, TerrainTiles ter, int boxW, int boxH)
    {
        var placed = msn.PlacedObjects();
        static bool IsSpawn(PlacedObject o) => o.ObjectType == 1 && o.Name.TrimStart('#').StartsWith("spawn", StringComparison.OrdinalIgnoreCase);
        var starts = placed.Where(IsSpawn).Select(o => new Vector2(o.Position.X, o.Position.Z)).ToList();
        if (starts.Count == 0) starts = [.. placed.Where(o => o.IsPlayer).Select(o => new Vector2(o.Position.X, o.Position.Z))];
        var roads = msn.RoadSegments().Select(r => r.Points.Select(p => p.Raw.Length >= 24
            ? new Vector2((Bin.F32(p.Raw, 0) + Bin.F32(p.Raw, 12)) / 2, (Bin.F32(p.Raw, 8) + Bin.F32(p.Raw, 20)) / 2)
            : new Vector2(p.X, p.Z)).ToArray()).ToList();
        int? Height(int sx, int sz) => ter.Height(sx, sz);
        var (r0, r1, c0, c1) = ter.Bounds();
        if (r1 < r0) return null;
        const int T = MissionFile.TerrainTile;
        var lo = new Vector2(c0 * T, r0 * T);
        var hi = new Vector2((c1 + 1) * T, (r1 + 1) * T);
        var points = placed.Select(o => new Vector2(o.Position.X, o.Position.Z))
            .Where(p => p.X / MissionFile.WorldScale >= lo.X && p.Y / MissionFile.WorldScale >= lo.Y && p.X / MissionFile.WorldScale < hi.X && p.Y / MissionFile.WorldScale < hi.Y)
            .ToList();
        return Build(Height, starts, points, roads, boxW, boxH);
    }

    /// <summary>
    /// The sketch from heights (a sample's 12-bit height at global sample coordinates, null off the terrain; world height
    /// is a tenth of it) and world positions (X, Z): the starts, every placement (which with the starts bound the
    /// region drawn) and the roads' centre lines. Null when there is nothing to bound a region with.
    /// </summary>
    public static ArenaSketch? Build(Func<int, int, int?> height, IReadOnlyList<Vector2> starts, IReadOnlyList<Vector2> points,
        IReadOnlyList<Vector2[]> roads, int boxW, int boxH)
    {
        const float S = MissionFile.WorldScale;
        var all = starts.Concat(points).Select(p => p / S).ToList();
        if (all.Count == 0 || boxW <= 0 || boxH <= 0) return null;
        float ax = all.Min(p => p.X), az = all.Min(p => p.Y), bx = all.Max(p => p.X), bz = all.Max(p => p.Y);
        int gw = boxW * Cells, gh = boxH * Cells;
        // the region: the points' span and two fifths again, plus 40 samples (room for the hatched band), widened to the box's shape
        float cx = (ax + bx) / 2, cz = (az + bz) / 2, w = (bx - ax) * 1.4f + 40, d = (bz - az) * 1.4f + 40;
        if (w / d < (float)gw / gh) w = d * gw / gh;
        else d = w * gh / gw;
        float gx = cx - w / 2, gz = cz - d / 2;

        // heights over the grid (+X right, +Z up: row 0 is the far edge), bilinear between samples; off the terrain the lowest
        var g = new float[gw * gh];
        var missing = new bool[gw * gh];
        float min = float.MaxValue;
        for (int r = 0; r < gh; r++)
            for (int c = 0; c < gw; c++)
            {
                float sx = gx + (c + 0.5f) / gw * w, sz = gz + d - (r + 0.5f) / gh * d;
                if (Bilinear(height, sx, sz) is { } h) { g[r * gw + c] = h; min = Math.Min(min, h); }
                else missing[r * gw + c] = true;
            }
        if (min == float.MaxValue) min = 0;
        for (int i = 0; i < g.Length; i++) if (missing[i]) g[i] = min;

        // the floor: up to FloorRise above the starts' upper quartile; what rises higher is the arena's edge
        Vector2 ToGrid(Vector2 p) => new((p.X / S - gx) / w, (gz + d - p.Y / S) / d);
        var st = starts.Select(ToGrid).ToArray();
        var sh = st.Select(u => g[Math.Clamp((int)(u.Y * gh), 0, gh - 1) * gw + Math.Clamp((int)(u.X * gw), 0, gw - 1)]).ToList();
        if (sh.Count == 0) sh = [.. points.Select(ToGrid).Select(u => g[Math.Clamp((int)(u.Y * gh), 0, gh - 1) * gw + Math.Clamp((int)(u.X * gw), 0, gw - 1)])];
        float level = Percentile(sh, 0.75f) + FloorRise;
        var blurred = Blur(g, gw, gh, 1.0f);
        var m = new float[g.Length];
        for (int i = 0; i < m.Length; i++) m[i] = blurred[i] > level ? 1 : 0;
        m = Dilate(Erode(m, gw, gh), gw, gh);
        m = Band(m, gw, gh);
        var soft = Blur(m, gw, gh, 0.7f);
        var rd = roads.Select(r => r.Select(ToGrid).ToArray()).Where(r => r.Length > 1).ToList();
        return new ArenaSketch(boxW, boxH, soft, st, rd, (bx - ax) * S, (bz - az) * S);
    }

    /// <summary>The hatched band's width beside the floor, in grid cells (6 shell pixels), and how far its outer edge wanders either way.</summary>
    public const float BandCells = 12f, BandWander = 0.35f;
    /// <summary>High ground this near the box's edge (grid cells, 10 shell pixels; wandering as the band does) is left as paper too.</summary>
    public const float EdgeCells = 20f;
    /// <summary>The floor: up to this much above the starts' upper quartile (world units); the slopes up to it are drivable ground, not hatched.</summary>
    public const float FloorRise = 10f;

    /// <summary>
    /// The high ground beyond a band round the floor is left as paper where it runs out to the box's edge, the band's outer
    /// edge wandering as a hand would draw it (a smooth noise of fixed seed on its width): an arena ringed by high ground as
    /// far as the box reaches (Jorczak's Peak) reads as a shape on the paper, not a hatched rectangle. High ground the floor
    /// encloses stays whole. With no floor at all the high ground is left as it is.
    /// </summary>
    internal static float[] Band(float[] m, int w, int h)
    {
        // chamfer distance (3-4) from the floor
        const int Far = int.MaxValue / 2;
        var d = new int[m.Length];
        bool floor = false;
        for (int i = 0; i < m.Length; i++) { d[i] = m[i] > 0.5f ? Far : 0; floor |= d[i] == 0; }
        if (!floor) return m;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + 3);
                if (y > 0) v = Math.Min(v, d[i - w] + 3);
                if (x > 0 && y > 0) v = Math.Min(v, d[i - w - 1] + 4);
                if (x + 1 < w && y > 0) v = Math.Min(v, d[i - w + 1] + 4);
                d[i] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x, v = d[i];
                if (x + 1 < w) v = Math.Min(v, d[i + 1] + 3);
                if (y + 1 < h) v = Math.Min(v, d[i + w] + 3);
                if (x + 1 < w && y + 1 < h) v = Math.Min(v, d[i + w + 1] + 4);
                if (x > 0 && y + 1 < h) v = Math.Min(v, d[i + w - 1] + 4);
                d[i] = v;
            }
        // value noise on a 24-cell lattice, smoothstepped between its knots
        const int Knot = 24;
        static float Hash(int a, int b) { uint u = (uint)(a * 73856093 ^ b * 19349663 ^ 76); u ^= u >> 13; u *= 0x5bd1e995; u ^= u >> 15; return (u & 0xffff) / 65535f * 2 - 1; }
        var far = new bool[m.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (m[i] <= 0.5f) continue;
                int kx = x / Knot, ky = y / Knot;
                float fx = (float)(x % Knot) / Knot, fy = (float)(y % Knot) / Knot;
                fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
                float a = Hash(kx, ky) + (Hash(kx + 1, ky) - Hash(kx, ky)) * fx, b = Hash(kx, ky + 1) + (Hash(kx + 1, ky + 1) - Hash(kx, ky + 1)) * fx;
                float wander = 1 + BandWander * (a + (b - a) * fy);
                // beyond the band, or near the box's edge (the sketch closes before it, never cut straight by it)
                far[i] = d[i] > BandCells * wander * 3 || Math.Min(Math.Min(x, w - 1 - x), Math.Min(y, h - 1 - y)) < EdgeCells * wander;
            }
        // what goes is the far ground the box's edge reaches: high ground the floor encloses (a mesa, a wall between two roads) stays whole
        var o = (float[])m.Clone();
        var stack = new Stack<int>();
        for (int x = 0; x < w; x++) { stack.Push(x); stack.Push((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { stack.Push(y * w); stack.Push(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (!far[i] || o[i] == 0) continue;
            o[i] = 0;
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x + 1 < w) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y + 1 < h) stack.Push(i + w);
        }
        return o;
    }

    private static float? Bilinear(Func<int, int, int?> height, float sx, float sz)
    {
        int x0 = (int)MathF.Floor(sx), z0 = (int)MathF.Floor(sz);
        float fx = sx - x0, fz = sz - z0;
        float sum = 0, wsum = 0;
        for (int k = 0; k < 4; k++)
        {
            int dx = k & 1, dz = k >> 1;
            float wt = (dx == 1 ? fx : 1 - fx) * (dz == 1 ? fz : 1 - fz);
            if (height(x0 + dx, z0 + dz) is { } h) { sum += h * 0.1f * wt; wsum += wt; }
        }
        return wsum > 1e-4f ? sum / wsum : null;
    }

    /// <summary>The value a fraction q up the sorted list, between neighbours (numpy's linear percentile).</summary>
    internal static float Percentile(List<float> v, float q)
    {
        if (v.Count == 0) return 0;
        v.Sort();
        float pos = q * (v.Count - 1);
        int i = (int)MathF.Floor(pos);
        return i + 1 < v.Count ? v[i] + (v[i + 1] - v[i]) * (pos - i) : v[i];
    }

    private static float[] Blur(float[] a, int w, int h, float sigma)
    {
        int r = (int)MathF.Ceiling(4 * sigma);
        var k = new float[2 * r + 1];
        float s = 0;
        for (int i = -r; i <= r; i++) s += k[i + r] = MathF.Exp(-i * i / (2 * sigma * sigma));
        for (int i = 0; i < k.Length; i++) k[i] /= s;
        var t = new float[a.Length];
        var o = new float[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * a[y * w + Math.Clamp(x + i, 0, w - 1)];
                t[y * w + x] = acc;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int i = -r; i <= r; i++) acc += k[i + r] * t[Math.Clamp(y + i, 0, h - 1) * w + x];
                o[y * w + x] = acc;
            }
        return o;
    }

    // a 3x3 opening: specks of high ground narrower than three cells go
    private static float[] Erode(float[] a, int w, int h) => Morph(a, w, h, true);
    private static float[] Dilate(float[] a, int w, int h) => Morph(a, w, h, false);
    private static float[] Morph(float[] a, int w, int h, bool erode)
    {
        var o = new float[a.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool all = true, any = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        bool on = a[yy * w + xx] > 0.5f;
                        all &= on; any |= on;
                    }
                o[y * w + x] = (erode ? all : any) ? 1 : 0;
            }
        return o;
    }

    private (int X0, int Y0, int X1, int Y1)? _ink;
    private bool _inkKnown;
    private byte[]? _drawn;

    /// <summary>Whether the sketch inks the box's pixel (x, y) (the 1997 look's drawing, alpha 40 and up): a note set by it keeps off the lines.</summary>
    public bool Inked(int x, int y)
    {
        _drawn ??= Render(1, aliased: true);
        return x >= 0 && y >= 0 && x < BoxWidth && y < BoxHeight && _drawn[(y * BoxWidth + x) * 4 + 3] >= 40;
    }

    /// <summary>The box's rows and columns the sketch inks (the 1997 look's drawing, alpha 40 and up), inclusive; null when it inks none. The form centres these in its space.</summary>
    public (int X0, int Y0, int X1, int Y1)? InkBounds()
    {
        if (_inkKnown) return _ink;
        var px = _drawn ??= Render(1, aliased: true);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int i = 0; i < px.Length / 4; i++)
            if (px[i * 4 + 3] >= 40)
            {
                int x = i % BoxWidth, y = i / BoxWidth;
                x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            }
        _inkKnown = true;
        return _ink = x1 < 0 ? null : (x0, y0, x1, y1);
    }

    /// <summary>
    /// The sketch as straight-alpha RGBA, scale pixels per shell pixel of the box. Aliased: every pixel all ink or none
    /// (the 1997 look's frame, where the ink is mixed into the palette by the caller), the hatching wider apart.
    /// </summary>
    public byte[] Render(int scale, bool aliased)
    {
        int W = BoxWidth * scale, H = BoxHeight * scale;
        float k = scale;
        var hatch = new float[W * H];
        var edge = new float[W * H];
        var roads = new float[W * H];
        var marks = new float[W * H];
        var fade = new float[W * H];
        float pitch = aliased ? 4f : 2.5f * k, hw = 0.45f * k / 2;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float f = Math.Clamp(Math.Min(Math.Min(px, W - px), Math.Min(py, H - py)) / (Fade * k), 0, 1);
                f = f * f * (3 - 2 * f);
                fade[y * W + x] = aliased ? (f > 0.5f ? 1 : 0) : f;
                float inside = Math.Clamp((SoftAt(px / W * _gw - 0.5f, py / H * _gh - 0.5f) - 0.5f) * 4 + 0.5f, 0, 1);
                float t = (px + py) % pitch;
                hatch[y * W + x] = aliased ? (t < 1 && inside > 0.5f ? 1 : 0) : Math.Clamp(hw + 0.5f - MathF.Min(t, pitch - t) / MathF.Sqrt(2), 0, 1) * inside;
            }
        // the floor's edge: the soft mask's 0.5 contour, cell centres mapped to the output's
        float sx = (float)W / _gw, sy = (float)H / _gh;
        foreach (var (a, b) in Contour(_soft, _gw, _gh, 0.5f))
            Line(edge, W, H, new Vector2((a.X + 0.5f) * sx, (a.Y + 0.5f) * sy), new Vector2((b.X + 0.5f) * sx, (b.Y + 0.5f) * sy), 0.8f * k, aliased);
        foreach (var r in _roads)
            for (int i = 0; i + 1 < r.Length; i++) Line(roads, W, H, new Vector2(r[i].X * W, r[i].Y * H), new Vector2(r[i + 1].X * W, r[i + 1].Y * H), 0.5f * k, aliased);
        foreach (var s in _starts)
        {
            float px = s.X * W, py = s.Y * H, a = aliased ? 2 : 2.25f * k;
            Line(marks, W, H, new Vector2(px - a, py - a), new Vector2(px + a, py + a), 0.8f * k, aliased);
            Line(marks, W, H, new Vector2(px - a, py + a), new Vector2(px + a, py - a), 0.8f * k, aliased);
        }
        var o = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++)
        {
            float r = 0, gg = 0, bb = 0, al = 0;
            void Over(float cov, (byte R, byte G, byte B) c)
            {
                if (cov <= 0) return;
                r = r * (1 - cov) + c.R * cov; gg = gg * (1 - cov) + c.G * cov; bb = bb * (1 - cov) + c.B * cov;
                al = cov + al * (1 - cov);
            }
            Over(hatch[i] * fade[i] * 0.45f, Graphite);
            Over(edge[i] * fade[i] * 0.85f, Ink);
            Over(roads[i] * fade[i] * 0.55f, Sepia);
            Over(marks[i] * 0.95f, Ink);
            if (al <= 0) continue;
            // straight alpha: the inks' mix over one another, apart from how much of the pixel they cover
            o[i * 4] = (byte)Math.Clamp(r / al + 0.5f, 0, 255);
            o[i * 4 + 1] = (byte)Math.Clamp(gg / al + 0.5f, 0, 255);
            o[i * 4 + 2] = (byte)Math.Clamp(bb / al + 0.5f, 0, 255);
            o[i * 4 + 3] = (byte)Math.Clamp(al * 255 + 0.5f, 0, 255);
        }
        return o;
    }

    private float SoftAt(float u, float v)
    {
        u = Math.Clamp(u, 0, _gw - 1); v = Math.Clamp(v, 0, _gh - 1);
        int x0 = Math.Min((int)u, Math.Max(0, _gw - 2)), y0 = Math.Min((int)v, Math.Max(0, _gh - 2));
        int x1 = Math.Min(x0 + 1, _gw - 1), y1 = Math.Min(y0 + 1, _gh - 1);
        float fx = u - x0, fy = v - y0;
        float a = _soft[y0 * _gw + x0] * (1 - fx) + _soft[y0 * _gw + x1] * fx;
        float b = _soft[y1 * _gw + x0] * (1 - fx) + _soft[y1 * _gw + x1] * fx;
        return a * (1 - fy) + b * fy;
    }

    /// <summary>A segment of the given width laid into a coverage layer (the larger coverage kept where two cross).</summary>
    private static void Line(float[] layer, int W, int H, Vector2 p, Vector2 q, float width, bool aliased)
    {
        float half = aliased ? MathF.Max(width / 2, 0.55f) : width / 2;
        int x0 = Math.Max(0, (int)(MathF.Min(p.X, q.X) - half - 1)), y0 = Math.Max(0, (int)(MathF.Min(p.Y, q.Y) - half - 1));
        int x1 = Math.Min(W, (int)(MathF.Max(p.X, q.X) + half + 2)), y1 = Math.Min(H, (int)(MathF.Max(p.Y, q.Y) + half + 2));
        var v = q - p;
        float l2 = v.LengthSquared() + 1e-9f;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                var c = new Vector2(x + 0.5f, y + 0.5f);
                float t = Math.Clamp(Vector2.Dot(c - p, v) / l2, 0, 1);
                float dist = Vector2.Distance(c, p + t * v);
                float cov = aliased ? (dist <= half ? 1 : 0) : Math.Clamp(half + 0.5f - dist, 0, 1);
                if (cov > layer[y * W + x]) layer[y * W + x] = cov;
            }
    }

    /// <summary>The segments of a level's contour through a grid of values (marching squares; a saddle split by the cell's mean), in cell coordinates.</summary>
    internal static List<(Vector2 A, Vector2 B)> Contour(float[] a, int w, int h, float level)
    {
        var segs = new List<(Vector2, Vector2)>();
        for (int y = 0; y + 1 < h; y++)
            for (int x = 0; x + 1 < w; x++)
            {
                float v0 = a[y * w + x], v1 = a[y * w + x + 1], v2 = a[(y + 1) * w + x + 1], v3 = a[(y + 1) * w + x];
                int c = (v0 > level ? 1 : 0) | (v1 > level ? 2 : 0) | (v2 > level ? 4 : 0) | (v3 > level ? 8 : 0);
                if (c is 0 or 15) continue;
                Vector2 Lerp(Vector2 p, Vector2 q, float va, float vb) => p + (q - p) * ((level - va) / (vb - va));
                Vector2 top = Lerp(new(x, y), new(x + 1, y), v0, v1), right = Lerp(new(x + 1, y), new(x + 1, y + 1), v1, v2);
                Vector2 bottom = Lerp(new(x, y + 1), new(x + 1, y + 1), v3, v2), left = Lerp(new(x, y), new(x, y + 1), v0, v3);
                bool centre = (v0 + v1 + v2 + v3) / 4 > level;
                switch (c)
                {
                    case 1 or 14: segs.Add((left, top)); break;
                    case 2 or 13: segs.Add((top, right)); break;
                    case 3 or 12: segs.Add((left, right)); break;
                    case 4 or 11: segs.Add((right, bottom)); break;
                    case 6 or 9: segs.Add((top, bottom)); break;
                    case 7 or 8: segs.Add((left, bottom)); break;
                    case 5:
                        if (centre) { segs.Add((left, bottom)); segs.Add((top, right)); }
                        else { segs.Add((left, top)); segs.Add((right, bottom)); }
                        break;
                    case 10:
                        if (centre) { segs.Add((left, top)); segs.Add((right, bottom)); }
                        else { segs.Add((left, bottom)); segs.Add((top, right)); }
                        break;
                }
            }
        return segs;
    }
}
