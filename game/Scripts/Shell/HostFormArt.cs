using Godot;
using I76.Formats.Mission;
using I76.Formats.Shell;
using I76.Formats.Vfs;

namespace I76.Game.Shell;

/// <summary>
/// Words set in the form's printed type (the port's labels on the host form: its rules' names, the MELEE and A.I. CARS
/// buttons): Arial at the cap height asked, the art's own TEAM PLAY being Helvetica at 8 px caps. The modern look draws
/// them at the window's resolution, anti-aliased (a picture glyph on the text layer); the 1997 look into the frame's
/// pixels, the labels hard-edged as the shell's own fonts are, a button's small bold letters mixed into the palette as
/// the art's RENAME is. Without the face, the shell's sans (font 0x47).
/// </summary>
public static class PrintedType
{
    private static Font? _regular, _bold, _regular1997, _bold1997;
    private static readonly Dictionary<(string, int, bool, Color), (ImageTexture Tex, float W)?> Pictures = [];
    private static readonly Dictionary<(string, int, bool), (byte[] A, int W, int H)?> Pixels = [];
    private static readonly Dictionary<bool, int> CapAt200 = [];

    private static Font? Face(bool bold, bool modern)
    {
        if (modern) return bold ? _bold ??= HdAssets.Arial(true, TextServer.FontAntialiasing.Gray, TextServer.Hinting.None)
                                : _regular ??= HdAssets.Arial(false, TextServer.FontAntialiasing.Gray, TextServer.Hinting.None);
        // the 1997 look: the labels from the face's own hinted one-bit rendering, a button's from its grey one (mixed below)
        return bold ? _bold1997 ??= HdAssets.Arial(true, TextServer.FontAntialiasing.Gray, TextServer.Hinting.Normal)
                    : _regular1997 ??= HdAssets.Arial(false, TextServer.FontAntialiasing.None, TextServer.Hinting.Normal);
    }

    /// <summary>The face's capital H at the raster size, in px (the scale of everything else).</summary>
    private static int CapHeight(bool bold)
    {
        if (CapAt200.TryGetValue(bold, out int h)) return h;
        return CapAt200[bold] = Face(bold, true) is { } f && ShellGlyphs.Raster(f, "H") is { } r ? r.H : 143;
    }

    /// <summary>How wide the words are, in shell pixels, in either look (the layout is the same in both).</summary>
    public static float Measure(ShellDatabase db, string text, int cap, bool bold = false)
    {
        if (Face(bold, true) is not { } f || ShellGlyphs.Raster(f, text) is not { } r) return db.Font(0x47).Measure(text);
        return r.W * (float)cap / CapHeight(bold);
    }

    /// <summary>
    /// The words with their capitals' foot on the row above <paramref name="baseline"/> (caps from baseline − cap to
    /// baseline − 1), left-aligned at x or centred on it. Returns their width.
    /// </summary>
    public static float Draw(ShellFrame frame, ShellDatabase db, float x, int baseline, string text, int cap, Color ink, bool bold = false, bool centre = false)
    {
        bool modern = frame.Modern;
        if (Face(bold, true) is null) return Fallback(frame, db, x, baseline, text, ink, centre);
        float w = Measure(db, text, cap, bold);
        if (centre) x -= w / 2;
        if (modern)
        {
            if (Picture(text, cap, bold, ink) is not { } p) return w;
            frame.Picture(new Rect2I((int)MathF.Round(x), baseline - cap, (int)MathF.Ceiling(p.W), cap), p.Tex);
            return w;
        }
        if (Raster1997(text, cap, bold) is not { } r) return w;
        // centred words keep their middle where the measured width put it
        int x0 = centre ? (int)MathF.Round(x + w / 2 - r.W / 2f) : (int)MathF.Round(x), y0 = baseline - r.H;
        byte solid = frame.Nearest(ink);
        var mixed = new Dictionary<(byte, byte), byte>();
        for (int yy = 0; yy < r.H; yy++)
            for (int xx = 0; xx < r.W; xx++)
            {
                int a = r.A[yy * r.W + xx];
                if (!bold) { if (a >= 128) frame.Pixel(x0 + xx, y0 + yy, solid); continue; }
                if (a < 40) continue;
                byte under = frame.At(x0 + xx, y0 + yy), q = (byte)(a * 4 / 255);
                if (!mixed.TryGetValue((under, q), out byte v))
                {
                    var pal = frame.Palette;
                    var paper = Color.Color8(pal[under * 3], pal[under * 3 + 1], pal[under * 3 + 2]);
                    mixed[(under, q)] = v = frame.Nearest(paper.Lerp(ink, Math.Min(1f, q / 4f + 0.125f)));
                }
                frame.Pixel(x0 + xx, y0 + yy, v);
            }
        return w;
    }

    private static float Fallback(ShellFrame frame, ShellDatabase db, float x, int baseline, string text, Color ink, bool centre)
    {
        var f = db.Font(0x47);
        var remap = new byte[256];
        byte i = frame.Nearest(ink);
        for (int k = 0; k < 256; k++) remap[k] = k == f.Key ? (byte)0xff : i;
        int w = f.Measure(text);
        frame.Text(f, (int)(centre ? x - w / 2f : x), baseline - 9, text, remap);
        return w;
    }

    /// <summary>The words as a picture at <see cref="ShellGlyphs.Scale"/> texels a shell pixel, in the ink, and their width in shell pixels.</summary>
    private static (ImageTexture Tex, float W)? Picture(string text, int cap, bool bold, Color ink)
    {
        if (Pictures.TryGetValue((text, cap, bold, ink), out var p)) return p;
        (ImageTexture, float)? made = null;
        if (Face(bold, true) is { } f && ShellGlyphs.Raster(f, text) is { } r)
        {
            const int S = ShellGlyphs.Scale;
            float k = (float)cap * S / CapHeight(bold);
            int tw = Math.Max(1, (int)MathF.Round(r.W * k)), th = Math.Max(1, (int)MathF.Round(r.H * k));
            var cov = Image.CreateFromData(r.W, r.H, false, Image.Format.L8, r.A);
            cov.Resize(tw, th, Image.Interpolation.Lanczos);
            var a = cov.GetData();
            float w = r.W * (float)cap / CapHeight(bold);
            int cw = (int)MathF.Ceiling(w) * S, ch = cap * S;
            var rgba = new byte[cw * ch * 4];
            for (int y = 0; y < Math.Min(th, ch); y++)
                for (int x = 0; x < Math.Min(tw, cw); x++)
                {
                    int o = (y * cw + x) * 4;
                    (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)ink.R8, (byte)ink.G8, (byte)ink.B8, a[y * tw + x]);
                }
            var img = Image.CreateFromData(cw, ch, false, Image.Format.Rgba8, rgba);
            img.GenerateMipmaps();
            made = (ImageTexture.CreateFromImage(img), w);
        }
        return Pictures[(text, cap, bold, ink)] = made;
    }

    /// <summary>The words at the 640x480 frame's own size: the face at the pixel size whose capital H is cap px.</summary>
    private static (byte[] A, int W, int H)? Raster1997(string text, int cap, bool bold)
    {
        if (Pixels.TryGetValue((text, cap, bold), out var r)) return r;
        int size = Math.Max(4, (int)MathF.Round(cap * 200f / CapHeight(bold)));
        return Pixels[(text, cap, bold)] = Face(bold, false) is { } f ? ShellGlyphs.Raster(f, text, size) : null;
    }
}

/// <summary>
/// The host form's arena sketch (<see cref="ArenaSketch"/>, the port's), one per mission file: built on first use from
/// the install's terrain and placements, drawn as a picture in the modern look and into the frame's pixels in the 1997 look.
/// </summary>
public static class ArenaSketches
{
    /// <summary>Pixels of the picture per shell pixel in the modern look (the box is 400 px wide in a 1280-wide window).</summary>
    public const int Scale = 4;
    private static readonly Dictionary<string, ArenaSketch?> Sketches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageTexture> Textures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, byte[]> Aliased = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The mission's sketch for a box of w x h shell pixels, or null (no file, no terrain, nothing placed).</summary>
    public static ArenaSketch? Of(GameInstall install, string stem, int w, int h)
    {
        string key = $"{stem}:{w}x{h}";
        if (Sketches.TryGetValue(key, out var s)) return s;
        try
        {
            if (install.FindMission(stem) is { } path)
            {
                var msn = new MissionFile(path);
                if (I76.Formats.Assembly.MissionWorld.FindTerrain(install, msn) is { } ter)
                    s = ArenaSketch.Of(msn, TerrainTiles.Load(msn, ter), w, h);
            }
        }
        catch (Exception e) { GD.PrintErr($"arena sketch: {stem}: {e.Message}"); }
        return Sketches[key] = s;
    }

    /// <summary>The sketch over the box at (x, y): a picture in the modern look; in the 1997 look its ink multiplied into the paper and matched to the palette.</summary>
    public static void Draw(ShellFrame frame, string stem, ArenaSketch s, int x, int y)
    {
        string key = $"{stem}:{s.BoxWidth}x{s.BoxHeight}";
        if (frame.Modern)
        {
            if (!Textures.TryGetValue(key, out var tex))
            {
                var img = Image.CreateFromData(s.BoxWidth * Scale, s.BoxHeight * Scale, false, Image.Format.Rgba8, s.Render(Scale, aliased: false));
                img.GenerateMipmaps();
                Textures[key] = tex = ImageTexture.CreateFromImage(img);
            }
            frame.Picture(new Rect2I(x, y, s.BoxWidth, s.BoxHeight), tex);
            return;
        }
        if (!Aliased.TryGetValue(key, out var px)) Aliased[key] = px = s.Render(1, aliased: true);
        var pal = frame.Palette;
        var mixed = new Dictionary<(byte, int), byte>();
        for (int yy = 0; yy < s.BoxHeight; yy++)
            for (int xx = 0; xx < s.BoxWidth; xx++)
            {
                int o = (yy * s.BoxWidth + xx) * 4, a = px[o + 3];
                if (a < 40) continue;
                byte under = frame.At(x + xx, y + yy);
                int ink = px[o] << 16 | px[o + 1] << 8 | px[o + 2] | a << 24;
                if (!mixed.TryGetValue((under, ink), out byte v))
                {
                    float al = a / 255f;
                    float P(int c, int i) => pal[under * 3 + c] * (1 - al) + pal[under * 3 + c] * px[o + i] / 255f * al;
                    mixed[(under, ink)] = v = frame.Nearest(Color.Color8((byte)P(0, 0), (byte)P(1, 1), (byte)P(2, 2)));
                }
                frame.Pixel(x + xx, y + yy, v);
            }
    }
}
