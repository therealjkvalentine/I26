using I76.Formats.Cockpit;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The damage panel's stamps at the packs' scales: a cell lands at its layout anchor, whole and nothing else, whatever scale the
/// sheet and the panel are served at, and the panel's mip levels under a stamp hold the stamp's own texels only.
/// </summary>
public class DamagePanelStampTests
{
    /// <summary>The damage panel's front, side and bottom armour as the cockpit layout places them (the chevron 52 x 9 at the panel's top).</summary>
    private const string Layout = """
        dst zsy_.map
            label farm               120    6       0    0
            label rarm               173   41       0    0
            label larm               116   41       0    0
            label barm               119  109       0    0
        src zsye.map
            label farm_off           238   50      52    9
            label farm_grn           238   59      52    9
            label rarm_off           156   75       4   46
            label rarm_grn           160   75       4   46
            label barm_off             0   98      55    4
            label barm_grn             0  102      55    4
        """;

    private static readonly EltLayout Elt = EltLayout.Parse(Layout);

    /// <summary>A 305 x 130 sheet at s texels a pixel, each cell its own flat colour (red = its index + 1), the cell's top-centre pixel marked in blue.</summary>
    private static byte[] Sheet(int s, out int sw, out int sh)
    {
        sw = 305 * s; sh = 130 * s;
        var b = new byte[sw * sh * 4];
        int n = 0;
        foreach (var block in Elt.Blocks)
        {
            if (!block.IsSource) continue;
            foreach (var l in block.Labels)
            {
                n++;
                for (int y = l.Y * s; y < (l.Y + l.H) * s; y++)
                    for (int x = l.X * s; x < (l.X + l.W) * s; x++)
                    {
                        int i = ((sh - 1 - y) * sw + x) * 4;
                        b[i] = (byte)n; b[i + 1] = 0; b[i + 2] = (byte)(y / s == l.Y && x / s == l.X + l.W / 2 ? 255 : 0); b[i + 3] = 255;
                    }
            }
        }
        return b;
    }

    private static (byte R, byte G, byte B, byte A) At(byte[] img, int w, int h, int x, int y) { int i = ((h - 1 - y) * w + x) * 4; return (img[i], img[i + 1], img[i + 2], img[i + 3]); }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(4, 8)]
    [InlineData(8, 4)]
    [InlineData(1, 8)]
    public void ACellLandsAtItsAnchorWholeAndNothingElse(int ss, int ds)
    {
        var sheet = Sheet(ss, out int sw, out int sh);
        int dw = 256 * ds, dh = 128 * ds;
        int n = 0;
        foreach (var (sprite, anchor) in new[] { ("farm_off", "farm"), ("farm_grn", "farm"), ("rarm_off", "rarm"), ("rarm_grn", "larm"), ("barm_off", "barm"), ("barm_grn", "barm") })
        {
            n++;
            var panel = new byte[dw * dh * 4];
            for (int i = 0; i < panel.Length; i += 4) { panel[i + 1] = 200; panel[i + 3] = 255; }
            var s = Elt.Find(sprite)!.Value.Label; var a = Elt.Find(anchor)!.Value.Label;
            CockpitPanels.Blit(sheet, sw, sh, ss, s.X, s.Y, s.W, s.H, panel, dw, dh, ds, a.X, a.Y, keyed: true);
            byte cell = 0;
            for (int j = 0; j < Elt.Blocks[1].Labels.Count; j++) if (Elt.Blocks[1].Labels[j].Name == sprite) cell = (byte)(j + 1);
            // inside the anchor's rectangle every texel is the cell's; round it (two pixels each way) the panel's own
            for (int y = Math.Max(0, (a.Y - 2) * ds); y < Math.Min(dh, (a.Y + s.H + 2) * ds); y++)
                for (int x = Math.Max(0, (a.X - 2) * ds); x < Math.Min(dw, (a.X + s.W + 2) * ds); x++)
                {
                    bool inside = x >= a.X * ds && x < (a.X + s.W) * ds && y >= a.Y * ds && y < (a.Y + s.H) * ds;
                    var t = At(panel, dw, dh, x, y);
                    if (inside) Assert.True(t.R == cell && t.G == 0, $"{sprite} at {ss}x on {ds}x: ({x}, {y}) holds {t}, not the cell");
                    else Assert.True(t.R == 0 && t.G == 200, $"{sprite} at {ss}x on {ds}x: ({x}, {y}) outside the anchor's rectangle was written {t}");
                }
            // the chevron's point (the cell's top-centre pixel) sits at the anchor's top row, centre column, as a ds-square
            int px = (a.X + s.W / 2) * ds, py = a.Y * ds;
            Assert.Equal(255, At(panel, dw, dh, px, py).B);
            Assert.Equal(255, At(panel, dw, dh, px + ds - 1, py + ds - 1).B);
            Assert.Equal(0, At(panel, dw, dh, px - 1, py).B);
            Assert.Equal(0, At(panel, dw, dh, px + ds, py).B);
            Assert.Equal(0, At(panel, dw, dh, px, py + ds).B);
        }
        Assert.Equal(6, n);
    }

    [Fact]
    public void TheLevelsUnderAStampHoldOnlyTheStampsTexels()
    {
        // the 8x panel (2048 x 1024) in its base colour with the next state's colour painted in the row above and below the chevron's
        // place, as a neighbouring cell would sit on the sheet; the chevron stamped at its anchor; then the chain the live texture makes
        const int ds = 8;
        int dw = 256 * ds, dh = 128 * ds;
        var a = Elt.Find("farm")!.Value.Label; var s = Elt.Find("farm_off")!.Value.Label;
        var panel = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int i = (y * dw + x) * 4;
                bool beside = x >= a.X * ds && x < (a.X + s.W) * ds && (y / ds == a.Y - 1 || y / ds == a.Y + s.H);
                panel[i] = (byte)(beside ? 0 : 60); panel[i + 1] = (byte)(beside ? 220 : 60); panel[i + 2] = 60; panel[i + 3] = 255;
            }
        var sheet = Sheet(ds, out int sw, out int sh);
        CockpitPanels.Blit(sheet, sw, sh, ds, s.X, s.Y, s.W, s.H, panel, dw, dh, ds, a.X, a.Y, keyed: true);
        // rows as stored (bottom-up), as the live texture holds them
        int x0 = a.X * ds, x1 = (a.X + s.W) * ds, y0 = dh - (a.Y + s.H) * ds, y1 = dh - a.Y * ds;
        byte[] above = panel;
        int lw = dw, lh = dh;
        for (int k = 1; k < LiveTexture.LevelCount(dw, dh); k++)
        {
            int nw = LiveTexture.LevelSize(dw, k), nh = LiveTexture.LevelSize(dh, k);
            var level = new byte[nw * nh * 4];
            LiveTexture.Downsample(above, lw, lh, level, 0, 0, nw, nh);
            int f = 1 << k, across = 0;
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int i = (y * nw + x) * 4;
                    bool inside = x * f >= x0 && (x + 1) * f <= x1 && y * f >= y0 && (y + 1) * f <= y1;
                    bool outside = (x + 1) * f <= x0 || x * f >= x1 || (y + 1) * f <= y0 || y * f >= y1;
                    // a texel whose box lies inside the stamp is the cell's colour (red 1, green 0); one whose box lies outside never
                    // holds the stamp's red; a box across the edge mixes the stamp with what is beside it on the panel and no more
                    if (inside) Assert.True(level[i] == 1 && level[i + 1] == 0, $"level {k} ({x}, {y}) under the stamp holds ({level[i]}, {level[i + 1]})");
                    else if (outside) Assert.True(level[i] != 1, $"level {k} ({x}, {y}) beside the stamp holds the stamp's red");
                    else { across++; Assert.True(level[i] <= 60 && level[i + 2] >= 15, $"level {k} ({x}, {y}) across the edge holds ({level[i]}, {level[i + 1]}, {level[i + 2]})"); }
                }
            // down to the level that is the original's pixels (the 8x halved three times) no texel crosses the stamp's edge at all
            if (k <= 3) Assert.Equal(0, across); else Assert.True(across > 0);
            above = level; lw = nw; lh = nh;
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(1, 8)]
    public void TheChevronIsDrawnFromTheRowAboveItsLabelPointAndAll(int ss, int ds)
    {
        // the damage sheet as the original packs it: each chevron state's art from the row above its label (its point, marked blue)
        // to the label's second-last row, so the label's last row holds the next state's point; drawn a row up, a state shows its
        // own point at the anchor's row above and not the next one's under it
        int sw = 305 * ss, sh = 130 * ss;
        var sheet = new byte[sw * sh * 4];
        var off = Elt.Find("farm_off")!.Value.Label; var grn = Elt.Find("farm_grn")!.Value.Label; var a = Elt.Find("farm")!.Value.Label;
        foreach (var (l, red) in new[] { (off, 1), (grn, 2) })
            for (int y = (l.Y - 1) * ss; y < (l.Y + l.H - 1) * ss; y++)
                for (int x = l.X * ss; x < (l.X + l.W) * ss; x++)
                {
                    int i = ((sh - 1 - y) * sw + x) * 4;
                    sheet[i] = (byte)red; sheet[i + 2] = (byte)(y / ss == l.Y - 1 ? 255 : 0); sheet[i + 3] = 255;
                }
        Assert.Equal(1, CockpitPanels.RowsUp(off, up: true));
        Assert.Equal(0, CockpitPanels.RowsUp(off, up: false));
        Assert.Equal(0, CockpitPanels.RowsUp(new EltLabel("engine_off", 0, 0, 61, 12), up: true));
        int dw = 256 * ds, dh = 128 * ds, u = CockpitPanels.RowsUp(off, up: true);
        var panel = new byte[dw * dh * 4];
        CockpitPanels.Blit(sheet, sw, sh, ss, off.X, off.Y - u, off.W, off.H, panel, dw, dh, ds, a.X, a.Y - u, keyed: true);
        int cx = (a.X + off.W / 2) * ds;
        Assert.Equal((1, 255), (At(panel, dw, dh, cx, (a.Y - 1) * ds).R, At(panel, dw, dh, cx, (a.Y - 1) * ds).B));
        for (int y = (a.Y - 1) * ds; y < (a.Y + off.H - 1) * ds; y++) Assert.Equal(1, At(panel, dw, dh, cx, y).R);
        Assert.Equal(0, At(panel, dw, dh, cx, (a.Y + off.H - 1) * ds).A);
        Assert.Equal(0, At(panel, dw, dh, cx, (a.Y - 2) * ds).A);
        // drawn as labelled, the point is lost and the next state's point shows under the chevron
        var asLabelled = new byte[dw * dh * 4];
        CockpitPanels.Blit(sheet, sw, sh, ss, off.X, off.Y, off.W, off.H, asLabelled, dw, dh, ds, a.X, a.Y, keyed: true);
        Assert.Equal(0, At(asLabelled, dw, dh, cx, (a.Y - 1) * ds).A);
        Assert.Equal((2, 255), (At(asLabelled, dw, dh, cx, (a.Y + off.H - 1) * ds).R, At(asLabelled, dw, dh, cx, (a.Y + off.H - 1) * ds).B));
    }
}
