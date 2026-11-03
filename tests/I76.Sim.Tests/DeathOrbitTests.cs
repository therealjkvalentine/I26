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

    /// <summary>[port] death_orbit=smooth: 0.35 rad a second on the render delta, whatever the tick.</summary>
    [Fact]
    public void SmoothTurnsTwentyDegreesASecond()
    {
        var o = new DeathOrbit();
        o.Start(0);
        for (int i = 0; i < 144; i++) o.AdvanceSmooth(1f / 144);
        Assert.Equal(0.35f, o.Yaw, 4);
    }

    /// <summary>
    /// Drawn between its steps: at 60 frames a second and 20 ticks the drawn yaw moves on every frame, by a third of a step,
    /// never holding still and never jumping a whole step; the steps themselves are unchanged.
    /// </summary>
    [Fact]
    public void ItIsDrawnBetweenItsSteps()
    {
        var o = new DeathOrbit();
        o.Start(0);
        float tick = 102f / 2048, last = o.Drawn, deg = MathF.PI / 180f;
        for (int i = 0; i < 300; i++)
        {
            o.Advance(1f / 60, tick);
            float move = (o.Drawn - last) / deg;
            Assert.InRange(move, 8f / 60 / tick * 0.99f, 8f / 60 / tick * 1.01f);
            Assert.InRange(o.Drawn - o.Yaw, 0f, 8f * deg);
            last = o.Drawn;
        }
    }

    [Fact]
    public void SmoothIsDrawnAsItTurns()
    {
        var o = new DeathOrbit();
        o.Start(0);
        o.AdvanceSmooth(0.5f);
        Assert.Equal(o.Yaw, o.Drawn);
    }
}
