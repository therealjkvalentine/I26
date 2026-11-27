namespace I76.Game.Sim;

/// <summary>
/// Smoke Linger Long's fresh smoke, tunable on the Developer panel ([port] keys, written when not the default; the modern look's only,
/// Normal and the 1997 look untouched): how black a fresh puff starts (<see cref="Dark"/>, smoke_start_dark, percent: the colour of a
/// dark puff taken that far toward black at its birth, easing back to its own over its life, so the column is blackest at the fire and
/// greys as it rises; white steam and trails are left as they are), how dense it starts (<see cref="Density"/>, smoke_start_density,
/// percent of Normal's density; the layer's cap on a column rises with it), how narrow it starts (<see cref="Width"/>, smoke_start_width,
/// percent of Normal's size at the source; it widens to its own full size by the end of its life, and the emitters' scatter narrows
/// with it), and how long the haze takes to fade (<see cref="Fade"/>, smoke_fade, seconds a lone puff takes to thin away after its
/// life). The values before these keys (section IW): dark 0, density 135, width 100, fade 150. I76_SMOKE_START=dark,density,width,fade
/// overrides the file for a run (a before | after). Rendering only: nothing reaches the simulation or the wire.
/// </summary>
public static class SmokeStart
{
    public const int DefaultDark = 85, DefaultDensity = 200, DefaultWidth = 55, DefaultFade = 150;
    /// <summary>Section IW's values, before the keys.</summary>
    public const int BeforeDark = 0, BeforeDensity = 135, BeforeWidth = 100, BeforeFade = 150;
    public const int MinDark = 0, MaxDark = 90, MinDensity = 100, MaxDensity = 250, MinWidth = 30, MaxWidth = 100, MinFade = 60, MaxFade = 600;

    public static int DarkPercent = DefaultDark, DensityPercent = DefaultDensity, WidthPercent = DefaultWidth, FadeSeconds = DefaultFade;

    /// <summary>I76_SMOKE_START=dark,density,width,fade: the four for the run, whatever the file says.</summary>
    public static readonly int[]? Hook = Parse4(System.Environment.GetEnvironmentVariable("I76_SMOKE_START"));

    private static int[]? Parse4(string? s)
    {
        if (s is null) return null;
        var p = s.Split(',');
        if (p.Length != 4) return null;
        var v = new int[4];
        for (int i = 0; i < 4; i++) if (!int.TryParse(p[i], out v[i])) return null;
        return v;
    }

    public static int ParseDark(string? s) => int.TryParse(s, out int v) ? Math.Clamp(v, MinDark, MaxDark) : DefaultDark;
    public static int ParseDensity(string? s) => int.TryParse(s, out int v) ? Math.Clamp(v, MinDensity, MaxDensity) : DefaultDensity;
    public static int ParseWidth(string? s) => int.TryParse(s, out int v) ? Math.Clamp(v, MinWidth, MaxWidth) : DefaultWidth;
    public static int ParseFade(string? s) => int.TryParse(s, out int v) ? Math.Clamp(v, MinFade, MaxFade) : DefaultFade;

    /// <summary>A fresh dark puff's extra blackness (0..0.9).</summary>
    public static float Dark => Math.Clamp(Hook?[0] ?? DarkPercent, MinDark, MaxDark) / 100f;
    /// <summary>The factor on a puff's starting density (1..2.5).</summary>
    public static float Density => Math.Clamp(Hook?[1] ?? DensityPercent, MinDensity, MaxDensity) / 100f;
    /// <summary>A puff's size at the source as a share of Normal's (0.3..1).</summary>
    public static float Width => Math.Clamp(Hook?[2] ?? WidthPercent, MinWidth, MaxWidth) / 100f;
    /// <summary>Seconds a lone haze puff takes to thin away after its life.</summary>
    public static float Fade => Math.Clamp(Hook?[3] ?? FadeSeconds, MinFade, MaxFade);

    /// <summary>The layer's cap on a column for a density factor: 0.65 (Normal's) at 1, rising 0.4 for each 1 more, at most 0.95 (135 gives section IW's 0.79).</summary>
    public static float Cap(float density) => Math.Clamp(0.65f + 0.4f * (density - 1f), 0.65f, 0.95f);

    /// <summary>
    /// A puff's size at <paramref name="life"/> (0..1 of its life) growing from <paramref name="start"/> to <paramref name="end"/> as Normal's
    /// does (fast, then slowing), times a narrowing <paramref name="width"/> at the source that eases out over the same curve: the end size kept.
    /// </summary>
    public static float Size(float start, float end, float life, float width)
    {
        float e = 1f - (1f - life) * (1f - life);
        return (start + (end - start) * e) * (width + (1f - width) * e);
    }

    /// <summary>How much darker a fresh puff of darkness <paramref name="dark"/> is drawn at shader age <paramref name="life"/> (the shader's rule): 1 - k·dark²·(1-life), nothing past its life (soot blackens, light trails hardly).</summary>
    public static float Shade(float k, float dark, float life)
    {
        float t = Math.Clamp(1f - life, 0f, 1f);
        return 1f - k * dark * dark * t;
    }
}
