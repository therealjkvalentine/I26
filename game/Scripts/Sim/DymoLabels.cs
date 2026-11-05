namespace I76.Game.Sim;

/// <summary>
/// The modern look's weapon and special labels: the name embossed in a label-tape font (Impact Label Reversed, game/Fonts)
/// on the plate's own tape, in place of the plate's painted letters. The 1997 look keeps the painted plates. Engine-free:
/// the cockpit panels hand in the plate's texels and take the label's back.
/// </summary>
public static class DymoLabels
{
    /// <summary>
    /// What the original's plates say, by the plate's stem (the gdf's plate name without its '3' and _on/_off; the specials
    /// panel's sprite names): the painted spelling, which differs from the weapon tables' names ("FireRite Rkt" is
    /// FIRE-RITE RKT, "50cal MG" 50 CAL MG). The tank cannons' plate is the original's EMPTY.
    /// </summary>
    private static readonly Dictionary<string, string> Painted = new(StringComparer.OrdinalIgnoreCase)
    {
        ["car_eraser"] = "CAR-E-RACER", ["oilslick"] = "OIL SLICK", ["bloxdroppr"] = "BLOXDROPPER", ["caltrops"] = "CALTROPS",
        ["landmines"] = "LANDMINES", ["smokescrn"] = "SMOKE SCREEN", ["firedroppr"] = "FIRE-DROPPER", ["he_mortar"] = "HE MORTAR",
        ["ezk_mortar"] = "EZK MORTAR", ["wp_mortar"] = "WP MORTAR", ["clustrbmb"] = "CLUSTER-BOMB", ["fr_rocket"] = "FIRE-RITE RKT",
        ["an_missile"] = "AIM-NEIN MSL", ["cherub_msl"] = "CHERUB MSL", ["dr_missile"] = "DR RADAR MSL", ["gaslaunchr"] = "GAS LAUNCHER",
        ["flame"] = "FLAMETHROWER", ["napalm"] = "NAPALM HOSE", ["pyro_tonic"] = "PYRO-TOMIC", ["20mm_can"] = "20MM CANNON",
        ["25mm_can"] = "25MM CANNON", ["30mm_can"] = "30MM CANNON", ["hades_can"] = "HADES CANNON", ["30cal_mg"] = "30 CAL MG",
        ["50cal_mg"] = "50 CAL MG", ["762_mg"] = "7.62MM MG", ["empty"] = "EMPTY",
        ["30cal_trt"] = "30 CAL TURRET", ["50cal_trt"] = "50 CAL TURRET", ["762_trt"] = "7.62MM TURRET", ["20mm_trt"] = "20MM TURRET",
        ["25mm_trt"] = "25MM TURRET", ["30mm_trt"] = "30MM TURRET", ["hades_trt"] = "HADES TURRET", ["fr_trt"] = "FIRE-RITE TRT",
        ["aim_trt"] = "AIM-NEIN TRT", ["cherub_trt"] = "CHERUB TRT", ["dr_trt"] = "DR RADAR TRT", ["gas_trt"] = "GAS LNCH TRT",
        ["flame_trt"] = "FLAME TURRET", ["napalm_trt"] = "NAPALM TRT", ["pyro_trt"] = "PYRO-TURRET", ["4get_u_not"] = "4GET-U-NOT",
        ["4get_trt"] = "4GET-U-NOT TRT", ["ball_o_fire"] = "BALL O'FLAME", ["ball_trt"] = "BALL O'FLM TRT", ["chem_bomb"] = "CHEM MORTAR",
        // the specials panel
        ["sp_empty"] = "EMPTY", ["radar"] = "RADAR JAMMER", ["nitrous"] = "NITROUS OXIDE", ["blower"] = "BLOWER", ["xaust"] = "X-AUST BRAKE",
        ["structo"] = "STRUCTO BMPR", ["spiked"] = "SPIKED HUBS", ["curb"] = "CURB FEELERS", ["mud"] = "MUD FLAPS", ["heated"] = "HEATED SEATS",
        ["cuphldr"] = "CUP HOLDER", ["tire_covr"] = "TIRE COVERS", ["dash_mary"] = "DASHBD MARY",
    };

    /// <summary>A full-size plate sprite's stem (the sprite with _on/_off taken off; the gdf's names carry a '3' more, which the cockpit drops).</summary>
    public static string Stem(string plate)
    {
        string s = plate;
        if (s.EndsWith("_on", StringComparison.OrdinalIgnoreCase)) s = s[..^3];
        else if (s.EndsWith("_off", StringComparison.OrdinalIgnoreCase)) s = s[..^4];
        return s;
    }

    /// <summary>
    /// The label's text: the painted spelling for a plate the original has, else the table's name upper-cased (a weapon
    /// the original's sheet has no plate for), else the plate's stem. <paramref name="plate"/> is the full-size sprite's name.
    /// </summary>
    public static string Text(string plate, string? tableName)
    {
        string stem = Stem(plate);
        if (Painted.TryGetValue(stem, out var t)) return t;
        if (!string.IsNullOrWhiteSpace(tableName)) return tableName.Trim().ToUpperInvariant();
        return stem.Replace('_', ' ').ToUpperInvariant();
    }

    // the plate's layout in the original's pixels (93 x 22): the letters' rows 7..14 (8 high, centred on row 11), up to 84
    // wide; rows 5..16 carry the tape under them
    public const int PlateW = 93, PlateH = 22;
    private const float CapRows = 8, CentreRow = 11, Width = 84;
    private const int BandTop = 5, BandBottom = 17;

    /// <summary>
    /// Paints the label over a plate: <paramref name="rgba"/> is the plate at <paramref name="s"/> texels a pixel
    /// (w·s × h·s RGBA8, stored bottom-up as the panels are). The lettering's rows take each row's tape colour (the
    /// commonest colour that is not a letter's grey), then the name is set in the font at the painted letters' height,
    /// closed up by <see cref="Tracking"/>, squeezed across where it is still wider than the tape, embossed as the tape's
    /// raised letters are: a dark shadow down-right, the face a step under white with its edge crisp, lit along its upper-left edges
    /// and shaded along its lower-right ones.
    /// </summary>
    public static void Paint(byte[] rgba, int w, int h, int s, DymoFont font, string text, bool lit)
    {
        int tw = w * s, th = h * s;
        for (int y = BandTop * s; y < Math.Min(BandBottom * s, th); y++)
        {
            int r = (th - 1 - y) * tw * 4;
            if (Tape(rgba, r, tw) is not { } c) continue;
            for (int x = 0; x < tw; x++) { int i = r + x * 4; rgba[i] = c.R; rgba[i + 1] = c.G; rgba[i + 2] = c.B; rgba[i + 3] = 255; }
        }
        var (contours, left, right) = font.Layout(text, Tracking * font.MeanGap);
        if (right <= left) return;
        float sy = CapRows * s / font.CapHeight;
        float sx = Math.Min(sy, Width * s / (right - left));
        float ox = (tw - (right - left) * sx) / 2 - left * sx;
        float oy = (CentreRow + CapRows / 2) * s;
        var face = DymoFont.Fill(contours, tw, th, sx, sy, ox, oy);
        // the face's edge made crisp: coverage 0.35..0.75 ramps to full, so the strokes are solid with a texel's soft
        // edge rather than a wide pink fringe where the antialiased edge blends into the red tape
        for (int k = 0; k < face.Length; k++) face[k] = Math.Clamp((face[k] - 0.35f) / 0.4f, 0, 1);
        // the shadow: the face blurred a little, offset half and three quarters of a pixel
        int dx = (int)Math.Round(0.5f * s), dy = (int)Math.Round(0.75f * s);
        var shadow = Blur(face, tw, th, Math.Max(1, s / 6));
        (byte R, byte G, byte B) shade = lit ? ((byte)60, (byte)0, (byte)12) : ((byte)40, (byte)8, (byte)8);
        byte white = lit ? LitWhite : UnlitWhite, body = lit ? LitFace : UnlitFace;
        // the emboss's bevel: a face texel with no face a quarter pixel up-left of it is a lit edge, one with none that
        // far down-right a shaded edge; under 4 texels a pixel the edge is a texel, wider, so it is drawn fainter
        int e = Math.Max(1, s / 4);
        float bevel = Math.Min(1, s / (4f * e));
        float F(int x, int y) => x >= 0 && y >= 0 && x < tw && y < th ? Math.Min(1, face[y * tw + x]) : 0;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                int i = ((th - 1 - y) * tw + x) * 4;
                if (rgba[i + 3] < 128) continue;
                float a = x >= dx && y >= dy ? Math.Min(1, shadow[(y - dy) * tw + x - dx]) : 0;
                if (a > 0) Mix(rgba, i, shade.R, shade.G, shade.B, a);
                float f = F(x, y);
                if (f <= 0) continue;
                float hi = bevel * Math.Max(0, f - F(x - e, y - e)), lo = bevel * Math.Max(0, f - F(x + e, y + e));
                // the body, lifted to the white on the lit edge and taken down to the bevel's shade on the other
                float v = body + (white - body) * hi - (body - BevelShade) * lo * (1 - hi);
                byte c = (byte)Math.Clamp(v + 0.5f, 0, 255);
                Mix(rgba, i, c, c, c, f);
            }
    }

    /// <summary>
    /// The share of the font's gap between letters taken out (<see cref="DymoFont.MeanGap"/>): the font is a typewriter's,
    /// its letters set about 0.6 em apart with some 0.2 em between them, which at the plates' 8-pixel capitals spread a
    /// name across the whole tape where the painted plates' letters sit closer.
    /// </summary>
    public const float Tracking = 0.3f;

    /// <summary>
    /// The face's colours, lit plate and unlit: the white of its lit upper-left edges, the body of the stroke a step
    /// under it (so the bevel shows; the painted letters are light greys), and the shade of its lower-right edges.
    /// </summary>
    public const byte LitWhite = 255, UnlitWhite = 238, LitFace = 232, UnlitFace = 214, BevelShade = 150;

    private static void Mix(byte[] p, int i, byte r, byte g, byte b, float a)
    {
        p[i] = (byte)(p[i] + (r - p[i]) * a + 0.5f);
        p[i + 1] = (byte)(p[i + 1] + (g - p[i + 1]) * a + 0.5f);
        p[i + 2] = (byte)(p[i + 2] + (b - p[i + 2]) * a + 0.5f);
    }

    /// <summary>A row's tape colour: of its opaque texels with a channel at or under 120 (the letters are light greys), the
    /// commonest colour to 4 bits a channel, averaged; null if the row has none.</summary>
    private static (byte R, byte G, byte B)? Tape(byte[] p, int r, int w)
    {
        var count = new Dictionary<int, (int N, int R, int G, int B)>();
        for (int x = 0; x < w; x++)
        {
            int i = r + x * 4;
            if (p[i + 3] < 128 || Math.Min(p[i], Math.Min(p[i + 1], p[i + 2])) > 120) continue;
            int k = p[i] >> 4 << 8 | p[i + 1] >> 4 << 4 | p[i + 2] >> 4;
            var c = count.GetValueOrDefault(k);
            count[k] = (c.N + 1, c.R + p[i], c.G + p[i + 1], c.B + p[i + 2]);
        }
        if (count.Count == 0) return null;
        var best = count.Values.MaxBy(v => v.N);
        return ((byte)(best.R / best.N), (byte)(best.G / best.N), (byte)(best.B / best.N));
    }

    private static float[] Blur(float[] m, int w, int h, int r)
    {
        var a = new float[m.Length]; var b = new float[m.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += m[y * w + Math.Clamp(x + k, 0, w - 1)];
                a[y * w + x] = sum / (2 * r + 1);
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int k = -r; k <= r; k++) sum += a[Math.Clamp(y + k, 0, h - 1) * w + x];
                b[y * w + x] = sum / (2 * r + 1);
            }
        return b;
    }

    private static DymoFont? _font;
    private static bool _tried;

    /// <summary>The label font, read from the game assembly (null if it is missing: the painted plates are drawn).</summary>
    public static DymoFont? Font
    {
        get
        {
            if (_tried) return _font;
            _tried = true;
            // capture hook: I76_DYMO_FONT=path.ttf letters the labels in another TrueType face (comparing candidates in play)
            if (System.Environment.GetEnvironmentVariable("I76_DYMO_FONT") is { Length: > 0 } alt && System.IO.File.Exists(alt))
            {
                try { return _font = new DymoFont(System.IO.File.ReadAllBytes(alt)); } catch (Exception) { _font = null; }
            }
            using var st = typeof(DymoLabels).Assembly.GetManifestResourceStream("ImpactLabelReversed.ttf");
            if (st is null) return null;
            using var ms = new System.IO.MemoryStream();
            st.CopyTo(ms);
            try { _font = new DymoFont(ms.ToArray()); } catch (Exception) { _font = null; }
            return _font;
        }
    }
}
