namespace I76.Sim.Views;

/// <summary>
/// The orbit round a dead car, and the network end of match's round the winner's. In the original both are one view:
/// it turns 8 degrees once per call, a frame, with no clock in it (the CD release, the GOG build and Nitro alike), and
/// its frame was its sim step. Here it turns 8 degrees per sim tick's worth of time, continuously on the display
/// frame's delta: 160 degrees a second at the default 20 Hz, 222 at 28, the original at that sim rate, smooth at any
/// display rate. [port] death_orbit_speed scales the turn (<see cref="Speed"/>), drawn only.
/// </summary>
public sealed class DeathOrbit
{
    /// <summary>Degrees per sim tick.</summary>
    public const float StepDegrees = 8f;

    private const float StepRadians = StepDegrees * (MathF.PI / 180f);

    /// <summary>The port's earlier orbit, [port] death_orbit=smooth: a steady 0.35 rad (20 degrees) a second, on the render delta.</summary>
    public const float SmoothRadiansPerSecond = 0.35f;

    /// <summary>[port] death_orbit_speed as a factor of the turn (1: the rate above; 0.5 by default), drawn only.</summary>
    public float Speed = 1f;

    /// <summary>The yaw, radians.</summary>
    public float Yaw;

    /// <summary>The turn rate, radians a second, at a sim tick of <paramref name="tick"/> seconds (before <see cref="Speed"/>).</summary>
    public static float RadiansPerSecond(float tick) => StepRadians / tick;

    /// <summary>Starts the orbit at a yaw (radians).</summary>
    public void Start(float yaw) => Yaw = yaw;

    /// <summary>Turns by <paramref name="dt"/> seconds at the rate of a sim tick of <paramref name="tick"/> seconds, times <see cref="Speed"/>.</summary>
    public void Advance(float dt, float tick) => Yaw += RadiansPerSecond(tick) * Speed * dt;

    /// <summary>Smooth's turn: <paramref name="dt"/> seconds at <see cref="SmoothRadiansPerSecond"/>.</summary>
    public void AdvanceSmooth(float dt) => Yaw += SmoothRadiansPerSecond * Speed * dt;

    /// <summary>The yaw to draw (radians): the turn is continuous, so the yaw itself.</summary>
    public float Drawn => Yaw;
}
