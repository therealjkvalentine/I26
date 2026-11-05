namespace I76.Game.Boot;

/// <summary>
/// The modern look's sun shadows by Graphic Detail's Shadow Detail (docs/perf/plan.md, item 4): Low, the default, is two splits
/// to 600 m; High four splits to 1500 m, the reach the shadows had before. Engine-free, for the tests. A shadow map's cost is its
/// casters' draws more than its pixels (docs/perf/costs.md: the four splits drew ~900 of a frame's 2 600 calls), so the reach and
/// the splits are what the row trades; SSAO, which High also turned on, is the Ambient Occlusion row's now. The placements and the
/// plants were already off the far shadow map: the placements cast the soft drop shadows (DropShadows, 250 m at Low, 500 m at High)
/// and the shrubs the map only within Vegetation's 60 m (High); the terrain's casters stop at <see cref="CasterReach"/>.
/// </summary>
public static class ShadowReach
{
    public const float Low = 600f, High = 1500f;

    /// <summary>The splits and the reach (metres) for Shadow Detail's byte (0 Off, 1 Low, 2 High; Off draws none, its numbers are Low's).</summary>
    public static (int Splits, float Reach) For(int shadowDetail) => shadowDetail > 1 ? (4, High) : (2, Low);

    /// <summary>How far a terrain chunk casts: the reach and the longest shadow that can fall into it (a low sun on a mesa), twice the reach.</summary>
    public static float CasterReach(float reach) => 2f * reach;
}
