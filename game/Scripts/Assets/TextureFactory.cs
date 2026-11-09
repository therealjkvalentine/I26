using Godot;
using I76.Formats.Assembly;
using I76.Formats.Imaging;

namespace I76.Game.Assets;

/// <summary>
/// Texture members to Godot textures, with the mission palette for 8-bit
/// data. Index 255 always becomes alpha 0; whether a material honours the
/// key is the material's decision (the key is per draw type).
/// In the modern look a member with an upscale in the player's pack (HdTextures) is served at the upscale's size
/// with the original's key under it, unless the HD Textures row is off; the 1997 look is served the originals, and a
/// flip of the look or of the row swaps the pictures of the textures already made (SetMode1997, SetHd). A pack's .dds
/// (block-compressed with its mipmaps, in the texture's stored order, keyed and bled by the tool) goes to the GPU as it is;
/// its texels are decompressed only for the few that are painted on the CPU (GetRgba).
/// </summary>
public sealed class TextureFactory(TextureResolver resolver, Palette? palette)
{
    /// <param name="KeyColour">What index 255 decodes to (its palette colour): a face that does not honour the key draws that, not the bled colour under it.</param>
    /// <param name="Base">The original's texels (bled under the key), what the port's material analyses read (car material table, windows,
    /// prop and car maps): they are keyed to the original's texel grid, and their maps are sampled by UV over any upscale.</param>
    public sealed record Entry(ImageTexture Texture, bool HasKey, int Width, int Height, Color KeyColour, RoadRaster.Tex Base);

    /// <summary>An upscale: its texels fitted from a picture (RGBA8, stored order), or a .dds as stored (Gpu; its texels decompressed when asked).</summary>
    private sealed class HdPic(int width, int height, RoadRaster.Tex? rgba, Image? gpu)
    {
        public readonly int Width = width, Height = height;
        public readonly Image? Gpu = gpu;
        private RoadRaster.Tex? _rgba = rgba;
        public RoadRaster.Tex Rgba
        {
            get
            {
                if (_rgba is not null) return _rgba;
                var im = (Image)Gpu!.Duplicate();
                im.ClearMipmaps();
                im.Decompress();
                im.Convert(Image.Format.Rgba8);
                return _rgba = new RoadRaster.Tex(Width, Height, im.GetData());
            }
        }
    }

    /// <summary>Whether upscales are served (the modern look with HD Textures on); off, every member is the original. Set before the first texture; after, SetHd.</summary>
    public bool Hd { get; set; } = true;

    /// <summary>After SetHd swaps what is served: art made from GetRgba (the cockpit's panels, radar frames, wheel) is made again.</summary>
    public event System.Action? Swapped;

    /// <summary>
    /// The palette the pack's pictures were coloured by (HdTexture.PackPalette). Where the mission's palette turns grey entries
    /// that one colours (the dream), an upscale over them is turned grey too (HdTexture.Recolour). Set before the first texture.
    /// </summary>
    public Palette? PackPalette
    {
        get => _packPalette;
        set { _packPalette = value; _greyed = palette is not null && value is not null ? HdTexture.Greyed(palette, value) : null; }
    }
    private Palette? _packPalette;
    private bool[]? _greyed;

    private readonly Dictionary<string, RoadRaster.Tex?> _rgba = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HdPic?> _hd = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A texture's RGBA (index 255 keyed to alpha 0), for CPU-side painting; the upscale's where one is served.</summary>
    public RoadRaster.Tex? GetRgba(string member) => Hd && HdOf(member) is { } h ? h.Rgba : Original(member);

    /// <summary>The original's RGBA whatever is served (index 255 keyed to alpha 0): what analyses keyed to its texel grid read.</summary>
    public RoadRaster.Tex? GetOriginalRgba(string member) => Original(member);

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

    private readonly Dictionary<string, RoadRaster.Tex?> _bled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The original with its keyed texels given their opaque neighbours' colour (alpha untouched): what is drawn, and the colour under an upscale's key.</summary>
    private RoadRaster.Tex? Bled(string member)
    {
        if (_bled.TryGetValue(member, out var b)) return b;
        if (Original(member) is { } o)
        {
            var rgba = (byte[])o.Rgba.Clone();
            if (HasKey(rgba)) BleedUnderKey(rgba, o.Width, o.Height);
            b = new RoadRaster.Tex(o.Width, o.Height, rgba);
        }
        return _bled[member] = b;
    }

    /// <summary>The pack's upscale of the member in the texture's stored order and key, decoded the first time; null where there is none.</summary>
    private HdPic? HdOf(string member)
    {
        if (_hd.TryGetValue(member, out var h)) return h;
        h = Bled(member) is { } b && HdTextures.Read(member) is { } f ? Fitted(member, f.Name, f.Bytes, b, GreyedOf(member)) : null;
        return _hd[member] = h;
    }

    /// <summary>
    /// Decodes and fits the upscales of the members not made yet on worker threads (a model's or a scene's textures, before its
    /// materials ask for them one at a time). Nothing touches the renderer.
    /// <paramref name="rgba"/>: they are painted on the CPU (GetRgba), so a .dds is decompressed there too.
    /// </summary>
    /// <summary>
    /// The workers a Prefetch from this thread may use (0: as many as the pool gives). The mission's build sets half the cores:
    /// its prefetches run under the briefing, whose frames the main thread must still draw on time (Main.StartBuildJob).
    /// </summary>
    [System.ThreadStatic] public static int PrefetchThreads;

    public void Prefetch(IEnumerable<string> members, bool rgba = false)
    {
        if (!Hd || HdTextures.Source is null) return;
        // a folder's files are read on the workers too; a zip's here
        bool anywhere = HdTextures.ReadAnywhere;
        var todo = new List<(string Member, (string Name, byte[] Bytes)? File, RoadRaster.Tex Base, byte[]? Greyed)>();
        foreach (var m in members.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_hd.ContainsKey(m)) continue;
            if (Bled(m) is not { } b) { _hd[m] = null; continue; }
            var f = anywhere ? null : HdTextures.Read(m);
            if (!anywhere && f is null) { _hd[m] = null; continue; }
            todo.Add((m, f, b, GreyedOf(m)));
        }
        if (todo.Count == 0) return;
        var made = new HdPic?[todo.Count];
        System.Threading.Tasks.Parallel.For(0, todo.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = PrefetchThreads > 0 ? PrefetchThreads : -1 }, i =>
        {
            if ((todo[i].File ?? HdTextures.Read(todo[i].Member)) is not { } f) return;
            made[i] = Fitted(todo[i].Member, f.Name, f.Bytes, todo[i].Base, todo[i].Greyed);
            if (rgba) _ = made[i]?.Rgba;
        });
        for (int i = 0; i < todo.Count; i++) _hd[todo[i].Member] = made[i];
    }

    /// <summary>
    /// The pack's upscales of <paramref name="members"/> as the GPU would take them (a .dds as stored, with its mipmaps; a fitted
    /// picture as RGBA8), decoded on worker threads and not kept here: art the caller keeps in its own form (the radar's frames,
    /// RadarFrames). Null where the original is served.
    /// </summary>
    public Image?[] Pictures(IReadOnlyList<string?> members)
    {
        var made = new Image?[members.Count];
        if (!Hd || HdTextures.Source is null) return made;
        bool anywhere = HdTextures.ReadAnywhere;
        var todo = new ((string Name, byte[] Bytes)? File, RoadRaster.Tex Base, byte[]? Greyed)?[members.Count];
        for (int i = 0; i < members.Count; i++)
            if (members[i] is { } m && Bled(m) is { } b && (anywhere || HdTextures.Read(m) is not null)) todo[i] = (anywhere ? null : HdTextures.Read(m), b, GreyedOf(m));
        System.Threading.Tasks.Parallel.For(0, members.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = PrefetchThreads > 0 ? PrefetchThreads : -1 }, i =>
        {
            if (todo[i] is not { } t || (t.File ?? HdTextures.Read(members[i]!)) is not { } f) return;
            if (Fitted(members[i]!, f.Name, f.Bytes, t.Base, t.Greyed) is { } h)
                made[i] = h.Gpu ?? Image.CreateFromData(h.Width, h.Height, false, Image.Format.Rgba8, h.Rgba.Rgba);
        });
        return made;
    }

    /// <summary>The member's palette indices when it uses an entry the mission's palette greys (PackPalette), else null.</summary>
    private byte[]? GreyedOf(string member) =>
        _greyed is { } g && resolver.Load(member) is { Palette: null } t && HdTexture.Uses(t.Indices, g) ? t.Indices : null;

    private HdPic? Fitted(string member, string name, byte[] bytes, RoadRaster.Tex b, byte[]? greyedIndices)
    {
        if (HdTextures.Decode(name, bytes) is not { } img) return null;
        int w = img.GetWidth(), hh = img.GetHeight(), s = HdTexture.ScaleOf(w, hh, b.Width, b.Height);
        if (s == 0) { GD.PushWarning($"HD texture {member}: {w}x{hh} is not a whole multiple of {b.Width}x{b.Height}, the same both ways"); return null; }
        Served(s, member);
        byte[] rgba;
        if (img.IsCompressed())
        {
            if (greyedIndices is null) return new HdPic(w, hh, null, img);
            // a .dds over the greyed entries is decompressed, turned grey and sent as RGBA8 with new mipmaps
            img.ClearMipmaps(); img.Decompress(); img.Convert(Image.Format.Rgba8);
            rgba = img.GetData();
        }
        else rgba = HdTexture.Fit(img.GetData(), w, hh, s, b.Rgba, b.Width, b.Height, !member.EndsWith(".vqm", StringComparison.OrdinalIgnoreCase));
        if (greyedIndices is not null) HdTexture.Recolour(rgba, w, hh, s, greyedIndices, b.Width, b.Height, _greyed!, palette!, _packPalette!);
        return new HdPic(w, hh, new RoadRaster.Tex(w, hh, rgba), null);
    }

    private static readonly int[] _served = new int[17];

    /// <summary>How many upscales at scale s (1..16) have been fitted in this run, for a log or a check that a pack's size loads.</summary>
    public static int ServedAt(int s) => s is > 0 and < 17 ? System.Threading.Volatile.Read(ref _served[s]) : 0;

    /// <summary>Counts an upscale fitted (worker threads too); the first at each scale is logged.</summary>
    private static void Served(int s, string member)
    {
        if (s >= _served.Length) return;
        if (System.Threading.Interlocked.Increment(ref _served[s]) == 1) GD.Print($"HD textures: first {s}x upscale served ({member})");
    }

    /// <summary>The upscale's picture for the GPU: a .dds as it is, a fitted picture with its mipmaps.</summary>
    private static Image Picture(HdPic h) => h.Gpu ?? Prepare(h.Rgba).Img;

    private readonly Dictionary<string, Entry?> _cache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The members with an upscale among the textures made: each look's picture, for the swap.</summary>
    private readonly Dictionary<string, (Image Modern, Image Faithful)> _looks = new(StringComparer.OrdinalIgnoreCase);

    public Entry? Get(string member)
    {
        if (_cache.TryGetValue(member, out var e)) return e;
        e = Stored ? I76.Formats.Vfs.SessionStore.GetOrAdd("tex|" + _scope + member, () => Make(member), x => x is null ? 0 : x.Base.Rgba.LongLength * 5 / 2) : Make(member);
        _cache[member] = e;
        return e;
    }

    private Entry? Make(string member)
    {
        long t0 = I76.Game.Boot.LoadProfiler.On ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        if (Original(member) is not { } o) return null;
        var b = Bled(member)!;
        bool hd = Hd && HdOf(member) is not null;
        var img = hd ? Picture(HdOf(member)!) : Prepare(b).Img;
        // keyed as the original is: an upscale carries the original's key
        var e = new Entry(ImageTexture.CreateFromImage(img), HasKey(b.Rgba), img.GetWidth(), img.GetHeight(), KeyColour(o), b);
        if (t0 != 0) { double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds; I76.Game.Boot.LoadProfiler.Texture(hd, img.GetDataSize(), ms); }
        return e;
    }

    private readonly Dictionary<string, byte[]?> _edgeField = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageTexture?> _edges = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The smooth outline's texels per original pixel: four for the cockpit's art (drawn large), two for the rest.</summary>
    public static int KeyEdgeScale(string member)
    {
        string stem = member[(member.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        return stem.StartsWith('z') || stem.StartsWith('Z') ? 4 : 2;
    }

    /// <summary>
    /// The modern look's smooth outline of a keyed texture (KeyEdge: the original's key as curves, staircases straightened and
    /// corners kept) as a signed distance field, R8 at KeyEdgeScale texels a pixel with its mipmaps, 128 on the edge; null for
    /// a texture with nothing keyed. From the original's key whatever is served: a pack carries the original's key.
    /// </summary>
    public ImageTexture? KeyEdge(string member)
    {
        if (_edges.TryGetValue(member, out var t)) return t;
        PrefetchKeyEdges([member]);
        t = null;
        if (_edgeField[member] is { } f && Original(member) is { } o)
        {
            int s = KeyEdgeScale(member);
            var img = Image.CreateFromData(o.Width * s, o.Height * s, false, Image.Format.R8, f);
            img.GenerateMipmaps();
            t = ImageTexture.CreateFromImage(img);
        }
        _edgeField.Remove(member);
        return _edges[member] = t;
    }

    /// <summary>Makes the smooth outlines of the keyed members not made yet on worker threads (each a few ms; nothing touches the renderer).</summary>
    public void PrefetchKeyEdges(IEnumerable<string> members)
    {
        var todo = new List<(string Member, bool[] Mask, int W, int H)>();
        foreach (var m in members.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_edges.ContainsKey(m) || _edgeField.ContainsKey(m)) continue;
            if (Original(m) is not { } o) { _edgeField[m] = null; continue; }
            var mask = I76.Formats.Imaging.KeyEdge.Opaque(o.Rgba, o.Width, o.Height);
            if (System.Array.IndexOf(mask, false) < 0) { _edgeField[m] = null; continue; }
            todo.Add((m, mask, o.Width, o.Height));
        }
        if (todo.Count == 0) return;
        var made = new byte[todo.Count][];
        System.Threading.Tasks.Parallel.For(0, todo.Count, i => made[i] = I76.Formats.Imaging.KeyEdge.Field(todo[i].Mask, todo[i].W, todo[i].H, KeyEdgeScale(todo[i].Member)));
        for (int i = 0; i < todo.Count; i++) _edgeField[todo[i].Member] = made[i];
    }

    /// <summary>The look's pictures on the textures made so far: the originals in the 1997 look, the modern look's per the HD Textures row.</summary>
    public void SetMode1997(bool on) => SetHd(!on && HdTextures.Enabled);

    /// <summary>Serve the upscales or the originals, swapping the pictures of the textures made so far.</summary>
    public void SetHd(bool on)
    {
        if (on == Hd) return;
        Hd = on;
        foreach (var (member, e) in _cache)
        {
            if (e is null || HdOf(member) is not { } h) continue;
            if (!_looks.TryGetValue(member, out var pair)) _looks[member] = pair = (Picture(h), Prepare(Bled(member)!).Img);
            e.Texture.SetImage(on ? pair.Modern : pair.Faithful);
        }
        Swapped?.Invoke();
    }

    /// <summary>What index 255 decodes to in the original (its first keyed texel's colour, before any bleed).</summary>
    private static Color KeyColour(RoadRaster.Tex t)
    {
        for (int i = 3; i < t.Rgba.Length; i += 4) if (t.Rgba[i] == 0) return Color.Color8(t.Rgba[i - 3], t.Rgba[i - 2], t.Rgba[i - 1]);
        return default;
    }

    private static bool HasKey(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] == 0) return true;
        return false;
    }

    /// <summary>A texture already coloured under its key (Bled, or an upscale fitted over it) with its mipmaps.</summary>
    private static (Image Img, bool Key) Prepare(RoadRaster.Tex t)
    {
        var img = Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, t.Rgba);
        img.GenerateMipmaps();
        return (img, HasKey(t.Rgba));
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
