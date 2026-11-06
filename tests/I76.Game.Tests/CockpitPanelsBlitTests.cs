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
        CounterDigits.Paint(cell, s, 1, CounterDigits.Font!, DrumStyle.Flat);
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
    public void TheInsetDrumLightsTheFiguresLowerEdgeAndShadesItsTop()
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var drum = Cell(s); var inset = Cell(s); var raised = Cell(s);
        CounterDigits.Paint(drum, s, 2, CounterDigits.Font!, DrumStyle.Drum, Edge(0), Housing);
        CounterDigits.Paint(inset, s, 2, CounterDigits.Font!, DrumStyle.Inset, Edge(0), Housing);
        CounterDigits.Paint(raised, s, 2, CounterDigits.Font!, DrumStyle.Raised, Edge(0), Housing);
        // the 2's foot (column 4): its lowest dark row lifted toward the face inset, two rows up the same as the plain drum
        int foot = -1, top = -1;
        for (int y = h - 1; y >= 0 && foot < 0; y--) if (Rgb(drum, w, h, 4 * s, y).R < 30) foot = y;
        for (int y = 0; y < h && top < 0; y++) if (Rgb(drum, w, h, 4 * s, y).R < 30) top = y;
        Assert.True(foot > 0 && top > 0);
        Assert.True(Rgb(inset, w, h, 4 * s, foot).R > Rgb(drum, w, h, 4 * s, foot).R + 40, $"{Rgb(inset, w, h, 4 * s, foot)}");
        Assert.Equal(Rgb(drum, w, h, 4 * s, foot - 2), Rgb(inset, w, h, 4 * s, foot - 2));
        // the face just above the figure a little darker cut in, just below it raised; subtle: a fifth at most
        double Darkest(byte[] c, int y0, int y1)
        {
            double k = 0;
            for (int y = y0; y < y1; y++) { int a = Rgb(drum, w, h, 4 * s, y).R; if (a > 150) k = Math.Max(k, 1 - Rgb(c, w, h, 4 * s, y).R / (double)a); }
            return k;
        }
        Assert.InRange(Darkest(inset, 0, top), 0.05, 0.22);
        Assert.InRange(Darkest(raised, foot, h), 0.05, 0.22);
        Assert.True(Darkest(inset, foot, h) < 0.01 && Darkest(raised, 0, top) < 0.01);
        // and raised lights the figure's top edge instead
        Assert.True(Rgb(raised, w, h, 4 * s, top).R > Rgb(drum, w, h, 4 * s, top).R + 20, $"{Rgb(raised, w, h, 4 * s, top)}");
    }

    [Fact]
    public void TheDrumIsLitAtItsCrownAndDarkerTowardTheWindowsEdges()
    {
        int h = CounterDigits.CellH * 4;
        float crown = Enumerable.Range(0, h).Max(y => CounterDigits.Shade(y, h));
        Assert.InRange(crown, 0.99f, 1.0f);
        // brightest a little above the middle (lit from above), the bottom edge darker than the top, neither under 0.7
        int peak = Enumerable.Range(0, h).OrderByDescending(y => CounterDigits.Shade(y, h)).First();
        Assert.InRange(peak, h / 4, h / 2);
        Assert.InRange(CounterDigits.Shade(0, h), 0.8f, 0.95f);
        Assert.InRange(CounterDigits.Shade(h - 1, h), 0.7f, 0.8f);
        Assert.True(CounterDigits.Shade(h - 1, h) < CounterDigits.Shade(0, h));
    }

    [Theory]
    [InlineData(DrumStyle.Drum)]
    [InlineData(DrumStyle.Inset)]
    [InlineData(DrumStyle.Raised)]
    [InlineData(DrumStyle.Gaps)]
    public void EveryFigureStaysLegibleOnTheShadedDrum(DrumStyle style)
    {
        foreach (float feather in new[] { 0f, 0.15f, 0.25f, 0.30f }) Legible(style, feather);
    }

    private static void Legible(DrumStyle style, float feather)
    {
        // legibility: on every row the figure crosses, the face beside it is at least 130 levels brighter than the
        // figure's ink (the inset's lit lower edge included), and the figure keeps the flat one's place (top and foot within half a pixel, centred alike)
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        for (int d = 0; d < 10; d++)
        {
            var flat = Cell(s); var drum = Cell(s);
            CounterDigits.Paint(flat, s, d, CounterDigits.Font!, DrumStyle.Flat);
            CounterDigits.Paint(drum, s, d, CounterDigits.Font!, style, Edge(feather), Housing);
            (int Top, int Bottom) Extent(byte[] c, int y0, int y1)
            {
                int t = -1, b = -1;
                for (int y = y0; y < y1; y++) for (int x = 2 * s; x < w - s; x++) if (Rgb(c, w, h, x, y).R < 100) { if (t < 0) t = y; b = y; }
                return (t, b);
            }
            var f = Extent(flat, 0, h);
            // the neighbours' ends (Gaps) lie outside the flat figure's rows: measure inside them
            if (feather == 0)
            {
                var g = Extent(drum, f.Top - s / 2, f.Bottom + s / 2 + 1);
                Assert.InRange(g.Top, f.Top - s / 2, f.Top + s / 2);
                Assert.InRange(g.Bottom, f.Bottom - s / 2, f.Bottom + s / 2);
            }
            for (int y = f.Top; y <= f.Bottom; y++)
            {
                if (CounterDigits.EdgeWeight(y, h, feather) > 0) continue;   // the window's feathered edges: the middle band is what must read
                int ink = 255, face = 0;
                for (int x = s; x < w; x++) { int r = Rgb(drum, w, h, x, y).R; ink = Math.Min(ink, r); face = Math.Max(face, r); }
                if (ink > 100) continue;       // a row the figure only grazes
                Assert.True(face - ink >= 130, $"{style} {feather} {d} row {y}: face {face}, ink {ink}");
            }
        }
    }

    [Fact]
    public void GapsShowTheNeighbouringFiguresAtTheWindowsEdgesOnly()
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var drum = Cell(s); var gaps = Cell(s);
        CounterDigits.Paint(drum, s, 8, CounterDigits.Font!, DrumStyle.Drum, Edge(0), Housing);
        CounterDigits.Paint(gaps, s, 8, CounterDigits.Font!, DrumStyle.Gaps, Edge(0), Housing);
        int Dark(byte[] c, int y) { int n = 0; for (int x = 2 * s; x < w - s; x++) if (Rgb(c, w, h, x, y).R < 140) n++; return n; }
        // the 7 above shows its foot in the top row, the 9 below its top in the bottom row; the plain drum shows neither
        Assert.Equal(0, Dark(drum, 0)); Assert.Equal(0, Dark(drum, h - 1));
        Assert.True(Dark(gaps, 0) > 0 && Dark(gaps, h - 1) > 0, $"{Dark(gaps, 0)} {Dark(gaps, h - 1)}");
        // the gap between the wheels is darker than the separator's grey
        Assert.True(Rgb(gaps, w, h, 0, h / 2).R < 118);
    }

    [Theory]
    [InlineData(null, DrumStyle.Inset)]
    [InlineData("", DrumStyle.Inset)]
    [InlineData("flat", DrumStyle.Flat)]
    [InlineData("GAPS", DrumStyle.Gaps)]
    [InlineData("7", DrumStyle.Inset)]
    [InlineData("bogus", DrumStyle.Inset)]
    public void TheDrumStyleHookParses(string? v, DrumStyle want) => Assert.Equal(want, CounterDigits.FromEnv(v));
    /// <summary>The cockpit's edge with the band given (and the lip lit or not).</summary>
    private static EdgeFade Edge(float feather, bool lip = false) => EdgeFade.Default with { Fraction = feather, Lip = lip };

    /// <summary>The housing a counter window sits in, as the weapons panel's chrome bezel reads at the seat: a grey a shade warm.</summary>
    private static readonly (byte R, byte G, byte B) Housing = (146, 146, 141);

    [Theory]
    [InlineData(0.15f)]
    [InlineData(0.25f)]
    [InlineData(0.30f)]
    public void TheWindowsEdgesFadeIntoTheHousingsShadow(float feather)
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var sharp = Cell(s); var soft = Cell(s);
        CounterDigits.Paint(sharp, s, 0, CounterDigits.Font!, DrumStyle.Inset, Edge(0), Housing);
        CounterDigits.Paint(soft, s, 0, CounterDigits.Font!, DrumStyle.Inset, Edge(feather), Housing);
        int face = w - 2;            // a face column right of the figure
        var target = CounterDigits.FadeTarget(Edge(feather), Housing);
        // the middle band untouched; the lip rows close to the target, which is the housing darkened, not black; the band
        // between easing toward it, never brighter than the row inside it and never past the target
        int band = (int)MathF.Ceiling(feather * h);
        for (int y = band; y < h - band; y++) Assert.Equal(Rgb(sharp, w, h, face, y), Rgb(soft, w, h, face, y));
        Assert.InRange(Rgb(soft, w, h, face, 0).R, target.R, target.R + 25);
        Assert.InRange(Rgb(soft, w, h, face, h - 1).R, target.R, target.R + 25);
        Assert.True(target.R >= 0.4f * Housing.R && target.R > 3 * CounterDigits.ShadowRgb.R, $"target {target}");
        for (int y = 1; y < band; y++) Assert.True(Rgb(soft, w, h, face, y).R >= Rgb(soft, w, h, face, y - 1).R, $"row {y}");
        for (int y = h - band; y < h - 1; y++) Assert.True(Rgb(soft, w, h, face, y).R >= Rgb(soft, w, h, face, y + 1).R, $"row {y}");
        // most of the darkening in the last rows: at the band's middle less than a third of the drop has happened
        int mid = band / 2;
        float drop = Rgb(sharp, w, h, face, mid).R - Rgb(soft, w, h, face, mid).R, full = Rgb(sharp, w, h, face, 0).R - target.R;
        Assert.InRange(drop / full, 0.05f, 0.34f);
        // the figure's ink thins into the band: the 0's foot row has less contrast than the sharp drum's
        int foot = -1;
        for (int y = h - 1; y >= 0 && foot < 0; y--) if (Rgb(sharp, w, h, 4 * s, y).R < 40) foot = y;
        if (CounterDigits.EdgeWeight(foot, h, feather) > 0.2f)
        {
            int c0 = Rgb(sharp, w, h, face, foot).R - Rgb(sharp, w, h, 4 * s, foot).R, c1 = Rgb(soft, w, h, face, foot).R - Rgb(soft, w, h, 4 * s, foot).R;
            Assert.True(c1 < c0, $"{c1} {c0}");
        }
    }

    [Theory]
    [InlineData(FeatherCurve.Ease)]
    [InlineData(FeatherCurve.Cos)]
    [InlineData(FeatherCurve.Pow2)]
    [InlineData(FeatherCurve.Pow3)]
    public void TheEdgeWeightIsFullAtTheLipOnlyAndFallsWithoutAStep(FeatherCurve curve)
    {
        const int h = 40; const float feather = 0.25f;
        int band = (int)(feather * h);
        // nothing outside the band, the lip row nearly full (the edge itself is half a row out), every row in strictly less
        for (int y = band; y < h - band; y++) Assert.Equal(0, CounterDigits.EdgeWeight(y, h, feather, curve));
        Assert.InRange(CounterDigits.EdgeWeight(0, h, feather, curve), 0.8f, 1f);
        for (int y = 1; y < band; y++) Assert.True(CounterDigits.EdgeWeight(y, h, feather, curve) < CounterDigits.EdgeWeight(y - 1, h, feather, curve), $"row {y}");
        // the same from the bottom, and no step: no row-to-row rise over 0.3
        for (int y = 0; y < h; y++)
        {
            Assert.Equal(CounterDigits.EdgeWeight(y, h, feather, curve), CounterDigits.EdgeWeight(h - 1 - y, h, feather, curve), 5);
            if (y > 0) Assert.InRange(MathF.Abs(CounterDigits.EdgeWeight(y - 1, h, feather, curve) - CounterDigits.EdgeWeight(y, h, feather, curve)), 0, 0.3f);
        }
        // the ease-in curves put less than a third of the weight at the band's middle row; the smoothstep about half
        float mid = CounterDigits.EdgeWeight(band / 2, h, feather, curve);
        if (curve == FeatherCurve.Ease) Assert.InRange(mid, 0.35f, 0.6f); else Assert.InRange(mid, 0.05f, 0.34f);
        Assert.Equal(0, CounterDigits.EdgeWeight(0, h, 0, curve));
    }

    [Fact]
    public void TheFadeTargetIsTheHousingDarkenedNotBlack()
    {
        var t = CounterDigits.FadeTarget(EdgeFade.Default, Housing);
        Assert.Equal(Housing.R * EdgeFade.DefaultDepth, t.R, 3);
        Assert.Equal(Housing.B * EdgeFade.DefaultDepth, t.B, 3);
        Assert.True(t.R > 80 && t.R < Housing.R);
        // an olive panel fades toward a darker olive, the hue kept
        var o = CounterDigits.FadeTarget(EdgeFade.Default, (88, 96, 53));
        Assert.True(o.G > o.R && o.R > o.B && o.G > 50);
        // deep and light take the housing lower and higher; black is the earlier near-black whatever the housing
        Assert.True(CounterDigits.FadeTarget(EdgeFade.Parse("25deep"), Housing).R < t.R && CounterDigits.FadeTarget(EdgeFade.Parse("25light"), Housing).R > t.R);
        Assert.Equal((CounterDigits.ShadowRgb.R, CounterDigits.ShadowRgb.G, CounterDigits.ShadowRgb.B), CounterDigits.FadeTarget(EdgeFade.Parse("20black"), Housing));
    }

    [Fact]
    public void TheDrumWordShadesTheCylinderDarkerTowardTheEdges()
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var plain = Cell(s); var drum = Cell(s);
        CounterDigits.Paint(plain, s, 1, CounterDigits.Font!, DrumStyle.Inset, EdgeFade.Parse("15"), Housing);
        CounterDigits.Paint(drum, s, 1, CounterDigits.Font!, DrumStyle.Inset, EdgeFade.Parse("15drum"), Housing);
        int face = w - 2, inner = h - (int)MathF.Ceiling(0.15f * h) - 1;   // the last row before the feather's band
        Assert.True(Rgb(drum, w, h, face, inner).R < Rgb(plain, w, h, face, inner).R - 10, $"{Rgb(drum, w, h, face, inner)} {Rgb(plain, w, h, face, inner)}");
        Assert.True(Rgb(drum, w, h, face, h - 1).R < Rgb(plain, w, h, face, h - 1).R);
        Assert.True(Rgb(drum, w, h, face, h / 2).R >= Rgb(plain, w, h, face, h / 2).R - 3);
        Assert.True(CounterDigits.Shade(h - 1, h, EdgeFade.DrumAmbient) < CounterDigits.Shade(h - 1, h) - 0.1f);
    }

    [Fact]
    public void TheHousingIsReadRoundTheWindow()
    {
        // a panel at 2 texels a pixel: olive, a white window 8 x 10 at (5, 5) with a chrome row above and below it
        const int ds = 2, W = 20, H = 24;
        int dw = W * ds, dh = H * ds;
        var panel = new byte[dw * dh * 4];
        for (int j = 0; j < dh; j++)
            for (int i = 0; i < dw; i++)
            {
                int x = i / ds, y = j / ds, o = ((dh - 1 - j) * dw + i) * 4;
                bool win = x is >= 5 and < 13 && y is >= 5 and < 15, chrome = x is >= 5 and < 13 && (y == 4 || y == 15);
                (panel[o], panel[o + 1], panel[o + 2], panel[o + 3]) = win ? ((byte)255, (byte)255, (byte)255, (byte)255) : chrome ? ((byte)170, (byte)170, (byte)170, (byte)255) : ((byte)88, (byte)96, (byte)52, (byte)255);
            }
        // two rows above and below: half chrome, half olive, the window itself never read
        var c = CockpitPanels.HousingAround(panel, dw, dh, ds, 5, 5, 8, 10);
        Assert.Equal(((byte)129, (byte)133, (byte)111), c);
        // off the panel's top: what is there of the band, and nothing at all gives the default grey
        Assert.Equal(((byte)88, (byte)96, (byte)52), CockpitPanels.HousingAround(panel, dw, dh, ds, 0, 1, 4, 2));
        Assert.Equal(CounterDigits.DefaultHousing, CockpitPanels.HousingAround(panel, dw, dh, ds, 0, 0, 4, H));
    }

    [Fact]
    public void TheLipCatchesAFaintLightAtTheWindowsTop()
    {
        const int s = 4;
        int w = CounterDigits.CellW * s, h = CounterDigits.CellH * s;
        var plain = Cell(s); var lip = Cell(s);
        CounterDigits.Paint(plain, s, 5, CounterDigits.Font!, DrumStyle.Inset, Edge(0.15f), Housing);
        CounterDigits.Paint(lip, s, 5, CounterDigits.Font!, DrumStyle.Inset, Edge(0.15f, lip: true), Housing);
        int face = w - 2;
        Assert.InRange(Rgb(lip, w, h, face, 0).R - Rgb(plain, w, h, face, 0).R, 20, 80);
        Assert.True(Rgb(lip, w, h, face, h - 1).R > Rgb(plain, w, h, face, h - 1).R);
        Assert.Equal(Rgb(plain, w, h, face, h / 2), Rgb(lip, w, h, face, h / 2));
    }

    [Theory]
    [InlineData(null, 0.25f, FeatherCurve.Pow2, 0.6f, 0.5f, false, false)]
    [InlineData("0", 0f, FeatherCurve.Pow2, 0.6f, 0.5f, false, false)]
    [InlineData("10", 0.10f, FeatherCurve.Pow2, 0.6f, 0.5f, false, false)]
    [InlineData("20lip", 0.20f, FeatherCurve.Pow2, 0.6f, 0.5f, true, false)]
    [InlineData(" 15LIP ", 0.15f, FeatherCurve.Pow2, 0.6f, 0.5f, true, false)]
    [InlineData("99", 0.25f, FeatherCurve.Pow2, 0.6f, 0.5f, false, false)]
    [InlineData("soft", 0.25f, FeatherCurve.Pow2, 0.6f, 0.5f, false, false)]
    [InlineData("20black", 0.20f, FeatherCurve.Ease, 0.6f, 0.5f, false, true)]
    [InlineData("20black-pow2", 0.20f, FeatherCurve.Pow2, 0.6f, 0.5f, false, true)]
    [InlineData("25cos", 0.25f, FeatherCurve.Cos, 0.6f, 0.5f, false, false)]
    [InlineData("30pow3deep", 0.30f, FeatherCurve.Pow3, 0.45f, 0.5f, false, false)]
    [InlineData("15drum", 0.15f, FeatherCurve.Pow2, 0.6f, 0.3f, false, false)]
    [InlineData("25-light+lip", 0.25f, FeatherCurve.Pow2, 0.75f, 0.5f, true, false)]
    [InlineData("25 ease lip", 0.25f, FeatherCurve.Ease, 0.6f, 0.5f, true, false)]
    public void TheFeatherHookParses(string? v, float fraction, FeatherCurve curve, float depth, float ambient, bool lip, bool black)
    {
        var e = EdgeFade.Parse(v);
        Assert.Equal(fraction, e.Fraction, 3);
        Assert.Equal(curve, e.Curve);
        Assert.Equal(depth, e.Depth, 3);
        Assert.Equal(ambient, e.Ambient, 3);
        Assert.Equal(lip, e.Lip);
        Assert.Equal(black, e.Black);
        // the spelling reads back the same (the cache key)
        Assert.Equal(e, EdgeFade.Parse(e.ToString()));
    }
}
