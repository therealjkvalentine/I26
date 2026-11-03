using I76.Formats.Mission;

namespace I76.Formats.Imaging;

/// <summary>
/// The modern look's colour match against the 1997 look ([port] look_colour_match, docs/deviations.md "Colour (modern
/// look)"). The 1997 look shows a cell of ground as the engine lights it: each texel (sRGB) times the light ramp's entry
/// LUMA[round(31·light)], light = d·fade + ambient with d the unnormalised gradient's dot with the light (so a slope
/// across the light is as bright as flat ground), through the 3D card's gamma ramp (MonitorBrightness.GlideRamp). The
/// modern look lights the same colours (its ground palette is the texture's) by a sun on the true normal and the sky's
/// ambient, then Godot's Filmic tonemap. Left alone the two part: the ambient does not fall with the sun as the ramp
/// does, cliffs across the light go dark, and Filmic's toe raises the ground's chroma. The match solves, per mission,
/// the exposure that gives the modern ground, averaged over the map's own cells, the 1997 ground's luminance, then a
/// tint per channel of the ground's colours that gives it the 1997 ground's colour.
/// </summary>
public static class LookColourMatch
{
    /// <summary>Linear RGB.</summary>
    public readonly record struct Rgb(double R, double G, double B)
    {
        public double this[int c] => c == 0 ? R : c == 1 ? G : B;
        public double Luminance => 0.2126 * R + 0.7152 * G + 0.0722 * B;
        public static Rgb operator *(Rgb a, double k) => new(a.R * k, a.G * k, a.B * k);
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    }

    /// <summary>Cells of ground alike: the 1997 look's ramp entry (0..1), the modern look's light on them (linear), how many.</summary>
    public readonly record struct Cell(double Ramp, Rgb Light, int Count = 1);

    /// <summary>The view-depth fade the match takes: the engine's clamp(z·A + B, 0.75, 1) about 100 m out.</summary>
    public const double TypicalFade = 0.79;

    /// <summary>The exposure's bounds (a dusk mission's ground is a third of the modern look's own) and the tint's (half to double a channel).</summary>
    public const double MinExposure = 0.25, MaxExposure = 2.0, MinTint = 0.5, MaxTint = 2.0;

    public static double ToLinear(double s) => s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);

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
    /// The map's ground as cells, every <paramref name="stride"/>th sample each way: the engine's gradient (central
    /// differences, in sample units) and from it both looks' light; cells with the same ramp entry and nearly the same
    /// modern light (to 1/16) counted together. <paramref name="light"/>: toward the light, game frame;
    /// <paramref name="sun"/>: the modern sun's energy times its colour; <paramref name="sky"/>: the modern ambient on flat
    /// ground (a slope sees (1 + ny)/2 of it).
    /// </summary>
    public static List<Cell> Cells(TerrainTiles t, int stride, System.Numerics.Vector3 light, IReadOnlyList<byte>? luma, double ambient, Rgb sun, Rgb sky)
    {
        const double k = 0.1 / 5.0;   // height units to sample units (TerrainNode)
        var groups = new Dictionary<(double, int, int, int), (Rgb Light, int Count)>();
        var (r0, r1, c0, c1) = t.Bounds();
        for (int sz = r0 * TerrainTiles.Tile + 1; sz < (r1 + 1) * TerrainTiles.Tile - 1; sz += stride)
            for (int sx = c0 * TerrainTiles.Tile + 1; sx < (c1 + 1) * TerrainTiles.Tile - 1; sx += stride)
            {
                if (t.Height(sx - 1, sz) is not { } w || t.Height(sx + 1, sz) is not { } e || t.Height(sx, sz - 1) is not { } s || t.Height(sx, sz + 1) is not { } n) continue;
                double dhdx = (e - w) * k / 2, dhdz = (n - s) * k / 2;
                double d = light.Y - dhdx * light.X - dhdz * light.Z;
                double lit = d < 0 ? ambient : Math.Min(d * TypicalFade + ambient, 1);
                double len = Math.Sqrt(1 + dhdx * dhdx + dhdz * dhdz);
                var l = sun * Math.Max(d / len, 0) + sky * ((1 + 1 / len) / 2);
                var key = (Ramp(luma, lit), (int)Math.Round(l.R * 16), (int)Math.Round(l.G * 16), (int)Math.Round(l.B * 16));
                groups[key] = groups.TryGetValue(key, out var g) ? (g.Light, g.Count + 1) : (l, 1);
            }
        return groups.Select(g => new Cell(g.Key.Item1, g.Value.Light, g.Value.Count)).ToList();
    }

    /// <summary>The 1997 look's ground (linear, displayed): the mean over cells and texels (sRGB bytes, RGB RGB ...).</summary>
    public static Rgb Target(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> cells)
    {
        double[] s = new double[3];
        int n = texels.Length / 3;
        double m = 0;
        foreach (var cell in cells)
        {
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 3; c++) s[c] += cell.Count * ToLinear(Card(texels[3 * i + c] / 255.0 * cell.Ramp));
            m += (double)n * cell.Count;
        }
        m = Math.Max(m, 1);
        return new(s[0] / m, s[1] / m, s[2] / m);
    }

    /// <summary>The modern look's ground (linear, displayed): the texels' colours tinted, lit by each cell, exposed and tonemapped, the mean.</summary>
    public static Rgb Modern(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> cells, double exposure, Rgb tint)
    {
        var lin = new double[texels.Length];
        for (int i = 0; i < lin.Length; i++) lin[i] = ToLinear(texels[i] / 255.0);
        return Modern(lin, cells, exposure, tint);
    }

    private static Rgb Modern(double[] lin, IReadOnlyList<Cell> cells, double exposure, Rgb tint)
    {
        double[] s = new double[3];
        int n = lin.Length / 3;
        double m = 0;
        foreach (var cell in cells)
        {
            double kr = exposure * tint.R * cell.Light.R, kg = exposure * tint.G * cell.Light.G, kb = exposure * tint.B * cell.Light.B;
            double sr = 0, sg = 0, sb = 0;
            for (int i = 0; i < n; i++) { sr += Filmic(kr * lin[3 * i]); sg += Filmic(kg * lin[3 * i + 1]); sb += Filmic(kb * lin[3 * i + 2]); }
            s[0] += cell.Count * sr; s[1] += cell.Count * sg; s[2] += cell.Count * sb;
            m += (double)n * cell.Count;
        }
        m = Math.Max(m, 1);
        return new(s[0] / m, s[1] / m, s[2] / m);
    }

    /// <summary>
    /// The exposure and ground tint that show the modern ground as the 1997 look does: the exposure matches the
    /// luminance with the ground untinted, then the tint each channel; within the bounds above. None (1 and white) without cells.
    /// </summary>
    public static (double Exposure, Rgb Tint) Solve(ReadOnlySpan<byte> texels, IReadOnlyList<Cell> cells)
    {
        var one = new Rgb(1, 1, 1);
        if (cells.Count == 0 || texels.Length < 3) return (1, one);
        var target = Target(texels, cells);
        var tx = new double[texels.Length];
        for (int i = 0; i < tx.Length; i++) tx[i] = ToLinear(texels[i] / 255.0);
        double exposure = Bisect(e => Modern(tx, cells, e, one).Luminance, target.Luminance, MinExposure, MaxExposure);
        var t = new double[3];
        for (int c = 0; c < 3; c++)
        {
            int ch = c;
            t[c] = Bisect(k => Modern(tx, cells, exposure, new Rgb(ch == 0 ? k : 1, ch == 1 ? k : 1, ch == 2 ? k : 1))[ch], target[c], MinTint, MaxTint);
        }
        return (exposure, new Rgb(t[0], t[1], t[2]));
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
