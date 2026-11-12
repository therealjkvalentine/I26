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
/// Each draw notes the rectangle it wrote; Commit uploads only those (LiveTexture), the whole panel only when it was
/// started again from its original image. A redraw of one element (Region) puts the original image back in the element's
/// rectangle and replays the panel's draws clipped to it, which gives there the texels of the whole panel drawn again.
/// </summary>
public sealed class CockpitPanels(EltLayout layout, I76.Formats.Assembly.TextureResolver resolver, Assets.TextureFactory textures, Assets.MaterialLibrary materials)
{
    private sealed class Target
    {
        public required LiveTexture Live;
        /// <summary>The image's pixels as RGBA8 bytes, so a stamp is array copies, not one engine call per pixel.</summary>
        public required byte[] BaseBytes, WorkBytes;
        public int W, H;
        /// <summary>Its texels per original pixel.</summary>
        public int S;
        /// <summary>Started again from its original image since the last upload: it goes up whole (or by its difference, Diff).</summary>
        public bool Whole;
        /// <summary>Whole by a redraw that keeps most of the panel: the upload is the rows and columns that differ from <see cref="Before"/>.</summary>
        public bool Diff;
        public byte[]? Before;
        /// <summary>Uploaded since it was made or reloaded: the GPU holds WorkBytes as they stood at the last Commit.</summary>
        public bool Uploaded;
        /// <summary>Taken back as a look kept left it (Reload): its next redraw from the original image sends only what differs.</summary>
        public bool Restored;
        /// <summary>The rectangles written since the last upload, in texels, rows from the top of the picture.</summary>
        public readonly List<Rect2I> Dirty = [];
        /// <summary>The rectangle (texels, rows from the top) the draws are held to while an element is redrawn (Region, ClipTo).</summary>
        public Rect2I? Clip;
    }

    private readonly List<Rect2I> _upload = [];

    private readonly Dictionary<string, Target?> _targets = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Image?> _sheets = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, byte[]?> _sheetBytes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _scales = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The modern look's labels, made once per plate, text and scale: RGBA8 at the target's scale, stored bottom-up.</summary>
    private Dictionary<(string, string, int), byte[]> _labels = new();
    /// <summary>The modern look's counter drum cells, per strip, cell row, drum style, edge, housing colour and scale.</summary>
    private Dictionary<(string, int, DrumStyle, EdgeFade, (byte, byte, byte), int), byte[]> _cells = new();

    /// <summary>
    /// What a Reload makes for one look (what the factory serves and the panels' look settings): the panels' and sheets' pictures and
    /// what is made from them. Kept (<see cref="KeepLooks"/>) when the look changes, for a change back.
    /// </summary>
    private sealed record LookState(Dictionary<string, Image?> Sheets, Dictionary<string, byte[]?> SheetBytes, Dictionary<string, int> Scales,
        Dictionary<(string, string, int), byte[]> Labels, Dictionary<(string, int, DrumStyle, EdgeFade, (byte, byte, byte), int), byte[]> Cells,
        Dictionary<string, (byte[] Base, byte[] Work, int W, int H, int S)> Targets);
    private readonly Assets.LookKept<(bool Hd, int LabelScale, bool KeyRuled), LookState> _looks = new() { Keep = false };

    /// <summary>
    /// Keep each look's panels and sheets from the first change of look to the mission's end (TextureFactory.KeepBothLooks), so a
    /// change back makes nothing again: the pack's pictures are not read and decoded again.
    /// </summary>
    public bool KeepLooks { get => _looks.Keep; set => _looks.Keep = value; }

    private (bool, int, bool) LookKey => (textures.Hd, LabelScale, KeyRuledLines);

    /// <summary>Whether the panels for what is served now are kept (a Reload takes them as they were: no Prefetch wanted).</summary>
    public bool Holds => _looks.Has(LookKey);

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
    public Texture2D? Texture(string dst) => _targets.GetValueOrDefault(dst)?.Live.Texture;

    /// <summary>A live panel's texels per original pixel (1 until it is first drawn).</summary>
    public int Scale(string dst) => _targets.GetValueOrDefault(dst)?.S ?? 1;

    private string? Member(string tex) => resolver.FindByStem(System.IO.Path.GetFileNameWithoutExtension(tex));

    private Image? Load(string tex)
    {
        _looks.Made(LookKey);
        if (Member(tex) is not { } member || textures.GetRgba(member) is not { } t) return null;
        _scales[tex] = textures.Scale(member);
        return Image.CreateFromData(t.Width, t.Height, false, Image.Format.Rgba8, t.Rgba);
    }

    /// <summary>
    /// A pack's textures of the panels and their sheets (every block of the layout) decompressed together on worker threads, ahead of
    /// the first draws or a Reload: the factory does not keep a .dds's texels, so each load would decode it alone on the main thread.
    /// </summary>
    public void Prefetch() => textures.Prefetch(layout.Blocks.Select(b => Member(b.Texture)).OfType<string>(), rgba: true);

    /// <summary>A loaded texture's texels per original pixel.</summary>
    private int ScaleOf(string tex) => _scales.GetValueOrDefault(tex, 1);

    /// <summary>
    /// Makes the panels and sheets again from what the factory now serves (the originals or a pack's upscales): each live
    /// texture keeps its identity (the materials and the 2D dash hold it) and takes the new size, blank until redrawn.
    /// </summary>
    /// <returns>Whether every panel was taken back as a look kept left it (none made again).</returns>
    public bool Reload()
    {
        bool all = true;
        // the panels shown until now kept for a change back (their dictionaries go with them), and this look's if it was shown before
        LookState? kept = _looks.Change(LookKey, () => new LookState(_sheets, _sheetBytes, _scales, _labels, _cells,
            _targets.Where(kv => kv.Value is not null && kv.Value.Uploaded && !kv.Value.Whole && kv.Value.Dirty.Count == 0)
                .ToDictionary(kv => kv.Key, kv => (kv.Value!.BaseBytes, kv.Value.WorkBytes, kv.Value.W, kv.Value.H, kv.Value.S), StringComparer.OrdinalIgnoreCase)),
            out var found) ? found : null;
        if (kept is not null) (_sheets, _sheetBytes, _scales, _labels, _cells) = (kept.Sheets, kept.SheetBytes, kept.Scales, kept.Labels, kept.Cells);
        else (_sheets, _sheetBytes, _scales, _labels, _cells) = (new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), new(), new());
        foreach (var (dst, t) in _targets)
        {
            if (t is null) continue;
            t.Before = null; t.Whole = t.Diff = false; t.Dirty.Clear(); t.Clip = null;
            t.Live.KeepSizes = KeepLooks;
            // a look shown before: its panel as the GPU kept it (LiveTexture.Show), drawn as it was left, so the redraw that follows sends
            // only what differs from it (Restored), not the whole panel
            if (kept?.Targets.TryGetValue(dst, out var k) == true)
            {
                // (a panel of one size in both looks has one texture: the kept picture goes up whole instead)
                bool shown = t.Live.Show(k.W, k.H);
                if (!shown && k.W == t.Live.Width && k.H == t.Live.Height) { t.Live.Set(k.Work, k.W, k.H); shown = true; }
                if (shown)
                {
                    (t.BaseBytes, t.WorkBytes, t.W, t.H, t.S) = (k.Base, k.Work, k.W, k.H, k.S);
                    t.Uploaded = t.Restored = true;
                    continue;
                }
            }
            all = false;
            if (LoadTarget(dst) is not { } img) continue;
            var bytes = img.GetData();
            (t.W, t.H, t.S) = (img.GetWidth(), img.GetHeight(), ScaleOf(dst));
            t.BaseBytes = bytes; t.WorkBytes = (byte[])bytes.Clone(); t.Restored = false;
            t.Live.Set(t.WorkBytes, t.W, t.H);
            t.Uploaded = true;
        }
        return all && kept is not null;
    }

    private Target? TargetOf(string dst)
    {
        if (_targets.TryGetValue(dst, out var t)) return t;
        t = null;
        if (Member(dst) is { } member && LoadTarget(dst) is { } img)
        {
            var bytes = img.GetData();
            var live = new LiveTexture(bytes, img.GetWidth(), img.GetHeight());
            // the weapons panel's housings hang where the bare art is keyed: its texels key it (the specials' sit on the dash's opaque art)
            if (!Unbound.Contains(dst)) materials.OverrideTexture(member, live.Texture, ownKey: dst.Equals("zbk_.map", StringComparison.OrdinalIgnoreCase));
            t = new Target { Live = live, BaseBytes = bytes, WorkBytes = (byte[])bytes.Clone(), W = img.GetWidth(), H = img.GetHeight(), S = ScaleOf(dst), Uploaded = true };
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
        if (_sheets.TryGetValue(src, out var s)) return s;
        s = Load(src);
        if (s is not null && KeyRuledLines && src.StartsWith("zneedle", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = s.GetData();
            if (KeyRuledRows(bytes, s.GetWidth(), s.GetHeight()) > 0) s = Image.CreateFromData(s.GetWidth(), s.GetHeight(), false, Image.Format.Rgba8, bytes);
        }
        return _sheets[src] = s;
    }

    /// <summary>
    /// The modern look: the needle sheets' ruled lines keyed. zneedle6.map is a grid of 80-pixel cells, each a 79x79 frame and a
    /// light grey line along its foot and its right (in the packs a few texel rows, one of them crossed by a needle's tip); the layout's frames from the second row on start a row lower than their
    /// cells (81, 161, ...), so their last row is the line, which the gauges stamped under the dial as a bar across its width.
    /// The 1997 look keeps the sheet as it is. Takes effect at the next Reload.
    /// </summary>
    public bool KeyRuledLines { get; set; } = true;

    /// <summary>
    /// Keys (alpha 0) the light texels of every row of an RGBA sheet that is opaque and light across nearly its whole width (95 %:
    /// a ruled line, not a sprite's art, which never spans the sheet; a needle's tip that crosses the line keeps its texels);
    /// returns how many rows.
    /// </summary>
    public static int KeyRuledRows(byte[] rgba, int w, int h)
    {
        int n = 0;
        bool Light(int o) => rgba[o + 3] >= 128 && System.Math.Min(rgba[o], System.Math.Min(rgba[o + 1], rgba[o + 2])) >= 200;
        for (int y = 0; y < h; y++)
        {
            int o = y * w * 4, light = 0;
            for (int x = 0; x < w; x++) if (Light(o + x * 4)) light++;
            if (light * 20 < w * 19) continue;
            for (int x = 0; x < w; x++) if (Light(o + x * 4)) rgba[o + x * 4 + 3] = 0;
            n++;
        }
        return n;
    }

    /// <summary>
    /// Copies a w × h rectangle (the layout's pixels) of a sheet at (sx, sy) to (dx, dy) of a target, each at its
    /// own scale, the sheet's texels mapped nearest onto the target's; <paramref name="keyed"/>: a texel under alpha
    /// 0.5 leaves the target alone. The .elt counts rows from the top of the picture as shown, the maps store them
    /// bottom-up (a DIB's order): row y of the layout is image row h − 1 − y, in the sheets and the panels alike.
    /// <paramref name="clip"/>: only the target's texels inside it (texels, rows from the top of the picture) are written.
    /// </summary>
    public static void Blit(byte[] src, int sw, int sh, int ss, int sx, int sy, int w, int h, byte[] dst, int dw, int dh, int ds, int dx, int dy, bool keyed, Rect2I? clip = null)
    {
        // the target's columns this rectangle reaches: on the target and, through the nearest mapping, on the sheet
        int i0 = System.Math.Max(0, -dx * ds), i1 = System.Math.Min(w * ds, dw - dx * ds);
        while (i1 > i0 && sx * ss + (i1 - 1) * ss / ds >= sw) i1--;
        int j0 = 0, j1 = h * ds;
        if (clip is { } c)
        {
            i0 = System.Math.Max(i0, c.Position.X - dx * ds); i1 = System.Math.Min(i1, c.End.X - dx * ds);
            j0 = System.Math.Max(0, c.Position.Y - dy * ds); j1 = System.Math.Min(j1, c.End.Y - dy * ds);
        }
        if (i1 <= i0 || j1 <= j0) return;
        // a texel is four bytes: copied as one 32-bit word, row by row (a whole row at once where nothing is keyed or resampled)
        var s32 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(src.AsSpan(0, src.Length & ~3));
        var d32 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(dst.AsSpan(0, dst.Length & ~3));
        bool same = ss == ds;
        for (int j = j0; j < j1; j++)
        {
            int y = sy * ss + j * ss / ds, oy = dy * ds + j;
            if (y >= sh || oy < 0 || oy >= dh) continue;
            int srow = (sh - 1 - y) * sw, drow = (dh - 1 - oy) * dw + dx * ds;
            if (same && !keyed) { s32.Slice(srow + sx * ss + i0, i1 - i0).CopyTo(d32.Slice(drow + i0, i1 - i0)); continue; }
            for (int i = i0; i < i1; i++)
            {
                int so = srow + sx * ss + (same ? i : i * ss / ds);
                if (keyed && src[so * 4 + 3] < 128) continue;
                d32[drow + i] = s32[so];
            }
        }
    }

    /// <summary>
    /// Starts a redraw of a dst texture from its original image. <paramref name="diff"/>: the redraw will change little of it
    /// (a plate lit, a diode, a counter): the upload is what differs from the panel as the GPU holds it, found at Commit.
    /// </summary>
    public void Clear(string dst, bool diff = false)
    {
        if (TargetOf(dst) is not { } t) return;
        if (!t.Whole)
        {
            t.Diff = (diff || t.Restored) && t.Uploaded && t.Dirty.Count == 0;
            if (t.Diff)
            {
                // the panel as it stood at the last upload (what the GPU holds), kept to compare with
                if (t.Before?.Length != t.WorkBytes.Length) t.Before = new byte[t.WorkBytes.Length];
                System.Buffer.BlockCopy(t.WorkBytes, 0, t.Before, 0, t.WorkBytes.Length);
            }
        }
        else if (!diff) t.Diff = false;
        System.Array.Copy(t.BaseBytes, t.WorkBytes, t.BaseBytes.Length);
        t.Whole = true;
        t.Dirty.Clear();
        t.Clip = null;
    }

    /// <summary>
    /// Starts a redraw of one element: the original image put back in the layout's rectangle of a dst texture, and every draw
    /// after it held to that rectangle until <see cref="EndClip"/>. The caller then replays the panel's draws in their order,
    /// which leaves there what the whole panel drawn again would (each draw writes its own texels from its source alone).
    /// </summary>
    public void Region(string dst, Rect2I rect)
    {
        if (TargetOf(dst) is not { } t) return;
        if (Texels(t, rect) is not { } r) { t.Clip = new Rect2I(); return; }
        for (int j = r.Position.Y; j < r.End.Y; j++)
        {
            int o = ((t.H - 1 - j) * t.W + r.Position.X) * 4;
            System.Buffer.BlockCopy(t.BaseBytes, o, t.WorkBytes, o, r.Size.X * 4);
        }
        Mark(t, r);
        t.Clip = r;
    }

    /// <summary>Holds the draws on a dst texture to the layout's rectangle until <see cref="EndClip"/>, nothing put back.</summary>
    public void ClipTo(string dst, Rect2I rect)
    {
        if (TargetOf(dst) is { } t) t.Clip = Texels(t, rect) ?? new Rect2I();
    }

    public void EndClip(string dst)
    {
        if (_targets.GetValueOrDefault(dst) is { } t) t.Clip = null;
    }

    /// <summary>A layout rectangle in a target's texels (rows from the top), cut to the texture; null if nothing is left.</summary>
    private static Rect2I? Texels(Target t, Rect2I rect)
    {
        var r = new Rect2I(rect.Position * t.S, rect.Size * t.S).Intersection(new Rect2I(0, 0, t.W, t.H));
        return r.Size.X > 0 && r.Size.Y > 0 ? r : null;
    }

    /// <summary>Notes the layout's rectangle (x, y, w, h) as written on a target (within its clip).</summary>
    private static void Mark(Target t, int x, int y, int w, int h)
    {
        if (t.Whole || Texels(t, new Rect2I(x, y, w, h)) is not { } r) return;
        if (t.Clip is { } c) { r = r.Intersection(c); if (r.Size.X <= 0 || r.Size.Y <= 0) return; }
        Mark(t, r);
    }

    private static void Mark(Target t, Rect2I r)
    {
        if (!t.Whole) AddRect(t.Dirty, r);
    }

    /// <summary>
    /// Adds a rectangle to a list of them, merged with any it overlaps (again until none do); past <paramref name="max"/>
    /// rectangles the list becomes their bounding box.
    /// </summary>
    public static void AddRect(List<Rect2I> list, Rect2I r, int max = 32)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        for (int i = 0; i < list.Count; i++)
        {
            var o = list[i];
            if (r.Position.X < o.End.X && o.Position.X < r.End.X && r.Position.Y < o.End.Y && o.Position.Y < r.End.Y)
            {
                r = r.Merge(o);
                list.RemoveAt(i);
                i = -1;
            }
        }
        list.Add(r);
        if (list.Count <= max) return;
        var all = list[0];
        foreach (var o in list) all = all.Merge(o);
        list.Clear();
        list.Add(all);
    }

    /// <summary>A sprite's rectangle in the layout's pixels with its top-left at an anchor (moved dx), or null if either is missing;
    /// <paramref name="up"/>: drawn a row above its label (<see cref="RowsUp"/>).</summary>
    public Rect2I? Footprint(string sprite, string anchor, int dx = 0, bool up = false) =>
        layout.Find(anchor) is { } a && layout.Find(sprite) is { } s ? new Rect2I(a.Label.X + dx, a.Label.Y - RowsUp(s.Label, up), s.Label.W, s.Label.H) : null;

    /// <summary>
    /// How many rows above its label a cell is drawn, on its sheet and on the panel alike: one where <paramref name="up"/> asks
    /// for it and the sheet has a row above the label, else none. The damage panel's sheet holds every part's art but the engine
    /// plate's a row above its label (the front chevron's point, the plates' and bars' top edges), the label's last row being the
    /// next state's first or empty; the original draws that row and not the last, the art where the label puts it.
    /// </summary>
    public static int RowsUp(EltLabel s, bool up) => up && s.Y > 0 ? 1 : 0;

    /// <summary>The same at a dst block's anchor (an anchor name the layout repeats across its blocks, as the 2D gauges' are).</summary>
    public Rect2I? FootprintIn(string dst, string sprite, string anchor) =>
        layout.FindIn(dst, anchor) is { } a && layout.Find(sprite) is { } s ? new Rect2I(a.X, a.Y, s.Label.W, s.Label.H) : null;

    /// <summary>Stamps the sprite <paramref name="sprite"/> (a src label) with its top-left at (x, y) of its dst texture.</summary>
    /// <summary><paramref name="up"/>: the sprite's rectangle a row higher on the sheet and on the target (<see cref="RowsUp"/>).</summary>
    public void Stamp(string dst, string sprite, int x, int y, bool up = false)
    {
        if (TargetOf(dst) is not { } t || layout.Find(sprite) is not { } s || Sheet(s.Texture) is not { } sheet) return;
        var r = s.Label;
        int u = RowsUp(r, up);
        Blit(SheetBytes(s.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(s.Texture), r.X, r.Y - u, r.W, r.H, t.WorkBytes, t.W, t.H, t.S, x, y - u, keyed: true, t.Clip);
        Mark(t, x, y - u, r.W, r.H);
    }

    /// <summary>Stamps a sprite at a dst anchor label (its texture is the anchor's).</summary>
    public void StampAt(string sprite, string anchor, int dx = 0, int dy = 0, bool up = false)
    {
        if (layout.Find(anchor) is { } a && !a.Label.Name.Equals(sprite, StringComparison.OrdinalIgnoreCase)) Stamp(a.Texture, sprite, a.Label.X + dx, a.Label.Y + dy, up);
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
        Blit(label, r.W * t.S, r.H * t.S, t.S, 0, 0, r.W, r.H, t.WorkBytes, t.W, t.H, t.S, a.Label.X, a.Label.Y, keyed: true, t.Clip);
        Mark(t, a.Label.X, a.Label.Y, r.W, r.H);
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
        if (deepInk) DeepenInk(t.WorkBytes, t.W, t.H, t.S, a.Label.X + dx, a.Label.Y, tp.Label.W, tp.Label.H, t.Clip);
    }

    /// <summary>
    /// The modern look's counter drum: VWindow's cell for <paramref name="digit"/> (0..10 down the strip) with its figure
    /// set again in the counters' font (CounterDigits), at the panel's scale, made once per cell and scale. Without the
    /// font, the strip's cell with its ink deepened. <paramref name="housing"/>: the housing colour read when the panel was last
    /// drawn whole (a redraw of the counters alone, where what is round the window now may differ); returns the one used.
    /// </summary>
    public (byte R, byte G, byte B)? Digit(string dst, string anchor, string top, string bottom, int digit, int dx = 0, (byte R, byte G, byte B)? housing = null)
    {
        if (CounterDigits.Font is not { } font) { VWindow(dst, anchor, top, bottom, digit * 0.1f, dx, deepInk: true); return null; }
        if (TargetOf(dst) is not { } t || layout.Find(anchor) is not { } a || layout.Find(top) is not { } tp || layout.Find(bottom) is not { } bt || Sheet(tp.Texture) is not { } sheet) return null;
        int sy = tp.Label.Y + (int)((bt.Label.Y - tp.Label.Y) * digit * 0.1f + 0.5f);
        if (sy == tp.Label.Y) sy = bt.Label.Y;
        int w = tp.Label.W, h = tp.Label.H;
        // the housing round the window as the panel stands (its bracket stamped before the digits): what the window's edges fade toward
        housing ??= HousingAround(t.WorkBytes, t.W, t.H, t.S, a.Label.X + dx, a.Label.Y, w, h);
        // made once per strip, cell, look, housing and scale; found again without a string made
        var key = (tp.Texture, sy, CounterDigits.Style, CounterDigits.Edge, housing.Value, t.S);
        if (!_cells.TryGetValue(key, out var cell))
        {
            cell = new byte[w * t.S * h * t.S * 4];
            Blit(SheetBytes(tp.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(tp.Texture), tp.Label.X, sy, w, h, cell, w * t.S, h * t.S, t.S, 0, 0, keyed: false);
            if (w == CounterDigits.CellW && h == CounterDigits.CellH) CounterDigits.Paint(cell, t.S, digit % 10, font, CounterDigits.Style, CounterDigits.Edge, housing.Value);
            _cells[key] = cell;
        }
        Blit(cell, w * t.S, h * t.S, t.S, 0, 0, w, h, t.WorkBytes, t.W, t.H, t.S, a.Label.X + dx, a.Label.Y, keyed: false, t.Clip);
        Mark(t, a.Label.X + dx, a.Label.Y, w, h);
        return housing;
    }

    /// <summary>
    /// The mean colour of a target's texels in the <see cref="HousingBand"/> layout pixels above and below a w × h window
    /// at (x, y) of the layout, over the window's width (rows bottom-up as Blit's; off the target left out): the housing
    /// the counter window sits in, which CounterDigits fades the drum's edges toward. The default housing grey where
    /// nothing is there to read.
    /// </summary>
    public static (byte R, byte G, byte B) HousingAround(byte[] dst, int dw, int dh, int ds, int x, int y, int w, int h)
    {
        long r = 0, g = 0, b = 0; int n = 0;
        void Rows(int j0, int j1)
        {
            for (int j = Math.Max(0, j0); j < Math.Min(dh, j1); j++)
                for (int i = Math.Max(0, x * ds); i < Math.Min(dw, (x + w) * ds); i++)
                {
                    int o = ((dh - 1 - j) * dw + i) * 4;
                    if (dst[o + 3] < 128) continue;
                    r += dst[o]; g += dst[o + 1]; b += dst[o + 2]; n++;
                }
        }
        Rows((y - HousingBand) * ds, y * ds);
        Rows((y + h) * ds, (y + h + HousingBand) * ds);
        return n == 0 ? CounterDigits.DefaultHousing : ((byte)((r + n / 2) / n), (byte)((g + n / 2) / n), (byte)((b + n / 2) / n));
    }

    /// <summary>How far above and below the counter window the housing is read, in layout pixels (the bezel's own two).</summary>
    public const int HousingBand = 2;

    /// <summary>
    /// The counter digits' ink taken darker in a w × h rectangle (the layout's pixels) of a target at <paramref name="ds"/>
    /// texels a pixel, rows bottom-up as Blit's: a texel under <see cref="InkKnee"/> in brightness is scaled by
    /// (brightness / knee) ^ <see cref="InkGamma"/>, so the strip's darkest ink, (41, 41, 20), comes to about a third of
    /// itself, its grey anti-aliasing steps less, and the white drum not at all; the hue is kept.
    /// </summary>
    public static void DeepenInk(byte[] dst, int dw, int dh, int ds, int x, int y, int w, int h, Rect2I? clip = null)
    {
        for (int j = y * ds; j < (y + h) * ds; j++)
            for (int i = x * ds; i < (x + w) * ds; i++)
            {
                if (i < 0 || j < 0 || i >= dw || j >= dh) continue;
                if (clip is { } cr && !cr.HasPoint(new Vector2I(i, j))) continue;
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
        Blit(SheetBytes(sheetName)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(sheetName), sx, sy, w, h, t.WorkBytes, t.W, t.H, t.S, dx, dy, keyed: false, t.Clip);
        Mark(t, dx, dy, w, h);
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
        var d = img.GetData();
        if (StampInto(d, img.GetWidth(), img.GetHeight(), ds, dst, sprite, anchor)) img.SetData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, d);
    }

    /// <summary>
    /// The same into an RGBA8 picture's bytes (w x h), with no image in between, and only inside <paramref name="clip"/> (texels,
    /// rows from the top) if one is given; false if the sprite or its anchor is missing.
    /// </summary>
    public bool StampInto(byte[] d, int w, int h, int ds, string dst, string sprite, string anchor, Rect2I? clip = null)
    {
        if (layout.FindIn(dst, anchor) is not { } a || layout.Find(sprite) is not { } s || Sheet(s.Texture) is not { } sheet) return false;
        var r = s.Label;
        Blit(SheetBytes(s.Texture)!, sheet.GetWidth(), sheet.GetHeight(), ScaleOf(s.Texture), r.X, r.Y, r.W, r.H, d, w, h, ds, a.X, a.Y, keyed: true, clip);
        return true;
    }

    /// <summary>A sprite's rectangle stamped at a dst block's anchor, in texels at <paramref name="ds"/> a pixel (rows from the top), or null.</summary>
    public Rect2I? StampRect(int ds, string dst, string sprite, string anchor) =>
        layout.FindIn(dst, anchor) is { } a && layout.Find(sprite) is { } s ? new Rect2I(a.X * ds, a.Y * ds, s.Label.W * ds, s.Label.H * ds) : null;

    /// <summary>
    /// Uploads what was redrawn since the last call: a panel started again whole (or, for a redraw that keeps most of it, the
    /// rows and columns that differ from what the GPU holds), otherwise only the rectangles its draws wrote.
    /// </summary>
    public void Commit()
    {
        foreach (var (name, t) in _targets)
        {
            if (t is null) continue;
            t.Clip = null;
            t.Restored = false;
            _upload.Clear();
            if (t.Whole)
            {
                if (!t.Diff) t.Live.Set(t.WorkBytes, t.W, t.H);
                else
                {
                    Differ(t.Before!, t.WorkBytes, t.W, t.H, _upload);
                    if (_upload.Count > 0) t.Live.Update(t.WorkBytes, _upload);
                }
                t.Whole = t.Diff = false;
            }
            else if (t.Dirty.Count > 0)
            {
                // the texture's rows are stored bottom-up: a rectangle's rows from the top become stored rows
                foreach (var r in t.Dirty) _upload.Add(new Rect2I(r.Position.X, t.H - r.End.Y, r.Size.X, r.Size.Y));
                t.Live.Update(t.WorkBytes, _upload);
                t.Dirty.Clear();
            }
            else continue;
            t.Uploaded = true;
            if (DashStats.Verify) DashStats.Check(name, t.Live.Expected(t.WorkBytes), t.Live.ReadBack());
        }
        if (System.Environment.GetEnvironmentVariable("I76_PIT_DUMP") is { } dir && Godot.Engine.GetProcessFrames() == 15)
        {
            foreach (var (name, t) in _targets) if (t is not null) Image.CreateFromData(t.W, t.H, false, Image.Format.Rgba8, t.WorkBytes).SavePng(System.IO.Path.Combine(dir, name + ".png"));
            foreach (var (name, s) in _sheets) s?.SavePng(System.IO.Path.Combine(dir, "src_" + name + ".png"));
        }
    }

    /// <summary>
    /// The rectangles (rows as stored) where two w x h RGBA8 pictures differ: each run of differing rows, cut into bands of at
    /// most <paramref name="band"/> rows, with the columns from the band's leftmost to its rightmost difference (a diagonal
    /// change, the radar's sweep line, is a staircase of narrow bands rather than its whole bounding box).
    /// </summary>
    public static void Differ(byte[] a, byte[] b, int w, int h, List<Rect2I> into, int band = 32)
    {
        var a32 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(a.AsSpan(0, w * h * 4));
        var b32 = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(b.AsSpan(0, w * h * 4));
        int run = -1, lo = 0, hi = 0;
        for (int y = 0; y <= h; y++)
        {
            int l = -1, r = -1;
            if (y < h)
            {
                var ra = a32.Slice(y * w, w);
                var rb = b32.Slice(y * w, w);
                if (!ra.SequenceEqual(rb))
                {
                    l = 0; while (ra[l] == rb[l]) l++;
                    r = w - 1; while (ra[r] == rb[r]) r--;
                }
            }
            if (run >= 0 && (l < 0 || y - run >= band))
            {
                AddRect(into, new Rect2I(lo, run, hi - lo + 1, y - run));
                run = -1;
            }
            if (l >= 0)
            {
                if (run < 0) (run, lo, hi) = (y, l, r);
                else (lo, hi) = (System.Math.Min(lo, l), System.Math.Max(hi, r));
            }
        }
    }

    /// <summary>Lets the panels' GPU textures go (the cockpit leaving the tree).</summary>
    public void Free()
    {
        _looks.Release();
        foreach (var t in _targets.Values) t?.Live.Free();
    }
}
