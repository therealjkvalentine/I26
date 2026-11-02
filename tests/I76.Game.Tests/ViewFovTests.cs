using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's Field of View: Monitor keeps the original's 73.74 degrees of height in any window (90 across at 4:3, more in a
/// wider one), and a number is exact degrees across the window, the height following its shape.
/// </summary>
public class ViewFovTests
{
    private const float SixteenByNine = 16f / 9;

    [Fact]
    public void TheTableIsMonitorThenSixtyToOneHundredTenByFive()
    {
        Assert.Equal(ViewFov.Names.Length, ViewFov.Across.Length);
        Assert.Equal("Monitor" + I76.Game.Shell.ShellSettings.PortMark, ViewFov.Names[ViewFov.Monitor]);
        Assert.Equal(0, ViewFov.Across[ViewFov.Monitor]);
        for (int d = 60, i = 1; d <= 110; d += 5, i++)
        {
            Assert.Equal(d, ViewFov.Across[i]);
            Assert.StartsWith(d.ToString(), ViewFov.Names[i]);
        }
        Assert.Equal(12, ViewFov.Across.Length);
        // the original's 90 is the one value unmarked: every pad row marks the values the original does not have
        Assert.Equal(90, ViewFov.Across[ViewFov.Original]);
        Assert.Equal("90", ViewFov.Names[ViewFov.Original]);
    }

    [Fact]
    public void MonitorKeepsTheOriginalsHeightInAnyWindow()
    {
        Assert.Equal(73.74f, ViewFov.VerticalOf(ViewFov.Monitor, ViewFov.FourByThree), 0.01f);
        Assert.Equal(73.74f, ViewFov.VerticalOf(ViewFov.Monitor, SixteenByNine), 0.01f);
        // which is the original's 90 across at 4:3, and 106 across at 16:9
        Assert.Equal(90f, ViewFov.MonitorAcross(ViewFov.FourByThree), 0.05f);
        Assert.Equal(106.26f, ViewFov.MonitorAcross(SixteenByNine), 0.05f);
    }

    [Fact]
    public void NinetyAcrossAFourByThreeWindowIsExactlyTheOriginal() => Assert.Equal(73.74f, ViewFov.VerticalOf(ViewFov.Original, ViewFov.FourByThree), 0.01f);

    [Theory]
    [InlineData(90, 16f / 9, 58.72)]    // the original's width in a wide window: less height than the original
    [InlineData(110, 16f / 9, 77.6)]
    [InlineData(60, 4f / 3, 46.8)]
    [InlineData(110, 4f / 3, 93.9)]
    public void ADegreeChoiceIsThatWidthAcrossTheWindow(int across, float aspect, double vertical)
    {
        float v = ViewFov.Vertical(across, aspect);
        Assert.Equal(vertical, v, 0.1);
        // and back: tan(h/2) = tan(v/2) * aspect
        double h = 2 * System.Math.Atan(System.Math.Tan(v * System.Math.PI / 360) * aspect) * 180 / System.Math.PI;
        Assert.Equal(across, h, 0.01);
    }

    [Fact] public void ParseIsMonitorForAbsentZeroOrAnythingNotOffered()
    {
        Assert.Equal(ViewFov.Monitor, ViewFov.Parse(null));
        Assert.Equal(ViewFov.Monitor, ViewFov.Parse(0));
        Assert.Equal(ViewFov.Monitor, ViewFov.Parse(137));
        Assert.Equal(ViewFov.Monitor, ViewFov.Parse(73));
        Assert.Equal(ViewFov.Original, ViewFov.Parse(90));
        Assert.Equal(100, ViewFov.Across[ViewFov.Parse(100)]);
    }

    [Fact] public void ApplyChangesTheMainViewsAndClamps()
    {
        float aspect = ViewFov.Aspect;
        try
        {
            ViewFov.Aspect = SixteenByNine;
            ViewFov.Apply(ViewFov.Original);
            Assert.Equal(ViewFov.Vertical(90, SixteenByNine), ViewFov.Main, 0.001f);
            ViewFov.Apply(99);
            Assert.Equal(ViewFov.Across.Length - 1, ViewFov.Choice);
        }
        finally { ViewFov.Apply(ViewFov.Monitor); ViewFov.Aspect = aspect; }
        Assert.Equal(73.74f, ViewFov.Main);
    }

    /// <summary>
    /// The 2D dash's forward reticle sits where the gun line (level with the car, the head 13 degrees up from it at rest) projects:
    /// at the original's view 74 px under the centre, and with another Field of View where that view puts the same direction.
    /// </summary>
    [Theory]
    [InlineData(0, 4f / 3)]
    [InlineData(90, 4f / 3)]
    [InlineData(90, 16f / 9)]
    [InlineData(60, 4f / 3)]
    [InlineData(110, 4f / 3)]
    public void TheDashReticleFollowsTheFieldOfView(int across, float aspect)
    {
        float vertical = across == 0 ? ViewFov.OriginalVertical : ViewFov.Vertical(across, aspect);
        double Focal(float v) => 240 / System.Math.Tan(v * System.Math.PI / 360);
        double drop = System.Math.Tan(13 * System.Math.PI / 180);
        float y0 = (float)(240 + Focal(ViewFov.OriginalVertical) * drop);
        Assert.InRange(y0, 313f, 315f);                              // the original's sprite: centre 316 (300 + 16), the gun 2 px under the eye line
        var (x, y) = ViewFov.ScreenPoint(320, y0, vertical);
        Assert.Equal(320f, x, 3);
        Assert.Equal(240 + Focal(vertical) * drop, y, 2);
        // a wider view lifts it toward the centre: 110 across 4:3 puts it 22 px higher, 60 lower; 90 at 4:3 is the original's place
        if (across == 110) Assert.InRange(y0 - y, 21f, 24f);
        if (across == 60) Assert.True(y > y0);
        if (across == 90 && aspect == 4f / 3) Assert.Equal(y0, y, 0.5f);
    }
}
