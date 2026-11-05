namespace I76.Game.Boot;

/// <summary>
/// Which leads at a mission start while the world builds on a thread of its own ([port] briefing_while_loading,
/// docs/deviations.md): the loading card ("SOMEWHERE IN THE SOUTHWEST...") for the retail game's time, then the briefing
/// (<see cref="CardFirst"/>, the default, the original's order), or the briefing at once with the card only after it
/// (<see cref="MovieFirst"/>). The build runs behind both.
/// </summary>
public enum CardOrder { CardFirst, MovieFirst }

/// <summary>
/// A mission start's plan: whether the world builds on a thread behind the card and the briefing, and in which order they
/// show. The original loads the whole mission behind loadscr.pcx, then plays the WRLD briefing, then starts (re/subsystems/
/// mission.md: the intro movie at 0x40371c after the load), so its card is up as long as its load takes: on the emulated
/// box-spec P166 (docs/perf/loadtime.md) <see cref="RetailCardSeconds"/>. Here the build starts at once on its thread, the card
/// holds that long, then the briefing plays while the build goes on; a briefing over before the build is done shows the card
/// until the mission is ready.
/// </summary>
public static class BriefingOverlap
{
    /// <summary>
    /// How long the retail game (1.0, March 1997) shows the card on its box-spec machine, from the card's first frame to the
    /// briefing's: T01 5.7 s and T05 7.4 s on the lab's 86Box P166 (Windows 95, the software renderer), in guest time
    /// (docs/perf/loadtime.md, "The retail card"); the card itself is up 5.0 and 6.5 s of it, its palette ramp in and out
    /// included, then black until the movie's first frame. The original's time is its load's, so it differs by mission; this
    /// is the two's mean.
    /// </summary>
    public const double RetailCardSeconds = 6.5;

    /// <summary>[port] briefing_while_loading: <c>movie_first</c>, else (<c>card_first</c>, absent, or the old on/off/true/false) the card first.</summary>
    public static CardOrder Parse(string? value) =>
        string.Equals(value?.Trim().Trim('"'), "movie_first", System.StringComparison.OrdinalIgnoreCase) ? CardOrder.MovieFirst : CardOrder.CardFirst;

    public static string Name(CardOrder o) => o == CardOrder.MovieFirst ? "movie_first" : "card_first";

    /// <summary>
    /// The world builds on its own thread: a single-player start in the modern look (the 1997 look builds on the main thread,
    /// its modern share deferred), not a checkpoint's retry, the map editor or a network game.
    /// </summary>
    public static bool Threaded(bool look1997, bool network, bool editor, bool retry) => !look1997 && !network && !editor && !retry;

    /// <summary>The briefing plays during the build: a threaded build, a briefing movie that is there and movies not skipped.</summary>
    public static bool Plan(bool threaded, string? briefing, bool movieFound, bool skipMovies)
        => threaded && briefing is { Length: > 0 } && movieFound && !skipMovies;
}

/// <summary>
/// The screens of a mission start, a step a frame (Main.BuildBehindCard): <see cref="Stage.Card"/> while the card leads,
/// <see cref="Stage.Movie"/> while the briefing plays, <see cref="Stage.CardAgain"/> after it until the build is done, then
/// <see cref="Stage.Attach"/> (the rest of the load, on the main thread behind the card). With no briefing the card holds until
/// the build is done and at least the retail time.
/// </summary>
public sealed class CardSequence(CardOrder order, bool briefing, double holdSeconds = BriefingOverlap.RetailCardSeconds)
{
    public enum Stage { Card, Movie, CardAgain, Attach }

    public Stage Now { get; private set; } = briefing && order == CardOrder.MovieFirst ? Stage.Movie : Stage.Card;

    /// <summary>The movie was started (it starts once, when the stage first becomes <see cref="Stage.Movie"/>).</summary>
    public bool MovieShown { get; private set; }

    /// <summary>
    /// The next stage, from the seconds since the card came up (<paramref name="cardSeconds"/>), whether the build is done and
    /// whether the movie is over (ended or skipped).
    /// </summary>
    public Stage Step(double cardSeconds, bool built, bool movieOver)
    {
        switch (Now)
        {
            case Stage.Card:
                if (briefing) { if (cardSeconds >= holdSeconds) Now = Stage.Movie; }
                else if (built && cardSeconds >= holdSeconds) Now = Stage.Attach;
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
