using System.Text.Json;
using I76.Formats.Imaging;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Cli;

/// <summary>
/// i76 [--install DIR] menu-export OUT [--pad-palette NAME.act] [--banks 33-64] [--only bg,bank,pad,load,glyphs]
/// Everything the HD menu art (tex-menu) is made from, in one folder, for tools/menu-pack.py:
///   bg/bgNN.ppm          the shell's background records 1..83 (as `bg` writes them)
///   bank/ID/NN.ppm       the sprite banks, keyed texels magenta, in record 26's palette (as `bank` writes them)
///   pad/NAME.ppm         the Esc pads (6NAME.vqm), in the palette --pad-palette names (default t01.act)
///   load/NAME.ppm        the loading cards (*load*.pcx, loadgame, loadscr) in their own palettes
///   glyphs/font69.json   record 69's glyphs (the garage's legend marks): advance and ink box per character, and the
///                        install's Lee_____.ttf beside it as glyphs/lee.ttf (the face the game redraws them from)
///   manifest.json        what was written, sizes and palettes
/// The output is the player's game data: keep it out of the repository.
/// I76_NATIVE=1: a Nitro database by its own record numbers (backgrounds 1..76; banks and their palette record native too).
/// </summary>
public static class MenuExportCommand
{
    public static readonly string[] Pads = ["abrtms1", "audcon1", "extgme1", "grxdet1", "mainmn1", "plyopt1", "savpnt1"];

    public static int Run(GameInstall install, List<string> argv)
    {
        if (argv.Count < 2) { Console.Error.WriteLine("usage: i76 --install DIR menu-export OUT [--pad-palette NAME.act] [--banks A-B] [--only bg,bank,pad,load,glyphs]"); return 2; }
        string outDir = argv[1];
        string padPalette = Opt(argv, "--pad-palette") ?? "t01.act";
        var only = (Opt(argv, "--only") ?? "bg,bank,pad,load,glyphs").Split(',').ToHashSet();
        var (b0, b1) = Range(Opt(argv, "--banks") ?? "33-64");
        bool native = Environment.GetEnvironmentVariable("I76_NATIVE") == "1";
        var vfs = new AssetVfs(install);
        var db = ShellDatabase.Open((install.FindFile("DATABASE.MW2") ?? install.FindFile("database.tvf")) ?? throw new FileNotFoundException("DATABASE.MW2"), translate: !native);
        var manifest = new Dictionary<string, object>();
        Directory.CreateDirectory(outDir);

        if (only.Contains("bg"))
        {
            var bgs = new Dictionary<string, int[]>();
            Directory.CreateDirectory(Path.Combine(outDir, "bg"));
            for (int n = 1; n <= (native ? 76 : 83); n++)
            {
                Pcx pcx;
                try { pcx = native ? db.BackgroundNative(n) : db.Background(n); } catch (Exception) { continue; }
                if (pcx.Width < 100) continue;
                WritePpm(Path.Combine(outDir, "bg", $"bg{n:00}.ppm"), pcx.Width, pcx.Height, k => pcx.Pixels[k], pcx.Palette);
                bgs[n.ToString()] = [pcx.Width, pcx.Height];
            }
            manifest["bg"] = bgs;
            Console.WriteLine($"bg: {bgs.Count} records");
        }

        if (only.Contains("bank"))
        {
            // record 26's palette, the main menu's (which 28 of the 32 backgrounds share): the game draws the banks in the
            // palette of the page they are on, and the HD art has one picture a sprite
            int palRec = native ? 22 : 26;
            var pal = (native ? db.BackgroundNative(palRec) : db.Background(palRec)).Palette;
            var banks = new Dictionary<string, Dictionary<string, int[]>>();
            for (int id = b0; id <= b1; id++)
            {
                var bank = native ? db.BankNative(id) : db.Bank(id);
                var sizes = new Dictionary<string, int[]>();
                for (int i = 0; i < bank.Count; i++)
                {
                    if (bank[i] is not { } sp) continue;
                    var dir = Path.Combine(outDir, "bank", id.ToString());
                    Directory.CreateDirectory(dir);
                    using var o = File.Create(Path.Combine(dir, $"{i:00}.ppm"));
                    o.Write(System.Text.Encoding.ASCII.GetBytes($"P6 {sp.Width} {sp.Height} 255\n"));
                    for (int k = 0; k < sp.Pixels.Length; k++)
                        if (sp.Opaque[k]) o.Write([pal[sp.Pixels[k] * 3], pal[sp.Pixels[k] * 3 + 1], pal[sp.Pixels[k] * 3 + 2]]);
                        else o.Write([(byte)255, (byte)0, (byte)255]);
                    sizes[i.ToString()] = [sp.Width, sp.Height];
                }
                if (sizes.Count > 0) banks[id.ToString()] = sizes;
            }
            manifest["bank"] = banks;
            manifest["bank_palette_record"] = palRec;
            Console.WriteLine($"bank: {banks.Count} banks, {banks.Values.Sum(b => b.Count)} sprites");
        }

        if (only.Contains("pad"))
        {
            // the pads are drawn in the mission's palette; the HD art was made in t01's (the first trip mission's), which
            // reproduces the 2026-11-03 export of all seven pads exactly
            var pal = new Palette(vfs.Read(padPalette)).Rgb;
            var pads = new Dictionary<string, int[]>();
            Directory.CreateDirectory(Path.Combine(outDir, "pad"));
            foreach (var name in Pads)
            {
                if (vfs.TryRead($"6{name}.vqm") is not { } d) continue;
                var v = new VqmImage(d);
                var px = v.Decode(new Codebook(vfs.Read(v.CodebookName)));
                WritePpm(Path.Combine(outDir, "pad", $"{name}.ppm"), v.Width, v.Height, k => px.Indices[k / v.Width * px.Width + k % v.Width], pal);
                pads[name] = [v.Width, v.Height];
            }
            manifest["pad"] = pads;
            manifest["pad_palette"] = padPalette;
            Console.WriteLine($"pad: {pads.Count} in {padPalette}");
        }

        if (only.Contains("load"))
        {
            var cards = new Dictionary<string, int[]>();
            Directory.CreateDirectory(Path.Combine(outDir, "load"));
            var names = new[] { "b01load", "loadgame", "loadscr" }.Concat(Enumerable.Range(1, 19).Select(i => $"p{i:00}load"));
            foreach (var name in names)
            {
                if (vfs.TryRead(name + ".pcx") is not { } d) continue;
                var pcx = Pcx.Parse(d);
                WritePpm(Path.Combine(outDir, "load", $"{name}.ppm"), pcx.Width, pcx.Height, k => pcx.Pixels[k], pcx.Palette);
                cards[name] = [pcx.Width, pcx.Height];
            }
            manifest["load"] = cards;
            Console.WriteLine($"load: {cards.Count} cards");
        }

        if (only.Contains("glyphs") && !native)
        {
            var f = db.Font(69);
            var glyphs = new Dictionary<string, object>();
            for (int c = 33; c < Math.Min(256, f.Count); c++)
            {
                if (f.Glyph((char)c) is not { } g) continue;
                int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
                for (int y = 0; y < f.Height; y++)
                    for (int x = 0; x < g.Width; x++)
                        if (g.Px[y * g.Width + x] != f.Key) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
                glyphs[((char)c).ToString()] = new { advance = g.Width, ink = x1 < 0 ? null : new[] { x0, y0, x1 + 1, y1 + 1 } };
            }
            Directory.CreateDirectory(Path.Combine(outDir, "glyphs"));
            File.WriteAllText(Path.Combine(outDir, "glyphs", "font69.json"), JsonSerializer.Serialize(new { record = 69, height = f.Height, glyphs }, new JsonSerializerOptions { WriteIndented = true }));
            if (install.FindFile("Lee_____.ttf") is { } lee) File.Copy(lee, Path.Combine(outDir, "glyphs", "lee.ttf"), true);
            manifest["glyphs"] = new { font69 = "glyphs/font69.json", lee = install.FindFile("Lee_____.ttf") is null ? null : "glyphs/lee.ttf" };
            Console.WriteLine("glyphs: record 69");
        }

        manifest["nitro"] = native;
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static string? Opt(List<string> argv, string name)
    {
        int i = argv.IndexOf(name);
        return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
    }

    private static (int, int) Range(string s)
    {
        var p = s.Split('-');
        return (int.Parse(p[0]), int.Parse(p[^1]));
    }

    private static void WritePpm(string path, int w, int h, Func<int, byte> index, byte[] pal)
    {
        var buf = new byte[w * h * 3];
        for (int k = 0; k < w * h; k++) { int c = index(k) * 3; buf[k * 3] = pal[c]; buf[k * 3 + 1] = pal[c + 1]; buf[k * 3 + 2] = pal[c + 2]; }
        using var o = File.Create(path);
        o.Write(System.Text.Encoding.ASCII.GetBytes($"P6 {w} {h} 255\n"));
        o.Write(buf);
    }
}
