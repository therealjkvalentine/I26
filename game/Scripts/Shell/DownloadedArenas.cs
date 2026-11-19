using Godot;
using I76.Formats.Mission;
using I76.Formats.Vfs;
using I76.Net;

namespace I76.Game.Shell;

/// <summary>
/// The maps downloaded from network games' hosts (user://downloaded_maps, docs/multiplayer.md "Custom maps") on the host forms' AREA OF
/// PLAY lists (#207): after the install's own arenas of the kind, each named as its file names it and marked "(downloaded)" on the list.
/// A list entry's file is "&lt;hash&gt;/&lt;mission file&gt;" (no arena file holds a '/'), so two downloads of one name, or a download beside an
/// install's own copy of another version, stay apart; hosting one plays it from its folder (the server's <c>MapDir</c>), which sends it on to
/// players who lack it as any custom map.
/// </summary>
public static class DownloadedArenas
{
    public const string Mark = " (downloaded)";

    public static string Root => ProjectSettings.GlobalizePath("user://downloaded_maps");

    /// <summary>The downloads of these mission kinds a host can offer (<see cref="MapCache.ForHost"/>), as list entries.</summary>
    public static List<(string Name, string Msn)> For(GameInstall install, params string[] extensions)
    {
        var o = new List<(string, string)>();
        IReadOnlyList<CachedMap> maps;
        try { maps = new MapCache(Root).ForHost(install, extensions); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { GD.PrintErr($"downloaded maps: {e.Message}"); return o; }
        foreach (var m in maps)
        {
            string? name = null;
            try { name = WorldSettings.ArenaName(new MissionFile(Path.Combine(m.Dir, m.Mission))); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException or IOException)
            {
                GD.PrintErr($"downloaded map {m.Mission}: {ex.Message}");
                continue;
            }
            o.Add((name is { Length: > 0 } n ? n : Path.GetFileNameWithoutExtension(m.Mission), $"{m.HashHex}/{m.Mission}"));
        }
        return o;
    }

    /// <summary>Whether a list entry's file is a download's.</summary>
    public static bool Is(string msn) => msn.IndexOf('/') == 64;

    /// <summary>A list entry's mission file (a download's own name).</summary>
    public static string FileOf(string msn) => Is(msn) ? msn[65..] : msn;

    /// <summary>A download's folder, or null for the install's own arena.</summary>
    public static string? DirOf(string msn) => Is(msn) ? Path.Combine(Root, msn[..64]) : null;

    /// <summary>The install as an entry's arena is read: a download's from its folder.</summary>
    public static GameInstall InstallOf(GameInstall install, string msn) => DirOf(msn) is { } d ? new GameInstall(install.Root) { MapDir = d } : install;
}
