using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The live textures' mip chain: each level a 2x2 box of the one above, made again only under the rectangles that changed.</summary>
public class LiveTextureMipTests
{
    private static byte[] Picture(int w, int h, int seed)
    {
        var r = new Random(seed);
        var b = new byte[w * h * 4];
        r.NextBytes(b);
        return b;
    }

    /// <summary>The whole chain of a w x h picture made from scratch: level 1 first.</summary>
    private static byte[][] Chain(byte[] rgba, int w, int h)
    {
        int levels = LiveTexture.Levels(w, h);
        var mips = new byte[levels - 1][];
        byte[] above = rgba;
        for (int k = 1; k < levels; k++)
        {
            int lw = LiveTexture.LevelSize(w, k), lh = LiveTexture.LevelSize(h, k);
            mips[k - 1] = new byte[lw * lh * 4];
            LiveTexture.Downsample(above, LiveTexture.LevelSize(w, k - 1), LiveTexture.LevelSize(h, k - 1), mips[k - 1], 0, 0, lw, lh);
            above = mips[k - 1];
        }
        return mips;
    }

    [Fact]
    public void LevelsRunDownToOneTexel()
    {
        Assert.Equal(12, LiveTexture.Levels(2048, 1024));
        Assert.Equal(1, LiveTexture.Levels(1, 1));
        Assert.Equal(2, LiveTexture.Levels(3, 1));
        Assert.Equal(1, LiveTexture.LevelSize(1024, 11));
        Assert.Equal(1, LiveTexture.LevelSize(3, 1));
        // 4x2: 32 bytes, 2x1: 8, 1x1: 4
        Assert.Equal(44, LiveTexture.Packed(4, 2, 3));
        Assert.Equal(40, LiveTexture.Packed(4, 2, 2));
        // the chain carried stops at a sixteenth: 2048x1024 down to 128x64
        Assert.Equal(5, LiveTexture.LevelCount(2048, 1024));
        Assert.Equal(3, LiveTexture.LevelCount(4, 2));
    }

    [Fact]
    public void ALevelIsTheMeanOfItsFourTexels()
    {
        var src = new byte[2 * 2 * 4];
        byte[] a = [10, 20, 30, 255], b = [20, 40, 60, 255], c = [30, 60, 90, 255], d = [40, 80, 120, 255];
        a.CopyTo(src, 0); b.CopyTo(src, 4); c.CopyTo(src, 8); d.CopyTo(src, 12);
        var dst = new byte[4];
        LiveTexture.Downsample(src, 2, 2, dst, 0, 0, 1, 1);
        Assert.Equal(new byte[] { 25, 50, 75, 255 }, dst);
    }

    [Fact]
    public void AKeyedTexelLendsOnlyItsAlpha()
    {
        var src = new byte[2 * 2 * 4];
        byte[] painted = [100, 100, 100, 255], keyed = [0, 0, 0, 0];
        painted.CopyTo(src, 0); keyed.CopyTo(src, 4); painted.CopyTo(src, 8); keyed.CopyTo(src, 12);
        var dst = new byte[4];
        LiveTexture.Downsample(src, 2, 2, dst, 0, 0, 1, 1);
        // the colour stays the painted side's; the alpha is the coverage (half)
        Assert.Equal(new byte[] { 100, 100, 100, 128 }, dst);
        // all four keyed: their plain mean
        keyed.CopyTo(src, 0); keyed.CopyTo(src, 8); src[0] = 40; src[8] = 80;
        LiveTexture.Downsample(src, 2, 2, dst, 0, 0, 1, 1);
        Assert.Equal(new byte[] { 30, 0, 0, 0 }, dst);
    }

    [Fact]
    public void AnOddLevelReadsItsLastColumnTwice()
    {
        // 3x1 -> 1x1: the texels 0 and 1 (the third is past the level's 2x2)
        var src = new byte[3 * 4];
        src[0] = 10; src[3] = 255; src[4] = 30; src[7] = 255; src[8] = 200; src[11] = 255;
        var dst = new byte[4];
        LiveTexture.Downsample(src, 3, 1, dst, 0, 0, 1, 1);
        Assert.Equal(20, dst[0]);
    }

    [Fact]
    public void TheLevelsUnderAChangedRectangleMatchTheChainMadeWhole()
    {
        const int w = 40, h = 24;
        var pic = Picture(w, h, 1);
        var mips = Chain(pic, w, h);
        // a change in the picture: an odd-edged rectangle, as a counter's window is
        var changed = Picture(w, h, 2);
        int x0 = 7, y0 = 3, x1 = 18, y1 = 14;
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) System.Buffer.BlockCopy(changed, (y * w + x) * 4, pic, (y * w + x) * 4, 4);
        byte[] above = pic;
        for (int k = 1; k < LiveTexture.Levels(w, h); k++)
        {
            int lw = LiveTexture.LevelSize(w, k), lh = LiveTexture.LevelSize(h, k);
            (x0, y0, x1, y1) = LiveTexture.Under(x0, y0, x1, y1, lw, lh);
            LiveTexture.Downsample(above, LiveTexture.LevelSize(w, k - 1), LiveTexture.LevelSize(h, k - 1), mips[k - 1], x0, y0, x1, y1);
            above = mips[k - 1];
        }
        var whole = Chain(pic, w, h);
        for (int k = 0; k < whole.Length; k++) Assert.Equal(whole[k], mips[k]);
        // the first level's rectangle: the half-covered edge texels included
        Assert.Equal((3, 1, 9, 7), LiveTexture.Under(7, 3, 18, 14, 20, 12));
    }
}
