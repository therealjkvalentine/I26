using Godot;
using I76.Game.Sim;
using Xunit;
using Xunit.Abstractions;

namespace I76.Game.Tests;

/// <summary>The rounder rear mirror's shape (MirrorRound): a tapered outline fitted from a mask, and its core shrunk by the radius. Synthetic masks.</summary>
public class MirrorRoundTests(ITestOutputHelper output)
{
    /// <summary>A w × h mask of a trapezoid: rows top..bottom, its left side from x0 at the top to x1 at the bottom, mirrored on the right, the corners cut by <paramref name="cut"/> pixels.</summary>
    private static bool[] Trapezoid(int w, int h, int top, int bottom, float x0, float x1, int cut = 0)
    {
        var m = new bool[w * h];
        for (int y = top; y <= bottom; y++)
        {
            float l = x0 + (x1 - x0) * (y + 0.5f - top) / (bottom + 1 - top);
            int c = y - top < cut ? cut - (y - top) : bottom - y < cut ? cut - (bottom - y) : 0;
            for (int x = 0; x < w; x++) m[y * w + x] = x >= l + c && x < w - l - c;
        }
        return m;
    }

    [Fact]
    public void TheSidesAreFittedAwayFromTheCorners()
    {
        var q = MirrorRound.Fit(Trapezoid(256, 64, 2, 61, 2, 8, cut: 4), 256, 64)!.Value;
        Assert.Equal(2f, q.TL.Y, 0.01f);
        Assert.Equal(62f, q.BL.Y, 0.01f);
        // the left side runs 2 to 8 over the rows, the corners' cut not in it
        Assert.InRange(q.TL.X, 1.5f, 3f);
        Assert.InRange(q.BL.X, 7.5f, 9f);
        Assert.Equal(256 - q.TL.X, q.TR.X, 1.01f);   // a pixel either way: the mask's edges fall on whole pixels
    }

    [Fact]
    public void TheCoreIsTheShapeMovedInwardByTheRadius()
    {
        var q = new MirrorRound.Quad(new(0, 0), new(100, 0), new(100, 40), new(0, 40));
        var s = MirrorRound.Shrink(q, 5);
        Assert.Equal(new Vector2(5, 5), s.TL);
        Assert.Equal(new Vector2(95, 35), s.BR);
        // a tapered side moves along its normal: further across than the radius
        var t = MirrorRound.Shrink(new MirrorRound.Quad(new(0, 0), new(100, 0), new(90, 40), new(10, 40)), 5);
        Assert.True(t.TL.X > 5.1f && t.TR.X < 94.9f);
    }

    [Fact]
    public void NoHookIsTheArtsOwnCorners()
    {
        Assert.Equal(0f, MirrorRound.Radius(null));
        Assert.Equal(10f, MirrorRound.Radius("10"));
        Assert.Equal(24f, MirrorRound.Radius("99"));
        Assert.Null(MirrorRound.Fit(new bool[16], 4, 4));
    }

    [Fact]
    public void TheRearMirrorKeepsTheArtsCornersByDefault()
    {
        Assert.Equal(0, MirrorRound.DefaultRadius);
        Assert.Equal(0, new I76.Game.Shell.ShellSettings().MirrorRound);
        Assert.Equal(GlassRound.DefaultShare, new I76.Game.Shell.ShellSettings().SideMirrorRound);
        Assert.Equal(GlassRound.DefaultShare, GlassRound.Share(null, GlassRound.DefaultShare));
        Assert.Equal(50, GlassRound.Share("80", 0));
        Assert.Equal(10, GlassRound.Share("10", 25));
    }

    /// <summary>A door mirror's glass: the hull of its corners counter-clockwise, without the points inside it.</summary>
    [Fact]
    public void TheGlassOutlineIsItsHull()
    {
        var h = GlassRound.Hull([new(0, 0), new(2, 0), new(2, 1), new(0, 1), new(1, 0.5f), new(1, 0)]);
        Assert.Equal(4, h.Count);
        float area = 0;
        for (int i = 0; i < h.Count; i++) area += h[i].X * h[(i + 1) % h.Count].Y - h[(i + 1) % h.Count].X * h[i].Y;
        Assert.True(area > 0, "counter-clockwise");
    }

    [Fact]
    public void TheRoundedGlassIsTheHullErodedByTheRadius()
    {
        var rect = GlassRound.Hull([new(0, 0), new(2, 0), new(2, 1), new(0, 1)]);
        Assert.Equal(0.5f, GlassRound.Inradius(rect), 0.001f);
        Assert.Null(GlassRound.Shape(rect, 0));   // the glass's own corners
        // 25 % of the shorter side: a radius of 0.25, the core the rectangle 0.25 in from each side
        var (core, r) = GlassRound.Shape(rect, 25)!.Value;
        Assert.Equal(0.25f, r, 0.001f);
        Assert.Equal(0.25f, core.Min(q => q.X), 0.001f);
        Assert.Equal(1.75f, core.Max(q => q.X), 0.001f);
        Assert.Equal(0.75f, core.Max(q => q.Y), 0.001f);
        // 50 %: round ends, just inside the largest circle (a stadium's core is nearly a line)
        var (line, rr) = GlassRound.Shape(rect, 50)!.Value;
        Assert.InRange(rr, 0.48f, 0.5f);
        Assert.InRange(line.Max(q => q.Y) - line.Min(q => q.Y), 0f, 0.05f);
        // a hexagon's short sides drop out as it erodes; the core stays convex and inside it
        var hex = GlassRound.Hull(Enumerable.Range(0, 6).Select(k => new Vector2(Mathf.Cos(k * Mathf.Pi / 3), 0.6f * Mathf.Sin(k * Mathf.Pi / 3))));
        var (hc, hr) = GlassRound.Shape(hex, 35)!.Value;
        Assert.True(hc.Count is >= 3 and <= GlassRound.MaxCorners);
        Assert.True(hr > 0 && hr < GlassRound.Inradius(hex));
    }

    [Fact]
    public void TheMouldingsOuterEdgeIsTheHousingsByDefault()
    {
        Assert.Equal(GlassRound.DefaultRimOut, new I76.Game.Shell.ShellSettings().SideMirrorRimOut);
        Assert.Equal(GlassRound.RimOut.Housing, GlassRound.Out(null, GlassRound.DefaultRimOut));   // the housing's outline unchanged, as the 1997 look's
        Assert.Equal(new GlassRound.RimOut(true, 2), GlassRound.Out(null, GlassRound.RoundedRimOut));
        Assert.False(GlassRound.Out(null, 0).On);                     // 0: the housing's straight outline, the look before
        Assert.False(GlassRound.Out("off", 3).On);
        Assert.Equal(new GlassRound.RimOut(true, 0), GlassRound.Out("in", 0));
        Assert.Equal(new GlassRound.RimOut(true, 0, true, true), GlassRound.Out("cover,dark", 0));
        Assert.Equal(new GlassRound.RimOut(true, 1.5f, false, true), GlassRound.Out("1.5, dark", 0));
        Assert.Equal(GlassRound.MaxRimOut, GlassRound.Out("40", 0).Widths);
        Assert.Equal(GlassRound.MaxRimOut, GlassRound.Out(null, 99).Widths);
    }

    [Fact]
    public void ADistanceToAConvexOutlineIsNoughtInside()
    {
        var sq = GlassRound.Hull([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        Assert.Equal(0f, GlassRound.Distance(sq, new(0.5f, 0.5f)));
        Assert.Equal(0.5f, GlassRound.Distance(sq, new(1.5f, 0.5f)), 0.0001f);
        Assert.Equal(MathF.Sqrt(2), GlassRound.Distance(sq, new(2, 2)), 0.0001f);
    }

    /// <summary>The growth that takes in the housing's corners: a rectangle's corner lies r·√2 from its core, so r·(√2 − 1) beyond the rounded outline.</summary>
    [Fact]
    public void CoverReachesTheHousingsCorners()
    {
        var rect = GlassRound.Hull([new(0, 0), new(2, 0), new(2, 1), new(0, 1)]);
        var (core, r) = GlassRound.Shape(rect, 25)!.Value;
        float g = GlassRound.Cover(rect, core, r);
        Assert.Equal(0.25f * (MathF.Sqrt(2) - 1), g, 0.0005f);
        Assert.All(rect, c => Assert.True(GlassRound.Distance(core, c) <= r + g + 1e-4f));
        // a hexagon's blunter corners stick out less than a rectangle's of the same rounding
        var hex = GlassRound.Hull(Enumerable.Range(0, 6).Select(k => new Vector2(Mathf.Cos(k * Mathf.Pi / 3), Mathf.Sin(k * Mathf.Pi / 3))));
        var (hc, hr) = GlassRound.Shape(hex, 30)!.Value;
        Assert.True(GlassRound.Cover(hex, hc, hr) < 0.42f * hr);
    }

    /// <summary>The grown glass's mesh holds the whole ring: every point within the radius of the core is inside it, and it reaches no further than the circles' outer polygon.</summary>
    [Fact]
    public void TheGrownMeshHoldsTheRing()
    {
        var rect = GlassRound.Hull([new(0, 0), new(2, 0), new(2, 1), new(0, 1)]);
        var (core, r) = GlassRound.Shape(rect, 30)!.Value;
        float R = r + 0.06f;
        var around = GlassRound.Around(core, R);
        Assert.True(around.Count <= 4 * 16);
        foreach (var c in core)
            for (int k = 0; k < 360; k += 5)
            {
                var p = c + new Vector2(Mathf.Cos(Mathf.DegToRad(k)), Mathf.Sin(Mathf.DegToRad(k))) * R;
                Assert.True(GlassRound.Distance(around, p) < 1e-4f, $"{p} outside");
            }
        Assert.All(around, q => Assert.InRange(GlassRound.Distance(core, q), R - 1e-4f, R / MathF.Cos(MathF.PI / 16) + 1e-4f));
    }

    /// <summary>
    /// The ring is the same width on screen all round: a glass seen foreshortened (its picture stretched 1.6 times across and
    /// sheared, as the Piranha's left glass is near the screen's edge) has its glass's edge one ring width inside the outer edge
    /// everywhere, edge middles and corners, within a quarter of a pixel; the ring as a band of picture units drew its sides
    /// 1.6 times as wide as its top.
    /// </summary>
    [Fact]
    public void TheRingIsTheSameWidthOnScreenAllRound()
    {
        var rect = GlassRound.Hull([new(0, 0), new(1.5f, 0), new(1.5f, 1), new(0, 1)]);
        var (core, r) = GlassRound.Shape(rect, GlassRound.DefaultShare)!.Value;
        float rimW = MirrorRim.OuterShare, rimOut = GlassRound.RoundedRimOut * rimW, ringW = MirrorRim.RingWidth(rimOut);
        // screen pixels from picture units: 400 px per unit up, stretched and sheared across
        float k = 400, ax = 1.6f * k, sh = 0.3f * k;
        Vector2 ToScreen(Vector2 p) => new(ax * p.X + sh * p.Y, k * p.Y);
        Vector2 ToPicture(Vector2 s) => new((s.X - sh * s.Y / k) / ax, s.Y / k);
        float Sd(Vector2 s) => GlassRound.Distance(core, ToPicture(s)) - r;
        float pxPerUnit = MathF.Sqrt(ax * k);
        (float In, float Width) At(Vector2 s)
        {
            // the screen gradient as the shader's dFdx / dFdy take it, a pixel apart
            float g = new Vector2(Sd(s + new Vector2(0.5f, 0)) - Sd(s - new Vector2(0.5f, 0)), Sd(s + new Vector2(0, 0.5f)) - Sd(s - new Vector2(0, 0.5f))).Length();
            return GlassRound.RingOnScreen(Sd(s), g, rimOut, ringW, pxPerUnit);
        }
        var mid = ToScreen(new Vector2(0.75f, 0.5f));
        float width = ringW * pxPerUnit;
        Assert.InRange(width, 7, 15);
        var seen = new List<float>(); var before = new List<float>();
        for (int deg = 0; deg < 360; deg += 3)
        {
            var dir = new Vector2(MathF.Cos(MathF.PI * deg / 180), MathF.Sin(MathF.PI * deg / 180));
            // the outer edge along the ray from the middle, then a ring's width inward along the outline's normal on screen
            float lo = 0, hi = 2000;
            for (int i = 0; i < 60; i++) { float m = (lo + hi) / 2; if (At(mid + dir * m).In > 0) lo = m; else hi = m; }
            var edge = mid + dir * lo;
            var n = new Vector2(Sd(edge + new Vector2(0.01f, 0)) - Sd(edge - new Vector2(0.01f, 0)), Sd(edge + new Vector2(0, 0.01f)) - Sd(edge - new Vector2(0, 0.01f))).Normalized();
            for (lo = 0, hi = 3 * width; hi - lo > 1e-4f;) { float m = (lo + hi) / 2; if (At(edge - n * m).In < At(edge - n * m).Width) lo = m; else hi = m; }
            seen.Add(lo);
            Assert.InRange(lo, width - 0.25f, width + 0.25f);
            // the band of picture units it replaces: the glass from rimW inside the rounded outline
            for (lo = 0, hi = 6 * width; hi - lo > 1e-4f;) { float m = (lo + hi) / 2; if (Sd(edge - n * m) > -rimW) lo = m; else hi = m; }
            before.Add(lo);
        }
        Assert.True(seen.Max() - seen.Min() < 0.5f);
        Assert.True(before.Max() / before.Min() > 1.4f, $"{before.Min()}..{before.Max()}");
    }

    /// <summary>
    /// The halving moved the glass's edge out, not the outer edge in: the ring's outer edge is where it was before (the default two of
    /// the rim's widths before the halving beyond the rounded glass, 0.0375 of the glass's height, where it covered the housing's edges),
    /// the ring half as wide as it was there (the outer edge plus the old rim inside the outline: 0.05625, now 0.028125), so the
    /// glass's edge lies 0.0094 beyond the rounded outline where it lay 0.01875 inside it.
    /// </summary>
    [Fact]
    public void TheOuterEdgeStaysWhereItWasAndTheGlassGrowsOut()
    {
        float before = 6f / 64 * 2 / 3 * 0.3f;   // the rim's width before the halving
        Assert.Equal(before, MirrorRim.OuterShare, 0.00001f);
        float rimOut = GlassRound.RoundedRimOut * MirrorRim.OuterShare;
        Assert.Equal(0.0375f, rimOut, 0.00001f);
        Assert.Equal((rimOut + before) / 2, MirrorRim.RingWidth(rimOut), 0.00001f);
        Assert.Equal(0.028125f, MirrorRim.RingWidth(rimOut), 0.00001f);
        // seen square at 400 px a glass height: the outer edge where the outline's distance is rimOut, the glass one width inside it
        float k = 400;
        var (atOuter, width) = GlassRound.RingOnScreen(rimOut, 1 / k, rimOut, MirrorRim.RingWidth(rimOut), k);
        Assert.Equal(0f, atOuter, 0.001f);
        Assert.Equal(11.25f, width, 0.01f);
        float glassEdge = rimOut - width / k;
        Assert.Equal(rimOut - 0.028125f, glassEdge, 0.0001f);
        Assert.True(glassEdge > -before && glassEdge > 0);   // beyond the rounded outline, where the old ring's inner edge lay inside it
        // "in" (no outer growth): the ring the rim alone, inside the outline as before
        Assert.Equal(MirrorRim.RimShare, MirrorRim.RingWidth(0), 0.00001f);
    }

    /// <summary>
    /// No corner of the glass's outline (the housing's edge) shows past the ring as a point: on a pane sharper-cornered than the rounded
    /// glass (a parallelogram sheared as the CC cars' glass is) the ring as round as the glass left corners outside its outer edge;
    /// the outer edge's corners are now tightened until every corner of the outline lies inside it, while along the straight runs
    /// it is where it was (the outline lies on them), so the band is as wide there. The even variant grows the whole ring instead.
    /// </summary>
    [Fact]
    public void TheRingHidesTheOutlinesPoints()
    {
        var pane = GlassRound.Hull([new(0, 0), new(1.4f, 0), new(1.8f, 1), new(0.4f, 1)]);
        var (core, r) = GlassRound.Shape(pane, GlassRound.DefaultShare)!.Value;
        float grow = GlassRound.RoundedRimOut * MirrorRim.OuterShare, margin = 0.004f;
        Assert.Contains(pane, v => GlassRound.Distance(core, v) - r > grow);   // as round as the glass: points stick out
        var (oc, or) = GlassRound.OuterShape(pane, core, r, grow, margin);
        Assert.True(or < r);
        Assert.All(pane, v => Assert.True(GlassRound.Distance(oc, v) - or <= grow - margin + 1e-5f, $"{v} past the ring"));
        // the straight runs: each side's middle as far inside the outer edge as inside the glass's rounded shape's
        for (int i = 0; i < pane.Count; i++)
        {
            var m = (pane[i] + pane[(i + 1) % pane.Count]) / 2;
            Assert.Equal(SignedDistance(core, m) - r, SignedDistance(oc, m) - or, 0.0005f);
        }
        // the band at the sharpest corner, in ring widths (the glass's edge stays one width inside the round shape's outer edge)
        float corner = pane.Max(v => GlassRound.Distance(core, v) - r - grow) / MirrorRim.RingWidth(grow) + 1;
        output.WriteLine($"outer corner radius {or:0.000} (glass {r:0.000}); band at the sharpest corner about {corner:0.0} ring widths");
        // a glass whose corners fit already keeps its shape
        var rect = GlassRound.Hull([new(0, 0), new(1.5f, 0), new(1.5f, 1), new(0, 1)]);
        var rs = GlassRound.Shape(rect, GlassRound.DefaultShare)!.Value;
        float rg = GlassRound.UniformGrow(rect, rs.Core, rs.R, grow, margin);
        // the even variant: grown until the corners fit, never less than before
        float ug = GlassRound.UniformGrow(pane, core, r, grow, margin);
        Assert.True(ug > grow);
        Assert.All(pane, v => Assert.True(GlassRound.Distance(core, v) - r <= ug - margin + 1e-5f));
        Assert.True(rg >= grow);
    }

    // the door glasses' outlines as the driver sees them (picture units, the glass's height 1), logged by I76_MIRROR_POINTS at
    // 3440x1440: the Piranha's left (six-sided), its right, and the CC cars' left with its housing's rim 1.6 cm behind the glass
    private static readonly Vector2[] PiranhaLeft = [new(0, 0.6332f), new(0.1365f, 0.2098f), new(0.3680f, 1), new(0.5962f, 0), new(0.8278f, 0.7902f), new(0.8520f, 0.2444f)];
    private static readonly Vector2[] PiranhaRight = [new(0, 0.2749f), new(0.0395f, 0.8305f), new(0.2137f, 0), new(0.4455f, 1), new(0.6196f, 0.1695f), new(0.7524f, 0.5890f)];
    private static readonly Vector2[] CcLeft = [new(0, 0.1555f), new(0.0757f, 1), new(0.9255f, 0), new(1.0012f, 0.8445f)];
    private static readonly (Vector2, float)[] CcHead = [(new(0.0756f, 0.1716f), -0.0155f), (new(0.1494f, 0.9950f), -0.0155f), (new(0.9778f, 0.0202f), -0.0157f), (new(1.0515f, 0.8432f), -0.0158f),
        (new(0.6862f, 0.5195f), -0.0448f), (new(0.9259f, -0.3360f), -0.0777f), (new(1.2262f, -0.1226f), -0.1961f), (new(0, 0.1555f), -0.0024f)];

    /// <summary>
    /// The border one thickness all round (a playtest: "we don't want any part of them to be thicker than another part"): on every door
    /// glass's outline the ring's outer edge (GlassRound.EvenShape) and the glass's edge (that outline inset by the band) are measured
    /// along the whole perimeter, the band within 2 % of its width everywhere, the corners included; every corner of the outline lies
    /// inside the outer edge; along the straight runs the edge lies the rim's reach past the outline (even), the glass's corners stay
    /// round (no point) and no rounder than the glass's own.
    /// </summary>
    [Theory]
    [InlineData("piranha-left")]
    [InlineData("piranha-right")]
    [InlineData("cc-left")]
    [InlineData("cc-left-housing")]
    public void TheBandIsOneThicknessAllRound(string which)
    {
        var pane = GlassRound.Hull(which switch { "piranha-left" => PiranhaLeft, "piranha-right" => PiranhaRight, _ => CcLeft });
        var outline = which == "cc-left-housing" ? GlassRound.Housing(pane, CcHead) : pane;
        var (_, r) = GlassRound.Shape(pane, GlassRound.DefaultShare)!.Value;
        float reach = GlassRound.RoundedRimOut * MirrorRim.OuterShare, ringW = MirrorRim.RingWidth(reach), margin = 0.004f;
        var (core, e, over) = GlassRound.EvenShape(outline, r, reach, margin, ringW);
        Assert.Equal(reach, over, 1e-5f);   // these outlines take the rim's reach as it is
        Assert.True(e <= r + 1e-5f && e > 0);
        float F(Vector2 p) => SignedDistance(core, p) - e;   // the shader's sdo
        Assert.All(outline, v => Assert.True(F(v) <= over - margin + 1e-4f, $"{v} past the outer edge"));
        // each side's middle: the outer edge the reach past it
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Count];
            if (a.DistanceTo(b) > 2 * (e + over)) Assert.Equal(0f, F((a + b) / 2), 0.0005f);
        }
        // the two edges round the whole perimeter: 720 rays from the middle, each edge found by halving
        var mid = outline.Aggregate(Vector2.Zero, (x, y) => x + y) / outline.Count;
        Vector2 Edge(Vector2 dir, float level)
        {
            float lo = 0, hi = 3;
            for (int k = 0; k < 40; k++) { float m = (lo + hi) / 2; if (F(mid + dir * m) <= level) lo = m; else hi = m; }
            return mid + dir * lo;
        }
        var outer = new List<Vector2>(); var inner = new List<Vector2>();
        for (int k = 0; k < 720; k++)
        {
            var dir = new Vector2(MathF.Cos(k * MathF.PI / 360), MathF.Sin(k * MathF.PI / 360));
            outer.Add(Edge(dir, over)); inner.Add(Edge(dir, over - ringW));
        }
        // the band at each point of the outer edge: its distance to the glass's edge
        var widths = outer.Select(p => inner.Min(q => q.DistanceTo(p))).ToList();
        float lo_ = widths.Min(), hi_ = widths.Max();
        output.WriteLine($"{which}: corner radius outer {e + over:0.000} glass {e + over - ringW:0.000} (the glass's own {r:0.000}); reach {over:0.0000}; band {lo_ / ringW:0.000}-{hi_ / ringW:0.000} widths");
        Assert.True(hi_ - lo_ <= 0.02f * ringW, $"band {lo_:0.0000}-{hi_:0.0000}");
        Assert.True(e + over - ringW >= 0.5f * ringW);
        // the glass's picture spread over its inside where it reaches past the pane
        var (bmin, bmax) = GlassRound.PictureBox(core, e + over - ringW, 1);
        Assert.All(inner, q => Assert.True(q.X >= bmin.X - 1e-4f && q.X <= bmax.X + 1e-4f && q.Y >= bmin.Y - 1e-4f && q.Y <= bmax.Y + 1e-4f));
        Assert.True(bmin.X <= 0 && bmin.Y <= 0 && bmax.X >= 1 && bmax.Y >= 1);
    }

    /// <summary>A corner too sharp for the rim's reach: the reach grows until the glass's corner keeps half a band's radius, the band still even.</summary>
    [Fact]
    public void ASharpCornerReachesFurtherRatherThanAPoint()
    {
        var pane = GlassRound.Hull([new(0, 0), new(1.6f, 0), new(0.2f, 1)]);
        float ringW = MirrorRim.RingWidth(GlassRound.RoundedRimOut * MirrorRim.OuterShare);
        var (core, e, over) = GlassRound.EvenShape(pane, 0.3f, 0.005f, 0.004f, ringW);
        Assert.True(over > 0.005f);
        Assert.Equal(1.5f * ringW, e + over, 1e-4f);
        Assert.All(pane, v => Assert.True(GlassRound.Distance(core, v) - e <= over - 0.004f + 1e-4f));
    }

    /// <summary>The housing's rim seen past the glass is covered, its arm is not; a point is seen where the eye's ray meets the glass's plane.</summary>
    [Fact]
    public void TheHousingsRimIsCoveredNotItsArm()
    {
        var cover = GlassRound.Housing(GlassRound.Hull(CcLeft), CcHead);
        Assert.Contains(new Vector2(1.0515f, 0.8432f), cover);
        Assert.Contains(new Vector2(0.9778f, 0.0202f), cover);
        Assert.DoesNotContain(cover, v => v.Y < -0.01f || v.X > 1.06f);
        var pl = (O: new Vector3(0, 0, 0), A: new Vector3(1, 0, 0), B: new Vector3(0, 1, 0));
        var eye = new Vector3(0.5f, 0.5f, 2);
        var on = GlassRound.Seen(eye, pl, new Vector3(0.3f, 0.7f, 0))!.Value;
        Assert.Equal(0.3f, on.Uv.X, 1e-5f); Assert.Equal(0.7f, on.Uv.Y, 1e-5f); Assert.Equal(0f, on.Depth, 1e-5f);
        var behind = GlassRound.Seen(eye, pl, new Vector3(1.5f, 0.5f, -0.02f))!.Value;
        Assert.Equal(-0.02f, behind.Depth, 1e-5f); Assert.Equal(0.5f + 2f / 2.02f, behind.Uv.X, 1e-4f);   // nearer the middle: seen through the plane
    }

    private static float SignedDistance(List<Vector2> poly, Vector2 p)
    {
        float d = GlassRound.Distance(poly, p);
        if (d > 0) return d;
        float m = float.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count], e = b - a;
            float t = Math.Clamp((p - a).Dot(e) / e.Dot(e), 0, 1);
            m = Math.Min(m, (a + e * t).DistanceTo(p));
        }
        return -m;
    }

    /// <summary>The pane's plane from its corners' picture coordinates: recovered exactly for a flat pane, and a point past its outline lands in it.</summary>
    [Fact]
    public void ThePlaneMapsPictureCoordinatesOntoThePane()
    {
        var o = new Vector3(-0.9f, 1.05f, -0.3f); var a = new Vector3(0.12f, 0.01f, 0.05f); var b = new Vector3(-0.01f, 0.08f, 0.02f);
        var pts = new (Vector2, Vector3)[] { (new(0, 0), o), (new(1.6f, 0), o + a * 1.6f), (new(1.6f, 1), o + a * 1.6f + b), (new(0, 1), o + b), (new(0.8f, 1.1f), o + a * 0.8f + b * 1.1f) };
        var (po, pa, pb) = GlassRound.Plane(pts)!.Value;
        Assert.True(po.DistanceTo(o) < 1e-4f && pa.DistanceTo(a) < 1e-4f && pb.DistanceTo(b) < 1e-4f);
        var out_ = po + pa * -0.1f + pb * 1.2f;
        Assert.True(out_.DistanceTo(o + a * -0.1f + b * 1.2f) < 1e-4f);
        // collinear corners hold no plane
        Assert.Null(GlassRound.Plane([(new(0, 0), o), (new(1, 0), o + a), (new(2, 0), o + a * 2)]));
    }
}
