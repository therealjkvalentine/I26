using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Anti-Aliasing's SMAA* and 2x+SMAA* (AntiAliasingRow): Godot's SMAA 1x, a post-process edge filter after the tonemap, on the main
/// view in the modern look; it also catches the edges MSAA does not (shader edges, alpha-tested cards, specular), at a slight softening.
/// The 1997 look and the mirrors draw none. The Compatibility renderer has no SMAA (Godot's docs: Forward+ and Mobile only): none there.
/// I76_SCREEN_AA=off|fxaa|smaa overrides the row for a run (comparison captures).
/// </summary>
public static class ScreenAa
{
    public static Viewport.ScreenSpaceAAEnum Mode(bool look1997, bool smaa, bool compatibility, string? hook = null)
    {
        if (look1997 || compatibility) return Viewport.ScreenSpaceAAEnum.Disabled;
        return hook?.Trim().ToLowerInvariant() switch
        {
            "off" or "0" => Viewport.ScreenSpaceAAEnum.Disabled,
            "fxaa" => Viewport.ScreenSpaceAAEnum.Fxaa,
            "smaa" or "1" => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => smaa ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled,
        };
    }

    /// <summary>The main view's, only on a change (a change remakes post-process buffers).</summary>
    public static void Apply(Viewport vp, bool look1997, bool smaa)
    {
        var m = Mode(look1997, smaa, RenderBackend.Compatibility, System.Environment.GetEnvironmentVariable("I76_SCREEN_AA"));
        if (vp.ScreenSpaceAA != m) vp.ScreenSpaceAA = m;
    }
}
