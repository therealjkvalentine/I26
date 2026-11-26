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
            _scenarios = [.. I76.Formats.Mission.MissionTitles.BaseScenarios, .. RemixBuilder.Listed(Dir).Where(l => ids.Contains(l.Stem)).Select(l => (l.Title, l.Stem))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            GD.PrintErr($"remix: {ex.Message}");
        }
    }
}
