namespace I76.Game.Boot;

/// <summary>
/// Whether a mission start plays its briefing while the world builds ([port] briefing_while_loading, docs/deviations.md). The
/// original loads the whole mission behind its loading card, then plays the WRLD briefing, then starts (re/subsystems/mission.md:
/// the movie at 0x40371c after the load). Here, as upstream's 0.12 does, the card comes up, holds a while ([port] briefing_card_hold,
/// <see cref="DefaultCardHold"/>; upstream's <see cref="UpstreamCardHold"/>) with the build already going behind it, goes to black
/// and the briefing plays while the build goes on; a skip before the mission is ready is kept under "PLEASE STAND BY" (StandByTag)
/// and the mission starts by itself when it is. The port's: no press skips while the build's thread works (the card, or the line
/// faint over the playing picture), nor is kept for later; a new one after does (MovieSkipGate: the card comes back with its progress
/// for the rest of the load), and the line says READY only once the mission is ready, when a press starts it at once.
/// Off: the original's order.
/// </summary>
public static class BriefingOverlap
{
    /// <summary>Upstream's 0.12 hold of the loading card before a briefing played over the build (after its fade-in, before its fade-out), seconds (its Main.CardHold).</summary>
    public const double UpstreamCardHold = 0.7;

    /// <summary>
    /// The port's: the loading card's hold before the briefing (after its fade-in, before its fade-out), seconds: long enough to read
    /// "SOMEWHERE IN THE SOUTHWEST..." as the original's card is up for its load (5.0 s in T01 on a P166), short of the original's whole
    /// load; the build runs behind it from the start, so it costs nothing a build longer than it does not already take.
    /// </summary>
    public const double DefaultCardHold = 3.0;

    /// <summary>The longest hold [port] briefing_card_hold takes, seconds.</summary>
    public const double MaxCardHold = 10;

    /// <summary>[port] briefing_card_hold: seconds from 0 (straight to black) to <see cref="MaxCardHold"/>; absent or not a number, <see cref="DefaultCardHold"/>.</summary>
    public static double ParseHold(string? value) =>
        double.TryParse(value?.Trim().Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? System.Math.Clamp(v, 0, MaxCardHold) : DefaultCardHold;

    /// <summary>
    /// [port] briefing_while_loading: off for <c>false</c> (upstream's shell.cfg writes the bool), <c>off</c>, <c>0</c> or
    /// <c>no</c>; on otherwise (absent, <c>true</c>, <c>on</c>, and the fork's older <c>card_first</c> and <c>movie_first</c>).
    /// </summary>
    public static bool Parse(string? value) =>
        value?.Trim().Trim('"').ToLowerInvariant() is not ("false" or "off" or "0" or "no");

    /// <summary>
    /// The world builds on its own thread: a single-player start in the modern look (the 1997 look builds on the main thread,
    /// its modern share deferred), not a checkpoint's retry, the map editor or a network game.
    /// </summary>
    public static bool Threaded(bool look1997, bool network, bool editor, bool retry) => !look1997 && !network && !editor && !retry;

    /// <summary>
    /// The rest of the load (the world into the tree, the look, the cars, the first tick) runs under the playing briefing too: the
    /// setting ([port] load_under_briefing), unless the hook I76_ATTACH_UNDER_MOVIE says otherwise (0 off, 1 on).
    /// </summary>
    public static bool LoadUnder(bool setting, string? hook) => hook switch { "0" => false, "1" => true, _ => setting };

    /// <summary>The briefing plays during the build: the setting on, a threaded build, a briefing movie that is there and movies not skipped.</summary>
    public static bool Plan(bool setting, bool threaded, string? briefing, bool movieFound, bool skipMovies)
        => setting && threaded && briefing is { Length: > 0 } && movieFound && !skipMovies;
}

/// <summary>
/// The screens of a mission start, a step a frame (Main.BuildBehindCard): <see cref="Stage.Card"/> while the card leads (the build
/// already going behind it), <see cref="Stage.Movie"/> while the briefing plays (a skip or its end kept under "PLEASE STAND BY" until
/// the mission is ready), <see cref="Stage.CardAgain"/> only when the movie ended before the build was done (it could not be played),
/// then <see cref="Stage.Attach"/> (the rest of the load). A build done while the card is up goes on to the briefing as any other; a
/// new press on the card once it is done (none counts while it builds: MovieSkipGate) skips the briefing, the card staying up for
/// the rest of the load. With no briefing the card holds until the build is done, as the original's is up for its load (Main builds
/// such a start on the main thread).
/// </summary>
public sealed class CardSequence(bool briefing, double holdSeconds)
{
    public enum Stage { Card, Movie, CardAgain, Attach }

    public Stage Now { get; private set; } = Stage.Card;

    /// <summary>The movie was started (it starts once, when the stage first becomes <see cref="Stage.Movie"/>).</summary>
    public bool MovieShown { get; private set; }

    /// <summary>The briefing was skipped on the card, before it began (<see cref="Step"/>'s <c>skipped</c>).</summary>
    public bool SkippedOnCard { get; private set; }

    /// <summary>
    /// The next stage, from the seconds since the card came up (<paramref name="cardSeconds"/>), whether the build is done, whether
    /// the movie is over (ended: with the mission ready, or because it could not play) and whether a skip was pressed on the card
    /// (<paramref name="skipped"/>: a new press once the build was done; the caller drops those made while it builds).
    /// </summary>
    public Stage Step(double cardSeconds, bool built, bool movieOver, bool skipped = false)
    {
        switch (Now)
        {
            case Stage.Card:
                if (briefing && skipped && built) { SkippedOnCard = true; Now = Stage.Attach; }
                else if (briefing) { if (cardSeconds >= holdSeconds) Now = Stage.Movie; }
                else if (built) Now = Stage.Attach;
                break;
            case Stage.Movie:
                if (movieOver) Now = built ? Stage.Attach : Stage.CardAgain;
                break;
            case Stage.CardAgain:
                if (built) Now = Stage.Attach;
                break;
        }
        if (Now == Stage.Movie) MovieShown = true;
        return Now;
    }
}
