using I76.Sim.Session;
using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The death and end-of-match orbit: 8 degrees per sim tick's worth of time, as the original's 8 per frame.</summary>
public class DeathOrbitTests
{
    private const float Deg = MathF.PI / 180f;

    /// <summary>At each offered sim rate: 8 degrees times the ticks a second (160 degrees a second at 20 Hz, 222 at 28).</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(25)]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(60)]
    public void ItTurnsEightDegreesATick(int hz)
    {
        var rate = SimRate.FromLabel(hz);
        Assert.Equal(hz, rate.Label);
        var o = new DeathOrbit();
        o.Start(0);
        for (int i = 0; i < 1440; i++) o.Advance(1f / 144, rate.TickSeconds);   // 10 s of 144 fps display frames
        Assert.Equal(8f / rate.TickSeconds, o.Yaw / Deg / 10f, 0.5f);
        Assert.InRange(o.Yaw / Deg / 10f, 8f * hz * 0.98f, 8f * hz * 1.02f);
    }

    /// <summary>The same display frames at two sim rates: the turn follows the sim rate, not the display.</summary>
    [Fact]
    public void TheRateFollowsTheSimRate()
    {
        float Turned(SimRate r, int fps)
        {
            var o = new DeathOrbit();
            o.Start(0);
            for (int i = 0; i < 10 * fps; i++) o.Advance(1f / fps, r.TickSeconds);
            return o.Yaw / Deg / 10f;
        }
        Assert.Equal(160.6f, Turned(SimRate.R20, 60), 0.1f);
        Assert.Equal(222.2f, Turned(SimRate.R28, 60), 0.1f);
        Assert.Equal(Turned(SimRate.R20, 60), Turned(SimRate.R20, 165), 0.1f);   // the display rate does not matter
        Assert.Equal(2f, Turned(SimRate.R40, 60) / Turned(SimRate.R20, 60), 0.01f);
    }

    /// <summary>Continuous: every display frame turns its share, a third of a step at 60 fps and 20 Hz, never a held or whole step.</summary>
    [Fact]
    public void EveryFrameTurnsItsShare()
    {
        var o = new DeathOrbit();
        o.Start(1f);
        float tick = SimRate.R20.TickSeconds, last = o.Yaw;
        for (int i = 0; i < 300; i++)
        {
            o.Advance(1f / 60, tick);
            Assert.Equal(8f / 60 / tick, (o.Yaw - last) / Deg, 0.01f);
            last = o.Yaw;
        }
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
