using Godot;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The rounder rear mirror's shape (MirrorRound): a tapered outline fitted from a mask, and its core shrunk by the radius. Synthetic masks.</summary>
public class MirrorRoundTests
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
}
