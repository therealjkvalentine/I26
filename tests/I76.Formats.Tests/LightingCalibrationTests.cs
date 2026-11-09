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
    public void TheCardCurveIsThe1997LooksDisplayBelowItsKneeAndRollsOffAbove()
    {
        for (int i = 0; i <= 255; i++)
        {
            double s = i / 255.0, v = M.ToLinear(s);
            if (v <= M.Knee) Assert.Equal(M.ToLinear(M.Card(s)), M.CardTone(v), 9);
        }
        Assert.True(M.CardTone(1.0) < 1 && M.CardTone(M.Headroom) > 0.9999);
        for (double v = 0; v < M.Headroom; v += 0.01) Assert.True(M.CardTone(v + 0.01) > M.CardTone(v));
    }

    [Fact]
    public void TheCardCurvesTableIsTheCurveOverTheHeadroomAndAtOtherLevelsTakesThePaletteCurve()
    {
        var t = M.CardToneTable(1024, MonitorBrightness.Default);
        Assert.Equal(1024, t.Length);
        for (int i = 0; i < 1024; i += 37)
            Assert.Equal(M.CardTone(M.Headroom * M.ToLinear(i / 1023.0)), M.ToLinear(t[i]), 6);
        // a brighter level lifts every entry, as its palette curve does in the 1997 look
        var bright = M.CardToneTable(1024, 6);
        Assert.All(Enumerable.Range(1, 1000), i => Assert.True(bright[i] > t[i]));
    }

    // Oklab hue (degrees) and chroma of a linear colour
    private static (double H, double C) Hue(M.Rgb c)
    {
        double l = Math.Cbrt(0.4122214708 * c.R + 0.5363325363 * c.G + 0.0514459929 * c.B), m = Math.Cbrt(0.2119034982 * c.R + 0.6806995451 * c.G + 0.1073969566 * c.B),
            s = Math.Cbrt(0.0883024619 * c.R + 0.2817188376 * c.G + 0.6299787005 * c.B);
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, b = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return (Math.Atan2(b, a) * 180 / Math.PI, Math.Sqrt(a * a + b * b));
    }

    [Fact]
    public void ThroughTheCardCurveTheTintSolvedOnTheGroundGivesAPaintIts1997HueAndChromaAsFilmicDoesNot()
    {
        // an orange paint the solve never saw: Filmic's curve, steep in the darks and flat in the brights, moves its hue and
        // chroma with its light; the card's, near a power, carries the ground's tint over to it
        byte[] paint = Enumerable.Range(0, 16).SelectMany(i => i % 2 == 0 ? new byte[] { 200, 90, 30 } : [150, 60, 25]).ToArray();
        var ground = M.Cells(Tile((x, z) => 1000 + (int)(40 * Math.Sin(x / 9.0) + 30 * Math.Cos(z / 7.0))), 2, Sun(0.785), Luma, 0, SunLight, SkyLight);
        var car = Car(0.785);
        double Error(M.ToneCurve curve)
        {
            var r = M.Solve(Sand, ground, car, curve);
            var (h0, c0) = Hue(M.Target(paint, car));
            var (h1, c1) = Hue(M.Modern(paint, car, r.Exposure, r.ObjectTint, r.Sky, curve));
            return Math.Abs(h1 - h0) + 100 * Math.Abs(c1 / c0 - 1);
        }
        double card = Error(M.ToneCurve.Card), filmic = Error(M.ToneCurve.Filmic);
        Assert.True(card < 4, $"card {card:0.00}, filmic {filmic:0.00}");
        Assert.True(card < filmic / 2, $"card {card:0.00}, filmic {filmic:0.00}");
    }

    [Fact]
    public void TheCardCurvesSolveShowsTheGroundAsThe1997LookDoes()
    {
        var cells = M.Cells(Tile((x, z) => 1000 + (int)(40 * Math.Sin(x / 9.0) + 30 * Math.Cos(z / 7.0))), 2, Sun(0.785), Luma, 0, SunLight, SkyLight);
        var r = M.Solve(Sand, cells, Car(0.785), M.ToneCurve.Card);
        var target = M.Target(Sand, cells);
        var shown = M.Modern(Sand, cells, r.Exposure, r.GroundTint, 1, M.ToneCurve.Card);
        for (int c = 0; c < 3; c++) Assert.Equal(target[c], shown[c], 3);
        // the card's curve and the ramp are both near a power of the light: the tint is little more than the modern light's colour
        Assert.InRange(r.GroundTint.B / r.GroundTint.R, 0.8, 1.6);
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

    // a night ramp like T06's: steep from a floor near black
    private static readonly byte[] NightLuma = [12, 22, 22, 34, 48, 48, 63, 80, 80, 99, 99, 118, 118, 139, 139, 160, 160, 182, 182, 182, 205, 205, 230, 230, 255, 255, 255, 255, 255, 255, 255, 255];

    // a night map's ground texture: 64 texels of two dark navies
    private static readonly byte[] Navy = Enumerable.Range(0, 64).SelectMany(i => i % 3 == 0 ? new byte[] { 10, 12, 28 } : [16, 19, 40]).ToArray();

    [Fact]
    public void TheNightMoonShowsADarkGroundThroughTheCardAsFilmicShowsIt()
    {
        var night = Lighting.Presets[0];
        var moon = new M.Rgb(0.318 * 0.122, 0.448 * 0.122, 0.122);   // the modern moon's colour times 1.3 times the preset's intensity
        var sky = new M.Rgb(0.002, 0.002, 0.006);
        var cells = M.Cells(Tile((x, z) => (x * 7 + z * 3) % 40), 8, night.Direction, NightLuma, night.Ambient, moon, sky, M.CardFlatWeight);
        var glint = moon * M.GroundGlint;
        double k = M.NightMoon(Navy, cells, glint);
        Assert.InRange(k, 0.2, 0.8);
        // the glint is most of the night ground's light, so it lifts the share: without it the solve sits deeper in the card's toe
        Assert.True(k > M.NightMoon(Navy, cells, new M.Rgb(0, 0, 0)));
        // by day, in the brights, the card's curve is the darker: the sun keeps its whole light
        Assert.Equal(1, M.NightMoon(Sand, M.Cells(Tile((_, _) => 1000), 8, Sun(0.785), Luma, 0, SunLight, SkyLight), new M.Rgb(0, 0, 0)));
    }

    [Fact]
    public void TheNightRoadShowsAGreyAgainstTheFlatGroundAsTheRampDoes()
    {
        var night = Lighting.Presets[0];
        double ly = night.Direction.Y, moon = 1.3 * night.Intensity * 0.46;
        double k = M.NightRoad(NightLuma, night.Intensity, ly, night.Ambient, moon);
        Assert.InRange(k, 0.005, 0.1);
        // a mid grey as the 1997 look shows a road (LUMA[round(31 I Ly)]) against flat ground (LUMA[round(31 (Ly fade + ambient))]), each
        // through the card; the modern road at that share of the ground's moonlight, through the card's curve
        const double grey = 0.18;
        double road = NightLuma[(int)Math.Round(31 * night.Intensity * ly)] / 255.0, ground = NightLuma[(int)Math.Round(31 * (ly * M.TypicalFade + night.Ambient))] / 255.0;
        double want = M.ToLinear(M.ToSrgb(grey) * road) / M.ToLinear(M.ToSrgb(grey) * ground);
        double v = grey * moon * ly;
        Assert.Equal(want, M.CardTone(k * v) / M.CardTone(v), 3);
    }

    [Fact]
    public void ARoadLitNoDarkerThanTheGroundKeepsTheGroundsLight()
    {
        Assert.Equal(1, M.NightRoad(Luma, 1, 0.7, 0, 0.5));
    }

    // Oklab from linear RGB: lightness, and chroma as a share of it (a hue-keeping dimming leaves C/L where it was)
    private static (double L, double CL, double Hue) Oklab(M.Rgb c)
    {
        double l = Math.Cbrt(0.4122214708 * c.R + 0.5363325363 * c.G + 0.0514459929 * c.B);
        double m = Math.Cbrt(0.2119034982 * c.R + 0.6806995451 * c.G + 0.1073969566 * c.B);
        double s = Math.Cbrt(0.0883024619 * c.R + 0.2817188376 * c.G + 0.6299787005 * c.B);
        double L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        double a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, b = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
        return (L, Math.Sqrt(a * a + b * b) / L, Math.Atan2(b, a) * 180 / Math.PI);
    }

    [Fact]
    public void TheMoonsLightOnTheNightGroundKeepsTheGroundsOwnColour()
    {
        // a brown ground (the texels of a desert map's dirt); the modern moon's light, blue, at T06's solved share
        var brown = new M.Rgb(0.0116, 0.0048, 0.0024);
        var moon = new M.Rgb(0.318, 0.448, 1.0) * (1.3 * 0.094 * 0.37 * 0.8);
        var (L0, cl0, hue0) = Oklab(brown);
        // the moon's own colour, with its glint in it: a dark blue-grey, charcoal
        var (Lb, clb, hueb) = Oklab(M.NightGroundLight(brown, moon, M.GroundGlint, 0));
        Assert.True(hueb < -60 && hueb > -120, $"hue {hueb}");
        // at the whole chroma: the ground's own hue and colour share, darkened
        var kept = M.NightGroundLight(brown, moon, M.GroundGlint, M.NightChroma);
        var (L1, cl1, hue1) = Oklab(kept);
        Assert.InRange(hue1, hue0 - 3, hue0 + 3);
        Assert.InRange(cl1 / cl0, 0.9, 1.1);
        Assert.True(L1 < L0 / 2);
        // the chroma leaves the light's luminance about where it was (within a few percent: the channels' products), so the night stays as dark
        Assert.InRange(kept.Luminance / M.NightGroundLight(brown, moon, M.GroundGlint, 0).Luminance, 0.95, 1.05);
        Assert.InRange(kept.Luminance / M.NightGroundLight(brown, moon, M.GroundGlint, 0.6).Luminance, 0.95, 1.05);
        // 0.6 and 0.8 keep about those shares of the ground's colour
        Assert.InRange(Oklab(M.NightGroundLight(brown, moon, M.GroundGlint, 0.6)).CL / cl0, 0.45, 0.75);
        Assert.InRange(Oklab(M.NightGroundLight(brown, moon, M.GroundGlint, 0.8)).CL / cl0, 0.65, 0.95);
    }

    [Fact]
    public void TheWarmMoonTurnsAGreyGroundBrownAtTheSameLuminance()
    {
        // T06's ground is a near-neutral grey: kept as it is with warm 0, the cutscene's dark brown at 1 (the default), moonlight's
        // warm white, the first target, about half way
        var grey = new M.Rgb(0.010, 0.010, 0.011);
        var moon = new M.Rgb(0.318, 0.448, 1.0) * 0.045;
        var kept = M.NightGroundLight(grey, moon, M.GroundGlint, 1);
        var warm = M.NightGroundLight(grey, moon, M.GroundGlint, 1, 1);
        var half = M.NightGroundLight(grey, moon, M.GroundGlint, 1, 0.5);
        Assert.True(Oklab(kept).CL < 0.06);
        Assert.InRange(Oklab(warm).CL, 0.13, 0.20);
        Assert.InRange(Oklab(warm).Hue, 60, 80);
        Assert.InRange(Oklab(half).CL, 0.05, 0.10);   // moonlight's own gave 0.06 to 0.07 on this ground
        Assert.InRange(warm.Luminance / kept.Luminance, 0.95, 1.05);
        Assert.Equal(1, M.Moonlight.Luminance, 2);
        Assert.Equal(1, M.CutsceneBrown.Luminance, 2);
        Assert.Equal(1, M.NightWarm);
    }

    [Fact]
    public void TheNightSkyAndTheGroundsColourAreTheConsolesLevels()
    {
        // the sky darker than the quarter that left it at twice the ground's lightness; the ground's colour whole
        Assert.InRange(M.NightSky, 0.02, 0.125);
        Assert.Equal(1, M.NightChroma);
    }
}
