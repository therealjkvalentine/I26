namespace I76.Game.Sim;

/// <summary>
/// The drawing-side settings without a row (shell.cfg's [port] keys, docs/playing.md; the words are I76.Game.Shell.OriginalRecipe's),
/// applied with the graphics settings (Main.ApplyGraphics): the mirror's far clip, the brake lamps' hold, the modern look's
/// machine gun rounds, the 1997 sky's drift, the death orbit's turn and the deaths by cause. Engine-free. The simulation's own (the live ordnance
/// cap, Play Options' Unlim. Ordnance) is the combat's (PlayOptionsCombat).
/// </summary>
public static class OriginalBehavior
{
    public const int Original = 0, Updated = 1;

    /// <summary>A choice that follows the look until the player makes one: Original in the 1997 look, the update in the modern one.</summary>
    public static int Resolve(int? choice, bool look1997) => I76.Formats.Shell.LookChoice.Resolve(choice, look1997);

    /// <summary>The player's choices (null: follow the look).</summary>
    public static int? MirrorRange, BrakeLamps, MgTracers;
    /// <summary>The sky steps once a tick whatever the mirror does (the default; [port] sky_drift=original gives the original's second step), and the orbit's smooth turn.</summary>
    public static bool SteadySky = true, SmoothOrbit;
    /// <summary>The 1997 look's Early Tick: a change of the keys runs the next tick at once ([port] early_tick, on unless false; SimDriver, TickPacer).</summary>
    public static bool EarlyTick = true;
    /// <summary>Draw Ahead: the player's car drawn at the newest tick carried forward, not a tick behind ([port] draw_ahead, off unless true; SimDriver, CarNode).</summary>
    public static bool DrawAhead;
    /// <summary>The 1997 look takes the Anti-Aliasing row ([port] aa_1997, on unless false); off, no multisampling, as the 3D card drew it.</summary>
    public static bool Antialias1997 = true;
    /// <summary>The 1997 look's even flash: each gun's one flash sprite lit again at the shot's place in its tick ([port] even_flash, on unless false).</summary>
    public static bool EvenFlash = true;

    public static void Apply(I76.Game.Shell.ShellSettings s)
    {
        MirrorRange = s.MirrorRangeChoice;
        BrakeLamps = s.BrakeLampChoice;
        MgTracers = s.MgTracerChoice;
        SteadySky = s.SkyDrift != Original;
        SmoothOrbit = s.DeathOrbit != Original;
        EarlyTick = s.EarlyTick;
        DrawAhead = s.DrawAhead;
        Antialias1997 = s.Antialias1997;
        EvenFlash = s.EvenFlash;
        DeathVariety.Enabled = s.DeathVariety;
        DamageView.MovieIgnition = s.MovieIgnition;
    }

    /// <summary>
    /// The mirror camera's far clip in a look: the original's 100 m, or Full, as far as the main view's camera. The modern look keeps
    /// Full: with the mirror every second frame and without SSAO (docs/perf/plan.md item 2), Full measured 0.2 ms over the 100 m
    /// (GPU frame means, the T01 cockpit, 3440x1440 and 1920x1080), and the 100 m loses the mesas behind the car even faded into the sky.
    /// </summary>
    public static float MirrorFar(bool look1997) =>
        Resolve(MirrorRange, look1997) == Original ? MirrorCameraSettings.FarMetres : MirrorCameraSettings.FullFarMetres;

    /// <summary>Whether the brake lamps hold on after the brake lets go (LampSupport.BrakeLit).</summary>
    public static bool BrakeHold(bool look1997) => Resolve(BrakeLamps, look1997) == Updated;

    /// <summary>Whether a machine gun's round is drawn as the original's one-pixel line: always in the 1997 look (the bolts are the modern look's).</summary>
    public static bool LineTracers(bool look1997) => look1997 || Resolve(MgTracers, look1997) == Original;
}
