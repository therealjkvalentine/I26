namespace I76.Sim.Views;

/// <summary>
/// The orbit round a dead car, and the network end of match's round the winner's. The original's only automatic orbit
/// (the end of a network match) forces the track yaw delta to 8 and its orbit view adds it in degrees once per call, a
/// frame; here once per sim tick, as the other per-frame camera rules, so it turns 160 degrees a second at 20 Hz. The
/// single-player death camera is not in our reading of the original; it takes the same rate, by analogy. It is drawn
/// between its steps (<see cref="Drawn"/>), as the cars are drawn between ticks: the original drew one frame a step, so
/// every frame it showed had turned, where held steps on a faster display turn 8 degrees on one frame in two or three.
/// </summary>
public sealed class DeathOrbit
{
    /// <summary>Degrees a step.</summary>
    public const float StepDegrees = 8f;

    /// <summary>The port's earlier orbit, [port] death_orbit=smooth: a steady 0.35 rad (20 degrees) a second, on the render delta.</summary>
    public const float SmoothRadiansPerSecond = 0.35f;

    private float _acc, _tick;

    /// <summary>The yaw, radians.</summary>
    public float Yaw;

    /// <summary>Starts the orbit at a yaw (radians), no step pending.</summary>
    public void Start(float yaw) { Yaw = yaw; _acc = 0; _tick = 0; }

    /// <summary>Advances by the ticks of <paramref name="tick"/> seconds that <paramref name="dt"/> completes (at most 4 at once); returns them.</summary>
    public int Advance(float dt, float tick)
    {
        _tick = tick;
        _acc = MathF.Min(_acc + dt, 4 * tick);
        int n = 0;
        for (; _acc >= tick; _acc -= tick) n++;
        Yaw += n * StepDegrees * (MathF.PI / 180f);
        return n;
    }

    /// <summary>Smooth's turn: <paramref name="dt"/> seconds at <see cref="SmoothRadiansPerSecond"/>, no steps.</summary>
    public void AdvanceSmooth(float dt) { Yaw += SmoothRadiansPerSecond * dt; _acc = 0; }

    /// <summary>The yaw to draw (radians): <see cref="Yaw"/> and the part of the next step the time since the last has covered.</summary>
    public float Drawn => _tick > 0 ? Yaw + _acc / _tick * StepDegrees * (MathF.PI / 180f) : Yaw;
}
