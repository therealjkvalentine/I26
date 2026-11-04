namespace I76.Game.Boot;

/// <summary>
/// Whether a mission start plays its briefing while the world builds ([port] briefing_while_loading, docs/deviations.md).
/// The original loads the whole mission behind loadscr.pcx, then plays the WRLD briefing, then starts (docs/mission.md's
/// start-up: the intro movie at 0x40371c after the load); here the build runs on a thread under the movie and the rest of the
/// load follows it, so the 5 to 10 s of the build are spent in a briefing of 17 to 158 s. The modern look only: the 1997 look
/// keeps the original's order.
/// </summary>
public static class BriefingOverlap
{
    public static bool Plan(bool setting, bool look1997, string? briefing, bool movieFound, bool skipMovies, bool network, bool editor, bool retry)
        => setting && !look1997 && briefing is { Length: > 0 } && movieFound && !skipMovies && !network && !editor && !retry;
}
