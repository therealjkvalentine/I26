using System.Linq;
using System.Numerics;
using I76.Game.Shell;
using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The modern look's baked terrain shadows (TerrainShadeBake) on made-up heightfields, and the Terrain Shadows row.</summary>
public class TerrainShadeBakeTests
{
    private const float Texel = 2.5f;

    private static TerrainShadeBake.Grid Flat(int w, int d, float h = 0, float texel = Texel) { var a = new float[w * d]; Array.Fill(a, h); return new(a, w, d, texel); }

    /// <summary>A sun over +x (the presets' azimuth) at <paramref name="deg"/> degrees.</summary>
    private static Vector3 Sun(float deg) => new(MathF.Cos(deg * MathF.PI / 180), MathF.Sin(deg * MathF.PI / 180), 0);

    /// <summary>The margin as the shader reads it back from its byte, then its share (a pixel no wider than the sun's disc).</summary>
    private static float[] Shares(float[] margin) => TerrainShadeBake.PackMargin(margin).Select(b => TerrainShadeBake.Lit(TerrainShadeBake.UnpackMargin(b))).ToArray();

    /// <summary>The horizon crossing between texels along a row, from the linearly interpolated margin (where the shader's edge lies), in texels.</summary>
    private static float Crossing(float[] m, int row, int from, int to)
    {
        for (int x = from; x > to; x--)
            if (m[row + x - 1] >= 0 && m[row + x] < 0) return x - 1 + m[row + x - 1] / (m[row + x - 1] - m[row + x]);
        return float.NaN;
    }

    /// <summary>
    /// A wall 20 m high, one texel thick, across the grid: its shadow falls toward -x as long as the sun's elevation makes it, and the
    /// edge, interpolated from the margins, lies within a tenth of a texel of it (a line, not a texel's step).
    /// </summary>
    [Theory]
    [InlineData(15f, 2.5f)]   // presets 2 and 7 (dawn, dusk)
    [InlineData(35f, 2.5f)]   // preset 3
    [InlineData(45f, 1.25f)]  // preset 5
    [InlineData(20f, 5f)]
    public void AWallCastsAShadowAsLongAsTheSunsElevationMakesItToATenthOfATexel(float deg, float texel)
    {
        var g = Flat(200, 8, texel: texel);
        const int wall = 170;
        const float height = 20f;
        for (int z = 0; z < g.D; z++) g.H[z * g.W + wall] = height;
        var m = TerrainShadeBake.Margin(g, Sun(deg));
        float length = (height - TerrainShadeBake.SunBias * texel) / MathF.Tan(deg * MathF.PI / 180);
        int row = 4 * g.W;
        float cross = Crossing(m, row, wall - 1, 0);
        Assert.False(float.IsNaN(cross), "no edge");
        Assert.InRange((wall - cross) * texel, length - 0.1f * texel, length + 0.1f * texel);
        var lit = Shares(m);
        Assert.True(lit[row + wall - 2] < 0.01f);
        Assert.True(lit[row + Math.Max(0, wall - (int)(1.3f * length / texel) - 3)] > 0.99f);
        for (int x = wall + 1; x < g.W; x++) Assert.Equal(1f, lit[row + x]);
    }

    /// <summary>The penumbra is the sun's disc seen from the ground: the distance to the ridge times the disc's angle, so it grows with that distance.</summary>
    [Fact]
    public void ThePenumbraGrowsWithTheDistanceToTheRidge()
    {
        float Width(float height)
        {
            var g = Flat(4000, 3, texel: 0.5f);
            const int wall = 3990;
            for (int z = 0; z < g.D; z++) g.H[z * g.W + wall] = height;
            var m = TerrainShadeBake.Margin(g, Sun(15));
            // metres between the shares 0.9 and 0.1 along the middle row, the margin interpolated as the shader's filter does
            float At(float share)
            {
                for (int x = wall - 1; x > 0; x--)
                {
                    float a = m[g.W + x - 1], b = m[g.W + x];
                    for (int k = 0; k < 16; k++)
                    {
                        float t0 = k / 16f, t1 = (k + 1) / 16f;
                        float la = TerrainShadeBake.Lit(a + (b - a) * t0), lb = TerrainShadeBake.Lit(a + (b - a) * t1);
                        if (la >= share && lb < share) return (x - 1 + t0 + (la - share) / (la - lb) / 16) * g.Texel;
                    }
                }
                return float.NaN;
            }
            return MathF.Abs(At(0.1f) - At(0.9f));
        }
        float near = Width(10), far = Width(100);
        Assert.InRange(far / near, 8f, 12f);
        // the disc's angle at the ridge's distance along the ray, laid on the level ground at the sun's elevation (100 m high at 15 degrees:
        // 386 m along the ray; 0.1 to 0.9 of a smoothstep is 0.58 of its width): 8.1 m
        float expect = 0.58f * 386 * 2 * TerrainShadeBake.SunRadius / MathF.Sin(15 * MathF.PI / 180);
        Assert.InRange(far, 0.9f * expect, 1.1f * expect);
    }

    [Fact]
    public void FlatGroundIsUnshadowedAndOpenToTheSky()
    {
        var g = Flat(40, 30, 123.4f);
        foreach (float deg in new[] { 15f, 20f, 35f, 80f })
            Assert.All(Shares(TerrainShadeBake.Margin(g, Sun(deg))), v => Assert.Equal(1f, v));
        Assert.All(TerrainShadeBake.Sky(g), v => Assert.Equal(1f, v, 5));
    }

    [Fact]
    public void ASunUnderTheHorizonLightsNothingAndOneOverheadEverything()
    {
        var g = Flat(10, 10);
        g.H[55] = 50;
        Assert.All(Shares(TerrainShadeBake.Margin(g, new Vector3(1, -0.1f, 0))), v => Assert.Equal(0f, v));
        Assert.All(Shares(TerrainShadeBake.Margin(g, new Vector3(0, 1, 0))), v => Assert.Equal(1f, v));
    }

    [Fact]
    public void TheMarginSurvivesItsByteToATenthOfTheSunsRadius()
    {
        for (float m = -TerrainShadeBake.MarginRange; m <= TerrainShadeBake.MarginRange; m += 0.0013f)
            Assert.InRange(TerrainShadeBake.UnpackMargin(TerrainShadeBake.PackMargin([m])[0]) - m, -0.1f * TerrainShadeBake.SunRadius, 0.1f * TerrainShadeBake.SunRadius);
        Assert.Equal(0, TerrainShadeBake.PackMargin([-1f])[0]);
        Assert.Equal(255, TerrainShadeBake.PackMargin([1f])[0]);
    }

    [Fact]
    public void APitIsLessOpenToTheSkyThanThePlain()
    {
        var g = Flat(60, 60, 100, 5f);
        for (int z = 26; z < 34; z++)
            for (int x = 26; x < 34; x++)
                g.H[z * g.W + x] = 80;
        var sky = TerrainShadeBake.Sky(g);
        float pit = sky[30 * g.W + 30], plain = sky[5 * g.W + 5];
        Assert.Equal(1f, plain, 5);
        Assert.True(pit < 0.8f, $"pit {pit}");
        Assert.True(sky[30 * g.W + 26] < pit, $"foot {sky[30 * g.W + 26]}, middle {pit}");
    }

    [Fact]
    public void CoarseningAveragesBlocks()
    {
        var g = Flat(8, 4);
        for (int i = 0; i < g.H.Length; i++) g.H[i] = i % 8;
        var c = TerrainShadeBake.Coarsen(g, 4);
        Assert.Equal((2, 1, 10f), (c.W, c.D, c.Texel));
        Assert.Equal([1.5f, 5.5f], c.H);
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
        byte[] Bake() => TerrainShadeBake.PackMargin(TerrainShadeBake.Margin(g, Sun(20))).Concat(TerrainShadeBake.Pack(TerrainShadeBake.Sky(TerrainShadeBake.Coarsen(g, 4)))).ToArray();
        var a = Bake();
        for (int run = 0; run < 3; run++) Assert.Equal(a, Bake());
        var off = new Vector3(0.8f, 0.4f, 0.45f);
        Assert.Equal(TerrainShadeBake.Margin(g, off), TerrainShadeBake.Margin(g, off));
    }

    /// <summary>The march an azimuth off the grid's lines takes agrees with the sweep's horizons for one on them (a sun turned a hair off +x).</summary>
    [Fact]
    public void TheMarchAgreesWithTheSweep()
    {
        var g = Flat(80, 6);
        for (int z = 0; z < g.D; z++) { g.H[z * g.W + 50] = 30; g.H[z * g.W + 51] = 30; }
        var swept = Shares(TerrainShadeBake.Margin(g, Sun(20)));
        var marched = Shares(TerrainShadeBake.Margin(g, new Vector3(Sun(20).X, Sun(20).Y, 0.01f)));
        for (int x = 0; x < g.W; x++) Assert.InRange(marched[3 * g.W + x] - swept[3 * g.W + x], -0.1f, 0.1f);
    }

    /// <summary>The texture's place: at 2.5 m two texels a sample, their centres a quarter sample either side of it, the corner half a sample before the first.</summary>
    [Fact]
    public void ThePlacementCentresTexelsAboutTheSamples()
    {
        var p = new TerrainShadeBake.Placement(-2.5f, -2.5f, 640, 640, 256, 256, 0.5, 0, 0);
        Assert.Equal(2.5f, p.Texel);
        Assert.Equal((-0.25, -0.25), p.Centre(0, 0));
        Assert.Equal((0.25, 0.25), p.Centre(1, 1));
    }

    /// <summary>Terrain Shadows: On in a fresh block and in a file without the key (older files), every value written (an explicit Off kept), an unknown word Off.</summary>
    [Fact]
    public void TheRowIsOnUnlessChosenOffAndSurvivesTheFile()
    {
        var s = new ShellSettings();
        Assert.Equal(TerrainShadowsRow.On, s.TerrainShadows);
        var fresh = s.Write(null);
        Assert.Equal("on", fresh.GetValue("port", "terrain_shadows").ToString());
        Assert.Equal(TerrainShadowsRow.On, ShellSettings.Read(fresh).TerrainShadows);
        // older files (the key absent) get On; the default file is the same
        var old = ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile);
        Assert.False(old.HasSectionKey("port", "terrain_shadows"));
        Assert.Equal(TerrainShadowsRow.On, ShellSettings.Read(old).TerrainShadows);
        // a player who chose Off stays Off through write and read, and the other settings are not touched
        var off = new ShellSettings { TerrainShadows = TerrainShadowsRow.Off, SmokeLighting = true }.Write(null);
        Assert.Equal("off", off.GetValue("port", "terrain_shadows").ToString());
        var back = ShellSettings.Read(off);
        Assert.Equal(TerrainShadowsRow.Off, back.TerrainShadows);
        Assert.True(back.SmokeLighting);
        foreach (var (v, word) in new[] { (TerrainShadowsRow.On, "on"), (TerrainShadowsRow.Fine, "fine"), (TerrainShadowsRow.Off, "off") })
        {
            var file = new ShellSettings { TerrainShadows = v }.Write(null);
            Assert.Equal(word, file.GetValue("port", "terrain_shadows").ToString());
            Assert.Equal(v, ShellSettings.Read(file).TerrainShadows);
        }
        Assert.Equal(TerrainShadowsRow.Off, TerrainShadowsRow.FromFile("lots"));
        Assert.Equal(TerrainShadowsRow.Fine, TerrainShadowsRow.FromFile(" Fine "));
        Assert.Equal(["Off", "On" + ShellSettings.PortMark, "Fine" + ShellSettings.PortMark], TerrainShadowsRow.Names);
        TerrainShadowsRow.Step(s); Assert.Equal(TerrainShadowsRow.Fine, s.TerrainShadows);
        TerrainShadowsRow.Step(s); Assert.Equal(TerrainShadowsRow.Off, s.TerrainShadows);
        TerrainShadowsRow.Step(s); Assert.Equal(TerrainShadowsRow.On, s.TerrainShadows);
        Assert.Equal((0f, 2.5f, 1.25f), (TerrainShadowsRow.Texel(0), TerrainShadowsRow.Texel(1), TerrainShadowsRow.Texel(2)));
    }
}

/// <summary>The bake on every stock map from the drawn surface: its size and time at 2.5 and 1.25 m (TerrainShade runs it on a worker).</summary>
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
            // the drawn surface as TerrainNode.SurfaceFor makes it (without its cache, an engine path)
            var surf = new I76.Formats.Assembly.TerrainSurface(world.Terrain, I76.Formats.Assembly.TerrainSurface.Seeds(world).ToList(), world.Outside, world.Footprints(), I76.Formats.Assembly.TerrainSurface.Links(world));
            foreach (float texel in new[] { 2.5f, 1.25f })
            {
                var p = TerrainShadeBake.PlacementFor(world.Terrain, texel);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var g = TerrainShadeBake.HeightsFor(surf, p);
                long th = sw.ElapsedMilliseconds;
                var m = TerrainShadeBake.Margin(g, light.Direction);
                long ts = sw.ElapsedMilliseconds;
                var sky = TerrainShadeBake.Sky(TerrainShadeBake.Coarsen(g, Math.Max(1, (int)MathF.Round(TerrainShadeBake.SkyTexel / p.Texel))));
                long t = sw.ElapsedMilliseconds;
                if (texel == 2.5f) worst = Math.Max(worst, t);
                log.WriteLine($"{Path.GetFileNameWithoutExtension(f),-8} {(light.IsDay ? "day  " : "night")} {p.W,5} x {p.D,-5} {p.Texel,4} m  heights {th,5} ms  sun {ts - th,4} ms  sky {t - ts,4} ms  total {t,5} ms  {p.W * (long)p.D * 4 / 3 / 1024 / 1024} MiB");
            }
        }
        Assert.True(worst < 2000, $"worst {worst} ms");
    }
}
