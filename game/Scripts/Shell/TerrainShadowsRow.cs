namespace I76.Game.Shell;

/// <summary>
/// Terrain Shadows ([port] terrain_shadows, Graphic Detail > Advanced, the modern look's): the hills' own shadows on the land baked from
/// the drawn ground and the mission's sun as it loads (World/TerrainShade), at every distance and every Shadow Detail, crisp where the
/// ground meets its shadow and softening with the distance to the ridge that casts it. On is the default; Off leaves the land's own shadows to the
/// shadow map (to 600 m at Shadow Detail Low, 1500 m at High). On (the default) bakes at 2.5 m, Fine at 1.25 m. By day only; the 1997 look is unchanged.
/// </summary>
public static class TerrainShadowsRow
{
    public const int Off = 0, On = 1, Fine = 2;
    public static readonly string[] Names = ["Off", "On" + ShellSettings.PortMark, "Fine" + ShellSettings.PortMark];
    public const string Label = "Terrain Shadows";
    private static readonly string[] Words = ["off", "on", "fine"];

    public static int Index(ShellSettings s) => Math.Clamp(s.TerrainShadows, Off, Fine);
    public static void Put(ShellSettings s, int v) => s.TerrainShadows = Math.Clamp(v, Off, Fine);
    public static void Step(ShellSettings s) => s.TerrainShadows = (Index(s) + 1) % Names.Length;

    /// <summary>The sun's texels for a value, metres (0 off).</summary>
    public static float Texel(int v) => v switch { On => 2.5f, Fine => 1.25f, _ => 0f };

    /// <summary>The file's word; one this version does not know is Off (a missing key is On, the default: ShellSettings.Read).</summary>
    public static int FromFile(string? word) => Math.Max(0, Array.IndexOf(Words, word?.Trim().ToLowerInvariant()));
    public static string Word(int v) => Words[Math.Clamp(v, Off, Fine)];
}
