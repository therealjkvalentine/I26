using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The damage smoke's draw window is the original's: a puff is drawn from 1 m of view depth out to the far limit (60 m in the
/// 1997 look), never nearer, whatever the camera's own near plane (0.25 m outside, 0.05 m in the cockpit).
/// </summary>
public class SmokeWindowTests
{
    [Theory]
    [InlineData(0.05f)]   // the cockpit camera
    [InlineData(0.25f)]   // the outside cameras
    public void APuffNearerThanOneMetreIsNotDrawn(float cameraNear)
    {
        Assert.False(SmokeWindow.Drawn(0.06f, cameraNear, 60));
        Assert.False(SmokeWindow.Drawn(0.3f, cameraNear, 60));
        Assert.False(SmokeWindow.Drawn(0.99f, cameraNear, 60));
        Assert.True(SmokeWindow.Drawn(1f, cameraNear, 60));
        Assert.True(SmokeWindow.Drawn(5f, cameraNear, 60));
    }

    [Fact]
    public void BehindTheEyeAndBeyondTheFarLimitAreNotDrawn()
    {
        Assert.False(SmokeWindow.Drawn(-2f, 0.05f, 60));
        Assert.True(SmokeWindow.Drawn(60f, 0.05f, 60));
        Assert.False(SmokeWindow.Drawn(60.5f, 0.05f, 60));
    }

    [Fact]
    public void ACameraNearPlaneBeyondOneMetreStillApplies() => Assert.False(SmokeWindow.Drawn(1.5f, 2f, 60));
}
