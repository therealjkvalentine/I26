namespace I76.Game.Sim;

/// <summary>
/// How strong the modern look's lamps are against the daylight. The original lights nothing from a car by day (its
/// headlight pool and night light exist only under a night preset), and its lit lamp faces are flat texture; the modern
/// look's beams, glares and lens glows are an addition, full at night and turned down in sunlight, where a lamp reads
/// as a lit lens rather than a light source. Daylight is the preset's sun intensity (1 by day, 0.625 at dusk), 0 at night.
/// </summary>
public static class LampStrength
{
    /// <summary>The headlamp spotlights' energy at night.</summary>
    public const float HeadEnergy = 9f;
    /// <summary>
    /// The rear lamps' red light on the ground behind the car, at night: a spotlight at each lamp, looking back and down. The tail
    /// filament throws a faint glow a couple of metres; the brake's, several times as bright, a plain red pool. Small beside the
    /// headlamps' 9: the pool lies a metre or two from the lamp, where the headlamps' light is spread over 50 m of road (at 1 and 4,
    /// the first tries, the tail lamps' pool was as bright as the headlamps' road).
    /// </summary>
    public const float TailGroundEnergy = 0.06f, BrakeGroundEnergy = 0.8f;
    /// <summary>The lens glow over the lit brake and tail lamp faces, at night.</summary>
    public const float BrakeGlow = 0.9f, TailGlow = 0.4f;
    /// <summary>What is left of each in full sun.</summary>
    public const float HeadDayFloor = 0.2f, GlowDayFloor = 0.4f;
    /// <summary>The rear lamps' light on the ground is a night's: none in full sun.</summary>
    public const float RearGroundDayFloor = 0f;

    /// <summary>
    /// The rear lamps' ground light at the tail and brake filaments' levels (0 to 1, LampRamp's): the tail's gives way to the
    /// brake's as it rises, as a dual-filament bulb's does, turned down by day to nothing.
    /// </summary>
    public static float RearGround(float tail, float brake, float daylight) =>
        (TailGroundEnergy * tail * (1f - brake) + BrakeGroundEnergy * brake) * Scale(daylight, RearGroundDayFloor);

    /// <summary>
    /// [port] lamp_halo: the lens glows and the glares (the halo round a lit lamp) as a percentage of the strengths above, which
    /// stay the 100 mark; 65 by default, a third under, the owner's "chill out the halo on the lights a bit". The lights themselves
    /// (the beams on the road, the rear lamps' red light on the ground) keep their energy. I76_LAMP_HALO overrides the file for a run.
    /// </summary>
    public const int HaloDefault = 65;
    public static int HaloPercent = HaloDefault;
    public static readonly int? HaloHook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_LAMP_HALO"), out int hh) ? hh : null;
    public static float Halo => Math.Clamp(HaloHook ?? HaloPercent, 0, 200) / 100f;
    /// <summary>The file's word (0 to 200), the default when absent or not a number.</summary>
    public static int ParseHalo(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, 0, 200) : HaloDefault;

    /// <summary>The daylight a preset gives: its intensity under the sun, 0 under the moon.</summary>
    public static float Daylight(bool isDay, float intensity) => isDay ? Math.Clamp(intensity, 0f, 1f) : 0f;

    /// <summary>A lamp's share of its night strength at <paramref name="daylight"/>: 1 at night, <paramref name="dayFloor"/> in full sun.</summary>
    public static float Scale(float daylight, float dayFloor) => 1f - (1f - dayFloor) * Math.Clamp(daylight, 0f, 1f);

    /// <summary>The current mission's daylight, as last set (the shaders read it as the i76_daylight global).</summary>
    public static float Current { get; private set; }

    public static void Set(float daylight)
    {
        Current = daylight;
        I76.Game.World.ObjectLight.Set("i76_daylight", daylight);
        DashLight.Refresh();
    }
}
