namespace I76.Game.Boot;

/// <summary>
/// What a launch does when the car it asked for (--drive X.vcf, a shell car, the Developer panel's) is not in the install. A mission
/// built without its player's car has no chase camera and no sim player, so the game used to run on, throwing every frame; now a
/// scripted run (a dev or capture launch: --mission on the command line, --quit-after, --screenshot) stops with a message and a
/// non-zero exit code, and an interactive launch plays the mission's own car with a warning.
/// </summary>
public static class MissingCarPolicy
{
    public enum Outcome { Load, Fallback, Fail }

    /// <summary>The car file name as it is looked up: ".vcf" added when the name has none; null for no car asked.</summary>
    public static string? FileName(string? requested)
        => string.IsNullOrEmpty(requested) ? null : requested.EndsWith(".vcf", System.StringComparison.OrdinalIgnoreCase) ? requested : requested + ".vcf";

    /// <summary>No car asked, or the file is there: load. Missing: fail a scripted run, else fall back to the mission's default car.</summary>
    public static Outcome Decide(string? requested, bool exists, bool scripted)
        => string.IsNullOrEmpty(requested) || exists ? Outcome.Load : scripted ? Outcome.Fail : Outcome.Fallback;

    /// <summary>The one log line naming the file and where it was looked for.</summary>
    public static string Message(string file, string lookedIn, Outcome outcome)
        => $"I26: car {file} is not in {lookedIn}"
           + (outcome == Outcome.Fail ? "; stopping (a scripted run needs the car it names)" : "; playing the mission's own car instead");
}
