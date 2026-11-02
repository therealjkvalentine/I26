using System.Numerics;
using I76.Formats.Mission;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The host form's arena sketch on a made-up arena: a square floor in a ring of high ground, four starts on it.</summary>
public class ArenaSketchTests
{
    // one 128 x 128 tile: a floor of samples 34..93 at height 10 (raw 100), walls of 40 (raw 400) round it
    private static int? Height(int sx, int sz) =>
        sx is < 0 or >= 128 || sz is < 0 or >= 128 ? null : sx is >= 34 and < 94 && sz is >= 34 and < 94 ? 100 : 400;

    private static readonly Vector2[] Starts = [new(250, 250), new(400, 250), new(250, 400), new(400, 400)];
    private static readonly Vector2[] Corners = [new(175, 175), new(465, 465)];

    private static ArenaSketch Sketch() => ArenaSketch.Build(Height, Starts, Corners, [], 200, 120)!;

    [Fact]
    public void CountsTheStartsAndMeasuresTheirSpan()
    {
        var s = Sketch();
        Assert.Equal(4, s.Starts);
        Assert.Equal(290, s.Width, 1);
        Assert.Equal(290, s.Depth, 1);
        Assert.Equal("0.3 x 0.3 km", s.SizeText);
    }

    [Fact]
    public void DrawsTheStartsHatchesTheWallsAndLeavesTheFloorAndTheBoxEdgeBare()
    {
        var s = Sketch();
        const int k = 2, W = 200 * k, H = 120 * k;
        var px = s.Render(k, aliased: false);
        Assert.Equal(W * H * 4, px.Length);
        int A(int x, int y) => px[(y * W + x) * 4 + 3];
        // the region is square in samples, widened to the box's 5:3: the floor's centre (sample 64, 64) is the box's
        Assert.Equal(0, A(W / 2, H / 2));
        // a start: the x's crossing; the region spans 290 / 5 * 1.4 + 40 = 121.2 samples top to bottom, centred on 64
        float d = 290f / 5 * 1.4f + 40, w = d * W / H;
        int StartX(float sx) => (int)((sx / 5 - (64 - w / 2)) / w * W);
        int StartY(float sz) => (int)(((64 + d / 2) - sz / 5) / d * H);
        Assert.True(A(StartX(250), StartY(250)) > 200);
        // the high ground just beside the floor (sample 30, 64; the band reaches about 7 samples out here) is hatched: ink within a hatch pitch
        int hx = (int)((30 - (64 - w / 2)) / w * W), hy = H / 2;
        Assert.Contains(Enumerable.Range(0, 6), i => A(hx + i, hy) > 0);
        // no frame: the ground fades out before the box's edge, its corners stay paper
        Assert.Equal(0, A(0, 0));
        Assert.Equal(0, A(W - 1, H - 1));
        Assert.Equal(0, A(W / 2, 0));
    }

    [Fact]
    public void TheAliasedSketchIsInkOrPaper()
    {
        var px = Sketch().Render(1, aliased: true);
        var alphas = Enumerable.Range(0, px.Length / 4).Select(i => px[i * 4 + 3]).Where(a => a > 0).Distinct().ToList();
        // each layer is all or nothing: the alphas are only their opacities and those laid over one another
        Assert.All(alphas, a => Assert.True(a >= 114, $"alpha {a}"));
        Assert.NotEmpty(alphas);
    }

    [Fact]
    public void ContourFollowsAStep()
    {
        // a 4 x 3 grid, the right half high: one vertical run of segments at x = 1.5
        float[] g = [0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1];
        var segs = ArenaSketch.Contour(g, 4, 3, 0.5f);
        Assert.Equal(2, segs.Count);
        Assert.All(segs, s => { Assert.Equal(1.5f, s.A.X, 3); Assert.Equal(1.5f, s.B.X, 3); });
    }

    [Fact]
    public void InkBoundsAreTheDrawnExtent()
    {
        var s = Sketch();
        var px = s.Render(1, aliased: true);
        var (x0, y0, x1, y1) = s.InkBounds()!.Value;
        bool Ink(int x, int y) => px[(y * 200 + x) * 4 + 3] >= 40;
        Assert.Contains(Enumerable.Range(0, 200), x => Ink(x, y1));
        Assert.Contains(Enumerable.Range(0, 200), x => Ink(x, y0));
        Assert.Contains(Enumerable.Range(0, 120), y => Ink(x0, y));
        Assert.Contains(Enumerable.Range(0, 120), y => Ink(x1, y));
        for (int y = y1 + 1; y < 120; y++) Assert.DoesNotContain(Enumerable.Range(0, 200), x => Ink(x, y));
        // a square arena in a 5:3 box: its ink is about centred across it
        Assert.InRange((x0 + x1) / 2, 95, 105);
    }

    [Fact]
    public void HighGroundIsHatchedOnlyInABandBesideTheFloor()
    {
        // a floor in the middle of a 200 x 120 grid, high ground everywhere else: the far corners are left as paper
        int w = 200, h = 120;
        var m = new float[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = x is >= 80 and < 120 && y is >= 40 and < 80 ? 0 : 1;
        var b = ArenaSketch.Band(m, w, h);
        Assert.Equal(1, b[60 * w + 78]);                     // right beside the floor
        Assert.Equal(0, b[0]);                               // far away
        Assert.Equal(0, b[60 * w + 100]);                    // the floor stays floor
        Assert.All(Enumerable.Range(0, b.Length), i => Assert.True(b[i] <= m[i]));
        // a wide block of high ground inside a ring of floor stays hatched whole, its middle too
        for (int y = 10; y < 110; y++) for (int x = 10; x < 190; x++) m[y * w + x] = x is >= 15 and < 185 && y is >= 15 and < 105 ? 1 : 0;
        Assert.Equal(1, ArenaSketch.Band(m, w, h)[60 * w + 100]);
        // no floor at all: nothing to measure from, the mask as it was
        Assert.Equal(Enumerable.Repeat(1f, 10), ArenaSketch.Band(Enumerable.Repeat(1f, 10).ToArray(), 5, 2));
    }

    [Fact]
    public void NoPlacementsNoSketch() => Assert.Null(ArenaSketch.Build(Height, [], [], [], 200, 120));

    /// <summary>The stock arenas' starts and spans, as the lab's own reading of the same files gave them.</summary>
    [InstallTheory]
    [InlineData("m01", 17, "1.0 x 1.0 km")]
    [InlineData("m09", 9, "1.5 x 1.0 km")]
    [InlineData("m12", 12, "4.3 x 0.6 km")]   // 4.25 km: the lab printed 4.2 (round half to even)
    public void StockArenas(string stem, int starts, string size)
    {
        var msn = new MissionFile(TestInstall.Install.FindMission(stem)!);
        var ter = TerrainTiles.Load(msn, I76.Formats.Assembly.MissionWorld.FindTerrain(TestInstall.Install, msn)!);
        var s = ArenaSketch.Of(msn, ter, 200, 120)!;
        Assert.Equal(starts, s.Starts);
        Assert.Equal(size, s.SizeText);
        Assert.Contains(s.Render(1, aliased: true).Where((b, i) => i % 4 == 3), a => a > 0);
    }

    [Fact]
    public void PercentileIsNumpysLinear()
    {
        Assert.Equal(3.25f, ArenaSketch.Percentile([1, 2, 3, 4], 0.75f), 3);
        Assert.Equal(5f, ArenaSketch.Percentile([5], 0.75f), 3);
    }
}
