using Godot;
using I76.Formats.Remix;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// The port's generated scenarios (docs/remix-missions.md): the remix descriptions the port ships, built from the player's
/// install into user://remix (never the install) when missing or stale, and listed after the stock scenarios on the
/// SCENARIO form. GameInstall.UserMissionDir points there, so --mission, the bookmarks and checkpoints find them by name.
/// Base game only: every description uses an Interstate '76 map.
/// </summary>
public static class RemixScenarios
{
    public static string Dir => ProjectSettings.GlobalizePath("user://remix");

    /// <summary>The list's last row but one: a job the director rolls afresh each time it is entered (RollJob).</summary>
    public const string RandomJob = "RANDOM JOB";
    /// <summary>The list's last row: a run of four jobs across the region (RoadTripScreen; docs/remix-missions.md "Road trip").</summary>
    public const string RoadTrip = "ROAD TRIP";

    /// <summary>The last jobs rolled, newest first, one number a line (user://remix/jobs.txt).</summary>
    public static string HistoryFile => System.IO.Path.Combine(Dir, "jobs.txt");
    public const int HistoryLength = 10;

    /// <summary>The last ten jobs rolled, newest first (an unreadable file: none).</summary>
    public static List<int> History()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return [];
            return [.. File.ReadAllLines(HistoryFile).Select(l => int.TryParse(l.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0)
                .Where(n => n > 0).Distinct().Take(HistoryLength)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>A job goes to the top of the history (once), the list cut to ten.</summary>
    public static void Remember(int seed)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllLines(HistoryFile, new[] { seed }.Concat(History().Where(n => n != seed)).Take(HistoryLength)
                .Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { GD.PrintErr($"remix: the job history: {ex.Message}"); }
    }

    /// <summary>
    /// RANDOM JOB's ENTER AREA: the job number typed (its seed: the same job, exactly), or a new seed (the shell's, outside the
    /// simulation: the job's description carries it, and its objective text says it); the director's description for it built
    /// into user://remix as xjob (over the last one), and the number put at the top of the history. The seed, or an error to
    /// log (the launch is then not made).
    /// </summary>
    public static (int Seed, string? Error) RollJob(string? installRoot, int? job = null)
    {
        if (string.IsNullOrEmpty(installRoot)) return (0, "no install");
        int seed = job is > 0 ? job.Value : System.Random.Shared.Next(1, 1_000_000);
        try
        {
            GameInstall.UserMissionDir = Dir;
            var spec = RemixDirector.Roll(seed);
            var yaml = RemixDirector.ToYaml(spec);
            RemixBuilder.Build(new GameInstall(installRoot), RemixSpec.Parse(yaml)).WriteTo(Dir);
            File.WriteAllText(System.IO.Path.Combine(Dir, RemixDirector.JobStem + ".job.yaml"), yaml);   // the description, to roll it again by hand
            GD.Print($"remix: RANDOM JOB seed {seed}: {spec.Objective.Template} on {spec.Base} (i76 remix roll --seed {seed})");
            Remember(seed);
            return (seed, null);
        }
        catch (Exception ex) when (ex is RemixError or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (seed, $"remix: RANDOM JOB seed {seed}: {ex.Message}");
        }
    }

    private static string? _builtFor;
    private static (string Name, string Msn)[] _scenarios = I76.Formats.Mission.MissionTitles.BaseScenarios;

    /// <summary>The scenario form's list: the stock ones, then the generated ones built for the current install.</summary>
    public static (string Name, string Msn)[] Scenarios => _scenarios;

    /// <summary>Points the install at user://remix and builds and lists the generated scenarios for this install (once a process an install).</summary>
    public static void Ensure(string? installRoot)
    {
        GameInstall.UserMissionDir = Dir;
        if (string.IsNullOrEmpty(installRoot) || _builtFor == installRoot) return;
        _builtFor = installRoot;
        _scenarios = I76.Formats.Mission.MissionTitles.BaseScenarios;
        try
        {
            var install = new GameInstall(installRoot);
            if (install.IsNitro) return;
            var specs = RemixCatalogue.Builtin();
            var t0 = Time.GetTicksMsec();
            var (built, errors) = RemixBuilder.BuildAll(install, specs, Dir);
            foreach (var e in errors) GD.PrintErr($"remix: {e}");
            if (built.Count > 0) GD.Print($"remix: built {string.Join(", ", built)} into {Dir} ({Time.GetTicksMsec() - t0} ms)");
            var ids = specs.Select(s => { try { return RemixSpec.Parse(s.Yaml).Id.ToLowerInvariant(); } catch (RemixError) { return null; } }).OfType<string>().ToHashSet();
            _scenarios = [.. I76.Formats.Mission.MissionTitles.BaseScenarios, .. RemixBuilder.Listed(Dir).Where(l => ids.Contains(l.Stem)).Select(l => (l.Title, l.Stem)),
                (RandomJob, RemixDirector.JobStem), (RoadTrip, RemixRun.Stem)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PrintErr($"remix: {ex.Message}");
        }
    }
}
