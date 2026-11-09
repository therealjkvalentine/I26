using Godot;
using I76.Formats.Assembly;
using I76.Formats.Imaging;
using I76.Game.Ui;
using I76.Formats.Tests;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's drawn gunsight ring (<see cref="ReticleArt"/>) against the sprites it replaces: zretc_1 (32x32) and zretc_6 (128x128).
/// The design numbers were measured from the sprites' texels (ring 13.5..16 of a 16 texel half tile, the outer edge the tile's; four ticks,
/// two texels wide reaching in to 11), and the ring's shading across its width and round it from zretc_6's; here the geometry and the shading are
/// held to them, and, with an install, to the texels themselves.
/// </summary>
public class ReticleArtTests
{
    [Fact]
    public void TheRingFillsTheTileAndKeepsTheMeasuredProportions()
    {
        var s = ReticleArt.Coarse;
        Assert.Equal(1f, s.RingOut);                                 // the tile's edge: the sprite's box is the 32x32
        Assert.Equal(13.6f, s.RingIn * 16f, 0.2f);                   // the ring starts 13.5..13.75 texels out
        Assert.Equal(2f, s.TickHalf * 2f * 16f, 0.01f);              // the ticks are two texels wide
        Assert.Equal(11f, s.TickIn * 16f, 0.01f);                    // and reach in to 11 texels from the centre
        var f = ReticleArt.Fine;
        Assert.Equal(54.5f, f.RingIn * 64f, 0.3f);
        Assert.Equal(3f, f.TickHalf * 2f * 64f, 0.01f);              // the binoculars' ticks are thinner, three texels of 128
        Assert.Equal(47f, f.TickIn * 64f, 0.01f);
    }

    [Fact]
    public void TheShapeCoversTheRingTheTicksAndNothingElse()
    {
        var s = ReticleArt.Coarse;
        Assert.True(s.Covers(0.92f, 0)); Assert.True(s.Covers(0, -0.92f)); Assert.True(s.Covers(-0.65f * 1.1f, 0.01f));   // the ring and a tick
        Assert.False(s.Covers(0, 0)); Assert.False(s.Covers(0.5f, 0.5f)); Assert.False(s.Covers(0.7f, 0.3f));           // the hollow, off the ticks
        Assert.False(s.Covers(1.05f, 0));                                                                              // outside the tile
    }

    [Fact]
    public void TheRingIsShadedAsARaisedTubeAndTheTicksAreBevelled()
    {
        foreach (var s in new[] { ReticleArt.Coarse, ReticleArt.Fine })
        {
            Assert.Equal(48, s.Steps);
            Assert.Equal(7, s.Across);
            // at the four compass points a lit rim at each edge and a darker core between them (the sprite's tube), not a flat band
            foreach (float deg in new[] { 0f, 90f, 180f, 270f })
            {
                float a = Mathf.DegToRad(deg);
                float core = s.GreyAt(a, 0.5f), inner = s.GreyAt(a, 0.05f);
                Assert.True(inner > core + 0.1f, $"{deg}: the inner rim {inner:F2} over the core {core:F2}");
                Assert.True(s.GreyAt(a, 0.9f) > core + 0.1f, $"{deg}: the outer rim over the core {core:F2}");
            }
            // the glints at the lower left and upper right: bright across the middle of the band
            Assert.True(s.GreyAt(Mathf.DegToRad(135), 0.5f) > 0.85f);
            Assert.True(s.GreyAt(Mathf.DegToRad(315), 0.5f) > 0.85f);
            // the core is darkest on the lower right and the upper left, between the glints
            Assert.True(s.GreyAt(Mathf.DegToRad(45), 0.5f) < 0.2f);
            Assert.True(s.GreyAt(Mathf.DegToRad(225), 0.5f) < s.GreyAt(Mathf.DegToRad(135), 0.5f) - 0.5f);
            // the ticks: the upright ones light on the left, the level ones light below
            Assert.True(s.TickGreyAt(true, 0f) > s.TickGreyAt(true, 1f) + 0.1f);
            Assert.True(s.TickGreyAt(false, 1f) > s.TickGreyAt(false, 0f) + 0.1f);
            // the angle is continuous across 0 / tau, and a measured point is its value
            Assert.Equal(s.GreyAt(0.0001f, 0.5f), s.GreyAt(Mathf.Tau - 0.0001f, 0.5f), 2);
        }
        Assert.Equal(ReticleArt.Fine.GreyAt(0f, 0f), ReticleArt.Fine.GreyAt(Mathf.Tau, 0f), 4);
    }

    [Fact]
    public void TheMapPlacesTheRingWhereTheSpriteIsAtTheScale()
    {
        // the 2D dash's sprite: 32 texels at k pixels each from (x, y); the ring's centre and edge land on the sprite's centre and edge
        var pts = new List<Vector2>();
        Vector2 Map(Vector2 u) { var p = new Vector2(100, 40) + (u + Vector2.One) * 16f * 3f; pts.Add(p); return p; }
        Assert.Equal(new Vector2(148, 88), Map(Vector2.Zero));
        Assert.Equal(new Vector2(196, 88), Map(Vector2.Right));
        Assert.Equal(new Vector2(100, 88), Map(Vector2.Left));
        Assert.Equal(new Vector2(148, 136), Map(Vector2.Down));
    }

    [Fact]
    public void ThePlaneFromThreeVerticesIsTheTextureMapping()
    {
        // a quad 2 wide and 1 high in x/y at z = -3, uv (0,0) at its lower left corner
        var plane = Godot_PlaneFrom();
        Assert.NotNull(plane);
        var pl = plane!.Value;
        var centre = pl.Origin + pl.U * 0.5f + pl.V * 0.5f;
        Assert.Equal(new Vector3(0, 0.5f, -3), centre);
        Assert.Equal(2f, pl.U.Length());
        Assert.Equal(1f, pl.V.Length());

        static (Vector3 Origin, Vector3 U, Vector3 V)? Godot_PlaneFrom()
            => CockpitReticle.PlaneFrom(new(-1, 0, -3), new(0, 0), new(1, 0, -3), new(1, 0), new(-1, 1, -3), new(0, 1));
    }

    [InstallFact]
    public void TheDesignMatchesTheSpritesTexels()
    {
        var res = new TextureResolver(TestInstall.Vfs);
        foreach (var (name, shape, min) in new[] { ("zretc_1.map", ReticleArt.Coarse, 0.9), ("zretc_6.map", ReticleArt.Fine, 0.88) })
        {
            var t = res.Load(name)!;
            Assert.NotNull(t);
            var rgba = TextureReader.ToRgba(t, new Palette(TestInstall.Vfs.TryRead("p01.act")!));
            int w = t.Width;
            Assert.Equal(t.Height, w);
            int inter = 0, union = 0, minX = w, maxX = -1, minY = w, maxY = -1;
            for (int y = 0; y < w; y++)
                for (int x = 0; x < w; x++)
                {
                    bool sprite = rgba[(y * w + x) * 4 + 3] != 0;
                    if (sprite) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                    bool mine = shape.Covers((x + 0.5f) / (w / 2f) - 1f, (y + 0.5f) / (w / 2f) - 1f);
                    if (sprite && mine) inter++;
                    if (sprite || mine) union++;
                }
            Assert.True(minX <= 1 && minY <= 1 && maxX >= w - 2 && maxY >= w - 2, $"{name}: the sprite's box is the whole tile");
            Assert.True(inter / (double)union >= min, $"{name}: {inter / (double)union:F3} of the sprite's texels");
            // the shading across the ring's width: in each eighth of the circle, the sprite's mean grey over the inner, middle and outer thirds of
            // the band (texels clear of its keyed edge and of the ticks) against the drawn ring's at the same texels; and texel by texel
            var sum = new double[8, 3, 2]; var cnt = new int[8, 3];
            double err = 0, errFlat = 0; int n = 0;
            float R = w / 2f;
            for (int y = 0; y < w; y++)
                for (int x = 0; x < w; x++)
                {
                    float px = (x + 0.5f) / R - 1f, py = (y + 0.5f) / R - 1f, r = MathF.Sqrt(px * px + py * py);
                    if (rgba[(y * w + x) * 4 + 3] == 0 || r < shape.RingIn + 0.25f / R || r > 1f - 0.75f / R) continue;
                    if ((MathF.Abs(px) <= shape.TickHalf + 1f / R || MathF.Abs(py) <= shape.TickHalf + 1f / R) && r < shape.RingIn + 0.5f / R) continue;
                    float a = MathF.Atan2(py, px); if (a < 0) a += Mathf.Tau;
                    float across = (r - shape.RingIn) / (shape.RingOut - shape.RingIn);
                    double sprite = rgba[(y * w + x) * 4 + 1] / 255.0, drawn = shape.GreyAt(a, across), flat = 0;
                    for (int k = 0; k < 7; k++) flat += shape.GreyAt(a, k / 6f) / 7;
                    err += Math.Abs(sprite - drawn); errFlat += Math.Abs(sprite - flat); n++;
                    int sec = (int)((a + Mathf.Pi / 8) / (Mathf.Pi / 4)) % 8, third = Math.Min((int)(across * 3), 2);
                    sum[sec, third, 0] += sprite; sum[sec, third, 1] += drawn; cnt[sec, third]++;
                }
            Assert.True(err / n < errFlat / n * (name == "zretc_6.map" ? 0.4 : 0.8), $"{name}: off by {err / n:F3} a texel, a flat band by {errFlat / n:F3}");
            if (name != "zretc_6.map") continue;
            Assert.True(err / n < 0.06, $"{name}: off by {err / n:F3} a texel");
            for (int sec = 0; sec < 8; sec++)
                for (int third = 0; third < 3; third++)
                    Assert.True(Math.Abs(sum[sec, third, 0] - sum[sec, third, 1]) / cnt[sec, third] < 0.04,
                        $"{name}: at {sec * 45} degrees, third {third}: the sprite {sum[sec, third, 0] / cnt[sec, third]:F3}, drawn {sum[sec, third, 1] / cnt[sec, third]:F3}");
        }
    }
}
