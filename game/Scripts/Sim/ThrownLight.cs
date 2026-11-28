namespace I76.Game.Sim;

/// <summary>
/// The thrown light ([port] thrown_light, the Developer panel), the modern look's: the light a car's lamps, a shot, a strike,
/// an explosion or a fire throws on the world round it. Not the lamps' own lit faces, glows and glares (LampStrength, LampHalo).
/// <para>
/// The original makes dynamic lights only in a night period (its time-of-day table's daylight flag: 22:00 to 03:59; dawn at 06:00 and
/// the 21:00 dusk are daylight periods): by day nothing from a car's lamps, its guns or an explosion lights anything. At night a car's
/// headlamps lay a flat 20 m pool on the road ahead and light objects (a spot of range 20); muzzle flashes, rounds and explosions light
/// objects (omni, 20 to 25 m), never the terrain or the roads, which do not read the light list; nothing red falls behind a braking car
/// (docs/deviations.md, the Headlights, Rear lamp light and Muzzle light rows).
/// </para>
/// <para>
/// Original (the default) follows that: by day (any daylight preset) the lamps throw nothing; the flashes, strikes, explosions and fires a
/// trace (<see cref="DayTrace"/> of their night strength, set on the screen as the lamps' light is, LampStrength.GainFor, so every
/// Lighting choice shows it alike), and only on objects; at night the headlamps light the road as before, the rear lamps throw nothing,
/// and the flashes, explosions and fires light the cars and the scenery at their full strength but not the ground. Bright is the modern
/// look's light as it was before the key: the headlamps a fifth by day, the rear lamps' red pool and wash, the muzzle flash's day trace
/// and every explosion's and fire's light at full strength by day and night on the ground too. The 1997 look draws none of it either way.
/// </para>
/// </summary>
public static class ThrownLight
{
    /// <summary>The setting: Bright (the look before the key) or Original (false, the default).</summary>
    public static bool Bright { get; set; }

    /// <summary>I76_THROWN_LIGHT=original|bright overrides the file for a run (a before | after).</summary>
    public static readonly bool? Hook = System.Environment.GetEnvironmentVariable("I76_THROWN_LIGHT") switch { "bright" => true, "original" => false, _ => null };

    /// <summary>The setting now: the hook, else the file's.</summary>
    public static bool BrightNow => Hook ?? Bright;

    /// <summary>What is left of a flash's, an explosion's or a fire's night strength by day under Original: a trace, on objects only.</summary>
    public const float DayTrace = 0.12f;

    /// <summary>Whether <paramref name="daylight"/> (Daylight: the preset's sun intensity under the sun, 0 under the moon) is a night period's.</summary>
    public static bool Night(float daylight) => daylight <= 0f;

    /// <summary>
    /// A lamp's thrown light (the headlamps' beams) as a share of its night strength: Original's the original's, all at night and none by
    /// day; Bright's turned down by the daylight to <paramref name="dayFloor"/> in full sun (Daylight.Scale), as before.
    /// </summary>
    public static float Lamp(float daylight, float dayFloor, bool bright) => bright ? Daylight.Scale(daylight, dayFloor) : Night(daylight) ? 1f : 0f;

    /// <summary>Whether the rear lamps throw their red light on the ground behind the car (Bright only: the original's throw none).</summary>
    public static bool Rear(bool bright) => bright;

    /// <summary>
    /// A flash's, a strike's, an explosion's or a fire's light as a share of its strength: Bright's 1 (as before); Original's 1 at night and by
    /// day <see cref="DayTrace"/>, set on the screen (<paramref name="exposure"/>: the calibration's, divided out as the lamps' is).
    /// </summary>
    public static float Effect(float daylight, float exposure, bool bright) =>
        bright || Night(daylight) ? 1f : DayTrace * LampStrength.GainFor(exposure, daylight);

    /// <summary>The muzzle flash's share by day (MuzzleReach): Bright's the trace it had (<paramref name="dayFloor"/> through the daylight), Original's <see cref="Effect"/>.</summary>
    public static float Muzzle(float daylight, float dayFloor, float exposure, bool bright) =>
        bright ? Daylight.Scale(daylight, dayFloor) : Effect(daylight, exposure, false);

    /// <summary>What an effect's light lights, from the layers it would light (<paramref name="mask"/>): Original's leaves out the ground (the terrain's layer: the ground, the roads on it and its plants).</summary>
    public static uint Mask(uint mask, bool bright) => bright ? mask : mask & ~I76.Game.World.TerrainNode.TerrainLayer;

    /// <summary>The share now (the mission's daylight and the calibration's exposure, the setting in force).</summary>
    public static float EffectNow => Effect(Daylight.Current, LampStrength.Exposure, BrightNow);

    /// <summary>The cull mask now for an effect's light that would light <paramref name="mask"/>.</summary>
    public static uint MaskNow(uint mask = 0xFFFFFu) => Mask(mask, BrightNow);

    /// <summary>An effect's light set to <paramref name="energy"/> (its strength as designed) under the rule now: its energy and what it lights.</summary>
    public static void Set(Godot.Light3D light, float energy, uint mask = 0xFFFFFu)
    {
        light.LightEnergy = energy * EffectNow;
        light.LightCullMask = MaskNow(mask);
    }
}
