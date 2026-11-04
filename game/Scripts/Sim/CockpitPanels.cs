using Godot;
using I76.Formats.Cockpit;

namespace I76.Game.Sim;

/// <summary>
/// The cockpit's live panel textures (vpit_1.elt): each dst texture (the
/// damage panel zsy_, the weapon panel zbk_, the gear strip zgear101, the
/// compass zcm_, ...) is redrawn from its original image with sprites from
/// the src sheets stamped at the layout's anchors, and every material that
/// draws the texture is pointed at the live copy. Palette index 255 (alpha
/// 0) in a sprite leaves the panel showing through. The layout's anchors
/// and rectangles are the originals' pixels; a panel or a sheet served
/// upscaled (TextureFactory.Scale) is worked at its own scale, the sheet's
/// texels mapped nearest onto the panel's where the two differ. When what is served changes (the HD Textures row,
/// a flip of the look) Reload makes every panel again at the new scale, in the same textures.
/// </summary>
public sealed class CockpitPanels(EltLayout layout, I76.Formats.Assembly.TextureResolver resolver, Assets.TextureFactory textures, Assets.MaterialLibrary materials)
{
    private sealed class Target
    {
        public required Image Base, Work;
        public required ImageTexture Texture;
        /// <summary>The image's pixels as RGBA8 bytes, so a stamp is array copies, not one engine call per pixel.</summary>
        public required byte[] BaseBytes, WorkBytes;
        public int W, H;
        /// <summary>Its texels per original pixel.</summary>
        public int S;
        public bool Dirty;
    }

    private readonly Dictionary<string, Target?> _targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Image?> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _sheetBytes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _scales = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The modern look's labels, made once per plate, text and scale: RGBA8 at the target's scale, stored bottom-up.</summary>
    private readonly Dictionary<(string, string, int), byte[]> _labels = new();

    /// <summary>The panels the modern look's labels are drawn on (the weapons and the specials panels).</summary>
    private static readonly HashSet<string> Labelled = new(["zbk_.map", "zbks_.map"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The fewest texels per original pixel the labelled panels are worked at: 2 in the modern look, so its labels are
    /// drawn finer than the original's pixels without the pack (the panel's own pixels doubled, nearest); 1 in the 1997 look.
    /// Takes effect at the next Reload.
    /// </summary>
    public int LabelScale { get; set; } = 2;

    public EltLayout Layout => layout;

    /// <summary>
    /// The 2D gauges' dials: the original composes them on handles of their
    /// own, not on cockpit parts, so the 3D dash's faces keep
    /// their own picture; they are drawn only on the screen.
    /// </summary>
    private static readonly HashSet<string> Unbound = new(["zspeedo6.map", "ztach6.map", "zspeedo3.map", "ztach3.map"], StringComparer.OrdinalIgnoreCase);

    /// <summary>A dst texture's live copy, or null until it is first drawn (the original's empty descriptor). Rows bottom-up, as stored.</summary>
    public Texture2D? Texture(string dst) => _targets.GetValueOrDefault(dst)?.Texture;

    /// <summary>A live panel's texels per original pixel (1 until it is first drawn).</summary>
    public int Scale(string dst) => _targets.GetValueOrDefault(dst)?.S ?? 1;

    private string? Member(string tex) => resolver.FindByStem(System.IO.Path.GetFileNameWithoutExtension(tex));

    private Image? Load(string tex)
    {
        if (Member(tex) is not { } member || textures.GetRgba(member) is not { } t) return null;
        _scales[tex] = textures.Scale(member);
        return Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, t.Rgba);
    }

    /// <summary>A loaded texture's texels per original pixel.</summary>
    private int ScaleOf(string tex) => _scales.GetValueOrDefault(tex, 1);

    /// <summary>
    /// Makes the panels and sheets again from what the factory now serves (the originals or a pack's upscales): each live
    /// texture keeps its identity (the materials and the 2D dash hold it) and takes the new size, blank until redrawn.
    /// </summary>
    public void Reload()
    {
        _sheets.Clear(); _sheetBytes.Clear(); _scales.Clear(); _labels.Clear();
        foreach (var (dst, t) in _targets)
        {
            if (t is null || LoadTarget(dst) is not { } img) continue;
            var bytes = img.GetData();
            t.Base = img; t.Work = (Image)img.Duplicate(); t.BaseBytes = bytes; t.WorkBytes = (byte[])bytes.Clone();
            t.W = img.GetWidth(); t.H = img.GetHeight(); t.S = ScaleOf(dst); t.Dirty = false;
            t.Texture.SetImage(t.Work);
        }
    }

    private Target? TargetOf(string dst)
    {
        if (_targets.TryGetValue(dst, out var t)) return t;
        t = null;
        if (Member(dst) is { } member && LoadTarget(dst) is { } img)
        {
            var tex = ImageTexture.CreateFromImage(img);
            // the weapons panel's housings hang where the bare art is keyed: its texels key it (the specials' sit on the dash's opaque art)
            if (!Unbound.Contains(dst)) materials.OverrideTexture(member, tex, ownKey: dst.Equals("zbk_.map", StringComparison.OrdinalIgnoreCase));
            var bytes = img.GetData();
            t = new Target { Base = img, Work = (Image)img.Duplicate(), Texture = tex, BaseBytes = bytes, WorkBytes = (byte[])bytes.Clone(), W = img.GetWidth(), H = img.GetHeight(), S = ScaleOf(dst) };
        }
        return _targets[dst] = t;
    }

    /// <summary>A dst texture's original image, a labelled panel's doubled (nearest) up to LabelScale.</summary>
    private Image? LoadTarget(string dst)
    {
        if (Load(dst) is not { } img) return null;
        int s = ScaleOf(dst);
        if (Labelled.Contains(dst) && s < LabelScale && LabelScale % s == 0)
        {
            int k = LabelScale / s;
            img.Resize(img.GetWidth() * k, img.GetHeight() * k, Image.Interpolation.Nearest);
            _scales[dst] = LabelScale;
        }
        return img;
    }

    private Image? Sheet(string src)
    {
        if (!_sheets.TryGetValue(src, out var s)) _sheets[src] = s = Load(src);
        return s;
    }

    /// <summary>
    /// Copies a w × h rectangle (the layout's pixels) of a sheet at (sx, sy) to (dx, dy) of a target, each at its
    /// own scale, the sheet's texels mapped nearest onto the target's; <paramref name="keyed"/>: a texel under alpha
    /// 0.5 leaves the target alone. The .elt counts rows from the top of the picture as shown, the maps store them
    /// bottom-up (a DIB's order): row y of the layout is image row h − 1 − y, in the sheets and the panels alike.
    /// </summary>
    public static void Blit(byte[] src, int sw, int sh, int ss, int sx, int sy, int w, int h, byte[] dst, int dw, int dh, int ds, int dx, int dy, bool keyed)
    {
        for (int j = 0; j < h * ds; j++)
            for (int i = 0; i < w * ds; i++)
            {
                int x = sx * ss + i * ss / ds, y = sy * ss + j * ss / ds, ox = dx * ds + i, oy = dy * ds + j;
                if (x >= sw || y >= sh || ox < 0 || oy < 0 || ox >= dw || oy >= dh) continue;
                int so = ((sh - 1 - y) * sw + x) * 4;
                if (keyed && src[so + 3] < 128) continue;
                System.Buffer.BlockCopy(src, so, dst, ((dh - 1 - oy) * dw + ox) * 4, 4);
            }
    }

    /// <summary>Starts a redraw of a dst texture from its original image.</summary>
    public void Clear(string dst)
    {
        if (TargetOf(dst) is not { } t) return;
        System.Array.Copy(t.BaseBytes, t.WorkBytes, t.BaseBytes.Length);
        t.Dirty = true;
    }

    /// <summary>Stamps the sprite <paramref name="sprite"/> (a src label) with its top-left at (x, y) of its dst texture.</summary>
    public void Stamp(string dst, string sprite, int x, int y)
    {
        if (TargetOf(dst) is not { } t || layout.Find(sprite) is not { } s || Sheet(s.Texture) is not { } sheet) return;
        var r = s.Label;
        Blit(SheetBytes(s.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(s.Texture), r.X, r.Y, r.W, r.H, t.WorkBytes, t.W, t.H, t.S, x, y, keyed: true);
        t.Dirty = true;
    }

    /// <summary>Stamps a sprite at a dst anchor label (its texture is the anchor's).</summary>
    public void StampAt(string sprite, string anchor, int dx = 0, int dy = 0)
    {
        if (layout.Find(anchor) is { } a && !a.Label.Name.Equals(sprite, StringComparison.OrdinalIgnoreCase)) Stamp(a.Texture, sprite, a.Label.X + dx, a.Label.Y + dy);
    }

    /// <summary>
    /// The modern look's label at a dst anchor: the plate <paramref name="sprite"/> (or <paramref name="fallback"/>, the
    /// EMPTY plate, for a plate the sheet has not got) with its painted letters replaced by <paramref name="text"/> in the
    /// label font (DymoLabels), at the panel's scale. Without the font, the plate as painted.
    /// </summary>
    public void StampLabel(string sprite, string fallback, string anchor, string text, bool lit)
    {
        if (layout.Find(anchor) is not { } a || TargetOf(a.Texture) is not { } t) return;
        if (layout.Find(sprite) is null) sprite = fallback;
        if (DymoLabels.Font is not { } font) { StampAt(sprite, anchor); return; }
        if (layout.Find(sprite) is not { } s || Sheet(s.Texture) is not { } sheet) return;
        var r = s.Label;
        var key = (sprite.ToLowerInvariant(), text + (lit ? "|on" : "|off"), t.S);
        if (!_labels.TryGetValue(key, out var label))
        {
            label = new byte[r.W * t.S * r.H * t.S * 4];
            Blit(SheetBytes(s.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(s.Texture), r.X, r.Y, r.W, r.H, label, r.W * t.S, r.H * t.S, t.S, 0, 0, keyed: false);
            DymoLabels.Paint(label, r.W, r.H, t.S, font, text, lit);
            _labels[key] = label;
        }
        Blit(label, r.W * t.S, r.H * t.S, t.S, 0, 0, r.W, r.H, t.WorkBytes, t.W, t.H, t.S, a.Label.X, a.Label.Y, keyed: true);
        t.Dirty = true;
    }

    /// <summary>
    /// A horizontal window into a strip, copied opaque to an
    /// anchor. sx = L.x + round((R.x − L.x)·frac), and sx == L.x reads R.x;
    /// (L.w + 1) × (L.h + 1) pixels. The compass.
    /// </summary>
    public void Window(string dst, string anchor, string left, string right, float frac)
    {
        if (TargetOf(dst) is not { } t || layout.Find(anchor) is not { } a || layout.Find(left) is not { } l || layout.Find(right) is not { } r || Sheet(l.Texture) is not { } sheet) return;
        int sx = l.Label.X + (int)((r.Label.X - l.Label.X) * frac + 0.5f);
        if (sx == l.Label.X) sx = r.Label.X;
        Copy(l.Texture, sheet, sx, l.Label.Y, l.Label.W + 1, l.Label.H + 1, t, a.Label.X, a.Label.Y);
    }

    /// <summary>
    /// The vertical twin, a T.w × T.h window at sy = T.y + round((B.y − T.y)·frac) (sy == T.y reads B.y), copied to the
    /// anchor moved <paramref name="dx"/> pixels across. The rolling digits; <paramref name="deepInk"/> (the modern look)
    /// draws their dark ink deeper (<see cref="DeepenInk"/>).
    /// </summary>
    public void VWindow(string dst, string anchor, string top, string bottom, float frac, int dx = 0, bool deepInk = false)
    {
        if (TargetOf(dst) is not { } t || layout.Find(anchor) is not { } a || layout.Find(top) is not { } tp || layout.Find(bottom) is not { } bt || Sheet(tp.Texture) is not { } sheet) return;
        int sy = tp.Label.Y + (int)((bt.Label.Y - tp.Label.Y) * frac + 0.5f);
        if (sy == tp.Label.Y) sy = bt.Label.Y;
        Copy(tp.Texture, sheet, tp.Label.X, sy, tp.Label.W, tp.Label.H, t, a.Label.X + dx, a.Label.Y);
        if (deepInk) DeepenInk(t.WorkBytes, t.W, t.H, t.S, a.Label.X + dx, a.Label.Y, tp.Label.W, tp.Label.H);
    }

    /// <summary>
    /// The counter digits' ink taken darker in a w × h rectangle (the layout's pixels) of a target at <paramref name="ds"/>
    /// texels a pixel, rows bottom-up as Blit's: a texel under <see cref="InkKnee"/> in brightness is scaled by
    /// (brightness / knee) ^ <see cref="InkGamma"/>, so the strip's darkest ink, (41, 41, 20), comes to about a third of
    /// itself, its grey anti-aliasing steps less, and the white drum not at all; the hue is kept.
    /// </summary>
    public static void DeepenInk(byte[] dst, int dw, int dh, int ds, int x, int y, int w, int h)
    {
        for (int j = y * ds; j < (y + h) * ds; j++)
            for (int i = x * ds; i < (x + w) * ds; i++)
            {
                if (i < 0 || j < 0 || i >= dw || j >= dh) continue;
                int o = ((dh - 1 - j) * dw + i) * 4;
                float l = 0.299f * dst[o] + 0.587f * dst[o + 1] + 0.114f * dst[o + 2];
                if (l >= InkKnee) continue;
                float k = MathF.Pow(l / InkKnee, InkGamma);
                for (int c = 0; c < 3; c++) dst[o + c] = (byte)(dst[o + c] * k + 0.5f);
            }
    }

    /// <summary>The brightness under which the counters' ink is deepened, and how hard (DeepenInk).</summary>
    public const float InkKnee = 150, InkGamma = 0.8f;

    private void Copy(string sheetName, Image sheet, int sx, int sy, int w, int h, Target t, int dx, int dy)
    {
        Blit(SheetBytes(sheetName)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(sheetName), sx, sy, w, h, t.WorkBytes, t.W, t.H, t.S, dx, dy, keyed: false);
        t.Dirty = true;
    }

    private byte[]? SheetBytes(string src)
    {
        if (!_sheetBytes.TryGetValue(src, out var b)) _sheetBytes[src] = b = Sheet(src)?.GetData();
        return b;
    }

    /// <summary>
    /// Stamps a sprite into a given image at an anchor of the layout's dst block (the radar's current frame, a picture
    /// of its own: zrad.map is only the layout's name for it), the image at <paramref name="ds"/> texels a pixel.
    /// </summary>
    public void StampInto(Image img, int ds, string dst, string sprite, string anchor)
    {
        if (layout.FindIn(dst, anchor) is not { } a || layout.Find(sprite) is not { } s || Sheet(s.Texture) is not { } sheet) return;
        var r = s.Label;
        var d = img.GetData();
        Blit(SheetBytes(s.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(s.Texture), r.X, r.Y, r.W, r.H, d, img.GetWidth(), img.GetHeight(), ds, a.X, a.Y, keyed: true);
        img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, d);
    }

    /// <summary>Uploads the textures redrawn since the last call.</summary>
    public void Commit()
    {
        foreach (var t in _targets.Values)
            if (t is { Dirty: true }) { t.Work.SetData(t.W, t.H, false, Image.Format.Rgba8, t.WorkBytes); t.Texture.Update(t.Work); t.Dirty = false; }
        if (System.Environment.GetEnvironmentVariable("I76_PIT_DUMP") is { } dir && Godot.Engine.GetProcessFrames() == 15)
        {
            foreach (var (name, t) in _targets) t?.Work.SavePng(System.IO.Path.Combine(dir, name + ".png"));
            foreach (var (name, s) in _sheets) s?.SavePng(System.IO.Path.Combine(dir, "src_" + name + ".png"));
        }
    }
}
