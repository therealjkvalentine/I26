using System.Text;
using System.Text.Json;
using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The widescreen menu pack's manifest and composition (WideMenuPack): the layer a window takes, the 4:3 page untouched in the
/// middle, the strips beside it in layers, pads, and the fingerprint that keeps another 4:3 pack's page at 4:3. Made-up pictures only.
/// </summary>
public sealed class WideMenuPackTests
{
    private const int W = WideMenuPack.InnerWidth, H = WideMenuPack.InnerHeight, S16 = 427, S21 = 587;

    /// <summary>A made-up 4:3 page: gradients that differ with the seed.</summary>
    private static byte[] Page(int seed)
    {
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                px[i] = (byte)((x * 7 + seed) & 255); px[i + 1] = (byte)((y / 8 + seed) & 255); px[i + 2] = (byte)((x + y) & 255); px[i + 3] = 255;
            }
        return px;
    }

    /// <summary>A strip file's pixels: red = value, green 10 on the left side and 20 on the right.</summary>
    private static (byte[], int, int) Strip(int side, byte value)
    {
        var px = new byte[2 * side * H * 4];
        for (int i = 0; i < px.Length; i += 4) { px[i] = value; px[i + 1] = (byte)(i / 4 % (2 * side) < side ? 10 : 20); px[i + 3] = 255; }
        return (px, 2 * side, H);
    }

    private const string Layers = "\"layers\":{\"21x9\":{\"width\":4588,\"side\":587,\"over\":\"16x9\"},\"16x9\":{\"width\":3414,\"side\":427,\"over\":\"4x3\"}}";

    /// <summary>A manifest with record 6 and Nitro's record 2 made for <paramref name="page"/>.</summary>
    private static WideMenuPack Pack(byte[] page, bool pad = false)
    {
        var bare = WideMenuPack.Parse(Encoding.UTF8.GetBytes("{\"height\":1920," + Layers + "}"))!;
        var e = bare.Fingerprint(page, W, H);
        int[][] Side(int s) => [.. Enumerable.Range(0, e.GetLength(1)).Select(b => new[] { e[s, b, 0], e[s, b, 1], e[s, b, 2] })];
        var entry = new Dictionary<string, object> { ["edge"] = new Dictionary<string, object> { ["L"] = Side(0), ["R"] = Side(1) } };
        if (pad) entry["pad"] = true;
        else { entry["16x9"] = "16x9/shellbg6.webp"; entry["21x9"] = "21x9/shellbg6.webp"; }
        var json = "{\"height\":1920," + Layers + ",\"pages\":{\"6\":" + JsonSerializer.Serialize(entry) + "},\"nitro\":{\"2\":" + JsonSerializer.Serialize(entry)
            + "},\"fingerprint\":{\"cols\":4,\"bands\":16,\"tolerance\":8},\"pad\":{\"cols\":4}}";
        return WideMenuPack.Parse(Encoding.UTF8.GetBytes(json))!;
    }

    [Fact]
    public void TheLayersAreReadNarrowestFirstAndAWindowTakesTheNarrowestThatFillsIt()
    {
        var p = Pack(Page(1));
        Assert.Equal(["16x9", "21x9"], p.Layers.Select(l => l.Name));
        Assert.Null(p.Choose(4f / 3f));
        Assert.Null(p.Choose(1.0f));
        Assert.Equal("16x9", p.Choose(16f / 10f)!.Name);
        Assert.Equal("16x9", p.Choose(1920f / 1080f)!.Name);
        Assert.Equal("21x9", p.Choose(2560f / 1080f)!.Name);
        Assert.Equal("21x9", p.Choose(3440f / 1440f)!.Name);
        Assert.Equal("21x9", p.Choose(32f / 9f)!.Name);
        Assert.NotNull(p.Get(6));
        Assert.NotNull(p.Get(-2));
        Assert.Null(p.Get(2));
    }

    [Fact]
    public void TheFourThreePageStaysUntouchedInTheMiddleAndTheStripsGoBesideItInLayers()
    {
        var page = Page(3);
        var p = Pack(page);
        var strips = new Dictionary<string, (byte[], int, int)> { ["16x9/shellbg6.webp"] = Strip(S16, 100), ["21x9/shellbg6.webp"] = Strip(S21, 200) };
        (byte[], int, int)? Read(string f) => strips.TryGetValue(f, out var s) ? s : null;

        var w16 = p.Compose(6, page, W, H, p.Layers[0], Read)!.Value;
        Assert.Equal((3414, H), (w16.Width, w16.Height));
        for (int y = 0; y < H; y += 97)
        {
            Assert.True(page.AsSpan(y * W * 4, W * 4).SequenceEqual(w16.Rgba.AsSpan((y * 3414 + S16) * 4, W * 4)));
            Assert.Equal(100, w16.Rgba[(y * 3414 + 5) * 4]);
            Assert.Equal(10, w16.Rgba[(y * 3414 + 5) * 4 + 1]);
            Assert.Equal(20, w16.Rgba[(y * 3414 + 3414 - 5) * 4 + 1]);
        }

        var w21 = p.Compose(-2, page, W, H, p.Layers[1], Read)!.Value;
        Assert.Equal(4588, w21.Width);
        int off = S21 + S16;
        for (int y = 0; y < H; y += 131)
        {
            Assert.True(page.AsSpan(y * W * 4, W * 4).SequenceEqual(w21.Rgba.AsSpan((y * 4588 + off) * 4, W * 4)));
            Assert.Equal(200, w21.Rgba[(y * 4588 + 3) * 4]);          // the 21:9 strip outside
            Assert.Equal(100, w21.Rgba[(y * 4588 + S21 + 3) * 4]);    // the 16:9 strip inside it
            Assert.Equal(20, w21.Rgba[(y * 4588 + 4587) * 4 + 1]);
        }
    }

    [Fact]
    public void AnotherPacksPageAMissingStripOrAWrongSizeStaysFourThree()
    {
        var p = Pack(Page(3));
        var strips = new Dictionary<string, (byte[], int, int)> { ["16x9/shellbg6.webp"] = Strip(S16, 100) };
        (byte[], int, int)? Read(string f) => strips.TryGetValue(f, out var s) ? s : null;
        Assert.Null(p.Compose(6, Page(90), W, H, p.Layers[0], Read));      // other edges: another 4:3 pack's page
        Assert.Null(p.Compose(6, Page(3), W, H, p.Layers[1], Read));       // no 21:9 strip
        Assert.Null(p.Compose(7, Page(3), W, H, p.Layers[0], Read));       // no entry
        Assert.Null(p.Compose(6, new byte[640 * 480 * 4], 640, 480, p.Layers[0], Read));
        strips["16x9/shellbg6.webp"] = Strip(S16 - 1, 100);
        Assert.Null(p.Compose(6, Page(3), W, H, p.Layers[0], Read));       // a strip of the wrong width
        // a page within the tolerance (a rebuilt 4:3 pack, a level or two off) is widened
        var near = Page(3);
        for (int i = 0; i < near.Length; i += 4) near[i] = (byte)Math.Min(255, near[i] + 2);
        strips["16x9/shellbg6.webp"] = Strip(S16, 100);
        Assert.NotNull(p.Compose(6, near, W, H, p.Layers[0], Read));
    }

    [Fact]
    public void APadPageRepeatsEachRowsOuterColumns()
    {
        var page = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) { int i = (y * W + x) * 4; page[i] = (byte)(y % 251); page[i + 1] = (byte)(x < W / 2 ? 0 : 9); page[i + 3] = 255; }
        var p = Pack(page, pad: true);
        var w = p.Compose(6, page, W, H, p.Layers[1], _ => null)!.Value;
        Assert.Equal(4588, w.Width);
        foreach (int y in new[] { 0, 777, H - 1 })
        {
            Assert.Equal((byte)(y % 251), w.Rgba[y * 4588 * 4]);
            Assert.Equal(0, w.Rgba[y * 4588 * 4 + 1]);
            Assert.Equal(9, w.Rgba[(y * 4588 + 4587) * 4 + 1]);
            Assert.True(page.AsSpan(y * W * 4, W * 4).SequenceEqual(w.Rgba.AsSpan((y * 4588 + S21 + S16) * 4, W * 4)));
        }
    }

    [Fact]
    public void ABrokenManifestIsNone()
    {
        Assert.Null(WideMenuPack.Parse(null));
        Assert.Null(WideMenuPack.Parse(Encoding.UTF8.GetBytes("{\"layers\":")));
        Assert.Null(WideMenuPack.Parse(Encoding.UTF8.GetBytes("{\"pages\":{}}")));
    }
}
