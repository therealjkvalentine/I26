namespace I76.Sim.Views;

/// <summary>
/// The orbit round a dead car, and the network end of match's round the winner's. In the original both are one view:
/// its angle turns 8 degrees once per call, a frame, with no clock in it (the CD release, the GOG build and Nitro alike), and
/// its frame was its sim step. Here the angle turns 8 degrees per sim tick's worth of time, continuously on the display
/// frame's delta: 160 degrees a second at the default 20 Hz, 192 at 24, the original at that sim rate, smooth at any
/// display rate. [port] death_orbit_speed scales the turn (<see cref="Speed"/>), drawn only.
/// The view the original draws follows that angle as its chase view follows the track keys: each frame it closes 2·dt of
/// the gap (the whole of it on a frame of 0.475 s or more, or within 0.005 rad), so the turn eases in over the first
/// second (measured in the original at 20 frames a second: about 5 degrees a frame at first, 8 after 0.6 s) and the camera
/// runs half a second behind the angle (<see cref="Drawn"/>).
/// </summary>
public sealed class DeathOrbit
{
    /// <summary>Degrees per sim tick.</summary>
    public const float StepDegrees = 8f;

    private const float StepRadians = StepDegrees * (MathF.PI / 180f);

    /// <summary>The port's earlier orbit, [port] death_orbit=smooth: a steady 0.35 rad (20 degrees) a second, on the render delta.</summary>
    public const float SmoothRadiansPerSecond = 0.35f;

    /// <summary>[port] death_orbit_speed as a factor of the turn (1: the rate above; 0.25 by default), drawn only.</summary>
    public float Speed = 1f;

    private float _yaw, _shown;

    /// <summary>The orbit's angle, radians (setting it, as a camera restore does, puts the view there too).</summary>
    public float Yaw { get => _yaw; set { _yaw = value; _shown = value; } }

    /// <summary>The turn rate, radians a second, at a sim tick of <paramref name="tick"/> seconds (before <see cref="Speed"/>).</summary>
    public static float RadiansPerSecond(float tick) => StepRadians / tick;

    /// <summary>Starts the orbit at a yaw (radians), the view on it.</summary>
    public void Start(float yaw) => Yaw = yaw;

    /// <summary>Turns by <paramref name="dt"/> seconds at the rate of a sim tick of <paramref name="tick"/> seconds, times <see cref="Speed"/>; the view eases after it.</summary>
    public void Advance(float dt, float tick)
    {
        _yaw += RadiansPerSecond(tick) * Speed * dt;
        float f = dt + dt, gap = _yaw - _shown;
        _shown = f >= 0 && f <= 0.95f && MathF.Abs(gap) >= 0.005f ? _shown + f * gap : _yaw;
    }

    /// <summary>Smooth's turn: <paramref name="dt"/> seconds at <see cref="SmoothRadiansPerSecond"/>, the view on it (the port's earlier orbit had no easing).</summary>
    public void AdvanceSmooth(float dt) { _yaw += SmoothRadiansPerSecond * Speed * dt; _shown = _yaw; }

    /// <summary>The yaw to draw (radians): the view's, easing after the angle.</summary>
    public float Drawn => _shown;
}
