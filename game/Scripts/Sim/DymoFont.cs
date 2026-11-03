namespace I76.Game.Sim;

/// <summary>
/// A TrueType font's outlines (glyf, quadratic) read and filled on the CPU, engine-free: the modern look's weapon labels
/// are drawn from it into the cockpit panels' pixels once per weapon. Simple and offset-only composite glyphs, the
/// cmap's format-4 table, no hinting or kerning.
/// </summary>
public sealed class DymoFont
{
    private readonly byte[] _d;
    private readonly Dictionary<string, int> _tables = new(StringComparer.Ordinal);
    private readonly int _glyphs, _hMetrics, _locFormat;
    private readonly Dictionary<int, int> _cmap = new();

    /// <summary>Units per em.</summary>
    public int Em { get; }
    /// <summary>The capitals' height in font units (OS/2 sCapHeight, else the H's top).</summary>
    public int CapHeight { get; }

    public DymoFont(byte[] data)
    {
        _d = data;
        int n = U16(4);
        for (int i = 0; i < n; i++)
        {
            int r = 12 + 16 * i;
            _tables[System.Text.Encoding.ASCII.GetString(data, r, 4)] = (int)U32(r + 8);
        }
        int head = _tables["head"];
        Em = U16(head + 18);
        _locFormat = S16(head + 50);
        _glyphs = U16(_tables["maxp"] + 4);
        _hMetrics = U16(_tables["hhea"] + 34);
        ReadCmap();
        int cap = _tables.TryGetValue("OS/2", out int os2) && U16(os2) >= 2 ? S16(os2 + 88) : 0;
        CapHeight = cap > 0 ? cap : Bounds(Glyph('H')).YMax;
    }

    private int U16(int o) => _d[o] << 8 | _d[o + 1];
    private int S16(int o) => (short)U16(o);
    private uint U32(int o) => (uint)(_d[o] << 24 | _d[o + 1] << 16 | _d[o + 2] << 8 | _d[o + 3]);

    private void ReadCmap()
    {
        int c = _tables["cmap"], n = U16(c + 2);
        int sub = -1;
        for (int i = 0; i < n; i++)
        {
            int p = U16(c + 4 + 8 * i), e = U16(c + 6 + 8 * i), o = c + (int)U32(c + 8 + 8 * i);
            if (U16(o) == 4 && (p == 3 && e is 1 or 0 || p == 0)) { sub = o; if (p == 3) break; }
        }
        if (sub < 0) return;
        int segs = U16(sub + 6) / 2, ends = sub + 14, starts = ends + 2 * segs + 2, deltas = starts + 2 * segs, ranges = deltas + 2 * segs;
        for (int s = 0; s < segs; s++)
        {
            int end = U16(ends + 2 * s), start = U16(starts + 2 * s), delta = S16(deltas + 2 * s), ro = U16(ranges + 2 * s);
            for (int ch = start; ch <= end && ch != 0xffff; ch++)
            {
                int g = ro == 0 ? ch + delta : U16(ranges + 2 * s + ro + 2 * (ch - start)) is var gi && gi != 0 ? gi + delta : 0;
                _cmap[ch] = g & 0xffff;
            }
        }
    }

    /// <summary>A character's glyph (0, the missing glyph, when it has none).</summary>
    public int Glyph(char ch) => _cmap.GetValueOrDefault(ch);

    /// <summary>A glyph's advance in font units.</summary>
    public int Advance(int g) => U16(_tables["hmtx"] + 4 * Math.Min(g, _hMetrics - 1));

    private (int Start, int Len) GlyphData(int g)
    {
        if (g < 0 || g >= _glyphs) return (0, 0);
        int loca = _tables["loca"];
        int a = _locFormat == 0 ? U16(loca + 2 * g) * 2 : (int)U32(loca + 4 * g);
        int b = _locFormat == 0 ? U16(loca + 2 * g + 2) * 2 : (int)U32(loca + 4 * g + 4);
        return (_tables["glyf"] + a, b - a);
    }

    /// <summary>A glyph's contours as closed polylines in font units (y up), its quadratic arcs cut into short lines.</summary>
    public List<List<(float X, float Y)>> Outline(int g, float dx = 0, float dy = 0, int depth = 0)
    {
        var res = new List<List<(float X, float Y)>>();
        var (o, len) = GlyphData(g);
        if (len <= 0 || depth > 4) return res;
        int nc = S16(o);
        if (nc < 0)
        {
            // composite: the parts' outlines at their offsets (the scale forms are read past, not applied)
            int p = o + 10;
            while (true)
            {
                int flags = U16(p), part = U16(p + 2); p += 4;
                float ox, oy;
                if ((flags & 1) != 0) { ox = S16(p); oy = S16(p + 2); p += 4; }
                else { ox = (sbyte)_d[p]; oy = (sbyte)_d[p + 1]; p += 2; }
                if ((flags & 2) == 0) ox = oy = 0;   // point-matched parts: not used by the font's labels
                if ((flags & 8) != 0) p += 2; else if ((flags & 0x40) != 0) p += 4; else if ((flags & 0x80) != 0) p += 8;
                res.AddRange(Outline(part, dx + ox, dy + oy, depth + 1));
                if ((flags & 0x20) == 0) break;
            }
            return res;
        }
        int q = o + 10;
        var endPts = new int[nc];
        for (int i = 0; i < nc; i++) endPts[i] = U16(q + 2 * i);
        int pts = nc == 0 ? 0 : endPts[^1] + 1;
        q += 2 * nc;
        q += 2 + U16(q);
        var fl = new byte[pts];
        for (int i = 0; i < pts;)
        {
            byte f = _d[q++];
            fl[i++] = f;
            if ((f & 8) != 0) for (int r = _d[q++]; r > 0 && i < pts; r--) fl[i++] = f;
        }
        var xs = new int[pts]; var ys = new int[pts];
        int v = 0;
        for (int i = 0; i < pts; i++)
        {
            byte f = fl[i];
            if ((f & 2) != 0) { int b = _d[q++]; v += (f & 0x10) != 0 ? b : -b; }
            else if ((f & 0x10) == 0) { v += S16(q); q += 2; }
            xs[i] = v;
        }
        v = 0;
        for (int i = 0; i < pts; i++)
        {
            byte f = fl[i];
            if ((f & 4) != 0) { int b = _d[q++]; v += (f & 0x20) != 0 ? b : -b; }
            else if ((f & 0x20) == 0) { v += S16(q); q += 2; }
            ys[i] = v;
        }
        int s0 = 0;
        foreach (int e in endPts)
        {
            int n = e - s0 + 1;
            if (n >= 2) res.Add(Contour(xs, ys, fl, s0, n, dx, dy));
            s0 = e + 1;
        }
        return res;
    }

    private static List<(float X, float Y)> Contour(int[] xs, int[] ys, byte[] fl, int s, int n, float dx, float dy)
    {
        bool On(int i) => (fl[s + (i % n + n) % n] & 1) != 0;
        (float X, float Y) P(int i) { int k = s + (i % n + n) % n; return (xs[k] + dx, ys[k] + dy); }
        (float X, float Y) Mid((float X, float Y) a, (float X, float Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        // start on an on-curve point (or the midpoint of two off-curve ones)
        int first = 0;
        while (first < n && !On(first)) first++;
        var start = first < n ? P(first) : Mid(P(0), P(1));
        if (first == n) first = 0;
        var line = new List<(float X, float Y)> { start };
        var cur = start;
        for (int k = 1; k <= n; k++)
        {
            int i = first + k;
            if (On(i)) { cur = P(i); line.Add(cur); continue; }
            var ctrl = P(i);
            var end = On(i + 1) ? P(i + 1) : Mid(ctrl, P(i + 1));
            for (int t = 1; t <= 8; t++)
            {
                float u = t / 8f, a = (1 - u) * (1 - u), b = 2 * u * (1 - u), c = u * u;
                line.Add((a * cur.Item1 + b * ctrl.X + c * end.X, a * cur.Item2 + b * ctrl.Y + c * end.Y));
            }
            cur = end;
            if (On(i + 1)) k++;
        }
        return line;
    }

    /// <summary>A glyph's ink box in font units.</summary>
    public (int XMin, int YMin, int XMax, int YMax) Bounds(int g)
    {
        var (o, len) = GlyphData(g);
        return len < 10 ? (0, 0, 0, 0) : (S16(o + 2), S16(o + 4), S16(o + 6), S16(o + 8));
    }

    /// <summary>
    /// The text set on one line from x = 0 at the baseline, its contours in font units, and its ink's left and right
    /// (font units; both 0 for text with no ink). <paramref name="bold"/> > 0 emboldens it synthetically, as a
    /// rasteriser's synthetic bold does: each outline pushed out by that many font units on every side
    /// (<see cref="Embolden"/>), the glyph moved right by as much and its advance grown by twice it, so the letters keep
    /// their spacing.
    /// </summary>
    public (List<List<(float X, float Y)>> Contours, float InkLeft, float InkRight) Layout(string text, float bold = 0)
    {
        var all = new List<List<(float X, float Y)>>();
        float pen = 0, left = float.MaxValue, right = float.MinValue;
        foreach (char ch in text)
        {
            int g = Glyph(ch);
            var b = Bounds(g);
            if (b.XMax > b.XMin) { left = Math.Min(left, pen + b.XMin); right = Math.Max(right, pen + b.XMax + 2 * bold); }
            var outline = Outline(g, pen + bold, 0);
            all.AddRange(bold > 0 ? Embolden(outline, bold) : outline);
            pen += Advance(g) + 2 * bold;
        }
        return left > right ? (all, 0, 0) : (all, left, right);
    }

    /// <summary>
    /// A glyph's contours pushed out from the ink by <paramref name="d"/> font units: each point moved along its two
    /// edges' outward normals (the mitre, held to twice d at sharp corners). Which side is the ink's follows the
    /// outline's winding (the sum of the contours' signed areas: TrueType's outer contours run clockwise), so holes
    /// shrink as the strokes thicken.
    /// </summary>
    public static List<List<(float X, float Y)>> Embolden(List<List<(float X, float Y)>> contours, float d)
    {
        double area = 0;
        foreach (var c in contours)
            for (int i = 0; i < c.Count; i++) { var a = c[i]; var b = c[(i + 1) % c.Count]; area += (double)a.X * b.Y - (double)b.X * a.Y; }
        // clockwise (negative area, y up): the ink is on the right of the direction of travel, outward is the left normal
        float side = area < 0 ? 1 : -1;
        var res = new List<List<(float X, float Y)>>(contours.Count);
        foreach (var c in contours)
        {
            // the contour without repeated points (the curves' cuts and closing points can land on each other)
            var p = new List<(float X, float Y)>(c.Count);
            foreach (var q in c) if (p.Count == 0 || MathF.Abs(q.X - p[^1].X) + MathF.Abs(q.Y - p[^1].Y) > 1e-3f) p.Add(q);
            while (p.Count > 1 && MathF.Abs(p[0].X - p[^1].X) + MathF.Abs(p[0].Y - p[^1].Y) <= 1e-3f) p.RemoveAt(p.Count - 1);
            int n = p.Count;
            if (n < 3) { res.Add(c); continue; }
            var o = new List<(float X, float Y)>(n);
            for (int i = 0; i < n; i++)
            {
                var prev = p[(i + n - 1) % n]; var cur = p[i]; var next = p[(i + 1) % n];
                var n1 = Normal(prev, cur, side); var n2 = Normal(cur, next, side);
                float mx = n1.X + n2.X, my = n1.Y + n2.Y, dot = 1 + n1.X * n2.X + n1.Y * n2.Y;
                // the mitre: (n1 + n2) / (1 + n1·n2) reaches d off both edges; a corner sharper than 60 degrees is held to 2d
                float k = dot > 0.5f ? d / dot : 2 * d / MathF.Max(1e-4f, MathF.Sqrt(mx * mx + my * my));
                o.Add((cur.X + mx * k, cur.Y + my * k));
            }
            res.Add(o);
        }
        return res;
    }

    private static (float X, float Y) Normal((float X, float Y) a, (float X, float Y) b, float side)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, l = MathF.Max(1e-6f, MathF.Sqrt(dx * dx + dy * dy));
        return (-dy / l * side, dx / l * side);
    }

    /// <summary>
    /// Fills contours (nonzero winding) into a w × h coverage mask (0..1, rows from the top): a contour point (x, y)
    /// lands at pixel (ox + x·sx, oy − y·sy). Eight sample rows a pixel, each span's ends covered by their fraction.
    /// </summary>
    public static float[] Fill(List<List<(float X, float Y)>> contours, int w, int h, float sx, float sy, float ox, float oy)
    {
        const int Sub = 8;
        var mask = new float[w * h];
        var edges = new List<(float X0, float Y0, float X1, float Y1, int Dir)>();
        foreach (var c in contours)
            for (int i = 0; i < c.Count; i++)
            {
                var a = c[i]; var b = c[(i + 1) % c.Count];
                float x0 = ox + a.X * sx, y0 = oy - a.Y * sy, x1 = ox + b.X * sx, y1 = oy - b.Y * sy;
                if (y0 == y1) continue;
                edges.Add(y0 < y1 ? (x0, y0, x1, y1, 1) : (x1, y1, x0, y0, -1));
            }
        var xs = new List<(float X, int Dir)>();
        for (int row = 0; row < h; row++)
            for (int k = 0; k < Sub; k++)
            {
                float y = row + (k + 0.5f) / Sub;
                xs.Clear();
                foreach (var e in edges)
                    if (y >= e.Y0 && y < e.Y1) xs.Add((e.X0 + (y - e.Y0) / (e.Y1 - e.Y0) * (e.X1 - e.X0), e.Dir));
                xs.Sort((p, q) => p.X.CompareTo(q.X));
                int wind = 0;
                for (int i = 0; i + 1 < xs.Count; i++)
                {
                    wind += xs[i].Dir;
                    if (wind != 0) Span(mask, w, row, xs[i].X, xs[i + 1].X, 1f / Sub);
                }
            }
        return mask;
    }

    private static void Span(float[] mask, int w, int row, float a, float b, float weight)
    {
        a = Math.Clamp(a, 0, w); b = Math.Clamp(b, 0, w);
        if (b <= a) return;
        int ia = (int)a, ib = (int)b;
        if (ia == ib) { if (ia < w) mask[row * w + ia] += (b - a) * weight; return; }
        mask[row * w + ia] += (ia + 1 - a) * weight;
        for (int x = ia + 1; x < ib; x++) mask[row * w + x] += weight;
        if (ib < w) mask[row * w + ib] += (b - ib) * weight;
    }
}
