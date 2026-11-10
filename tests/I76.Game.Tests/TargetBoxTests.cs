using Godot;
using I76.Game.Ui;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The target box's size rule (the original's, both looks): S = clamp(R·sx/z, 23, 150) of the 640x480 frame's pixels by the
/// collider's radius over its depth, the box 2·trunc(0.6S) across and 2·trunc(0.4S) + 16 down, scaled by the window's height
/// over 480 and nothing else, so a wide window shows the same box round the same car as a 4:3 one of its height. Measured in
/// the original (retail 1.0, 640x480): a car of R 2.1 m at 6, 12 and 25 m drew 138x108, 68x60 and 32x36. The modern look
/// had fitted its brackets to the projected collision box with no upper bound.
/// </summary>
public class TargetBoxTests
{
    [Theory]
    [InlineData(115, 138, 108)]   // measured in the original: a car 6 m ahead
    [InlineData(57, 68, 60)]      // 12 m
    [InlineData(27, 32, 36)]      // 25 m
    [InlineData(23, 26, 34)]      // the floor: every car beyond about 35-40 m
    [InlineData(150, 180, 136)]   // the ceiling
    public void TheBoxIsTheOriginalsForItsSize(int s, int width, int height)
    {
        var r = TargetBox.Rect(320, 240, s);
        Assert.Equal(width, r.Size.X);
        Assert.Equal(height, r.Size.Y);
        Assert.Equal(320 - width / 2, r.Position.X);   // centred across
    }

    [Fact]
    public void TheBoxSitsAThirdOfItsHalfHeightAboveTheCentre()
    {
        // trunc(0.4S) = 46 for S 115: the top 46 + 15 over the centre, the corners' feet 46 - 15 + 16 under it
        var r = TargetBox.Rect(320, 240, 115);
        Assert.Equal(240 - 15 - 46, r.Position.Y);
        Assert.Equal(240 - 15 + 46 + 16, r.End.Y);
    }

    [Theory]
    [InlineData(2.1f, 5.9f, 113)]
    [InlineData(2.1f, 11.9f, 56)]
    [InlineData(2.1f, 24.9f, 26)]
    [InlineData(2.1f, 40f, 23)]     // held at the floor
    [InlineData(2.1f, 400f, 23)]
    [InlineData(2.1f, 2f, 150)]     // and at the ceiling
    [InlineData(6.1f, 30f, 65)]     // a large target keeps growing farther out
    public void TheSizeIsTheRadiusOverTheDepthClamped(float radius, float depth, int s) =>
        Assert.Equal(s, TargetBox.Size(radius, depth, TargetBox.Focal(640, 480, ViewFov.OriginalVertical, false)));

    [Fact]
    public void TheOriginalsViewHasAFocalLengthOf320()
    {
        Assert.Equal(320f, TargetBox.Focal(640, 480, ViewFov.OriginalVertical, false), 0.5f);
        // the mirror's camera keeps its width: 256 across at the same angle across
        Assert.Equal(128f, TargetBox.Focal(256, 64, 90f, true), 0.01f);
    }

    /// <summary>The box in window pixels for a target at (x, z) metres off the view's axis, as the modern look draws it.</summary>
    private static Rect2 WindowBox(Vector2 window, float vfov, float radius, float right, float depth)
    {
        float k = TargetBox.Scale(window);
        float f = TargetBox.Focal(window.X / k, window.Y / k, vfov, false);
        // the projection (Godot's KeepAspect Height): window pixels from the centre = metres / depth × focal × k
        float sx = window.X / 2 + right / depth * f * k, sy = window.Y / 2;
        var b = TargetBox.Rect((int)(sx / k), (int)(sy / k), TargetBox.Size(radius, depth, f));
        return new Rect2(b.Position.X * k, b.Position.Y * k, b.Size.X * k, b.Size.Y * k);
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(12f)]
    [InlineData(25f)]
    [InlineData(100f)]
    public void FourByThreeSixteenByNineAndTwentyOneByNineDrawTheSameBoxAtOneHeight(float depth)
    {
        // Field of View Monitor: the original's height in any window, so only the window's height scales the box
        foreach (float h in new[] { 480f, 1080f, 1440f })
        {
            var four = WindowBox(new Vector2(h * 4 / 3, h), ViewFov.OriginalVertical, 2.1f, 0, depth);
            var sixteen = WindowBox(new Vector2(h * 16 / 9, h), ViewFov.OriginalVertical, 2.1f, 0, depth);
            var wide = WindowBox(new Vector2(h * 43 / 18, h), ViewFov.OriginalVertical, 2.1f, 0, depth);
            Assert.Equal(four.Size, sixteen.Size);
            Assert.Equal(four.Size, wide.Size);
            var at480 = WindowBox(new Vector2(640, 480), ViewFov.OriginalVertical, 2.1f, 0, depth);
            Assert.Equal(at480.Size * (h / 480f), four.Size);
        }
    }

    [Fact]
    public void At3440x1440TheBoxIsThreeTimesThe640x480One()
    {
        var at480 = WindowBox(new Vector2(640, 480), ViewFov.OriginalVertical, 2.1f, 0, 6f);
        var wide = WindowBox(new Vector2(3440, 1440), ViewFov.OriginalVertical, 2.1f, 0, 6f);
        Assert.Equal(at480.Size * 3, wide.Size);
        // and far out, the 26x34 floor: 78x102 of the window's pixels
        Assert.Equal(new Vector2(78, 102), WindowBox(new Vector2(3440, 1440), ViewFov.OriginalVertical, 2.1f, 0, 80f).Size);
    }

    [Fact]
    public void ANarrowerFieldOfViewMagnifiesTheBoxWithTheView()
    {
        // 90° across a 21:9 window is about 45° high: everything, the car and its box, is about 1.8 times the Monitor view's
        float v = ViewFov.Vertical(90, 3440f / 1440);
        float k = TargetBox.Scale(new Vector2(3440, 1440));
        float ratio = TargetBox.Focal(3440 / k, 480, v, false) / TargetBox.Focal(3440 / k, 480, ViewFov.OriginalVertical, false);
        Assert.InRange(ratio, 1.7f, 1.9f);
        Assert.True(TargetBox.Size(2.1f, 12, TargetBox.Focal(3440 / k, 480, v, false)) > TargetBox.Size(2.1f, 12, TargetBox.Focal(3440 / k, 480, ViewFov.OriginalVertical, false)));
    }
}
