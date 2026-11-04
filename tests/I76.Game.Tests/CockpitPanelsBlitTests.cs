using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The cockpit panels' stamp at a scale: the layout's pixels on a panel and a sheet each served at their own size.</summary>
public class CockpitPanelsBlitTests
{
    /// <summary>A w × h RGBA8 sheet in stored (bottom-up) order whose texel (x, y) of the picture is (x, y, 0, a).</summary>
    private static byte[] Sheet(int w, int h, Func<int, int, byte> alpha)
    {
        var b = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { int i = ((h - 1 - y) * w + x) * 4; b[i] = (byte)x; b[i + 1] = (byte)y; b[i + 3] = alpha(x, y); }
        return b;
    }

    private static (byte X, byte Y, byte A) At(byte[] img, int w, int h, int x, int y) { int i = ((h - 1 - y) * w + x) * 4; return (img[i], img[i + 1], img[i + 3]); }

    [Fact]
    public void ASheetAtTwiceThePanelsScaleIsSampledNearest()
    {
        var sheet = Sheet(8, 8, (x, y) => 255);
        var panel = new byte[4 * 4 * 4];
        // the layout's 2x2 rectangle at (1, 1) of the sheet to (2, 2) of the panel
        CockpitPanels.Blit(sheet, 8, 8, 2, 1, 1, 2, 2, panel, 4, 4, 1, 2, 2, keyed: true);
        Assert.Equal(((byte)2, (byte)2, (byte)255), At(panel, 4, 4, 2, 2));
        Assert.Equal(((byte)4, (byte)4, (byte)255), At(panel, 4, 4, 3, 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 4, 4, 1, 1));
    }

    [Fact]
    public void APanelAtTwiceTheSheetsScaleRepeatsEachTexelAndKeepsTheKey()
    {
        var sheet = Sheet(4, 4, (x, y) => (byte)(x == 1 ? 0 : 255));
        var panel = new byte[8 * 8 * 4];
        CockpitPanels.Blit(sheet, 4, 4, 1, 0, 0, 2, 2, panel, 8, 8, 2, 1, 1, keyed: true);
        // texel (0, 0) covers the panel's (2..3, 2..3); texel (1, y) is keyed and leaves the panel alone
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(panel, 8, 8, 2, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)255), At(panel, 8, 8, 3, 3));
        Assert.Equal(((byte)0, (byte)1, (byte)255), At(panel, 8, 8, 2, 4));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 8, 8, 4, 2));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 8, 8, 1, 1));
    }

    [Fact]
    public void AnUnkeyedCopyClipsAtTheEdges()
    {
        var sheet = Sheet(4, 4, (x, y) => 0);
        var panel = new byte[4 * 4 * 4];
        CockpitPanels.Blit(sheet, 4, 4, 1, 2, 2, 4, 4, panel, 4, 4, 1, 3, 3, keyed: false);
        Assert.Equal(((byte)2, (byte)2, (byte)0), At(panel, 4, 4, 3, 3));
        Assert.Equal(((byte)0, (byte)0, (byte)0), At(panel, 4, 4, 2, 2));
    }

    [Fact]
    public void TheCountersInkIsDeepenedAndTheDrumLeftWhite()
    {
        // a 3 x 1 pixel window at 2 texels a pixel: the strip's ink (41, 41, 20), a mid grey and the drum's white
        int w = 3 * 2, h = 2;
        var panel = new byte[w * h * 4];
        (byte, byte, byte)[] cols = [(41, 41, 20), (101, 101, 101), (255, 255, 255)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { int i = ((h - 1 - y) * w + x) * 4; (panel[i], panel[i + 1], panel[i + 2], panel[i + 3]) = (cols[x / 2].Item1, cols[x / 2].Item2, cols[x / 2].Item3, 255); }
        CockpitPanels.DeepenInk(panel, w, h, 2, 0, 0, 3, 1);
        (byte R, byte G, byte B) Px(int x) { int i = ((h - 1) * w + x) * 4; return (panel[i], panel[i + 1], panel[i + 2]); }
        // the ink to about a third of itself, its hue kept
        Assert.InRange(Px(0).R, 10, 18);
        Assert.True(Px(0).B < Px(0).R);
        // the grey less, the white untouched
        Assert.InRange(Px(2).R, 70, 90);
        Assert.Equal(((byte)255, (byte)255, (byte)255), Px(4));
    }

    /// <summary>A strip cell as the original's at s texels a pixel: the separator's grey in column 0, the drum's white face, a painted figure's olive-grey in columns 2..5 of rows 2..8.</summary>
    private static byte[] Cell(int s)
    {
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var b = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int px = x / s, py = y / s, i = ((h - 1 - y) * w + x) * 4;
                var c = px == 0 ? ((byte)118, (byte)118, (byte)118) : px is >= 2 and <= 5 && py is >= 2 and <= 8 ? ((byte)66, (byte)72, (byte)32) : ((byte)255, (byte)255, (byte)255);
                (b[i], b[i + 1], b[i + 2], b[i + 3]) = (c.Item1, c.Item2, c.Item3, 255);
            }
        return b;
    }

    private static (byte R, byte G, byte B) Rgb(byte[] img, int w, int h, int x, int y) { int i = ((h - 1 - y) * w + x) * 4; return (img[i], img[i + 1], img[i + 2]); }

    [Fact]
    public void TheCountersFontIsInTheGameAssembly()
    {
        var f = CounterDigits.Font;
        Assert.NotNull(f);
        for (char c = '0'; c <= '9'; c++) Assert.NotEqual(0, f!.Glyph(c));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void AFontFigureReplacesTheStripsOnItsDrum(int s)
    {
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var cell = Cell(s);
        CounterDigits.Paint(cell, s, 1, CounterDigits.Font!, inset: false);
        int top = h, bottom = -1, left = w, right = -1, solid = 0, edge = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = Rgb(cell, w, h, x, y);
                // the separator column is the original's; the painted figure is gone
                if (x < s) { Assert.Equal(((byte)118, (byte)118, (byte)118), c); continue; }
                Assert.NotEqual(((byte)66, (byte)72, (byte)32), c);
                if (c == CounterDigits.Ink) solid++;
                else if (c != ((byte)255, (byte)255, (byte)255)) edge++;
                if (c.R < 128) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); left = Math.Min(left, x); right = Math.Max(right, x); }
            }
        // the 1 stands where the strip's does: rows 1.4..9.0, about the strip's figure centre
        Assert.InRange(top, (int)(1.2f * s), (int)(1.8f * s));
        Assert.InRange(bottom, 9 * s - 2, 9 * s - 1);
        Assert.InRange((left + right + 1) / 2f / s, 2.8f, 4.2f);
        // crisp at the pack's 4x: the stroke is the ink itself, its anti-aliasing about a texel round the edge (at 2x a
        // stroke is under three texels, half of them edge)
        if (s >= 4) Assert.True(solid > edge, $"{solid} solid texels against {edge} edge ones");
    }

    [Fact]
    public void TheInsetVariantLightsTheFiguresLowerEdge()
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var flat = Cell(s); var inset = Cell(s);
        CounterDigits.Paint(flat, s, 2, CounterDigits.Font!, inset: false);
        CounterDigits.Paint(inset, s, 2, CounterDigits.Font!, inset: true);
        // the 2's foot: its lowest solid row is the ink flat and lifted toward the face inset; the row above it the same in both
        int foot = -1;
        for (int y = h - 1; y >= 0 && foot < 0; y--) if (Rgb(flat, w, h, 4 * s, y) == CounterDigits.Ink) foot = y;
        Assert.True(foot > 0);
        Assert.True(Rgb(inset, w, h, 4 * s, foot).R > CounterDigits.Ink.R + 60, $"{Rgb(inset, w, h, 4 * s, foot)}");
        Assert.Equal(CounterDigits.Ink, Rgb(inset, w, h, 4 * s, foot - 2));
    }
}
