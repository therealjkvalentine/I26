using I76.Sim.Session;
using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The display clock against the fixed tick (TickPacer): plain it waits for whole ticks; with Early Tick a key change runs the next
/// tick at once, borrowed, without changing the tick grid or the rate, and the drawn fraction stays continuous.
/// </summary>
public class TickPacerTests
{
    private const double T = 85.0 / 2048;   // 24 Hz
    private const double Frame = 1.0 / 179; // the desktop's display

    [Fact]
    public void PlainItRunsAWholeTickAtATimeAndDrawsTheRemainder()
    {
        var p = new TickPacer(T);
        Assert.Equal(0, p.Advance(T * 0.6));
        Assert.Equal(0.6f, p.Alpha, 4);
        Assert.Equal(1, p.Advance(T * 0.6));
        Assert.Equal(0.2f, p.Alpha, 4);
        Assert.False(p.WantsEarly(true));   // off unless asked for
    }

    [Fact]
    public void AKeyChangeRunsTheTickNowAndTheNextRegularTickKeepsItsPlaceOnTheGrid()
    {
        var p = new TickPacer(T) { EarlyOnInput = true };
        Assert.Equal(0, p.Advance(T * 0.3));
        Assert.True(p.WantsEarly(true));
        Assert.False(p.WantsEarly(false));
        p.Borrow();
        Assert.True(p.Blending);
        Assert.Equal(0f, p.Alpha, 4);   // drawn from the pose kept at the borrow
        Assert.False(p.WantsEarly(true));   // one outstanding: a second change waits until it is paid back
        // the tick that would have run 0.7 T from now has run; the one after comes 1.7 T from now, where it always would
        Assert.Equal(0, p.Advance(T * 1.69));
        Assert.Equal(1.69 / 1.7, p.Alpha, 3);
        Assert.Equal(1, p.Advance(T * 0.02));
        Assert.False(p.Blending);
        Assert.Equal(0.01f, p.Alpha, 3);
    }

    [Fact]
    public void ABorrowPaidBackAllowsAnotherAndTheBlendStillReachesTheNewTick()
    {
        var p = new TickPacer(T) { EarlyOnInput = true };
        p.Advance(T * 0.5); p.Borrow();   // acc -0.5 T
        p.Advance(T * 0.6);               // acc 0.1 T: paid back, still blending (no regular tick yet)
        Assert.True(p.Blending);
        Assert.True(p.WantsEarly(true));
        p.Borrow();                        // acc -0.9 T
        Assert.Equal(0f, p.Alpha, 4);
        Assert.Equal(0, p.Advance(T * 1.8));
        Assert.Equal(1.8 / 1.9, p.Alpha, 3);
        Assert.Equal(1, p.Advance(T * 0.1));
    }

    [Fact]
    public void TheRateOverAMinuteOfRandomPressesIsTheTicksAndTheDrawnFractionNeverJumpsBack()
    {
        var plain = new TickPacer(T);
        var early = new TickPacer(T) { EarlyOnInput = true };
        var rng = new System.Random(76);
        int ticksPlain = 0, ticksEarly = 0;
        double wall = 0;
        float lastAlpha = 0;
        bool lastBlend = false;
        while (wall < 60)
        {
            wall += Frame;
            ticksPlain += plain.Advance(Frame);
            int due = early.Advance(Frame);
            ticksEarly += due;
            bool change = rng.NextDouble() < 0.08;   // a key change on about one frame in twelve
            if (due == 0 && early.WantsEarly(change)) { early.Borrow(); ticksEarly++; lastBlend = true; lastAlpha = 0; continue; }
            // within one span (no tick, no borrow) the drawn fraction only moves forward
            if (due == 0 && early.Blending == lastBlend) Assert.True(early.Alpha >= lastAlpha - 1e-5f);
            lastAlpha = early.Alpha; lastBlend = early.Blending;
        }
        Assert.InRange(ticksEarly - ticksPlain, 0, 1);   // at most the one outstanding borrow ahead
        Assert.True(early.Borrowed > 200);
        Assert.InRange(ticksPlain / wall, 24.08, 24.10);
    }

    [Fact]
    public void TheKeysChangeWhenAHeldKeyGoesDownOrUpOrTheStickMovesPastOnePercent()
    {
        var a = new CarInput { Buttons = CarButtons.ThrottleUp | CarButtons.Cockpit };
        Assert.False(TickPacer.InputChanged(a, new CarInput { Buttons = CarButtons.ThrottleUp }));   // the view's bits are no keys
        Assert.True(TickPacer.InputChanged(a, new CarInput { Buttons = CarButtons.ThrottleUp | CarButtons.SteerLeft }));
        Assert.True(TickPacer.InputChanged(a, new CarInput()));
        Assert.True(TickPacer.InputChanged(a, new CarInput { Buttons = CarButtons.ThrottleUp | CarButtons.Fire }));
        var s = new CarInput { Steer = 1000 };
        Assert.False(TickPacer.InputChanged(s, new CarInput { Steer = 1000 + TickPacer.AxisStep }));
        Assert.True(TickPacer.InputChanged(s, new CarInput { Steer = 1000 + TickPacer.AxisStep + 1 }));
        Assert.True(TickPacer.InputChanged(new CarInput(), new CarInput { Throttle = -400 }));
    }
}
