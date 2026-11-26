namespace I76.Game.World;

/// <summary>
/// Heat Shimmer's "hot day" (HeatShimmer's road shimmer and mirage, Fires + Road): a day preset with the sun at least
/// <see cref="MinElevation"/> degrees up (the presets for 7 to 15 o'clock: 35, 80 and 45 degrees; not the 20 degrees of the late
/// afternoon, nor dawn and dusk at 15, nor night) over a warm land, the mission's palette (its .act's mean colour) redder than blue by
/// <see cref="MinWarmth"/> (the Southwest's sand and rock; a grey or cold palette has no hot road). From the mission's data alone.
/// </summary>
public static class HotDay
{
    public const float MinElevation = 30f;
    public const float MinWarmth = 0.04f;

    public static bool Is(bool day, float sunElevationDeg, float warmth) => day && sunElevationDeg >= MinElevation && warmth >= MinWarmth;

    public static bool Is(I76.Formats.Mission.LightPreset light, I76.Formats.Imaging.Palette? palette) =>
        Is(light.IsDay, I76.Game.Sim.SmokeLight.Elevation(light), palette is null ? MinWarmth : Warmth(palette.Rgb));

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
