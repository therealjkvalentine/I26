using I76.Formats.Imaging;
using I76.Formats.Mission;
using Xunit;
using M = I76.Formats.Imaging.LookColourMatch;

namespace I76.Formats.Tests;

public class LookColourMatchTests
{
    // one 128x128 tile at the grid's corner, heights from h(sx, sz)
    private static TerrainTiles Tile(Func<int, int, int> h)
    {
        var grid = Enumerable.Repeat(MissionFile.ZmapEmpty, MissionFile.ZmapGrid * MissionFile.ZmapGrid).ToArray();
        grid[0] = 0;
        var t = new TerrainTiles(new byte[TerrainTiles.Tile * TerrainTiles.Tile * 2], 1, grid);
        for (int z = 0; z < TerrainTiles.Tile; z++)
            for (int x = 0; x < TerrainTiles.Tile; x++) t.SetHeight(x, z, h(x, z));
        return t;
    }

    // a sandy ground texture: 64 texels of two browns
    private static readonly byte[] Sand = Enumerable.Range(0, 64).SelectMany(i => i % 3 == 0 ? new byte[] { 150, 110, 80 } : [190, 150, 110]).ToArray();

    // a ramp like the missions' .lum tables: a floor well above black, about 0.85 for a 45-degree sun on flat ground
    private static readonly byte[] Luma = Enumerable.Range(0, 32).Select(k => (byte)Math.Round(255 * (0.15 + 0.85 * Math.Pow(k / 31.0, 0.6)))).ToArray();

    private static System.Numerics.Vector3 Sun(double zenith) => new((float)Math.Sin(zenith), (float)Math.Cos(zenith), 0);

    [Fact]
    public void FilmicIsGodotsCurveWithWhiteAtOne()
    {
        Assert.Equal(0, M.Filmic(0), 3);
        Assert.Equal(1, M.Filmic(1), 6);
        Assert.Equal(1, M.Filmic(4), 6);
        // Hable's curve with Godot's exposure bias of 2: mid grey 0.18 shows at 0.306
        Assert.Equal(0.306, M.Filmic(0.18), 3);
    }

    [Fact]
    public void FlatGroundIsLitByTheRampAtLyTimesTheFade()
    {
        var cells = M.Cells(Tile((_, _) => 1000), 4, Sun(0.785), Luma, 0, new(1.3, 1.3, 1.3), new(0.2, 0.3, 0.3));
        var c = Assert.Single(cells);
        Assert.Equal(M.Ramp(Luma, Math.Cos(0.785) * M.TypicalFade), c.Ramp, 9);
        Assert.Equal(1.3 * Math.Cos(0.785) + 0.3, c.Light.G, 6);
    }

    [Fact]
    public void ASlopeAcrossTheLightKeepsTheFlatRampAsTheEngineDoesButDarkensInTheModernLight()
    {
        // rising 1 in 2 along Z, the sun in the X-Y plane: the engine's unnormalised dot is Ly, as on flat ground
        var cells = M.Cells(Tile((_, z) => 500 + 25 * z), 4, Sun(0.785), Luma, 0, new(1, 1, 1), new(0, 0, 0));
        var c = Assert.Single(cells);
        Assert.Equal(M.Ramp(Luma, Math.Cos(0.785) * M.TypicalFade), c.Ramp, 9);
        Assert.Equal(Math.Cos(0.785) / Math.Sqrt(1.25), c.Light.R, 3);
    }

    [Fact]
    public void TheSolvedExposureAndTintShowTheModernGroundAsThe1997LookDoes()
    {
        var cells = M.Cells(Tile((x, z) => 1000 + (int)(40 * Math.Sin(x / 9.0) + 30 * Math.Cos(z / 7.0))), 2, Sun(0.785), Luma, 0,
            new(1.3, 1.25, 1.14), new(0.24, 0.35, 0.33));
        var (e, t) = M.Solve(Sand, cells);
        var target = M.Target(Sand, cells);
        var shown = M.Modern(Sand, cells, e, t);
        for (int c = 0; c < 3; c++) Assert.Equal(target[c], shown[c], 3);
        Assert.InRange(e, M.MinExposure, M.MaxExposure);
    }

    [Fact]
    public void ALowSunGetsALowerExposure()
    {
        // the ramp falls with the sun faster than the modern light, whose sky ambient stays: at dusk the match darkens more
        M.Rgb sun = new(1.3, 1.25, 1.14), sky = new(0.24, 0.35, 0.33);
        var flat = Tile((_, _) => 1000);
        var (high, _) = M.Solve(Sand, M.Cells(flat, 8, Sun(0.785), Luma, 0, sun, sky));
        var (low, _) = M.Solve(Sand, M.Cells(flat, 8, Sun(1.309), Luma, 0, sun, sky));
        Assert.True(low < high, $"{low} < {high}");
    }

    [Fact]
    public void NoCellsLeaveTheModernLookAsItIs()
    {
        var (e, t) = M.Solve(Sand, []);
        Assert.Equal(1, e);
        Assert.Equal(new M.Rgb(1, 1, 1), t);
    }
}
