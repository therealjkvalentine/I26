using I76.Formats.Imaging;

namespace I76.Game.World;

/// <summary>
/// The modern look's night levels (docs/deviations.md "Lighting (modern look)"): the share of its own light the moon keeps on the
/// ground, how much of the 1997 look's light the ground takes besides, the share of the ground's light a road keeps, the sky's gain
/// over its own, how much of the ground's colour the moon's light keeps and how far it is turned toward moonlight's warm white; the
/// night terrain shader's globals (terrain_night.gdshaderinc). Under every lighting_calibration: through the card's curve (on) the
/// moon's share is solved at the load (LightingCalibration.NightMoon); through Filmic (off, filmic) the whole moon is the level that
/// share was solved to show, and the road's share is solved through Filmic. The Developer panel's Night sliders scale the solved
/// levels ([port] night_moon, night_road, night_sky) and set the colour's (night_chroma, night_warm); the I76_NIGHT_ hooks set a
/// level outright and win over both.
/// </summary>
public static class NightDial
{
    /// <summary>The levels the shaders and the light take; <see cref="Neutral"/> by day: nothing changed.</summary>
    public readonly record struct Levels(float Moon, float Ground, float Road, float Sky, float Chroma, float Warm)
    {
        public static readonly Levels Neutral = new(1, 0, 1, 1, 0, 0);
    }

    /// <summary>The panel's scales of a solved level (night_moon, night_road, night_sky), 1 the level as solved.</summary>
    public static readonly double[] ScaleSteps = [0.1, 0.15, 0.2, 0.3, 0.5, 0.7, 1, 1.4, 2, 3, 5, 7, 10];
    /// <summary>The panel's colour levels (night_chroma, night_warm), 0 to 1 in tenths.</summary>
    public static readonly double[] MixSteps = [.. Enumerable.Range(0, 11).Select(i => i / 10.0)];

    public static int Nearest(double[] steps, double v) => Array.IndexOf(steps, steps.MinBy(s => Math.Abs(Math.Log(Math.Max(s, 1e-3)) - Math.Log(Math.Max(v, 1e-3)))));
    public static int NearestMix(double v) => Array.IndexOf(MixSteps, MixSteps.MinBy(s => Math.Abs(s - v)));

    /// <summary>
    /// The moon's and the sky's scales as shipped: 1.4 each, dialled by eye against the night mission's cutscenes (the gas stop's open
    /// ground and sky); 1 is the level as solved, the earlier default.
    /// </summary>
    public const double MoonDefault = 1.4, SkyDefault = 1.4;

    /// <summary>
    /// The mean luminance (linear) of the night sky texture the levels were set on: the gas stop's (T06; Suburbia shares it). The
    /// other stock night sky (T12 and Night Driver share it) is 0.59 of it, so the same sky gain showed those missions' skies darker.
    /// </summary>
    public const double SkyReference = 0.0080;
    /// <summary>The bounds of <see cref="SkyNorm"/>: a sky far from the reference is brought part of the way only.</summary>
    public const double SkyNormMin = 0.5, SkyNormMax = 3;

    /// <summary>
    /// The modern look's night sky gain for a mission, over the reference's: what brings its sky texture's mean luminance
    /// <paramref name="skyAverage"/> (linear) to <see cref="SkyReference"/>, so night_sky shows the same sky in every night mission
    /// (1 on the reference's own; from the mission's own sky, not its name). The 1997 look keeps each texture as it is, as the original.
    /// </summary>
    public static double SkyNorm(double skyAverage) => skyAverage > 0 && double.IsFinite(skyAverage)
        ? Math.Clamp(SkyReference / skyAverage, SkyNormMin, SkyNormMax) : 1;

    /// <summary>A scale's value read from shell.cfg: a positive number, kept to the slider's range; else <paramref name="dflt"/>.</summary>
    public static double ParseScale(string? text, double dflt = 1) =>
        double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 && double.IsFinite(v)
            ? Math.Clamp(v, ScaleSteps[0], ScaleSteps[^1]) : dflt;

    /// <summary>A colour level read from shell.cfg: 0 to 1; else <paramref name="dflt"/>.</summary>
    public static double ParseMix(string? text, double dflt) =>
        double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? Math.Clamp(v, 0, 1) : dflt;

    /// <summary>The panel's settings: the three scales (1 as solved; the moon's and the sky's 1.4 by default) and the two colour levels.</summary>
    public readonly record struct Dial(double Moon = MoonDefault, double Road = 1, double Sky = SkyDefault, double Chroma = LightingCalibration.NightChroma, double Warm = LightingCalibration.NightWarm)
    {
        public Dial() : this(MoonDefault) { }
    }

    /// <summary>
    /// The levels for a night: <paramref name="solvedMoon"/> the moon's share through the card's curve (1 through Filmic),
    /// <paramref name="road"/> the road's share for a moon level (LightingCalibration.NightRoad through the curve in use),
    /// <paramref name="skyNorm"/> the mission's sky gain over the reference's (<see cref="SkyNorm"/>); <paramref name="env"/> an I76_NIGHT_ hook's value, or null.
    /// </summary>
    public static Levels Night(bool card, double solvedMoon, Func<double, double> road, Dial dial, Func<string, float?> env, double skyNorm = 1)
    {
        float moon = env("I76_NIGHT_MOON") ?? (float)((card ? solvedMoon : 1) * dial.Moon);
        return new Levels(moon,
            env("I76_NIGHT_GROUND") ?? (float)LightingCalibration.NightGround,
            env("I76_NIGHT_ROAD") ?? (float)Math.Clamp(road(moon) * dial.Road, 0, 1),
            env("I76_NIGHT_SKY") ?? (float)((card ? LightingCalibration.NightSky : LightingCalibration.NightSkyFilmic) * dial.Sky * skyNorm),
            env("I76_NIGHT_CHROMA") ?? (float)dial.Chroma,
            env("I76_NIGHT_WARM") ?? (float)dial.Warm);
    }

    /// <summary>An I76_NIGHT_ hook from the environment.</summary>
    public static float? Env(string name) => float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out var k) ? k : null;

    /// <summary>The night terrain shader's globals (project.godot) and their values; the moon's and the sky's go on the light and the sky.</summary>
    public static (string Name, float Value)[] Globals(Levels l) =>
        [("i76_night_ground", l.Ground), ("i76_night_road", l.Road), ("i76_night_chroma", l.Chroma), ("i76_night_warm", l.Warm)];
}
