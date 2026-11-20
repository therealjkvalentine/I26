using I76.Formats.Shell;
using I76.Formats.Tests;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// CHOOSE YOUR VEHICLE's highlights in the modern look (HaloSprite): only the glow, where the sprite differs from the
/// background under it, is drawn; the rest of its rectangle stays see-through over the upscaled background.
/// </summary>
public class HaloSpriteTests
{
    private const int W = ShellFrame.W;

    /// <summary>A sprite of literal rows (bank 0x40), every pixel opaque but where <paramref name="hole"/> says.</summary>
    private static Sprite Make(byte[,] px, Func<int, int, bool>? hole = null)
    {
        int h = px.GetLength(0), w = px.GetLength(1);
        var head = new byte[0x18];
        BitConverter.GetBytes(0).CopyTo(head, 8);
        BitConverter.GetBytes(0).CopyTo(head, 12);
        BitConverter.GetBytes(w - 1).CopyTo(head, 16);
        BitConverter.GetBytes(h - 1).CopyTo(head, 20);
        var o = new List<byte>(head);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (hole?.Invoke(x, y) == true) { o.Add(0); o.Add(1); continue; }   // skip 1
                o.Add(3); o.Add(px[y, x]);                                          // one literal
            }
            o.Add(0);
        }
        var s = Sprite.Decode(o.ToArray());
        s.Bank = 0x40;
        return s;
    }

    private static byte[] Palette()
    {
        var p = new byte[768];
        for (int i = 0; i < 256; i++) { p[i * 3] = (byte)i; p[i * 3 + 1] = (byte)(255 - i); p[i * 3 + 2] = 77; }
        return p;
    }

    /// <summary>A background of index 10, a sprite copied from it at (100, 50) with a 4x4 glow of index 200 in its middle.</summary>
    private static (Sprite S, byte[] Bg) Glowing()
    {
        var bg = new byte[W * ShellFrame.H];
        Array.Fill(bg, (byte)10);
        var px = new byte[12, 12];
        for (int y = 0; y < 12; y++)
            for (int x = 0; x < 12; x++)
                px[y, x] = x is >= 4 and < 8 && y is >= 4 and < 8 ? (byte)200 : (byte)10;
        return (Make(px), bg);
    }

    [Fact]
    public void ChangedIsWhereTheSpriteDiffersFromTheBackground()
    {
        var (s, bg) = Glowing();
        var m = HaloSprite.Changed(s, bg, 100, 50);
        Assert.Equal(16, m.Count(c => c));
        Assert.True(m[5 * 12 + 5]);
        Assert.False(m[0]);
    }

    [Fact]
    public void ATransparentPixelChangesNothing()
    {
        var px = new byte[3, 3];
        px[1, 1] = 99;
        var s = Make(px, (x, y) => x == 1 && y == 1);
        Assert.DoesNotContain(true, HaloSprite.Changed(s, new byte[W * ShellFrame.H], 0, 0));
    }

    [Fact]
    public void ASpriteThatIsAllBackgroundDrawsNothing()
    {
        var bg = new byte[W * ShellFrame.H];
        for (int i = 0; i < bg.Length; i++) bg[i] = (byte)(i * 7);
        var px = new byte[20, 30];
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 30; x++)
                px[y, x] = bg[(40 + y) * W + 60 + x];
        var rgba = HaloSprite.Rgba(Make(px), bg, 60, 40, Palette(), 4);
        Assert.Equal(30 * 4 * 20 * 4 * 4, rgba.Length);
        for (int i = 3; i < rgba.Length; i += 4) Assert.Equal(0, rgba[i]);
    }

    [Fact]
    public void TheGlowIsOpaqueInItsOwnColourAndTheRectangleAroundItClear()
    {
        var (s, bg) = Glowing();
        var pal = Palette();
        const int k = 4, tw = 12 * k;
        var rgba = HaloSprite.Rgba(s, bg, 100, 50, pal, k);
        int At(int tx, int ty) => (ty * tw + tx) * 4;
        // the glow's middle: opaque, its own colour (no background colour mixed into it)
        int c = At(6 * k, 6 * k);
        Assert.Equal(255, rgba[c + 3]);
        Assert.Equal(new[] { pal[600], pal[601], pal[602] }, rgba[c..(c + 3)]);
        // an edge texel of the glow, partly covered: still the glow's colour
        int e = At(4 * k, 6 * k);
        Assert.InRange(rgba[e + 3], 1, 255);
        Assert.Equal(pal[600], rgba[e]);
        // the rectangle's border and corners: clear
        for (int t = 0; t < tw; t++)
        {
            Assert.Equal(0, rgba[At(t, 0) + 3]);
            Assert.Equal(0, rgba[At(t, tw - 1) + 3]);
            Assert.Equal(0, rgba[At(0, t) + 3]);
            Assert.Equal(0, rgba[At(tw - 1, t) + 3]);
        }
    }

    /// <summary>The three highlights at the places CHOOSE YOUR VEHICLE blits them: a glow round the vehicle, most of the rectangle the background's own.</summary>
    [InstallFact]
    public void TheInstallsHighlightsAreGlowsOnRecord27()
    {
        var db = ShellDatabase.Open(TestInstall.Install.FindFile("DATABASE.MW2")!);
        var bg = db.Background(27);
        var bank = db.Bank(0x40);
        foreach (var (i, x, y) in new[] { (0, 8, 195), (1, 191, 195), (2, 304, 160) })
        {
            var s = bank[i]!;
            Assert.True(HaloSprite.Applies(s));
            var frame = new byte[W * ShellFrame.H];
            for (int r = 0; r < Math.Min(ShellFrame.H, bg.Height); r++) Array.Copy(bg.Pixels, r * bg.Width, frame, r * W, Math.Min(W, bg.Width));
            int changed = HaloSprite.Changed(s, frame, x, y).Count(c => c), opaque = s.Opaque.Count(o => o);
            Assert.InRange(changed, opaque / 10, opaque / 4);
            var rgba = HaloSprite.Rgba(s, frame, x, y, bg.Palette, 4);
            int drawn = 0;
            for (int p = 3; p < rgba.Length; p += 4) if (rgba[p] > 0) drawn++;
            // about the glow's own share of the rectangle, nowhere near all of it
            Assert.InRange(drawn, rgba.Length / 4 / 12, rgba.Length / 4 / 3);
        }
    }
}
