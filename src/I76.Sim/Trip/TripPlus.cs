namespace I76.Sim.Trip;

/// <summary>
/// Trip+ (the play fork's New Game Plus; docs/deviations.md "Trip+"): the rules a trip mission is played by on a lap after the
/// first. Off (<see cref="IsOff"/>) is the original's trip. The rules are part of what a session is built from
/// (<see cref="Save.SessionBuild.TripPlus"/>), so a snapshot made under them restores only into a session built under the same.
/// </summary>
/// <param name="Copies">Posse: copies of every enemy ground car the mission is not about (<see cref="Missions.CoopKeyCars"/>), made beside it as Co-op Scaling makes them (up to <see cref="Missions.CoopSetup.MaxExtraEnemies"/>); 0 none.</param>
/// <param name="SkillOffset">Sharp: added to every enemy's setSkill tiers (1 best, 5 worst; the result held to 1..5), so −1 is one tier better; 0 none.</param>
/// <param name="SalvageOffset">Loot up: added to the mission number the salvage tier clamp reads; 0 none.</param>
public sealed record TripPlusRules(int Copies, int SkillOffset, int SalvageOffset)
{
    /// <summary>No rule changes anything: the original's trip.</summary>
    public bool IsOff => Copies == 0 && SkillOffset == 0 && SalvageOffset == 0;

    /// <summary>A setSkill tier under <paramref name="offset"/>: held to 1 (best) .. 5 (worst); a tier outside 1..5 is left as it is.</summary>
    public static int Tier(int tier, int offset) => tier is < 1 or > 5 || offset == 0 ? tier : System.Math.Clamp(tier + offset, 1, 5);
}
