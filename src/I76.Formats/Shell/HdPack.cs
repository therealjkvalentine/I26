using System.IO.Compression;
using System.Text.Json;

namespace I76.Formats.Shell;

/// <summary>What a pack's top-level pack.json says about it.</summary>
public sealed record HdPackInfo(string Name, string Version, string Date, string MinEngine)
{
    public override string ToString() => $"{Name} {Version}";

    /// <summary>A world pack that holds only some textures ("partial": true), the rest served by the next size down (HdPackFolders.Chain).</summary>
    public bool Partial { get; init; }
    /// <summary>What a partial pack holds, in the words the HD Textures row shows ("covers": "cockpit &amp; cars"); empty when it does not say.</summary>
    public string Covers { get; init; } = "";

    public static HdPackInfo? Parse(byte[]? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            return new HdPackInfo(S("name") is { Length: > 0 } n ? n : "HD Menu Pack", S("version"), S("date"), S("min_engine"))
            {
                Partial = r.TryGetProperty("partial", out var p) && p.ValueKind == JsonValueKind.True,
                Covers = S("covers"),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Positive when <paramref name="a"/> is the newer version (dotted numbers, else ordinal).</summary>
    public static int Compare(string a, string b) =>
        System.Version.TryParse(a, out var x) && System.Version.TryParse(b, out var y) ? x.CompareTo(y) : string.CompareOrdinal(a, b);
}

/// <summary>Where the HD menu art is read from: paths relative to the tex-menu layout ("shellbg1.webp", "nitro/shellbg.json").</summary>
/// <summary>
/// Several sources read as one, in order: a file comes from the first that has it (a partial 8x pack over the 4x: HdPackFolders.Chain).
/// Info is the first's; the count is the files of all of them, a name counted once.
/// </summary>
public sealed class HdChainSource : HdSource
{
    private readonly HdSource[] _sources;
    public HdChainSource(IEnumerable<HdSource> sources) { _sources = [.. sources]; Info = _sources.FirstOrDefault()?.Info; }
    public IReadOnlyList<HdSource> Sources => _sources;
    public override string Origin => string.Join(" over ", _sources.Select(s => s.Origin));
    // a file the 8x and the 4x both have is one file (the 8x's): the names of all of them, each once, counted once
    // counted once; a count asked from two threads at once may be counted twice, never read half-written (-1: not yet)
    private int _count = -1;
    public override int Count
    {
        get
        {
            int c = System.Threading.Volatile.Read(ref _count);
            if (c < 0) System.Threading.Volatile.Write(ref _count, c = Names.Count());
            return c;
        }
    }
    public override IEnumerable<string> Names => _sources.SelectMany(s => s.Names).Distinct(StringComparer.OrdinalIgnoreCase);
    public override bool Exists(string rel) => _sources.Any(s => s.Exists(rel));
    public override byte[]? Read(string rel)
    {
        foreach (var s in _sources)
            if (s.Exists(rel) && s.Read(rel) is { } b) return b;
        return null;
    }
    /// <summary>The source a file comes from, or null.</summary>
    public HdSource? Owner(string rel) => _sources.FirstOrDefault(s => s.Exists(rel));
    public override bool ThreadSafe => _sources.All(s => s.ThreadSafe);
    public override void Dispose() { foreach (var s in _sources) s.Dispose(); }
}

public abstract class HdSource : IDisposable
{
    /// <summary>A folder or a zip: what the log says.</summary>
    public abstract string Origin { get; }
    public abstract int Count { get; }
    /// <summary>The files' paths in the layout (<see cref="Count"/> of them).</summary>
    public abstract IEnumerable<string> Names { get; }
    public HdPackInfo? Info { get; protected set; }
    public abstract bool Exists(string rel);
    public abstract byte[]? Read(string rel);
    /// <summary>Read may be called from several threads at once.</summary>
    public virtual bool ThreadSafe => false;
    public virtual void Dispose() { }

    protected static string Norm(string rel) => rel.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}

/// <summary>A folder in the tex-menu layout (case-sensitive file systems: the names as written).</summary>
public sealed class HdDirSource : HdSource
{
    private readonly string _dir;
    // the folder's files, listed once (a mission asks for hundreds of names that are not there)
    private readonly Lazy<HashSet<string>> _names;
    public HdDirSource(string dir)
    {
        _dir = dir;
        _names = new(() => Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/')).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [], System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        var p = Path.Combine(dir, "pack.json");
        if (File.Exists(p)) Info = HdPackInfo.Parse(File.ReadAllBytes(p));
    }
    public override string Origin => _dir;
    public override int Count => _names.Value.Count;
    public override IEnumerable<string> Names => _names.Value;
    private string P(string rel) => Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));
    public override bool Exists(string rel) => _names.Value.Contains(rel);
    public override byte[]? Read(string rel) { if (!Exists(rel)) return null; try { return File.ReadAllBytes(P(rel)); } catch (IOException) { return null; } }
    /// <summary>Plain files: read from any thread.</summary>
    public override bool ThreadSafe => true;
}

/// <summary>A pack file: a zip whose tex-menu/ folder is the layout (or another root, tex-world/ for the world textures), read in place.</summary>
public sealed class HdZipSource : HdSource
{
    private readonly string _path;
    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = [];
    private readonly object _lock = new();
    public const string Root = "tex-menu/";

    public HdZipSource(string path, string root = Root)
    {
        _path = path;
        _zip = new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read);
        foreach (var e in _zip.Entries)
        {
            var n = Norm(e.FullName);
            if (n.StartsWith(root) && n.Length > root.Length) _entries[n[root.Length..]] = e;
        }
        Info = HdPackInfo.Parse(Slurp(_zip.GetEntry("pack.json")));
    }

    public override string Origin => _path;
    public override int Count => _entries.Count;
    public override IEnumerable<string> Names => _entries.Keys;
    public override bool Exists(string rel) => _entries.ContainsKey(Norm(rel));
    public override byte[]? Read(string rel) => _entries.TryGetValue(Norm(rel), out var e) ? Slurp(e) : null;

    private byte[]? Slurp(ZipArchiveEntry? e)
    {
        if (e is null) return null;
        lock (_lock)
        {
            using var s = e.Open();
            using var ms = new MemoryStream((int)Math.Min(e.Length, int.MaxValue));
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public override void Dispose() => _zip.Dispose();
}

public static class HdPackFinder
{
    /// <summary>Opens the newest readable pack among the folders' <paramref name="pattern"/> zips with files under <paramref name="root"/>, or null; unreadable ones are reported through <paramref name="warn"/>.</summary>
    public static HdZipSource? Newest(IEnumerable<string> folders, Action<string>? warn = null, string pattern = "i76-hd-menus*.zip", string root = HdZipSource.Root)
    {
        HdZipSource? best = null;
        foreach (var dir in folders.Where(Directory.Exists).Distinct())
            foreach (var f in Directory.EnumerateFiles(dir, pattern).OrderBy(f => f, StringComparer.Ordinal))
            {
                HdZipSource z;
                try { z = new HdZipSource(f, root); }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException) { warn?.Invoke($"{f}: {e.Message}"); continue; }
                if (z.Info is null || z.Count == 0) { warn?.Invoke($"{f}: no pack.json or no {root} files"); z.Dispose(); continue; }
                if (best is null || HdPackInfo.Compare(z.Info.Version, best.Info!.Version) > 0) { best?.Dispose(); best = z; }
                else z.Dispose();
            }
        return best;
    }
}
