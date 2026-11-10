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
    /// <summary>How much flatter the fall-off gets per e-fold of the throw: 0.75 keeps the lit road's length about in proportion to the setting (measured on T06's straight road from overhead, the road clearly lit to 73 m at 75, 87 m at 90, 99 m at 100, 117 m at 110).</summary>
    public const float Flatten = 0.75f;

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
    /// The share of the light just ahead of the hood (at <see cref="Near"/>) below which a plant or post facing the lamps no longer
    /// shows lit at night: 7 %, where today's beam (75) ends visibly at 90 m and the default's at 126 m (the road "faintly lit" there).
    /// </summary>
    public const float VisibleShare = 0.07f;

    /// <summary>
    /// How far the lamps visibly light what stands by the road at a setting (metres from the lamp): where their light, by the
    /// fall-off and the energy at that setting, drops to <see cref="VisibleShare"/> of the light at Near (beyond, the beam is too
    /// dim to show it). 56 m at 50, 90 at 75, 126 at 100, 163 at 125, 201 at 150.
    /// </summary>
    public static float VisibleReach(int percent)
    {
        float r = Range(percent), a = Attenuation(percent), want = VisibleShare * Falloff(Near, TodayRange, TodayAttenuation), es = EnergyScale(percent);
        float lo = Near, hi = r;
        for (int i = 0; i < 40; i++)
        {
            float m = 0.5f * (lo + hi);
            if (es * Falloff(m, r, a) > want) lo = m; else hi = m;
        }
        return lo;
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
