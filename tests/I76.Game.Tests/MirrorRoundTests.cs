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
}
