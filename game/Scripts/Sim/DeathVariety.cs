using I76.Sim.Damage;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's car death ([port] death_variety, on by default; docs/deviations.md "Car deaths"). The stock death
/// (upstream's: the x1_carx1 blast, vexplode.wav, the wreck and its fire, the kick, the effect roll's chunks and blasts) with
/// three changes only: the drawn death comes <see cref="Delay"/> s after the moment of death, its blast at
/// <see cref="SizeFactor"/> of the stock size and its sparks and chunks at <see cref="ShrapnelFactor"/> of the stock count
/// (the sim's blast and damage are unchanged); and at the moment of death a small event by cause (<see cref="PreFor"/>).
/// The 1997 look keeps the original's death; off, the modern look draws the stock one. Engine-free: the views read these.
/// </summary>
public static class DeathVariety
{
    /// <summary>[port] death_variety: off draws every death as the stock one.</summary>
    public static bool Enabled = true;

    /// <summary>The drawn death's delay after the moment of death (s): the blast, vexplode.wav, the wreck's hull and its fire.</summary>
    public const float Delay = 0.1f;
    /// <summary>The death blast's drawn size, a fraction of the stock one's.</summary>
    public const float SizeFactor = 0.5f;
    /// <summary>The death blast's sparks and the dying car's thrown chunks, a fraction of the stock count.</summary>
    public const float ShrapnelFactor = 0.5f;

    /// <summary>The small event at the moment of death, before the blast: none, the hood popping up with a puff under it, a burst at the failed wheel, a puff at the cab, a single crack.</summary>
    public enum Pre { None, Hood, Wheel, Cab, Crack }

    /// <summary>The pre-event of a cause: the engine's hood, the wheel's burst, the driver's puff, the ammunition's crack; nothing for the others.</summary>
    public static Pre PreFor(DeathCause cause) => cause switch
    {
        DeathCause.Engine => Pre.Hood,
        DeathCause.Wheel => Pre.Wheel,
        DeathCause.Driver => Pre.Cab,
        DeathCause.Weapon => Pre.Crack,
        _ => Pre.None,
    };

    /// <summary>The pre-events' numbers: the puffs' and the wheel burst's sizes (m), the hood's lift (m/s up) and turn (rad/s).</summary>
    public const float HoodPuff = 0.7f, HoodLift = 2.5f, HoodSpin = 2.5f, WheelBurst = 0.6f, CabPuff = 0.6f;

    /// <summary>Whether the modern look draws this death its own way for this car: the variety on, a car's own (not a gun emplacement's or a helicopter's).</summary>
    public static bool Owns(I76.Sim.Vehicles.Vehicle v, bool modernCar) => Enabled && modernCar && !v.IsEmplacement && !v.IsHeli;

    /// <summary>The failed wheel of a wheel kill: the wheel slot (0..5) with the fewest hit points (the lowest share of its own on a tie, then the first); −1 for none.</summary>
    public static int FailedWheel(IReadOnlyList<Part?> slots)
    {
        int best = -1;
        for (int i = 0; i < System.Math.Min(6, slots.Count); i++)
        {
            if (slots[i] is not WheelPart w) continue;
            if (best < 0 || w.Hp < slots[best]!.Hp || w.Hp == slots[best]!.Hp && w.Ratio < slots[best]!.Ratio) best = i;
        }
        return best;
    }

    /// <summary>Whether the <paramref name="n"/>th chunk (from 0) a dying car throws is drawn: <see cref="ShrapnelFactor"/> of them, spread evenly.</summary>
    public static bool KeepChunk(int n) => (int)((n + 1) * ShrapnelFactor) != (int)(n * ShrapnelFactor);

    /// <summary>The death blast's spark count at <paramref name="size"/> m: the stock count (ModernFx.Explode) and the share drawn.</summary>
    public static int Sparks(float size, float share = 1f) => (int)(System.Math.Clamp(size * 6, 12, 48) * share);
}
