namespace I76.Game.Sim;

/// <summary>
/// The Original Recipe pad's drawing-side choices (I76.Game.Shell.OriginalRecipe), applied with the graphics settings
/// (Main.ApplyGraphics): the mirror's far clip, the brake lamps' hold, the modern look's machine gun rounds, the 1997 sky's
/// drift and the death orbit's turn. Engine-free. The simulation's own (the AI's dropper gate, the live ordnance cap) are the
/// combat's (PlayOptionsCombat).
/// </summary>
public static class OriginalBehavior
{
    public const int Original = 0, Updated = 1;

    /// <summary>A choice that follows the look until the player makes one: Original in the 1997 look, the update in the modern one.</summary>
    public static int Resolve(int? choice, bool look1997) => choice is { } c ? (c == Original ? Original : Updated) : look1997 ? Original : Updated;

    /// <summary>The player's choices (null: follow the look).</summary>
    public static int? MirrorRange, BrakeLamps, MgTracers;
    public static bool SteadySky, SmoothOrbit;

    public static void Apply(I76.Game.Shell.ShellSettings s)
    {
        MirrorRange = s.MirrorRangeChoice;
        BrakeLamps = s.BrakeLampChoice;
        MgTracers = s.MgTracerChoice;
        SteadySky = s.SkyDrift != Original;
        SmoothOrbit = s.DeathOrbit != Original;
    }

    /// <summary>The mirror camera's far clip in a look: the original's 100 m, or Full, as far as the main view's camera.</summary>
    public static float MirrorFar(bool look1997) =>
        Resolve(MirrorRange, look1997) == Original ? MirrorCameraSettings.FarMetres : MirrorCameraSettings.FullFarMetres;

    /// <summary>Whether the brake lamps hold on after the brake lets go (LampSupport.BrakeLit).</summary>
    public static bool BrakeHold(bool look1997) => Resolve(BrakeLamps, look1997) == Updated;

    /// <summary>Whether a machine gun's round is drawn as the original's one-pixel line: always in the 1997 look (the bolts are the modern look's).</summary>
    public static bool LineTracers(bool look1997) => look1997 || Resolve(MgTracers, look1997) == Original;
}
