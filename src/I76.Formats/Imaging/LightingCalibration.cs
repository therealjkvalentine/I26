using I76.Formats.Mission;

namespace I76.Formats.Imaging;

/// <summary>
/// The modern look's lighting calibrated against the 1997 look's ([port] lighting_calibration, docs/deviations.md
/// "Lighting (modern look)"). The 1997 look shows a surface as the engine lights it: each texel (sRGB) times the light
/// ramp's entry LUMA[round(31·light)] through the 3D card's gamma ramp (MonitorBrightness.GlideRamp). The ground's light
/// is d·fade + ambient with d the unnormalised gradient's dot with the light (a slope across the light is as bright as
/// flat ground); an object's is fade·intensity·max(n·L, 0) + ambient on its true normal, so a face turned from the sun
/// sits at the ramp's floor; by day the preset's ambient is 0. The modern look lights the same colours by a sun on the
/// true normal and the sky's light (ambient and reflections) from every side, then Godot's Filmic tonemap. Left alone the
/// two part: the sky light does not fall with the sun as the ramp does and keeps the faces turned from the sun bright (a
/// car's sides, most of what is seen of it), cliffs across the light go dark, and Filmic's toe raises chroma. The
/// calibration solves, per mission, the exposure that gives the ground, averaged over the map's own cells, the 1997
/// ground's luminance and a tint per channel of its colours that gives its colour; then for objects (cars, buildings),
/// at that exposure, the share of the sky light that makes their faces turned from the sun as much darker than the whole
/// as the 1997 look's are, and a tint per channel of their colours that gives them the 1997 look's colour. The modern
/// look's frame is shown through the card's curve by default (<see cref="ToneCurve.Card"/>): Filmic's curve, per channel,
/// steepens the darks (a local slope up to 1.14 in log-log) and flattens the brights (0.5 to 0.6), where the card's ramp
/// is 0.77 throughout, so a colour whose channels sit at different levels changed hue and chroma with its lightness
/// (a car's paint redder and more chromatic in shade, a dusk sky's orange clouds paled toward lilac).
/// </summary>
public static class LightingCalibration
{
    /// <summary>Linear RGB.</summary>
    public readonly record struct Rgb(double R, double G, double B)
    {
        public double this[int c] => c == 0 ? R : c == 1 ? G : B;
        public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;
        public static Rgb operator *(Rgb a, double k) => new(a.R * k, a.G * k, a.B * k);
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    }

    /// <summary>
    /// Surfaces alike: the 1997 look's ramp entry (0..1), the modern look's sun and sky light on them (linear; the sky's
    /// taken at the solved share), their weight, and whether the 1997 look leaves them at the ramp's floor (turned from the sun).
    /// </summary>
    public readonly record struct Cell(double Ramp, Rgb Sun, Rgb Sky, double Count = 1, bool Shade = false)
    {
        public Rgb Light(double sky) => Sun + Sky * sky;
    }

    /// <summary>The ground's view-depth fade: the engine's clamp(z·A + B, 0.75, 1) about 100 m out.</summary>
    public const double TypicalFade = 0.79;

    /// <summary>An object's: the same about 30 m out, where cars are mostly seen.</summary>
    public const double NearFade = 0.76;

    /// <summary>
    /// The sky light on an object's faces against the estimate the ground's solve takes: a car has none of the terrain's
    /// occlusion (its materials' AO on the sky light and a third of the sun's), and its paint and glass reflect the sky.
    /// Measured under Filmic: with it the cars' mean L on T01, T05, M01 and T10 (dusk) comes nearest the 1997 look's (2 and 3
    /// leave them brighter, the most at dusk). Through the card's curve, with the sky's light grey, <see cref="CardObjectSky"/>
    /// (measured by day on T01, T05, M01: 1, 6 and 9 leave the paint +0.062, -0.013 and -0.022 L).
    /// </summary>
    public const double ObjectSky = 4, CardObjectSky = 6;

    /// <summary>
    /// Through the card's curve: the share of the 1997 look's flat-ground weighting in the ground's solve (each cell counts
    /// ny^<see cref="CardFlatWeight"/>: the ramp lights a slope across the sun as a flat, which the modern light cannot, so the
    /// solve fits the flats the eye sees near it rather than the map's mix; measured: 0, 8 and 16 leave the near flat ground
    /// +0.091, +0.066 and +0.050 L, the slopes +0.022, +0.017, +0.018); the glass pane's own painted colour at
    /// <see cref="CardGlassPane"/> (0.3 left the panes -0.046 L by day, 0.6 -0.011); the sky's light on the scene (ambient and
    /// reflections) in grey (<see cref="CardRadianceChroma"/>), as the ramp's light is whatever the sky.
    /// </summary>
    public const double CardFlatWeight = 16, CardGlassPane = 0.6, CardRadianceChroma = 0;

    /// <summary>
    /// The bounds: the exposure (a dusk mission's ground is a third of the modern look's own), the tints (half to double a
    /// channel), the sky light's share (a tenth to all of it).
    /// </summary>
    public const double MinExposure = 0.15, MaxExposure = 2.0, MinTint = 0.4, MaxTint = 2.0, MinSky = 0.1, MaxSky = 1.0;

    public static double ToLinear(double s) => s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);

    public static double ToSrgb(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;

    /// <summary>The modern look's tone curve: Godot's Filmic, or the 3D card's (the 1997 look's display, with a shoulder over its top).</summary>
    public enum ToneCurve { Filmic, Card }

    /// <summary>
    /// The card curve's shoulder: scene light (after the exposure) up to <see cref="Knee"/> as it is, above it eased into 1 by
    /// <see cref="Headroom"/>, so a highlight rolls off as Filmic's does where the 1997 look clips. Below the knee the curve is
    /// the 1997 look's display exactly (all of the 1997 frame's colours but its near whites).
    /// </summary>
    public const double Knee = 0.8, Headroom = 2.0;

    public static double Shoulder(double v) => v <= Knee ? v : Knee + (1 - Knee) * Math.Tanh((v - Knee) / (1 - Knee));

    /// <summary>The card curve: scene light (linear, exposed) shown as the 1997 look shows a colour, its sRGB value through the Glide ramp; linear.</summary>
    public static double CardTone(double v) => ToLinear(Card(ToSrgb(Shoulder(Math.Max(v, 0)))));

    /// <summary>What the modern look shows (linear) for scene light <paramref name="v"/> after the exposure, through <paramref name="curve"/>.</summary>
    public static double Display(double v, ToneCurve curve) => curve == ToneCurve.Card ? CardTone(v) : Filmic(v);

    /// <summary>
    /// The card curve as Godot applies it: a linear tonemap at the exposure over <see cref="Headroom"/>, then the frame's sRGB
    /// value u (0..1) looked up in a table of <paramref name="n"/> entries (the colour correction): the shoulder over
    /// Headroom·linear(u), Monitor Brightness's palette curve for <paramref name="level"/> (the identity at 3) and the Glide
    /// ramp, as the 1997 look's table (MonitorBrightness.CardCurve) is; sRGB, 0..1.
    /// </summary>
    public static double[] CardToneTable(int n, int level)
    {
        double k = 1 / MonitorBrightness.PaletteGamma(level);
        var t = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = ToSrgb(Shoulder(Headroom * ToLinear(i / (double)(n - 1))));
            t[i] = Card(Math.Pow(Math.Clamp(s, 0, 1), k));
        }
        return t;
    }

    /// <summary>Godot's Filmic tonemap (Hable's curve with an exposure bias of 2, white 1), clipped at 1.</summary>
    public static double Filmic(double x)
    {
        static double H(double v)
        {
            const double a = 0.22 * 4, b = 0.30 * 2, c = 0.10, d = 0.20, e = 0.01, f = 0.30;
            return (v * (a * v + c * b) + d * e) / (v * (a * v + b) + d * f) - e / f;
        }
        return Math.Clamp(H(Math.Max(x, 0)) / H(1), 0, 1);
    }

    /// <summary>The 3D card's display of an sRGB value (0..1): the Glide driver's default gamma ramp.</summary>
    public static double Card(double s) => Math.Pow(Math.Clamp(s, 0, 1), 1 / MonitorBrightness.GlideGamma);

    /// <summary>The ramp's entry (0..1) for light <paramref name="light"/>: LUMA[round(31·light)], or light^0.4 without a table.</summary>
    public static double Ramp(IReadOnlyList<byte>? luma, double light)
    {
        int k = (int)Math.Round(31 * Math.Clamp(light, 0, 1));
        return luma is { Count: >= 32 } ? luma[k] / 255.0 : Math.Pow(k / 31.0, 0.4);
    }

    /// <summary>
    /// How much of the 1997 look's night ground the modern look adds to its own (terrain_night.gdshaderinc): the ground's colour
    /// times the ramp's entry for the light's direction alone (d·fade + ambient), as the 1997 look shows it, which the night maps'
    /// dark-painted ground shows nearly as by day. 0: the ground keeps the moon's light alone (<see cref="NightMoon"/>), a tenth of
    /// the 1997 look's (T06, Oklab L 0.10 against 0.26), chosen at the console over the 1997 level: the night is dark and the
    /// headlamps are what shows the road. 1 (I76_NIGHT_GROUND=1) is the 1997 look's level.
    /// </summary>
    public const double NightGround = 0;

    /// <summary>
    /// The moon's glint on the ground at night: its specular reflection, which does not scale with the ground's colour and so is
    /// most of what shows of a night map's near-black ground (T06: about three times its diffuse light). Godot's lobe on the
    /// terrain (roughness 0.9, specular 0.5, the moon's specular amount 0.5) seen a few metres up across the ground: the GGX
    /// distribution about 0.3 over the lobe's width, its visibility 0.76, F0 0.04, times PI as the renderer scales a light's
    /// colour; times the moon's colour and energy and n·L.
    /// </summary>
    public const double GroundGlint = 0.3 * 0.76 * 0.04 * 0.5 * Math.PI;

    /// <summary>
    /// At night, the share of its own light the moon keeps through the card's curve, so the ground shows as the modern look's own
    /// night shows it (lighting_calibration off: the whole moon through Filmic): the card's curve is the brighter in the darks
    /// (2.3 times Filmic at a scene light of 0.001, 1.6 at 0.02), so the moon-lit ground, near black under Filmic and chosen so at the
    /// console, came out a third lighter through it. The share that gives the ground's cells (<see cref="Cells"/>, lit by the moon
    /// and the sky's light) and texels, with the moon's <paramref name="glint"/> on them (<see cref="GroundGlint"/>, in the moon's
    /// colour, times n·L), the mean luminance Filmic shows them at under the whole moon; 1 where Filmic is no darker.
    /// </summary>
    public static double NightMoon(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> ground, Rgb glint)
    {
        if (ground.Count == 0 || texels.Length < 3) return 1;
        var tx = Linear(texels);
        double moonY = Math.Max(glint.Luminance / GroundGlint, 1e-9);
        double Shown(double k, ToneCurve curve)
        {
            Func<double, double> show = curve == ToneCurve.Card ? CardTone : Filmic;
            double[] s = new double[3];
            int n = tx.Length / 3;
            double m = 0;
            foreach (var cell in ground)
            {
                var l = cell.Light(1);
                double gl = Math.Min(cell.Sun.Luminance / moonY, 1);   // n·L: the cell's share of the moon's light
                for (int i = 0; i < n; i++)
                    for (int c = 0; c < 3; c++) s[c] += cell.Count * show(k * (l[c] * tx[3 * i + c] + glint[c] * gl));
                m += n * cell.Count;
            }
            m = Math.Max(m, 1e-9);
            return new Rgb(s[0] / m, s[1] / m, s[2] / m).Luminance;
        }
        double want = Shown(1, ToneCurve.Filmic);
        if (want >= Shown(1, ToneCurve.Card)) return 1;
        return Bisect(k => Shown(k, ToneCurve.Card), want, 0.01, 1);
    }

    /// <summary>
    /// The share of the ground's light a road keeps at night, so a road shows against the flat ground beside it as the original
    /// shows it: the original lights a road by the preset's intensity through the ramp, LUMA[round(31·(I·Ly + ambient))] (T06: 34 of
    /// 255), and flat ground by the light's direction alone, LUMA[round(31·(Ly·fade + ambient))] (205 of 255), so a night road is
    /// the darker band and its own texture, lighter than the night ground's, does not make it shine. The modern moon lights both
    /// alike, so a road at the same light was twice the ground's lightness (T06: L 0.26 against 0.11; at the share, 0.05 against 0.10,
    /// as the 1997 look's 0.23 against 0.26 is in its ramp's terms). Solved through the card's
    /// curve for a mid grey (0.18) at the ground's moonlight <paramref name="moon"/> (energy times luminance, times Ly): the share
    /// that shows the grey as the road's ramp entry shows it against the ground's. 1 when the road's entry is no darker.
    /// </summary>
    public static double NightRoad(IReadOnlyList<byte>? luma, double intensity, double ly, double ambient, double moon)
    {
        const double grey = 0.18;
        double road = Ramp(luma, Math.Max(intensity * ly, 0) + ambient);
        double ground = Ramp(luma, Math.Min(Math.Max(ly, 0) * TypicalFade + ambient, 1));
        if (road >= ground) return 1;
        double want = ToLinear(ToSrgb(grey) * road) / Math.Max(ToLinear(ToSrgb(grey) * ground), 1e-9);
        double v = grey * moon * Math.Max(ly, 0.05);
        double shown = CardTone(v);
        return Bisect(k => CardTone(k * v) / Math.Max(shown, 1e-9), want, 1e-4, 1);
    }

    /// <summary>
    /// The modern night sky's gain through the card's curve (sky_modern.gdshaderinc sky_gain; the moon's disc is not scaled): 1 is the
    /// texture as the 1997 dome shows it (T06, Oklab L 0.28 against the 1997 look's 0.24); darker to sit over a ground at a tenth
    /// of the 1997 look's light (<see cref="NightGround"/>), chosen at the console (I76_NIGHT_SKY to compare): a quarter left the sky
    /// (T06, Oklab L 0.18) at twice the ground's lightness, where the original's night sky sits near its ground's (the retail game's
    /// T06 through the 3D card: sky 0.25, ground 0.21 to 0.28), so it is darker again, near the ground's (0.11 against 0.10).
    /// </summary>
    public const double NightSky = 0.05;

    /// <summary>
    /// How much of the ground's own colour the moon's light on it keeps at night (terrain_modern_night.gdshader i76_night_chroma): the
    /// original's ramp is a grey light on the texture's colour, so its night ground is the texture's own colour, darkened (T06's a
    /// near-neutral blue-grey: in the retail game and the 1997 look Oklab chroma 0.04 of its lightness); the modern moon is blue
    /// (0.6, 0.7, 1) and its glint on the ground, most of a near-black ground's light, is in the moon's colour, so the moonlit ground was
    /// four times as chromatic, a dark blue. At 1 the moon's light on the ground is its luminance in grey and its glint takes the
    /// ground's colour at the glint's own luminance (<see cref="NightGroundLight"/>), so the ground darkens without changing colour, as
    /// the ramp darkens it; 0 is the moon's own colour on it (I76_NIGHT_CHROMA to compare). The cars and the sky keep the moon's blue.
    /// </summary>
    public const double NightChroma = 1;

    /// <summary>
    /// How far the moon's light on the ground at night is turned toward moonlight's own colour, a warm white (about 4100 K: the sun's
    /// light off the moon's regolith, redder than daylight; the modern moon is drawn blue, as night is shown by convention), at the same
    /// luminance (terrain_modern_night.gdshader i76_night_warm). 0: the original's colours, the ground the texture's own (T06's is a
    /// near-neutral blue-grey, Oklab chroma 0.03 to 0.04 of its lightness, in the original as in the 1997 look). 1 shows it as a dark
    /// brown-grey (I76_NIGHT_WARM to compare).
    /// </summary>
    public const double NightWarm = 0;

    /// <summary>The moon's warm white (<see cref="NightWarm"/>), linear, at a luminance of 1.</summary>
    public static readonly Rgb Moonlight = new(1.472, 0.909, 0.518);

    /// <summary>
    /// The moon's light on a ground texel at night as the night terrain shader lights it (linear): its diffuse light,
    /// <paramref name="albedo"/> times the moon's light <paramref name="moon"/> (colour times energy and n·L), and its glint,
    /// <paramref name="glint"/> of the moon's light off the surface, each with <paramref name="chroma"/> of the ground's own colour
    /// (<see cref="NightChroma"/>): the moon's colour turned toward its luminance in grey, the glint toward the albedo's colour at
    /// the albedo's luminance, and turned <paramref name="warm"/> of the way toward <see cref="Moonlight"/> at its luminance.
    /// </summary>
    public static Rgb NightGroundLight(Rgb albedo, Rgb moon, double glint, double chroma, double warm = 0)
    {
        double ml = moon.Luminance, al = Math.Max(albedo.Luminance, 1e-9);
        var light = new Rgb(ml + (moon.R - ml) * (1 - chroma), ml + (moon.G - ml) * (1 - chroma), ml + (moon.B - ml) * (1 - chroma));
        light = light * (1 - warm) + Moonlight * (ml * warm);
        var hue = new Rgb(1 + (albedo.R / al - 1) * chroma, 1 + (albedo.G / al - 1) * chroma, 1 + (albedo.B / al - 1) * chroma);
        return new Rgb(albedo.R * light.R + glint * light.R * hue.R, albedo.G * light.G + glint * light.G * hue.G, albedo.B * light.B + glint * light.B * hue.B);
    }

    /// <summary>
    /// The map's ground as cells, every <paramref name="stride"/>th sample each way: the engine's gradient (central
    /// differences, in sample units) and from it both looks' light; cells with the same ramp entry and nearly the same
    /// modern light (to 1/16) counted together. <paramref name="light"/>: toward the light, game frame;
    /// <paramref name="sun"/>: the modern sun's energy times its colour; <paramref name="sky"/>: the modern sky light on
    /// flat ground (a slope sees (1 + ny)/2 of it).
    /// </summary>
    public static List<Cell> Cells(TerrainTiles t, int stride, System.Numerics.Vector3 light, IReadOnlyList<byte>? luma, double ambient, Rgb sun, Rgb sky, double flat = 0)
    {
        const double k = 0.1 / 5.0;   // height units to sample units (TerrainNode)
        var groups = new Dictionary<(double, int, int, int), Cell>();
        var (r0, r1, c0, c1) = t.Bounds();
        for (int sz = r0 * TerrainTiles.Tile + 1; sz < (r1 + 1) * TerrainTiles.Tile - 1; sz += stride)
            for (int sx = c0 * TerrainTiles.Tile + 1; sx < (c1 + 1) * TerrainTiles.Tile - 1; sx += stride)
            {
                if (t.Height(sx - 1, sz) is not { } w || t.Height(sx + 1, sz) is not { } e || t.Height(sx, sz - 1) is not { } s || t.Height(sx, sz + 1) is not { } n) continue;
                double dhdx = (e - w) * k / 2, dhdz = (n - s) * k / 2;
                double d = light.Y - dhdx * light.X - dhdz * light.Z;
                double lit = d < 0 ? ambient : Math.Min(d * TypicalFade + ambient, 1);
                double len = Math.Sqrt(1 + dhdx * dhdx + dhdz * dhdz);
                var cell = new Cell(Ramp(luma, lit), sun * Math.Max(d / len, 0), sky * ((1 + 1 / len) / 2));
                var l = cell.Light(1);
                var key = (cell.Ramp, (int)Math.Round(l.R * 16), (int)Math.Round(l.G * 16), (int)Math.Round(l.B * 16));
                double wf = Math.Pow(1 / len, flat);
                groups[key] = groups.TryGetValue(key, out var g) ? g with { Count = g.Count + wf } : cell with { Count = wf };
            }
        return groups.Values.ToList();
    }

    /// <summary>
    /// An object's faces as cells, in a car's proportions: up (0.3), at 45 degrees (0.3) and upright (0.4), the last two
    /// at eight bearings each. The 1997 light is fade·intensity·max(n·L, 0) + ambient (<see cref="NearFade"/>); the modern
    /// look's the sun's on the normal and the sky's from above the horizon (<paramref name="skyUp"/>) and below it
    /// (<paramref name="skyDown"/>), each in the share of it the face sees, times <see cref="ObjectSky"/>.
    /// </summary>
    public static List<Cell> ObjectCells(System.Numerics.Vector3 light, IReadOnlyList<byte>? luma, double intensity, double ambient, Rgb sun, Rgb skyUp, Rgb skyDown, double objectSky = ObjectSky)
    {
        var l = System.Numerics.Vector3.Normalize(light);
        var cells = new List<Cell>();
        void Add(double nx, double ny, double nz, double weight)
        {
            double d = nx * l.X + ny * l.Y + nz * l.Z;
            double lit = Math.Min(NearFade * intensity * Math.Max(d, 0) + ambient, 1);
            cells.Add(new Cell(Ramp(luma, lit), sun * Math.Max(d, 0), (skyUp * ((1 + ny) / 2) + skyDown * ((1 - ny) / 2)) * objectSky, weight, d <= 0));
        }
        Add(0, 1, 0, 0.3);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4, c = Math.Cos(a), s = Math.Sin(a);
            Add(c * Math.Sqrt(0.5), Math.Sqrt(0.5), s * Math.Sqrt(0.5), 0.3 / 8);
            Add(c, 0, s, 0.4 / 8);
        }
        return cells;
    }

    /// <summary>The 1997 look's surfaces (linear, displayed): the mean over cells and texels (sRGB bytes, RGB RGB ...).</summary>
    public static Rgb Target(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> cells)
    {
        double[] s = new double[3];
        int n = texels.Length / 3;
        double m = 0;
        foreach (var cell in cells)
        {
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 3; c++) s[c] += cell.Count * ToLinear(Card(texels[3 * i + c] / 255.0 * cell.Ramp));
            m += n * cell.Count;
        }
        m = Math.Max(m, 1e-9);
        return new(s[0] / m, s[1] / m, s[2] / m);
    }

    /// <summary>The modern look's surfaces (linear, displayed): the texels' colours tinted, lit by each cell (the sky light at its share), exposed and shown through <paramref name="curve"/>, the mean.</summary>
    public static Rgb Modern(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> cells, double exposure, Rgb tint, double sky = 1, ToneCurve curve = ToneCurve.Filmic)
        => Modern(Linear(texels), cells, exposure, tint, sky, curve);

    private static double[] Linear(ReadOnlySpan<byte> texels)
    {
        var lin = new double[texels.Length];
        for (int i = 0; i < lin.Length; i++) lin[i] = ToLinear(texels[i] / 255.0);
        return lin;
    }

    private static Rgb Modern(double[] lin, IEnumerable<Cell> cells, double exposure, Rgb tint, double sky, ToneCurve curve)
    {
        Func<double, double> show = curve == ToneCurve.Card ? CardTone : Filmic;
        double[] s = new double[3];
        int n = lin.Length / 3;
        double m = 0;
        foreach (var cell in cells)
        {
            var l = cell.Light(sky);
            double kr = exposure * tint.R * l.R, kg = exposure * tint.G * l.G, kb = exposure * tint.B * l.B;
            double sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < n; i++) { sr += show(kr * lin[3 * i]); sg += show(kg * lin[3 * i + 1]); sb += show(kb * lin[3 * i + 2]); }
            s[0] += cell.Count * sr; s[1] += cell.Count * sg; s[2] += cell.Count * sb;
            m += n * cell.Count;
        }
        m = Math.Max(m, 1e-9);
        return new(s[0] / m, s[1] / m, s[2] / m);
    }

    /// <summary>
    /// The solved calibration: the exposure, the objects' share of the sky light, the ground's tint and the objects'; at night
    /// through the card's curve, none of those but the moon's share (<see cref="NightMoon"/>).
    /// </summary>
    public readonly record struct Result(double Exposure, double Sky, Rgb GroundTint, Rgb ObjectTint, double Moon = 1)
    {
        public static readonly Result None = new(1, 1, new(1, 1, 1), new(1, 1, 1));
    }

    /// <summary>
    /// The calibration (<see cref="Result"/>). The ground: the exposure that gives it the 1997 ground's luminance, untinted,
    /// then its tint per channel. The objects, at that exposure: the share of the sky light that makes their faces turned
    /// from the sun as much darker than the whole of them as the 1997 look's are (luminance, untinted), then their tint per
    /// channel. Within the bounds above. With no ground cells, none; with no object cells (or none turned from the sun) the
    /// objects keep the whole sky light. The texels are the ground's, and stand for the objects' too (the mission's own colours;
    /// through the card curve, near a power, a tint acts alike on every colour). The modern look shown through <paramref name="curve"/>.
    /// </summary>
    public static Result Solve(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> ground, IReadOnlyList<Cell> objects, ToneCurve curve = ToneCurve.Filmic)
    {
        var one = new Rgb(1, 1, 1);
        if (ground.Count == 0 || texels.Length < 3) return Result.None;
        var tx = Linear(texels);
        var target = Target(texels, ground);
        double exposure = Bisect(e => Modern(tx, ground, e, one, 1, curve).Luminance, target.Luminance, MinExposure, MaxExposure);
        var groundTint = Tint(tx, target, ground, exposure, 1, curve);
        double sky = 1;
        var shade = objects.Where(c => c.Shade).ToList();
        if (shade.Count > 0 && shade.Count < objects.Count)
        {
            double want = Target(texels, shade).Luminance / Math.Max(Target(texels, objects).Luminance, 1e-9);
            sky = Bisect(k => Modern(tx, shade, exposure, one, k, curve).Luminance / Math.Max(Modern(tx, objects, exposure, one, k, curve).Luminance, 1e-9), want, MinSky, MaxSky);
        }
        var objectTint = objects.Count > 0 ? Tint(tx, Target(texels, objects), objects, exposure, sky, curve) : one;
        return new(exposure, sky, groundTint, objectTint);
    }

    private static Rgb Tint(double[] tx, Rgb target, IReadOnlyList<Cell> cells, double exposure, double sky, ToneCurve curve)
    {
        var t = new double[3];
        for (int c = 0; c < 3; c++)
        {
            int ch = c;
            t[c] = Bisect(k => Modern(tx, cells, exposure, new Rgb(ch == 0 ? k : 1, ch == 1 ? k : 1, ch == 2 ? k : 1), sky, curve)[ch], target[c], MinTint, MaxTint);
        }
        return new(t[0], t[1], t[2]);
    }

    // f increasing: the x in lo..hi where f(x) = y (an end if y is out of reach)
    private static double Bisect(Func<double, double> f, double y, double lo, double hi)
    {
        for (int i = 0; i < 20; i++)
        {
            double mid = Math.Sqrt(lo * hi);
            if (f(mid) < y) lo = mid; else hi = mid;
        }
        return Math.Sqrt(lo * hi);
    }
}
