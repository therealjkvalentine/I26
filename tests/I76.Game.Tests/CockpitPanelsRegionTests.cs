using Godot;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The panels redrawn and uploaded by element: a stamp held to a rectangle, a panel's draws replayed in one element's rectangle,
/// the changed rectangles merged, and the difference between two pictures found for the upload.
/// </summary>
public class CockpitPanelsRegionTests
{
    private static (byte[] Src, int Sw, int Sh) RandomSheet(Random rng, int ss)
    {
        int sw = 24 * ss, sh = 20 * ss;
        var src = new byte[sw * sh * 4];
        rng.NextBytes(src);
        return (src, sw, sh);
    }

    [Fact]
    public void AClippedStampWritesTheWholeStampsTexelsInsideTheClipAndNothingOutside()
    {
        var rng = new Random(6);
        foreach (int ds in new[] { 1, 2, 4, 8 })
            for (int n = 0; n < 60; n++)
            {
                int ss = new[] { 1, 2, 4, 8 }[rng.Next(4)];
                var (src, sw, sh) = RandomSheet(rng, ss);
                int dw = 30 * ds, dh = 26 * ds;
                var start = new byte[dw * dh * 4];
                rng.NextBytes(start);
                int sx = rng.Next(0, 20), sy = rng.Next(0, 16), w = rng.Next(1, 14), h = rng.Next(1, 14), dx = rng.Next(-6, 34), dy = rng.Next(-6, 30);
                bool keyed = rng.Next(2) == 0;
                var clip = new Rect2I(rng.Next(-4, dw), rng.Next(-4, dh), rng.Next(0, dw / 2), rng.Next(0, dh / 2));
                var whole = (byte[])start.Clone();
                var held = (byte[])start.Clone();
                CockpitPanels.Blit(src, sw, sh, ss, sx, sy, w, h, whole, dw, dh, ds, dx, dy, keyed);
                CockpitPanels.Blit(src, sw, sh, ss, sx, sy, w, h, held, dw, dh, ds, dx, dy, keyed, clip);
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        int i = ((dh - 1 - y) * dw + x) * 4;
                        var want = clip.HasPoint(new Vector2I(x, y)) ? whole : start;
                        Assert.True(want.AsSpan(i, 4).SequenceEqual(held.AsSpan(i, 4)), $"ds {ds} n {n} at {x},{y}");
                    }
            }
    }

    [Fact]
    public void APanelsDrawsReplayedInOneRectangleGiveTheWholeRedrawsTexelsThere()
    {
        // a panel of overlapping keyed stamps: one stamp moves; the old and new places put back to the base image and every
        // stamp replayed there, clipped, gives the panel the whole redraw gives
        var rng = new Random(76);
        const int ds = 4;
        int dw = 40 * ds, dh = 30 * ds;
        for (int n = 0; n < 30; n++)
        {
            var (src, sw, sh) = RandomSheet(rng, 2);
            var baseImg = new byte[dw * dh * 4];
            rng.NextBytes(baseImg);
            var stamps = Enumerable.Range(0, 8).Select(_ => (Sx: rng.Next(0, 16), Sy: rng.Next(0, 12), W: rng.Next(2, 9), H: rng.Next(2, 9), Dx: rng.Next(-2, 38), Dy: rng.Next(-2, 28))).ToArray();
            void Draw(byte[] into, Rect2I? clip)
            {
                foreach (var s in stamps) CockpitPanels.Blit(src, sw, sh, 2, s.Sx, s.Sy, s.W, s.H, into, dw, dh, ds, s.Dx, s.Dy, keyed: true, clip);
            }
            var panel = (byte[])baseImg.Clone();
            Draw(panel, null);
            int k = rng.Next(stamps.Length);
            var old = new Rect2I(stamps[k].Dx, stamps[k].Dy, stamps[k].W, stamps[k].H);
            stamps[k].Dx = rng.Next(-2, 38); stamps[k].Dy = rng.Next(-2, 28);
            var moved = new Rect2I(stamps[k].Dx, stamps[k].Dy, stamps[k].W, stamps[k].H);
            var whole = (byte[])baseImg.Clone();
            Draw(whole, null);
            foreach (var r in new[] { old, moved })
            {
                var t = new Rect2I(r.Position * ds, r.Size * ds).Intersection(new Rect2I(0, 0, dw, dh));
                if (t.Size.X <= 0 || t.Size.Y <= 0) continue;
                for (int y = t.Position.Y; y < t.End.Y; y++)
                {
                    int o = ((dh - 1 - y) * dw + t.Position.X) * 4;
                    Buffer.BlockCopy(baseImg, o, panel, o, t.Size.X * 4);
                }
                Draw(panel, t);
            }
            Assert.True(whole.AsSpan().SequenceEqual(panel), $"case {n}");
        }
    }

    [Fact]
    public void RectanglesThatOverlapAreMergedAndTooManyBecomeTheirBox()
    {
        var list = new List<Rect2I>();
        CockpitPanels.AddRect(list, new Rect2I(0, 0, 4, 4));
        CockpitPanels.AddRect(list, new Rect2I(10, 10, 2, 2));
        Assert.Equal(2, list.Count);
        // touching the first: kept apart (a staircase of bands stays narrow)
        CockpitPanels.AddRect(list, new Rect2I(4, 0, 2, 2));
        Assert.Equal(3, list.Count);
        // overlapping the first: one rectangle round it, which now overlaps the touching one too
        CockpitPanels.AddRect(list, new Rect2I(3, 3, 2, 2));
        Assert.Contains(new Rect2I(0, 0, 6, 5), list);
        // one that overlaps them all merges everything
        CockpitPanels.AddRect(list, new Rect2I(1, 1, 10, 10));
        Assert.Equal(new[] { new Rect2I(0, 0, 12, 12) }, list);
        list.Clear();
        for (int i = 0; i < 9; i++) CockpitPanels.AddRect(list, new Rect2I(i * 10, 0, 2, 2), max: 8);
        Assert.Equal(new[] { new Rect2I(0, 0, 82, 2) }, list);
    }

    [Fact]
    public void TheDifferenceOfTwoPicturesIsTheirChangedRowsAndColumns()
    {
        int w = 32, h = 20;
        var a = new byte[w * h * 4];
        new Random(1).NextBytes(a);
        var b = (byte[])a.Clone();
        var none = new List<Rect2I>();
        CockpitPanels.Differ(a, b, w, h, none);
        Assert.Empty(none);
        // two texels in rows 3 and 4, one in row 15
        b[(3 * w + 5) * 4] ^= 1;
        b[(4 * w + 9) * 4 + 3] ^= 1;
        b[(15 * w + 30) * 4 + 1] ^= 1;
        var d = new List<Rect2I>();
        CockpitPanels.Differ(a, b, w, h, d);
        Assert.Equal(2, d.Count);
        Assert.Contains(new Rect2I(5, 3, 5, 2), d);
        Assert.Contains(new Rect2I(30, 15, 1, 1), d);
        // a diagonal: bands of at most four rows, each as wide as its own part of the line
        var c = (byte[])a.Clone();
        for (int y = 0; y < 16; y++) c[(y * w + 2 * y) * 4] ^= 1;
        var diag = new List<Rect2I>();
        CockpitPanels.Differ(a, c, w, h, diag, band: 4);
        Assert.Equal(new[] { new Rect2I(0, 0, 7, 4), new Rect2I(8, 4, 7, 4), new Rect2I(16, 8, 7, 4), new Rect2I(24, 12, 7, 4) }, diag);
    }

    [Theory]
    [InlineData(1, 16)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    [InlineData(256, 256)]
    [InlineData(632, 768)]
    [InlineData(1520, 1536)]
    public void AStagingTextureIsThePowerOfTwoAtOrAboveTheRectangle(int n, int side) => Assert.Equal(side, LiveTexture.Bucket(n));
}
