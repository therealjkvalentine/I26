using Godot;
using I76.Formats.Imaging;
using I76.Formats.Shell;

namespace I76.Game.Assets;

/// <summary>
/// Where upscaled world, car and cockpit textures are found (HdTexture: by the member's stem, at a whole
/// multiple of the original). None ships with the port; sources in order: $I76_TEX_DIR/world/ (development),
/// user://tex-world-Nx/ for the chosen size N (chosen apart for the world, 4, and for the cars and the cockpit's art, 4 or 8: Graphic Detail's
/// World Textures and Car &amp; Dash Textures; any whole multiple a file or the Developer panel names; else the plain user://tex-world/, else the
/// nearest size present), read per texture over the smaller sizes present (HdPackFolders.ChainFor: a partial 8x of the cockpit and the cars
/// over the 4x, each kind from its own size), then the newest i76-hd-*.zip next to the executable or in user:// that has a tex-world/ folder
/// (read in place). A kind at 0 (the row's 1997) is served the install's own; both at 0, nothing is (Enabled); the 1997 look never draws
/// them (TextureFactory.Hd).
/// </summary>
public static class HdTextures
{

    /// <summary>Any upscale is served: either size not 0 (the modern look draws the pack's upscales; the 1997 look never does).</summary>
    public static bool Enabled => _world != 0 || _car != 0;

    private static int _world = HdPackFolders.DefaultSize, _car = HdPackFolders.DefaultSize;
    /// <summary>A size as stored: 0 (the install's own) or a size in the set (anything else the default).</summary>
    public static int Normalize(int size) => size == 0 ? 0 : HdPackFolders.Normalize(size);
    /// <summary>The size the world's textures are read at (ShellSettings.WorldTextures: 0, 4, or 3 or 2 from the file or the Developer panel): tex-world-Nx first. A change is picked up by the next mission's textures, not by those already built.</summary>
    public static int WorldSize
    {
        get => _world;
        set { value = Normalize(value); if (value != _world) { _world = value; Forget(); } }
    }
    /// <summary>The size the cars' textures and the cockpit's art are read at (ShellSettings.CarTextures: 0, 4 or 8, Car &amp; Dash Textures').</summary>
    public static int CarSize
    {
        get => _car;
        set { value = Normalize(value); if (value != _car) { _car = value; Forget(); } }
    }
    private static void Forget() { lock (Gate) _state = null; }

    // the textures of the vehicles' models (MeshBuilder marks them as it loads a car): a car's kind, not the world's
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Cars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A car model's textures: read at the cars' size (call before they are first asked for).</summary>
    public static void MarkCar(IEnumerable<string> members) { foreach (var m in members) Cars.TryAdd(m, 0); }

    /// <summary>A texture's kind, as the loader reads it.</summary>
    public static TextureKind KindOf(string member) => HdPackFolders.KindOf(member, Cars.ContainsKey(member));

    // a pack of that layout name was found (HdPacks: the user folder, one level down, beside the program or in its zip)
    private static bool UserHas(string folder) => HdPacks.Found.Has(folder);

    /// <summary>The sizes whose tex-world-Nx pack was found (HdPacks), largest first (none: the row is greyed).</summary>
    public static int[] AvailableSizes => HdPackFolders.Available(UserHas);

    /// <summary>A size as shell.cfg's world_textures and car_textures write it: 1997 for 0, else 4x, 8x ...</summary>
    public static string Word(int size) => size == 0 ? "1997" : $"{size}x";

    /// <summary>A stored word (1997, 4x, 8, off ...) as a size; null when it is none.</summary>
    public static int? ParseWord(string? word)
    {
        var w = (word ?? "").Trim().ToLowerInvariant().TrimEnd('x');
        if (w is "1997" or "0" or "off" or "false" or "original") return 0;
        return int.TryParse(w, out int n) && Array.IndexOf(HdPackFolders.Sizes, n) >= 0 ? n : null;
    }

    /// <summary>
    /// shell.cfg's world_textures and car_textures, each a size or 1997. A file from before them (Graphic Detail's HD Textures, 8x Dash
    /// and 8x Cars rows) is read as those rows meant: hd_textures false, both 1997 (absent, the menu art's switch, off when it is off, as
    /// before the key; else on); on, the world at the cars' old size (hd_car_size, or the single hd_texture_size before it; an 8 is the
    /// 4x, since an 8x pack holds no world texture) and the cars and the dash as <see cref="OldCarAndDash"/> says. A key or size absent
    /// takes the first start's.
    /// </summary>
    /// <summary>
    /// The old dash's and cars' sizes as one: the dash's 8x, a choice that took the memory, is kept; the cars' 8x alone (the first start
    /// of a 4 GB card, which took the cars' 8x and the dash's 4x) is the 8x only where this machine's first start takes it, the dash's
    /// rule, else the 4x; otherwise the larger of the two (a 3x or 2x as it is). Neither: the first start's.
    /// </summary>
    public static int OldCarAndDash(int? dash, int? car, int firstStart)
    {
        if (dash is null && car is null) return firstStart != 0 ? firstStart : HdPackFolders.DefaultSize;
        int d = dash is { } dv ? HdPackFolders.Normalize(dv) : 0, c = car is { } cv ? HdPackFolders.Normalize(cv) : 0;
        if (d == 8) return 8;
        if (c == 8) return firstStart == 8 ? 8 : HdPackFolders.DefaultSize;
        return Math.Max(d, c);
    }

    public static (int World, int Car) FromFile(string? world, string? car, bool? hdTextures, bool hdMenuArt, int? dash, int? carOld, int? single, (int World, int Car) firstStart)
    {
        int? w = ParseWord(world), c = ParseWord(car);
        bool on = hdTextures ?? (hdMenuArt && (firstStart.World != 0 || firstStart.Car != 0));
        int Or4(int first) => first != 0 ? first : HdPackFolders.DefaultSize;
        w ??= !on ? 0 : (carOld ?? single) is { } o ? (o is 2 or 3 ? o : HdPackFolders.DefaultSize) : Or4(firstStart.World);
        c ??= !on ? 0 : OldCarAndDash(dash ?? single, carOld ?? single, firstStart.Car);
        return (w.Value, c.Value);
    }

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
    /// The first start's Car &amp; Dash Textures (the row's Reset): 8 when tex-world-8x is beside the 4x and the GPU has room for the
    /// cockpit's art at 8x (<see cref="LargeVideoMemoryMb"/>, or <see cref="LargeSharedMemoryMb"/> where it shares the machine's
    /// memory): drawn near full screen, its 144 textures of a mission are large, +482 MB over the 4x of the 510 MB the cars' and the
    /// dash's 8x take together (docs/perf/costs.md); else 4. Unknown memory never takes an 8x.
    /// </summary>
    public static int PickCar(bool has4x, bool has8x, long? memoryMb, bool sharedMemory) =>
        has4x && has8x && memoryMb is { } mb && mb >= (sharedMemory ? LargeSharedMemoryMb : LargeVideoMemoryMb) ? HdTextureLarge : HdPackFolders.DefaultSize;

    private const int HdTextureLarge = 8;

    private static (int World, int Car)? _pick;
    /// <summary>The first start's World Textures and Car &amp; Dash Textures for this machine (4x, and <see cref="PickCar"/>, where <see cref="DefaultOn"/>; else 1997 both): the user folder and the adapter (RenderBackend.VideoMemoryMb; macOS shares the machine's memory), read once.</summary>
    public static (int World, int Car) FirstStart
    {
        get
        {
            if (_pick is { } p) return p;
            bool has4x = UserHas(HdPackFolders.Folder(HdPackFolders.DefaultSize)), has8x = UserHas(HdPackFolders.Folder(HdTextureLarge));
            long? mb = I76.Game.Boot.RenderBackend.VideoMemoryMb;
            return (_pick = Pick(has4x, has8x, mb, OperatingSystem.IsMacOS())).Value;
        }
    }

    /// <summary>
    /// The first start's pair: 4x and the cars' and the dash's pick where the 4x fits, else 1997 both. The 8x cockpit-and-cars pack
    /// alone (no 4x) stands on its own: it holds every car and cockpit texture, so the cars and the dash take its 8x and the world, which
    /// it has none of, stays 1997; it takes less memory than the 4x (about 0.5 GB against 0.7), so it goes where the 4x would.
    /// </summary>
    public static (int World, int Car) Pick(bool has4x, bool has8x, long? memoryMb, bool sharedMemory) =>
        DefaultOn(has4x, memoryMb) ? (HdPackFolders.DefaultSize, PickCar(has4x, has8x, memoryMb, sharedMemory))
        : !has4x && DefaultOn(has8x, memoryMb) ? (0, HdTextureLarge) : (0, 0);

    /// <summary>
    /// [port] texture_packs: the packs there were when the file was saved, in the file's words ("4x 8x", "tex-world", "none"), so
    /// that a 1997 saved with nothing else to choose is told from a 1997 chosen with a pack there.
    /// </summary>
    public static string PacksWord(int[] sizes, bool plain) =>
        sizes.Length == 0 && !plain ? "none" : string.Join(" ", sizes.Select(s => $"{s}x").Concat(plain ? [HdPackFolders.Plain] : []));

    /// <summary>This start's <see cref="PacksWord"/> (what Save writes).</summary>
    public static string PacksNow => PacksWord(AvailableSizes, UserHas(HdPackFolders.Plain));
    /// <summary>A texture pack of any size was found (the rows are shown).</summary>
    public static bool AnyPack => AvailableSizes.Length > 0 || UserHas(HdPackFolders.Plain);

    /// <summary>The sizes a <see cref="PacksWord"/> names (a plain tex-world as 0), or null for a word it did not write.</summary>
    public static int[]? ParsePacks(string? word)
    {
        if (word is null) return null;
        var parts = word.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["none"]) return [];
        var l = new List<int>();
        foreach (var p in parts)
            if (p == HdPackFolders.Plain) l.Add(0);
            else if (ParseWord(p) is int n && n > 0) l.Add(n);
            else return null;
        return [.. l];
    }

    /// <summary>
    /// The stored World Textures and Car &amp; Dash Textures with the packs installed since the file was saved: a row at 1997 saved
    /// when no pack was there (<paramref name="packsThen"/> empty) had nothing else to choose, so with a pack now it takes this
    /// machine's first-start choice; Car &amp; Dash at 4x saved without the 8x there takes the 8x where the first start would. A file
    /// from before [port] texture_packs (null) is read as saved without a pack when both rows are 1997 (with a pack there they start
    /// at 4x, so 1997 for both was a choice only with the rows changed by hand), else as saved with the packs it names. A 1997 saved
    /// with a pack there is the player's and stays.
    /// </summary>
    public static (int World, int Car) WithNewPacks((int World, int Car) stored, int[]? packsThen, int[] sizesNow, bool anyNow, (int World, int Car) firstStart)
    {
        if (!anyNow) return stored;
        packsThen ??= stored == (0, 0) ? [] : null;
        if (packsThen is null) return stored;
        var (w, c) = stored;
        if (packsThen.Length == 0)
        {
            if (w == 0) w = firstStart.World;
            if (c == 0) c = firstStart.Car;
        }
        if (c == HdPackFolders.DefaultSize && !packsThen.Contains(HdTextureLarge) && sizesNow.Contains(HdTextureLarge) && firstStart.Car == HdTextureLarge) c = HdTextureLarge;
        return (w, c);
    }

    public static HdSource? Source => State.Source;
    /// <summary>A pack was found.</summary>
    public static bool Installed => Source is not null;

    /// <summary>
    /// What the probe found, as one value: the pack and the folders a world texture and a car's or the cockpit's are looked in, in order
    /// (HdPackFolders.ChainFor; one source each for I76_TEX_DIR or a zip). Swapped whole, so a mission's build thread and the main thread
    /// asking at once both see a finished probe (one found the pack, the other saw none while it was being listed).
    /// </summary>
    private sealed record Probed(HdSource? Source, HdSource[] World, HdSource[] Car);
    private static volatile Probed? _state;
    private static readonly object Gate = new();

    private static Probed State
    {
        get
        {
            if (_state is { } s) return s;
            lock (Gate) return _state ??= Probe(_world, _car);
        }
    }

    private static Probed Probe(int worldSize, int carSize)
    {
        HdSource? source;
        HdSource[] worldChain = [], carChain = [];
        var env = System.Environment.GetEnvironmentVariable("I76_TEX_DIR") is { Length: > 0 } d ? Path.Combine(d, "world") : null;
        // a kind at 0 (1997) reads nothing, but the pack is still found (the rows are shown when one is there)
        var world = HdPackFolders.Chain(worldSize == 0 ? HdPackFolders.DefaultSize : worldSize, UserHas);
        var car = HdPackFolders.Chain(carSize == 0 ? HdPackFolders.DefaultSize : carSize, UserHas);
        if (env is not null && Directory.Exists(env)) source = new HdDirSource(env);
        else if (world.Length + car.Length > 0)
        {
            // one source per folder, shared by the two chains; the log's source is every folder, largest first
            var folders = world.Concat(car).Distinct().OrderByDescending(HdPackFolders.SizeOf).ToArray();
            var sources = folders.ToDictionary(f => f, f => HdPacks.Found.Get(f)!.Open());
            source = folders.Length == 1 ? sources[folders[0]] : new HdChainSource(folders.Select(f => sources[f]));
            worldChain = worldSize == 0 ? [] : [.. world.Select(f => sources[f])];
            carChain = carSize == 0 ? [] : [.. car.Select(f => sources[f])];
        }
        else source = null;
        if (world.Length + car.Length == 0 && source is { } one) { worldChain = worldSize == 0 ? [] : [one]; carChain = carSize == 0 ? [] : [one]; }
        GD.Print(source is null ? "HD textures: none (no I76_TEX_DIR/world, and no tex-world-Nx or tex-world folder or I26-HD-*.zip / i76-hd-*.zip holding one in the user folder or beside the program)"
            : $"HD textures: {source.Origin} ({source.Count} files); world {(worldSize == 0 ? "1997" : string.Join(" over ", world))}, cars and dash {(carSize == 0 ? "1997" : string.Join(" over ", car))}");
        return new Probed(source, worldChain, carChain);
    }

    /// <summary>
    /// Whether the member has an upscale in the pack for its kind's size (a file in any format; whatever the switch or the look): a
    /// texture the factory may swap (TextureFactory.SetHd). A lookup in the folders' lists, no read.
    /// </summary>
    public static bool Has(string member)
    {
        var s = State;
        if (s.Source is null) return false;
        foreach (var src in KindOf(member) == TextureKind.World ? s.World : s.Car)
            foreach (var rel in HdTexture.Candidates(member))
                if (src.Exists(rel)) return true;
        return false;
    }

    /// <summary>The member's upscale decoded to RGBA8 (rows top-down), or null for none or one that does not decode (whatever the switch: the factory decides what it serves).</summary>
    public static Image? Load(string member) => Read(member) is { } f ? Decode(f.Name, f.Bytes) : null;

    /// <summary>The member's upscale file as stored, or null for none (from one thread unless <see cref="ReadAnywhere"/>).</summary>
    /// <summary>Whether <see cref="Read"/> may run on worker threads (a folder; a zip is read from one).</summary>
    public static bool ReadAnywhere => Source?.ThreadSafe == true;

    public static (string Name, byte[] Bytes)? Read(string member)
    {
        var s = State;
        if (s.Source is null) return null;
        // the texture's kind's chain (a partial 8x over the 4x for the cars and the dash; none for a kind at 1997): the first folder that
        // has the texture in any format, so a .png of the 8x beats a .dds of the 4x
        foreach (var src in KindOf(member) == TextureKind.World ? s.World : s.Car)
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
