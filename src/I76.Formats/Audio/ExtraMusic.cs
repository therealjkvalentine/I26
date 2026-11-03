namespace I76.Formats.Audio;

/// <summary>
/// The player's own tracks mixed into the mission music: a folder of .mp3 or .ogg files (music-extra in the user folder, or the
/// one named by --music-extra / I76_MUSIC_EXTRA) of which one song start in <c>every</c> plays a file of the folder in place
/// of the disc track. The disc sequence (<see cref="MissionMusic"/>) is untouched: the track number stays the logical one, so
/// when the extra song ends the music goes on with the track after it, as if the extra had been that track. Never in the shell.
/// The files play in a shuffled bag: all of them once before any repeats, shuffled again each round.
/// </summary>
public sealed class ExtraMusic
{
    public const int DefaultEvery = 3;
    public const int MaxEvery = 20;

    private readonly List<string> _files;
    private readonly int _every;
    private readonly Random _rng;
    private readonly List<string> _bag = [];
    private int _starts;

    public ExtraMusic(IEnumerable<string> files, int every, Random rng)
    {
        _files = [.. files];
        _every = Math.Clamp(every, 1, MaxEvery);
        _rng = rng;
    }

    public int Count => _files.Count;

    /// <summary>A song start: the extra file to play instead of the disc track (every Nth start), or null for the disc's.</summary>
    public string? Start()
    {
        if (_files.Count == 0) return null;
        if (++_starts % _every != 0) return null;
        if (_bag.Count == 0)
        {
            _bag.AddRange(_files);
            for (int i = _bag.Count - 1; i > 0; i--) { int j = _rng.Next(i + 1); (_bag[i], _bag[j]) = (_bag[j], _bag[i]); }
        }
        var next = _bag[^1];
        _bag.RemoveAt(_bag.Count - 1);
        return next;
    }

    /// <summary>The music files of a folder and its direct sub-folders, in name order; none if it is absent.</summary>
    public static List<string> Scan(string? folder)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return found;
        try
        {
            foreach (var dir in new[] { folder }.Concat(Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)))
                foreach (var f in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                    if (Path.GetExtension(f) is var x && (x.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || x.Equals(".ogg", StringComparison.OrdinalIgnoreCase)))
                        found.Add(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return found;
    }

    /// <summary>The "one start in N" setting: 1 to 20, the default 3 when absent or not a number.</summary>
    public static int ParseEvery(string? text) => int.TryParse(text, out int n) ? Math.Clamp(n, 1, MaxEvery) : DefaultEvery;

    /// <summary>The folder to read: "" / "0" / "off": none; a given path; nothing given: music-extra in the user folder.</summary>
    public static string? FolderFor(string? given, string userRoot)
    {
        if (string.IsNullOrWhiteSpace(given)) return Path.Combine(userRoot, "music-extra");
        given = given.Trim();
        return given == "0" || given.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : given;
    }
}
