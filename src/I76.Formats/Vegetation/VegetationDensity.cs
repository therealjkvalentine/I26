namespace I76.Formats.Vegetation;

/// <summary>
/// The modern look's scattered plants, thinned by a config-only key in shell.cfg's [port] section (no menu row):
/// vegetation_density, a percentage of the cover Graphic Detail's Vegetation level places (Low or High), 10 to 150, 60 when
/// absent. It scales the placer's density, so a lower value keeps a subset of the same plants. The map's own placed objects
/// (its trees, bushes and cacti) are not touched.
/// </summary>
public static class VegetationDensity
{
    public const int Default = 60, Min = 10, Max = 150;

    /// <summary>A key's value: a whole percentage (a trailing % allowed), clamped to 10..150; absent or unreadable, 60.</summary>
    public static int Parse(string? s)
    {
        var t = (s ?? "").Trim().TrimEnd('%').Trim();
        if (!double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.IsNaN(v)) return Default;
        return Clamp((int)System.Math.Round(v));
    }

    public static int Clamp(int percent) => System.Math.Clamp(percent, Min, Max);

    /// <summary>The factor on the level's density a percentage stands for (100: the level as it is).</summary>
    public static float Factor(int percent) => Clamp(percent) / 100f;
}
