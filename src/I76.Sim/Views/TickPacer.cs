namespace I76.Sim.Views;

/// <summary>
/// The display's clock against the sim's fixed tick: how many ticks a frame runs and how far between the last two ticks it
/// draws. Plain, a tick runs when the accumulator has a whole tick in it, so a key pressed just after a tick waits up to a
/// tick (uniform 0..T, 21 ms on average at 24 Hz) before the sim sees it. With <see cref="EarlyOnInput"/> a change of the
/// driving keys runs the next tick at once, borrowed from the accumulator (it goes negative): every tick is still exactly
/// T of simulation, the tick grid and the long-run rate are unchanged, and at most one tick is outstanding (a second
/// change waits until the borrow is paid back). The frames until the next regular tick draw from the pose that was on
/// screen at the borrow to the new tick's pose (<see cref="Alpha"/> 0 to 1 over that span), so the picture stays
/// continuous and steady motion keeps its drawn speed. Presentation timing only: the sim, its inputs a tick and its
/// determinism are untouched. (The patch kit's early tick, I76_SIM_TICK_INPUT, measured 4.2 ms median key to first
/// changed drawn frame at 120 fps against T/2 without it.)
/// </summary>
public sealed class TickPacer
{
    public TickPacer(double tick) => Tick = tick;

    public double Tick { get; }
    /// <summary>Run a tick early when the keys change (the 1997 look's Early Tick, [port] early_tick).</summary>
    public bool EarlyOnInput { get; set; }
    /// <summary>Wall time owed to the sim, seconds; negative while a borrowed tick is not yet paid back.</summary>
    public double Acc { get; set; }
    /// <summary>A borrowed tick's blend is being drawn (until the next regular tick).</summary>
    public bool Blending { get; private set; }
    private double _acc0;
    /// <summary>Early ticks run so far (for the logs and tests).</summary>
    public int Borrowed { get; private set; }

    /// <summary>The frame's wall time in; the regular ticks it owes out (the caller runs them, in order).</summary>
    public int Advance(double dt)
    {
        Acc += dt;
        int n = 0;
        while (Acc >= Tick) { Acc -= Tick; n++; }
        if (n > 0) Blending = false;
        return n;
    }

    /// <summary>
    /// After <see cref="Advance"/> ran no tick this frame: whether the caller should run one now because the keys changed.
    /// When it says yes the caller first keeps each object's pose at <see cref="Alpha"/> as it stands (the drawn pose), then
    /// runs the tick, then calls <see cref="Borrow"/>.
    /// </summary>
    public bool WantsEarly(bool inputChanged) => EarlyOnInput && inputChanged && Acc >= 0;

    /// <summary>The early tick has run: take it from the accumulator and draw the blend from here.</summary>
    public void Borrow()
    {
        Acc -= Tick;
        _acc0 = Acc;
        Blending = true;
        Borrowed++;
    }

    /// <summary>
    /// How far to draw between the two poses the objects hold: Acc/T between regular ticks; during a borrowed tick's blend
    /// (Acc − Acc0)/(T − Acc0), from the pose kept at the borrow (0) to the borrowed tick's (1) at the next regular tick.
    /// </summary>
    public float Alpha => (float)System.Math.Clamp(Blending ? (Acc - _acc0) / (Tick - _acc0) : Acc / Tick, 0, 1);

    /// <summary>The keys a driver holds (the view's own bits, the cockpit and the arms, are not keys).</summary>
    public const I76.Sim.Session.CarButtons Keys = I76.Sim.Session.CarButtons.ThrottleUp | I76.Sim.Session.CarButtons.ThrottleDown
        | I76.Sim.Session.CarButtons.SteerLeft | I76.Sim.Session.CarButtons.SteerRight | I76.Sim.Session.CarButtons.EBrake
        | I76.Sim.Session.CarButtons.Fire | I76.Sim.Session.CarButtons.Hardpoint1 | I76.Sim.Session.CarButtons.Hardpoint2
        | I76.Sim.Session.CarButtons.Hardpoint3 | I76.Sim.Session.CarButtons.Hardpoint4 | I76.Sim.Session.CarButtons.Hardpoint5
        | I76.Sim.Session.CarButtons.Horn | I76.Sim.Session.CarButtons.Pistol;
    /// <summary>A stick axis counts as moved past 1 % of its range (the patch kit's threshold).</summary>
    public const int AxisStep = 328;

    /// <summary>Whether <paramref name="now"/> differs from what the last tick sampled: a key down or up, or a stick axis moved past 1 %.</summary>
    public static bool InputChanged(I76.Sim.Session.CarInput atTick, I76.Sim.Session.CarInput now) =>
        (atTick.Buttons & Keys) != (now.Buttons & Keys)
        || System.Math.Abs(atTick.Steer - now.Steer) > AxisStep || System.Math.Abs(atTick.Throttle - now.Throttle) > AxisStep;

    /// <summary>A restart (a restore, a resume): nothing owed, nothing blending.</summary>
    public void Reset(double acc = 0) { Acc = acc; Blending = false; }
}
