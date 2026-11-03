using I76.Sim.Damage;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's car death by what killed it ([port] death_variety, on by default; docs/deviations.md "Car deaths"). The
/// original's death is one explosion whatever the cause, and the 1997 look keeps it; every look here is drawn on top of it,
/// from the death's cause and seed (<see cref="VehicleKilled"/>), so every machine of a network game draws the same one.
/// Engine-free: the views read the plan and draw it.
/// </summary>
public static class DeathVariety
{
    /// <summary>[port] death_variety: off draws every death as the plain one (every thrown wheel glowing, the fire on the middle).</summary>
    public static bool Enabled = true;

    /// <summary>A blast on the car, after <see cref="Delay"/> s: where, as fractions of the body's half width, height and half length (game axes: x right, z forward), and its radius in metres.</summary>
    public readonly record struct Blast(float Delay, float X, float Y, float Z, float Size, BlastKind Kind = BlastKind.Fire);

    public enum BlastKind { Fire, Dust, Pop }

    /// <summary>
    /// One death's look. <see cref="FlamingWheels"/>: how many of the wheels the wreck throws burn as they fly (the first ones
    /// thrown; the others go charred and cold). The fire: where on the length it burns (−1 rear … 1 front), its size, and how
    /// long before it catches. <see cref="Sag"/> drops the burnt body that far (m) and tilts it (<see cref="Pitch"/>,
    /// <see cref="Roll"/>, radians). <see cref="Panels"/> body panels thrown from <see cref="PanelAt"/> along the length.
    /// </summary>
    public sealed record Plan(int FlamingWheels, float FireAt, float FireSize, float FireDelay, float Sag, float Pitch, float Roll, Blast[] Blasts, int Panels, float PanelAt)
    {
        public static readonly Plan Plain = new(int.MaxValue, 0, 1, 0, 0, 0, 0, [], 0, 0);
    }

    /// <summary>A number in [0, 1) for draw <paramref name="k"/> of a death's seed.</summary>
    public static float U(ushort seed, int k)
    {
        uint x = seed * 0x9e3779b1u + (uint)k * 0x85ebca77u;
        x ^= x >> 15; x *= 0x2c1b3c6du; x ^= x >> 12; x *= 0x297a2d39u; x ^= x >> 15;
        return (x >> 8) / 16777216f;
    }

    /// <summary>How many thrown wheels burn: 0 (10 %), 1 (65 %), 2 (20 %), 3 (4 %), 4 (1 %).</summary>
    public static int WheelCount(ushort seed)
    {
        float u = U(seed, 0);
        return u < 0.10f ? 0 : u < 0.75f ? 1 : u < 0.95f ? 2 : u < 0.99f ? 3 : 4;
    }

    /// <summary>The plan for a death; <see cref="Plan.Plain"/> when the variety is off.</summary>
    public static Plan For(DeathCause cause, ushort seed, bool enabled = true)
    {
        if (!enabled) return Plan.Plain;
        int wheels = WheelCount(seed);
        float side = U(seed, 1) < 0.5f ? -1 : 1, end = U(seed, 2) < 0.5f ? -1 : 1;
        float Jit(int k, float a) => (U(seed, k) * 2 - 1) * a;
        switch (cause)
        {
            case DeathCause.Engine:
                // a fireball out of the hood a beat after the car's blast, the hood thrown up, the fire on the engine
                return new(wheels, 0.55f, 1.2f, 0, 0, 0, 0, [new(1.1f, Jit(3, 0.2f), 0.7f, 0.85f, 3.6f)], 1, 0.7f);
            case DeathCause.Wheel:
                // the corner that failed bursts low; that wheel always goes burning
                return new(System.Math.Max(wheels, 1), 0, 1, 0, 0.15f, end * -0.08f, side * 0.1f, [new(0.9f, side * 0.95f, 0.15f, end * 0.75f, 2.6f)], 0, 0);
            case DeathCause.Brakes:
                // a shower of sparks and a small burst at a corner, which sags
                return new(System.Math.Max(wheels, 1), 0, 1, 0, 0.2f, end * -0.12f, side * 0.12f,
                    [new(0.7f, side * 0.95f, 0.1f, end * 0.75f, 1.2f, BlastKind.Pop), new(1.3f, side * 0.9f, 0.15f, end * 0.7f, 2f)], 0, 0);
            case DeathCause.Suspension:
                // the body sags onto its frame and burns low; nothing extra thrown
                return new(wheels, 0, 0.85f, 0, 0.35f, Jit(4, 0.08f), side * 0.16f, [], 0, 0);
            case DeathCause.Weapon:
                // the ammunition cooks off: pops over a few seconds around the body
                int n = 4 + (int)(U(seed, 5) * 3);
                var pops = new Blast[n];
                for (int i = 0; i < n; i++) pops[i] = new(1.2f + i * 0.7f + U(seed, 10 + i) * 0.4f, Jit(20 + i, 0.8f), 0.9f, Jit(30 + i, 0.8f), 0.8f + U(seed, 40 + i) * 0.7f, BlastKind.Pop);
                return new(wheels, 0, 1, 0, 0, 0, 0, pops, 0, 0);
            case DeathCause.Special:
                // a second, bigger blast at the rear, panels thrown, a bigger fire
                return new(wheels, -0.45f, 1.35f, 0, 0, 0, 0, [new(1.0f, Jit(6, 0.3f), 0.5f, -0.8f, 6.5f)], 2, -0.6f);
            case DeathCause.Driver:
                // the original's: no explosion, the car rolls on with the horn stuck; here a fire takes the engine bay some seconds later
                return new(0, 0.5f, 0.7f, 3f + U(seed, 7) * 4f, 0, 0, 0, [], 0, 0);
            case DeathCause.Fall:
                // the landing's burst: dust thrown out low and wide, panels off
                return new(wheels, 0, 1.1f, 0, 0.15f, Jit(8, 0.05f), Jit(9, 0.05f), [new(0.02f, 0, 0, 0, 5f, BlastKind.Dust), new(0.15f, 0, 0.3f, 0, 3f)], 3, 0);
            case DeathCause.Rollover:
                // on its roof: a burst from the underside and a wide fire
                return new(wheels, 0, 1.25f, 0, 0, 0, 0, [new(1.0f, 0, 0, Jit(11, 0.4f), 3f)], 0, 0);
            default:
                // the self-destruct key, a script's destroy, a departing player: the original's blast alone
                return new(wheels, 0, 1, 0, 0, 0, 0, [], 0, 0);
        }
    }
}
