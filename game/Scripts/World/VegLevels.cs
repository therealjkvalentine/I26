namespace I76.Game.World;

/// <summary>
/// Graphic Detail's Vegetation, Low and High (docs/modern-vegetation.md): the density, where each kind of plant fades in
/// and out (the shrubs; the ground layer's larger plants, its forbs, weeds and rosettes; its grass tufts), where a plant
/// changes to its middle and its far mesh (per plant: tier 1 and 2), how near shrubs cast sun shadows, and where the shade
/// under the plants fades out. No engine types: the tests read it.
/// </summary>
public static class VegLevels
{
    public sealed record Level(float Density, float ShrubFade, float ShrubEnd, float ForbFade, float ForbEnd, float GrassFade, float GrassEnd,
        float ShadowRange, float ShrubMid, float ShrubFar, float GroundMid, float GroundFar, float ShadeFade, float ShadeEnd)
    {
        /// <summary>How far the ground layer's chunks are wanted: the farther of its two kinds' ends.</summary>
        public float GroundEnd => MathF.Max(ForbEnd, GrassEnd);
    }

    /// <summary>
    /// How far past the headlamps' reach (<see cref="I76.Game.Sim.LampStrength.HeadRange"/>, from the lamps) a plant may
    /// first show (metres): the chase view's camera sits this far behind the lamps at most at its usual zoom, so what fades
    /// in beyond the lamps' reach plus this does so in the dark, never in the beam.
    /// </summary>
    public const float LampMargin = 20f;

    /// <summary>The nearest a bush (a shrub, a forb, a rolling tumbleweed) may begin to fade in at any level (metres from the camera).</summary>
    public const float BushAppear = I76.Game.Sim.LampStrength.HeadRange + LampMargin;

    // Low's shrubs fade over 130-170 m (120-160 before: their first 10 m in the beam); the ground layer's larger plants
    // with them (35-50 m before, wholly in the beam); the grass tufts keep 35-50 m and grow in out of the sward as they fade in.
    public static readonly Level Low = new(0.6f, 130, 170, 130, 170, 35, 50, 0, 20, 50, 9, 22, 35, 50);
    public static readonly Level High = new(1f, 190, 250, 190, 250, 60, 85, 60, 30, 70, 12, 35, 55, 75);

    /// <summary>The levels as they were (I76_VEG_REACH=old, for before / after captures): every ground plant at the grasses' reach.</summary>
    public static readonly Level OldLow = new(0.6f, 120, 160, 35, 50, 35, 50, 0, 20, 50, 9, 22, 35, 50);
    public static readonly Level OldHigh = new(1f, 190, 250, 60, 85, 60, 85, 60, 30, 70, 12, 35, 55, 75);

    public static readonly bool Old = System.Environment.GetEnvironmentVariable("I76_VEG_REACH") == "old";

    /// <summary>The Low (1) or High (2) level in force.</summary>
    public static Level For(int level) => level >= 2 ? (Old ? OldHigh : High) : (Old ? OldLow : Low);
}
