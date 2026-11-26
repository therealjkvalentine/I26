using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>
/// Trip+'s Posse in single player (the play fork's; docs/deviations.md "Trip+"): Co-op Scaling's copies (<see cref="ScriptHost.ExtrasOf"/>)
/// without a co-op game. Each enemy ground car the script sets going, not one the mission is about (<see cref="CoopKeyCars"/>), gets
/// <see cref="Count"/> copies beside it, made and driven as Co-op Scaling's are (aimed at the one human); a key car with an escort rule
/// (<see cref="CoopEscorts"/>) gets that many escorts instead, and the cars the mission fails on losing take 1 / <see cref="CoopSetup.EscortHealth"/>
/// of every damage. Built by <see cref="MissionSetup.Build"/> from the session's Trip+ rules.
/// </summary>
public sealed class SoloCopies
{
    /// <summary>The copies per car (1..<see cref="CoopSetup.MaxExtraEnemies"/>).</summary>
    public int Count { get; init; }
    /// <summary>Builds an enemy car's copy from its .vcf (an AI car's damage state, the night's lights), not placed.</summary>
    internal Func<string, Vehicle>? BuildEnemy { get; init; }
    /// <summary>The script's paths (mission data), which a copy's place beside its original keeps off (<see cref="SpawnSpace.Beside"/>).</summary>
    internal IReadOnlyList<IReadOnlyList<System.Numerics.Vector3>>? Paths { get; init; }
    /// <summary>A copy (or escort) was made: its car and .vcf; the game makes its node.</summary>
    public event Action<Vehicle, string>? Spawned;
    internal void RaiseSpawned(Vehicle v, string def) => Spawned?.Invoke(v, def);
}

public sealed partial class ScriptHost
{
    /// <summary>Trip+'s Posse in single player (<see cref="SoloCopies"/>); null off, and always null in a co-op game or a melee.</summary>
    public SoloCopies? Solo { get; internal set; }

    /// <summary>
    /// Trip+'s Sharp (the play fork's; docs/deviations.md "Trip+"): added to the tiers of every enemy car's skill (setSkill's, and the
    /// AI block's default at the mission build), held to 1..5 (<see cref="Trip.TripPlusRules.Tier"/>); 0, the original's. An enemy is a
    /// car on a team neither the humans' nor nobody's; a key car's tiers move as any other's, and nothing else of its orders changes.
    /// The script's own values stay in <see cref="Entity.SkillA"/> / <see cref="Entity.SkillB"/>; only the AI's tables read the offset.
    /// </summary>
    public int SkillOffset { get; internal set; }

    /// <summary>A car Sharp makes better: an AI car (a helicopter and a gun emplacement too) on a team neither the humans' nor nobody's.</summary>
    private bool SharpApplies(Entity e) => SkillOffset != 0 && e.Vehicle is not null && !IsHumanEntity(e) && e.Team != 0 && Lead is { } lead && e.Team != lead.Team;

    /// <summary>The AI block's tables for setSkill a, b on <paramref name="e"/>: Sharp's offset on an enemy's tiers, else as given.</summary>
    private void ApplySkill(Entity e, int a, int b)
    {
        if (SharpApplies(e)) { a = Trip.TripPlusRules.Tier(a, SkillOffset); b = Trip.TripPlusRules.Tier(b, SkillOffset); }
        e.Ai.SetSkill(a, b);
    }

    /// <summary>
    /// Sharp at the mission build: every enemy's AI block from its default tiers (5, 5: <see cref="Ai.AiBlock"/>) to the offset's,
    /// for the cars the script never gives a setSkill. Called once the offset is set, before the script's first frame.
    /// </summary>
    internal void ApplySkillOffsetAtBuild()
    {
        foreach (var e in Entities)
            if (SharpApplies(e)) ApplySkill(e, 5, 5);
    }
}
