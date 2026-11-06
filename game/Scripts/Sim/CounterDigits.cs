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

/// <summary>How the window's edge weight falls off across the feather band (CounterDigits.EdgeWeight).</summary>
public enum FeatherCurve
{
    /// <summary>Smoothstep: symmetric, half the darkening at the band's middle (the earlier look, which read as a band with an inner edge).</summary>
    Ease,
    /// <summary>1 - cos: eases in from nothing at the band's inner end, steepest at the lip.</summary>
    Cos,
    /// <summary>The square of the way in: a quarter of the darkening at the band's middle.</summary>
    Pow2,
    /// <summary>The cube: an eighth at the middle, nearly all of it in the last rows.</summary>
    Pow3,
}

/// <summary>
/// The counter window's edge treatment (CounterDigits.Edge): the band's <see cref="Fraction"/> of the window's height, the
/// <see cref="Curve"/> across it, how dark the housing's colour is taken at the lip (<see cref="Depth"/> of itself), the
/// drum's <see cref="Ambient"/> light (CounterDigits.Ambient, or less with "drum"), a <see cref="Lip"/> light, and
/// <see cref="Black"/> for the earlier fade to a near-black. Spelled as I76_COUNTER_FEATHER spells it.
/// </summary>
public readonly record struct EdgeFade(float Fraction, FeatherCurve Curve, float Depth, float Ambient, bool Lip, bool Black)
{
    public const float DefaultFraction = 0.25f, DefaultDepth = 0.6f, DeepDepth = 0.45f, LightDepth = 0.75f, DrumAmbient = 0.3f;
    public const FeatherCurve DefaultCurve = FeatherCurve.Pow2;

    /// <summary>The cockpit's: a quarter of the window, squared, the housing at six tenths.</summary>
    public static readonly EdgeFade Default = new(DefaultFraction, DefaultCurve, DefaultDepth, CounterDigits.Ambient, false, false);

    /// <summary>
    /// The hook's grammar: a percent of the window's height (0 to 50), then any of the words, in any order, run together or
    /// separated by "-", "+", "," or spaces: "lip" (the lip lit), "black" (the earlier near-black target), "ease", "cos",
    /// "pow2", "pow3" (the curve), "deep" or "light" (the housing at 45 % or 75 % instead of 60 %), "drum" (the drum's ambient
    /// light 0.3 instead of 0.5: its own shading darker toward the edges). "20black" is the earlier look (its smoothstep
    /// unless a curve is named). Empty or unreadable: the default; a word not known is ignored.
    /// </summary>
    public static EdgeFade Parse(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return Default;
        v = v.Trim().ToLowerInvariant();
        int n = 0;
        while (n < v.Length && char.IsDigit(v[n])) n++;
        var e = Default;
        if (n > 0 && int.TryParse(v[..n], out int pct) && pct is >= 0 and <= 50) e = e with { Fraction = pct / 100f };
        bool curveNamed = false;
        foreach (var w in v[n..].Split(new[] { '-', '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var rest = w;
            while (rest.Length > 0)
            {
                string? word = Array.Find(Words, rest.StartsWith);
                if (word is null) break;
                e = word switch
                {
                    "lip" => e with { Lip = true },
                    "black" => e with { Black = true },
                    "ease" => e with { Curve = FeatherCurve.Ease },
                    "cos" => e with { Curve = FeatherCurve.Cos },
                    "pow2" => e with { Curve = FeatherCurve.Pow2 },
                    "pow3" => e with { Curve = FeatherCurve.Pow3 },
                    "deep" => e with { Depth = DeepDepth },
                    "light" => e with { Depth = LightDepth },
                    "drum" => e with { Ambient = DrumAmbient },
                    _ => e,
                };
                curveNamed |= word is "ease" or "cos" or "pow2" or "pow3";
                rest = rest[word.Length..];
            }
        }
        if (e.Black && !curveNamed) e = e with { Curve = FeatherCurve.Ease };
        return e;
    }

    private static readonly string[] Words = { "lip", "black", "ease", "cos", "pow2", "pow3", "deep", "light", "drum" };

    /// <summary>The hook's spelling of this edge (a cache key).</summary>
    public override string ToString() =>
        $"{(int)MathF.Round(Fraction * 100)}{Curve.ToString().ToLowerInvariant()}" + (Depth == DeepDepth ? "-deep" : Depth == LightDepth ? "-light" : "") +
        (Ambient != CounterDigits.Ambient ? "-drum" : "") + (Lip ? "-lip" : "") + (Black ? "-black" : "");
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
    /// The window's edges on a drum style: over the top and bottom <see cref="EdgeFade.Fraction"/> of the window's height
    /// the face and the figure fade into the housing's shadow, as the drum curves away under the lip: toward the colour of
    /// the housing behind the window (sampled round it where the panel is composed, <see cref="DefaultHousing"/> without)
    /// darkened to <see cref="EdgeFade.Depth"/> of itself, never to black, by an ease-in curve (<see cref="EdgeFade.Curve"/>:
    /// most of the darkening in the last rows, no inner edge), the figure's ink thinning as well (to <see cref="InkFade"/> at
    /// the lip). <see cref="EdgeFade.Lip"/> lights the lip's edge faintly; <see cref="EdgeFade.Black"/> is the earlier look,
    /// the fade to a near-black (<see cref="ShadowRgb"/>) by <see cref="ShadowDepth"/>, for comparisons.
    /// I76_COUNTER_FEATHER sets another (<see cref="EdgeFade.Parse"/>; docs/dev-hooks.md).
    /// </summary>
    public static EdgeFade Edge { get; set; } = EdgeFade.Parse(System.Environment.GetEnvironmentVariable("I76_COUNTER_FEATHER"));

    public const float ShadowDepth = 0.85f, InkFade = 0.5f, LipLight = 0.3f;
    public static readonly (byte R, byte G, byte B) ShadowRgb = (24, 24, 20);
    /// <summary>The housing's colour when no panel is behind the cell: the strip's chrome bezel's grey.</summary>
    public static readonly (byte R, byte G, byte B) DefaultHousing = (150, 150, 145);

    /// <summary>
    /// The weight of the window's edge on row <paramref name="y"/> (from the top) of a window <paramref name="h"/> high: 1
    /// at the lip's edge (half a row above row 0), falling to 0 at <paramref name="feather"/> of the height in from it, by
    /// <paramref name="curve"/> (<see cref="FeatherCurve"/>).
    /// </summary>
    public static float EdgeWeight(int y, int h, float feather, FeatherCurve curve = EdgeFade.DefaultCurve)
    {
        if (feather <= 0) return 0;
        float d = Math.Min(y + 0.5f, h - y - 0.5f) / (feather * h);
        if (d >= 1) return 0;
        float t = 1 - d;
        return curve switch
        {
            FeatherCurve.Ease => t * t * (3 - 2 * t),
            FeatherCurve.Cos => 1 - MathF.Cos(MathF.PI * t / 2),
            FeatherCurve.Pow3 => t * t * t,
            _ => t * t,
        };
    }

    /// <summary>The colour a feathered row goes toward: the housing behind the window at the fade's depth, or the earlier near-black.</summary>
    public static (float R, float G, float B) FadeTarget(EdgeFade edge, (byte R, byte G, byte B) housing) =>
        edge.Black ? (ShadowRgb.R, ShadowRgb.G, ShadowRgb.B) : (housing.R * edge.Depth, housing.G * edge.Depth, housing.B * edge.Depth);

    /// <summary>
    /// Paints figure <paramref name="digit"/> (0..9) over a cell: <paramref name="rgba"/> is the strip's cell at
    /// <paramref name="s"/> texels a pixel (CellW·s × CellH·s RGBA8, stored bottom-up as the panels are). Right of the
    /// separator column every row takes its face colour (the cell's rightmost texel, the face in every cell of the
    /// strip), taking the painted figure off; then the font's figure is filled at the strip's figure height and place,
    /// on the drum's curve for the drum styles, and the face shaded.
    /// </summary>
    public static void Paint(byte[] rgba, int s, int digit, DymoFont font, DrumStyle style) => Paint(rgba, s, digit, font, style, Edge, DefaultHousing);

    /// <summary>
    /// Paint with the window's edge given (the drum styles only; Flat has no window edge) and the colour of the
    /// <paramref name="housing"/> behind the window, which the edges fade toward.
    /// </summary>
    public static void Paint(byte[] rgba, int s, int digit, DymoFont font, DrumStyle style, EdgeFade edge, (byte R, byte G, byte B) housing)
    {
        float feather = style == DrumStyle.Flat ? 0 : edge.Fraction;
        var curve = edge.Curve;
        bool lip = edge.Lip;
        var (tr, tg, tb) = FadeTarget(edge, housing);
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
                a *= 1 - InkFade * EdgeWeight(y, h, feather, curve);
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
            float k = Shade(y, h, edge.Ambient), sw = (edge.Black ? ShadowDepth : 1) * EdgeWeight(y, h, feather, curve);
            // the lip: the window's top edge lit from above, the bottom one less
            float lw = !lip || feather <= 0 ? 0 : y < Math.Max(1, s / 2) ? LipLight : y >= h - Math.Max(1, s / 2) ? LipLight / 2 : 0;
            for (int x = s; x < w; x++)
            {
                int i = ((h - 1 - y) * w + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float sc = c == 0 ? tr : c == 1 ? tg : tb;
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
    public static float Shade(int y, int h) => Shade(y, h, Ambient);

    /// <summary>Shade with the ambient share given (EdgeFade "drum": less of it, the cylinder's own shading carrying more of the edge).</summary>
    public static float Shade(int y, int h, float ambient)
    {
        float v = Math.Clamp((y + 0.5f - h / 2f) / Radius(h), -1, 1);
        float th = MathF.Asin(v), l = -LightAngle * MathF.PI / 180;
        return ambient + (1 - ambient) * Math.Max(0, MathF.Cos(th - l));
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
