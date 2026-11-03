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
/// the art's RENAME is; small notes in the install's Lee. Without the face, the shell's sans (font 0x47).
/// </summary>
public static class PrintedType
{
    /// <summary>The faces: the labels' Arial, the buttons' Arial Bold, and the form's handwriting (the install's Lee) for small notes.</summary>
    public enum Face { Label, Button, Hand }
    /// <summary>The install's Lee_____.ttf, for <see cref="Face.Hand"/>.</summary>
    public static string? LeePath { get; set; }
    private static readonly Dictionary<(Face, bool), Font?> Faces = [];
    private static readonly Dictionary<(string, int, Face, Color), (ImageTexture Tex, float W)?> Pictures = [];
    private static readonly Dictionary<(string, int, Face), (byte[] A, int W, int H)?> Pixels = [];
    private static readonly Dictionary<Face, int> CapAt200 = [];

    private static Font? FontOf(Face face, bool modern)
    {
        if (Faces.TryGetValue((face, modern), out var f)) return f;
        // the 1997 look: the labels from the face's own hinted one-bit rendering, a button's and the hand's from their grey one
        var aa = modern || face != Face.Label ? TextServer.FontAntialiasing.Gray : TextServer.FontAntialiasing.None;
        var hinting = modern ? TextServer.Hinting.None : TextServer.Hinting.Normal;
        if (face == Face.Hand)
        {
            if (LeePath is null) return null;
            var lee = new FontFile { Antialiasing = aa, Hinting = hinting, SubpixelPositioning = TextServer.SubpixelPositioning.Disabled };
            f = lee.LoadDynamicFont(LeePath) == Error.Ok ? lee : null;
        }
        else f = HdAssets.Arial(face == Face.Button, aa, hinting);
        return Faces[(face, modern)] = f;
    }

    /// <summary>The face's capital H at the raster size, in px (the scale of everything else).</summary>
    private static int CapHeight(Face face)
    {
        if (CapAt200.TryGetValue(face, out int h)) return h;
        return CapAt200[face] = FontOf(face, true) is { } f && ShellGlyphs.Raster(f, "H") is { } r ? r.H : 143;
    }

    /// <summary>How wide the words are, in shell pixels, in either look (the layout is the same in both).</summary>
    public static float Measure(ShellDatabase db, string text, int cap, Face face = Face.Label)
    {
        if (FontOf(face, true) is not { } f || ShellGlyphs.Raster(f, text) is not { } r) return db.Font(0x47).Measure(text);
        return r.W * (float)cap / CapHeight(face);
    }

    public static float Draw(ShellFrame frame, ShellDatabase db, float x, int baseline, string text, int cap, Color ink, bool bold = false, bool centre = false) =>
        Draw(frame, db, x, baseline, text, cap, ink, bold ? Face.Button : Face.Label, centre);

    /// <summary>
    /// The words with their foot on the row above <paramref name="baseline"/> (capitals from baseline − cap to
    /// baseline − 1), left-aligned at x or centred on it. Returns their width.
    /// </summary>
    public static float Draw(ShellFrame frame, ShellDatabase db, float x, int baseline, string text, int cap, Color ink, Face face, bool centre = false)
    {
        bool modern = frame.Modern;
        if (FontOf(face, true) is null) return Fallback(frame, db, x, baseline, text, ink, centre);
        float w = Measure(db, text, cap, face);
        if (centre) x -= w / 2;
        if (modern)
        {
            if (Picture(text, cap, face, ink) is not { } p) return w;
            frame.Picture(new Rect2I((int)MathF.Round(x), baseline - cap, (int)MathF.Ceiling(p.W), cap), p.Tex);
            return w;
        }
        if (Raster1997(text, cap, face) is not { } r) return w;
        // centred words keep their middle where the measured width put it
        int x0 = centre ? (int)MathF.Round(x + w / 2 - r.W / 2f) : (int)MathF.Round(x), y0 = baseline - r.H;
        byte solid = frame.Nearest(ink);
        var mixed = new Dictionary<(byte, byte), byte>();
        for (int yy = 0; yy < r.H; yy++)
            for (int xx = 0; xx < r.W; xx++)
            {
                int a = r.A[yy * r.W + xx];
                // the labels hard-edged, as the shell's own fonts; a button's small bold letters and the hand's small note mixed into the paper
                if (face == Face.Label) { if (a >= 128) frame.Pixel(x0 + xx, y0 + yy, solid); continue; }
                if (a < 40) continue;
                byte under = frame.At(x0 + xx, y0 + yy), q = (byte)(a * 4 / 255);
                if (!mixed.TryGetValue((under, q), out byte v))
                {
                    var pal = frame.Palette;
                    var paper = Color.Color8(pal[under * 3], pal[under * 3 + 1], pal[under * 3 + 2]);
                    mixed[(under, q)] = v = frame.Nearest(paper.Lerp(ink, Math.Min(1f, q / 4f + (face == Face.Hand ? 0.5f : 0.125f))));
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
    private static (ImageTexture Tex, float W)? Picture(string text, int cap, Face face, Color ink)
    {
        if (Pictures.TryGetValue((text, cap, face, ink), out var p)) return p;
        (ImageTexture, float)? made = null;
        if (FontOf(face, true) is { } f && ShellGlyphs.Raster(f, text) is { } r)
        {
            const int S = ShellGlyphs.Scale;
            float k = (float)cap * S / CapHeight(face);
            int tw = Math.Max(1, (int)MathF.Round(r.W * k)), th = Math.Max(1, (int)MathF.Round(r.H * k));
            var cov = Image.CreateFromData(r.W, r.H, false, Image.Format.L8, r.A);
            cov.Resize(tw, th, Image.Interpolation.Lanczos);
            var a = cov.GetData();
            float w = r.W * (float)cap / CapHeight(face);
            int cw = (int)MathF.Ceiling(w) * S, ch = cap * S;
            var rgba = new byte[cw * ch * 4];
            // the ink's foot on the box's foot (words lower than a capital sit on the line)
            int dy = ch - th;
            for (int y = Math.Max(0, -dy); y < th && y + dy < ch; y++)
                for (int x = 0; x < Math.Min(tw, cw); x++)
                {
                    int o = ((y + dy) * cw + x) * 4;
                    (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = ((byte)ink.R8, (byte)ink.G8, (byte)ink.B8, a[y * tw + x]);
                }
            var img = Image.CreateFromData(cw, ch, false, Image.Format.Rgba8, rgba);
            img.GenerateMipmaps();
            made = (ImageTexture.CreateFromImage(img), w);
        }
        return Pictures[(text, cap, face, ink)] = made;
    }

    /// <summary>The words' coverage at scale pixels a shell pixel (trimmed to the ink, its foot the words' foot), in either look's rendering.</summary>
    internal static (byte[] A, int W, int H)? Coverage(string text, int cap, Face face, int scale, bool modern)
    {
        if (FontOf(face, modern) is not { } f) return null;
        return ShellGlyphs.Raster(f, text, Math.Max(4, (int)MathF.Round(cap * scale * 200f / CapHeight(face))));
    }

    /// <summary>The words at the 640x480 frame's own size: the face at the pixel size whose capital H is cap px.</summary>
    private static (byte[] A, int W, int H)? Raster1997(string text, int cap, Face face)
    {
        if (Pixels.TryGetValue((text, cap, face), out var r)) return r;
        int size = Math.Max(4, (int)MathF.Round(cap * 200f / CapHeight(face)));
        return Pixels[(text, cap, face)] = FontOf(face, false) is { } f ? ShellGlyphs.Raster(f, text, size) : null;
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

    /// <summary>The scrap's paper: the form's own lightened (grain kept; the one used), or nearer white (no such colours in the 1997 look's palette, which turns it green-grey).</summary>
    public enum Paper { Lightened, Whiter }
    public static Paper ScrapPaper = Paper.Lightened;
    private static readonly Dictionary<string, (ImageTexture Tex, Rect2I At)> Scraps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sketch on a scrap of paper laid on the form (the port's): the form's paper (a blank patch of the same art, from
    /// <paramref name="art"/> at <paramref name="paperAt"/>) made lighter, its edges torn, turned a degree or two (by the
    /// arena's name), with the span as a legend on it. In the modern look a picture with a soft shadow; in the 1997 look its
    /// pixels matched to the palette, the shadow a hard pixel's. <paramref name="scrap"/> is the paper's rectangle and
    /// (lx, ly) the legend's left and foot, both in the sketch box's own pixels; the box's top left goes at (bx, by).
    /// </summary>
    public static void DrawScrap(ShellFrame frame, byte[] art, (int X, int Y) paperAt, string stem, ArenaSketch s, int bx, int by,
        Rect2I scrap, string legend, int lx, int ly, int cap, Color legendInk)
    {
        bool modern = frame.Modern;
        int k = modern ? Scale : 1;
        const int M = 4;
        var canvas = scrap.Grow(M);
        string key = $"{stem}:{s.BoxWidth}x{s.BoxHeight}:{scrap}:{ScrapPaper}";
        if (modern && Scraps.TryGetValue(key, out var have))
        {
            frame.Picture(new Rect2I(bx + have.At.Position.X, by + have.At.Position.Y, have.At.Size.X, have.At.Size.Y), have.Tex);
            return;
        }
        int W = canvas.Size.X * k, H = canvas.Size.Y * k;
        var ink = InkLayer(s, stem, k, !modern);
        int iw = s.BoxWidth * k, ih = s.BoxHeight * k;
        var lg = PrintedType.Coverage(legend, cap, PrintedType.Face.Hand, k, modern);
        // a degree or two either way, the same for an arena every time
        uint hsh = 2166136261;
        foreach (char ch in stem.ToLowerInvariant()) hsh = (hsh ^ ch) * 16777619;
        float ang = (1f + (hsh % 100) / 100f * 0.8f) * ((hsh & 0x100) != 0 ? 1 : -1) * MathF.PI / 180;
        float ca = MathF.Cos(ang), sa = MathF.Sin(ang);
        float mx = scrap.Position.X + scrap.Size.X / 2f, my = scrap.Position.Y + scrap.Size.Y / 2f;
        float X0 = scrap.Position.X, Y0 = scrap.Position.Y, X1 = scrap.End.X, Y1 = scrap.End.Y;
        static float Hash(int i) { uint u = (uint)(i * 374761393 + 668265263); u = (u ^ (u >> 13)) * 1274126177; return ((u ^ (u >> 16)) & 0xffff) / 65535f; }
        static float Noise(float t) { int i = (int)MathF.Floor(t); float f = t - i; f = f * f * (3 - 2 * f); return Hash(i) + (Hash(i + 1) - Hash(i)) * f; }
        // how far in from the rectangle's edge the torn edge runs at (p, q): a slow wander and a fine fray
        float Inside(float p, float q)
        {
            float l = p - X0, r = X1 - p, t = q - Y0, b = Y1 - q;
            float d = MathF.Min(MathF.Min(l, r), MathF.Min(t, b));
            float along = d == l || d == r ? q + (d == l ? 0 : 500) : p + (d == t ? 1000 : 1500);
            return d - (0.3f + 1.2f * Noise(along / 6f) + 0.5f * Noise(along * 1.3f));
        }
        var pal = frame.Palette;
        (float R, float G, float B) PaperAt(float p, float q)
        {
            int c = (int)MathF.Floor(p - X0), r = (int)MathF.Floor(q - Y0);
            int m = ((c % 400) + 400) % 400, sc = m < 200 ? m : 399 - m;
            int mr = ((r % 220) + 220) % 220, sr = mr < 110 ? mr : 219 - mr;
            int i = art[(paperAt.Y + sr) * ShellFrame.W + paperAt.X + sc] * 3;
            float R = pal[i], G = pal[i + 1], B = pal[i + 2];
            return ScrapPaper == Paper.Lightened
                ? (R + (255 - R) * 0.42f, G + (255 - G) * 0.42f, B + (255 - B) * 0.42f)
                : (R + (250 - R) * 0.7f, G + (246 - G) * 0.7f, B + (234 - B) * 0.7f);
        }
        float Sample(byte[] a, int w, int h, float x, float y, int stride, int off)
        {
            // bilinear in the modern look, nearest in the 1997 look's pixels
            if (!modern) { int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y); return xi < 0 || yi < 0 || xi >= w || yi >= h ? 0 : a[(yi * w + xi) * stride + off]; }
            x -= 0.5f; y -= 0.5f;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0, acc = 0;
            for (int j = 0; j < 4; j++)
            {
                int xx = x0 + (j & 1), yy = y0 + (j >> 1);
                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                acc += a[(yy * w + xx) * stride + off] * ((j & 1) == 1 ? fx : 1 - fx) * ((j >> 1) == 1 ? fy : 1 - fy);
            }
            return acc;
        }
        var rgba = modern ? new byte[W * H * 4] : null;
        var mixed = new Dictionary<int, byte>();
        for (int oy = 0; oy < H; oy++)
            for (int ox = 0; ox < W; ox++)
            {
                float u = canvas.Position.X + (ox + 0.5f) / k - mx, v = canvas.Position.Y + (oy + 0.5f) / k - my;
                float p = mx + ca * u + sa * v, q = my - sa * u + ca * v;
                float din = Inside(p, q);
                float cover = modern ? Math.Clamp(din * k + 0.5f, 0, 1) : din > 0 ? 1 : 0;
                // the shadow: the scrap's own shape a little down and right, soft (modern) or one hard pixel (1997)
                float shadow = modern ? Math.Clamp((Inside(p - 1.2f, q - 1.6f) + 1.5f) / 3f, 0, 1) * 0.3f
                                      : Inside(p - 1, q - 1) > 0 ? 1 : 0;
                if (cover <= 0 && shadow <= 0) continue;
                var (R, G, B) = PaperAt(p, q);
                float ia = Sample(ink, iw, ih, p * k, q * k, 4, 3) / 255f;
                if (ia > 0)
                {
                    // the ink's colour from the nearest inked pixel (the layer's colour is straight, its alpha the coverage)
                    int xi = Math.Clamp((int)(p * k), 0, iw - 1), yi = Math.Clamp((int)(q * k), 0, ih - 1), o = (yi * iw + xi) * 4;
                    float ir = ink[o + 3] > 0 ? ink[o] : 40, ig = ink[o + 3] > 0 ? ink[o + 1] : 30, ib = ink[o + 3] > 0 ? ink[o + 2] : 18;
                    // the inks laid on the paper (multiplied, as pen on paper)
                    R = R * (1 - ia) + R * ir / 255 * ia; G = G * (1 - ia) + G * ig / 255 * ia; B = B * (1 - ia) + B * ib / 255 * ia;
                }
                if (lg is { } L)
                {
                    float la = Sample(L.A, L.W, L.H, (p - lx) * k, (q - (ly - (float)L.H / k)) * k, 1, 0) / 255f;
                    if (!modern) la = la >= 0.35f ? 1 : 0;
                    R = R * (1 - la) + legendInk.R8 * la; G = G * (1 - la) + legendInk.G8 * la; B = B * (1 - la) + legendInk.B8 * la;
                }
                if (modern)
                {
                    float a = cover + shadow * (1 - cover);
                    int o = (oy * W + ox) * 4;
                    // straight alpha: the paper where it covers, the shadow's dark brown under its edge
                    rgba![o] = (byte)Math.Clamp((R * cover + 40 * shadow * (1 - cover)) / a, 0, 255);
                    rgba[o + 1] = (byte)Math.Clamp((G * cover + 30 * shadow * (1 - cover)) / a, 0, 255);
                    rgba[o + 2] = (byte)Math.Clamp((B * cover + 18 * shadow * (1 - cover)) / a, 0, 255);
                    rgba[o + 3] = (byte)Math.Clamp(a * 255 + 0.5f, 0, 255);
                    continue;
                }
                int fx = bx + canvas.Position.X + ox, fy = by + canvas.Position.Y + oy;
                int keyc;
                if (cover > 0) keyc = (int)R / 4 << 12 | (int)G / 4 << 6 | (int)B / 4;
                else
                {
                    byte under = frame.At(fx, fy);
                    keyc = 1 << 24 | under;
                }
                if (!mixed.TryGetValue(keyc, out byte idx))
                {
                    Color c = cover > 0 ? Color.Color8((byte)R, (byte)G, (byte)B)
                        : Color.Color8((byte)(pal[(keyc & 0xff) * 3] * 0.72f), (byte)(pal[(keyc & 0xff) * 3 + 1] * 0.72f), (byte)(pal[(keyc & 0xff) * 3 + 2] * 0.72f));
                    mixed[keyc] = idx = frame.Nearest(c);
                }
                frame.Pixel(fx, fy, idx);
            }
        if (!modern) return;
        var img = Image.CreateFromData(W, H, false, Image.Format.Rgba8, rgba);
        img.GenerateMipmaps();
        var tex = ImageTexture.CreateFromImage(img);
        Scraps[key] = (tex, canvas);
        frame.Picture(new Rect2I(bx + canvas.Position.X, by + canvas.Position.Y, canvas.Size.X, canvas.Size.Y), tex);
    }

    private static byte[] InkLayer(ArenaSketch s, string stem, int k, bool aliased)
    {
        string key = $"{stem}:{s.BoxWidth}x{s.BoxHeight}:{k}:{aliased}";
        if (!Aliased.TryGetValue(key, out var px)) Aliased[key] = px = s.Render(k, aliased);
        return px;
    }
}
