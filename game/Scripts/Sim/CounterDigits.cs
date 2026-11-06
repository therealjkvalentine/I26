namespace I76.Game.Sim;

/// <summary>The counter drum's look in the modern cockpit (CounterDigits.Style).</summary>
public enum DrumStyle
{
    /// <summary>The figure printed flat on a flat white face.</summary>
    Flat,
    /// <summary>A cylinder under the window: the face lit at its crown and darker toward the window's top and bottom
    /// edges, the figure following the curve (a little shorter toward the edges).</summary>
    Drum,
    /// <summary>Drum, the figure cut into the face (reverse-embossed): its lower edge lit, a shadow along its top.</summary>
    Inset,
    /// <summary>Drum, the figure standing a little proud: its top edge lit, a shadow under it.</summary>
    Raised,
    /// <summary>Drum with the gaps between the wheels and the neighbouring figures' ends showing above and below the window.</summary>
    Gaps,
}

/// <summary>
/// The modern look's ammunition counter digits: each drum's figure set in a font (Roboto Bold, game/Fonts) on the strip's
/// own cell, in place of the strip's painted 8-pixel figures, which an upscale leaves soft-edged. The cell keeps the
/// original's separator line (its left column) and the drum's face colour row by row; the figure is drawn over it with a
/// texel's anti-aliasing, and the face shaded as a drum (<see cref="Style"/>). The 1997 look keeps the strip.
/// Engine-free: the cockpit panels hand in the cell's texels.
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
    /// The drum: the window shows <see cref="HalfAngle"/> degrees of it either side of the crown (its radius the cell's
    /// half height over that angle's sine), lit from <see cref="LightAngle"/> degrees above the line of sight, with
    /// <see cref="Ambient"/> of the light reaching every part of the face: the crown full, the window's top edge about
    /// nine tenths and its bottom edge about three quarters.
    /// </summary>
    public const float HalfAngle = 50f, LightAngle = 12f, Ambient = 0.5f;

    /// <summary>
    /// The emboss (Inset, Raised): an edge <see cref="EmbossDepth"/> pixels deep (one texel at the pack's 4x), the lit edge
    /// taken <see cref="EmbossLift"/> of the way from the ink to the face, the shadowed one darkened by <see cref="EmbossShade"/>.
    /// </summary>
    public const float EmbossDepth = 0.33f, EmbossLift = 0.45f, EmbossShade = 0.22f;

    /// <summary>Gaps: the neighbouring figures' pitch round the drum (pixels of arc) and the gap's grey.</summary>
    public const float Pitch = 9.0f;
    public const byte GapGrey = 70;

    /// <summary>
    /// The cockpit's drum (Inset: shaded as a drum, the figures cut in). I76_COUNTER_DRUM (flat, drum, inset, raised,
    /// gaps) sets another, for comparisons (docs/dev-hooks.md).
    /// </summary>
    public static DrumStyle Style { get; set; } = FromEnv(System.Environment.GetEnvironmentVariable("I76_COUNTER_DRUM"));

    public static DrumStyle FromEnv(string? v) => Enum.TryParse<DrumStyle>(v, true, out var s) && Enum.IsDefined(s) ? s : DrumStyle.Inset;

    /// <summary>
    /// The window's edges on a drum style: over the top and bottom <see cref="Feather"/> of the window's height the face
    /// and the figure fade into the housing's shadow (<see cref="ShadowRgb"/>, at most <see cref="ShadowDepth"/> at the
    /// lip), the figure's ink thinning first (to <see cref="InkFade"/> at the lip) as it curves away under the window;
    /// with <see cref="Lip"/>, the lip's edge catches a faint light. I76_COUNTER_FEATHER ("0", "10", "15", "20", each
    /// optionally with "lip": "15lip") sets another, for comparisons (docs/dev-hooks.md).
    /// </summary>
    public static float Feather { get; set; } = FeatherFromEnv(System.Environment.GetEnvironmentVariable("I76_COUNTER_FEATHER")).Fraction;
    public static bool Lip { get; set; } = FeatherFromEnv(System.Environment.GetEnvironmentVariable("I76_COUNTER_FEATHER")).Lip;
    public const float DefaultFeather = 0.20f, ShadowDepth = 0.85f, InkFade = 0.5f, LipLight = 0.3f;
    public const bool DefaultLip = false;
    public static readonly (byte R, byte G, byte B) ShadowRgb = (24, 24, 20);

    public static (float Fraction, bool Lip) FeatherFromEnv(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return (DefaultFeather, DefaultLip);
        v = v.Trim().ToLowerInvariant();
        bool lip = v.EndsWith("lip");
        if (lip) v = v[..^3];
        return int.TryParse(v, out int pct) && pct is >= 0 and <= 40 ? (pct / 100f, lip) : (DefaultFeather, DefaultLip);
    }

    /// <summary>The window-edge weight of row <paramref name="y"/> (from the top) of a window <paramref name="h"/> high: 1 at
    /// the lip, easing to 0 at <paramref name="feather"/> of the height in from it.</summary>
    public static float EdgeWeight(int y, int h, float feather)
    {
        if (feather <= 0) return 0;
        float d = Math.Min(y + 0.5f, h - y - 0.5f) / (feather * h);
        if (d >= 1) return 0;
        float t = 1 - d;
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Paints figure <paramref name="digit"/> (0..9) over a cell: <paramref name="rgba"/> is the strip's cell at
    /// <paramref name="s"/> texels a pixel (CellW·s × CellH·s RGBA8, stored bottom-up as the panels are). Right of the
    /// separator column every row takes its face colour (the cell's rightmost texel, the face in every cell of the
    /// strip), taking the painted figure off; then the font's figure is filled at the strip's figure height and place,
    /// on the drum's curve for the drum styles, and the face shaded.
    /// </summary>
    public static void Paint(byte[] rgba, int s, int digit, DymoFont font, DrumStyle style) => Paint(rgba, s, digit, font, style, Feather, Lip);

    /// <summary>Paint with the window's feather and lip given (the drum styles only; Flat has no window edge).</summary>
    public static void Paint(byte[] rgba, int s, int digit, DymoFont font, DrumStyle style, float feather, bool lip)
    {
        if (style == DrumStyle.Flat) feather = 0;
        int w = CellW * s, h = CellH * s;
        for (int y = 0; y < h; y++)
        {
            int r = (h - 1 - y) * w * 4, f = r + (w - 1) * 4;
            for (int x = s; x < w - 1; x++) { int i = r + x * 4; rgba[i] = rgba[f]; rgba[i + 1] = rgba[f + 1]; rgba[i + 2] = rgba[f + 2]; rgba[i + 3] = 255; }
        }
        digit = (digit % 10 + 10) % 10;
        var m = style == DrumStyle.Flat ? FlatMask(font, digit, w, h, s) : DrumMask(font, digit, w, h, s, style == DrumStyle.Gaps);
        if (m is null) return;
        int e = Math.Max(1, (int)Math.Round(EmbossDepth * s));
        float M(int x, int y) => x >= 0 && y >= 0 && x < w && y < h ? Math.Min(1, m[y * w + x]) : 0;
        for (int y = 0; y < h; y++)
            for (int x = s; x < w; x++)
            {
                float a = M(x, y);
                int i = ((h - 1 - y) * w + x) * 4;
                // the shadow on the face beside the figure: above it cut in, below it standing proud
                float sh = style switch
                {
                    DrumStyle.Inset => Math.Max(0, M(x, y + e) - a),
                    DrumStyle.Raised => Math.Max(0, M(x, y - e) - a),
                    _ => 0,
                };
                if (sh > 0) for (int c = 0; c < 3; c++) rgba[i + c] = (byte)(rgba[i + c] * (1 - EmbossShade * sh) + 0.5f);
                // the figure curving away under the window: its ink thins toward the lip
                a *= 1 - InkFade * EdgeWeight(y, h, feather);
                if (a <= 0) continue;
                var (cr, cg, cb) = ((float)Ink.R, (float)Ink.G, (float)Ink.B);
                // the lit edge inside the figure: cut in, the groove's lower wall; standing proud, its top
                float lo = style switch
                {
                    DrumStyle.Inset => EmbossLift * Math.Max(0, a - M(x, y + e)),
                    DrumStyle.Raised => EmbossLift * Math.Max(0, a - M(x, y - e)),
                    _ => 0,
                };
                if (lo > 0) { cr += (rgba[i] - cr) * lo; cg += (rgba[i + 1] - cg) * lo; cb += (rgba[i + 2] - cb) * lo; }
                rgba[i] = (byte)(rgba[i] + (cr - rgba[i]) * a + 0.5f);
                rgba[i + 1] = (byte)(rgba[i + 1] + (cg - rgba[i + 1]) * a + 0.5f);
                rgba[i + 2] = (byte)(rgba[i + 2] + (cb - rgba[i + 2]) * a + 0.5f);
            }
        if (style == DrumStyle.Flat) return;
        if (style == DrumStyle.Gaps) Gap(rgba, w, h, s);
        for (int y = 0; y < h; y++)
        {
            float k = Shade(y, h), sw = ShadowDepth * EdgeWeight(y, h, feather);
            // the lip: the window's top edge lit from above, the bottom one less
            float lw = !lip || feather <= 0 ? 0 : y < Math.Max(1, s / 2) ? LipLight : y >= h - Math.Max(1, s / 2) ? LipLight / 2 : 0;
            for (int x = s; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    byte sc = c == 0 ? ShadowRgb.R : c == 1 ? ShadowRgb.G : ShadowRgb.B;
                    float v = rgba[i + c] * k;
                    v += (sc - v) * sw;
                    v += (200 - v) * lw;
                    rgba[i + c] = (byte)Math.Clamp(v + 0.5f, 0, 255);
                }
            }
        }
    }

    /// <summary>The drum's radius in pixels for a cell <paramref name="h"/> high.</summary>
    public static float Radius(int h) => h / 2f / MathF.Sin(HalfAngle * MathF.PI / 180);

    /// <summary>
    /// The light on row <paramref name="y"/> (from the top) of a cell <paramref name="h"/> high, 0..1: the face's angle to
    /// the line of sight there, Lambert from a light a little above, with the ambient share; 1 where it faces the light.
    /// </summary>
    public static float Shade(int y, int h)
    {
        float v = Math.Clamp((y + 0.5f - h / 2f) / Radius(h), -1, 1);
        float th = MathF.Asin(v), l = -LightAngle * MathF.PI / 180;
        return Ambient + (1 - Ambient) * Math.Max(0, MathF.Cos(th - l));
    }

    private static float[]? FlatMask(DymoFont font, int digit, int w, int h, int s)
    {
        int g = font.Glyph((char)('0' + digit));
        if (g == 0) return null;
        float sc = FigureHeight * s / font.CapHeight;
        return DymoFont.Fill(font.Outline(g), w, h, sc, sc, CentreX * s - font.Advance(g) * sc / 2, Baseline * s);
    }

    /// <summary>
    /// The figure on the drum: set in arc length round the drum (four sample rows a pixel), so that, seen through the
    /// window, its top and foot fall where the flat figure's do, and each pixel row reads the arc a radius R drum puts
    /// there, R·asin(dy / R) from the window's middle: the figure a little shorter toward the edges. With
    /// <paramref name="neighbours"/>, the figures before and after it round the drum (<see cref="Pitch"/> apart; on a
    /// counter the next one up comes from below) as far as the window shows them.
    /// </summary>
    private static float[]? DrumMask(DymoFont font, int digit, int w, int h, int s, bool neighbours)
    {
        int g = font.Glyph((char)('0' + digit));
        if (g == 0) return null;
        const int K = 4;
        float R = Radius(h), c = h / 2f;
        float at = R * MathF.Asin(((Baseline - FigureHeight) * s - c) / R), ab = R * MathF.Asin((Baseline * s - c) / R);
        float span = R * HalfAngle * MathF.PI / 180 + 1;          // the arc the window shows either side, and a pixel
        int hh = (int)MathF.Ceiling(2 * span * K) + 2;
        float sx = FigureHeight * s / font.CapHeight, sy = (ab - at) * K / font.CapHeight;
        var hi = new float[w * hh];
        void Set(int d, float shift)
        {
            int gg = font.Glyph((char)('0' + (d % 10 + 10) % 10));
            if (gg == 0) return;
            var f = DymoFont.Fill(font.Outline(gg), w, hh, sx, sy, CentreX * s - font.Advance(gg) * sx / 2, (ab + shift + span) * K);
            for (int i = 0; i < hi.Length; i++) hi[i] = Math.Max(hi[i], f[i]);
        }
        Set(digit, 0);
        if (neighbours) { Set(digit + 1, Pitch * s); Set(digit - 1, -Pitch * s); }
        var m = new float[w * h];
        const int Sub = 4;
        for (int y = 0; y < h; y++)
            for (int k = 0; k < Sub; k++)
            {
                float dy = y + (k + 0.5f) / Sub - c;
                float hr = (R * MathF.Asin(Math.Clamp(dy / R, -1, 1)) + span) * K - 0.5f;
                int r0 = Math.Clamp((int)MathF.Floor(hr), 0, hh - 1), r1 = Math.Min(hh - 1, r0 + 1);
                float t = Math.Clamp(hr - r0, 0, 1);
                for (int x = 0; x < w; x++) m[y * w + x] += (hi[r0 * w + x] * (1 - t) + hi[r1 * w + x] * t) / Sub;
            }
        return m;
    }

    /// <summary>Gaps: the separator column as the dark gap between two wheels, and a half-pixel rim of shadow either side of the face.</summary>
    private static void Gap(byte[] rgba, int w, int h, int s)
    {
        int rim = Math.Max(1, s / 2);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4;
                if (x < s) { rgba[i] = rgba[i + 1] = rgba[i + 2] = GapGrey; continue; }
                int d = Math.Min(x - s, w - 1 - x);
                if (d < rim) { float k = 0.8f + 0.2f * d / rim; for (int c = 0; c < 3; c++) rgba[i + c] = (byte)(rgba[i + c] * k + 0.5f); }
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
