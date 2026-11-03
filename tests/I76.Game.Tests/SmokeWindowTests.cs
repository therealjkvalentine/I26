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

/// <summary>
/// A burning scenery wreck's fire and smoke in the modern look reach as far as the original's sprite planes are drawn, its
/// 600 m clip, where they began at 130 m (fire) and 150 m (smoke) and the wreck came into view bare; a car's trail keeps its 150 m.
/// </summary>
public class WreckSmokeReachTests
{
    [Fact]
    public void AWrecksFireAndSmokeReachTheOriginalsClip()
    {
        Assert.Equal(I76.Formats.Imaging.DrawDistance.OriginalClip, SmokeWindow.WreckFar);
        Assert.Equal(SmokeWindow.WreckFar, StaticFire.Reach);
        Assert.True(SmokeWindow.Drawn(450f, 0.25f, SmokeWindow.WreckFar));
        Assert.False(SmokeWindow.Drawn(450f, 0.25f, SmokeWindow.ModernTrailFar));
    }
}
