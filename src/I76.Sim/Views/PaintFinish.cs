namespace I76.Sim.Views;

/// <summary>
/// The port's Hood Gloss (Graphic Detail): how much the modern look's car paint and cockpit hood shine, a level 0 to 100. The
/// original's hood is a flat texture with no reflection; the modern look's clear-coated paint mirrors the sky across it. 0 (Matte),
/// the default, takes the coat off, so the paint shows the original's colours (the sky it mirrors washed an orange paint 7 degrees
/// toward the sky's hue, measured against the 1997 look); 50 (Satin) thins the coat; 100 (Gloss) is the full coat (as before the
/// setting). Between them the shader blends the two neighbouring presets' material values (object.gdshaderinc, i76_hood_gloss).
/// </summary>
public static class PaintFinish
{
    /// <summary>The three named levels: the earlier Hood Finish's Matte, Satin and Gloss.</summary>
    public const int Matte = 0, Satin = 50, Gloss = 100;
    public const int Min = 0, Max = 100, Default = Matte;
    /// <summary>The menu rows' step; the file takes any integer in 0..100.</summary>
    public const int Step = 5;
    /// <summary>The menu rows' levels, 0, 5, ... 100.</summary>
    public static readonly int[] Steps = [.. Enumerable.Range(0, Max / Step + 1).Select(i => i * Step)];
    /// <summary>The presets' names: Matte is the original's look (its hood and paint reflect nothing); the shine is the port's, marked as the pads mark it.</summary>
    public static readonly string[] PresetNames = ["Matte", "Satin*", "Gloss*"];
    private static readonly int[] PresetLevels = [Matte, Satin, Gloss];

    /// <summary>
    /// A level as a row shows it: a preset's name at 0, 50 and 100, else the number, marked as the port's (the original has only
    /// Matte's flat paint). The sheet's value column is about eight characters wide (Monitor*), so "35 (Satin)" would run off it.
    /// </summary>
    public static string Name(int level)
    {
        level = Clamp(level);
        int p = System.Array.IndexOf(PresetLevels, level);
        return p >= 0 ? PresetNames[p] : $"{level}*";
    }

    /// <summary>The menu rows' values, one per <see cref="Steps"/>.</summary>
    public static readonly string[] StepNames = [.. Steps.Select(Name)];

    /// <summary>The nearest preset's name, without the mark: Matte below 25, Satin to 75, Gloss above.</summary>
    public static string NearestPreset(int level) => PresetNames[Clamp(level) switch { < 25 => 0, <= 75 => 1, _ => 2 }].TrimEnd('*');

    public static int Clamp(int level) => System.Math.Clamp(level, Min, Max);

    /// <summary>The menu step nearest a level (a level set by hand in the file shows as the nearest step on the Esc pad).</summary>
    public static int NearestStep(int level) => (Clamp(level) + Step / 2) / Step;

    /// <summary>The main menu row's click: the next step above the level, past 100 back to 0.</summary>
    public static int NextStep(int level) => Clamp(level) >= Max ? Min : (Clamp(level) / Step + 1) * Step;

    /// <summary>The earlier Hood Finish's cfg word ([port] hood_finish) as a level: matte 0, satin 50, gloss 100; null if not one.</summary>
    public static int? FromWord(string? word) => (word ?? "").Trim().ToLowerInvariant() switch
    {
        "matte" => Matte,
        "satin" => Satin,
        "gloss" or "glossy" => Gloss,
        _ => null,
    };

    /// <summary>
    /// shell.cfg's level: [port] hood_gloss (any integer, clamped to 0..100); absent or not a number, the earlier hood_finish's word
    /// (matte 0, satin 50, gloss 100); absent too, the older hood_matte = true's Matte; else the default.
    /// </summary>
    public static int Parse(string? gloss, string? finishWord = null, bool legacyMatte = false)
    {
        if (gloss is not null && double.TryParse(gloss.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g)
            && !double.IsNaN(g))
            return Clamp((int)System.Math.Round(System.Math.Clamp(g, Min, Max)));
        if (FromWord(finishWord) is { } w) return w;
        return legacyMatte ? Matte : Default;
    }

    /// <summary>What shell.cfg's [port] hood_gloss gets: the level, clamped; null (the key left out) at the default. The earlier hood_finish is not written (as lighting_calibration's look_colour_match: read, never written).</summary>
    public static int? FileValue(int level) => Clamp(level) == Default ? null : Clamp(level);

    /// <summary>The shader's global (i76_hood_gloss): the level as 0..1.</summary>
    public static float Shader(int level) => Clamp(level) / (float)Max;

    /// <summary>The material values on a plain painted face (no respray, not glass, not the underside) at a level, as object.gdshaderinc blends them.</summary>
    public readonly record struct Material(float Roughness, float Specular, float Clearcoat, float ClearcoatRoughness, float Metallic, float HubcapCoat);

    /// <summary>
    /// The shader's blend, for the tests and the docs: each value is Matte's, Satin's or Gloss's limit applied to the paint's own value,
    /// mixed linearly between the two presets around the level (0..50 Matte to Satin, 50..100 Satin to Gloss), so 0, 50 and 100 are
    /// the three earlier steps exactly and a level between is a blend of real looks, not of limits that switch on part way.
    /// </summary>
    public static Material Blend(int level, float roughness = 0.4f, float specular = 0.5f, float clearcoat = 1f, float clearcoatRoughness = 0.06f, float metallic = 0f, float hubMetal = 1f)
    {
        float t = Shader(level), a = System.Math.Clamp(t * 2f, 0f, 1f), b = System.Math.Clamp(t * 2f - 1f, 0f, 1f);
        static float Mix(float x, float y, float k) => x + (y - x) * k;
        float Three(float matte, float satin, float gloss) => Mix(Mix(matte, satin, a), gloss, b);
        return new Material(
            Three(System.Math.Max(roughness, 0.88f), System.Math.Max(roughness, 0.65f), roughness),
            Three(System.Math.Min(specular, 0.12f), System.Math.Min(specular, 0.3f), specular),
            Three(0f, System.Math.Min(clearcoat, 0.2f), clearcoat),
            Three(System.Math.Max(clearcoatRoughness, 0.3f), System.Math.Max(clearcoatRoughness, 0.3f), clearcoatRoughness),
            Three(System.Math.Min(metallic, 0.1f), System.Math.Min(metallic, 0.5f), metallic),
            Mix(0f, hubMetal * 0.5f, a));
    }
}
