namespace I76.Game.Shell;

/// <summary>
/// Graphic Detail's Anti-Aliasing: the MSAA in the graphics block's +0x8 (Off, 2x, 4x, 8x*) and, the port's, SMAA* alone or with 2x
/// ([port] screen_aa = smaa, written only then). SMAA keeps +0x8 at the MSAA under it (Off, or 2x), so a build without the key reads
/// the file as Off or 2x and drops the key when it saves. The key counts only over Off or 2x: a byte of 4x or 8x (set by such a build
/// since) is that, without SMAA. The modern look's main view only (ScreenAa); the 1997 look takes the MSAA alone.
/// </summary>
public static class AntiAliasingRow
{
    public static readonly string[] Names = [.. I76.Game.Boot.RenderScale.AntiAliasing, "SMAA" + ShellSettings.PortMark, "2x+SMAA" + ShellSettings.PortMark];
    /// <summary>The first SMAA choice: the row's index over it is SMAA plus that MSAA byte (0 Off, 1 2x).</summary>
    public const int Smaa = 4;

    public static bool SmaaOn(ShellSettings s) => s.Smaa && s.Graphics[0x8] <= 1;
    public static int Index(ShellSettings s) => SmaaOn(s) ? Smaa + s.Graphics[0x8] : System.Math.Clamp((int)s.Graphics[0x8], 0, Smaa - 1);

    public static void Put(ShellSettings s, int v)
    {
        v = System.Math.Clamp(v, 0, Names.Length - 1);
        s.Smaa = v >= Smaa;
        s.Graphics[0x8] = (byte)(v >= Smaa ? v - Smaa : v);
    }

    public static void Step(ShellSettings s) => Put(s, (Index(s) + 1) % Names.Length);

    /// <summary>[port] screen_aa's word: "smaa" is on; anything else, or none, off.</summary>
    public static bool FromFile(string? word) => string.Equals(word?.Trim(), "smaa", System.StringComparison.OrdinalIgnoreCase);
    public const string Word = "smaa";
}

/// <summary>
/// Graphic Detail's Shadow Detail: the graphics block's +0x6 (Off, Low, High) and, the port's, Ultra* above High ([port] shadow_detail
/// = ultra, written only then). Ultra keeps +0x6 at High (its splits and reach), so a build without the key reads the file as High and
/// drops the key when it saves; the key counts only over High. The modern look only (ShadowUltra: filter, atlas, blended splits).
/// </summary>
public static class ShadowDetailRow
{
    public static readonly string[] Names = ["Off", "Low", "High", "Ultra" + ShellSettings.PortMark];
    public const int Ultra = 3;

    public static bool UltraOn(ShellSettings s) => s.ShadowUltra && s.Graphics[0x6] == 2;
    public static int Index(ShellSettings s) => UltraOn(s) ? Ultra : System.Math.Clamp((int)s.Graphics[0x6], 0, 2);

    public static void Put(ShellSettings s, int v)
    {
        v = System.Math.Clamp(v, 0, Ultra);
        s.ShadowUltra = v == Ultra;
        s.Graphics[0x6] = (byte)System.Math.Min(v, 2);
    }

    public static void Step(ShellSettings s) => Put(s, (Index(s) + 1) % Names.Length);

    /// <summary>[port] shadow_detail's word: "ultra" is on; anything else, or none, the byte alone.</summary>
    public static bool FromFile(string? word) => string.Equals(word?.Trim(), "ultra", System.StringComparison.OrdinalIgnoreCase);
    public const string Word = "ultra";
}
