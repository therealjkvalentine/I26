using Godot;
using I76.Formats.Assembly;
using I76.Formats.Imaging;
using I76.Formats.Vfs;

namespace I76.Game.Boot;

/// <summary>
/// --export-textures DIR: every texture of the install as a PNG the right way up, keyed texels transparent
/// (DIR/&lt;stem&gt;.png, the name an upscale pack uses) and DIR/bottom-up.txt (the stems the game stores bottom up), then quit. The input of tools/upscale-textures.py.
/// A stem held by several formats exports the one the game draws (.m16 first). An .m16 carries its own colours;
/// a .map or .vqm is coloured by --palette (t01.act unless said), and a mission with another palette shows an
/// upscale made from it in these colours.
/// </summary>
public static class TextureExport
{
    public static int Run(GameInstall install, string dir, string paletteName)
    {
        var vfs = new AssetVfs(install);
        var res = new TextureResolver(vfs);
        var pal = vfs.TryRead(paletteName) is { } pd ? new Palette(pd) : null;
        if (pal is null) GD.PushWarning($"export textures: palette {paletteName} not found, .map and .vqm come out grey");
        Directory.CreateDirectory(dir);
        var stems = vfs.Names.Where(n => TextureResolver.Extensions.Contains(Path.GetExtension(n).ToLowerInvariant()))
            .Select(n => Path.GetFileNameWithoutExtension(n).ToLowerInvariant()).Distinct().OrderBy(s => s, StringComparer.Ordinal);
        int n = 0;
        var bottomUp = new List<string>();
        foreach (var stem in stems)
        {
            if (res.FindByStem(stem) is not { } member || res.Load(member) is not { } t) continue;
            var img = Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, TextureReader.ToRgba(t, pal));
            // .map and .m16 rows are stored bottom up; a .vqm's top down
            if (!member.EndsWith(".vqm", StringComparison.OrdinalIgnoreCase)) { img.FlipY(); bottomUp.Add(stem); }
            if (img.SavePng(Path.Combine(dir, stem + ".png")) == Error.Ok) n++;
        }
        // the stems stored bottom up: the upscale script writes their .dds in the stored order, which a block format cannot flip on load
        File.WriteAllLines(Path.Combine(dir, "bottom-up.txt"), bottomUp);
        GD.Print($"export textures: {n} written to {dir}");
        return n;
    }
}
