using Godot;
using I76.Formats.Imaging;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// Where upscaled world, car and cockpit textures are found (HdTexture: by the member's stem, at a whole
/// multiple of the original). None ships with the port; sources in order: $I76_TEX_DIR/world/ (development),
/// user://tex-world-Nx/ for the chosen size N (4 or 8, chosen apart for the dash and for the cars: the 8x Dash and 8x Cars rows; any whole multiple a file or the Developer panel names; else the plain user://tex-world/, else the nearest size present),
/// read per texture over the smaller sizes present (HdPackFolders.ChainFor: a partial 8x of the cockpit and the cars over the 4x, each kind from its own size), then the newest i76-hd-*.zip next to the executable or in user:// that has a
/// tex-world/ folder (read in place). shell.cfg's [port] hd_textures (Graphic Detail's HD Textures row) switches them off (Enabled); the 1997 look
/// never draws them (TextureFactory.Hd).
/// </summary>
public static class HdTextures
{
    private static HdSource? _source;
    private static bool _probed;

    /// <summary>The player's switch (ShellSettings.HdTextures): the modern look draws the pack's upscales.</summary>
    public static bool Enabled { get; set; } = true;

    private static int _dash = HdPackFolders.DefaultSize, _car = HdPackFolders.DefaultSize;
    /// <summary>The size the cockpit's art is read at (ShellSettings.HdDashSize: 4 or 8, the 8x Dash row's, or 3 or 2 from the file or the Developer panel): tex-world-Nx first. A change is picked up by the next mission's textures, not by those already built.</summary>
    public static int DashSize
    {
        get => _dash;
        set { value = HdPackFolders.Normalize(value); if (value != _dash) { _dash = value; Forget(); } }
    }
    /// <summary>The size every other texture is read at (ShellSettings.HdCarSize: the cars, the 8x Cars row's; a full pack's world textures too).</summary>
    public static int CarSize
    {
        get => _car;
        set { value = HdPackFolders.Normalize(value); if (value != _car) { _car = value; Forget(); } }
    }
    private static void Forget() { _probed = false; _source = null; _dashChain = _carChain = []; }

    private static bool UserHas(string folder)
    {
        var d = ProjectSettings.GlobalizePath($"user://{folder}/");
        return Directory.Exists(d) && Directory.EnumerateFileSystemEntries(d).Any();
    }

    /// <summary>The sizes whose tex-world-Nx folder exists in the user folder, largest first (none: the row is greyed).</summary>
    public static int[] AvailableSizes => HdPackFolders.Available(UserHas);

    /// <summary>
    /// shell.cfg's [port] hd_textures. Absent (a first start, or a file from before the row, when HD Menu Art switched these too),
    /// the first start's default (<see cref="DefaultOn"/>), and off if hd_menu_art is: a player who had turned the menu art off keeps
    /// the textures off.
    /// </summary>
    public static bool FromFile(bool? hdTextures, bool hdMenuArt, bool firstStart = true) => hdTextures ?? (hdMenuArt && firstStart);

    /// <summary>Video memory under this (MiB) starts with the pack off: the 4x pack adds ~0.7 GB in T01 (docs/perf/costs.md); a 4 GB card reports about 4096.</summary>
    public const long LowVideoMemoryMb = 3840;

    /// <summary>A graphics card's video memory (MiB) from which a first start takes the 8x of the dash: a 6 GB card (reports about 6144). With the dash at 8x the game's whole use is ~2.5-3.0 GB (docs/perf/costs.md, the 8x section), so a 4 GB card stays on the 4x.</summary>
    public const long LargeVideoMemoryMb = 5888;

    /// <summary>The same where the GPU shares the machine's memory (Apple silicon: RenderBackend.VideoMemoryMb is the machine's memory): a 16 GB Mac (16384); an 8 GB Mac stays on the 4x.</summary>
    public const long LargeSharedMemoryMb = 15872;

    /// <summary>
    /// The first start's HD Textures switch (also the row's Reset): on when the user folder has tex-world-4x and the GPU has at least
    /// <see cref="LowVideoMemoryMb"/>; unknown memory (Linux) counts as enough. A saved shell.cfg keeps its own choice.
    /// </summary>
    public static bool DefaultOn(bool has4x, long? videoMemoryMb) => has4x && (videoMemoryMb is not { } mb || mb >= LowVideoMemoryMb);

    /// <summary>
    /// The first start's dash size (the 8x Dash row's Reset): 8 when tex-world-8x is beside the 4x and the GPU has room for the
    /// cockpit's art at 8x (<see cref="LargeVideoMemoryMb"/>, or <see cref="LargeSharedMemoryMb"/> where it shares the machine's
    /// memory): drawn near full screen, its 144 textures of a mission are large, +482 MB over the 4x (docs/perf/costs.md); else 4.
    /// Unknown memory never takes an 8x.
    /// </summary>
    public static int PickDash(bool has4x, bool has8x, long? memoryMb, bool sharedMemory) =>
        has4x && has8x && memoryMb is { } mb && mb >= (sharedMemory ? LargeSharedMemoryMb : LargeVideoMemoryMb) ? HdTextureLarge : HdPackFolders.DefaultSize;

    /// <summary>
    /// The first start's cars size (the 8x Cars row's Reset): 8 when tex-world-8x is beside the 4x and the GPU has room for the 4x
    /// (<see cref="LowVideoMemoryMb"/>): a car's textures are small and only the cars in the scene are loaded, +54 MB over the 4x
    /// at T01's start (docs/perf/costs.md); else 4. Unknown memory never takes an 8x.
    /// </summary>
    public static int PickCar(bool has4x, bool has8x, long? memoryMb, bool sharedMemory) =>
        has4x && has8x && memoryMb is { } mb && mb >= LowVideoMemoryMb ? HdTextureLarge : HdPackFolders.DefaultSize;

    private const int HdTextureLarge = 8;

    private static (bool On, int Dash, int Car)? _pick;
    /// <summary><see cref="DefaultOn"/>, <see cref="PickDash"/> and <see cref="PickCar"/> for this machine: the user folder and the adapter (RenderBackend.VideoMemoryMb; macOS shares the machine's memory), read once.</summary>
    public static (bool On, int Dash, int Car) FirstStart
    {
        get
        {
            if (_pick is { } p) return p;
            bool has4x = UserHas(HdPackFolders.Folder(HdPackFolders.DefaultSize)), has8x = UserHas(HdPackFolders.Folder(HdTextureLarge));
            long? mb = I76.Game.Boot.RenderBackend.VideoMemoryMb;
            bool mac = OperatingSystem.IsMacOS();
            return (_pick = (DefaultOn(has4x, mb), PickDash(has4x, has8x, mb, mac), PickCar(has4x, has8x, mb, mac))).Value;
        }
    }

    /// <summary>The first start's switch: on when the 4x is there and fits.</summary>
    public static bool FirstStartDefault => FirstStart.On;

    /// <summary>
    /// A saved size as it is (a 4 stays 4 on a machine whose first start would take the 8x), the default for one not in the set; absent
    /// (a file from before the key, which never chose a size), the first start's (<paramref name="firstStart"/>).
    /// </summary>
    public static int SizeFromFile(int? saved, int firstStart) => saved is { } v ? HdPackFolders.Normalize(v) : HdPackFolders.Normalize(firstStart);

    /// <summary>
    /// shell.cfg's [port] hd_dash_size and hd_car_size, each <see cref="SizeFromFile"/>; one that is absent takes the older single
    /// hd_texture_size when the file has it (a file from before the two keys: its 8 set both to 8, its 4 both to 4), else the first
    /// start's for that kind. The two keys are written from then on.
    /// </summary>
    public static (int Dash, int Car) SizesFromFile(int? dash, int? car, int? single, int firstStartDash, int firstStartCar) =>
        (SizeFromFile(dash ?? single, firstStartDash), SizeFromFile(car ?? single, firstStartCar));

    public static HdSource? Source { get { Probe(); return _source; } }
    /// <summary>A pack was found.</summary>
    public static bool Installed => Source is not null;

    // the folders a cockpit texture and any other are looked in, in order (HdPackFolders.ChainFor); one source each for I76_TEX_DIR or a zip
    private static HdSource[] _dashChain = [], _carChain = [];

    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        var env = System.Environment.GetEnvironmentVariable("I76_TEX_DIR") is { Length: > 0 } d ? Path.Combine(d, "world") : null;
        var dash = HdPackFolders.Chain(_dash, UserHas);
        var car = HdPackFolders.Chain(_car, UserHas);
        if (env is not null && Directory.Exists(env)) _source = new HdDirSource(env);
        else if (dash.Length + car.Length > 0)
        {
            // one source per folder, shared by the two chains; the log's source is every folder, largest first
            var folders = dash.Concat(car).Distinct().OrderByDescending(HdPackFolders.SizeOf).ToArray();
            var sources = folders.ToDictionary(f => f, f => (HdSource)new HdDirSource(ProjectSettings.GlobalizePath($"user://{f}/")));
            _source = folders.Length == 1 ? sources[folders[0]] : new HdChainSource(folders.Select(f => sources[f]));
            _dashChain = [.. dash.Select(f => sources[f])];
            _carChain = [.. car.Select(f => sources[f])];
        }
        else
        {
            var exe = I76.Formats.Installers.ExtractionPlaces.AppFolder(OS.GetExecutablePath());
            _source = HdPackFinder.Newest(new[] { exe, ProjectSettings.GlobalizePath("user://") }.OfType<string>(), w => GD.Print($"HD textures: {w}"), "i76-hd-*.zip", "tex-world/");
        }
        if (_dashChain.Length == 0 && _source is { } one) _dashChain = _carChain = [one];
        GD.Print(_source is null ? "HD textures: none (no I76_TEX_DIR/world, user://tex-world-Nx/, user://tex-world/ or i76-hd-*.zip with tex-world/)"
            : $"HD textures: {_source.Origin} ({_source.Count} files); dash {string.Join(" over ", dash)}, cars {string.Join(" over ", car)}");
    }

    /// <summary>The member's upscale decoded to RGBA8 (rows top-down), or null for none or one that does not decode (whatever the switch: the factory decides what it serves).</summary>
    public static Image? Load(string member) => Read(member) is { } f ? Decode(f.Name, f.Bytes) : null;

    /// <summary>The member's upscale file as stored, or null for none (from one thread unless <see cref="ReadAnywhere"/>).</summary>
    /// <summary>Whether <see cref="Read"/> may run on worker threads (a folder; a zip is read from one).</summary>
    public static bool ReadAnywhere => Source?.ThreadSafe == true;

    public static (string Name, byte[] Bytes)? Read(string member)
    {
        if (Source is null) return null;
        // the texture's kind's chain (a partial 8x over the 4x, from the dash's or the cars' size): the first folder that has the texture
        // in any format, so a .png of the 8x beats a .dds of the 4x
        foreach (var src in HdPackFolders.IsCockpit(member) ? _dashChain : _carChain)
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
