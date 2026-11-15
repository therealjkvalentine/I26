namespace I76.Game.Ui;

/// <summary>
/// What the small line in a briefing's corner says while its mission loads under it (the port's; StandByTag draws it, MoviePlayer
/// asks): nothing until the picture's first frame; "PLEASE STAND BY . . ." faint while the mission is not ready to start (its build
/// working under the picture, when no press skips, MovieSkipGate; then the rest of the load, which cannot run under a playing
/// picture without stopping it: a press then ends the briefing and the loading card comes back with the line in its corner); READY, as
/// faint and in the same place, only once the mission is ready, so that a press starts it at once; and "PLEASE STAND BY . . ." in
/// full once the player waits on the mission. Under PLEASE STAND BY a thin line as wide as its words fills from the left with the
/// load's progress (LoadProgress); READY has none. Engine-free so the rule is tested (StandByLineTests).
/// </summary>
public static class StandByLine
{
    public enum Says { Nothing, StandBy, Ready }

    /// <summary>The line's words and whether it is faint, from the movie's state and the load's.</summary>
    /// <param name="pictureUp">The movie's first frame is up (before it, the lettering is made unseen under the black).</param>
    /// <param name="ready">The mission can start this moment (the whole load done): a press goes straight into it.</param>
    /// <param name="waitedOn">The player waits on the mission: a skip kept, or the picture held (run out, or stopped after a skip).</param>
    public static (Says Says, bool Faint) For(bool pictureUp, bool ready, bool waitedOn)
    {
        if (waitedOn) return (Says.StandBy, false);
        if (!pictureUp) return (Says.Nothing, true);
        return ready ? (Says.Ready, true) : (Says.StandBy, true);
    }

    /// <summary>The words drawn for <paramref name="says"/>: READY, or the boot screen's own line (<paramref name="standBy"/>, I76's or Nitro's).</summary>
    public static string Text(Says says, string standBy) => says == Says.Ready ? "READY" : standBy;

    /// <summary>
    /// The progress line's thickness at 1080 lines, the window's height scaling it (rounded, never under a pixel): 2 px at 1080 and
    /// 1440, 1 at 720, 3 at 2160.
    /// </summary>
    public const float LinePx = 1.5f;

    /// <summary>The line under the words: under PLEASE STAND BY only (READY: the load is over, nothing is left to show).</summary>
    public static bool LineShown(Says says) => says == Says.StandBy;

    /// <summary>The line's thickness in screen pixels for a window <paramref name="height"/> tall: <paramref name="at1080"/> at 1080, rounded, at least 1.</summary>
    public static int Thickness(float height, float at1080 = LinePx) =>
        System.Math.Max(1, (int)System.Math.Round(at1080 * height / 1080f, System.MidpointRounding.AwayFromZero));

    /// <summary>
    /// How many of the line's <paramref name="width"/> pixels are filled at <paramref name="progress"/> (0 to 1): whole pixels from the
    /// left, rounded down, so the line is full only once the load is (a load short of 1 stops a pixel short).
    /// </summary>
    public static int Filled(double progress, int width)
    {
        if (width <= 0 || !(progress > 0)) return 0;
        if (progress >= 1) return width;
        return System.Math.Min(width - 1, (int)System.Math.Floor(progress * width));
    }

    /// <summary>
    /// The picture, in the SMK's pixels, that the line is laid out on over an upscaled replacement of it: the SMK's height
    /// (<paramref name="smkHeight"/>) at the replacement's aspect (<paramref name="width"/> by <paramref name="height"/>; 4:3 when it
    /// has no size yet), so that the line sits where it does over the SMK, whatever the replacement's resolution.
    /// </summary>
    public static (float X, float Y) ReplacementPicture(float smkHeight, float width, float height)
    {
        float aspect = width > 0 && height > 0 ? width / height : 4f / 3f;
        return (smkHeight * aspect, smkHeight);
    }
}
