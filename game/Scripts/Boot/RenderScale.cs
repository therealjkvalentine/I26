using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Graphic Detail's Render Scale: the 3D view drawn at 50 to 200 % of the window's size in each direction and scaled to it (the
/// viewport's 3D scaling). Below 100, fewer pixels to shade for a GPU that cannot fill the window (the ground, the horizon ring and
/// the sky are shaded per pixel, so the frame's cost falls nearly with the pixel count), scaled up by AMD FSR 1.0 (bilinear on the
/// Compatibility renderer, which has no FSR) with the texture mip bias the engine recommends for the scale, so the ground keeps its
/// detail; above 100, supersampling, a cleaner picture for a GPU with room to spare. The 2D layers (the HUD, the menus) are not
/// scaled. 100 % is the engine's own, as before. Also the Anti-Aliasing row's 8x step (4x on the Compatibility renderer, which may lack it).
/// </summary>
public static class RenderScale
{
    /// <summary>Percent, per choice, in the row's order; <see cref="Default"/> is 100.</summary>
    public static readonly int[] Percents = [50, 67, 75, 85, 100, 150, 200];
    /// <summary>The row's values: 100 % is the original's (the view drawn at the window's own size); the rest are marked as the port's.</summary>
    public static readonly string[] Names = ["50%*", "67%*", "75%*", "85%*", "100%", "150%*", "200%*"];
    public static readonly int Default = System.Array.IndexOf(Percents, 100);

    /// <summary>The choice for a shell.cfg [port] render_scale value (percent); anything not offered is 100.</summary>
    public static int Parse(int? percent) => percent is { } p && System.Array.IndexOf(Percents, p) is var i and >= 0 ? i : Default;

    public static float Scale(int choice) => Percents[System.Math.Clamp(choice, 0, Percents.Length - 1)] / 100f;

    /// <summary>The Anti-Aliasing row's choices: Off, 2x, 4x (the original's) and the port's 8x.</summary>
    public static readonly string[] AntiAliasing = ["Off", "2x", "4x", "8x"];

    /// <summary>The engine's MSAA for a row's choice; 8x is 4x where the renderer has no 8x (Compatibility, where it may be missing).</summary>
    public static Viewport.Msaa Msaa(int choice, bool compatibility) => choice switch
    {
        <= 0 => Viewport.Msaa.Disabled,
        1 => Viewport.Msaa.Msaa2X,
        2 => Viewport.Msaa.Msaa4X,
        _ => compatibility ? Viewport.Msaa.Msaa4X : Viewport.Msaa.Msaa8X,
    };

    /// <summary>Sets the main window's 3D scale, live: FSR 1.0 up from below 100 % (bilinear where the renderer has none), bilinear down from above.</summary>
    public static void Apply(I76.Game.Shell.ShellSettings s)
    {
        if (Engine.GetMainLoop() is not SceneTree { Root: { } root }) return;
        float scale = Scale(s.RenderScale);
        root.Scaling3DMode = scale < 1f && !RenderBackend.Compatibility ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        root.Scaling3DScale = scale;
        // no mip bias of our own: the engine adds log2 of the 3D scale to Viewport.texture_mipmap_bias itself
    }
}
