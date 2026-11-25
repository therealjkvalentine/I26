using I76.Game.Shell;

namespace I76.Game.Boot;

/// <summary>
/// The Nitro Pack's campaign played from Interstate '76's main menu (TRIP, then NITRO; docs/deviations.md "Nitro Pack under
/// TRIP"): the process is handed to the Nitro install at its character choice (<see cref="GameSwitch.ToNitro"/>), the campaign
/// plays as from the Nitro front end, and leaving it (BACK, or a mission's end) hands it back to the base install at its main menu
/// with the TRIP row open (<see cref="GameSwitch.BackFromNitro"/>). One install is open at a time, as with "Switch Game".
/// </summary>
public static class NitroVisit
{
    /// <summary>A visit: the base install the player came from, and the Nitro install played.</summary>
    public sealed record State(string Home, string Guest);

    /// <summary>The visit under way (null: none). Process-wide: it outlives the scene reloads of the Nitro side.</summary>
    public static State? Current { get; internal set; }
    public static bool Active => Current is not null;

    /// <summary>Set by the hand-back: the base main menu comes up with its TRIP row open (taken once).</summary>
    public static bool ReturnToTrip { get; internal set; }

    /// <summary>Takes <see cref="ReturnToTrip"/>: true once after a hand-back.</summary>
    public static bool TakeReturnToTrip()
    {
        bool r = ReturnToTrip;
        ReturnToTrip = false;
        return r;
    }

    /// <summary>
    /// A launch from the visit came back to the shell: hand back to the base game after a mission's end (won, failed or quit),
    /// stay on the Nitro screens when the launch could not be built (its form says why) or the escape menu's Reconfigure asked for the form.
    /// </summary>
    public static bool HandBackAfter(MissionResult result, bool fromLaunch, bool loadError) =>
        fromLaunch && !loadError && result is MissionResult.Won or MissionResult.Failed or MissionResult.Quit;

    /// <summary>A won Nitro campaign mission marks itself done (the Nitro front end's rule, Shell.FirstScreen) before the hand-back.</summary>
    public static bool MarksDone(MissionResult result, GameKind kind) => result == MissionResult.Won && kind == GameKind.Scenario;

    /// <summary>
    /// The Nitro install to offer from the base menu: the first of <paramref name="dirs"/> that is a Nitro Pack install and not the
    /// running one; null when there is none (the entry is not shown).
    /// </summary>
    public static string? PickNitro(IEnumerable<string?> dirs, string running, Func<string, bool> isNitroInstall)
    {
        string Full(string d) => System.IO.Path.GetFullPath(d).TrimEnd('/', '\\');
        var self = Full(running);
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d)) continue;
            try
            {
                if (string.Equals(Full(d), self, StringComparison.OrdinalIgnoreCase)) continue;
                if (isNitroInstall(d)) return d;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return null;
    }

    private static string? _nitro;
    private static string? _lookedFrom;

    /// <summary>The Nitro install beside a running base game (the places "the other game" is looked for: InstallLocator's OtherGameDirs), once per run.</summary>
    public static string? FindNitro(string running)
    {
        if (_lookedFrom == running) return _nitro;
        _lookedFrom = running;
        _nitro = PickNitro(LaunchOptions.OtherGameDirs(), running,
            d => I76.Formats.Vfs.InstallSearch.IsInstall(d) && new I76.Formats.Vfs.GameInstall(d).IsNitro);
        return _nitro;
    }
}
