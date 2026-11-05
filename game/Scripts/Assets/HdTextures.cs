using Godot;
using I76.Formats.Imaging;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// Where upscaled world, car and cockpit textures are found (HdTexture: by the member's stem, at a whole
/// multiple of the original). None ships with the port; sources in order: $I76_TEX_DIR/world/ (development),
/// user://tex-world-Nx/ for the chosen size N (4 or 8, the HD Textures row's; any whole multiple a file or the Developer panel names; else the plain user://tex-world/, else the nearest size present),
/// read per texture over the smaller sizes present (HdPackFolders.Chain: a partial 8x of the cockpit and the cars over the 4x), then the newest i76-hd-*.zip next to the executable or in user:// that has a
/// tex-world/ folder (read in place). shell.cfg's [port] hd_textures (Graphic Detail's HD Textures row) switches them off (Enabled); the 1997 look
/// never draws them (TextureFactory.Hd).
/// </summary>
public static class HdTextures
{
    private static HdSource? _source;
    private static bool _probed;

    /// <summary>The player's switch (ShellSettings.HdTextures): the modern look draws the pack's upscales.</summary>
    public static bool Enabled { get; set; } = true;

    private static int _size = HdPackFolders.DefaultSize;
    /// <summary>The pack size chosen (ShellSettings.HdTextureSize: 4 or 8, the row's, or 3 or 2 from the file or the Developer panel): tex-world-Nx. A change is picked up by the next mission's textures, not by those already built.</summary>
    public static int Size
    {
        get => _size;
        set { value = HdPackFolders.Normalize(value); if (value != _size) { _size = value; _probed = false; _source = null; } }
    }

    private static bool UserHas(string folder)
    {
        var d = ProjectSettings.GlobalizePath($"user://{folder}/");
        return Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any();
    }

    /// <summary>The sizes whose tex-world-Nx folder exists in the user folder, largest first (none: the row is greyed).</summary>
    public static int[] AvailableSizes => HdPackFolders.Available(UserHas);

    /// <summary>What a size's tex-world-Nx holds when it is partial (pack.json's "covers", "cockpit &amp; cars"; "partial" when it does not say), else null: the HD Textures row's words for it.</summary>
    public static string? CoversOf(int size)
    {
        var p = ProjectSettings.GlobalizePath($"user://{HdPackFolders.Folder(size)}/pack.json");
        if (!File.Exists(p)) return null;
        try { return HdPackInfo.Parse(File.ReadAllBytes(p)) is { Partial: true } i ? (i.Covers.Length > 0 ? i.Covers : "partial") : null; }
        catch (IOException) { return null; }
    }

    /// <summary>
    /// shell.cfg's [port] hd_textures. Absent (a first start, or a file from before the row, when HD Menu Art switched these too),
    /// the first start's default (<see cref="DefaultOn"/>), and off if hd_menu_art is: a player who had turned the menu art off keeps
    /// the textures off.
    /// </summary>
    public static bool FromFile(bool? hdTextures, bool hdMenuArt, bool firstStart = true) => hdTextures ?? (hdMenuArt && firstStart);

    /// <summary>Video memory under this (MiB) starts with the pack off: the 4x pack adds ~0.7 GB in T01 (docs/perf/costs.md); a 4 GB card reports about 4096.</summary>
    public const long LowVideoMemoryMb = 3840;

    /// <summary>
    /// The first start's HD Textures: 4x when the user folder has tex-world-4x, else Off; Off too on a GPU with under 4 GB of video
    /// memory (<see cref="LowVideoMemoryMb"/>; unknown memory counts as enough). The player's choice is saved from then on.
    /// </summary>
    public static bool DefaultOn(bool has4x, long? videoMemoryMb) => has4x && !(videoMemoryMb < LowVideoMemoryMb);

    /// <summary><see cref="DefaultOn"/> for this machine: the user folder and the adapter (RenderBackend.VideoMemoryMb).</summary>
    public static bool FirstStartDefault => DefaultOn(UserHas(HdPackFolders.Folder(HdPackFolders.DefaultSize)), I76.Game.Boot.RenderBackend.VideoMemoryMb);

    /// <summary>Video memory from this (MiB) starts with the 8x pack when it is there (FirstStartSize): HIGH8X_PLACEHOLDER</summary>
    public const long High8xVideoMemoryMb = 7680;
    /// <summary>The same for a GPU that shares the machine's memory (Apple silicon, whose VideoMemoryMb is the machine's): HIGH8X_UNIFIED_PLACEHOLDER</summary>
    public const long High8xUnifiedMemoryMb = 15360;

    /// <summary>
    /// The first start's size: 8 when the user folder has tex-world-8x and the GPU reports <see cref="High8xVideoMemoryMb"/> or more
    /// (<see cref="High8xUnifiedMemoryMb"/> for memory shared with the machine), else 4 (unknown memory, Linux's, counts as not enough
    /// for 8x: the 4x is the safe default). The player's choice is saved from then on.
    /// </summary>
    public static int FirstStartSize(bool has8x, long? videoMemoryMb, bool unified = false) =>
        has8x && videoMemoryMb >= (unified ? High8xUnifiedMemoryMb : High8xVideoMemoryMb) ? 8 : HdPackFolders.DefaultSize;

    /// <summary><see cref="FirstStartSize"/> for this machine (a Mac's memory is unified: RenderBackend.VideoMemoryMb is the machine's).</summary>
    public static int FirstStartSizeHere => FirstStartSize(UserHas(HdPackFolders.Folder(8)), I76.Game.Boot.RenderBackend.VideoMemoryMb, OperatingSystem.IsMacOS());

    public static HdSource? Source { get { Probe(); return _source; } }
    /// <summary>A pack was found.</summary>
    public static bool Installed => Source is not null;

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        var env = System.Environment.GetEnvironmentVariable("I76_TEX_DIR") is { Length: > 0 } d ? Path.Combine(d, "world") : null;
        var chain = HdPackFolders.Chain(_size, UserHas);
        if (env is not null && Directory.Exists(env)) _source = new HdDirSource(env);
        else if (chain.Length == 1) _source = new HdDirSource(ProjectSettings.GlobalizePath($"user://{chain[0]}/"));
        else if (chain.Length > 1) _source = new HdChainSource(chain.Select(f => new HdDirSource(ProjectSettings.GlobalizePath($"user://{f}/"))));
        else
        {
            var exe = I76.Formats.Installers.ExtractionPlaces.AppFolder(OS.GetExecutablePath());
            _source = HdPackFinder.Newest(new[] { exe, ProjectSettings.GlobalizePath("user://") }.OfType<string>(), w => GD.Print($"HD textures: {w}"), "i76-hd-*.zip", "tex-world/");
        }
        GD.Print(_source is null ? "HD textures: none (no I76_TEX_DIR/world, user://tex-world-Nx/, user://tex-world/ or i76-hd-*.zip with tex-world/)" : $"HD textures: {_source.Origin} ({_source.Count} files)");
    }

    /// <summary>The member's upscale decoded to RGBA8 (rows top-down), or null for none or one that does not decode (whatever the switch: the factory decides what it serves).</summary>
    public static Image? Load(string member) => Read(member) is { } f ? Decode(f.Name, f.Bytes) : null;

    /// <summary>The member's upscale file as stored, or null for none (from one thread unless <see cref="ReadAnywhere"/>).</summary>
    /// <summary>Whether <see cref="Read"/> may run on worker threads (a folder; a zip is read from one).</summary>
    public static bool ReadAnywhere => Source?.ThreadSafe == true;

    public static (string Name, byte[] Bytes)? Read(string member)
    {
        if (Source is not { } s) return null;
        // a chain (a partial 8x over the 4x): the first folder that has the texture in any format, so a .png of the 8x beats a .dds of the 4x
        foreach (var src in s is HdChainSource c ? c.Sources : [s])
            foreach (var rel in HdTexture.Candidates(member))
                if (src.Read(rel) is { } b) return (rel, b);
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
