namespace I76.Game.World;

/// <summary>
/// Heat Shimmer's "hot day" (HeatShimmer's road shimmer and mirage, Fires + Road): a day preset with the sun at least
/// <see cref="MinElevation"/> degrees up (the presets for 7 to 15 o'clock: 35, 80 and 45 degrees; not the 20 degrees of the late
/// afternoon, nor dawn and dusk at 15, nor night) over a warm land, the mission's palette (its .act's mean colour) redder than blue by
/// <see cref="MinWarmth"/> (the Southwest's sand and rock; a grey or cold palette has no hot road), and not on snow (M14's ground
/// family, whose palette is as warm as the desert's). From the mission's data alone.
/// </summary>
public static class HotDay
{
    public const float DefaultMinElevation = 30f, DefaultMinWarmth = 0.04f;
    /// <summary>The rule's numbers: the defaults, changed live only by the Developer panel's Heat shimmer group (HeatTune), never saved.</summary>
    public static float MinElevation { get; set; } = DefaultMinElevation;
    public static float MinWarmth { get; set; } = DefaultMinWarmth;

    public static bool Is(bool day, float sunElevationDeg, float warmth) => day && sunElevationDeg >= MinElevation && warmth >= MinWarmth;

    /// <param name="ground">WDEF's ground family (WorldSettings.Ground, "tp101sw6.map"): snow ("sw", "sr") has no hot road.</param>
    public static bool Is(I76.Formats.Mission.LightPreset light, I76.Formats.Imaging.Palette? palette, string? ground = null) =>
        !Snow(ground) && Is(light.IsDay, I76.Game.Sim.SmokeLight.Elevation(light), palette is null ? MinWarmth : Warmth(palette.Rgb));

    public static bool Snow(string? ground) => I76.Formats.Vegetation.Biomes.FamilyKind(ground) is "sw" or "sr";

    /// <summary>A palette's warmth: (mean red - mean blue) over the mean of the three, 0 for grey, of its 768 bytes (RGB a colour).</summary>
    public static float Warmth(ReadOnlySpan<byte> rgb)
    {
        double r = 0, g = 0, b = 0;
        int n = rgb.Length / 3;
        for (int i = 0; i < n; i++) { r += rgb[i * 3]; g += rgb[i * 3 + 1]; b += rgb[i * 3 + 2]; }
        double mean = (r + g + b) / 3;
        return mean <= 0 ? 0f : (float)((r - b) / n / (mean / n));
    }
}
