namespace I76.Game.Sim;

/// <summary>
/// How far down the road the modern look's headlamps reach ([port] headlamp_throw, in percent). The original draws a flat lit
/// pool on the ground ahead of a car at night, about 20 m long, and lights nothing beyond it; the modern look's spotlights
/// (CarNode) light the road and what stands by it. TodayPercent is the throw they had until this key (a 110 m spot range under
/// a 0.6 fall-off); 100, the default, reaches further down the road by a longer range and a flatter fall-off, a low beam's
/// shape, with the energy set so the road a few metres ahead (Near) takes the same light at every setting: the hood and the
/// near road do not brighten. Every car's lamps, the player's and the others', take it. I76_HEADLAMP_THROW overrides the file for a run.
/// </summary>
public static class HeadlampThrow
{
    public const int Default = 100, Min = 50, Max = 150;
    /// <summary>The setting that gives the beams as they were before the key: the 110 m spot under a 0.6 fall-off.</summary>
    public const int TodayPercent = 75;
    public const float TodayRange = 110f, TodayAttenuation = 0.6f;
    /// <summary>The distance from the lamp (metres) at which every setting puts the same light: the road just ahead of the hood.</summary>
    public const float Near = 8f;

    public static int Percent = Default;
    public static readonly int? Hook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_HEADLAMP_THROW"), out int h) ? h : null;

    /// <summary>The file's word (Min to Max), the default when absent or not a number.</summary>
    public static int Parse(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, Min, Max) : Default;

    /// <summary>The setting in force: the hook's, else the file's.</summary>
    public static int Current => Math.Clamp(Hook ?? Percent, Min, Max);

    /// <summary>The spot's range (metres, where its light ends) at a setting: in proportion to the setting, the 110 m at TodayPercent.</summary>
    public static float Range(int percent) => TodayRange * Math.Clamp(percent, Min, Max) / TodayPercent;

    /// <summary>
    /// The spot's distance fall-off (Godot's spot_attenuation, the power of the distance the light falls with) at a setting:
    /// today's 0.6, flatter as the throw grows (a low beam keeps its light down the road), steeper below it.
    /// </summary>
    public static float Attenuation(int percent) => Math.Max(0f, TodayAttenuation - Flatten * MathF.Log((float)Math.Clamp(percent, Min, Max) / TodayPercent));
    /// <summary>How much flatter the fall-off gets per e-fold of the throw.</summary>
    public static readonly float Flatten = float.TryParse(System.Environment.GetEnvironmentVariable("I76_TMP_FLATTEN"), System.Globalization.CultureInfo.InvariantCulture, out float tf) ? tf : 0.75f; // TEMP

    /// <summary>The spot's energy at a setting, as a share of the energy before the key: the light at Near kept as it was.</summary>
    public static float EnergyScale(int percent) =>
        Falloff(Near, TodayRange, TodayAttenuation) / Falloff(Near, Range(percent), Attenuation(percent));

    /// <summary>Godot's distance fall-off of an omni or spot light (the window (1 - (d/r)^4)^2 times d^-attenuation).</summary>
    public static float Falloff(float d, float range, float attenuation)
    {
        float nd = d / range;
        nd *= nd;
        nd *= nd;
        float w = MathF.Max(1f - nd, 0f);
        return w * w * MathF.Pow(MathF.Max(d, 0.0001f), -attenuation);
    }

    /// <summary>
    /// The light a flat road takes from one lamp at <paramref name="distance"/> metres ahead (the lamp <paramref name="height"/> over it),
    /// in the energy's units: the fall-off at the slant distance, the energy's share and the grazing angle (the beam's cone, near
    /// full along the road, left out). For the tests and the curve on the review sheet.
    /// </summary>
    public static float RoadLight(int percent, float distance, float height = 0.8f)
    {
        float d = MathF.Sqrt(distance * distance + height * height);
        return EnergyScale(percent) * Falloff(d, Range(percent), Attenuation(percent)) * height / d;
    }
}
