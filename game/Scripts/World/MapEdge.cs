using I76.Formats.Assembly;
using I76.Formats.Mission;

namespace I76.Game.World;

/// <summary>
/// Graphic Detail's Map Edge (the port's own, modern look only). A level's boundary is its heightfield's own outer tiles:
/// most missions are bowls whose rim rises 100-170 m to a cliff band along the last tiles, and the modern look's ring
/// (HorizonTerrain) carries on from the rim's height. Original (Plain on the pads, marked as the original's) draws that as it is. Haze lays a distance haze, toward the
/// modern sky's horizon colour, over the ground in a band inside the edge and over the ring beyond it, so the walls and
/// the ranges past them read as far land; Fade Out takes them wholly into that colour, the boundary dissolving into the
/// sky's horizon. Hide draws the walls, and the plateau behind them, down at the plain of their foot (EdgeWalls: the ground
/// no car reaches that is joined to the map's edge; the terrain's vertex shader holds it to the foot grid, the ring beyond
/// starts from the plain), so the ground runs on level to the ring's ranges. Drawing only (map_edge.gdshaderinc): the
/// heightfield, its collision and the simulation are unchanged, and the playable interior away from the edge is untouched.
/// </summary>
public static class MapEdge
{
    public const int Original = 0, Haze = 1, FadeOut = 2, Hide = 3;
    public static readonly string[] Names = ["Plain" + I76.Game.Shell.ShellSettings.OriginalMark, "Haze", "Fade Out", "Hide"];

    /// <summary>shell.cfg's [port] map_edge: absent or out of range, Original.</summary>
    public static int Parse(int? value) => value is >= Original and <= Hide ? value.Value : Original;

    // Hide's walls per mission, made once for the terrain's chunks and the ring (either may ask first, on its own thread)
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MissionWorld, Lazy<EdgeWalls?>> _walls = new();

    /// <summary>
    /// Hide's walls of a mission (<see cref="EdgeWalls"/>) from the surface the modern look draws, made on the first call and
    /// kept with the world; null for a map too big for one surface (it has no ring either). I76_MAP_EDGE_RIDGES=0 hides the
    /// ridges between drivable regions as well, for comparison.
    /// </summary>
    public static EdgeWalls? WallsFor(MissionWorld world, Func<ITerrainSurface> surface) =>
        _walls.GetValue(world, w => new Lazy<EdgeWalls?>(() => w.Terrain.Huge ? null : EdgeWalls.Build(w.Terrain, surface(), keepRidges: System.Environment.GetEnvironmentVariable("I76_MAP_EDGE_RIDGES") != "0"))).Value;

    /// <summary>Metres a texel of the edge-distance grid; coarser by twos on a map so large that it would pass <see cref="MaxTexels"/>.</summary>
    public const float Texel = 10f;
    public const int MaxTexels = 1 << 21;

    /// <summary>
    /// For each texel (<paramref name="texel"/> metres) over the map's tiles' bounds and one texel round them (row by row along game Z from
    /// the low corner), the distance in metres from its centre to the nearest ground with no tile (an empty grid cell, or past
    /// the bounds), 0 on such ground. <paramref name="x0"/>, <paramref name="z0"/>: the grid's low corner (game frame).
    /// </summary>
    public static float[] Distance(TerrainTiles terrain, out float x0, out float z0, out int width, out int depth, out float texel)
    {
        var (r0, r1, c0, c1) = terrain.Bounds();
        texel = Texel;
        int per = (int)(MissionFile.TerrainTile * TerrainNode.SampleSize / Texel);   // texels a tile
        if (r1 < r0) { x0 = z0 = 0; width = depth = 1; return [0f]; }
        while (per > 1 && (long)((c1 - c0 + 1) * per + 2) * ((r1 - r0 + 1) * per + 2) > MaxTexels) { per /= 2; texel *= 2; }
        width = (c1 - c0 + 1) * per + 2;
        depth = (r1 - r0 + 1) * per + 2;
        x0 = c0 * per * texel - texel;
        z0 = r0 * per * texel - texel;
        int w = width, h = depth;
        const float Far = 1e12f;
        var f = new float[w * h];   // squared distance in texels, 0 on ground with no tile
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                bool inside = i > 0 && j > 0 && i < w - 1 && j < h - 1
                    && terrain.TileAt(r0 + (j - 1) / per, c0 + (i - 1) / per) != MissionFile.ZmapEmpty;
                f[j * w + i] = inside ? Far : 0f;
            }
        // the exact Euclidean transform, separably (Felzenszwalb and Huttenlocher): columns, then rows
        var line = new float[Math.Max(w, h)];
        var outp = new float[Math.Max(w, h)];
        var v = new int[Math.Max(w, h)];
        var z = new float[Math.Max(w, h) + 1];
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++) line[j] = f[j * w + i];
            Transform(line, h, outp, v, z);
            for (int j = 0; j < h; j++) f[j * w + i] = outp[j];
        }
        for (int j = 0; j < h; j++)
        {
            Array.Copy(f, j * w, line, 0, w);
            Transform(line, w, outp, v, z);
            Array.Copy(outp, 0, f, j * w, w);
        }
        // texel centres to the edge of the ground with no tile: half a texel nearer than its centre
        for (int k = 0; k < f.Length; k++) f[k] = f[k] <= 0 ? 0 : MathF.Max(MathF.Sqrt(f[k]) - 0.5f, 0) * texel;
        return f;
    }

    // one line of the squared-distance transform: d[q] = min over p of (q - p)^2 + f[p]
    private static void Transform(float[] f, int n, float[] d, int[] v, float[] z)
    {
        int k = 0;
        v[0] = 0; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            float s;
            while (true)
            {
                int p = v[k];
                s = (f[q] + q * q - (f[p] + p * p)) / (2f * (q - p));
                if (s <= z[k] && k > 0) { k--; continue; }
                break;
            }
            if (s <= z[k]) { v[0] = q; z[0] = float.NegativeInfinity; z[1] = float.PositiveInfinity; k = 0; continue; }
            k++;
            v[k] = q; z[k] = s; z[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            float dq = q - v[k];
            d[q] = dq * dq + f[v[k]];
        }
    }
}
