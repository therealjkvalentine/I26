using Godot;
using I76.Formats.Imaging;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// Where upscaled world, car and cockpit textures are found (HdTexture: by the member's stem, at a power-of-two
/// multiple of the original). None ships with the port; sources in order: $I76_TEX_DIR/world/ (development),
/// user://tex-world/ (a folder), then the newest i76-hd-*.zip next to the executable or in user:// that has a
/// tex-world/ folder (read in place). The HD Menu Art setting switches them off with the menu art; the 1997 look
/// never asks (TextureFactory.Hd).
/// </summary>
public static class HdTextures
{
    private static HdSource? _source;
    private static bool _probed;

    public static HdSource? Source { get { Probe(); return _source; } }

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

    /// <summary>The member's upscale decoded to RGBA8 (rows top-down), or null for none or one that does not decode.</summary>
    public static Image? Load(string member)
    {
        if (!I76.Game.Shell.HdAssets.Enabled || Source is not { } s) return null;
        foreach (var rel in HdTexture.Candidates(member))
        {
            if (s.Read(rel) is not { } b) continue;
            var img = new Image();
            var err = rel.EndsWith(".png") ? img.LoadPngFromBuffer(b) : rel.EndsWith(".webp") ? img.LoadWebpFromBuffer(b) : img.LoadJpgFromBuffer(b);
            if (err != Error.Ok || img.IsEmpty()) { GD.PushWarning($"HD texture {rel}: does not decode"); return null; }
            img.Convert(Image.Format.Rgba8);
            return img;
        }
        return null;
    }
}
