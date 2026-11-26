using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Shadow Detail's Ultra* (ShadowDetailRow), the modern look's: High's four splits to 1500 m with a map twice as fine (8192), the
/// filter one step softer (Soft Medium: the stipple Soft Low leaves along a terrain shadow's edge goes), the near splits nearer, and
/// the splits blended so no seam shows where one ends. Low and High keep Soft Low on a 4096 atlas, as before. The filter and the atlas are the
/// renderer's (every directional light's), so they are sent only on a change (each send costs the next frame, Main.SetMode1997Steps).
/// I76_SHADOW_FILTER=hard|verylow|low|medium|high|ultra, I76_SHADOW_BLEND=0|1 and I76_SHADOW_BLUR=x override them for a run.
/// </summary>
public static class ShadowUltra
{
    public const RenderingServer.ShadowQuality UltraFilter = RenderingServer.ShadowQuality.SoftMedium;
    public const int UltraAtlas = 8192;
    public const float UltraBlur = 1f;

    public static RenderingServer.ShadowQuality Filter(bool ultra) => Hook("I76_SHADOW_FILTER") switch
    {
        "hard" => RenderingServer.ShadowQuality.Hard,
        "verylow" => RenderingServer.ShadowQuality.SoftVeryLow,
        "low" => RenderingServer.ShadowQuality.SoftLow,
        "medium" => RenderingServer.ShadowQuality.SoftMedium,
        "high" => RenderingServer.ShadowQuality.SoftHigh,
        "ultra" => RenderingServer.ShadowQuality.SoftUltra,
        _ => ultra ? UltraFilter : RenderingServer.ShadowQuality.SoftLow,
    };

    public static int Atlas(bool ultra) => ultra ? UltraAtlas : 4096;

    public static float Blur(bool ultra) =>
        float.TryParse(Hook("I76_SHADOW_BLUR"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b) ? b : ultra ? UltraBlur : 1f;

    /// <summary>The three split points as shares of the reach: Godot's defaults (0.1, 0.2, 0.5) at Low and High; I76_SHADOW_SPLIT_AT=a,b,c.</summary>
    public static (float A, float B, float C) Splits(bool ultra)
    {
        if (Hook("I76_SHADOW_SPLIT_AT")?.Split(',') is [var a, var b, var c]) return (P(a), P(b), P(c));
        return ultra ? UltraSplits : (0.1f, 0.2f, 0.5f);
        static float P(string t) => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
    }
    /// <summary>Ultra's: the first two splits nearer (75 and 225 m, not 150 and 300), so the ground's shadows within a few hundred metres get the finer texels; picked by eye on T01 at a low sun.</summary>
    public static readonly (float A, float B, float C) UltraSplits = (0.05f, 0.15f, 0.4f);

    public static bool Blend(bool ultra) => Hook("I76_SHADOW_BLEND") is { } h ? h == "1" : ultra;

    private static string? Hook(string name) => System.Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v.Trim().ToLowerInvariant() : null;
}

public partial class Main
{
    // Shadow Detail Ultra (ShadowDetailRow.UltraOn), and the modern filter and atlas last sent to the renderer for it
    private bool _shadowUltra;
    private static bool? _ultraSent;

    /// <summary>Ultra's part of ApplyModernQuality (the modern look only): the sun's blend and blur, and the filter and atlas when they change.</summary>
    private void ApplyShadowUltra()
    {
        _sun.DirectionalShadowBlendSplits = ShadowUltra.Blend(_shadowUltra);
        _sun.ShadowBlur = ShadowUltra.Blur(_shadowUltra);
        (_sun.DirectionalShadowSplit1, _sun.DirectionalShadowSplit2, _sun.DirectionalShadowSplit3) = ShadowUltra.Splits(_shadowUltra);
        if (_shadowUltra) GD.Print($"shadows: ultra ({ShadowUltra.Filter(true)}, atlas {ShadowUltra.Atlas(true)}, blur {_sun.ShadowBlur}, blend {_sun.DirectionalShadowBlendSplits})");
        if (_shadowFilterSet != false || _ultraSent == _shadowUltra) return;   // the look switch sends the modern ones the first time
        RenderingServer.DirectionalSoftShadowFilterSetQuality(ShadowUltra.Filter(_shadowUltra));
        RenderingServer.DirectionalShadowAtlasSetSize(ShadowUltra.Atlas(_shadowUltra), true);
        _ultraSent = _shadowUltra;
    }
}
