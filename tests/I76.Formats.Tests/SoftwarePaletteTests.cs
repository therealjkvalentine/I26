using I76.Formats.Imaging;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Formats.Tests;

public class SoftwarePaletteTests
{
    /// <summary>A palette of 256 distinct colours: a coarse RGB grid.</summary>
    private static Palette Grid()
    {
        var d = new byte[768];
        for (int i = 0; i < 256; i++)
        {
            d[i * 3] = (byte)(i % 8 * 36);
            d[i * 3 + 1] = (byte)(i / 8 % 8 * 36);
            d[i * 3 + 2] = (byte)(i / 64 * 85);
        }
        return new Palette(d);
    }

    [Theory]
    [InlineData(1f, 0)]        // full light: the brightest row
    [InlineData(0f, 31)]       // none: the darkest
    [InlineData(0.78f, 6)]     // (1 − 0.78)·31.5 = 6.93
    [InlineData(0.777f, 7)]    // 7.02: the identity row
    [InlineData(-1f, 31)]
    [InlineData(2f, 0)]
    public void TheRowIsTheEngines(float light, int row) => Assert.Equal(row, SoftwarePalette.Row(light));

    [Fact]
    public void AnIdentityTableIsNoShade()
    {
        var lum = new byte[256 * 256];
        for (int r = 0; r < 256; r++) for (int c = 0; c < 256; c++) lum[r * 256 + c] = (byte)c;
        foreach (var (a, b) in SoftwarePalette.Ramp(Grid(), lum))
        {
            Assert.Equal(1f, a, 3);
            Assert.Equal(0f, b, 3);
        }
    }

    [Fact]
    public void ADarkeningRowFitsItsFactor()
    {
        var pal = Grid();
        var lum = new byte[256 * 256];
        // every row: the entry nearest half the colour
        for (int c = 0; c < 256; c++)
        {
            var (r, g, b) = pal[c];
            byte to = (byte)SoftwarePalette.Nearest(pal, r / 2, g / 2, b / 2);
            for (int row = 0; row < 256; row++) lum[row * 256 + c] = to;
        }
        foreach (var (a, b) in SoftwarePalette.Ramp(pal, lum))
        {
            Assert.InRange(a, 0.4f, 0.6f);
            Assert.InRange(b, 0f, 0.05f);
        }
    }

    [Fact]
    public void NearestPrefersTheLowestIndexOnATie()
    {
        var d = new byte[768];
        d[3 * 3] = 10; d[5 * 3] = 10;   // entries 3 and 5 both (10,0,0); the rest black
        var pal = new Palette(d);
        Assert.Equal(3, SoftwarePalette.Nearest(pal, 12, 0, 0));
        Assert.Equal(0, SoftwarePalette.Nearest(pal, 1, 0, 0));
    }

    [Fact]
    public void TheLutHoldsTheNearestColourOfEachCell()
    {
        var pal = Grid();
        const int n = 16;
        var lut = SoftwarePalette.BuildLut(pal, n, out int side);
        Assert.Equal(64, side);
        Assert.Equal(side * side * 3, lut.Length);
        var rng = new Random(7);
        for (int k = 0; k < 500; k++)
        {
            int ri = rng.Next(n), gi = rng.Next(n), bi = rng.Next(n);
            int V(int i) => (int)Math.Round(255.0 * i / (n - 1));
            int want = SoftwarePalette.Nearest(pal, V(ri), V(gi), V(bi));
            int x = bi % 4 * n + ri, y = bi / 4 * n + gi;
            int at = (y * side + x) * 3;
            Assert.Equal(pal[want], (lut[at], lut[at + 1], lut[at + 2]));
        }
    }

    /// <summary>A palette like a mission's: most entries close together (a fine grey ramp), a few far apart.</summary>
    private static Palette Ramp()
    {
        var d = new byte[768];
        for (int i = 0; i < 256; i++) d[i * 3] = d[i * 3 + 1] = d[i * 3 + 2] = (byte)i;
        return new Palette(d);
    }

    private static (byte R, byte G, byte B) Cell(byte[] lut, int side, int n, int ri, int gi, int bi)
    {
        int tiles = (int)Math.Sqrt(n), x = bi % tiles * n + ri, y = bi / tiles * n + gi, at = (y * side + x) * 3;
        return (lut[at], lut[at + 1], lut[at + 2]);
    }

    [Fact]
    public void TheSoftLutWithNoSigmaIsTheNearestLut()
    {
        var hard = SoftwarePalette.BuildLut(Grid(), 16, out _);
        Assert.Equal(hard, SoftwarePalette.BuildSoftLut(Grid(), 16, 0f, out int side));
        Assert.Equal(64, side);
    }

    [Fact]
    public void TheSoftLutHasNoStepsWhereTheNearestLutHasThem()
    {
        // the grid palette's blue comes in steps of 85: the nearest lookup jumps 85 between neighbouring cells, the
        // soft one (σ 0.1) moves a little at a time
        const int n = 16;
        var hard = SoftwarePalette.BuildLut(Grid(), n, out int side);
        var soft = SoftwarePalette.BuildSoftLut(Grid(), n, 0.1f, out _);
        int hardJump = 0, softJump = 0;
        for (int bi = 1; bi < n; bi++)
        {
            hardJump = Math.Max(hardJump, Math.Abs(Cell(hard, side, n, 8, 8, bi).B - Cell(hard, side, n, 8, 8, bi - 1).B));
            softJump = Math.Max(softJump, Math.Abs(Cell(soft, side, n, 8, 8, bi).B - Cell(soft, side, n, 8, 8, bi - 1).B));
        }
        Assert.Equal(85, hardJump);
        Assert.InRange(softJump, 1, 40);
    }

    [Fact]
    public void WhereThePaletteIsDenseTheSoftLutKeepsTheColour()
    {
        // a grey ramp of every level: the grade leaves a grey as it is, away from the cube's ends
        const int n = 64;
        var soft = SoftwarePalette.BuildSoftLut(Ramp(), n, 0.05f, out int side);
        for (int i = 16; i < 48; i++)
        {
            int v = (int)Math.Round(255.0 * i / (n - 1));
            var (r, g, b) = Cell(soft, side, n, i, i, i);
            Assert.InRange(r, v - 3, v + 3);
            Assert.InRange(g, v - 3, v + 3);
            Assert.InRange(b, v - 3, v + 3);
        }
    }

    [Fact]
    public void TheLutNeedsASquareSize() => Assert.Throws<ArgumentException>(() => SoftwarePalette.BuildLut(Grid(), 32, out _));

    /// <summary>
    /// t01.act / t01.lum, the palette and table the software renderer drew T01 with (measured: every pixel of its
    /// frames is a t01.act entry): row 7 is the identity, row 0 a mix about 40% toward white, row 31 about 0.3.
    /// </summary>
    [InstallFact]
    public void T01sRowsAreHighlightIdentityAndShade()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var pal = new Palette(vfs.Read("t01.act"));
        var lum = vfs.Read("t01.lum");
        var ramp = SoftwarePalette.Ramp(pal, lum);
        // j = 31 is row 0: toward white
        Assert.InRange(ramp[31].A, 0.55f, 0.65f);
        Assert.InRange(ramp[31].B, 0.3f, 0.45f);
        // j = 24: (1 − 24/31)·31.5 = 7.1, row 7, the identity
        Assert.Equal(7, SoftwarePalette.Row(24 / 31f));
        Assert.Equal(1f, ramp[24].A, 2);
        Assert.Equal(0f, ramp[24].B, 2);
        // j = 0 is row 31: dark
        Assert.InRange(ramp[0].A, 0.25f, 0.35f);
        Assert.InRange(ramp[0].B, 0f, 0.02f);
        // the fit's colour, matched to the palette, is often the table's own entry (in the dark rows many near-blacks tie)
        foreach (int j in new[] { 0, 8, 16, 31 })
        {
            int row = SoftwarePalette.Row(j / 31f), same = 0;
            for (int c = 0; c < 256; c++)
            {
                var (r, g, b) = pal[c];
                int R(byte v) => Math.Clamp((int)Math.Round(v * ramp[j].A + 255 * ramp[j].B), 0, 255);
                int got = SoftwarePalette.Nearest(pal, R(r), R(g), R(b));
                if (pal[got] == pal[lum[row * 256 + c]]) same++;
            }
            Assert.True(same >= 90, $"light {j}/31 (row {row}): the fit reproduces {same} of 256 entries");
        }
    }

    [Theory]
    [InlineData(-0.01f, 0)]     // the band's foot (s = foot): row 0, the brightest
    [InlineData(-0.2f, 0)]      // below the foot: row 0 carried down
    [InlineData(0.2f, 31)]      // the top: row 31 (t = 0)
    [InlineData(0.2001f, -1)]   // above it: the sky
    [InlineData(0.04f, 7)]      // t = 0.16/0.21 = 0.762: (1 − t)·31 = 7.4
    public void TheBandsRowFollowsTheScreen(float s, int row) => Assert.Equal(row, SoftwarePalette.GradientRow(s, -0.01f, 0.2f));

    /// <summary>
    /// #87: the 1997 CD build's sky band through T01's palette and .lum (the same bytes as GOG's miss8 copies): row 0..6
    /// step through the brighter sky entries 234..237, row 7 on is 0xef itself, the steel blue (79,132,173).
    /// </summary>
    [InstallFact]
    public void T01sBandIsTheSkyRamp()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var pal = new Palette(vfs.Read("t01.act"));
        var rows = SoftwarePalette.SkyGradient(pal, vfs.Read("t01.lum"));
        int[] want = [234, 234, 235, 235, 236, 236, 237];
        for (int r = 0; r < 32; r++) Assert.Equal(pal[r < 7 ? want[r] : SoftwarePalette.SkyIndex], rows[r]);
        Assert.Equal(((byte)79, (byte)132, (byte)173), rows[31]);
        Assert.Equal(((byte)138, (byte)173, (byte)200), rows[0]);
    }

    [Fact]
    public void TheShadowLowersTheValueKeepingTheHue()
    {
        // v 0.533 -> 0.283: the road's grey (136) to 72
        var (r, g, b) = SoftwarePalette.Shadow(136 / 255f, 136 / 255f, 136 / 255f);
        Assert.Equal(72, (int)MathF.Round(r * 255));
        Assert.Equal(r, g);
        Assert.Equal(r, b);
        // above 0.6 everything lands on 0.35, hue and saturation kept
        var (r2, g2, b2) = SoftwarePalette.Shadow(1f, 0.5f, 0.25f);
        Assert.Equal(0.35f, r2, 4);
        Assert.Equal(0.175f, g2, 4);
        Assert.Equal(0.0875f, b2, 4);
        // 0.25 and darker go black
        Assert.Equal((0f, 0f, 0f), SoftwarePalette.Shadow(0.2f, 0.25f, 0.1f));
        Assert.Equal(0.35f, SoftwarePalette.ShadowValue(0.61f), 4);
        Assert.Equal(0.35f, SoftwarePalette.ShadowValue(0.6f), 4);
    }

    /// <summary>
    /// The table as the original's software frames show it (the GOG build, T01 world frame 400 with no
    /// plugin, the white car's shadow on the road): the road's 247 (136 grey) becomes 251, 248 (118) becomes 252, and
    /// what is already near black stays black.
    /// </summary>
    [InstallFact]
    public void T01sShadowTableIsTheFramesRemap()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var t = SoftwarePalette.ShadowTable(new Palette(vfs.Read("t01.act")));
        Assert.Equal(251, t[247]);
        Assert.Equal(252, t[248]);
        Assert.Equal(0, t[254]);
        Assert.Equal(0, t[0]);
    }

    [Fact]
    public void TheRadarGreenDarkensAsTheByteClimbs()
    {
        var (r0, g0, b0) = SoftwarePalette.RadarGreen(0);
        Assert.Equal((99, 198, 99), ((int)MathF.Round(r0 * 255), (int)MathF.Round(g0 * 255), (int)MathF.Round(b0 * 255)));
        var (r1, g1, b1) = SoftwarePalette.RadarGreen(0x69);   // a listed contact at rest
        Assert.Equal((58, 140, 58), ((int)MathF.Round(r1 * 255), (int)MathF.Round(g1 * 255), (int)MathF.Round(b1 * 255)));
        Assert.Equal((1f, 1f, 0f), SoftwarePalette.RadarGreen(255));
        for (int i = 1; i < 255; i++) Assert.True(SoftwarePalette.RadarGreen(i).G < SoftwarePalette.RadarGreen(i - 1).G);
        // as a palette entry: the grid's nearest
        var pal = Grid();
        int e = SoftwarePalette.RadarBlip(pal, 0);
        Assert.Equal(SoftwarePalette.Nearest(pal, 99, 198, 99), e);
        var (er, eg, eb) = pal[e];
        Assert.True(eg > er && eg > eb, "green");
    }
}
