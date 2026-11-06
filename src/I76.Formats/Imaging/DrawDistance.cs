namespace I76.Formats.Imaging;

/// <summary>
/// The port's draw distance (shell.cfg's [port] draw_distance: Graphic Detail's Visibility Range row), independent of
/// the look: Original draws terrain and objects only to the original's far clip (the world camera rebuild: 600 with Visibility
/// Range on) with the sky's horizon beyond; Far draws to 20 km as this port always did; Faded draws to 2300 m and fades the ground
/// into the horizon from 1725 m, as the patch kit i76-everywhere draws the original (its I76_FAR_CLIP 2300 with I76_GLIDE_FOG 0.75 in
/// the horizon strips' colour). View only: the game's own ranges (AI, weapons, visibility checks) are unaffected.
/// </summary>
public static class DrawDistance
{
    /// <summary>The stored values: 0 the original's 600 m, 1 the whole map (20 km), 2 the patch kit's 2300 m faded.</summary>
    public const int Original = 0, Far = 1, Faded = 2;

    /// <summary>The original's far clip, metres from the eye.</summary>
    public const float OriginalClip = 600f;
    public const float FarClip = 20000f;
    /// <summary>Faded: the far clip, and where the fade into the horizon begins (0.75 of it, the patch kit's I76_GLIDE_FOG start).</summary>
    public const float FadedClip = 2300f, FadedFogBegin = 1725f;
    /// <summary>The modern look's fade at Faded ends a little inside the clip, as at Original (560 of 600).</summary>
    public const float FadedFogEnd = 2240f;
    /// <summary>The modern look's distance fade at Original: clear to FogBegin, wholly sky by FogEnd, a little inside the clip so no edge shows.</summary>
    public const float FogBegin = 200f, FogEnd = 560f;

    /// <summary>A stored value as a setting: anything unknown or missing is Far, the default.</summary>
    public static int Parse(int? raw) => raw is Original ? Original : raw is Faded ? Faded : Far;

    /// <summary>
    /// The setting in a look: the player's choice, or with none made (no key in the settings file) the look's default,
    /// Original in the 1997 look (the original's clip and horizon panels), Far in the modern one.
    /// </summary>
    public static int Resolve(int? choice, bool look1997) => choice is { } c ? Parse(c) : I76.Formats.Shell.LookChoice.Resolve(null, look1997);

    /// <summary>
    /// A stored draw_distance as a choice, by the settings file's version: 0.11.0's Save wrote the key on every save (Far, the
    /// only default then), so a Far from a file older than version 12 is no choice and the look's default applies.
    /// </summary>
    public static int? FromFile(int? raw, int settingsVersion) => raw is null || (Parse(raw) == Far && settingsVersion < 12) ? null : Parse(raw);

    /// <summary>The camera's far plane for a setting.</summary>
    public static float CameraFar(int setting) => setting switch { Original => OriginalClip, Faded => FadedClip, _ => FarClip };

    /// <summary>Whether the 1997 look fades the ground into the horizon colour (Faded): from <see cref="FadedFogBegin"/> to the clip.</summary>
    public static bool Fade1997(int setting) => setting == Faded;

    /// <summary>The modern look's fade into the sky, from and to (at Original and Faded).</summary>
    public static (float Begin, float End) ModernFogRange(int setting) => setting == Faded ? (FadedFogBegin, FadedFogEnd) : (FogBegin, FogEnd);

    /// <summary>Whether the modern look fades its distance into the sky (it hides the clip at Original; at Far nothing is clipped).</summary>
    public static bool ModernFog(int setting) => setting != Far;

    /// <summary>
    /// Whether the modern sky draws the mission's horizon panels: at Original, in place of the 3D ring, and only for a mission that has panels
    /// (a Nitro arena may name none, n01 and n03 among them; the unset texture array would read as white).
    /// </summary>
    public static bool ModernPanels(int setting, bool missionHasPanels) => ModernFog(setting) && missionHasPanels;
}
