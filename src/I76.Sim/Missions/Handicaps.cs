using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The host's Handicaps rule (docs/multiplayer.md "Handicaps"): off, the host sets each level, or the game sets them from the standings.</summary>
public enum HandicapMode : byte { Off, Manual, Auto }

/// <summary>
/// A network game's handicaps (the port's; docs/multiplayer.md "Handicaps"): with the host's rule on, the host gives a
/// player a level from −5 to +5, which scales the damage that player deals (rounds, their splash, flame, rams and the .45's shot,
/// <see cref="Factor"/>) and, inversely, the damage the player takes from other cars (<see cref="Taken"/>, 1/factor held to
/// ×3..×⅓): a leader dies faster as well as killing slower, a trailer the reverse. <see cref="Vehicle.Handicap"/>; dealt where the
/// combat builds the packet, taken in the damage model. No speed, armour or handling changes. The original has no handicaps.
///
/// Auto (docs/multiplayer.md "Auto handicap"): the game sets the levels from the standings instead of the host. A
/// player's standing is their kills minus deaths against the table's average; every <see cref="AutoStep"/> net kills over the
/// average takes the player a level harder (−1 at 1, −2 at 2 … −5 at 5), the same behind a level easier
/// (+1..+5), one step per tick at most, and a level is given back only once the standing has fallen <see cref="AutoBand"/> short
/// of the line it was taken at, which is the line below (each kill or death moves a level one step at most, and nobody is
/// pushed past even). The standing does not fade with time: a lead stands until the field catches up (a fading standing would
/// reward hiding and tie the rule to the game's pace). Tuned on bots of mixed skill (i76 melee-sim, docs/multiplayer.md): the
/// aim is a winner one or two kills clear of the next, still the strongest; damage dealt alone and a step of 1.5 left the leader
/// three clear, steeper bottoms (×0.1) or a ×5 cap on the damage taken narrowed it no further and lost the strongest more games.
/// </summary>
public static class Handicaps
{
    public const int Min = -5, Max = 5;

    /// <summary>The dealt factor of the levels −1..−5; +n is the inverse of −n, so a level and its mirror cancel in a duel.</summary>
    private static readonly float[] Down = [0.75f, 0.55f, 0.4f, 0.3f, 0.2f];

    /// <summary>The damage-dealt factor of <paramref name="level"/> (clamped to −5..+5): 1 at 0, the original's damage exactly.</summary>
    public static float Factor(int level)
    {
        level = System.Math.Clamp(level, Min, Max);
        return level == 0 ? 1f : level < 0 ? Down[-level - 1] : 1f / Down[level - 1];
    }

    /// <summary>The farthest the damage taken is scaled, either way: ×3 for a leader, ×⅓ for a trailer.</summary>
    public const float TakenCap = 3f;

    /// <summary>The damage-taken factor of <paramref name="level"/>: the inverse of its dealt factor, held to ×⅓..×3 (1 at 0).</summary>
    public static float Taken(int level) => level == 0 ? 1f : System.Math.Clamp(1f / Factor(level), 1f / TakenCap, TakenCap);

    /// <summary>A level as the scoreboard shows it: "+2", "-1", "0".</summary>
    public static string Label(int level) => level > 0 ? $"+{level}" : level.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Auto: net kills over (under) the table's average per level: a level is taken at 1, 2, 3, 4 and 5.</summary>
    public const float AutoStep = 1f;
    /// <summary>Auto: how far back past the line a level was taken at the standing must fall before it is given back (a step, so it is the line below).</summary>
    public const float AutoBand = 1f;
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
        $"*** {name} is now {Label(level)} (deals x{X(Factor(level))}, takes x{X(Taken(level))})";

    private static string X(float f) => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
