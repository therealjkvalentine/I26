namespace I76.Game.Sim;

/// <summary>
/// The modern look's ammunition counter digits: each drum's figure set in a font (Roboto Bold, game/Fonts) on the strip's
/// own cell, in place of the strip's painted 8-pixel figures, which an upscale leaves soft-edged. The cell keeps the
/// original's separator line (its left column) and the drum's face colour row by row; the figure is drawn over it with a
/// texel's anti-aliasing. The 1997 look keeps the strip. Engine-free: the cockpit panels hand in the cell's texels.
/// </summary>
public static class CounterDigits
{
    /// <summary>
    /// The strip's cell in the original's pixels (znbe: 8 × 10 a figure, ten figures and the 0 again down the strip),
    /// and where its figures stand: the flat ones span rows 1.4 to 9.0 (a part-covered top row, the foot on row 8's
    /// lower edge), their ink centred about x 3.8, the separator in column 0 and the drum's face to the right.
    /// </summary>
    public const int CellW = 8, CellH = 10;
    public const float FigureHeight = 7.6f, Baseline = 9.0f, CentreX = 3.8f;

    /// <summary>The figures' ink: the strip's darkest (41, 41, 20) taken deeper, as its digits were in the modern look.</summary>
    public static readonly (byte R, byte G, byte B) Ink = (16, 16, 10);

    /// <summary>
    /// The inset variant: the figure as if cut into the drum, lit from above, so the groove's lower wall catches the
    /// light: the ink's lower edge, a third of a pixel deep, lifted <see cref="InsetLift"/> of the way to the face.
    /// </summary>
    public const float InsetLift = 0.45f, InsetDepth = 0.33f;

    /// <summary>Whether the cockpit draws the inset variant (else the figures flat, as printed on a counter's wheel).</summary>
    public const bool Inset = false;

    /// <summary>
    /// Paints figure <paramref name="digit"/> (0..9) over a cell: <paramref name="rgba"/> is the strip's cell at
    /// <paramref name="s"/> texels a pixel (CellW·s × CellH·s RGBA8, stored bottom-up as the panels are). Right of the
    /// separator column every row takes its face colour (the cell's rightmost texel, the face in every cell of the
    /// strip), taking the painted figure off; then the font's figure is filled at the strip's figure height and place.
    /// </summary>
    public static void Paint(byte[] rgba, int s, int digit, DymoFont font, bool inset)
    {
        int w = CellW * s, h = CellH * s;
        for (int y = 0; y < h; y++)
        {
            int r = (h - 1 - y) * w * 4, f = r + (w - 1) * 4;
            for (int x = s; x < w - 1; x++) { int i = r + x * 4; rgba[i] = rgba[f]; rgba[i + 1] = rgba[f + 1]; rgba[i + 2] = rgba[f + 2]; rgba[i + 3] = 255; }
        }
        int g = font.Glyph((char)('0' + (digit % 10 + 10) % 10));
        if (g == 0) return;
        float sc = FigureHeight * s / font.CapHeight;
        float ox = CentreX * s - font.Advance(g) * sc / 2, oy = Baseline * s;
        var m = DymoFont.Fill(font.Outline(g), w, h, sc, sc, ox, oy);
        int e = Math.Max(1, (int)Math.Round(InsetDepth * s));
        float M(int x, int y) => x >= 0 && y >= 0 && x < w && y < h ? Math.Min(1, m[y * w + x]) : 0;
        for (int y = 0; y < h; y++)
            for (int x = s; x < w; x++)
            {
                float a = M(x, y);
                if (a <= 0) continue;
                int i = ((h - 1 - y) * w + x) * 4;
                var (cr, cg, cb) = ((float)Ink.R, (float)Ink.G, (float)Ink.B);
                if (inset)
                {
                    // the groove's lit lower wall: ink with less ink under it, toward the face's colour
                    float lo = InsetLift * Math.Max(0, a - M(x, y + e));
                    cr += (rgba[i] - cr) * lo; cg += (rgba[i + 1] - cg) * lo; cb += (rgba[i + 2] - cb) * lo;
                }
                rgba[i] = (byte)(rgba[i] + (cr - rgba[i]) * a + 0.5f);
                rgba[i + 1] = (byte)(rgba[i + 1] + (cg - rgba[i + 1]) * a + 0.5f);
                rgba[i + 2] = (byte)(rgba[i + 2] + (cb - rgba[i + 2]) * a + 0.5f);
            }
    }

    private static DymoFont? _font;
    private static bool _tried;

    /// <summary>The counters' font, read from the game assembly (null if it is missing: the strip is drawn).</summary>
    public static DymoFont? Font
    {
        get
        {
            if (_tried) return _font;
            _tried = true;
            using var st = typeof(CounterDigits).Assembly.GetManifestResourceStream("RobotoBold.ttf");
            if (st is null) return null;
            using var ms = new System.IO.MemoryStream();
            st.CopyTo(ms);
            try { _font = new DymoFont(ms.ToArray()); } catch (Exception) { _font = null; }
            return _font;
        }
    }
}
