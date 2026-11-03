namespace I76.Formats.Vfs;

/// <summary>
/// An optional folder of voice files that stand in for the install's by name: the player's own copy of another
/// language's dub (the 1997 German one from their own disc), put in voices/LANG under the user folder. Read only;
/// nothing is written into the install. Off unless named (--voices LANG, or a folder; I76_VOICES).
/// </summary>
public static class VoiceOverlay
{
    /// <summary>The folder <see cref="AssetVfs"/> reads over the install's files; null: none. Set once at start.</summary>
    public static string? Folder { get; set; }

    /// <summary>
    /// What --voices names: a bare word is voices/NAME under the user folder, a path an existing folder;
    /// "", "en", "off" and "0" are no overlay. Null when nothing matches.
    /// </summary>
    public static string? Resolve(string? arg, string userRoot)
    {
        if (string.IsNullOrWhiteSpace(arg)) return null;
        arg = arg.Trim();
        if (arg.Equals("en", StringComparison.OrdinalIgnoreCase) || arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg == "0") return null;
        // a bare word is a language; only a path (with a separator) names a folder
        if (arg.IndexOfAny(['/', (char)92, ':']) < 0) return Path.Combine(userRoot, "voices", arg) is var dir && Directory.Exists(dir) ? dir : null;
        return Directory.Exists(arg) ? Path.GetFullPath(arg) : null;
    }

    /// <summary>
    /// The overlay's sound files by the name the install would be asked for: a file stands in for the install's file of the
    /// same name, and for the install's .wav or .gpw of the same stem (a German .wav replaces an English .gpw and the
    /// other way round), whichever <paramref name="installHas"/> says exists. Names the install does not have are added as they are.
    /// </summary>
    public static Dictionary<string, string> Files(string folder, Func<string, bool> installHas)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(path);
            var ext = Path.GetExtension(name);
            if (name.StartsWith('.') || !(ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) || ext.Equals(".gpw", StringComparison.OrdinalIgnoreCase))) continue;
            map[name] = path;
            var stem = Path.GetFileNameWithoutExtension(name);
            foreach (var other in new[] { stem + ".wav", stem + ".gpw" })
                if (!other.Equals(name, StringComparison.OrdinalIgnoreCase) && installHas(other)) map[other] = path;
        }
        return map;
    }
}
