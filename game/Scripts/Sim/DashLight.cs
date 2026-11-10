namespace I76.Game.Sim;

/// <summary>
/// The modern look's dash lamps: the speedometer's and tachometer's needles lit from behind the dial at night. The original lights
/// the needles as any object face, by the scene's light, while the dial faces are painted art shown as painted: measured in the
/// original (retail 1.0 in an emulated Pentium, 640x480), the faces read the same by day and at night and the needles fall from Oklab
/// L 0.29-0.35 by day to 0.17 at night, a dark maroon on the black face. Here the needles also show their own colour, warmed as a 12 V instrument bulb's light, in proportion to how far
/// the preset is from daylight: none in the sun (the scene lights them), full at night. [port] dash_light in percent: 100 shows a
/// needle at night as light as the sun shows it by day; I76_DASH_LIGHT overrides the file for a run. The 1997 look is untouched.
/// </summary>
public static class DashLight
{
    public const int Default = 100;
    public static int Percent = Default;
    public static readonly int? Hook = int.TryParse(System.Environment.GetEnvironmentVariable("I76_DASH_LIGHT"), out int h) ? h : null;

    /// <summary>The file's word (0 to 200), the default when absent or not a number.</summary>
    public static int Parse(string? s) => int.TryParse(s, out int p) ? Math.Clamp(p, 0, 200) : Default;

    /// <summary>
    /// The share of the needle's painted colour that dash_light 100 shows at night: measured at 3440x1440 in the modern look (the
    /// owner's settings, the dash glanced down), T01's needles by day at Oklab L 0.45 and 0.40 (speedometer, tachometer), T06's at night
    /// with this share 0.43 and 0.43 (0.25: 0.46; 0.35: 0.51; the whole colour: 0.64, a pink that no daylight shows).
    /// </summary>
    public const float DayMatch = 0.2f;

    /// <summary>The lamps' level at <paramref name="daylight"/> (LampStrength.Daylight: 1 in full sun, 0 at night): the share of the needle's colour shown.</summary>
    public static float Level(float daylight, int percent) => Math.Clamp(percent, 0, 200) / 100f * DayMatch * (1f - Math.Clamp(daylight, 0f, 1f));

    /// <summary>The level now: the setting (or the hook) at the mission's daylight.</summary>
    public static float Current => Level(LampStrength.Current, Hook ?? Percent);

    /// <summary>The object shader's global (object.gdshaderinc, the materials with dash_lamp).</summary>
    public const string Global = "i76_dash_light";
    /// <summary>The material flag of the faces the lamps light (the needles).</summary>
    public const string Uniform = "dash_lamp";

    public static void Refresh() => I76.Game.World.ObjectLight.Set(Global, Current);
}
