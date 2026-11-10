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
    /// How far past the headlamps' visible reach (from the lamps, <see cref="I76.Game.Sim.HeadlampThrow.VisibleReach"/>) a bush
    /// may first be fully drawn (metres): the chase view's camera sits up to about this far behind the lamps at its usual zoom,
    /// so what fades in beyond the reach plus this does so in the dark, never in the beam.
    /// </summary>
    public const float LampMargin = 20f;

    /// <summary>The nearest a bush (a shrub, a forb, a rolling tumbleweed) may be less than fully drawn, for the lamps' visible reach (metres from the camera).</summary>
    public static float BushAppear(float lampReach) => lampReach + LampMargin;

    /// <summary>The lamps' visible reach at the [port] headlamp_throw in force.</summary>
    public static float LampReachNow => I76.Game.Sim.HeadlampThrow.VisibleReach(I76.Game.Sim.HeadlampThrow.Current);

    // The levels' own reaches, the nearest they go: Low's shrubs and the ground layer's larger plants fade over 130-170 m
    // (the shrubs 120-160 m before, the larger ground plants 35-50 m: in the beam), High's over 190-250 m; the lamps' visible
    // reach pushes them out (For: Low's to 146-186 m at the default throw); the grass tufts keep 35-50 m (60-85 m) and grow
    // in out of the sward as they fade in.
    public static readonly Level Low = new(0.6f, 130, 170, 130, 170, 35, 50, 0, 20, 50, 9, 22, 35, 50);
    public static readonly Level High = new(1f, 190, 250, 190, 250, 60, 85, 60, 30, 70, 12, 35, 55, 75);

    /// <summary>The levels as they were (I76_VEG_REACH=old, for before / after captures): every ground plant at the grasses' reach.</summary>
    public static readonly Level OldLow = new(0.6f, 120, 160, 35, 50, 35, 50, 0, 20, 50, 9, 22, 35, 50);
    public static readonly Level OldHigh = new(1f, 190, 250, 60, 85, 60, 85, 60, 30, 70, 12, 35, 55, 75);

    public static readonly bool Old = System.Environment.GetEnvironmentVariable("I76_VEG_REACH") == "old";

    /// <summary>
    /// The Low (1) or High (2) level for the lamps' visible reach: each bush kind's fade moved out, band and all, until it starts
    /// at <see cref="BushAppear"/> (no nearer than the level's own).
    /// </summary>
    public static Level For(int level, float lampReach)
    {
        if (Old) return level >= 2 ? OldHigh : OldLow;
        var l = level >= 2 ? High : Low;
        float appear = BushAppear(lampReach);
        float s = MathF.Max(0, appear - l.ShrubFade), f = MathF.Max(0, appear - l.ForbFade);
        return l with { ShrubFade = l.ShrubFade + s, ShrubEnd = l.ShrubEnd + s, ForbFade = l.ForbFade + f, ForbEnd = l.ForbEnd + f };
    }

    /// <summary>The rolling tumbleweeds' range for a level and the lamps' visible reach: theirs, or far enough that they fade in beyond the lamps.</summary>
    public static float TumbleRange(int level, float lampReach) =>
        Old ? (level >= 2 ? 180f : 150f) : MathF.Max(I76.Formats.Vegetation.TumbleweedField.RangeFor(level), BushAppear(lampReach) + 45f);
}
