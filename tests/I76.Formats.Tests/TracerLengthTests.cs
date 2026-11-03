using I76.Formats.Assembly;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// Testers: the 7.62 turret's tracers seemed to start near its base. In the 1997 look a round just fired was drawn as a 4 m
/// bar ending at it, reaching back through the gun and out of the far side of a turret turned to the side.
/// </summary>
public class TracerLengthTests
{
    [Fact]
    public void ARoundJustFiredReachesNoFurtherBackThanTheMuzzle()
    {
        // a 7.62 round (speed 200, a 20 Hz tick: 9.96 m a step), a quarter of a tick out of the muzzle
        float step = 200f * 102 / 2048, travelled = step * 0.25f;
        foreach (float alpha in new[] { 0f, 0.1f, 0.5f, 0.9f, 1f })
        {
            // the drawn head is (1 - alpha) of the last step short of the round; the muzzle is travelled - that behind it
            float flown = TracerLength.Flown(travelled, step, alpha);
            Assert.True(TracerLength.Bar(step, flown) <= System.MathF.Max(0.05f, travelled - step * (1 - alpha)) + 1e-5f);
            Assert.True(TracerLength.Streak(6f, flown) <= flown);
        }
        // a round well on its way keeps its full bar
        Assert.Equal(4f, TracerLength.Bar(step, TracerLength.Flown(50f, step, 0.5f)));
    }

    [Theory]
    [InlineData(200f, 102f / 2048)]   // a 7.62 round at 20 Hz: 9.96 m a step
    [InlineData(200f, 1f / 60)]       // the same at 60 Hz
    [InlineData(400f, 102f / 2048)]   // a fast round: 19.9 m a step, past the old 4 m cap
    public void The1997CannonTubeSpansTheStepAndTheMachineGunLineItsFront65Percent(float speed, float tick)
    {
        float step = speed * tick, flown = TracerLength.Flown(100f, step, 0.5f);
        Assert.Equal(step, TracerLength.Tube(step, flown), 4);
        Assert.Equal(step * 0.65f, TracerLength.Line(step, flown), 4);
    }

    [Fact]
    public void The1997CannonTubeAndMachineGunLineReachNoFurtherBackThanTheMuzzle()
    {
        float step = 200f * 102 / 2048, flown = TracerLength.Flown(step * 0.25f, step, 1f);
        Assert.Equal(flown, TracerLength.Tube(step, flown), 5);
        Assert.Equal(flown, TracerLength.Line(step, flown), 5);
    }
    [Theory]
    [InlineData(480, 1)]    // the original's screen: its one-pixel line
    [InlineData(600, 1)]
    [InlineData(719, 1)]
    [InlineData(720, 2)]    // 1.5 rounds up
    [InlineData(960, 2)]
    [InlineData(1080, 2)]
    [InlineData(1440, 3)]
    [InlineData(2160, 4)]   // 4.5, capped
    [InlineData(4320, 4)]
    public void The1997MachineGunLineIsOnePixelPer480RowsAtMostFour(int rows, int px) => Assert.Equal(px, TracerLength.PixelWidth(rows));
}
