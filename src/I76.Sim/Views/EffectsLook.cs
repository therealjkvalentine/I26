namespace I76.Sim.Views;

/// <summary>
/// The modern look's weapon effects, scaled by three config-only keys in shell.cfg's [port] section (no menu row): effects_size,
/// effects_brightness and effects_opacity, each a percentage of the look as drawn (100), 25 to 150. They reach the rounds' streaks,
/// impacts, muzzle flashes and lights, rocket trails, smoke puffs and the explosion flipbooks; the 1997 look is not touched.
/// </summary>
public static class EffectsLook
{
    public const int Default = 100, Min = 25, Max = 150;

    /// <summary>A key's value: a whole percentage (a trailing % allowed), clamped to 25..150; absent or unreadable, 100.</summary>
    public static int Parse(string? s)
    {
        var t = (s ?? "").Trim().TrimEnd('%').Trim();
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v)) return Default;
        return Clamp((int)System.Math.Round(v));
    }

    public static int Clamp(int percent) => System.Math.Clamp(percent, Min, Max);

    /// <summary>The factor a percentage stands for (100: 1).</summary>
    public static float Factor(int percent) => Clamp(percent) / 100f;
}
