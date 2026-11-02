using Godot;
using I76.Formats.Imaging;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// Where upscaled world, car and cockpit textures are found (HdTexture: by the member's stem, at a power-of-two
/// multiple of the original). None ships with the port; sources in order: $I76_TEX_DIR/world/ (development),
/// user://tex-world/ (a folder), then the newest i76-hd-*.zip next to the executable or in user:// that has a
/// tex-world/ folder (read in place). Graphic Detail's HD Textures row switches them off (Enabled); the 1997 look
/// never draws them (TextureFactory.Hd).
/// </summary>
public static class HdTextures
{
    private static HdSource? _source;
    private static bool _probed;

    /// <summary>The player's switch (ShellSettings.HdTextures): the modern look draws the pack's upscales.</summary>
    public static bool Enabled { get; set; } = true;
    /// <summary>The row's values; the original draws the textures it ships (<see cref="I76.Game.Shell.ShellSettings.OriginalMark"/>).</summary>
    public static readonly string[] Names = ["Off" + I76.Game.Shell.ShellSettings.OriginalMark, "On"];

    /// <summary>
    /// shell.cfg's [port] hd_textures. Absent (a file from before the row, when HD Menu Art switched these too), it follows
    /// hd_menu_art, so a player who had turned the menu art off keeps the textures off.
    /// </summary>
    public static bool FromFile(bool? hdTextures, bool hdMenuArt) => hdTextures ?? hdMenuArt;

    public static HdSource? Source { get { Probe(); return _source; } }
    /// <summary>A pack was found (Graphic Detail shows the HD Textures row only then).</summary>
    public static bool Installed => Source is not null;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        var env = System.Environment.GetEnvironmentVariable("I76_TEX_DIR") is { Length: > 0 } d ? Path.Combine(d, "world") : null;
        var user = ProjectSettings.GlobalizePath("user://tex-world/");
        if (env is not null && Directory.Exists(env)) _source = new HdDirSource(env);
        else if (Directory.Exists(user) && Directory.EnumerateFileSystemEntries(user).Any()) _source = new HdDirSource(user);
        else
        {
            var exe = I76.Formats.Installers.ExtractionPlaces.AppFolder(OS.GetExecutablePath());
            _source = HdPackFinder.Newest(new[] { exe, ProjectSettings.GlobalizePath("user://") }.OfType<string>(), w => GD.Print($"HD textures: {w}"), "i76-hd-*.zip", "tex-world/");
        }
        GD.Print(_source is null ? "HD textures: none (no I76_TEX_DIR/world, user://tex-world/ or i76-hd-*.zip with tex-world/)" : $"HD textures: {_source.Origin} ({_source.Count} files)");
    }

    /// <summary>The member's upscale decoded to RGBA8 (rows top-down), or null for none or one that does not decode (whatever the switch: the factory decides what it serves).</summary>
    public static Image? Load(string member) => Read(member) is { } f ? Decode(f.Name, f.Bytes) : null;

    /// <summary>The member's upscale file as stored, or null for none (from one thread unless <see cref="ReadAnywhere"/>).</summary>
    /// <summary>Whether <see cref="Read"/> may run on worker threads (a folder; a zip is read from one).</summary>
    public static bool ReadAnywhere => Source?.ThreadSafe == true;

    public static (string Name, byte[] Bytes)? Read(string member)
    {
        if (Source is not { } s) return null;
        foreach (var rel in HdTexture.Candidates(member))
            if (s.Read(rel) is { } b) return (rel, b);
        return null;
    }

    /// <summary>A file of <see cref="Read"/> decoded to RGBA8 (a .dds kept as stored), or null; touches no renderer, so it may run on a worker thread.</summary>
    public static Image? Decode(string rel, byte[] b)
    {
        var img = new Image();
        var err = rel.EndsWith(".dds") ? img.LoadDdsFromBuffer(b) : rel.EndsWith(".png") ? img.LoadPngFromBuffer(b) : rel.EndsWith(".webp") ? img.LoadWebpFromBuffer(b) : img.LoadJpgFromBuffer(b);
        if (err != Error.Ok || img.IsEmpty()) { GD.PushWarning($"HD texture {rel}: does not decode"); return null; }
        // a .dds stays as stored (block-compressed, its mipmaps, the texture's row order) for the GPU
        if (!img.IsCompressed()) img.Convert(Image.Format.Rgba8);
        return img;
    }
}
