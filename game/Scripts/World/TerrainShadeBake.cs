using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// The modern look's baked terrain shading (TerrainShade), made once a mission from the drawn ground and its fixed sun.
///
/// The sun: per texel of a fine grid over the map's tiles (2.5 m by default, finer than the 5 m samples; the heights are the drawn
/// surface's, <see cref="ITerrainSurface.Point"/>, the shape the terrain mesh is built from), the angle by which the sun stands over
/// the terrain's horizon toward it (the margin: negative in the terrain's shadow). The shader smooths the margin over the sun's disc,
/// so the penumbra is the sun's angle seen from the point: a few centimetres at a rock's foot, metres from a far ridge, as a real
/// shadow's. A margin interpolates where a lit share would not: the shadow's edge falls between texels, a line rather than steps.
///
/// The sky: per coarse texel (about 10 m) the share of the sky the terrain round it leaves open (the horizon in eight directions,
/// cosine weighted).
///
/// Each horizon is exact along a line of texels, kept as the upper convex hull of the heights ahead of the point (each point added
/// and dropped once), so a line costs its length: the eight sky directions and the sun's are straight lines of the grid (every preset's
/// sun stands over +x: I76.Formats.Mission.Lighting), any other azimuth a march. Pure and deterministic (lines in parallel, each
/// writing only its own texels); no engine here, the tests run it on made-up heightfields.
/// </summary>
public static class TerrainShadeBake
{
    /// <summary>The sun's angular radius, radians (its disc is half a degree): the penumbra's half-width in the margin.</summary>
    public const float SunRadius = 0.0047f;
    /// <summary>The margin stored, ± radians (R8: 1/255 of twice this, a tenth of the sun's radius); beyond it the texel is plainly lit or dark.</summary>
    public const float MarginRange = 0.06f;
    /// <summary>The nearest texel ahead counted in a point's sun horizon: the next one, with the point raised <see cref="SunBias"/> texels.</summary>
    public const int SunSkip = 1;
    /// <summary>
    /// The point's rise for its sun horizon, in texels: a slope turned from the sun is shaded by the bake only once it is steeper than the
    /// sun is high plus a tenth, where the shading normal has already put it out of the sun (so a texel's own slope does not shade it).
    /// </summary>
    public const float SunBias = 0.1f;
    /// <summary>The finest grid: 2.5 m (the default) or 1.25 m texels, metres; <see cref="MaxTexels"/> may make it coarser on a big map.</summary>
    public const float DefaultTexel = 2.5f;
    /// <summary>The most texels the sun's texture may have (an R8 texture: 32 MB, 43 with its mipmaps); a bigger map is baked coarser.</summary>
    public const long MaxTexels = 32L << 20;
    /// <summary>The sky's texels, metres (its share changes over tens of metres).</summary>
    public const float SkyTexel = 10f;

    /// <summary>A heightfield: <paramref name="H"/> row by row (index z * W + x), metres; texels <paramref name="Texel"/> metres apart.</summary>
    public sealed record Grid(float[] H, int W, int D, float Texel);

    /// <summary>
    /// The baked texture's place: its corner (game frame x, z: uv 0), its size in metres, texels across, and samples per texel
    /// (<paramref name="Stride"/>: 0.5 at 2.5 m texels, 1 at 5 m).
    /// </summary>
    public readonly record struct Placement(float X0, float Z0, float SizeX, float SizeZ, int W, int D, double Stride, int SampleX0, int SampleZ0)
    {
        public float Texel => SizeX / W;
        /// <summary>Texel (i, j)'s centre in sample coordinates (sample s at game x = 5 s).</summary>
        public (double Fx, double Fz) Centre(int i, int j) => (SampleX0 - 0.5 + (i + 0.5) * Stride, SampleZ0 - 0.5 + (j + 0.5) * Stride);
    }

    /// <summary>
    /// Where a map's bake lies: its tiles' bounds, uv 0 half a sample before the first sample (so at 5 m a texel's centre is a sample),
    /// texels of <paramref name="texel"/> metres, coarser if the map would need more than <see cref="MaxTexels"/>.
    /// </summary>
    public static Placement PlacementFor(TerrainTiles ter, float texel = DefaultTexel)
    {
        var (r0, r1, c0, c1) = ter.Bounds();
        if (r1 < r0 || c1 < c0) return default;
        int sw = (c1 - c0 + 1) * TerrainTiles.Tile, sd = (r1 - r0 + 1) * TerrainTiles.Tile;
        float s = (float)(1 / MissionWorld.SampleScale);
        double stride = Math.Max(texel / s, 1.0 / 16);
        // texels a power of two of the sample, the stride doubled until the map fits
        stride = Math.Pow(2, Math.Round(Math.Log2(stride)));
        while ((long)(sw / stride) * (long)(sd / stride) > MaxTexels) stride *= 2;
        int w = Math.Max(1, (int)(sw / stride)), d = Math.Max(1, (int)(sd / stride));
        int sx0 = c0 * TerrainTiles.Tile, sz0 = r0 * TerrainTiles.Tile;
        return new Placement((float)((sx0 - 0.5) * s), (float)((sz0 - 0.5) * s), (float)(w * stride * s), (float)(d * stride * s), w, d, stride, sx0, sz0);
    }

    /// <summary>The drawn surface's heights at the texels' centres (its height where the mesh's vertex for that point lies), in parallel rows.</summary>
    public static Grid HeightsFor(ITerrainSurface surf, Placement p) => HeightsFor((fx, fz) => surf.Point(fx, fz, out _).Y, p);

    /// <summary>The heights <paramref name="at"/> gives for sample coordinates, at the texels' centres (a map too big for the drawn surface whole: the simulation's bilinear ground).</summary>
    public static Grid HeightsFor(Func<double, double, float> at, Placement p)
    {
        var h = new float[p.W * p.D];
        Parallel.For(0, p.D, j =>
        {
            for (int i = 0; i < p.W; i++)
            {
                var (fx, fz) = p.Centre(i, j);
                h[j * p.W + i] = at(fx, fz);
            }
        });
        return new Grid(h, p.W, p.D, p.Texel);
    }

    /// <summary>The heights averaged over <paramref name="factor"/> x <paramref name="factor"/> texels (the sky's grid); a partial block at the far edges is left out.</summary>
    public static Grid Coarsen(Grid g, int factor)
    {
        if (factor <= 1) return g;
        int w = Math.Max(1, g.W / factor), d = Math.Max(1, g.D / factor);
        var h = new float[w * d];
        Parallel.For(0, d, z =>
        {
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                int n = 0;
                for (int j = 0; j < factor && z * factor + j < g.D; j++)
                    for (int i = 0; i < factor && x * factor + i < g.W; i++) { s += g.H[(z * factor + j) * g.W + x * factor + i]; n++; }
                h[z * w + x] = s / n;
            }
        });
        return new Grid(h, w, d, g.Texel * factor);
    }

    /// <summary>
    /// The sun's margin per texel, radians: its elevation over the terrain's horizon toward <paramref name="light"/> (game frame, toward the
    /// light, any length); +<see cref="MarginRange"/> with nothing ahead. A sun at or under the horizon is -range everywhere, one overhead +range.
    /// </summary>
    public static float[] Margin(Grid g, Vector3 light)
    {
        var m = new float[g.W * g.D];
        float lh = MathF.Sqrt(light.X * light.X + light.Z * light.Z);
        if (light.Y <= 0) { Array.Fill(m, -MarginRange); return m; }
        if (lh < 1e-5f * light.Y) { Array.Fill(m, MarginRange); return m; }
        float elev = MathF.Atan2(light.Y, lh);
        float ux = light.X / lh, uz = light.Z / lh;
        int dx = MathF.Abs(ux) > 0.9999f ? MathF.Sign(ux) : 0, dz = MathF.Abs(uz) > 0.9999f ? MathF.Sign(uz) : 0;
        float dg = MathF.Abs(MathF.Abs(ux) - MathF.Abs(uz));
        if (dx == 0 && dz == 0 && dg < 1e-4f) { dx = MathF.Sign(ux); dz = MathF.Sign(uz); }
        float bias = SunBias * g.Texel;
        float[] tan = dx != 0 || dz != 0 ? Horizons(g, dx, dz, SunSkip, bias) : MarchHorizons(g, ux, uz, elev, bias);
        for (int i = 0; i < m.Length; i++) m[i] = Math.Clamp(elev - MathF.Atan(tan[i]), -MarginRange, MarginRange);
        return m;
    }

    /// <summary>The sun's share at a margin, as the shader takes it (the disc's smoothed share over the horizon), for a pixel no wider than the disc.</summary>
    public static float Lit(float margin)
    {
        float t = Math.Clamp((margin + SunRadius) / (2 * SunRadius), 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The sky's share per texel: the mean over the eight grid directions of cos² of the horizon's elevation there (the open part of a
    /// cosine-weighted level hemisphere's slice: 1 on a plain, 0.5 between 45-degree walls).
    /// </summary>
    public static float[] Sky(Grid g)
    {
        var sum = new float[g.W * g.D];
        foreach (var (dx, dz) in Directions)
        {
            var tan = Horizons(g, dx, dz, 1, 0);
            for (int i = 0; i < sum.Length; i++)
            {
                float t = MathF.Max(tan[i], 0);
                sum[i] += 1 / (1 + t * t);
            }
        }
        for (int i = 0; i < sum.Length; i++) sum[i] /= Directions.Length;
        return sum;
    }

    private static readonly (int, int)[] Directions = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    /// <summary>The margins as R8: 0.5 the horizon, 0 and 1 the range's ends.</summary>
    public static byte[] PackMargin(float[] m)
    {
        var b = new byte[m.Length];
        for (int i = 0; i < m.Length; i++) b[i] = (byte)MathF.Round(Math.Clamp(0.5f + m[i] / (2 * MarginRange), 0, 1) * 255);
        return b;
    }

    /// <summary>A margin back from its byte, as the shader reads it.</summary>
    public static float UnpackMargin(byte b) => (b / 255f - 0.5f) * 2 * MarginRange;

    /// <summary>Shares as R8.</summary>
    public static byte[] Pack(float[] share)
    {
        var b = new byte[share.Length];
        for (int i = 0; i < share.Length; i++) b[i] = (byte)MathF.Round(Math.Clamp(share[i], 0, 1) * 255);
        return b;
    }

    /// <summary>
    /// Per texel, the steepest slope (rise over run) from it, raised <paramref name="bias"/> metres, to any texel ahead along grid direction
    /// (dx, dz), leaving out the <paramref name="skip"/> - 1 nearest; float.NegativeInfinity with nothing ahead.
    /// </summary>
    public static float[] Horizons(Grid g, int dx, int dz, int skip, float bias)
    {
        var tan = new float[g.W * g.D];
        // every line starts at a texel whose one behind lies off the grid
        var starts = new List<(int X, int Z)>();
        for (int z = 0; z < g.D; z++)
            for (int x = 0; x < g.W; x++)
            {
                int bx = x - dx, bz = z - dz;
                if (bx < 0 || bz < 0 || bx >= g.W || bz >= g.D) starts.Add((x, z));
            }
        float spacing = g.Texel * MathF.Sqrt(dx * dx + dz * dz);
        Parallel.For(0, starts.Count, () => new int[Math.Max(g.W, g.D)], (si, _, hull) =>
        {
            var (x, z) = starts[si];
            int n = int.MaxValue;
            if (dx > 0) n = Math.Min(n, g.W - x); else if (dx < 0) n = Math.Min(n, x + 1);
            if (dz > 0) n = Math.Min(n, g.D - z); else if (dz < 0) n = Math.Min(n, z + 1);
            Line(g.H, z * g.W + x, dz * g.W + dx, n, spacing, skip, bias, tan, hull);
            return hull;
        }, _ => { });
        return tan;
    }

    /// <summary>
    /// One line's horizons, from its far end back: the points ahead (from the point <paramref name="skip"/> on) kept as their upper convex
    /// hull, nearest on top; the steepest slope from a point to them is at the hull's tangent, found by bisection (the slopes along a convex
    /// chain seen from its left rise, then fall).
    /// </summary>
    private static void Line(float[] h, int start, int stride, int n, float spacing, int skip, float bias, float[] tan, int[] hull)
    {
        int top = -1;
        for (int k = n - 1; k >= 0; k--)
        {
            int j = k + skip;
            if (j < n)
            {
                float hj = h[start + j * stride];
                // drop the hull's nearest points under the line from j to the next: never a tangent for a point further back
                while (top >= 1 && Slope(j, hj, hull[top], h[start + hull[top] * stride]) <= Slope(j, hj, hull[top - 1], h[start + hull[top - 1] * stride]))
                    top--;
                hull[++top] = j;
            }
            if (top < 0) { tan[start + k * stride] = float.NegativeInfinity; continue; }
            float hk = h[start + k * stride] + bias;
            int lo = 0, hi = top;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Slope(k, hk, hull[mid], h[start + hull[mid] * stride]) < Slope(k, hk, hull[mid + 1], h[start + hull[mid + 1] * stride])) lo = mid + 1;
                else hi = mid;
            }
            tan[start + k * stride] = Slope(k, hk, hull[lo], h[start + hull[lo] * stride]) / spacing;
        }
    }

    /// <summary>Rise over run from point a to point b along a line, in heights per texel step.</summary>
    private static float Slope(int a, float ha, int b, float hb) => (hb - ha) / (b - a);

    /// <summary>The sun's horizons for an azimuth off the grid's lines: a march a texel at a time, bilinear, until the ray toward the sun clears the highest ground.</summary>
    private static float[] MarchHorizons(Grid g, float ux, float uz, float elev, float bias)
    {
        var tan = new float[g.W * g.D];
        float hmax = g.H.Max();
        float te = MathF.Tan(elev);
        Parallel.For(0, g.D, z =>
        {
            for (int x = 0; x < g.W; x++)
            {
                float h0 = g.H[z * g.W + x] + bias, best = float.NegativeInfinity;
                for (int s = SunSkip; ; s++)
                {
                    float fx = x + ux * s, fz = z + uz * s;
                    if (fx < 0 || fz < 0 || fx > g.W - 1 || fz > g.D - 1) break;
                    float run = s * g.Texel;
                    if (h0 + run * MathF.Max(te, best) > hmax) break;
                    best = MathF.Max(best, (Bilinear(g, fx, fz) - h0) / run);
                }
                tan[z * g.W + x] = best;
            }
        });
        return tan;
    }

    private static float Bilinear(Grid g, float fx, float fz)
    {
        if (g.W < 2 || g.D < 2) return g.H[0];
        int x0 = Math.Min((int)fx, g.W - 2), z0 = Math.Min((int)fz, g.D - 2);
        float tx = fx - x0, tz = fz - z0;
        float a = g.H[z0 * g.W + x0] + (g.H[z0 * g.W + x0 + 1] - g.H[z0 * g.W + x0]) * tx;
        float b = g.H[(z0 + 1) * g.W + x0] + (g.H[(z0 + 1) * g.W + x0 + 1] - g.H[(z0 + 1) * g.W + x0]) * tx;
        return a + (b - a) * tz;
    }
}
