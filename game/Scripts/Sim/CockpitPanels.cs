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
        _sheets.Clear(); _sheetBytes.Clear(); _scales.Clear();
        foreach (var (dst, t) in _targets)
        {
            if (t is null || Load(dst) is not { } img) continue;
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
        if (Member(dst) is { } member && Load(dst) is { } img)
        {
            var tex = ImageTexture.CreateFromImage(img);
            if (!Unbound.Contains(dst)) materials.OverrideTexture(member, tex);
            var bytes = img.GetData();
            t = new Target { Base = img, Work = (Image)img.Duplicate(), Texture = tex, BaseBytes = bytes, WorkBytes = (byte[])bytes.Clone(), W = img.GetWidth(), H = img.GetHeight(), S = ScaleOf(dst) };
        }
        return _targets[dst] = t;
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

    /// <summary>The vertical twin, a T.w × T.h window at sy = T.y + round((B.y − T.y)·frac) (sy == T.y reads B.y). The rolling digits.</summary>
    public void VWindow(string dst, string anchor, string top, string bottom, float frac)
    {
        if (TargetOf(dst) is not { } t || layout.Find(anchor) is not { } a || layout.Find(top) is not { } tp || layout.Find(bottom) is not { } bt || Sheet(tp.Texture) is not { } sheet) return;
        int sy = tp.Label.Y + (int)((bt.Label.Y - tp.Label.Y) * frac + 0.5f);
        if (sy == tp.Label.Y) sy = bt.Label.Y;
        Copy(tp.Texture, sheet, tp.Label.X, sy, tp.Label.W, tp.Label.H, t, a.Label.X, a.Label.Y);
    }

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
