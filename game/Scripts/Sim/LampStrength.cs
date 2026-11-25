namespace I76.Game.Sim;

/// <summary>
/// How strong the modern look's lamps are against the daylight. The original lights nothing from a car by day (its
/// headlight pool and night light exist only under a night preset), and its lit lamp faces are flat texture; the modern
/// look's beams, glares and lens glows are an addition, full at night and turned down in sunlight, where a lamp reads
/// as a lit lens rather than a light source. Daylight is <see cref="Daylight"/>'s; the rear lamps' light on the ground is
/// <see cref="RearLampLight"/>'s, the halo's share <see cref="LampHalo"/>'s.
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

    /// <summary>The current mission's daylight (<see cref="Daylight.Current"/>), also for the shaders as the i76_daylight global and the dash lamps.</summary>
    public static void Set(float daylight)
    {
        Daylight.Current = daylight;
        I76.Game.World.ObjectLight.Set("i76_daylight", daylight);
        DashLight.Refresh();
    }
}
