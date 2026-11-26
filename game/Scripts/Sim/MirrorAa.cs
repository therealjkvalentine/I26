using Godot;

namespace I76.Game.Sim;

/// <summary>
/// [port] mirror_aa, Graphic Detail's Mirror AA (Car &amp; Cockpit, the modern look's): the anti-aliasing of the mirrors' own pictures
/// (the rear mirror's render and the door mirrors' own cameras). A SubViewport has its own MSAA and screen-space AA and takes none
/// from the main view, so the mirrors have always been drawn without any: Off, the default, is that. 2x, 4x and 8x are MSAA in the
/// mirror's render, SMAA Godot's edge filter after its tonemap, 2x+SMAA both, As View the main view's Anti-Aliasing row. The 1997
/// look's mirror is the original's, never anti-aliased.
/// </summary>
public static class MirrorAaRow
{
    public static readonly string[] Names = ["Off", "2x*", "4x*", "8x*", "SMAA*", "2x+SMAA*", "As View*"];
    public const int Off = 0, Smaa = 4, Msaa2Smaa = 5, AsView = 6;
    public const int Default = Off;
    private static readonly string?[] Words = [null, "2x", "4x", "8x", "smaa", "2x+smaa", "view"];

    public static int Clamp(int v) => System.Math.Clamp(v, 0, Names.Length - 1);
    /// <summary>The key's word; null for off, the default, which is not written.</summary>
    public static string? Word(int v) => Words[Clamp(v)];
    public static int Parse(string? word)
    {
        int i = System.Array.IndexOf(Words, word?.Trim().ToLowerInvariant());
        return i > 0 ? i : Default;
    }
    public static void Step(I76.Game.Shell.ShellSettings s) => s.MirrorAa = (Clamp(s.MirrorAa) + 1) % Names.Length;

    /// <summary>
    /// The mirror renders' MSAA and screen-space AA for a choice: As View takes the main view's (its MSAA byte, +0x8, and SMAA, from the
    /// settings: the main viewport is set after the mirrors); the 1997 look none; SMAA none on the Compatibility renderer (it has none),
    /// whose MSAA tops at 4x.
    /// </summary>
    public static (Viewport.Msaa Msaa, Viewport.ScreenSpaceAAEnum Screen) Of(int choice, bool look1997, bool compatibility, int viewMsaa, bool viewSmaa)
    {
        if (look1997) return (Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Disabled);
        var (msaa, smaa) = Clamp(choice) switch
        {
            1 => (1, false),
            2 => (2, false),
            3 => (3, false),
            Smaa => (0, true),
            Msaa2Smaa => (1, true),
            AsView => (viewMsaa, viewSmaa),
            _ => (0, false),
        };
        return (I76.Game.Boot.RenderScale.Msaa(msaa, compatibility),
            smaa && !compatibility ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled);
    }

    /// <summary>A mirror's viewport set to it, only on a change (a change remakes its buffers).</summary>
    public static void Apply(Viewport vp, (Viewport.Msaa Msaa, Viewport.ScreenSpaceAAEnum Screen) aa)
    {
        if (vp.Msaa3D != aa.Msaa) vp.Msaa3D = aa.Msaa;
        if (vp.ScreenSpaceAA != aa.Screen) vp.ScreenSpaceAA = aa.Screen;
    }
}
