using Godot;

namespace I76.Game.Shell;

/// <summary>
/// The paper forms' printed button glyphs redrawn at the window's resolution (the modern look over the HD menu art): the
/// scroll triangles of the lists (△ ▽ in a list's header cell and under it; ◁ ▷ on the original's single-player melee form) and the
/// DEFENSE / NO. DRIVERS / A.I. DRIVERS ⊟ ⊞ boxes. The art draws them in a few pixels each; a model upscale leaves them soft
/// and lopsided. Where the upscale is drawn, each glyph's patch of it is laid over with plain paper from beside it (the
/// paper's own grain, <see cref="Patch"/>) and the glyph is drawn again from its geometry: the art's own size, place, ink
/// and fill (read off the art), with even 1-pixel strokes and square corners, the pair of a list's arrows alike.
///
/// The places are the art's (found once by matching the shapes over every background and pop-up of the base game; the
/// install's art is checked against them by the tests). The 1997 look and a form without its upscale draw the art as it is.
/// </summary>
public static class FormGlyphs
{
    public enum Kind { Up, Down, Left, Right, Minus, Plus }

    /// <summary>
    /// A glyph: its kind, its centre (frame pixels, continuous), the stroke's centre line's width and height (a triangle's
    /// base and height, a box's side) and the stroke's width.
    /// </summary>
    public readonly record struct Glyph(Kind Kind, float Cx, float Cy, float W, float H, float Stroke)
    {
        public Glyph Moved(float dx, float dy) => this with { Cx = Cx + dx, Cy = Cy + dy };

        /// <summary>The pixels the glyph's ink can reach.</summary>
        public Rect2I Bounds
        {
            get
            {
                float rx = W / 2 + Stroke + 0.5f, ry = H / 2 + Stroke + 0.5f;
                int x0 = (int)MathF.Floor(Cx - rx), y0 = (int)MathF.Floor(Cy - ry);
                return new Rect2I(x0, y0, (int)MathF.Ceiling(Cx + rx) - x0, (int)MathF.Ceiling(Cy + ry) - y0);
            }
        }

        /// <summary>The pixels the glyph's ink covers (a box's 10 by 10): what an edit has to copy whole to carry it.</summary>
        public Rect2I InkBox
        {
            get
            {
                float rx = W / 2 + Stroke / 2, ry = H / 2 + Stroke / 2;
                int x0 = (int)MathF.Floor(Cx - rx + 0.01f), y0 = (int)MathF.Floor(Cy - ry + 0.01f);
                return new Rect2I(x0, y0, (int)MathF.Ceiling(Cx + rx - 0.01f) - x0, (int)MathF.Ceiling(Cy + ry - 0.01f) - y0);
            }
        }

        /// <summary>The signed distance (frame pixels, positive outside) from the outline's centre line's polygon: the max of the edges' lines.</summary>
        public float Edge(float x, float y)
        {
            float dx = x - Cx, dy = y - Cy, w = W / 2, h = H / 2;
            switch (Kind)
            {
                case Kind.Minus or Kind.Plus: return MathF.Max(MathF.Abs(dx) - w, MathF.Abs(dy) - h);
                case Kind.Up: return Tri(dx, dy, w, h);
                case Kind.Down: return Tri(dx, -dy, w, h);
                case Kind.Left: return Tri(dy, dx, h, w);
                default: return Tri(dy, -dx, h, w);
            }
        }

        // apex up at (0, -h), base from (-w, h) to (w, h): the base's line and the two sides'
        private static float Tri(float x, float y, float w, float h)
        {
            float len = MathF.Sqrt(w * w + 4 * h * h);
            float side = (2 * h * MathF.Abs(x) - w * y - w * h) / len;
            return MathF.Max(y - h, side);
        }

        /// <summary>What the glyph puts at a point: 2 ink, 1 its fill, 0 nothing.</summary>
        public int At(float x, float y)
        {
            float e = Edge(x, y), half = Stroke / 2;
            if (e > half) return 0;
            if (e > -half) return 2;
            if (Kind is Kind.Minus or Kind.Plus)
            {
                float dx = MathF.Abs(x - Cx), dy = MathF.Abs(y - Cy);
                // the bar: 6 long, 2 thick, as the art's; the plus has its upright too
                if (dx <= BarLength / 2 && dy <= BarThickness / 2) return 2;
                if (Kind == Kind.Plus && dy <= BarLength / 2 && dx <= BarThickness / 2) return 2;
            }
            return 1;
        }
    }

    public const float BarLength = 6, BarThickness = 2;

    // the art's sizes: the tables' small triangles (stroke centre line 10 by 8), the melee form's heavier ones, the 10-pixel boxes
    private static Glyph Small(Kind k, float x, float y) => new(k, x, y, 10, 8, 1);
    private static Glyph Heavy(Kind k, float x, float y) => k is Kind.Up or Kind.Down ? new(k, x, y, 10, 8.5f, 1.4f) : new(k, x, y, 8.5f, 10, 1.5f);
    private static Glyph Box(Kind k, float x, float y) => new(k, x, y, 9, 9, 1);

    private static Glyph[] Defense(float minus1, float plus1, float minus2, float plus2) =>
    [
        .. new[] { 361f, 378, 395, 412 }.SelectMany(y => new[]
        {
            Box(Kind.Minus, minus1, y), Box(Kind.Plus, plus1, y), Box(Kind.Minus, minus2, y), Box(Kind.Plus, plus2, y),
        }),
    ];

    // the net melee forms' list (LOCAL DRIVER's GAME NAME / AREA OF PLAY rows)
    private static readonly Glyph[] NetList = [Small(Kind.Up, 19.5f, 135.5f), Small(Kind.Down, 19.5f, 260.5f)];

    /// <summary>The glyphs printed on each base-game background record, by record.</summary>
    private static readonly Dictionary<int, Glyph[]> Records = new()
    {
        // BUILD AND REPAIR FORM: the repair order's arrows, DEFENSE armor / chassis ⊟ ⊞
        [1] = [Small(Kind.Up, 459.5f, 57.5f), Small(Kind.Down, 459.5f, 249.5f), .. Defense(113, 159, 213, 259)],
        // the melee's CONFIGURE CHASSIS: DEFENSE
        [2] = Defense(113, 158, 214, 259),
        // CAR / VAN INVENTORY with FIELD SALVAGE: each salvage list's up arrow in its header and down arrow under it
        [3] =
        [
            Small(Kind.Up, 19.5f, 283.5f), Small(Kind.Up, 177.5f, 283.5f), Small(Kind.Up, 335.5f, 283.5f), Small(Kind.Up, 493.5f, 283.5f),
            Small(Kind.Down, 19.5f, 333.5f), Small(Kind.Up, 19.5f, 351.5f), Small(Kind.Down, 493.5f, 367.5f), Small(Kind.Up, 493.5f, 385.5f),
            Small(Kind.Down, 335.5f, 436.5f), Small(Kind.Down, 493.5f, 436.5f), Small(Kind.Down, 19.5f, 453.5f), Small(Kind.Down, 177.5f, 453.5f),
        ],
        // AUTO MELEE SINGLE PLAYER: PLAYERS' ◁ ▷ on eight rows, AREA OF PLAY's arrows (no screen of the port shows this record now)
        [5] =
        [
            .. Enumerable.Range(0, 8).SelectMany(i => new[] { Heavy(Kind.Left, 17, 64 + 17 * i), Heavy(Kind.Right, 208.5f, 63.5f + 17 * i) }),
            Heavy(Kind.Up, 21.5f, 266.25f), Heavy(Kind.Down, 21.5f, 447),
        ],
        [18] = NetList,
        // NET MELEE HOST EVENT FORM: NO. DRIVERS and MAX SCORE
        [19] = [.. NetList, Box(Kind.Minus, 160, 346), Box(Kind.Plus, 206, 346), Box(Kind.Minus, 111, 406), Box(Kind.Plus, 199, 406)],
        [20] = NetList,
        [22] = NetList,
        // AUTO MELEE DRIVER ENTRY FORM: A.I. DRIVERS
        [24] = [.. NetList, Box(Kind.Minus, 159, 295), Box(Kind.Plus, 207, 295)],
        [25] = NetList,
        // BUILD AND REPAIR FORM greyed (salvage management off)
        [29] = [Small(Kind.Up, 460.5f, 57.5f), .. Defense(128, 164, 224, 260)],
        // the dream's salvage form (its BRAKES arrow under the NO FIELD SALVAGE stamp left as it is)
        [30] =
        [
            Small(Kind.Up, 19.5f, 283.5f), Small(Kind.Up, 177.5f, 283.5f), Small(Kind.Up, 335.5f, 283.5f),
            Small(Kind.Down, 19.5f, 333.5f), Small(Kind.Up, 19.5f, 351.5f), Small(Kind.Down, 493.5f, 367.5f), Small(Kind.Up, 493.5f, 385.5f),
            Small(Kind.Down, 335.5f, 436.5f), Small(Kind.Down, 493.5f, 436.5f), Small(Kind.Down, 19.5f, 453.5f), Small(Kind.Down, 177.5f, 453.5f),
        ],
    };

    /// <summary>The glyphs printed on bank 0x24's pop-ups (the parts, weapons, make / model, variant, paint, input and server pickers), by sprite, in its own pixels.</summary>
    private static readonly Dictionary<int, Glyph[]> Popups = new()
    {
        [7] = [Small(Kind.Up, 27.5f, 18.5f), Small(Kind.Down, 27.5f, 210.5f)],
        [8] = [Small(Kind.Up, 27.5f, 17.5f), Small(Kind.Down, 27.5f, 243.5f)],
        [9] = [Small(Kind.Up, 27.5f, 18.5f), Small(Kind.Down, 27.5f, 210.5f)],
        [10] = [Small(Kind.Up, 27.5f, 17.5f), Small(Kind.Down, 27.5f, 243.5f)],
        [12] = [Small(Kind.Up, 59.5f, 118.5f), Small(Kind.Down, 59.5f, 347.5f)],
        [15] = [Small(Kind.Up, 27.5f, 18.5f), Small(Kind.Down, 27.5f, 142.5f)],
        [16] = [Small(Kind.Up, 27.5f, 18.5f), Small(Kind.Down, 27.5f, 210.5f)],
        [29] = [Small(Kind.Up, 59.5f, 174.5f), Small(Kind.Down, 59.5f, 347.5f)],
    };

    /// <summary>The pop-ups' bank.</summary>
    public const int PopupBank = 0x24;

    public static IReadOnlyList<Glyph> ForRecord(int record) => Records.GetValueOrDefault(record) ?? [];
    public static IReadOnlyList<Glyph> ForPopup(int sprite) => Popups.GetValueOrDefault(sprite) ?? [];
    public static IEnumerable<int> RecordsWithGlyphs => Records.Keys;
    public static IEnumerable<int> PopupsWithGlyphs => Popups.Keys;

    /// <summary>
    /// The glyphs as the port's edits to the background leave them (ShellFrame.Edits): one whose pixels an edit writes over is
    /// gone, and one wholly inside an edit's source (where the edit's mask, if any, takes all of it) appears at the edit's
    /// destination too (the host form's NO. DRIVERS ⊟ ⊞ copied onto its new rows). An edit From an earlier state copies that
    /// state's glyphs, as ShellBackdrop copies its texels.
    /// </summary>
    public static List<Glyph> Follow(IReadOnlyList<Glyph> loaded, IReadOnlyList<ShellFrame.BackgroundEdit> edits)
    {
        var states = new List<List<Glyph>> { new(loaded) };
        var now = new List<Glyph>(loaded);
        for (int i = 0; i < edits.Count; i++)
        {
            var d = edits[i];
            var from = d.From < 0 ? now : states[Math.Min(d.From, i)];
            var src = new Rect2I(d.Sx, d.Sy, d.W, d.H);
            var copied = from.Where(g => src.Encloses(g.InkBox) && Covered(d, g.InkBox.Position - src.Position, g.InkBox.Size))
                .Select(g => g.Moved(d.Dx - d.Sx, d.Dy - d.Sy)).ToList();
            var dst = new Rect2I(d.Dx, d.Dy, d.W, d.H);
            now = [.. now.Where(g => !Written(d, dst, g.InkBox)), .. copied];
            states.Add([.. now]);
        }
        return now;
    }

    // every pixel of the box (relative to the edit's rectangle) is copied
    private static bool Covered(ShellFrame.BackgroundEdit d, Vector2I at, Vector2I size)
    {
        if (d.Mask is not { } m) return true;
        for (int y = at.Y; y < at.Y + size.Y; y++)
            for (int x = at.X; x < at.X + size.X; x++)
                if (!m[y * d.W + x]) return false;
        return true;
    }

    // any pixel of the glyph's box is written
    private static bool Written(ShellFrame.BackgroundEdit d, Rect2I dst, Rect2I box)
    {
        var o = dst.Intersection(box);
        if (o.Size.X <= 0 || o.Size.Y <= 0) return false;
        if (d.Mask is not { } m) return true;
        for (int y = o.Position.Y; y < o.End.Y; y++)
            for (int x = o.Position.X; x < o.End.X; x++)
                if (m[(y - d.Dy) * d.W + x - d.Dx]) return true;
        return false;
    }

    private static float Lum(byte[] pal, byte i) => 0.299f * pal[i * 3] + 0.587f * pal[i * 3 + 1] + 0.114f * pal[i * 3 + 2];

    /// <summary>
    /// The art's ink and fill for a glyph: the darkest palette index on its stroke (the stroke's core; the art softens its
    /// edges with lighter shades) and the commonest one inside it (a box's: away from its bar). Null when the art there has no
    /// dark stroke (the glyph is not there).
    /// </summary>
    public static (byte Ink, byte Fill)? Inks(Func<int, int, int> px, byte[] pal, Glyph g)
    {
        int ink = -1;
        var fill = new Dictionary<byte, int>();
        var b = g.Bounds;
        for (int y = b.Position.Y; y < b.End.Y; y++)
            for (int x = b.Position.X; x < b.End.X; x++)
            {
                int v = px(x, y);
                if (v < 0) continue;
                byte i = (byte)v;
                float e = g.Edge(x + 0.5f, y + 0.5f);
                if (MathF.Abs(e) <= g.Stroke / 2 && Lum(pal, i) < 110 && (ink < 0 || Lum(pal, i) < Lum(pal, (byte)ink))) ink = i;
                else if (e < -g.Stroke / 2 - 0.4f && g.At(x + 0.5f, y + 0.5f) == 1 && Lum(pal, i) >= 110) fill[i] = fill.GetValueOrDefault(i) + 1;
            }
        if (ink < 0 || fill.Count == 0) return null;
        return ((byte)ink, fill.MaxBy(kv => kv.Value).Key);
    }

    /// <summary>
    /// One ink and one fill for a form's glyphs, so every pair and row of them is alike: the darkest of their inks and the
    /// commonest of their fills (the art's dither gives each glyph a slightly different pick). The glyphs the art has there,
    /// with it; none when none is there.
    /// </summary>
    public static (List<Glyph> Glyphs, byte Ink, byte Fill) Inks(Func<int, int, int> px, byte[] pal, IReadOnlyList<Glyph> all)
    {
        var found = all.Select(g => (g, i: Inks(px, pal, g))).Where(t => t.i is not null).ToList();
        if (found.Count == 0) return ([], 0, 0);
        byte ink = found.Select(t => t.i!.Value.Ink).MinBy(i => Lum(pal, i));
        byte fill = found.GroupBy(t => t.i!.Value.Fill).MaxBy(gr => gr.Count())!.Key;
        return ([.. found.Select(t => t.g)], ink, fill);
    }

    /// <summary>The pixels a glyph's patch of paper covers (its ink's reach and the upscale's soft edge round it), relative to <see cref="PatchBox"/>.</summary>
    public static Rect2I PatchBox(Glyph g) => g.Bounds.Grow(1);

    // the glyph's ink and a pixel round it, but never another mark's ink there (a table's rule beside a triangle)
    private static bool InPatch(Glyph g, int x, int y, bool dark)
    {
        float e = g.Edge(x + 0.5f, y + 0.5f);
        return e <= g.Stroke / 2 + 1.1f && !(dark && e > g.Stroke / 2 + 0.5f);
    }

    /// <summary>
    /// Plain paper laid over a glyph in the upscale, as a background edit (the frame itself is left alone): the glyph's patch
    /// copied from the nearest place beside it whose pixels are all paper of the same tone (none dark, none another glyph's,
    /// within 20 of the paper's brightness round the glyph, or more where that paper varies more, each pixel's taken with its neighbours'; no ink beside them); failing that the nearest with the fewest pixels off that tone (a quarter at most). Null when there is none within 48 pixels.
    /// </summary>
    public static ShellFrame.BackgroundEdit? Patch(Func<int, int, int> px, byte[] pal, int w, int h, Glyph g, IReadOnlyList<Glyph> all)
    {
        var box = PatchBox(g);
        var mask = new bool[box.Size.X * box.Size.Y];
        for (int y = 0; y < box.Size.Y; y++)
            for (int x = 0; x < box.Size.X; x++)
                mask[y * box.Size.X + x] = InPatch(g, box.Position.X + x, box.Position.Y + y,
                    px(box.Position.X + x, box.Position.Y + y) is var v0 && v0 >= 0 && Lum(pal, (byte)v0) < 110);
        // the paper's tone: the light pixels of a ring round the patch
        var ring = new List<float>();
        var outer = box.Grow(3);
        for (int y = outer.Position.Y; y < outer.End.Y; y++)
            for (int x = outer.Position.X; x < outer.End.X; x++)
                if (!box.HasPoint(new Vector2I(x, y)) && Tone(px, pal, x, y) is var t && t >= 110) ring.Add(t);
        if (ring.Count < 8) return null;
        ring.Sort();
        float paper = ring[ring.Count / 2];
        // as close as the paper round it varies (a grainy, greyed form's more)
        var spread = ring.Select(l => MathF.Abs(l - paper)).OrderBy(l => l).ToList();
        float within = MathF.Max(20, 1.25f * spread[spread.Count * 9 / 10]);
        int masked = mask.Count(m => m);
        ShellFrame.BackgroundEdit? fallback = null;
        int fewest = masked / 4 + 1;
        foreach (var (dx, dy) in Offsets(box.Size))
        {
            var src = new Rect2I(box.Position.X + dx, box.Position.Y + dy, box.Size.X, box.Size.Y);
            if (src.Position.X < 0 || src.Position.Y < 0 || src.End.X > w || src.End.Y > h) continue;
            if (all.Any(o => o.Bounds.Grow(1).Intersects(src))) continue;
            bool ink = false;
            int off = 0;
            for (int y = 0; y < box.Size.Y && !ink; y++)
                for (int x = 0; x < box.Size.X && !ink; x++)
                {
                    if (!mask[y * box.Size.X + x]) continue;
                    int sx = src.Position.X + x, sy = src.Position.Y + y;
                    // no ink there nor beside it (the upscale's soft edge of a line would come with it) but where the glyph's place has the same beside it
                    for (int ny = -1; ny <= 1 && !ink; ny++)
                        for (int nx = -1; nx <= 1 && !ink; nx++)
                            ink = Dark(px, pal, sx + nx, sy + ny) && !Dark(px, pal, box.Position.X + x + nx, box.Position.Y + y + ny);
                    if (MathF.Abs(Tone(px, pal, sx, sy) - paper) > within) off++;
                }
            if (ink) continue;
            var edit = new ShellFrame.BackgroundEdit(-1, src.Position.X, src.Position.Y, box.Size.X, box.Size.Y, box.Position.X, box.Position.Y, mask);
            if (off == 0) return edit;
            // else the nearest with the fewest pixels off the tone, a quarter of them at most
            if (off < fewest) { fewest = off; fallback = edit; }
        }
        return fallback;
    }

    // the brightness of the paper round a pixel (3 by 3, its ink left out): a dithered paper's tone, not its dither's
    private static float Tone(Func<int, int, int> px, byte[] pal, int x, int y)
    {
        float sum = 0;
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (px(x + dx, y + dy) is var v && v >= 0 && Lum(pal, (byte)v) >= 110) { sum += Lum(pal, (byte)v); n++; }
        return n == 0 ? 0 : sum / n;
    }

    private static bool Dark(Func<int, int, int> px, byte[] pal, int x, int y) => px(x, y) is var v && v >= 0 && Lum(pal, (byte)v) < 110;

    // nearest first: along the row and the column, then the diagonals, out to 48 pixels; never overlapping the patch itself
    private static IEnumerable<(int, int)> Offsets(Vector2I size)
    {
        for (int r = 1; r <= 48; r++)
        {
            if (r >= size.X) { yield return (r, 0); yield return (-r, 0); }
            if (r >= size.Y) { yield return (0, -r); yield return (0, r); }
            if (r >= size.X && r >= size.Y) { yield return (r, -r); yield return (-r, -r); yield return (r, r); yield return (-r, r); }
        }
    }

    /// <summary>
    /// A shape drawn at the window's resolution: the screen pixels (whole) of the frame rectangle <paramref name="frame"/> at
    /// <paramref name="scale"/> screen pixels a frame pixel with the frame's origin at <paramref name="origin"/>, each pixel
    /// <paramref name="samples"/>² samples of <paramref name="at"/> (frame coordinates; 2 ink, 1 fill, 0 nothing), straight RGBA8.
    /// <paramref name="alpha"/> scales a sample's opacity (a stamp's uneven ink), given its frame coordinates.
    /// </summary>
    public static (Rect2I Screen, byte[] Rgba) Raster(Rect2 frame, float scale, Vector2 origin, Func<float, float, int> at, Color ink, Color fill,
        int samples = 4, Func<float, float, float>? alpha = null)
    {
        int x0 = (int)MathF.Floor(origin.X + frame.Position.X * scale), y0 = (int)MathF.Floor(origin.Y + frame.Position.Y * scale);
        int x1 = (int)MathF.Ceiling(origin.X + frame.End.X * scale), y1 = (int)MathF.Ceiling(origin.Y + frame.End.Y * scale);
        int w = Math.Max(1, x1 - x0), h = Math.Max(1, y1 - y0);
        var rgba = new byte[w * h * 4];
        float n = samples * samples;
        for (int py = 0; py < h; py++)
            for (int pxl = 0; pxl < w; pxl++)
            {
                float ci = 0, cf = 0;
                for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        float fx = (x0 + pxl + (sx + 0.5f) / samples - origin.X) / scale, fy = (y0 + py + (sy + 0.5f) / samples - origin.Y) / scale;
                        int k = at(fx, fy);
                        if (k == 0) continue;
                        float a = alpha?.Invoke(fx, fy) ?? 1;
                        if (k == 2) ci += a; else cf += a;
                    }
                ci /= n; cf /= n;
                float cov = ci + cf;
                if (cov <= 0) continue;
                int o = (py * w + pxl) * 4;
                // colour: the mean of what covers the pixel; opacity: how much covers it
                rgba[o] = (byte)Math.Clamp(MathF.Round((ink.R * ci + fill.R * cf) / cov * 255), 0, 255);
                rgba[o + 1] = (byte)Math.Clamp(MathF.Round((ink.G * ci + fill.G * cf) / cov * 255), 0, 255);
                rgba[o + 2] = (byte)Math.Clamp(MathF.Round((ink.B * ci + fill.B * cf) / cov * 255), 0, 255);
                rgba[o + 3] = (byte)Math.Clamp(MathF.Round(cov * 255), 0, 255);
            }
        return (new Rect2I(x0, y0, w, h), rgba);
    }

    /// <summary>A glyph drawn at the window's resolution, its frame drawn from <paramref name="origin"/> at <paramref name="scale"/>.</summary>
    public static (Rect2I Screen, byte[] Rgba) Raster(Glyph g, float scale, Vector2 origin, Color ink, Color fill)
    {
        var b = g.Bounds;
        return Raster(new Rect2(b.Position, b.Size), scale, origin, g.At, ink, fill);
    }

    /// <summary>A palette colour.</summary>
    public static Color Colour(byte[] pal, byte i) => Color.Color8(pal[i * 3], pal[i * 3 + 1], pal[i * 3 + 2]);
}
