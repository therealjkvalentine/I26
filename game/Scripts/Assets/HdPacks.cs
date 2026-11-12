using Godot;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// The HD packs found at start (HdPackDiscovery: the user folder, then beside the program; a folder one level too deep, inside the
/// folder Extract All made, or the archive left as a zip, each used where it is), scanned once for the menu art (HdAssets) and the
/// textures (HdTextures) and logged in one line.
/// </summary>
public static class HdPacks
{
    private static HdPackDiscovery? _found;
    private static readonly object Gate = new();

    public static HdPackDiscovery Found
    {
        get
        {
            if (_found is { } f) return f;
            lock (Gate)
            {
                if (_found is not null) return _found;
                var t = System.Diagnostics.Stopwatch.StartNew();
                var user = ProjectSettings.GlobalizePath("user://");
                var exe = I76.Formats.Installers.ExtractionPlaces.AppFolder(OS.GetExecutablePath());
                var d = HdPackDiscovery.Scan(user, exe);
                GD.Print($"HD packs: {d.Summary()} [{t.Elapsed.TotalMilliseconds:0} ms]");
                return _found = d;
            }
        }
    }

    /// <summary>
    /// What Graphic Detail's texture rows say was found, in a sentence: each texture pack and where it is when that is not where the
    /// README puts it, and what looked like one and was not used. Empty before the scan or with none found.
    /// </summary>
    public static string RowNote()
    {
        // only a scan made already (the settings' load makes it): never the engine's calls from here (the tests have no engine)
        if (_found is not { } f) return "";
        var parts = new List<string>();
        foreach (var n in HdPackDiscovery.Names.Where(n => n != HdPackDiscovery.Menu && f.Has(n)))
        {
            var p = f.Get(n)!;
            string where = p.Place switch
            {
                HdPackPlace.Nested => "one folder too deep",
                HdPackPlace.Extracted => "inside the folder Extract All made",
                HdPackPlace.BesideProgram => "beside the program",
                HdPackPlace.Zip => "read from " + System.IO.Path.GetFileName(p.Path),
                _ => "",
            };
            parts.Add(where.Length == 0 ? n : $"{n} ({where})");
        }
        var s = parts.Count == 0 ? "" : "Found: " + string.Join(", ", parts) + ".";
        if (f.Has(HdPackFolders.Folder(8)) && !f.Has(HdPackFolders.Folder(HdPackFolders.DefaultSize)) && !f.Has(HdPackFolders.Plain))
            s += " The 8x holds only the cockpit and the cars: the world stays 1997 without the 4x pack.";
        if (f.NotUsed.Count > 0)
            s += " Not used: " + string.Join("; ", f.NotUsed.Take(2).Select(n => $"{System.IO.Path.GetFileName(n.Path)}, {n.Why}")) + (f.NotUsed.Count > 2 ? $"; {f.NotUsed.Count - 2} more in the log." : ".");
        return s.Trim();
    }
}
