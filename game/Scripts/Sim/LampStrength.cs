namespace I76.Game.Sim;

/// <summary>
/// How strong the modern look's lamps are against the daylight. The original lights nothing from a car by day (its
/// headlight pool and night light exist only under a night preset), and its lit lamp faces are flat texture; the modern
/// look's beams, glares and lens glows are an addition, full at night and turned down in sunlight, where a lamp reads
/// as a lit lens rather than a light source. Daylight is <see cref="Daylight"/>'s; the rear lamps' light on the ground is
/// <see cref="RearLampLight"/>'s, the halo's share <see cref="LampHalo"/>'s.
/// <para>
/// The lamps' own light is set on the screen, not in the scene: the calibration's exposure (a mission's and a Lighting choice's: 0.15
/// at T10's dusk under Lighting 1997, 0.3 under 1997 Sunlit, 1 with the calibration off; 1 at night under every choice) would scale a
/// lens glow, a glare or a beam six times over between missions and choices by day. <see cref="Gain"/> divides it out and puts in the
/// reference exposure by day instead, so by day a lamp shows on the screen as it does at <see cref="ReferenceExposure"/>, whatever the
/// mission and choice, as the 1997 look's lit lamp faces show alike in every mission; at night nothing changes.
/// </para>
/// </summary>
public static class LampStrength
{
    /// <summary>The headlamp spotlights' energy at night.</summary>
    public const float HeadEnergy = 9f;
    /// <summary>The lens glow over the lit brake and tail lamp faces, at night.</summary>
    public const float BrakeGlow = 0.45f, TailGlow = 0.2f;
    /// <summary>The glare over a lit rear lamp seen from behind (the brake's full, the tail's <see cref="TailGlareShare"/> of it), at night, and what is left of it in full sun.</summary>
    public const float RearGlare = 0.225f, RearGlareDayFloor = 0.125f, TailGlareShare = 0.3f;
    /// <summary>What is left of each in full sun.</summary>
    public const float HeadDayFloor = 0.2f, GlowDayFloor = 0.25f;

    /// <summary>
    /// How far the headlamps' light reaches (metres from the lamp, where it ends) at a [port] headlamp_throw percentage, or at the
    /// one in force (null): the range every car's headlamp spotlights are given (HeadlampThrow).
    /// </summary>
    public static float HeadlampRange(int? throwPercent = null) => HeadlampThrow.Range(throwPercent ?? HeadlampThrow.Current);

    /// <summary>
    /// The exposure the lamps' day strengths are set for: the default Lighting (1997)'s by day, the median of the trip's and the
    /// scenarios' day missions (0.15 to 0.99, half of them 0.4 to 0.66), where the lamps measured nearest the 1997 look.
    /// </summary>
    public const float ReferenceExposure = 0.6f;

    /// <summary>
    /// The factor on the lamps' own light (lens glows, glares, beams, the rear lamps' light on the ground) at the scene's
    /// <paramref name="exposure"/> and <paramref name="daylight"/>: by day the reference exposure over the scene's, at night 1 over it
    /// (every Lighting choice's night exposure is 1), between by the daylight. Bounded, for a degenerate exposure.
    /// </summary>
    public static float GainFor(float exposure, float daylight)
    {
        if (!float.IsFinite(exposure) || exposure <= 0f) return 1f;
        float d = Math.Clamp(daylight, 0f, 1f);
        return Math.Clamp((1f + (ReferenceExposure - 1f) * d) / exposure, 0.1f, 10f);
    }

    /// <summary>The scene's exposure as the calibration last set it (Main; 1 with none).</summary>
    public static float Exposure { get; private set; } = 1f;

    /// <summary>The factor now (<see cref="GainFor"/>; 1 with the rule off), also the i76_lamp_gain global the lamps' shaders read.</summary>
    public static float Gain => ExposureRule ? GainFor(Exposure, Daylight.Current) : 1f;

    private static bool _rule = true;
    private static readonly bool RuleHook = System.Environment.GetEnvironmentVariable("I76_LAMP_EXPOSURE") != "0";
    /// <summary>The exposure rule on (I76_SHOT_SEQ lampfix=0 turns it off, for the comparisons; I76_LAMP_EXPOSURE=0 for a run).</summary>
    public static bool ExposureRule
    {
        get => _rule && RuleHook;
        set { _rule = value; Publish(); }
    }

    /// <summary>The scene's exposure (the calibration's: Main.ApplyLightingCalibration).</summary>
    public static void SetExposure(float exposure)
    {
        Exposure = float.IsFinite(exposure) && exposure > 0f ? exposure : 1f;
        Publish();
    }

    private static void Publish()
    {
        if (Godot.Engine.GetMainLoop() is null) return;   // (the tests: no renderer)
        I76.Game.World.ObjectLight.Set("i76_lamp_gain", Gain);
    }

    /// <summary>The current mission's daylight (<see cref="Daylight.Current"/>), also for the shaders as the i76_daylight global and the dash lamps.</summary>
    public static void Set(float daylight)
    {
        Daylight.Current = daylight;
        I76.Game.World.ObjectLight.Set("i76_daylight", daylight);
        Publish();
        DashLight.Refresh();
    }
}
