using Godot;

namespace I76.Game.Boot;

/// <summary>
/// Debanding in the modern look: Godot's dither after the tonemap, on the main view and the mirrors, so the sky's gradients
/// at dusk and night and the sky fog's fade show no 8-bit steps. The 1997 look draws none, as before. (Anisotropy is the
/// project's 16x in both looks: project.godot's level 4 is 2^4.)
/// </summary>
public static class LookFilters
{
    public static void Apply(Viewport vp, bool look1997)
    {
        if (vp.UseDebanding == look1997) vp.UseDebanding = !look1997;
    }
}
