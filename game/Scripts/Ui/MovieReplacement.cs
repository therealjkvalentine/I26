namespace I76.Game.Ui;

/// <summary>
/// An upscaled movie standing in for one of the install's Smacker files (docs/playing.md "HD movies"): the user folder's
/// movies/ file with the SMK's stem, any case, and the .ogv extension (Ogg Theora with Vorbis sound, which Godot plays).
/// </summary>
public static class MovieReplacement
{
    public const string Folder = "movies";

    /// <summary>The file in <paramref name="dir"/> standing in for <paramref name="smk"/>, or null.</summary>
    public static string? Find(string dir, string smk)
    {
        if (!System.IO.Directory.Exists(dir)) return null;
        var want = System.IO.Path.GetFileNameWithoutExtension(smk) + ".ogv";
        return System.IO.Directory.EnumerateFiles(dir).FirstOrDefault(f => System.IO.Path.GetFileName(f).Equals(want, StringComparison.OrdinalIgnoreCase));
    }
}
