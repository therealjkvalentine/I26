using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// The modern look's baked terrain shading (TerrainShade), made once a mission from its heightfield and its fixed sun: per texel
/// of the map's tiles' bounds, the share of the sun the terrain toward it lets through (a soft edge over the sun's angle, widened so
/// a texel's step blurs) and the share of the sky the terrain round it leaves open (the horizon in eight directions, cosine
/// weighted). Each is the exact horizon along a line of texels, kept as the upper convex hull of the heights ahead of the point
/// (each point added and dropped once), so a line costs its length: the eight sky directions and the sun's are straight lines of
/// the grid (every preset's sun stands over +x: I76.Formats.Mission.Lighting), any other azimuth a march. Pure and deterministic
/// (lines in parallel, each writing only its own texels); no engine here, the tests run it on made-up heightfields.
/// </summary>
public static class TerrainShadeBake
{
    /// <summary>The soft edge's half-width round the sun's elevation, radians (about 1.1 degrees: the sun's disc is 0.27, the rest hides a 5 m texel's step).</summary>
    public const float SunSpread = 0.02f;
    /// <summary>The nearest texel ahead counted in a point's sun horizon: 1, the next one (with <see cref="SunBias"/>, a slope turned from the sun is shaded by the bake only once it is steeper than the sun is high plus a tenth, where the shading normal has already put it out of the sun).</summary>
    public const int SunSkip = 1;
    /// <summary>Metres the point is raised for its sun horizon, so a texel's own slope does not shade it.</summary>
    public const float SunBias = 0.5f;
    /// <summary>The largest texture side; a bigger map is baked at 10, 15, ... m texels.</summary>
    public const int MaxSide = 2048;

    /// <summary>A heightfield: <paramref name="H"/> row by row (index z * W + x), metres; texels <paramref name="Texel"/> metres apart.</summary>
    public sealed record Grid(float[] H, int W, int D, float Texel);

    /// <summary>The baked texture's place: its corner (game frame x, z: uv 0), its size in metres, texels across, and the samples a texel spans.</summary>
    public readonly record struct Placement(float X0, float Z0, float SizeX, float SizeZ, int W, int D, int Step);

    /// <summary>Where a map's bake lies: its tiles' bounds, a texel a sample (a block of Step x Step samples past <see cref="MaxSide"/>), uv 0 half a texel before the first one's centre.</summary>
    public static Placement PlacementFor(TerrainTiles ter)
    {
        var (r0, r1, c0, c1) = ter.Bounds();
        if (r1 < r0 || c1 < c0) return default;
        int sw = (c1 - c0 + 1) * TerrainTiles.Tile, sd = (r1 - r0 + 1) * TerrainTiles.Tile;
        int step = Math.Max(1, (Math.Max(sw, sd) + MaxSide - 1) / MaxSide);
        int w = sw / step, d = sd / step;
        float s = (float)(1 / MissionWorld.SampleScale);
        float cx = c0 * TerrainTiles.Tile + (step - 1) * 0.5f, cz = r0 * TerrainTiles.Tile + (step - 1) * 0.5f;
        return new Placement((cx - step * 0.5f) * s, (cz - step * 0.5f) * s, w * step * s, d * step * s, w, d, step);
    }

    /// <summary>The map's heights over <paramref name="p"/>: each texel the mean of its samples, an empty cell's the outside ground's (as the simulation's ground has them) or 0.</summary>
    public static Grid HeightsFor(MissionWorld world, Placement p)
    {
        var ter = world.Terrain;
        var (r0, r1, c0, c1) = ter.Bounds();
        int sx0 = c0 * TerrainTiles.Tile, sz0 = r0 * TerrainTiles.Tile, step = p.Step, tile = TerrainTiles.Tile;
        int sw = (c1 - c0 + 1) * tile, sd = (r1 - r0 + 1) * tile;
        var outside = world.Outside;
        // the samples' heights, a tile at a time (a tile's own straight from its samples, an empty one's from the outside ground)
        var raw = new ushort[sw * sd];
        Parallel.For(0, (r1 - r0 + 1) * (c1 - c0 + 1), k =>
        {
            int r = r0 + k / (c1 - c0 + 1), c = c0 + k % (c1 - c0 + 1);
            byte t = ter.TileAt(r, c);
            int ox = (c - c0) * tile, oz = (r - r0) * tile;
            if (t != MissionFile.ZmapEmpty)
            {
                var src = ter.TileSamples(t);
                for (int j = 0; j < tile; j++)
                    for (int i = 0; i < tile; i++) raw[(oz + j) * sw + ox + i] = (ushort)(src[j * tile + i] & MissionFile.HeightMask);
            }
            else if (outside is not null)
                for (int j = 0; j < tile; j++)
                    for (int i = 0; i < tile; i++) raw[(oz + j) * sw + ox + i] = (ushort)outside.Raw(sx0 + ox + i, sz0 + oz + j);
        });
        var h = new float[p.W * p.D];
        float scale = (float)MissionWorld.HeightScale / (step * step);
        Parallel.For(0, p.D, z =>
        {
            for (int x = 0; x < p.W; x++)
            {
                int sum = 0;
                for (int j = 0; j < step; j++)
                    for (int i = 0; i < step; i++) sum += raw[(z * step + j) * sw + x * step + i];
                h[z * p.W + x] = sum * scale;
            }
        });
        return new Grid(h, p.W, p.D, p.Texel());
    }

    private static float Texel(this Placement p) => p.SizeX / p.W;

    /// <summary>
    /// The sun's share per texel (0 behind the terrain, 1 clear): toward <paramref name="light"/> (game frame, toward the light, any
    /// length), against the horizon there over the elevation's soft edge. A sun at or under the horizon lights nothing, one overhead everything.
    /// </summary>
    public static float[] Sun(Grid g, Vector3 light)
    {
        var lit = new float[g.W * g.D];
        float lh = MathF.Sqrt(light.X * light.X + light.Z * light.Z);
        if (light.Y <= 0) return lit;
        if (lh < 1e-5f * light.Y) { Array.Fill(lit, 1f); return lit; }
        float elev = MathF.Atan2(light.Y, lh);
        float ux = light.X / lh, uz = light.Z / lh;
        int dx = MathF.Abs(ux) > 0.9999f ? MathF.Sign(ux) : 0, dz = MathF.Abs(uz) > 0.9999f ? MathF.Sign(uz) : 0;
        float dg = MathF.Abs(MathF.Abs(ux) - MathF.Abs(uz));
        if (dx == 0 && dz == 0 && dg < 1e-4f) { dx = MathF.Sign(ux); dz = MathF.Sign(uz); }
        float[] tan;
        if (dx != 0 || dz != 0) tan = Horizons(g, dx, dz, SunSkip, SunBias);
        else tan = MarchHorizons(g, ux, uz, elev);
        for (int i = 0; i < lit.Length; i++) lit[i] = Edge(elev, tan[i]);
        return Blur(lit, g.W, g.D);
    }

    /// <summary>
    /// A 3 x 3 binomial blur: each line toward the sun is its own horizon, so where a ridge runs nearly along the sun's way the shadow's
    /// edge steps from line to line; a texel's blur across them takes the steps out (the edge stays within a texel of where it was).
    /// </summary>
    public static float[] Blur(float[] a, int w, int d)
    {
        var t = new float[a.Length];
        var o = new float[a.Length];
        Parallel.For(0, d, z =>
        {
            for (int x = 0; x < w; x++)
                t[z * w + x] = (a[z * w + Math.Max(x - 1, 0)] + 2 * a[z * w + x] + a[z * w + Math.Min(x + 1, w - 1)]) * 0.25f;
        });
        Parallel.For(0, d, z =>
        {
            int zm = Math.Max(z - 1, 0) * w, zp = Math.Min(z + 1, d - 1) * w;
            for (int x = 0; x < w; x++) o[z * w + x] = (t[zm + x] + 2 * t[z * w + x] + t[zp + x]) * 0.25f;
        });
        return o;
    }

    /// <summary>The share of the sun's disc (widened, <see cref="SunSpread"/>) over a horizon of slope <paramref name="tan"/>, smoothed.</summary>
    public static float Edge(float elev, float tan)
    {
        float t = Math.Clamp((elev - MathF.Atan(tan)) / (2 * SunSpread) + 0.5f, 0f, 1f);
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

    /// <summary>
    /// <see cref="Sky"/> on the heights averaged over <paramref name="factor"/> x <paramref name="factor"/> texels, then spread back over
    /// the grid bilinearly: the sky's share changes over tens of metres, and the eight sweeps were most of the bake (in a loading
    /// mission, whose workers it shares, 0.5 s of 0.64 on T05 at full size).
    /// </summary>
    public static float[] SkyCoarse(Grid g, int factor)
    {
        if (factor <= 1 || g.W < 2 * factor || g.D < 2 * factor) return Sky(g);
        int w = g.W / factor, d = g.D / factor;
        var h = new float[w * d];
        for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                float s = 0;
                for (int j = 0; j < factor; j++)
                    for (int i = 0; i < factor; i++) s += g.H[(z * factor + j) * g.W + x * factor + i];
                h[z * w + x] = s / (factor * factor);
            }
        var coarse = new Grid(h, w, d, g.Texel * factor);
        var c = Sky(coarse);
        var full = new float[g.W * g.D];
        for (int z = 0; z < g.D; z++)
        {
            // a coarse texel's centre lies at (k + 0.5) * factor - 0.5 fine texels
            float fz = Math.Clamp((z + 0.5f) / factor - 0.5f, 0, d - 1);
            int z0 = Math.Min((int)fz, d - 2 < 0 ? 0 : d - 2), z1 = Math.Min(z0 + 1, d - 1);
            float tz = fz - z0;
            for (int x = 0; x < g.W; x++)
            {
                float fx = Math.Clamp((x + 0.5f) / factor - 0.5f, 0, w - 1);
                int x0 = Math.Min((int)fx, w - 2 < 0 ? 0 : w - 2), x1 = Math.Min(x0 + 1, w - 1);
                float tx = fx - x0;
                float a = c[z0 * w + x0] + (c[z0 * w + x1] - c[z0 * w + x0]) * tx;
                float b = c[z1 * w + x0] + (c[z1 * w + x1] - c[z1 * w + x0]) * tx;
                full[z * g.W + x] = a + (b - a) * tz;
            }
        }
        return full;
    }

    /// <summary>The two as RG8, a texel a row's step (R the sun's share, G the sky's).</summary>
    public static byte[] Pack(float[] sun, float[] sky)
    {
        var rg = new byte[sun.Length * 2];
        for (int i = 0; i < sun.Length; i++)
        {
            rg[2 * i] = (byte)MathF.Round(Math.Clamp(sun[i], 0, 1) * 255);
            rg[2 * i + 1] = (byte)MathF.Round(Math.Clamp(sky[i], 0, 1) * 255);
        }
        return rg;
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
    private static float[] MarchHorizons(Grid g, float ux, float uz, float elev)
    {
        var tan = new float[g.W * g.D];
        float hmax = g.H.Max();
        float te = MathF.Tan(elev);
        Parallel.For(0, g.D, z =>
        {
            for (int x = 0; x < g.W; x++)
            {
                float h0 = g.H[z * g.W + x] + SunBias, best = float.NegativeInfinity;
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
        int x0 = Math.Min((int)fx, g.W - 2), z0 = Math.Min((int)fz, g.D - 2);
        if (g.W < 2 || g.D < 2) return g.H[0];
        float tx = fx - x0, tz = fz - z0;
        float a = g.H[z0 * g.W + x0] + (g.H[z0 * g.W + x0 + 1] - g.H[z0 * g.W + x0]) * tx;
        float b = g.H[(z0 + 1) * g.W + x0] + (g.H[(z0 + 1) * g.W + x0 + 1] - g.H[(z0 + 1) * g.W + x0]) * tx;
        return a + (b - a) * tz;
    }
}
