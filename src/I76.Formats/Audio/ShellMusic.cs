namespace I76.Formats.Audio;

/// <summary>
/// Which CD track plays in the shell. The original's shell starts its menu track as it opens and loops it on every screen,
/// CONFIGURE CHASSIS and the trip's garage included; only the credits change it (their own track, then the menu's again).
/// Nothing in the shell goes on from one track to the next. I76's disc: the missions' tracks are 2 to 14 (a mission's music never
/// goes past 14, see <see cref="MissionMusic"/>), the menu's 15, then the title's and the ending's. The Nitro Pack's disc: the menu's
/// 10, the credits' 4, the rest its missions'.
/// Track numbers here are the disc's (CdMusic maps them to music/N.mp3).
/// </summary>
public static class ShellMusic
{
    /// <summary>The menu's track: I76's disc 15 (the GOG shell asks for 13), Nitro's 10.</summary>
    public static int MenuTrack(bool nitro) => nitro ? 10 : 15;

    /// <summary>The credits' track on Nitro's disc (I76's is its shell's 8).</summary>
    public const int NitroCreditsTrack = 4;

    /// <summary>
    /// [port] garage_radio=on: the tracks the 3D garage's boombox picks from, of a disc whose last audio track is <paramref name="lastAudioTrack"/>:
    /// the missions' music only, never the menu's, the credits', the title's or the ending's.
    /// </summary>
    public static IEnumerable<int> RadioTracks(int lastAudioTrack, bool nitro)
    {
        int last = nitro ? lastAudioTrack : Math.Min(lastAudioTrack, MissionMusic.RangeEnd - 1);
        for (int t = MissionMusic.FallbackTrack; t <= last; t++)
            if (!nitro || (t != MenuTrack(true) && t != NitroCreditsTrack)) yield return t;
    }
}
