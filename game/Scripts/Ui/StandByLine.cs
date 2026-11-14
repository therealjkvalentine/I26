namespace I76.Game.Ui;

/// <summary>
/// What the small line in a briefing's corner says while its mission loads under it (the port's; StandByTag draws it, MoviePlayer
/// asks): nothing until the picture's first frame; "PLEASE STAND BY . . ." faint while the mission is not ready to start (its build
/// working under the picture, when no press skips, MovieSkipGate; then the rest of the load, which cannot run under a playing
/// picture without stopping it: a press then ends the briefing and the loading card comes back with its progress wheel); READY, as
/// faint and in the same place, only once the mission is ready, so that a press starts it at once; and "PLEASE STAND BY . . ." in
/// full once the player waits on the mission. Engine-free so the rule is tested (StandByLineTests).
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
}
