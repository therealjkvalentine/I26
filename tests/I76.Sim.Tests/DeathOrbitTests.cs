using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The death and end-of-match orbit: 8 degrees per tick, as the original's 8 per frame.</summary>
public class DeathOrbitTests
{
    [Theory]
    [InlineData(20, 160f)]
    [InlineData(30, 240f)]
    [InlineData(60, 480f)]
    public void ItTurnsEightDegreesATick(int hz, float degreesPerSecond)
    {
        var o = new DeathOrbit();
        o.Start(0);
        float tick = 1f / hz;
        int steps = 0;
        for (int i = 0; i < 1000; i++) steps += o.Advance(1f / 144, tick);   // 144 render frames a second
        float seconds = 1000f / 144;
        Assert.InRange(steps, (int)(seconds * hz) - 1, (int)(seconds * hz) + 1);
        Assert.Equal(steps * 8f, o.Yaw * 180f / MathF.PI, 0.05f);
        Assert.InRange(o.Yaw * 180f / MathF.PI / seconds, degreesPerSecond * 0.99f, degreesPerSecond * 1.01f);
    }

    [Fact]
    public void ALongFrameCatchesUpAtMostFourTicks()
    {
        var o = new DeathOrbit();
        o.Start(1f);
        Assert.Equal(0, o.Advance(0.04f, 102f / 2048));
        Assert.Equal(4, o.Advance(2f, 102f / 2048));
        Assert.Equal(1f + 32f * MathF.PI / 180f, o.Yaw, 5);
    }
}
