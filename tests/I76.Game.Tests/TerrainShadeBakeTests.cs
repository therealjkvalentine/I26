using System.Linq;
using System.Numerics;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's baked terrain shading (TerrainShadeBake) on made-up heightfields.</summary>
public class TerrainShadeBakeTests
{
    private const float Texel = 5f;

    private static TerrainShadeBake.Grid Flat(int w, int d, float h = 0) { var a = new float[w * d]; Array.Fill(a, h); return new(a, w, d, Texel); }

    /// <summary>A sun over +x (the presets' azimuth) at <paramref name="deg"/> degrees.</summary>
    private static Vector3 Sun(float deg) => new(MathF.Cos(deg * MathF.PI / 180), MathF.Sin(deg * MathF.PI / 180), 0);

    /// <summary>A wall 20 m high, one texel thick, across the grid at x = 60: its shadow falls toward -x, as long as the sun's elevation makes it.</summary>
    [Theory]
    [InlineData(15f)]   // presets 2 and 7 (dawn, dusk): 75 degrees from the zenith
    [InlineData(35f)]   // preset 3
    [InlineData(45f)]   // preset 5
    public void AWallCastsAShadowAsLongAsTheSunsElevationMakesIt(float deg)
    {
        var g = Flat(100, 8);
        const int wall = 60;
        const float height = 20f;
        for (int z = 0; z < g.D; z++) g.H[z * g.W + wall] = height;
        var sun = TerrainShadeBake.Sun(g, Sun(deg));
        float length = (height - TerrainShadeBake.SunBias) / MathF.Tan(deg * MathF.PI / 180);
        int row = 4 * g.W;
        // the half-lit point, between texels
        float cross = float.NaN;
        for (int x = wall - 1; x > 0; x--)
            if (sun[row + x - 1] >= 0.5f && sun[row + x] < 0.5f)
            {
                float a = sun[row + x - 1], b = sun[row + x];
                cross = (x - 1 + (0.5f - a) / (b - a));
                break;
            }
        Assert.False(float.IsNaN(cross), "no edge");
        Assert.InRange((wall - cross) * Texel, length - Texel, length + Texel);
        // dark well inside it (past the blur's texel at the wall's foot), lit well past it and on the sun's side
        Assert.True(sun[row + wall - 2] < 0.02f);
        Assert.True(sun[row + wall - Math.Max(2, (int)(0.7f * length / Texel))] < 0.02f);
        Assert.True(sun[row + Math.Max(0, wall - (int)(1.3f * length / Texel) - 3)] > 0.98f);
        for (int x = wall + 1; x < g.W; x++) Assert.Equal(1f, sun[row + x]);
    }

    [Fact]
    public void FlatGroundIsUnshadowedAndOpenToTheSky()
    {
        var g = Flat(40, 30, 123.4f);
        foreach (float deg in new[] { 15f, 20f, 35f, 80f })
            Assert.All(TerrainShadeBake.Sun(g, Sun(deg)), v => Assert.Equal(1f, v));
        Assert.All(TerrainShadeBake.Sky(g), v => Assert.Equal(1f, v, 5));
    }

    [Fact]
    public void ASunUnderTheHorizonLightsNothingAndOneOverheadEverything()
    {
        var g = Flat(10, 10);
        g.H[55] = 50;
        Assert.All(TerrainShadeBake.Sun(g, new Vector3(1, -0.1f, 0)), v => Assert.Equal(0f, v));
        Assert.All(TerrainShadeBake.Sun(g, new Vector3(0, 1, 0)), v => Assert.Equal(1f, v));
    }

    [Fact]
    public void APitIsLessOpenToTheSkyThanThePlain()
    {
        var g = Flat(60, 60, 100);
        // a pit 40 m across and 20 m deep in the middle
        for (int z = 26; z < 34; z++)
            for (int x = 26; x < 34; x++)
                g.H[z * g.W + x] = 80;
        var sky = TerrainShadeBake.Sky(g);
        float pit = sky[30 * g.W + 30], plain = sky[5 * g.W + 5];
        Assert.Equal(1f, plain, 5);
        Assert.True(pit < 0.8f, $"pit {pit}");
        // the walls' foot less open than the middle of the floor
        Assert.True(sky[30 * g.W + 26] < pit, $"foot {sky[30 * g.W + 26]}, middle {pit}");
    }

    /// <summary>The hull's horizons are the brute-force maximum over every texel ahead, in each of the eight directions.</summary>
    [Fact]
    public void HorizonsAreTheSteepestSlopeToAnyTexelAhead()
    {
        var rnd = new Random(76);
        var g = Flat(23, 17);
        for (int i = 0; i < g.H.Length; i++) g.H[i] = (float)(rnd.NextDouble() * 40);
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) })
            foreach (var (skip, bias) in new[] { (1, 0f), (1, 0.5f), (2, 0f) })
            {
                var tan = TerrainShadeBake.Horizons(g, dx, dz, skip, bias);
                float step = Texel * MathF.Sqrt(dx * dx + dz * dz);
                for (int z = 0; z < g.D; z++)
                    for (int x = 0; x < g.W; x++)
                    {
                        float best = float.NegativeInfinity, h0 = g.H[z * g.W + x] + bias;
                        for (int s = skip; ; s++)
                        {
                            int ax = x + dx * s, az = z + dz * s;
                            if (ax < 0 || az < 0 || ax >= g.W || az >= g.D) break;
                            best = MathF.Max(best, (g.H[az * g.W + ax] - h0) / (s * step));
                        }
                        if (float.IsNegativeInfinity(best)) Assert.True(float.IsNegativeInfinity(tan[z * g.W + x]));
                        else Assert.Equal(best, tan[z * g.W + x], 4);
                    }
            }
    }

    [Fact]
    public void TheBakeIsDeterministic()
    {
        var rnd = new Random(1997);
        var g = Flat(300, 200);
        for (int i = 0; i < g.H.Length; i++) g.H[i] = (float)(rnd.NextDouble() * 15 + 40 * Math.Sin(i % 300 * 0.05) * Math.Cos(i / 300 * 0.07));
        byte[] Bake() => TerrainShadeBake.Pack(TerrainShadeBake.Sun(g, Sun(20)), TerrainShadeBake.SkyCoarse(g, 2));
        var a = Bake();
        for (int run = 0; run < 3; run++) Assert.Equal(a, Bake());
        // an azimuth off the grid's lines (the march) too
        var off = new Vector3(0.8f, 0.4f, 0.45f);
        Assert.Equal(TerrainShadeBake.Sun(g, off), TerrainShadeBake.Sun(g, off));
    }

    /// <summary>The march an azimuth off the grid's lines takes agrees with the sweep's horizons for one on them (a sun turned a hair off +x).</summary>
    [Fact]
    public void TheMarchAgreesWithTheSweep()
    {
        var g = Flat(80, 6);
        for (int z = 0; z < g.D; z++) { g.H[z * g.W + 50] = 30; g.H[z * g.W + 51] = 30; }
        var swept = TerrainShadeBake.Sun(g, Sun(20));
        var marched = TerrainShadeBake.Sun(g, new Vector3(Sun(20).X, Sun(20).Y, 0.01f));
        for (int x = 0; x < g.W; x++) Assert.InRange(marched[3 * g.W + x] - swept[3 * g.W + x], -0.1f, 0.1f);
    }
}

/// <summary>The bake on every stock map: its size, and its time on the build's worker against the load's budget (TerrainShade).</summary>
public class TerrainShadeBakeMapTests(Xunit.Abstractions.ITestOutputHelper log)
{
    [I76.Formats.Tests.InstallFact]
    [Trait("Load", "Sensitive")]
    public void EveryStockMapBakesWellInsideTheLoadsBudget()
    {
        var vfs = I76.Formats.Tests.TestInstall.Vfs;
        long worst = 0;
        foreach (var f in Directory.EnumerateFiles(I76.Formats.Tests.TestInstall.Install.MissionDir!).Where(I76.Formats.Mission.MapFileSet.IsStockMission).OrderBy(x => x))
        {
            var world = new I76.Formats.Assembly.MissionWorld(vfs, new I76.Formats.Assembly.ModelAssembler(vfs), f);
            var settings = I76.Formats.Mission.WorldSettings.Read(world.Mission);
            var light = I76.Formats.Mission.Lighting.ForHour(settings?.Hour ?? 12);
            var p = TerrainShadeBake.PlacementFor(world.Terrain);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var g = TerrainShadeBake.HeightsFor(world, p);
            long th = sw.ElapsedMilliseconds;
            var sun = TerrainShadeBake.Sun(g, light.Direction);
            long ts = sw.ElapsedMilliseconds;
            var sky = TerrainShadeBake.SkyCoarse(g, 2);
            long tk = sw.ElapsedMilliseconds;
            TerrainShadeBake.Pack(sun, sky);
            long t = sw.ElapsedMilliseconds;
            worst = Math.Max(worst, t);
            log.WriteLine($"{Path.GetFileNameWithoutExtension(f),-8} {(light.IsDay ? "day  " : "night")} {p.W,5} x {p.D,-5} step {p.Step}  heights {th,4} ms  sun {ts - th,4} ms  sky {tk - ts,4} ms  total {t,4} ms  mean sun {sun.Average():0.000} sky {sky.Average():0.000}");
        }
        Assert.True(worst < 300, $"worst {worst} ms");
    }
}
