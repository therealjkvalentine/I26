namespace I76.Formats.Shell;

/// <summary>
/// Which folder of the user directory holds the world texture pack: tex-world-4x, tex-world-3x and tex-world-2x side by side (pack.json's
/// "scale" says each one's size), or the plain tex-world of a pack made before the sizes had folders.
/// </summary>
public static class HdPackFolders
{
    /// <summary>The sizes offered, largest first; the first is the default.</summary>
    public static readonly int[] Sizes = [4, 3, 2];
    public const int DefaultSize = 4;
    public const string Plain = "tex-world";

    public static string Folder(int size) => $"{Plain}-{size}x";

    /// <summary>A stored size in the offered set, else the default (a hand-edited file).</summary>
    public static int Normalize(int size) => Array.IndexOf(Sizes, size) >= 0 ? size : DefaultSize;

    /// <summary>The sizes whose folders exist, largest first (<paramref name="exists"/> takes a folder name).</summary>
    public static int[] Available(Func<string, bool> exists) => [.. Sizes.Where(s => exists(Folder(s)))];

    /// <summary>
    /// The folder to load: the chosen size's when it exists, else the plain tex-world, else (an install with only other sizes) the
    /// nearest size present; null for none.
    /// </summary>
    public static string? Choose(int size, Func<string, bool> exists)
    {
        size = Normalize(size);
        if (exists(Folder(size))) return Folder(size);
        if (exists(Plain)) return Plain;
        var have = Available(exists);
        return have.Length == 0 ? null : Folder(have.MinBy(s => Math.Abs(s - size)));
    }
}
