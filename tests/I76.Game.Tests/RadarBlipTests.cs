using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The radar's blips on a frame served upscaled: at the scope's place and size in the original's pixels, times the frame's scale.</summary>
public class RadarBlipTests
{
    [Fact]
    public void AnOriginalFrameTakesTheBlipAtTheScopesPixels()
    {
        var r = Cockpit.BlipRect(0.5f, -0.5f, 2, 256, 128, 1);
        Assert.Equal((124, 39, 2, 2), (r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));
    }

    [Fact]
    public void AnUpscaledFrameTakesItAtTheSamePlaceScaled()
    {
        // a 4x pack's frame: the same blip four times as far in and four times the size, not in the frame's top-left quarter
        var r = Cockpit.BlipRect(0.5f, -0.5f, 2, 1024, 512, 4);
        Assert.Equal((496, 156, 8, 8), (r.Position.X, r.Position.Y, r.Size.X, r.Size.Y));
    }

    [Fact]
    public void ABlipStaysInsideTheFrameAtAnyScale()
    {
        var a = Cockpit.BlipRect(5f, 5f, 3, 256, 128, 1);
        var b = Cockpit.BlipRect(5f, 5f, 3, 1024, 512, 4);
        Assert.Equal((253, 125), (a.Position.X, a.Position.Y));
        Assert.Equal((1012, 500, 12), (b.Position.X, b.Position.Y, b.Size.X));
    }
}
