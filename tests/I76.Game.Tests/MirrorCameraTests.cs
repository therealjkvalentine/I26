using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The rear-view mirror shows 45 degrees across and draws nothing past 100 m, as the original's (it asks for 30 and the camera set-up clamps it).</summary>
public class MirrorCameraTests
{
    [Fact]
    public void MirrorIs45DegreesAndFarClipIs100Metres()
    {
        Assert.Equal(45f, MirrorCameraSettings.FovDegrees);
        Assert.Equal(100f, MirrorCameraSettings.FarMetres);
    }

    [Theory]
    [InlineData(30f, 45f)]
    [InlineData(45f, 45f)]
    [InlineData(90f, 90f)]
    [InlineData(170f, 135f)]
    public void RequestedFovIsClampedToTheCameraRange(float requested, float expected) =>
        Assert.Equal(expected, MirrorCameraSettings.ClampFov(requested));

    [Fact]
    public void TheRequested30DegreesBecomesTheFixedValue() =>
        Assert.Equal(MirrorCameraSettings.FovDegrees, MirrorCameraSettings.ClampFov(30f));
}
