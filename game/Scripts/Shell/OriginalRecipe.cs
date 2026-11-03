using Godot;

namespace I76.Game.Shell;

/// <summary>
/// The settings without a row (docs/playing.md "Settings without a row"): the port's choices kept as words under shell.cfg's
/// [port] and read and written by ShellSettings, each table the original's word first. What remains of the Original Recipe
/// pad, whose rows went: a choice earns a row only where a player would plausibly change it; the rest is a key with the
/// right default and a row in docs/deviations.md.
/// </summary>
public static class OriginalRecipe
{
    public static readonly string[] MirrorRangeNames = ["Original", "Full"];
    public static readonly string[] BrakeLampNames = ["Original", "Held"];
    public static readonly string[] MgTracerNames = ["Original", "Bolts"];
    public static readonly string[] SkyDriftNames = ["Original", "Steady"];
    public static readonly string[] DeathOrbitNames = ["Original", "Smooth"];
    public static readonly string[] MusicTrackNames = ["Original", "All"];
    public static readonly string[] OrdnanceNames = ["Original", "Unlimited"];

    /// <summary>A stored word as an index into <paramref name="names"/> ("unlimited" for "Unlimited"); null when absent or unknown.</summary>
    public static int? ParseChoice(string? word, string[] names)
    {
        if (word is null) return null;
        int i = Array.FindIndex(names, n => Word(n) == word.Trim().ToLowerInvariant());
        return i >= 0 ? i : null;
    }

    public static string Word(string name) => name.ToLowerInvariant().Replace(' ', '_');

    internal static int? ReadChoice(ConfigFile c, string key, string[] names) =>
        c.HasSectionKey("port", key) ? ParseChoice((string)c.GetValue("port", key), names) : null;

    internal static void WriteChoice(ConfigFile c, string key, string[] names, int? choice)
    {
        if (choice is { } v && v >= 0 && v < names.Length) c.SetValue("port", key, Word(names[v]));
    }
}
