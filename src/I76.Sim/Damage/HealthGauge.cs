using I76.Sim.Vehicles;

namespace I76.Sim.Damage;

/// <summary>What the target box's health bar shows (the original reads the health percentage), as a 0–1 fraction.</summary>
public static class HealthGauge
{
    /// <summary>
    /// The health percentage by object type: a helicopter's (9) and an emplacement's (8) one pool, hp·(1/max); a car's
    /// VehicleDamage.GaugeFraction. A helicopter is asked first: the player's carries a VehicleDamage for its specials alone,
    /// whose zones no hit touches (the helicopter's damage), so it read whole all the way down (#38).
    /// </summary>
    public static float Fraction(Vehicle v) =>
        v.Heli is { } h ? h.HealthPercent / 100f
        : v.Emplacement is { } g ? (float)g.Hp / System.Math.Max(1, g.MaxHp)
        : v.Health?.GaugeFraction() ?? 1f;

    /// <summary>
    /// What the bar shows for <paramref name="v"/>; <paramref name="remote"/>: a network game's car other than the viewer's own.
    /// The original's health percentage of such a car is the step its owner's machine sends in two bits of the car's state:
    /// 100 above 66.67, 66.67 above 33.33, else 33.33 (<see cref="Remote"/>). Read in full, a car whose engine, suspension or
    /// brakes are scratched is below 1 % (the unscaled branch of <see cref="VehicleDamage.HealthPercent"/>, kept offline), a bar
    /// a pixel or two wide: online the bar went away once a remote car's brakes were hit.
    /// </summary>
    public static float Bar(Vehicle v, bool remote) =>
        remote && v.Heli is null && v.Emplacement is null && v.Health is not null ? Remote(Fraction(v)) : Fraction(v);

    /// <summary>The network step of a car's health fraction: 1 above 2/3, 2/3 above 1/3, else 1/3.</summary>
    public static float Remote(float fraction) =>
        fraction * 100f > 66.67f ? 1f : fraction * 100f > 33.33f ? 0.6667f : 0.3333f;
}
