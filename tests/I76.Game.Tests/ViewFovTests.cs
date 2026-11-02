using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Graphic Detail's Field of View: the original's 90 degrees across 4:3 is 73.74 high, and a choice scales the tangent of the half angle.</summary>
public class ViewFovTests
{
    [Fact] public void TheOriginalKeepsTheOriginalsHeight() => Assert.Equal(73.74f, ViewFov.Vertical(73.74f, 0), 0.02f);

    [Theory]
    [InlineData(1, 64.4)]    // 80 across 4:3
    [InlineData(2, 83.6)]    // 100
    [InlineData(3, 93.9)]    // 110
    public void AChoiceIsTheFieldAcrossFourByThree(int choice, double vertical)
    {
        float v = ViewFov.Vertical(73.74f, choice);
        Assert.Equal(vertical, v, 0.1);
        // across a 4:3 view: tan(h/2) = tan(v/2) * 4/3
        double across = 2 * System.Math.Atan(System.Math.Tan(v * System.Math.PI / 360) * 4 / 3) * 180 / System.Math.PI;
        Assert.Equal(ViewFov.Across[choice], across, 0.1);
    }

    [Fact] public void ParseKeepsTheOriginalForAnythingElse()
    {
        Assert.Equal(0, ViewFov.Parse(null));
        Assert.Equal(0, ViewFov.Parse(90));
        Assert.Equal(0, ViewFov.Parse(137));
        Assert.Equal(2, ViewFov.Parse(100));
    }

    [Fact] public void ApplyChangesTheMainViewsAndClamps()
    {
        try
        {
            ViewFov.Apply(2);
            Assert.Equal(ViewFov.Vertical(73.74f, 2), ViewFov.Main, 0.001f);
            ViewFov.Apply(99);
            Assert.Equal(3, ViewFov.Choice);
        }
        finally { ViewFov.Apply(0); }
        Assert.Equal(73.74f, ViewFov.Main);
    }
}
