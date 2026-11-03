using I76.Formats.Mission;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Map Edge's distance grid (MapEdge.Distance): metres from each 10 m texel to the nearest ground with no tile, the
/// exact transform checked against a brute-force search on an L of three tiles with a hole beside it.
/// </summary>
public class MapEdgeTests
{
    private static TerrainTiles Grid(params (int Row, int Col)[] cells)
    {
        var grid = Enumerable.Repeat(MissionFile.ZmapEmpty, MissionFile.ZmapGrid * MissionFile.ZmapGrid).ToArray();
        for (int k = 0; k < cells.Length; k++) grid[cells[k].Row * MissionFile.ZmapGrid + cells[k].Col] = (byte)k;
        return new TerrainTiles(new byte[cells.Length * MissionFile.TerrainTileBytes], cells.Length, grid);
    }

    [Fact]
    public void DistanceMatchesABruteForceSearch()
    {
        var t = Grid((10, 20), (10, 21), (11, 20));   // an L: (11, 21) is the hole in its bounds
        var d = MapEdge.Distance(t, out float x0, out float z0, out int w, out int h, out float texel);
        Assert.Equal(MapEdge.Texel, texel);
        Assert.Equal(2 * 64 + 2, w);
        Assert.Equal(2 * 64 + 2, h);
        Assert.Equal(20 * 640 - 10, x0);
        Assert.Equal(10 * 640 - 10, z0);
        bool Inside(int i, int j)
        {
            if (i <= 0 || j <= 0 || i >= w - 1 || j >= h - 1) return false;
            int row = 10 + (j - 1) / 64, col = 20 + (i - 1) / 64;
            return t.TileAt(row, col) != MissionFile.ZmapEmpty;
        }
        var outside = new List<(int, int)>();
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) if (!Inside(i, j)) outside.Add((i, j));
        var rnd = new Random(3);
        for (int n = 0; n < 400; n++)
        {
            int i = rnd.Next(w), j = rnd.Next(h);
            float best = outside.Min(o => MathF.Sqrt((o.Item1 - i) * (o.Item1 - i) + (o.Item2 - j) * (o.Item2 - j)));
            float want = best == 0 ? 0 : MathF.Max(best - 0.5f, 0) * texel;
            Assert.True(MathF.Abs(d[j * w + i] - want) < 0.01f, $"texel {i},{j}: {d[j * w + i]} against {want}");
        }
        // texel 33 of the corner tile: its centre 325 m in from the bounds on both sides
        Assert.Equal(325f, d[(1 + 32) * w + 1 + 32], 1);
        Assert.Equal(0f, d[(1 + 64 + 32) * w + 1 + 64 + 32]);   // in the hole
    }

    [Fact]
    public void ALargeMapTakesCoarserTexels()
    {
        // 40 x 40 cells, all one tile: 2562 texels square at 10 m, so 20 m
        var grid = Enumerable.Repeat(MissionFile.ZmapEmpty, MissionFile.ZmapGrid * MissionFile.ZmapGrid).ToArray();
        for (int r = 0; r < 40; r++) for (int c = 0; c < 40; c++) grid[r * MissionFile.ZmapGrid + c] = 0;
        var d = MapEdge.Distance(new TerrainTiles(new byte[MissionFile.TerrainTileBytes], 1, grid), out _, out _, out int w, out int h, out float texel);
        Assert.True((long)w * h <= MapEdge.MaxTexels);
        Assert.Equal(d.Length, w * h);
        Assert.Equal(20f, texel);
        Assert.Equal(40 * 32 + 2, w);
        Assert.Equal(12790f, d[(1 + 20 * 32) * w + 1 + 20 * 32], 1);   // the middle texel: its centre 640 texels from the high sides
    }

    [Fact]
    public void SettingParsesToOriginalWhenAbsentOrUnknown()
    {
        Assert.Equal(MapEdge.Original, MapEdge.Parse(null));
        Assert.Equal(MapEdge.Original, MapEdge.Parse(7));
        Assert.Equal(MapEdge.FadeOut, MapEdge.Parse(2));
        Assert.Equal(MapEdge.Hide, MapEdge.Parse(3));
        Assert.Equal(MapEdge.Original, MapEdge.Parse(4));
        Assert.Equal(["Plain*", "Haze", "Fade Out", "Hide"], MapEdge.Names);   // the original's value shows what it is, marked (ShellSettings.OriginalMark)
    }
}
