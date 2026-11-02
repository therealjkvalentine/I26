using Godot;
using I76.Formats.Assembly;
using I76.Formats.Imaging;

namespace I76.Game.Assets;

/// <summary>
/// Texture members to Godot textures, with the mission palette for 8-bit
/// data. Index 255 always becomes alpha 0; whether a material honours the
/// key is the material's decision (the key is per draw type).
/// In the modern look a member with an upscale in the player's pack (HdTextures) is served at the upscale's size
/// with the original's key under it; the 1997 look is served the originals, and a flip of the look swaps the
/// pictures of the textures already made (SetMode1997).
/// </summary>
public sealed class TextureFactory(TextureResolver resolver, Palette? palette)
{
    public sealed record Entry(ImageTexture Texture, bool HasKey, int Width, int Height, byte[] Rgba);

    /// <summary>Whether upscales are served (the modern look); off, every member is the original.</summary>
    public bool Hd { get; set; } = true;

    private readonly Dictionary<string, RoadRaster.Tex?> _rgba = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RoadRaster.Tex?> _hd = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A texture's RGBA (index 255 keyed to alpha 0), for CPU-side painting; the upscale's where one is served.</summary>
    public RoadRaster.Tex? GetRgba(string member) => Hd && HdOf(member) is { } h ? h : Original(member);

    /// <summary>How many texels of the served texture cover one of the original's, each way: 1 for the original.</summary>
    public int Scale(string member) => Hd && HdOf(member) is { } h && Original(member) is { } o ? h.Width / o.Width : 1;

    /// <summary>What a stored texture depends on besides its name: the files it was read from (the install and the map's own folder) and the palette.</summary>
    private readonly string _scope = $"{resolver.Vfs.Install.Root}|{resolver.Vfs.Install.MapDir}|{(palette is null ? "-" : Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(palette.Rgb)))}|";

    /// <summary>
    /// Whether the textures made are kept for the editor session (I76.Formats.Vfs.SessionStore): only with no upscale pack, as a look
    /// flip repaints the textures made under one (SetMode1997). The originals' RGBA are always kept.
    /// </summary>
    private static bool Stored => HdTextures.Source is null;

    private RoadRaster.Tex? Original(string member)
    {
        if (_rgba.TryGetValue(member, out var r)) return r;
        r = I76.Formats.Vfs.SessionStore.GetOrAdd("rgba|" + _scope + member,
            () => resolver.Load(member) is { } t ? new RoadRaster.Tex(t.Width, t.Height, TextureReader.ToRgba(t, palette)) : null,
            x => x?.Rgba.Length ?? 0);
        return _rgba[member] = r;
    }

    /// <summary>The pack's upscale of the member in the texture's stored order and key, decoded the first time; null where there is none.</summary>
    private RoadRaster.Tex? HdOf(string member)
    {
        if (_hd.TryGetValue(member, out var h)) return h;
        h = null;
        if (Original(member) is { } o && HdTextures.Load(member) is { } img)
        {
            int w = img.GetWidth(), hh = img.GetHeight(), s = HdTexture.ScaleOf(w, hh, o.Width, o.Height);
            if (s == 0) GD.PushWarning($"HD texture {member}: {w}x{hh} is not a power-of-two multiple of {o.Width}x{o.Height}");
            else h = new RoadRaster.Tex(w, hh, HdTexture.Fit(img.GetData(), w, hh, s, o.Rgba, o.Width, o.Height, !member.EndsWith(".vqm", StringComparison.OrdinalIgnoreCase)));
        }
        return _hd[member] = h;
    }

    private readonly Dictionary<string, Entry?> _cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The members with an upscale among the textures made: each look's picture, for the swap.</summary>
    private readonly Dictionary<string, (Image Modern, Image Faithful)> _looks = new(StringComparer.OrdinalIgnoreCase);

    public Entry? Get(string member)
    {
        if (_cache.TryGetValue(member, out var e)) return e;
        e = Stored ? I76.Formats.Vfs.SessionStore.GetOrAdd("tex|" + _scope + member, () => Make(member), x => x is null ? 0 : x.Rgba.LongLength * 5 / 2) : Make(member);
        _cache[member] = e;
        return e;
    }

    private Entry? Make(string member)
    {
        if (Original(member) is not { } o) return null;
        var served = Hd && HdOf(member) is { } h ? Prepare(h) : Prepare(o);
        return new Entry(ImageTexture.CreateFromImage(served.Img), served.Key, served.Img.GetWidth(), served.Img.GetHeight(), served.Rgba);
    }

    /// <summary>The look's pictures on the textures made so far: the originals in the 1997 look, the upscales in the modern.</summary>
    public void SetMode1997(bool on)
    {
        Hd = !on;
        foreach (var (member, e) in _cache)
        {
            if (e is null || HdOf(member) is not { } h) continue;
            if (!_looks.TryGetValue(member, out var pair)) _looks[member] = pair = (Prepare(h).Img, Prepare(Original(member)!).Img);
            e.Texture.SetImage(on ? pair.Faithful : pair.Modern);
        }
    }

    private static (Image Img, bool Key, byte[] Rgba) Prepare(RoadRaster.Tex t)
    {
        var rgba = (byte[])t.Rgba.Clone();
        bool key = false;
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] == 0) { key = true; break; }
        if (key) BleedUnderKey(rgba, t.Width, t.Height);
        var img = Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, rgba);
        img.GenerateMipmaps();
        return (img, key, rgba);
    }

    /// <summary>
    /// Give keyed texels the colour of an opaque neighbour, so filtering and
    /// mipmaps do not pull the key colour into the edges. Alpha is untouched.
    /// </summary>
    private static void BleedUnderKey(byte[] p, int w, int h)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            var src = (byte[])p.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 4;
                    if (src[i + 3] != 0 || (pass > 0 && (src[i] | src[i + 1] | src[i + 2]) != 0)) continue;
                    int n = 0, r = 0, g = 0, b = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int j = (yy * w + xx) * 4;
                            if (src[j + 3] == 0 && (pass == 0 || (src[j] | src[j + 1] | src[j + 2]) == 0)) continue;
                            r += src[j]; g += src[j + 1]; b += src[j + 2]; n++;
                        }
                    if (n == 0) { if (pass == 0) { p[i] = p[i + 1] = p[i + 2] = 0; } continue; }
                    p[i] = (byte)(r / n); p[i + 1] = (byte)(g / n); p[i + 2] = (byte)(b / n);
                }
        }
    }
}
