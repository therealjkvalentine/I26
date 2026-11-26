using System.Text.Json;

namespace I76.Formats.Shell;

/// <summary>
/// The optional widescreen menu pack (tex-menu-wide, tools/menu-wide.py): the sides of the 4:3 menu pack's pages for wider windows,
/// in layers (16x9 beside the 4:3 page, 21x9 beside the 16x9 page), never the pages themselves. Its wide.json names per record
/// (pages, and the Nitro Pack's own under nitro) each layer's strip file (the left side then the right side, side by side, the page's
/// height), or pad (each row's outer columns repeated), and the 4:3 page's edge fingerprint: a wide page is made only over the 4:3
/// page its sides were made for (another 4:3 pack's page stays 4:3). The 4:3 page is copied into the middle untouched.
/// </summary>
public sealed class WideMenuPack
{
    public sealed record Layer(string Name, int Width, int Side, string Over);

    public sealed record Page(IReadOnlyDictionary<string, string> Files, bool Pad, int[,,] Edge);

    public int Height { get; private init; }
    /// <summary>The layers, narrowest first.</summary>
    public IReadOnlyList<Layer> Layers { get; private init; } = [];
    private readonly Dictionary<int, Page> _pages = [];
    public int FingerprintCols { get; private init; } = 4;
    public int FingerprintBands { get; private init; } = 16;
    public int Tolerance { get; private init; } = 8;
    public int PadCols { get; private init; } = 4;

    /// <summary>The 4:3 pages' size the strips go beside (the menu pack's: 4 texels per frame pixel).</summary>
    public const int InnerWidth = 2560, InnerHeight = 1920;

    /// <summary>A record's entry: base game records by their number, the Nitro Pack's own by minus theirs (as ShellBackdrop keys them).</summary>
    public Page? Get(int key) => _pages.GetValueOrDefault(key);
    public int Count => _pages.Count;

    public static WideMenuPack? Parse(byte[]? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var layers = new List<Layer>();
            foreach (var l in r.GetProperty("layers").EnumerateObject())
                layers.Add(new Layer(l.Name, l.Value.GetProperty("width").GetInt32(), l.Value.GetProperty("side").GetInt32(), l.Value.GetProperty("over").GetString() ?? ""));
            layers.Sort((a, b) => a.Width.CompareTo(b.Width));
            var fp = r.TryGetProperty("fingerprint", out var f) ? f : default;
            int I(JsonElement e, string k, int d) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.TryGetInt32(out int n) ? n : d;
            var pack = new WideMenuPack
            {
                Height = r.GetProperty("height").GetInt32(),
                Layers = layers,
                FingerprintCols = I(fp, "cols", 4), FingerprintBands = I(fp, "bands", 16), Tolerance = I(fp, "tolerance", 8),
                PadCols = I(r.TryGetProperty("pad", out var p) ? p : default, "cols", 4),
            };
            foreach (var (section, sign) in new[] { ("pages", 1), ("nitro", -1) })
            {
                if (!r.TryGetProperty(section, out var s) || s.ValueKind != JsonValueKind.Object) continue;
                foreach (var e in s.EnumerateObject())
                {
                    if (!int.TryParse(e.Name, out int rec)) continue;
                    var files = new Dictionary<string, string>();
                    foreach (var l in layers)
                        if (e.Value.TryGetProperty(l.Name, out var fv) && fv.GetString() is { Length: > 0 } file) files[l.Name] = file;
                    bool pad = e.Value.TryGetProperty("pad", out var pv) && pv.ValueKind == JsonValueKind.True;
                    var edge = new int[2, pack.FingerprintBands, 3];
                    if (e.Value.TryGetProperty("edge", out var ev))
                        for (int side = 0; side < 2; side++)
                        {
                            int b = 0;
                            foreach (var band in ev.GetProperty(side == 0 ? "L" : "R").EnumerateArray())
                            {
                                if (b >= pack.FingerprintBands) break;
                                int c = 0;
                                foreach (var v in band.EnumerateArray()) if (c < 3) edge[side, b, c++] = v.GetInt32();
                                b++;
                            }
                        }
                    pack._pages[sign * rec] = new Page(files, pad, edge);
                }
            }
            return pack;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    /// <summary>
    /// The layer for a window of this width to height: the narrowest that fills it, else the widest; null when the 4:3 page already
    /// fills it (a window no wider than 4:3). A 16:10 window takes 16x9 (its sides cut a little at the window's edges), 16:9 16x9,
    /// 3440x1440 21x9, 32:9 21x9 with bars beyond.
    /// </summary>
    public Layer? Choose(float aspect)
    {
        if (Layers.Count == 0 || aspect <= 4f / 3f + 0.01f) return null;
        foreach (var l in Layers) if ((float)l.Width / Height >= aspect - 0.005f) return l;
        return Layers[^1];
    }

    /// <summary>The 4:3 page's edges as tools/menu-wide.py's fingerprint: per side the mean RGB of the outer columns in row bands (RGBA8, rows from the top).</summary>
    public int[,,] Fingerprint(byte[] rgba, int w, int h)
    {
        var fp = new int[2, FingerprintBands, 3];
        for (int side = 0; side < 2; side++)
        {
            int x0 = side == 0 ? 0 : w - FingerprintCols;
            for (int b = 0; b < FingerprintBands; b++)
            {
                int y0 = b * h / FingerprintBands, y1 = (b + 1) * h / FingerprintBands;
                long r = 0, g = 0, bl = 0; long n = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x0 + FingerprintCols; x++)
                    {
                        int i = (y * w + x) * 4;
                        r += rgba[i]; g += rgba[i + 1]; bl += rgba[i + 2]; n++;
                    }
                fp[side, b, 0] = (int)Math.Round((double)r / n, MidpointRounding.ToEven);
                fp[side, b, 1] = (int)Math.Round((double)g / n, MidpointRounding.ToEven);
                fp[side, b, 2] = (int)Math.Round((double)bl / n, MidpointRounding.ToEven);
            }
        }
        return fp;
    }

    /// <summary>Whether the page is the one the entry's sides were made for: its size, and its edges within the tolerance.</summary>
    public bool Matches(Page page, byte[] rgba, int w, int h)
    {
        if (w != InnerWidth || h != InnerHeight || h != Height) return false;
        var got = Fingerprint(rgba, w, h);
        for (int s = 0; s < 2; s++)
            for (int b = 0; b < FingerprintBands; b++)
                for (int c = 0; c < 3; c++)
                    if (Math.Abs(got[s, b, c] - page.Edge[s, b, c]) > Tolerance) return false;
        return true;
    }

    /// <summary>The layers from the 4:3 page out to <paramref name="layer"/>, inner first.</summary>
    public List<Layer> Chain(Layer layer)
    {
        var chain = new List<Layer>();
        for (Layer? l = layer; l is not null; l = Layers.FirstOrDefault(x => x.Name == l.Over)) chain.Insert(0, l);
        return chain;
    }

    /// <summary>
    /// The wide page (RGBA8, rows from the top) of a record whose 4:3 page is <paramref name="rgba"/>: the page in the middle, each
    /// layer's sides beside it, inner first. <paramref name="strip"/> reads a strip file as RGBA8 (null when missing). Null when the
    /// record has no entry, the page is not the one the sides were made for, or a layer's strip is missing or of the wrong size.
    /// The added width on each side (the affine's offset) is <c>(width - w) / 2</c>.
    /// </summary>
    public (byte[] Rgba, int Width, int Height)? Compose(int key, byte[] rgba, int w, int h, Layer layer, Func<string, (byte[] Rgba, int W, int H)?> strip)
    {
        if (Get(key) is not { } page || !Matches(page, rgba, w, h)) return null;
        byte[] cur = rgba; int cw = w;
        foreach (var l in Chain(layer))
        {
            int side = l.Side, nw = cw + 2 * side;
            if (nw != l.Width) return null;
            var next = new byte[nw * h * 4];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(cur, y * cw * 4, next, (y * nw + side) * 4, cw * 4);
            if (page.Pad) Pad(next, nw, h, side, cw);
            else
            {
                if (!page.Files.TryGetValue(l.Name, out var file) || strip(file) is not { } s || s.W != 2 * side || s.H != h) return null;
                for (int y = 0; y < h; y++)
                {
                    Buffer.BlockCopy(s.Rgba, y * s.W * 4, next, y * nw * 4, side * 4);
                    Buffer.BlockCopy(s.Rgba, (y * s.W + side) * 4, next, (y * nw + side + cw) * 4, side * 4);
                }
            }
            cur = next; cw = nw;
        }
        return (cur, cw, h);
    }

    /// <summary>A pad page's sides: each row the mean of the inner picture's outer PadCols columns, repeated out (as the tool's pad_sides).</summary>
    private void Pad(byte[] px, int w, int h, int side, int cw)
    {
        int k = Math.Min(PadCols, cw);
        for (int y = 0; y < h; y++)
            for (int s = 0; s < 2; s++)
            {
                int x0 = s == 0 ? side : side + cw - k;
                Span<int> sum = stackalloc int[4];
                for (int x = x0; x < x0 + k; x++) for (int c = 0; c < 4; c++) sum[c] += px[(y * w + x) * 4 + c];
                Span<byte> v = stackalloc byte[4];
                for (int c = 0; c < 4; c++) v[c] = (byte)Math.Round((double)sum[c] / k, MidpointRounding.ToEven);
                int from = s == 0 ? 0 : side + cw;
                for (int x = from; x < from + side; x++) v.CopyTo(px.AsSpan((y * w + x) * 4, 4));
            }
    }
}
