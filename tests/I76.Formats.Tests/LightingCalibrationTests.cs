using I76.Formats.Imaging;
using I76.Formats.Mission;
using Xunit;
using M = I76.Formats.Imaging.LightingCalibration;

namespace I76.Formats.Tests;

public class LightingCalibrationTests
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
        Assert.Equal(1.3 * Math.Cos(0.785) + 0.3, c.Light(1).G, 6);
    }

    [Fact]
    public void ASlopeAcrossTheLightKeepsTheFlatRampAsTheEngineDoesButDarkensInTheModernLight()
    {
        // rising 1 in 2 along Z, the sun in the X-Y plane: the engine's unnormalised dot is Ly, as on flat ground
        var cells = M.Cells(Tile((_, z) => 500 + 25 * z), 4, Sun(0.785), Luma, 0, new(1, 1, 1), new(0, 0, 0));
        var c = Assert.Single(cells);
        Assert.Equal(M.Ramp(Luma, Math.Cos(0.785) * M.TypicalFade), c.Ramp, 9);
        Assert.Equal(Math.Cos(0.785) / Math.Sqrt(1.25), c.Light(1).R, 3);
    }

    private static readonly M.Rgb SunLight = new(1.3, 1.25, 1.14), SkyLight = new(0.24, 0.35, 0.33), Below = new(0.03, 0.02, 0.01);

    private static List<M.Cell> Car(double zenith) => M.ObjectCells(Sun(zenith), Luma, 1, 0, SunLight, SkyLight, Below);

    [Fact]
    public void TheSolvedExposureAndTintShowTheModernGroundAsThe1997LookDoes()
    {
        var cells = M.Cells(Tile((x, z) => 1000 + (int)(40 * Math.Sin(x / 9.0) + 30 * Math.Cos(z / 7.0))), 2, Sun(0.785), Luma, 0, SunLight, SkyLight);
        var r = M.Solve(Sand, cells, Car(0.785));
        var target = M.Target(Sand, cells);
        var shown = M.Modern(Sand, cells, r.Exposure, r.GroundTint);
        for (int c = 0; c < 3; c++) Assert.Equal(target[c], shown[c], 3);
        Assert.InRange(r.Exposure, M.MinExposure, M.MaxExposure);
    }

    [Fact]
    public void AnObjectsFacesTurnedFromTheSunSitAtTheRampsFloorIn1997AndOnTheSkyLightInTheModernLook()
    {
        var car = Car(0.785);
        Assert.Equal(17, car.Count);
        Assert.Equal(1, car.Sum(c => c.Count), 9);
        // a face turned from the sun: the floor, no sun, the sky from its side and the ground below
        // (with the sun 45 degrees up along +X: the upright faces toward -X, and those square to the sun as near 0 falls)
        var shade = car.Where(c => c.Shade).ToList();
        Assert.InRange(shade.Count, 3, 5);
        foreach (var back in shade)
        {
            Assert.Equal(0.05, back.Count, 9);
            Assert.Equal(0, back.Sun.G);
            Assert.Equal(M.Ramp(Luma, 0), back.Ramp);
            Assert.Equal((SkyLight.R + Below.R) / 2 * M.ObjectSky, back.Sky.R, 9);
        }
        // the roof: the sun at its zenith angle through the fade, the whole sky
        var roof = car[0];
        Assert.False(roof.Shade);
        Assert.Equal(M.Ramp(Luma, M.NearFade * Math.Cos(0.785)), roof.Ramp, 9);
        Assert.Equal(SunLight.G * Math.Cos(0.785), roof.Sun.G, 5);
        Assert.Equal(SkyLight.B * M.ObjectSky, roof.Sky.B, 9);
    }

    [Fact]
    public void TheObjectsShareOfTheSkyLightGivesTheir1997ContrastAndTheirTintTheir1997Colour()
    {
        var ground = M.Cells(Tile((_, _) => 1000), 8, Sun(0.785), Luma, 0, SunLight, SkyLight);
        var car = Car(0.785);
        var r = M.Solve(Sand, ground, car);
        Assert.InRange(r.Sky, M.MinSky, M.MaxSky);
        var shade = car.Where(c => c.Shade).ToList();
        double Ratio(M.Rgb a, M.Rgb b) => a.Luminance / b.Luminance;
        var one = new M.Rgb(1, 1, 1);
        Assert.Equal(Ratio(M.Target(Sand, shade), M.Target(Sand, car)), Ratio(M.Modern(Sand, shade, r.Exposure, one, r.Sky), M.Modern(Sand, car, r.Exposure, one, r.Sky)), 3);
        var target = M.Target(Sand, car);
        var shown = M.Modern(Sand, car, r.Exposure, r.ObjectTint, r.Sky);
        for (int c = 0; c < 3; c++) Assert.Equal(target[c], shown[c], 3);
    }

    [Fact]
    public void ALowSunGetsALowerExposure()
    {
        // the ramp falls with the sun faster than the modern light, whose sky light stays: at dusk the calibration darkens more
        var flat = Tile((_, _) => 1000);
        var high = M.Solve(Sand, M.Cells(flat, 8, Sun(0.785), Luma, 0, SunLight, SkyLight), Car(0.785)).Exposure;
        var low = M.Solve(Sand, M.Cells(flat, 8, Sun(1.309), Luma, 0, SunLight, SkyLight), Car(1.309)).Exposure;
        Assert.True(low < high, $"{low} < {high}");
    }

    [Fact]
    public void NoCellsLeaveTheModernLookAsItIs()
    {
        Assert.Equal(M.Result.None, M.Solve(Sand, [], Car(0.785)));
        // no object cells: the objects keep the whole sky light, untinted
        var r = M.Solve(Sand, M.Cells(Tile((_, _) => 1000), 8, Sun(0.785), Luma, 0, SunLight, SkyLight), []);
        Assert.Equal(1, r.Sky);
        Assert.Equal(new M.Rgb(1, 1, 1), r.ObjectTint);
    }
}
