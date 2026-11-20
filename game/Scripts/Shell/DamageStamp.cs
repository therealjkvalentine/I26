using Godot;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The garage blueprint's DMG stamps redrawn at the window's resolution (the modern look over the HD menu art). The art bakes
/// each damaged part's stamp into its own sprite (banks 0x25..0x3b, four a part: undamaged, then green, yellow and red), turned
/// to its own angle and drawn in a few dotted pixels; a model upscale keeps the dots. Here the undamaged sprite's upscale is
/// drawn and the stamp over it, fitted to the damaged sprite: where it sits and how it is turned (the pixels that differ from
/// the undamaged sprite, matched against the stamp's design), and its ink (their commonest colour). The design is the art's:
/// a border of four bars round DMG, 36 by 19 pixels, every stroke an outline (<see cref="Style.Outline"/>, the pick) or
/// filled (<see cref="Style.Solid"/>); the ink a little uneven, as a rubber stamp's.
/// </summary>
public static class DamageStamp
{
    public enum Style { Outline, Solid }

    /// <summary>The stamp's size in frame pixels and its centre in its own coordinates.</summary>
    public const float Width = 36, Height = 19, U0 = 18, V0 = 9.5f;

    /// <summary>The I76_STAMP_STYLE hook (outline, solid), else the outline.</summary>
    public static Style Chosen => System.Environment.GetEnvironmentVariable("I76_STAMP_STYLE") == "solid" ? Style.Solid : Style.Outline;

    // the bars: top, bottom, left, right (outer rectangles)
    private static readonly (float X0, float Y0, float X1, float Y1)[] Bars = [(4, 0, 32, 3), (4, 16, 32, 19), (0, 2, 3, 17), (33, 2, 36, 17)];

    // the letters' centre lines, strokes 3 wide: D, M, G
    private static readonly Vector2[][] Letters =
    [
        [new(5.5f, 5.5f), new(9, 5.5f), new(11, 7.5f), new(11, 11.5f), new(9, 13.5f), new(5.5f, 13.5f), new(5.5f, 5.5f)],
        [new(14, 13.5f), new(14, 5.5f), new(17.5f, 10.5f), new(21, 5.5f), new(21, 13.5f)],
        [new(30.5f, 7), new(29, 5.5f), new(25.5f, 5.5f), new(24, 7), new(24, 12), new(25.5f, 13.5f), new(29, 13.5f), new(30.5f, 12), new(30.5f, 10), new(27.5f, 10)],
    ];

    /// <summary>
    /// How far inside the design's ink a point of the stamp (its own coordinates) is: positive in the ink, by the stroke's
    /// half-width it has to spare; <paramref name="rough"/> widens (or, negative, narrows) every stroke.
    /// </summary>
    public static float Depth(float u, float v, Style style, float rough = 0)
    {
        float best = float.MinValue;
        foreach (var (x0, y0, x1, y1) in Bars)
        {
            // inside the bar by this much (negative outside)
            float inside = MathF.Min(MathF.Min(u - x0, x1 - u), MathF.Min(v - y0, y1 - v));
            // the outline: a 1-pixel ring inside the bar's edge
            float d = style == Style.Solid ? inside : 0.5f - MathF.Abs(inside - 0.5f);
            best = MathF.Max(best, d + rough);
        }
        var p = new Vector2(u, v);
        foreach (var line in Letters)
        {
            float dist = float.MaxValue;
            for (int i = 0; i + 1 < line.Length; i++) dist = MathF.Min(dist, Segment(p, line[i], line[i + 1]));
            // a stroke 3 wide: filled to 1.5 from its line, or its two edges 1 wide with the middle left blank
            float d = style == Style.Solid ? 1.5f - dist : 0.5f - MathF.Abs(dist - 1);
            best = MathF.Max(best, d + rough);
        }
        return best;
    }

    private static float Segment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Math.Clamp((p - a).Dot(ab) / ab.LengthSquared(), 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    /// <summary>A damaged sprite's stamp: its centre (the sprite's own pixels, continuous), the turn of its lettering's baseline (radians, clockwise on screen) and its ink.</summary>
    public sealed record Fit(float Cx, float Cy, float Angle, byte Ink, float Score);

    private static readonly Dictionary<(Sprite, Sprite), Fit?> Fits = new(new PairComparer());

    private sealed class PairComparer : IEqualityComparer<(Sprite, Sprite)>
    {
        public bool Equals((Sprite, Sprite) a, (Sprite, Sprite) b) => ReferenceEquals(a.Item1, b.Item1) && ReferenceEquals(a.Item2, b.Item2);
        public int GetHashCode((Sprite, Sprite) p) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.Item1), System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.Item2));
    }

    private static float Lum(byte[] pal, int i) => 0.299f * pal[i * 3] + 0.587f * pal[i * 3 + 1] + 0.114f * pal[i * 3 + 2];
    private static float Dist(byte[] pal, int a, int b)
    {
        int dr = pal[a * 3] - pal[b * 3], dg = pal[a * 3 + 1] - pal[b * 3 + 1], db = pal[a * 3 + 2] - pal[b * 3 + 2];
        return MathF.Sqrt(dr * dr + dg * dg + db * db);
    }

    /// <summary>The stamp on <paramref name="damaged"/> (cached), measured against the same part <paramref name="undamaged"/>; null when none is found.</summary>
    public static Fit? Find(Sprite damaged, Sprite undamaged, byte[] pal)
    {
        if (Fits.TryGetValue((damaged, undamaged), out var f)) return f;
        return Fits[(damaged, undamaged)] = Measure(damaged, undamaged, pal);
    }

    /// <summary>
    /// The stamp's pixels: those of the damaged sprite that the undamaged one lacks or has otherwise, but for the colours of the
    /// part's own paper (a tyre's sprite carries its paper; those colours, common in the undamaged sprite, are left out), within
    /// 90 of the commonest such colour (the ink; its blends with the paper and the lines are near it). Their middle and long
    /// axis place the stamp; then the turn either way along the axis and a few degrees and half pixels round it are tried
    /// against the design, keeping the best (one within a degree and a half of square to the sprite set square, as the art's are).
    /// </summary>
    public static Fit? Measure(Sprite d, Sprite u, byte[] pal)
    {
        int ox = d.X0 - u.X0, oy = d.Y0 - u.Y0;
        int Under(int c, int r)
        {
            int uc = c + ox, ur = r + oy;
            return uc >= 0 && uc < u.Width && ur >= 0 && ur < u.Height && u.Opaque[ur * u.Width + uc] ? u.Pixels[ur * u.Width + uc] : -1;
        }
        // the part's paper: colours making up a tenth or more of the undamaged sprite, not dark
        var counts = new int[256];
        int opaque = 0;
        for (int i = 0; i < u.Pixels.Length; i++) if (u.Opaque[i]) { counts[u.Pixels[i]]++; opaque++; }
        var paper = new bool[256];
        for (int i = 0; i < 256; i++) paper[i] = counts[i] * 10 >= opaque && Lum(pal, i) >= 110;
        var diff = new bool[d.Width * d.Height];
        var inks = new int[256];
        for (int r = 0; r < d.Height; r++)
            for (int c = 0; c < d.Width; c++)
            {
                int i = r * d.Width + c;
                if (!d.Opaque[i] || Under(c, r) == d.Pixels[i] || paper[d.Pixels[i]]) continue;
                diff[i] = true;
                inks[d.Pixels[i]]++;
            }
        int ink = Array.IndexOf(inks, inks.Max());
        if (inks[ink] < 20) return null;
        var pts = new List<Vector2>();
        for (int i = 0; i < diff.Length; i++)
            if (diff[i] && Dist(pal, d.Pixels[i], ink) <= 90) pts.Add(new Vector2(i % d.Width + 0.5f, i / d.Width + 0.5f));
            else diff[i] = false;
        if (pts.Count < 20) return null;
        var m = pts.Aggregate(Vector2.Zero, (a, p) => a + p) / pts.Count;
        float sxx = 0, syy = 0, sxy = 0;
        foreach (var p in pts) { var q = p - m; sxx += q.X * q.X; syy += q.Y * q.Y; sxy += q.X * q.Y; }
        float axis = 0.5f * MathF.Atan2(2 * sxy, sxx - syy);
        // the clipped ones (a special's stamp runs off its sprite) have their middle off the stamp's: the search goes 3 pixels round
        // it, coarse (2 degrees, a pixel), then finer round the best (a degree, half a pixel)
        var area = Area(diff, d.Width, d.Height);
        Fit? best = null;
        void Try(float cx, float cy, float a)
        {
            float s = Score(diff, d.Width, area, cx, cy, a);
            if (best is null || s > best.Score) best = new Fit(cx, cy, a, (byte)ink, s);
        }
        // every stamp of the art reads upright or turned at most a quarter either way (a blurred, turned DMG matches itself upside
        // down nearly as well): of the axis's two ways, those within 100 degrees of upright
        foreach (float turn in new[] { 0f, MathF.PI })
        {
            float way = MathF.IEEERemainder(axis + turn, 2 * MathF.PI);
            if (MathF.Abs(way) > 100 * MathF.PI / 180) continue;
            for (int deg = -8; deg <= 8; deg += 2)
                for (float dx = -3; dx <= 3; dx++)
                    for (float dy = -3; dy <= 3; dy++)
                        Try(m.X + dx, m.Y + dy, way + deg * MathF.PI / 180);
        }
        var coarse = best!;
        for (int deg = -2; deg <= 2; deg++)
            for (float dx = -1; dx <= 1; dx += 0.5f)
                for (float dy = -1; dy <= 1; dy += 0.5f)
                    Try(coarse.Cx + dx, coarse.Cy + dy, coarse.Angle + deg * MathF.PI / 180);
        // the art's upright and sideways stamps are square to the sprite: within a degree and a half of it is that
        float quarter = MathF.PI / 2, near = MathF.Round(best!.Angle / quarter) * quarter;
        if (MathF.Abs(best.Angle - near) <= 1.5f * MathF.PI / 180)
        {
            var square = best;
            for (float dx = -1; dx <= 1; dx += 0.5f)
                for (float dy = -1; dy <= 1; dy += 0.5f)
                {
                    float s = Score(diff, d.Width, area, best.Cx + dx, best.Cy + dy, near);
                    if (square == best || s > square.Score) square = best with { Cx = best.Cx + dx, Cy = best.Cy + dy, Angle = near, Score = s };
                }
            best = square;
        }
        return best;
    }

    /// <summary>Where the stamp's pixels are, 4 pixels round, within the sprite: all a score needs to look at.</summary>
    public static Rect2I Area(bool[] stamp, int w, int h)
    {
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int i = 0; i < stamp.Length; i++)
            if (stamp[i]) { int x = i % w, y = i / w; x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        if (x1 < 0) return default;
        x0 = Math.Max(0, x0 - 4); y0 = Math.Max(0, y0 - 4); x1 = Math.Min(w - 1, x1 + 4); y1 = Math.Min(h - 1, y1 + 4);
        return new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    // the outline design's ink, 4 samples a pixel, with a pixel's margin round it
    private const int Grid = 4;
    private static readonly bool[] Design = MakeDesign();
    private static readonly int DesignW = (int)(Width + 2) * Grid, DesignH = (int)(Height + 2) * Grid;

    private static bool[] MakeDesign()
    {
        int w = (int)(Width + 2) * Grid, h = (int)(Height + 2) * Grid;
        var g = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                g[y * w + x] = Depth((x + 0.5f) / Grid - 1, (y + 0.5f) / Grid - 1, Style.Outline) >= 0;
        return g;
    }

    private static bool Inked(float u, float v)
    {
        int x = (int)MathF.Floor((u + 1) * Grid), y = (int)MathF.Floor((v + 1) * Grid);
        return x >= 0 && y >= 0 && x < DesignW && y < DesignH && Design[y * DesignW + x];
    }

    /// <summary>
    /// How well the design at (cx, cy) turned by <paramref name="angle"/> matches the stamp's pixels: each pixel the design inks
    /// scores 1 when the stamp has it and −0.6 when not; a stamp pixel the design leaves blank scores −0.3.
    /// </summary>
    public static float Score(bool[] stamp, int w, Rect2I area, float cx, float cy, float angle)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle), s = 0;
        for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
            {
                var (u, v) = ToStamp(x + 0.5f, y + 0.5f, cx, cy, cos, sin);
                bool design = Inked(u, v), has = stamp[y * w + x];
                s += design ? (has ? 1 : -0.6f) : has ? -0.3f : 0;
            }
        return s;
    }

    private static (float U, float V) ToStamp(float x, float y, float cx, float cy, float cos, float sin)
    {
        float qx = x - cx, qy = y - cy;
        return (cos * qx + sin * qy + U0, -sin * qx + cos * qy + V0);
    }

    /// <summary>
    /// What the stamp puts at a point of its sprite (2 ink, 0 none), and how heavily (0..1): the ink a little uneven, lighter in
    /// broad blotches and here and there where the stamp's rubber missed, its edges a little rough; the same for the same
    /// <paramref name="seed"/> (a part's own stamp looks the same every time).
    /// </summary>
    public static (int Ink, float Alpha) At(Fit f, Style style, int seed, float x, float y)
    {
        var (u, v) = ToStamp(x, y, f.Cx, f.Cy, MathF.Cos(f.Angle), MathF.Sin(f.Angle));
        if (u < -1 || u > Width + 1 || v < -1 || v > Height + 1) return (0, 0);
        float rough = (Noise(seed + 7, u * 1.7f, v * 1.7f) - 0.5f) * 0.3f;
        if (Depth(u, v, style, rough) < 0) return (0, 0);
        float a = 0.92f - 0.3f * Noise(seed, u * 0.22f, v * 0.22f);
        if (Noise(seed + 13, u * 2.2f, v * 2.2f) > 0.86f) a *= 0.45f;
        return (2, Math.Clamp(a, 0, 1));
    }

    // smooth value noise in 0..1
    private static float Noise(int seed, float x, float y)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(seed, xi, yi), b = Hash(seed, xi + 1, yi), c = Hash(seed, xi, yi + 1), e = Hash(seed, xi + 1, yi + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + e) * fx * fy;
    }

    private static float Hash(int seed, int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffffff) / (float)0x1000000;
    }

    /// <summary>The stamp drawn at the window's resolution over its sprite placed at frame (<paramref name="x"/>, <paramref name="y"/>), the frame drawn from <paramref name="origin"/> at <paramref name="scale"/>.</summary>
    public static (Rect2I Screen, byte[] Rgba) Raster(Fit f, Style style, int seed, int x, int y, Sprite s, float scale, Vector2 origin, Color ink)
    {
        // the stamp's box on the frame, from its corners
        float cos = MathF.Cos(f.Angle), sin = MathF.Sin(f.Angle);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (u, v) in new[] { (-1f, -1f), (Width + 1, -1f), (-1f, Height + 1), (Width + 1, Height + 1) })
        {
            float qx = cos * (u - U0) - sin * (v - V0) + f.Cx + x, qy = sin * (u - U0) + cos * (v - V0) + f.Cy + y;
            minX = MathF.Min(minX, qx); maxX = MathF.Max(maxX, qx); minY = MathF.Min(minY, qy); maxY = MathF.Max(maxY, qy);
        }
        // kept to the sprite's own box, as the art's stamp is
        minX = MathF.Max(minX, x); minY = MathF.Max(minY, y); maxX = MathF.Min(maxX, x + s.Width); maxY = MathF.Min(maxY, y + s.Height);
        var box = new Rect2(minX, minY, MathF.Max(0, maxX - minX), MathF.Max(0, maxY - minY));
        float last = 1;
        return FormGlyphs.Raster(box, scale, origin, (fx, fy) =>
        {
            if (fx < x || fy < y || fx >= x + s.Width || fy >= y + s.Height) { last = 0; return 0; }
            var (k, a) = At(f, style, seed, fx - x, fy - y);
            last = a;
            return k;
        }, ink, ink, 3, (_, _) => last);
    }
}
