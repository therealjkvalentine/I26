using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The host's Handicaps rule (docs/multiplayer.md "Handicaps"): off, the host sets each level, or the game sets them from the standings.</summary>
public enum HandicapMode : byte { Off, Manual, Auto }

/// <summary>
/// A network game's handicaps (the port's; docs/multiplayer.md "Handicaps"): with the host's rule on, the host gives a
/// player a level from −5 to +5, which scales the damage that player's rounds, their splash and flame deal
/// (<see cref="Vehicle.Handicap"/>, applied where the combat builds the packet). Nothing else changes: no speed, armour or
/// handling, so the clients' prediction is untouched. The original has no handicaps.
///
/// Auto (docs/multiplayer.md "Auto handicap"): the game sets the levels from the standings instead of the host. A
/// player's standing is their kills minus deaths against the table's average; every <see cref="AutoStep"/> net kills over the
/// average takes the player a level harder (−1 at 1.5, −2 at 3, −3 at 4.5, −4 at 6, −5 at 7.5), the same behind a level easier
/// (+1..+5), one step per tick at most, and a level is given back only once the standing has fallen <see cref="AutoBand"/> short
/// of the line it was taken at, which is the line below (a kill and a death in a row never flip a level twice, and nobody is
/// pushed past even). The standing does not fade with time: a lead stands until the field catches up (over ten bot-minutes a
/// fade of 3 or 10 minutes a net kill changed nothing measurable, and a fading standing would reward hiding and tie the rule to
/// the game's pace). Tuned on bots of mixed skill (i76 melee-sim): a step of 2 with the cap at ±3 narrowed less, a step of 1
/// moved levels twice as often for no gain, a standing of kills alone lost the order of the table.
/// </summary>
public static class Handicaps
{
    public const int Min = -5, Max = 5;

    /// <summary>The factor of each level, −5 to +5: 0 is the original's damage, exactly (±4 and ±5 were added with Auto, which needed the reach).</summary>
    private static readonly float[] Factors = [0.3f, 0.4f, 0.55f, 0.7f, 0.85f, 1f, 1.2f, 1.4f, 1.6f, 1.9f, 2.2f];

    /// <summary>The damage factor of <paramref name="level"/> (clamped to −5..+5).</summary>
    public static float Factor(int level) => Factors[System.Math.Clamp(level, Min, Max) - Min];

    /// <summary>A level as the scoreboard shows it: "+2", "-1", "0".</summary>
    public static string Label(int level) => level > 0 ? $"+{level}" : level.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Auto: net kills over (under) the table's average per level: a level is taken at 1.5, 3, 4.5, 6 and 7.5.</summary>
    public const float AutoStep = 1.5f;
    /// <summary>Auto: how far back past the line a level was taken at the standing must fall before it is given back (more than one kill, so one kill and one death never flip a level twice; a step, so it is the line below).</summary>
    public const float AutoBand = 1.5f;
    /// <summary>Auto: the farthest level the rule takes a player to, either way (the table's whole reach).</summary>
    public const int AutoCap = 5;

    /// <summary>
    /// Auto's step for one player this tick: from <paramref name="level"/> at <paramref name="standing"/> (kills minus deaths
    /// against the table's average), the level one step nearer the standing's own, or the same. Never more than one step.
    /// </summary>
    public static int AutoNext(int level, float standing)
    {
        // taken: the standing reached the next whole step ahead of (behind) the average
        if (level > -AutoCap && standing >= (-level + 1) * AutoStep) return level - 1;
        if (level < AutoCap && standing <= (-level - 1) * AutoStep) return level + 1;
        // given back: the standing fell AutoBand short of the line this level was taken at
        if (level < 0 && standing <= -level * AutoStep - AutoBand) return level + 1;
        if (level > 0 && standing >= -level * AutoStep + AutoBand) return level - 1;
        return level;
    }

    /// <summary>What everyone's feed says when Auto moves a player's level (a console line, "***").</summary>
    public static string Announce(string name, int level) =>
        $"*** {name} is now {Label(level)} (damage x{Factor(level).ToString(System.Globalization.CultureInfo.InvariantCulture)})";
}
