using Godot;
using I76.Formats.Shell;

namespace I76.Game.Shell;

/// <summary>
/// The shell's 640x480 8 bpp display: a clean copy of the background, a work copy that widgets draw into
/// and restore from the clean one, and the background's own palette. Sprites
/// and fonts are palette indices drawn into whatever palette the background
/// installed, as in the original. After a background load the first present
/// is black (the shell writes a black palette, flips, then the real one).
///
/// In the modern look the strings in the fonts <see cref="HdFonts"/> accepts
/// are not blitted: each glyph is kept as a record of its cell (the bitmap
/// glyph's advance by the font's height), its character and its ink, for
/// ShellTextLayer to draw at the window's resolution. Each cell's pixels are
/// marked as the glyph's, and whatever writes a pixel later (a restore, a
/// sprite, a fill, other text) takes it back, so a glyph lives as long as most
/// of its cell is left untouched — as long as the original's pixels would
/// have stayed on screen.
/// </summary>
public sealed class ShellFrame
{
    public const int W = 640, H = 480;
    private readonly byte[] _clean = new byte[W * H];
    private readonly byte[] _work = new byte[W * H];
    public byte[] Palette { get; private set; } = new byte[768];
    private bool _dirty = true, _black;
    /// <summary>The background record shown (0 before any, or when loaded from a picture alone).</summary>
    public int Record { get; private set; }
    /// <summary>On Nitro, the database's own number of the background shown (also for the Nitro-only ones, which have no base number); 0 on the base game.</summary>
    public int Native { get; private set; }

    /// <summary>Unpack, install the palette, decode into the clean buffer, everything dirty.</summary>
    public void LoadBackground(Pcx p, int record = 0, int native = 0, bool black = true)
    {
        Array.Clear(_clean);
        for (int y = 0; y < Math.Min(H, p.Height); y++)
            Array.Copy(p.Pixels, y * p.Width, _clean, y * W, Math.Min(W, p.Width));
        Array.Copy(_clean, _work, _clean.Length);
        UnplaceAll();
        Palette = p.Palette;
        Record = record;
        Native = native;
        _edits.Clear();
        _copies.Clear();
        EditsVersion++;
        ForgetText();
        _dirty = true;
        _black = black;
    }

    /// <summary>A DATABASE.MW2 background by record.</summary>
    public void LoadBackground(ShellDatabase db, int record, bool black = true) => LoadBackground(db.Background(record), record, db.NativeRecord(record), black);

    /// <summary>Puts the background back under a rectangle.</summary>
    public void Restore(int x, int y, int w, int h)
    {
        Clip(ref x, ref y, ref w, ref h);
        for (int r = 0; r < h; r++)
        {
            int at = (y + r) * W + x;
            Take(at, w);
            Unhide(at, w);
            Array.Copy(_clean, at, _work, at, w);
        }
        _dirty = true;
    }

    /// <summary>
    /// A change the port made to the background itself: the patch (Sx, Sy, W, H) laid at (Dx, Dy), only where Mask (W x H, by
    /// row) is true when given. From is -1 for the background as it is when the edit is made, else the background as it was
    /// after the first From edits (a <see cref="CleanCopy"/> taken then). Kept so an upscale drawn under the frame can be changed
    /// the same way (ShellBackdrop): the frame is see-through where it is still the background, edited or not.
    /// </summary>
    public sealed record BackgroundEdit(int From, int Sx, int Sy, int W, int H, int Dx, int Dy, bool[]? Mask);

    private readonly List<BackgroundEdit> _edits = [];
    private readonly Dictionary<byte[], int> _copies = new(ReferenceEqualityComparer.Instance);
    /// <summary>The edits made to the background since it was loaded, in order (those that changed none of its pixels left out).</summary>
    public IReadOnlyList<BackgroundEdit> Edits => _edits;
    /// <summary>Changes whenever <see cref="Edits"/> does (and on every background load).</summary>
    public int EditsVersion { get; private set; }

    /// <summary>Copies a patch of the background onto another place of it (the port's: a printed check box onto a line that has none).</summary>
    public void CopyBackground(int sx, int sy, int w, int h, int dx, int dy)
    {
        bool changed = false;
        for (int r = 0; r < h; r++)
            if (sy + r is >= 0 and < H && dy + r is >= 0 and < H && sx >= 0 && dx >= 0 && sx + w <= W && dx + w <= W
                && !_clean.AsSpan((sy + r) * W + sx, w).SequenceEqual(_clean.AsSpan((dy + r) * W + dx, w)))
            {
                changed = true;
                break;
            }
        if (changed)
        {
            _edits.Add(new BackgroundEdit(-1, sx, sy, w, h, dx, dy, null));
            EditsVersion++;
        }
        for (int r = 0; r < h; r++)
            if (sy + r is >= 0 and < H && dy + r is >= 0 and < H && sx >= 0 && dx >= 0 && sx + w <= W && dx + w <= W)
                Array.Copy(_clean, (sy + r) * W + sx, _clean, (dy + r) * W + dx, w);
        Restore(dx, dy, w, h);
    }

    /// <summary>A copy of the background as it is now (640 x 480 palette indices), to paste parts of it back after it has been drawn over.</summary>
    public byte[] CleanCopy()
    {
        var c = (byte[])_clean.Clone();
        _copies[c] = _edits.Count;
        return c;
    }

    /// <summary>
    /// Pastes a rectangle of a background copy (<see cref="CleanCopy"/>) onto the background at another place, only the
    /// pixels <paramref name="keep"/> accepts (offsets within the rectangle) when given.
    /// </summary>
    public void PasteBackground(byte[] from, int sx, int sy, int w, int h, int dx, int dy, Func<int, int, bool>? keep = null)
    {
        var mask = keep is null ? null : new bool[w * h];
        bool changed = false;
        for (int r = 0; r < h; r++)
            for (int c = 0; c < w; c++)
            {
                int x = sx + c, y = sy + r, tx = dx + c, ty = dy + r;
                if (x is < 0 or >= W || y is < 0 or >= H || tx is < 0 or >= W || ty is < 0 or >= H) continue;
                if (mask is not null && !(mask[r * w + c] = keep!(c, r))) continue;
                if (_clean[ty * W + tx] == from[y * W + x]) continue;
                _clean[ty * W + tx] = from[y * W + x];
                changed = true;
            }
        if (changed)
        {
            // a copy of an earlier load's background (none known): the background as loaded
            _edits.Add(new BackgroundEdit(_copies.TryGetValue(from, out int n) ? n : 0, sx, sy, w, h, dx, dy, mask));
            EditsVersion++;
        }
        Restore(dx, dy, w, h);
    }

    /// <summary>The palette index shown at a pixel now.</summary>
    public byte At(int x, int y) => x is >= 0 and < W && y is >= 0 and < H ? _work[y * W + x] : (byte)0;

    /// <summary>One pixel drawn in a palette index.</summary>
    public void Pixel(int x, int y, byte index)
    {
        if (x is < 0 or >= W || y is < 0 or >= H) return;
        Put(y * W + x, index);
        _dirty = true;
    }
    /// <summary>The frame's pixels as they stand (a screen's own layer over the frame compares them with what the screen drew, to keep off a pop-up).</summary>
    public ReadOnlySpan<byte> Pixels => _work;

    /// <summary>The whole work buffer back to the background.</summary>
    public void RestoreAll() { Array.Copy(_clean, _work, _clean.Length); UnplaceAll(); ForgetText(); _dirty = true; }

    /// <summary>The sprite with its own header offset added (the garage's parts sit by it).</summary>
    public void BlitAt(Sprite? s, int x, int y) { if (s is not null) Blit(s, x + s.X0, y + s.Y0); }

    /// <summary>A draw with scale (−1, −1), the rear weapons: source pixel (sx, sy), header offset included, lands at (ax − sx, ay − sy).</summary>
    public void BlitRotated(Sprite? s, int ax, int ay)
    {
        if (s is null) return;
        // the box the turned sprite covers: its last pixel lands at its top-left
        int bx = ax - s.X0 - (s.Width - 1), by = ay - s.Y0 - (s.Height - 1);
        var hd = HasHdSprite?.Invoke(s) == true ? Place(s, bx, by, true) : null;
        for (int r = 0; r < s.Height; r++)
            for (int c = 0; c < s.Width; c++)
            {
                int i = r * s.Width + c, xx = ax - (s.X0 + c), yy = ay - (s.Y0 + r);
                if (!s.Opaque[i] || xx < 0 || xx >= W || yy < 0 || yy >= H) continue;
                Put(yy * W + xx, s.Pixels[i]);
                if (hd is not null) Hide(yy * W + xx, hd);
            }
        if (hd is { Live: 0 }) Drop(hd);
        _dirty = true;
    }

    // ---- sprites drawn at the window's resolution (the modern look; ShellSprites.cs) ----

    /// <summary>A sprite with an upscale on the frame: the box it covers, where it was blitted; Turned: drawn at scale (−1, −1), the rear weapons.</summary>
    public sealed record PlacedSprite(Sprite Sprite, int X, int Y, bool Turned)
    {
        internal int Id;
        /// <summary>How many of the frame's pixels are still the sprite's own, hidden under its upscale.</summary>
        internal int Live;
    }

    // each hidden pixel's owner (a placed sprite's Id + 1), or 0: the frame is see-through there and the upscale shows
    private readonly int[] _hideOwner = new int[W * H];
    // a pixel a placed sprite lost to a restore or a fill while the rest of it stays: presented opaque, as its upscale is still drawn whole beneath
    private readonly bool[] _uncovered = new bool[W * H];
    private readonly List<PlacedSprite> _placed = [];
    private readonly List<PlacedSprite?> _slots = [];
    private readonly Stack<int> _freeSlots = new();
    /// <summary>Whether a sprite has an upscale to show in its place (set in the modern look).</summary>
    public Func<Sprite, bool>? HasHdSprite { get; set; }
    /// <summary>
    /// The sprites with an upscale that are on the frame now, in the order they were drawn (a later one over an earlier: a damage
    /// stroke over a list's paper). One stays placed while any of its pixels is still on the frame, and goes when the last is written
    /// over (its box restored, another sprite over all of it).
    /// </summary>
    public IReadOnlyList<PlacedSprite> Placed => _placed;
    /// <summary>Changes whenever <see cref="Placed"/> does.</summary>
    public int PlacedVersion { get; private set; }

    /// <summary>Whether the pixel is a placed sprite's own, presented see-through for its upscale.</summary>
    public bool Hidden(int x, int y) => x is >= 0 and < W && y is >= 0 and < H && _hideOwner[y * W + x] != 0;

    private PlacedSprite Place(Sprite s, int x, int y, bool turned)
    {
        var p = new PlacedSprite(s, x, y, turned) { Id = _freeSlots.Count > 0 ? _freeSlots.Pop() : _slots.Count };
        if (p.Id == _slots.Count) _slots.Add(null);
        _slots[p.Id] = p;
        _placed.Add(p);
        PlacedVersion++;
        return p;
    }

    /// <summary>The pixel is now this placed sprite's own (whatever sprite had it has lost it in <see cref="Put"/>).</summary>
    private void Hide(int i, PlacedSprite p)
    {
        _hideOwner[i] = p.Id + 1;
        _uncovered[i] = false;
        p.Live++;
    }

    /// <summary>A run of pixels written over: the placed sprites that had them lose them, and one left with none goes.</summary>
    private void Unhide(int at, int n)
    {
        for (int i = at; i < at + n; i++)
        {
            int o = _hideOwner[i];
            if (o == 0) continue;
            _hideOwner[i] = 0;
            var p = _slots[o - 1]!;
            if (--p.Live > 0) _uncovered[i] = true;
            else Drop(p);
        }
    }

    private void Drop(PlacedSprite p)
    {
        _slots[p.Id] = null;
        _freeSlots.Push(p.Id);
        _placed.Remove(p);
        PlacedVersion++;
        // nothing of it is left to keep opaque
        for (int y = Math.Max(0, p.Y); y < Math.Min(H, p.Y + p.Sprite.Height); y++)
            Array.Clear(_uncovered, y * W + Math.Max(0, p.X), Math.Max(0, Math.Min(W, p.X + p.Sprite.Width) - Math.Max(0, p.X)));
    }

    private void UnplaceAll()
    {
        if (_placed.Count > 0) PlacedVersion++;
        _placed.Clear();
        _slots.Clear();
        _freeSlots.Clear();
        Array.Clear(_hideOwner);
        Array.Clear(_uncovered);
    }

    public void Blit(Sprite? s, int x, int y)
    {
        if (s is null) return;
        // a sprite with an upscale: its own pixels are hidden under the picture, which is drawn over any placed before it
        var hd = HasHdSprite?.Invoke(s) == true ? Place(s, x, y, false) : null;
        for (int r = 0; r < s.Height; r++)
        {
            int yy = y + r;
            if (yy < 0 || yy >= H) continue;
            for (int c = 0; c < s.Width; c++)
            {
                int xx = x + c, i = r * s.Width + c;
                if (xx < 0 || xx >= W || !s.Opaque[i]) continue;
                Put(yy * W + xx, s.Pixels[i]);
                if (hd is not null) Hide(yy * W + xx, hd);
            }
        }
        // none of its pixels landed on the frame: nothing to show in its place
        if (hd is { Live: 0 }) Drop(hd);
        _dirty = true;
    }

    /// <summary>
    /// Draws a string in a bitmap font with its top-left at (x, y), each glyph
    /// pixel through <paramref name="remap"/> and skipped where that gives 0xFF
    /// (without a remap the font's key is the transparent one).
    /// A leading '~' centres the string on x. Returns the pen's end x.
    /// </summary>
    public int Text(ShellFont f, int x, int y, string s, byte[]? remap = null)
    {
        if (s.StartsWith('~')) { s = s[1..]; x -= f.Measure(s) / 2; }
        // the modern look: recorded for the text layer instead, in the colour the face's solid ink lands in
        if (HdFonts?.Invoke(f) == true && InkOf(f, remap) is >= 0 and var ink)
        {
            foreach (char ch in s)
            {
                if (f.Glyph(ch) is not { } g) continue;
                Claim(new HdGlyph { Font = f, Ch = ch, Box = new Rect2I(x, y, g.Width, f.Height), Ink = (byte)ink });
                x += g.Width;
            }
            return x;
        }
        foreach (char ch in s)
        {
            if (f.Glyph(ch) is not { } g) continue;
            for (int r = 0; r < f.Height; r++)
            {
                int yy = y + r;
                if (yy < 0 || yy >= H) continue;
                for (int c = 0; c < g.Width; c++)
                {
                    int xx = x + c;
                    byte v = g.Px[r * g.Width + c];
                    if (remap is not null) { v = remap[v]; if (v == 0xff) continue; }
                    else if (v == f.Key) continue;
                    if (xx < 0 || xx >= W) continue;
                    Put(yy * W + xx, v);
                }
            }
            x += g.Width;
        }
        _dirty = true;
        return x;
    }

    /// <summary>The display's default remap: identity, 0 transparent — glyphs draw in their own index (font 0x45's ink is 15).</summary>
    public static readonly byte[] DefaultInk = MakeDefault();

    private static byte[] MakeDefault()
    {
        var r = new byte[256];
        for (int i = 0; i < 256; i++) r[i] = (byte)i;
        r[0] = 0xff;
        return r;
    }

    /// <summary>The overlays' ink remap: identity, glyph ink 1 drawn as palette 200, 255 transparent.</summary>
    public static readonly byte[] OverlayInk = MakeInk(200);

    public static byte[] MakeInk(byte ink)
    {
        var r = new byte[256];
        for (int i = 0; i < 256; i++) r[i] = (byte)i;
        r[1] = ink;
        return r;
    }

    /// <summary>The palette index nearest a colour.</summary>
    public byte Nearest(Color c)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < 256; i++)
        {
            int dr = Palette[i * 3] - (int)(c.R * 255), dg = Palette[i * 3 + 1] - (int)(c.G * 255), db = Palette[i * 3 + 2] - (int)(c.B * 255);
            int d = dr * dr + dg * dg + db * db;
            if (d < bestD) { bestD = d; best = i; }
        }
        return (byte)best;
    }

    public void Fill(int x, int y, int w, int h, byte index)
    {
        Clip(ref x, ref y, ref w, ref h);
        for (int r = 0; r < h; r++) { int at = (y + r) * W + x; Take(at, w); Unhide(at, w); Array.Fill(_work, index, at, w); }
        _dirty = true;
    }

    // ---- the modern look's text ----------------------------------------------------------------

    /// <summary>
    /// One glyph the text layer draws: a bitmap font's character in its cell,
    /// or (Caption) a whole string fitted to an ink box in the shell's
    /// handwriting. Ink is a palette index, looked up when it is drawn.
    /// </summary>
    public sealed class HdGlyph
    {
        public ShellFont? Font;
        public char Ch;
        public string? Caption;
        /// <summary>A picture in its own colours drawn into the box (the port's: the host form's printed labels and arena sketch).</summary>
        public Texture2D? Picture;
        public Rect2I Box;
        public byte Ink;
        /// <summary>The cell's pixels on the frame, and how many are still the glyph's.</summary>
        public int Cells, Live;
        /// <summary>The part of the box still the glyph's: the bounds of the pixels it has left (a pop-up over half a word cuts its glyphs).</summary>
        public Rect2I Shown;
        internal int Id;
        /// <summary>Shown while most of its cell is left as it was.</summary>
        public bool Visible => Live * 2 > Cells;
    }

    /// <summary>Which fonts are drawn by the text layer (null: none, the 1997 look).</summary>
    public Func<ShellFont, bool>? HdFonts { get; set; }
    private readonly int[] _owner = new int[W * H];
    private readonly List<HdGlyph?> _glyphs = [];
    private readonly Stack<int> _free = new();
    /// <summary>Bumped whenever a glyph comes or goes, for the text layer to redraw.</summary>
    public int TextVersion { get; private set; }

    /// <summary>The glyphs on the frame now, each with the part of it left (<see cref="HdGlyph.Shown"/>).</summary>
    public IEnumerable<HdGlyph> Glyphs
    {
        get
        {
            foreach (var g in _glyphs)
            {
                if (g is not { Visible: true }) continue;
                g.Shown = g.Live == g.Cells ? g.Box : Owned(g);
                yield return g;
            }
        }
    }

    private Rect2I Owned(HdGlyph g)
    {
        var box = g.Box.Intersection(new Rect2I(0, 0, W, H));
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (int y = box.Position.Y; y < box.End.Y; y++)
            for (int x = box.Position.X; x < box.End.X; x++)
                if (_owner[y * W + x] == g.Id + 1) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return x1 < x0 ? default : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// The palette index a string's solid ink lands in: the face's top glyph
    /// value through the remap, down the ramp while the remap retires it (0xFF);
    /// without a remap the glyph's own value. −1 when nothing would show.
    /// </summary>
    public static int InkOf(ShellFont f, byte[]? remap)
    {
        int v = f.InkValue;
        if (v < 0) return -1;
        if (remap is null) return v;
        for (; v > 0; v--)
            if (remap[v] != 0xff) return remap[v];
        return -1;
    }

    /// <summary>A string set in the shell's handwriting and fitted to an ink box (the action names painted into record 16, when the upscale has none).</summary>
    public void Caption(Rect2I box, string text, byte ink) => Claim(new HdGlyph { Caption = text, Box = box, Ink = ink });

    /// <summary>A picture the text layer draws into a box at the window's resolution (the modern look), kept as a glyph is.</summary>
    public void Picture(Rect2I box, Texture2D picture) => Claim(new HdGlyph { Picture = picture, Box = box });

    /// <summary>Every glyph that reaches into the rectangle is taken off whole (a slip laid over words hides them, not only the part under it).</summary>
    public void Uncover(Rect2I r)
    {
        foreach (var g in _glyphs.ToList())
        {
            if (g is null || !g.Box.Intersects(r)) continue;
            var box = g.Box.Intersection(new Rect2I(0, 0, W, H));
            for (int y = box.Position.Y; y < box.End.Y; y++)
                for (int x = box.Position.X; x < box.End.X; x++)
                    if (_owner[y * W + x] == g.Id + 1) Take(y * W + x, 1);
        }
    }

    /// <summary>Whether strings and pictures go to the text layer (the modern look) rather than into the pixels.</summary>
    public bool Modern => HdFonts is not null;

    private void Claim(HdGlyph g)
    {
        int x = g.Box.Position.X, y = g.Box.Position.Y, w = g.Box.Size.X, h = g.Box.Size.Y;
        Clip(ref x, ref y, ref w, ref h);
        if (w <= 0 || h <= 0) return;
        int id = _free.Count > 0 ? _free.Pop() : _glyphs.Count;
        if (id == _glyphs.Count) _glyphs.Add(null);
        _glyphs[id] = g;
        g.Id = id;
        g.Cells = g.Live = w * h;
        for (int r = 0; r < h; r++)
        {
            int at = (y + r) * W + x;
            Take(at, w);
            Array.Fill(_owner, id + 1, at, w);
        }
        TextVersion++;
    }

    private void Put(int i, byte v)
    {
        if (_owner[i] != 0) Take(i, 1);
        if (_hideOwner[i] != 0) Unhide(i, 1);
        _work[i] = v;
    }

    /// <summary>A run of pixels written over: whatever glyph had them loses them.</summary>
    private void Take(int at, int n)
    {
        bool taken = false;
        for (int i = at; i < at + n; i++)
        {
            int o = _owner[i];
            if (o == 0) continue;
            _owner[i] = 0;
            taken = true;
            if (--_glyphs[o - 1]!.Live == 0) { _glyphs[o - 1] = null; _free.Push(o - 1); }
        }
        if (taken) TextVersion++;
    }

    private void ForgetText()
    {
        if (_glyphs.Count == 0) return;
        Array.Clear(_owner);
        _glyphs.Clear();
        _free.Clear();
        TextVersion++;
    }

    private static void Clip(ref int x, ref int y, ref int w, ref int h)
    {
        if (x < 0) { w += x; x = 0; }
        if (y < 0) { h += y; y = 0; }
        w = Math.Max(0, Math.Min(w, W - x));
        h = Math.Max(0, Math.Min(h, H - y));
    }

    private bool _transparent;

    /// <summary>
    /// Whether the background shows from beneath (an upscaled one is drawn
    /// under the frame): the pixels that are still the background's are
    /// presented transparent, keeping their colour for the filtering at the edges.
    /// </summary>
    public bool Transparent
    {
        get => _transparent;
        set { if (_transparent != value) { _transparent = value; _dirty = true; } }
    }

    private bool[]? _keep;

    /// <summary>Pixels presented opaque even where they are the background (where a borrowed upscale differs from it), or null.</summary>
    public bool[]? Keep
    {
        get => _keep;
        set { if (_keep != value) { _keep = value; _dirty = true; } }
    }

    /// <summary>Whether the last present was the black one after a background load.</summary>
    public bool ShowingBlack { get; private set; }

    /// <summary>The frame to show when something changed (a present), as RGBA; null when nothing did.</summary>
    public Image? Present()
    {
        if (!_dirty) return null;
        var rgba = new byte[W * H * 4];
        ShowingBlack = _black;
        if (_black)
        {
            // the black palette's frame; the real one shows on the next present
            for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
            _black = false;
        }
        else
        {
            var pal = Palette;
            bool see = _transparent;
            var keep = _keep;
            for (int i = 0, o = 0; i < _work.Length; i++, o += 4)
            {
                int c = _work[i] * 3;
                rgba[o] = pal[c]; rgba[o + 1] = pal[c + 1]; rgba[o + 2] = pal[c + 2];
                rgba[o + 3] = _hideOwner[i] != 0 || see && _work[i] == _clean[i] && keep?[i] != true && !_uncovered[i] ? (byte)0 : (byte)255;
            }
            _dirty = false;
        }
        return Image.CreateFromData(W, H, false, Image.Format.Rgba8, rgba);
    }
}
