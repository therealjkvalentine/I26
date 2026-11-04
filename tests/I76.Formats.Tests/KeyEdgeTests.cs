using I76.Formats.Assembly;
using I76.Formats.Imaging;
using I76.Formats.Vfs;
using Xunit;
using Xunit.Abstractions;

namespace I76.Formats.Tests;

/// <summary>The modern look's smooth keyed edges (KeyEdge): staircases straightened, curves smoothed, corners kept.</summary>
public class KeyEdgeTests(ITestOutputHelper output)
{
    private static bool[] Mask(int w, int h, Func<float, float, bool> inside)
    {
        var m = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) m[y * w + x] = inside(x + 0.5f, y + 0.5f);
        return m;
    }

    /// <summary>Where row Y of a field crosses 128, in pixels (linear between texels), the first crossing from the left.</summary>
    private static float Crossing(byte[] f, int fw, int scale, int Y)
    {
        for (int X = 1; X < fw; X++)
        {
            int a = f[Y * fw + X - 1], b = f[Y * fw + X];
            if ((a < 128) != (b < 128)) return (X - 0.5f + (127.5f - a) / (b - a)) / scale;
        }
        return float.NaN;
    }

    [Fact]
    public void AStaircaseOfAnySlopeComesOutStraight()
    {
        // pixels whose centre is left of x = 6 + 0.37 y: steps of two and three rows, unevenly
        const int w = 40, h = 48, s = 4;
        var f = KeyEdge.Field(Mask(w, h, (x, y) => x > 6 + 0.37f * y), w, h, s);
        float worst = 0;
        for (int Y = 6 * s; Y < 42 * s; Y++)
        {
            float y = (Y + 0.5f) / s;
            worst = MathF.Max(worst, MathF.Abs(Crossing(f, w * s, s, Y) - (6 + 0.37f * y)));
        }
        Assert.True(worst < 0.2f, $"off the line by {worst} px");
    }

    [Fact]
    public void ACircleComesOutRound()
    {
        // big enough to read as a circle: at 11 pixels or so the art's staircase is an octagon's (runs of 8, then 45 degrees)
        const int w = 48, h = 48;
        const float cx = 24, cy = 24, r = 17.3f;
        var outlines = KeyEdge.Outlines(Mask(w, h, (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) < r * r), w, h);
        var o = Assert.Single(outlines);
        float lo = float.MaxValue, hi = 0, worstTurn = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float d = MathF.Sqrt((o[i].X - cx) * (o[i].X - cx) + (o[i].Y - cy) * (o[i].Y - cy));
            lo = MathF.Min(lo, d); hi = MathF.Max(hi, d);
            var a = o[(i + o.Length - 1) % o.Length]; var b = o[i]; var c = o[(i + 1) % o.Length];
            float t0 = MathF.Atan2(b.Y - a.Y, b.X - a.X), t1 = MathF.Atan2(c.Y - b.Y, c.X - b.X), turn = MathF.Abs(MathF.IEEERemainder(t1 - t0, 2 * MathF.PI));
            worstTurn = MathF.Max(worstTurn, turn);
        }
        output.WriteLine($"radius {lo:F3}..{hi:F3}, sharpest turn {worstTurn * 180 / MathF.PI:F1} deg over {o.Length} points");
        // the pixel staircase runs from about r - 0.7 to r + 0.7; no corner on a circle
        Assert.InRange(lo, r - 0.35f, r + 0.35f);
        Assert.InRange(hi, r - 0.35f, r + 0.35f);
        Assert.True(worstTurn < 25 * MathF.PI / 180, $"a {worstTurn * 180 / MathF.PI} degree turn");
    }

    [Fact]
    public void ANotchKeepsItsSharpCornersAndStraightSides()
    {
        // opaque but for a 10 x 12 notch cut down from the top edge: two concave corners at (10, 12) and (20, 12)
        const int w = 32, h = 32, s = 4;
        var m = Mask(w, h, (x, y) => !(x > 10 && x < 20 && y < 12));
        var o = Assert.Single(KeyEdge.Outlines(m, w, h).Where(p => p.Any(q => q.X > 0 && q.X < w && q.Y > 0 && q.Y < h)));
        foreach (var (x, y) in new[] { (10f, 12f), (20f, 12f) })
            Assert.Contains(o, q => MathF.Abs(q.X - x) < 1e-4f && MathF.Abs(q.Y - y) < 1e-4f);
        var f = KeyEdge.Field(m, w, h, s);
        // the notch's sides stay on the pixel boundary right up to the corner
        for (int Y = 2 * s; Y < 12 * s; Y++) Assert.InRange(Crossing(f, w * s, s, Y), 9.97f, 10.03f);
        // just inside the corner is clear, just outside opaque
        Assert.True(f[(int)(11.75f * s) * w * s + (int)(10.25f * s)] < 128);
        Assert.True(f[(int)(12.25f * s) * w * s + (int)(10.25f * s)] > 128);
        Assert.True(f[(int)(11.75f * s) * w * s + (int)(9.75f * s)] > 128);
    }

    [Fact]
    public void AOnePixelLineStaysWholeAndASinglePixelKeepsItsSquare()
    {
        const int w = 32, h = 16, s = 4;
        // a one-pixel diagonal (pixels touching corner to corner) and a lone pixel
        var m = new bool[w * h];
        for (int i = 0; i < 12; i++) m[(2 + i) * w + 2 + i] = true;
        m[8 * w + 24] = true;
        var f = KeyEdge.Field(m, w, h, s);
        for (int i = 1; i < 11; i++) Assert.True(f[((2 + i) * s + s / 2) * w * s + (2 + i) * s + s / 2] > 128, $"pixel {i} of the line lost");
        var lone = Assert.Single(KeyEdge.Outlines(m, w, h), p => p.Length == 4);
        Assert.Contains((24f, 8f), lone);
        Assert.Contains((25f, 9f), lone);
    }

    [Fact]
    public void AStraightEdgeStaysOnThePixelBoundaryAndTheFieldIsSigned()
    {
        const int w = 24, h = 8, s = 4;
        var f = KeyEdge.Field(Mask(w, h, (x, _) => x > 8), w, h, s);
        for (int Y = 0; Y < h * s; Y++) Assert.InRange(Crossing(f, w * s, s, Y), 7.99f, 8.01f);
        Assert.Equal(0, f[0]);
        Assert.Equal(255, f[w * s - 1]);
    }

    /// <summary>What the edges cost to make: every keyed texture of the install at two texels a pixel.</summary>
    [InstallFact]
    public void TheInstallsKeyedTexturesTakeLittleTimeToEdge()
    {
        var vfs = TestInstall.Vfs;
        var res = new TextureResolver(vfs);
        var pal = new Palette(vfs.TryRead("t01.act")!);
        var masks = new List<(bool[] Mask, int W, int H)>();
        foreach (var name in vfs.Names.Where(n => n.EndsWith(".map", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".vqm", StringComparison.OrdinalIgnoreCase)))
        {
            if (res.Load(name) is not { } t || t.Width > 256 || t.Height > 256) continue;
            var m = KeyEdge.Opaque(TextureReader.ToRgba(t, pal), t.Width, t.Height);
            if (m.Contains(false) && m.Contains(true)) masks.Add((m, t.Width, t.Height));
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long texels = 0;
        foreach (var (m, w, h) in masks) texels += KeyEdge.Field(m, w, h, 2).Length;
        double ms = sw.Elapsed.TotalMilliseconds;
        output.WriteLine($"{masks.Count} keyed textures, {ms:F0} ms ({ms / Math.Max(1, masks.Count):F2} ms each, one thread), {texels / 1024} KiB of field");
        Assert.True(masks.Count > 100);
        Assert.True(ms / masks.Count < 20, $"{ms / masks.Count} ms a texture");
    }
}
