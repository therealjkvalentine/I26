namespace I76.Sim.Views;

/// <summary>
/// The drawn-only speeds the player may set in shell.cfg's [port] section (no menu row): sky_speed, the clouds' drift, and
/// death_orbit_speed, the camera's orbit after a car is destroyed and at a network match's end. Each is a percentage of the
/// rate the port had before the keys (100), 0 to 200, 50 by default. Neither is in the simulation or a snapshot: the sky and
/// the camera are presentation, so a replay or a network peer is unaffected.
/// </summary>
public static class PresentationSpeed
{
    public const int Default = 50, Full = 100, Min = 0, Max = 200;

    /// <summary>A key's value: a whole percentage (a trailing % allowed), clamped to 0..200; absent or unreadable, 50.</summary>
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
