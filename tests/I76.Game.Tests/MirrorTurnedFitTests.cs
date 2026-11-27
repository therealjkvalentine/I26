using Godot;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The turned door-mirror glass kept within the art-placed housing's height as the driver sees it (DoorFace.FitTurned): the ring's
/// top no higher than the housing's top, its bottom no lower than its bottom, shifted first and scaled only when taller, its shape kept.
/// </summary>
public class MirrorTurnedFitTests
{
    private const int Share = 30;
    private const float Margin = 0.004f, Tuck = 0.05f;
    private static readonly float Over = 2 * MirrorRim.OuterShare, RingW = MirrorRim.RingWidth(Over);

    /// <summary>A six-sided glass (the Piranha's left as the eye sees it, roughly), picture units, height <paramref name="h"/> about (cx, cy).</summary>
    private static List<Vector2> Hexagon(float cx, float cy, float w, float h) =>
        GlassRound.Hull([new(cx - w / 2, cy), new(cx - w / 4, cy + h / 2), new(cx + w / 4, cy + h / 2), new(cx + w / 2, cy), new(cx + w / 4, cy - h / 2), new(cx - w / 4, cy - h / 2)]);

    private static List<Vector2> Box(float x0, float y0, float x1, float y1) => [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];

    [Fact]
    public void AGlassTallerThanTheHousingIsScaledToItsHeight()
    {
        var hull = Hexagon(0.7f, 0.5f, 1.4f, 1f);
        var housing = Box(0.1f, 0.05f, 1.3f, 0.93f);   // the ring stands past it above and below
        var up = new Vector2(0, 1);
        var before = DoorFace.TurnedRing(hull, hull, up, Share, Over, Margin, RingW);
        Assert.True(before.Hi > 0.93f && before.Lo < 0.05f);
        var fit = DoorFace.FitTurned(hull, hull, housing, [], up, Share, Over, Margin, RingW, Tuck);
        var after = DoorFace.TurnedRing(fit.Hull, fit.Cover, up, Share, Over, Margin, RingW);
        Assert.InRange(after.Hi, 0.93f - Margin - 1e-3f, 0.93f - Margin + 1e-4f);
        Assert.InRange(after.Lo, 0.05f + Margin - 1e-4f, 0.05f + Margin + 1e-3f);
        Assert.InRange(fit.Scale, 0.75f, 0.99f);
        Assert.True(fit.Top > 0 && fit.Bottom > 0);
        // the shape kept: the same proportions, only smaller
        float W(List<Vector2> p) => p.Max(q => q.X) - p.Min(q => q.X);
        float H(List<Vector2> p) => p.Max(q => q.Y) - p.Min(q => q.Y);
        Assert.Equal(W(hull) / H(hull), W(fit.Hull) / H(fit.Hull), 3);
    }

    [Fact]
    public void AGlassThatFitsIsShiftedNotScaled()
    {
        var hull = Hexagon(0.7f, 0.5f, 1.4f, 0.8f);
        var up = new Vector2(0, 1);
        var ring = DoorFace.TurnedRing(hull, hull, up, Share, Over, Margin, RingW);
        // a housing as tall as the ring and a little more, lower down: the glass moves down, its size kept
        var housing = Box(0.1f, ring.Lo - 0.08f, 1.3f, ring.Hi - 0.06f);
        var fit = DoorFace.FitTurned(hull, hull, housing, [], up, Share, Over, Margin, RingW, Tuck);
        Assert.Equal(1f, fit.Scale);
        Assert.InRange(fit.Shift, -0.07f, -0.05f);
        var after = DoorFace.TurnedRing(fit.Hull, fit.Cover, up, Share, Over, Margin, RingW);
        Assert.True(after.Hi <= ring.Hi - 0.06f - Margin + 1e-4f && after.Lo >= ring.Lo - 0.08f + Margin - 1e-4f);

        // inside already: nothing changes
        var roomy = Box(0, ring.Lo - 0.1f, 1.4f, ring.Hi + 0.1f);
        var same = DoorFace.FitTurned(hull, hull, roomy, [], up, Share, Over, Margin, RingW, Tuck);
        Assert.Equal((1f, 0f), (same.Scale, same.Shift));
        Assert.Equal(hull, same.Hull);
    }

    [Fact]
    public void ACornerLeftShowingIsCoveredAtTheSideNotTheTop()
    {
        var hull = Hexagon(0.7f, 0.5f, 1.4f, 1f);
        var housing = Box(0.1f, 0.05f, 1.3f, 0.93f);
        var up = new Vector2(0, 1);
        var first = DoorFace.FitTurned(hull, hull, housing, [], up, Share, Over, Margin, RingW, Tuck);
        var ring = DoorFace.TurnedRing(first.Hull, first.Cover, up, Share, Over, Margin, RingW);
        // a corner of the housing's end just past the ring's upper right
        var mid = ring.Core.Aggregate(Vector2.Zero, (a, b) => a + b) / ring.Core.Count;
        var dir = new Vector2(0.94f, 0.34f).Normalized();
        float t = 0;
        while (GlassRound.Distance(ring.Core, mid + dir * t) - ring.Reach < 0.015f) t += 0.001f;
        var corner = mid + dir * t;
        var fit = DoorFace.FitTurned(hull, hull, [.. housing, corner], [corner], up, Share, Over, Margin, RingW, Tuck);
        var after = DoorFace.TurnedRing(fit.Hull, fit.Cover, up, Share, Over, Margin, RingW);
        Assert.True(after.Hi <= 0.93f - Margin + 1e-4f, $"top {after.Hi}");
        Assert.True(GlassRound.Distance(after.Core, corner) - after.Reach < 0.012f, $"the corner taken under the ring (or within a hair of it): {GlassRound.Distance(after.Core, corner) - after.Reach} scale {fit.Scale} shift {fit.Shift} first {first.Scale} reach {after.Reach} over {Over} ringw {RingW} hi {after.Hi} corner {corner}");
    }
}
