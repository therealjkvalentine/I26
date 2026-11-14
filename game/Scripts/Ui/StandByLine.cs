namespace I76.Game.Ui;

/// <summary>
/// What the small line in a briefing's corner says while its mission loads under it (the port's; StandByTag draws it, MoviePlayer
/// asks): nothing until the picture's first frame; "PLEASE STAND BY . . ." faint while the build still works under the playing
/// picture (no press skips then, MovieSkipGate); READY, as faint and in the same place, once it is done and a new press skips, for the
/// rest of the movie; and "PLEASE STAND BY . . ." in full once the player waits on the mission (a skip kept, or the movie run out
/// or held, before the mission can start). Engine-free so the rule is tested (StandByLineTests).
/// </summary>
public static class StandByLine
{
    public enum Says { Nothing, StandBy, Ready }

    /// <summary>The line's words and whether it is faint, from the movie's state and the load's.</summary>
    /// <param name="pictureUp">The movie's first frame is up (before it, the lettering is made unseen under the black).</param>
    /// <param name="loading">The mission's build still works under the picture: no press skips.</param>
    /// <param name="waitedOn">The player waits on the mission: a skip kept, or the picture held (run out, or stopped after a skip).</param>
    public static (Says Says, bool Faint) For(bool pictureUp, bool loading, bool waitedOn)
    {
        if (waitedOn) return (Says.StandBy, false);
        if (!pictureUp) return (Says.Nothing, true);
        return loading ? (Says.StandBy, true) : (Says.Ready, true);
    }

    /// <summary>The words drawn for <paramref name="says"/>: READY, or the boot screen's own line (<paramref name="standBy"/>, I76's or Nitro's).</summary>
    public static string Text(Says says, string standBy) => says == Says.Ready ? "READY" : standBy;
}
