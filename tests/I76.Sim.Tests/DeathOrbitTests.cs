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
    /// The view eases after the angle as the original's does, closing 2·dt of the gap a frame: on 20 fps frames at 20 Hz the first
    /// frame turns a tenth of the 8-degree step, the turn is 5 to 6.5 degrees a frame 0.6 s in and the full 8 after 2 s (measured in
    /// the original at 20 frames a second: about 5 a frame at first, 8 after 0.6 s), the camera behind the angle by then.
    /// </summary>
    [Fact]
    public void ItEasesInAsTheOriginalsViewDoes()
    {
        var o = new DeathOrbit();
        o.Start(0);
        float tick = SimRate.R20.TickSeconds, step = 8f * 0.05f / tick, last = 0;
        var turns = new List<float>();
        for (int i = 0; i < 40; i++)
        {
            o.Advance(0.05f, tick);
            turns.Add((o.Drawn - last) / Deg);
            last = o.Drawn;
        }
        Assert.Equal(step / 10, turns[0], 0.01f);
        Assert.InRange(turns[11], 5f, 6.5f);
        Assert.InRange(turns[39], step * 0.98f, step * 1.001f);
        Assert.All(turns.Zip(turns.Skip(1)), t => Assert.True(t.Second > t.First));     // speeding up, never a jump
        Assert.InRange((o.Yaw - o.Drawn) / Deg, step * 8.5f, step * 9.5f);              // half a second behind (9 frames' turn)
    }

    /// <summary>On a fast display the view moves on every frame, never holding still and never jumping: at most its share of a step.</summary>
    [Fact]
    public void EveryDrawnFrameMovesOn()
    {
        var o = new DeathOrbit();
        o.Start(0);
        float tick = SimRate.R20.TickSeconds, last = o.Drawn;
        for (int i = 0; i < 600; i++)
        {
            o.Advance(1f / 144, tick);
            float move = (o.Drawn - last) / Deg;
            Assert.InRange(move, 1e-4f, 8f / 144 / tick * 1.001f);
            Assert.True(o.Drawn <= o.Yaw);
            last = o.Drawn;
        }
    }

    /// <summary>A restore of the camera (a checkpoint) puts the view on the saved angle, with no easing from where it was.</summary>
    [Fact]
    public void SettingTheAnglePutsTheViewThere()
    {
        var o = new DeathOrbit();
        o.Start(0);
        for (int i = 0; i < 20; i++) o.Advance(0.05f, SimRate.R20.TickSeconds);
        o.Yaw = 3f;
        Assert.Equal(3f, o.Drawn);
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
