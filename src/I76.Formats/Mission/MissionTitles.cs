using System.Text;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Formats.Mission;

/// <summary>One map of the editor's list: its stem (t05), the title the game shows for it and the list it belongs to.</summary>
public sealed record MapListing(string Stem, string Title, string Group)
{
    /// <summary>"Jorczak's Peak (M14)"; the code alone when there is no title.</summary>
    public string Label => Title.Length == 0 || Title.Equals(Stem, StringComparison.OrdinalIgnoreCase) ? Stem.ToUpperInvariant() : $"{Title} ({Stem.ToUpperInvariant()})";
}

/// <summary>
/// The titles of the install's stock missions, from the places the game takes them from:
/// the base melee lobby's own tables (15 arenas and 7 scenarios); the
/// Nitro lobby's callback +0x58, which reads WRLD + 0x143 of each file; Nitro's campaign list, addon\SCENARIO.DAT. The base
/// campaign has no titles the original shows: the trip numbers its missions ("Scene N." on the bookmark board) and A01 is
/// the main menu's Training. The port names each scene by what happens in it (<see cref="SceneNames"/>), "Scene 5: Save Taurus".
/// </summary>
public static class MissionTitles
{
    public const string Campaign = "Campaign", Melee = "Melee arenas", Nitro = "Nitro";

    public static readonly (string Name, string Msn)[] BaseArenas =
    [
        ("The Crater", "m01"), ("Dunes", "m02"), ("Air Base", "m03"), ("Suburbia", "m04"), ("Slick Track", "m05"),
        ("Tombstone", "m06"), ("Hope Springs", "m07"), ("Creeper Canyon", "m08"), ("Mesa Maze", "m09"), ("Salt Flats", "m10"),
        ("Vigilantes' Paradise", "m11"), ("Night Driver", "m12"), ("Dodge", "m13"), ("Jorczak's Peak", "m14"), ("A Lot of Asphalt", "m15"),
    ];

    /// <summary>In list order, which is not file order.</summary>
    public static readonly (string Name, string Msn)[] BaseScenarios =
    [
        ("Oil Well Well", "s03"), ("Mondo Burger", "s02"), ("Seminole", "s04"), ("Pretty Bus", "s05"),
        ("Race No Shoot", "s06"), ("Trailer Pk. Madness", "s01"), ("Milk Toast", "s07"),
    ];

    /// <summary>The stock missions of the install, grouped (campaign, melee arenas or Nitro) and in the game's own order.</summary>
    public static List<MapListing> Stock(GameInstall install)
    {
        var dir = install.MissionDir;
        var stems = dir is null ? [] : Directory.EnumerateFiles(dir).Where(GameInstall.IsMissionFile)
            .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).Distinct().ToList();
        var o = new List<(string Group, int Rank, string Sort, MapListing L)>();
        ScenarioDat? campaign = null;
        if (install.IsNitro && install.Resolve("addon", "SCENARIO.DAT") is { } sc)
        {
            try { campaign = ScenarioDat.Parse(File.ReadAllText(sc, Encoding.Latin1)); }
            catch (IOException) { }
        }
        foreach (var stem in stems)
        {
            string group, title = ""; int rank;
            int num = stem.Length >= 3 && int.TryParse(stem.AsSpan(1), out int n) ? n : 0;
            if (install.IsNitro)
            {
                group = Nitro;
                var listed = campaign?.Characters.SelectMany(c => c.Missions).Select((m, i) => (m, i)).FirstOrDefault(x => x.m.Stem.Equals(stem, StringComparison.OrdinalIgnoreCase));
                if (listed?.m is { } lm) { title = lm.Title; rank = listed.Value.i; }
                else { title = FileName(install, stem); rank = stem[0] switch { 'b' => 100, 'p' => 101, _ => 200 }; }
            }
            else if (stem[0] is 'a' or 't') { group = Campaign; rank = stem[0] == 'a' ? 0 : 1; title = stem[0] == 't' ? TripTitle(num) : TrainingName; }
            else
            {
                group = Melee;
                int ai = Array.FindIndex(BaseArenas, a => a.Msn == stem), si = Array.FindIndex(BaseScenarios, a => a.Msn == stem);
                if (ai >= 0) { title = BaseArenas[ai].Name; rank = ai; }
                else if (si >= 0) { title = BaseScenarios[si].Name; rank = 100 + si; }
                else { title = FileName(install, stem); rank = 200; }
            }
            o.Add((group, rank, stem, new MapListing(stem, title.Trim(), group)));
        }
        return o.OrderBy(x => Array.IndexOf([Campaign, Melee, Nitro], x.Group)).ThenBy(x => x.Rank).ThenBy(x => x.Sort, StringComparer.Ordinal).Select(x => x.L).ToList();
    }

    /// <summary>
    /// The story missions a co-op game can play (docs/multiplayer.md "Co-op story missions"): the stock scripted ones, I76's A01, T01-T17 and
    /// S01-S07 or the Nitro Pack's campaign (P01-P19, B01), in <see cref="Stock"/>'s order (the game's own: the campaign first, then the
    /// scenarios in the shell's list order; Nitro's SCENARIO.DAT order, then B and P). No melee arena, no mission a player added.
    /// </summary>
    public static List<MapListing> Coop(GameInstall install)
    {
        var dir = install.MissionDir;
        if (dir is null) return [];
        var stock = Directory.EnumerateFiles(dir).Where(f => MapFileSet.IsStockMission(f) && f.EndsWith(".msn", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), f => f);
        var o = new List<MapListing>();
        foreach (var m in Stock(install))
        {
            if (m.Stem[0] is not ('a' or 'b' or 'p' or 's' or 't') || !stock.TryGetValue(m.Stem, out var path)) continue;
            try { if (new MissionFile(path).ReadFsmProgram() is not { Code.Length: > 0 }) continue; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { continue; }
            o.Add(m);
        }
        return o;
    }

    /// <summary>The title a co-op mission goes by in the lists (the stem's upper case for one the install does not list).</summary>
    public static string CoopTitle(GameInstall install, string mission) =>
        Coop(install).FirstOrDefault(m => m.Stem.Equals(mission, StringComparison.OrdinalIgnoreCase))?.Title is { Length: > 0 } t ? t : mission.ToUpperInvariant();

    private static readonly Dictionary<string, HashSet<string>> CoopScripts = new();

    /// <summary>
    /// Whether a mission file is a copy of a story mission the co-op list holds: its FSM chunk byte for byte that mission's. Packs
    /// for the original put T01-T17 and the scenarios in addon as .lvl under other names, to play them through its melee lobby;
    /// the melee lists leave them out, as the co-op list has them. False for a file without a script or one that cannot be read.
    /// </summary>
    public static bool IsCoopCopy(GameInstall install, string path)
    {
        HashSet<string> set;
        lock (CoopScripts)
        {
            if (!CoopScripts.TryGetValue(install.Root, out set!))
            {
                set = [];
                foreach (var m in Coop(install))
                    if (install.Resolve("miss16", m.Stem + ".msn") is { } f && ScriptHash(f) is { } h) set.Add(h);
                CoopScripts[install.Root] = set;
            }
        }
        return set.Count > 0 && ScriptHash(path) is { } own && set.Contains(own);
    }

    private static string? ScriptHash(string path)
    {
        try { return new MissionFile(path).FsmPayload() is { Length: >= 4 } b ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)) : null; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or UnauthorizedAccessException) { return null; }
    }


    /// <summary>
    /// T01-T17's names, the port's (docs/deviations.md "Scene names"): the original shows a trip mission only as "Scene N" and its
    /// objectives, so each is named by its objectives (the place, the job or the target). The titles the executable holds
    /// for the 17 are never shown by the original and mostly fit other stories than the missions that shipped; they are not used.
    /// Short enough that "Scene 13: The Truckstop" fits the host form's list row.
    /// </summary>
    public static readonly string[] SceneNames =
    [
        "Meet Taurus", "Levelland School", "The Bar-D", "Dream Race", "Save Taurus", "Out of Gas",
        "Highway 380", "The Warehouse", "Disable Calisto", "Van to Tatum", "The Pecos", "The Komplex",
        "The Truckstop", "Escort the Van", "To Fort Davis", "Fort Davis", "Malocchio",
    ];

    /// <summary>A01's name: the original's main menu button that starts it.</summary>
    public const string TrainingName = "Training";

    /// <summary>Trip scene N's name ("Save Taurus"); "Scene N" past the table.</summary>
    public static string SceneName(int n) => n >= 1 && n <= SceneNames.Length ? SceneNames[n - 1] : $"Scene {n}";

    /// <summary>"Scene 5: Save Taurus": trip mission N as the lists give it.</summary>
    public static string TripTitle(int n) => n >= 1 && n <= SceneNames.Length ? $"Scene {n}: {SceneNames[n - 1]}" : $"Scene {n}";

    /// <summary>The name in the mission file itself (WRLD + 0x143), as the Nitro lobby lists it; empty if none.</summary>
    private static string FileName(GameInstall install, string stem)
    {
        try { return install.FindMission(stem) is { } p ? WorldSettings.ArenaName(new MissionFile(p))?.Trim() ?? "" : ""; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return ""; }
    }
}
