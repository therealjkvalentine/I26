namespace I76.Game.Shell;

/// <summary>
/// Lighting ([port] lighting_calibration, Graphic Detail &gt; Effects, the modern look's): how its light and colours follow the 1997 look's.
/// 1997 (on, the default): each mission's exposure, sky light and tints solved so its ground and cars show the 1997 look's light,
/// through the 3D card's curve, the sky in its texture's colours. 1997 Sunlit (sunlit): the same with the sun's strength solved too
/// by day, for the 1997 look's contrast (the sunlit ground brighter, the shade as dim), and Palette Grade toward the mission's
/// 16-bit palette. Filmic (filmic): the calibration under Filmic with the modern sky. Off: none, the modern look's own light.
/// Not in the original; the 1997 look is unchanged by any.
/// </summary>
public static class LightingRow
{
    public const int Matched = 0, Sunlit = 1, Filmic = 2, Off = 3;
    public static readonly string[] Names = ["1997" + ShellSettings.PortMark, "1997 Sunlit" + ShellSettings.PortMark, "Filmic" + ShellSettings.PortMark, "Off"];
    public const string Label = "Lighting";

    public static int Index(ShellSettings s) => !s.LightingCalibration ? Off : s.LightingFilmic ? Filmic : s.LightingSunlit ? Sunlit : Matched;

    public static void Put(ShellSettings s, int v)
    {
        v = Math.Clamp(v, Matched, Off);
        s.LightingCalibration = v != Off;
        s.LightingFilmic = v == Filmic;
        s.LightingSunlit = v == Sunlit;
    }

    public static void Step(ShellSettings s) => Put(s, (Index(s) + 1) % Names.Length);

    /// <summary>The file's word for a choice (none for the default, which is not written).</summary>
    public static string Word(int v) => Math.Clamp(v, Matched, Off) switch { Sunlit => "sunlit", Filmic => "filmic", Off => "off", _ => "on" };
}
