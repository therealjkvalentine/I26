using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// A network game's handicaps (the port's; docs/multiplayer.md "Handicaps"): with the host's rule on, the host gives a
/// player a level from −3 to +3, which scales the damage that player's rounds, their splash and flame deal
/// (<see cref="Vehicle.Handicap"/>, applied where the combat builds the packet). Nothing else changes: no speed, armour or
/// handling, so the clients' prediction is untouched. The original has no handicaps.
/// </summary>
public static class Handicaps
{
    public const int Min = -3, Max = 3;

    /// <summary>The factor of each level, −3 to +3: 0 is the original's damage, exactly.</summary>
    private static readonly float[] Factors = [0.55f, 0.7f, 0.85f, 1f, 1.2f, 1.4f, 1.6f];

    /// <summary>The damage factor of <paramref name="level"/> (clamped to −3..+3).</summary>
    public static float Factor(int level) => Factors[System.Math.Clamp(level, Min, Max) - Min];

    /// <summary>A level as the scoreboard shows it: "+2", "-1", "0".</summary>
    public static string Label(int level) => level > 0 ? $"+{level}" : level.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
