using System.Text.Json.Serialization;

namespace I76.Formats.Audio;

/// <summary>One cut of a source clip in a spoken line's recipe (docs/remix-missions.md "Spoken lines").</summary>
public sealed class SplicePiece
{
    [JsonPropertyName("game")] public string Game { get; set; } = "i76";
    [JsonPropertyName("clip")] public string Clip { get; set; } = "";
    /// <summary>The cut, in samples of the source clip (11025 Hz).</summary>
    [JsonPropertyName("start")] public int Start { get; set; }
    [JsonPropertyName("end")] public int End { get; set; }
    /// <summary>The output sample the piece starts at.</summary>
    [JsonPropertyName("at")] public int At { get; set; }
    [JsonPropertyName("gain_db")] public double GainDb { get; set; }
    [JsonPropertyName("fade_in")] public int FadeIn { get; set; }
    [JsonPropertyName("fade_out")] public int FadeOut { get; set; }
    /// <summary>An RBJ high shelf at 1 kHz (slope 0.5), dB; 0 none.</summary>
    [JsonPropertyName("tilt_db")] public double TiltDb { get; set; }
    /// <summary>Output over input duration (WSOLA, the pitch kept); 0 none.</summary>
    [JsonPropertyName("stretch")] public double Stretch { get; set; }
    /// <summary>A Nitro Pack take filtered toward the 1997 session's spectrum.</summary>
    [JsonPropertyName("match")] public SpliceMatch? Match { get; set; }
    /// <summary>The fades before the match filter (the order a line was first heard in), not after the stretch.</summary>
    [JsonPropertyName("fade_before_match")] public bool FadeBeforeMatch { get; set; }
}

public sealed class SpliceMatch
{
    /// <summary>(Hz, dB) points, linear in log frequency between them.</summary>
    [JsonPropertyName("eq")] public List<double[]> Eq { get; set; } = [];
    [JsonPropertyName("gain_db")] public double GainDb { get; set; }
    [JsonPropertyName("noise_db")] public double NoiseDb { get; set; }
    [JsonPropertyName("seed")] public long Seed { get; set; }
}

/// <summary>The CB treatment over a whole line.</summary>
public sealed class SpliceRadio
{
    [JsonPropertyName("band")] public double[] Band { get; set; } = [320, 3200];
    [JsonPropertyName("comp")] public SpliceComp Comp { get; set; } = new();
    [JsonPropertyName("pre_level")] public double PreLevel { get; set; }
    [JsonPropertyName("drive")] public double Drive { get; set; }
    [JsonPropertyName("post_level")] public double PostLevel { get; set; }
    [JsonPropertyName("bed")] public SpliceBed Bed { get; set; } = new();
    [JsonPropertyName("hiss_db")] public double HissDb { get; set; }
    /// <summary>The key-up and key-down squelch bursts: game, clip, start, end, level.</summary>
    [JsonPropertyName("head")] public System.Text.Json.JsonElement[] Head { get; set; } = [];
    [JsonPropertyName("tail")] public System.Text.Json.JsonElement[] Tail { get; set; } = [];
    [JsonPropertyName("pad_samples")] public int Pad { get; set; }

    public static (string Game, string Clip, int Start, int End, double Level)? Cut(System.Text.Json.JsonElement[] e) =>
        e.Length < 5 ? null : (e[0].GetString()!, e[1].GetString()!, e[2].GetInt32(), e[3].GetInt32(), e[4].GetDouble());
}

public sealed class SpliceComp
{
    [JsonPropertyName("threshold_db")] public double ThresholdDb { get; set; }
    [JsonPropertyName("ratio")] public double Ratio { get; set; } = 1;
    [JsonPropertyName("attack_ms")] public double AttackMs { get; set; }
    [JsonPropertyName("release_ms")] public double ReleaseMs { get; set; }
}

public sealed class SpliceBed
{
    /// <summary>Cuts of CB clips' gaps: game, clip, start, end.</summary>
    [JsonPropertyName("sources")] public List<System.Text.Json.JsonElement[]> Sources { get; set; } = [];
    [JsonPropertyName("level")] public double Level { get; set; }
}

/// <summary>
/// Renders a spoken line from its recipe and the player's own clips (docs/remix-missions.md "Spoken lines"): each piece cut,
/// matched, tilted, stretched, faded and levelled, laid at its place, then the CB treatment over the whole (band limit, an RMS
/// compressor, tanh saturation, the static bed of CB clip gaps looped, seeded hiss, and the squelch bursts). The same steps
/// as the splice work's reference renderer, held to it by a test (its noise is a seeded stream of our own, so only the hiss
/// and the EQ match's noise floor differ). Presentation only: nothing the simulation sees depends on it.
/// </summary>
public static class SpliceRender
{
    public const int Rate = 11025;

    /// <summary>A clip's samples (-1..1), by game ("i76", "nitro") and stem, or null when this machine has none.</summary>
    public delegate float[]? Clips(string game, string clip);

    /// <summary>The line without the radio: the pieces at their places, times <paramref name="gain"/>.</summary>
    public static double[]? Plain(IReadOnlyList<SplicePiece> pieces, int total, double gain, Clips clips, int? silenceFrom = null)
    {
        var o = new double[total];
        foreach (var p in pieces)
        {
            if (clips(p.Game, p.Clip) is not { } x || p.Start < 0 || p.End > x.Length || p.End <= p.Start) return null;
            var seg = Piece(x, p);
            int a = p.At, b = Math.Min(total, a + seg.Length);
            for (int i = a; i < b; i++) o[i] += seg[i - a];
        }
        // a line trimmed where it was first heard: silent from there on
        if (silenceFrom is int s) for (int i = Math.Max(0, s); i < o.Length; i++) o[i] = 0;
        for (int i = 0; i < o.Length; i++) o[i] *= gain;
        return o;
    }

    private static double[] Piece(float[] x, SplicePiece p)
    {
        var seg = new double[p.End - p.Start];
        for (int i = 0; i < seg.Length; i++) seg[i] = x[p.Start + i];
        if (p.FadeBeforeMatch) Fades(seg, p);
        if (p.Match is { } m) seg = MatchEq(seg, m);
        if (p.TiltDb != 0) seg = Filter(seg, HighShelf(p.TiltDb));
        if (p.Stretch != 0) seg = Wsola(seg, p.Stretch);
        if (!p.FadeBeforeMatch) Fades(seg, p);
        double g = Math.Pow(10, p.GainDb / 20);
        for (int i = 0; i < seg.Length; i++) seg[i] *= g;
        return seg;
    }

    private static void Fades(double[] seg, SplicePiece p)
    {
        int fi = Math.Min(p.FadeIn, seg.Length), fo = Math.Min(p.FadeOut, seg.Length);
        if (p.FadeIn > 0) { var f = Fade(p.FadeIn); for (int i = 0; i < fi; i++) seg[i] *= f[i]; }
        if (p.FadeOut > 0) { var f = Fade(p.FadeOut); for (int i = 0; i < fo; i++) seg[seg.Length - fo + i] *= f[fo - 1 - i]; }
    }

    /// <summary>sin² over n samples, 0 to 1 (numpy's linspace(0, 1, n): both ends included).</summary>
    private static double[] Fade(int n)
    {
        n = Math.Max(n, 1);
        var f = new double[n];
        for (int i = 0; i < n; i++) { double t = n == 1 ? 0 : (double)i / (n - 1), s = Math.Sin(t * Math.PI / 2); f[i] = s * s; }
        return f;
    }

    // ---------------------------------------------------------------- the EQ match (a 255-tap FIR from the recipe's points)

    private static double[] MatchEq(double[] x, SpliceMatch m)
    {
        const int taps = 255, bins = 257;
        var lhz = m.Eq.Select(p => Math.Log(Math.Max(p[0], 1.0))).ToArray();
        var db = m.Eq.Select(p => p[1]).ToArray();
        var gain = new double[bins];
        for (int k = 0; k < bins; k++)
        {
            double f = (Rate / 2.0) * k / (bins - 1);
            gain[k] = Math.Pow(10, Interp(Math.Log(Math.Max(f, 1.0)), lhz, db) / 20);
        }
        var fir = Firwin2(taps, gain);
        // numpy.convolve(x, fir, 'same'): the full convolution's centre, as long as the longer input
        int n = Math.Max(x.Length, taps), full = x.Length + taps - 1, off = (full - n) / 2;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            int k = i + off;
            double s = 0;
            int j0 = Math.Max(0, k - taps + 1), j1 = Math.Min(x.Length - 1, k);
            for (int j = j0; j <= j1; j++) s += x[j] * fir[k - j];
            y[i] = s;
        }
        double g = Math.Pow(10, m.GainDb / 20), nl = Math.Pow(10, m.NoiseDb / 20);
        var rng = new Normal(m.Seed);
        for (int i = 0; i < n; i++) y[i] = y[i] * g + (nl > Math.Pow(10, -70 / 20.0) ? rng.Next() * nl * 0.5 : 0);
        return y;
    }

    private static double Interp(double v, double[] xs, double[] ys)
    {
        if (v <= xs[0]) return ys[0];
        if (v >= xs[^1]) return ys[^1];
        int i = 1;
        while (xs[i] < v) i++;
        return ys[i - 1] + (ys[i] - ys[i - 1]) * (v - xs[i - 1]) / (xs[i] - xs[i - 1]);
    }

    /// <summary>scipy's firwin2 on a 257-point grid already the gains': the inverse real FFT, shifted, Hamming-windowed.</summary>
    private static double[] Firwin2(int taps, double[] gain)
    {
        int nfreq = gain.Length, nfft = 2 * (nfreq - 1);
        double shift = -(taps - 1) / 2.0;
        var h = new double[taps];
        for (int t = 0; t < taps; t++)
        {
            double s = 0;
            for (int k = 0; k < nfreq; k++)
            {
                double w = Math.PI * k / (nfreq - 1);   // x = k/(nfreq-1) times pi
                double re = gain[k] * Math.Cos(shift * w), im = gain[k] * Math.Sin(shift * w);
                double ang = 2 * Math.PI * k * t / nfft;
                double c = re * Math.Cos(ang) - im * Math.Sin(ang);
                s += k == 0 || k == nfreq - 1 ? c : 2 * c;
            }
            h[t] = s / nfft * (0.54 - 0.46 * Math.Cos(2 * Math.PI * t / (taps - 1)));
        }
        return h;
    }

    // ---------------------------------------------------------------- filters

    private record struct Biquad(double B0, double B1, double B2, double A1, double A2);

    private static Biquad HighShelf(double gainDb, double f0 = 1000, double slope = 0.5)
    {
        double a = Math.Pow(10, gainDb / 40), w0 = 2 * Math.PI * f0 / Rate;
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / slope - 1) + 2), c = Math.Cos(w0), sa = 2 * Math.Sqrt(a) * alpha;
        double b0 = a * ((a + 1) + (a - 1) * c + sa), b1 = -2 * a * ((a - 1) + (a + 1) * c), b2 = a * ((a + 1) + (a - 1) * c - sa);
        double a0 = (a + 1) - (a - 1) * c + sa, a1 = 2 * ((a - 1) - (a + 1) * c), a2 = (a + 1) - (a - 1) * c - sa;
        return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>A 4th-order Butterworth high- or low-pass as two biquads (the bilinear transform, prewarped at the corner).</summary>
    private static Biquad[] Butter4(double f0, bool high)
    {
        var o = new Biquad[2];
        double w0 = 2 * Math.PI * f0 / Rate, c = Math.Cos(w0), s = Math.Sin(w0);
        for (int k = 0; k < 2; k++)
        {
            double q = 1 / (2 * Math.Cos(Math.PI * (2 * k + 1) / 8)), alpha = s / (2 * q), a0 = 1 + alpha;
            double b0 = high ? (1 + c) / 2 : (1 - c) / 2, b1 = high ? -(1 + c) : 1 - c;
            o[k] = new Biquad(b0 / a0, b1 / a0, b0 / a0, -2 * c / a0, (1 - alpha) / a0);
        }
        return o;
    }

    private static double[] Filter(double[] x, params Biquad[] chain)
    {
        var y = (double[])x.Clone();
        foreach (var q in chain)
        {
            double z1 = 0, z2 = 0;
            for (int i = 0; i < y.Length; i++)
            {
                double v = y[i], o = q.B0 * v + z1;
                z1 = q.B1 * v - q.A1 * o + z2;
                z2 = q.B2 * v - q.A2 * o;
                y[i] = o;
            }
        }
        return y;
    }

    // ---------------------------------------------------------------- WSOLA

    /// <summary>
    /// Waveform-similarity overlap-add: output duration = ratio x input, the pitch kept. Hann frames of 30 ms at 50 % overlap; each
    /// analysis frame is the one within 10 ms of its nominal place that best continues the previous frame.
    /// </summary>
    public static double[] Wsola(double[] x, double ratio)
    {
        if (Math.Abs(ratio - 1.0) < 1e-6) return (double[])x.Clone();
        int N = (int)(0.030 * Rate) / 2 * 2, Hs = N / 2, T = (int)(0.010 * Rate);
        double Ha = Hs / ratio;
        var w = new double[N];
        for (int i = 0; i < N; i++) w[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        int L = (int)Math.Round(x.Length * ratio, MidpointRounding.ToEven);
        int nf = (int)Math.Ceiling((double)L / Hs) + 1;
        var xp = new double[N + x.Length + (int)(nf * Ha) + 2 * N + 2 * T];
        Array.Copy(x, 0, xp, N, x.Length);
        int off = N;
        var y = new double[nf * Hs + N];
        var ws = new double[y.Length];
        int prev = -1;
        for (int m = 0; m < nf; m++)
        {
            int ideal = off + (int)Math.Round((m * Hs - Hs) / ratio, MidpointRounding.ToEven);
            int pos = ideal;
            if (prev >= 0)
            {
                int lo = Math.Max(0, ideal - T), hi = Math.Min(xp.Length - N, ideal + T);
                double rr = 0;
                for (int j = 0; j < N; j++) rr += xp[prev + Hs + j] * xp[prev + Hs + j];
                if (rr > 1e-12)
                {
                    double best = double.NegativeInfinity;
                    for (int k = 0; lo + k + N <= Math.Min(xp.Length, hi + N); k++)
                    {
                        double c = 0;
                        for (int j = 0; j < N; j++) c += xp[lo + k + j] * xp[prev + Hs + j] * w[j];
                        if (c > best) { best = c; pos = lo + k; }
                    }
                }
            }
            for (int j = 0; j < N; j++) { y[m * Hs + j] += w[j] * xp[pos + j]; ws[m * Hs + j] += w[j]; }
            prev = pos;
        }
        var o = new double[L];
        for (int i = 0; i < L; i++) o[i] = y[Hs + i] / Math.Max(ws[Hs + i], 1e-3);
        return o;
    }

    // ---------------------------------------------------------------- the radio

    /// <summary>
    /// The CB treatment. Without the clips of the squelch bursts (no Nitro Pack) they are left out; a bed cut whose clip is
    /// missing is left out of the loop (the base game's own cut alone).
    /// </summary>
    public static double[] Radio(double[] x, SpliceRadio r, long seed, Clips clips)
    {
        var band = Butter4(r.Band[0], true).Concat(Butter4(r.Band[1], false)).ToArray();
        var y = Filter(x, band);
        var cmp = r.Comp;
        double aAt = Math.Exp(-1 / (cmp.AttackMs / 1000 * Rate)), aRel = Math.Exp(-1 / (cmp.ReleaseMs / 1000 * Rate)), e = 0;
        for (int i = 0; i < y.Length; i++)
        {
            double sq = y[i] * y[i], k = sq > e ? aAt : aRel;
            e = k * e + (1 - k) * sq;
            double lvl = 10 * Math.Log10(e + 1e-10);
            if (lvl > cmp.ThresholdDb) y[i] *= Math.Pow(10, (cmp.ThresholdDb - lvl) * (1 - 1 / cmp.Ratio) / 20);
        }
        double peak = 0;
        foreach (var v in y) peak = Math.Max(peak, Math.Abs(v));
        double th = Math.Tanh(r.Drive);
        for (int i = 0; i < y.Length; i++) y[i] = Math.Tanh(r.Drive * (y[i] / (peak + 1e-9) * r.PreLevel)) / th * r.PostLevel;
        var bed = new List<double>();
        foreach (var s in r.Bed.Sources)
        {
            if (clips(s[0].GetString()!, s[1].GetString()!) is not { } c) continue;
            int a = s[2].GetInt32(), b = Math.Min(c.Length, s[3].GetInt32()), m = b - a;
            for (int i = 0; i < m; i++)
            {
                double hann = m == 1 ? 1 : 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (m - 1));
                bed.Add(c[a + i] * Math.Pow(hann, 0.1));
            }
        }
        if (bed.Count > 0) for (int i = 0; i < y.Length; i++) y[i] += bed[i % bed.Count] * r.Bed.Level;
        var rng = new Normal(seed);
        var hiss = new double[y.Length];
        for (int i = 0; i < hiss.Length; i++) hiss[i] = rng.Next();
        hiss = Filter(hiss, band);
        double hl = Math.Pow(10, r.HissDb / 20);
        for (int i = 0; i < y.Length; i++) y[i] += hiss[i] * hl;
        var o = new List<double>(y.Length + 8000);
        if (Burst(r.Head) is { } head) { o.AddRange(head); o.AddRange(new double[r.Pad]); }
        o.AddRange(y);
        if (Burst(r.Tail) is { } tail) { o.AddRange(new double[r.Pad]); o.AddRange(tail); }
        return [.. o.Select(v => Math.Clamp(v, -1, 1))];

        double[]? Burst(System.Text.Json.JsonElement[] cut)
        {
            if (SpliceRadio.Cut(cut) is not { } q || clips(q.Game, q.Clip) is not { } c || q.End > c.Length) return null;
            var b = new double[q.End - q.Start];
            for (int i = 0; i < b.Length; i++) b[i] = c[q.Start + i] * q.Level;
            return b;
        }
    }

    /// <summary>A seeded standard normal stream (SplitMix64 and Box-Muller): the hiss and the match's noise floor.</summary>
    private sealed class Normal(long seed)
    {
        private ulong _s = unchecked((ulong)seed * 0x9E3779B97F4A7C15UL + 0x2545F4914F6CDD1DUL);
        private double? _spare;

        private double Uniform()
        {
            ulong z = _s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return ((z >> 11) + 0.5) / (1UL << 53);
        }

        public double Next()
        {
            if (_spare is { } s) { _spare = null; return s; }
            double u = Uniform(), v = Uniform(), r = Math.Sqrt(-2 * Math.Log(u));
            _spare = r * Math.Sin(2 * Math.PI * v);
            return r * Math.Cos(2 * Math.PI * v);
        }
    }

    /// <summary>A rendered line as 16-bit PCM bytes (little-endian), as the reference writes it.</summary>
    public static byte[] Pcm16(double[] x)
    {
        var b = new byte[x.Length * 2];
        for (int i = 0; i < x.Length; i++)
        {
            short s = (short)(Math.Clamp(x[i], -1, 1) * 32767);
            b[2 * i] = (byte)s; b[2 * i + 1] = (byte)(s >> 8);
        }
        return b;
    }

    /// <summary>A mono 16-bit WAV file of a rendered line.</summary>
    public static byte[] Wav(double[] x)
    {
        var pcm = Pcm16(x);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        return ms.ToArray();
    }
}
