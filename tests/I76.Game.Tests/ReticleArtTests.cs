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
/// two texels wide reaching in to 11); here the geometry is held to them, and, with an install, to the texels themselves.
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
    public void TheRingsGreyIsTheSpritesMeanAndWrapsRoundTheCircle()
    {
        // the measured ring greys: 96 of 255 over the 32x32's ring, 113 over the 128x128's; light where the sprite catches the light
        Assert.Equal(96.0, ReticleArt.Coarse.Shade.Average(b => (double)b), 0.6);
        Assert.Equal(113.0, ReticleArt.Fine.Shade.Average(b => (double)b), 1.5);
        Assert.Equal(48, ReticleArt.Coarse.Shade.Length);
        Assert.Equal(96, ReticleArt.Fine.Shade.Length);
        // the angle is continuous across 0 / tau, and a step's centre is its value
        Assert.Equal(ReticleArt.Fine.GreyAt(0.0001f), ReticleArt.Fine.GreyAt(Mathf.Tau - 0.0001f), 2);
        Assert.Equal(ReticleArt.Coarse.Shade[17] / 255f, ReticleArt.Coarse.GreyAt((17 + 0.5f) * Mathf.Tau / 48f), 3);
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
            // the ring's grey at each texel against the drawn ring's at its angle: within a fifth of the range on average (the sprite's own texel noise)
            double err = 0; int n = 0;
            for (int y = 0; y < w; y++)
                for (int x = 0; x < w; x++)
                {
                    float px = (x + 0.5f) / (w / 2f) - 1f, py = (y + 0.5f) / (w / 2f) - 1f;
                    if (rgba[(y * w + x) * 4 + 3] == 0 || MathF.Sqrt(px * px + py * py) < shape.RingIn + 0.03f) continue;
                    err += Math.Abs(rgba[(y * w + x) * 4 + 1] / 255.0 - shape.GreyAt(MathF.Atan2(py, px) is var a && a < 0 ? a + Mathf.Tau : MathF.Atan2(py, px)));
                    n++;
                }
            Assert.True(err / n < 0.2, $"{name}: ring grey off by {err / n:F3} on average");
        }
    }
}
