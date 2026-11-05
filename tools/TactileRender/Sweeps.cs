using System.Globalization;
using System.Text;
using I76.Formats.Input;

/// <summary>
/// `--sweep OUTDIR`: measurements of the bass-shaker chain (the synth, its EQ, the crossover's low-pass, the 15 Hz high-pass,
/// the LFE gain and the limiter) from controlled inputs, as CSV files for tools/TactileRender/plot_sweeps.py, with WAVs to
/// listen to: (a) a stepped sine 10 to 200 Hz through the filters at crossovers 60/90/120 and through an EQ curve, against the
/// intended curve; (b) each surface at 5 to 45 m/s: dominant frequency, -3 dB band, centroid and RMS; (c) the crossing knock
/// at 5/15/30/45 m/s: peak and duration; (d) the limiter's input against output, and how much of each surface at full speed
/// sits in it. Everything is the synth's own code; only the inputs are made here.
/// </summary>
static class Sweeps
{
    const int Rate = 48000;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static readonly int TickSamples = (int)MathF.Round(Rate * 102f / 2048f);
    const float Mph = 2.2369363f;

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "wav"));
        Filters(dir);
        Surfaces(dir);
        Knocks(dir);
        Limiter(dir);
        Console.WriteLine($"sweeps in {Path.GetFullPath(dir)}");
    }

    // ---------- (a) the filters and the EQ ----------

    static readonly float[] Crossovers = [60f, 90f, 120f];
    /// <summary>The EQ curve the sweep checks the tones and the road bed against: [port] tactile_eq's own example.</summary>
    const string ExampleEq = "20:3.6, 35:-3.5, 60:0";

    /// <summary>A second-order Butterworth high-pass and low-pass at the corners, as the synth's sections are meant to be (bilinear warping is under 0.1 dB here).</summary>
    static double IntendedDb(double hz, double lowPass, TactileEq? eq) =>
        -10 * Math.Log10(1 + Math.Pow(TactileSynth.LowHz / hz, 4)) - 10 * Math.Log10(1 + Math.Pow(hz / lowPass, 4)) + (eq?.Db((float)hz) ?? 0);

    static void Filters(string dir)
    {
        var eq = TactileEq.Parse(ExampleEq)!;
        var csv = new StringBuilder("hz");
        foreach (var c in Crossovers) csv.Append($",measured_{c:0},intended_{c:0}");
        csv.Append(",eq_tone_measured,eq_tone_intended,probe_90\n");
        for (double hz = 10; hz <= 200.01; hz *= Math.Pow(2, 1 / 24.0))
        {
            csv.Append(F(hz));
            foreach (var c in Crossovers)
                csv.Append($",{F(ToneDb((float)hz, c, null))},{F(IntendedDb(hz, c, null))}");
            csv.Append($",{F(ToneDb((float)hz, 90f, eq))},{F(IntendedDb(hz, 90f, eq))},{F(ProbeDb((float)hz, 90f))}\n");
        }
        File.WriteAllText(Path.Combine(dir, "a_filters.csv"), csv.ToString());

        // the road bed through the EQ's filter bank: the bed's spectrum with the curve over the bed's without, per 1/3 octave
        var bank = new StringBuilder("hz,bed_ratio_db,curve_db\n");
        var flat = BedPsd(null, out var fr);
        var shaped = BedPsd(eq, out _);
        foreach (double c in new[] { 16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125 })
        {
            double a = 0, b = 0;
            for (int i = 0; i < fr.Length; i++)
                if (fr[i] >= c / Math.Pow(2, 1 / 6.0) && fr[i] < c * Math.Pow(2, 1 / 6.0)) { a += shaped[i]; b += flat[i]; }
            bank.Append($"{F(c)},{F(10 * Math.Log10(a / b))},{F(eq.Db((float)c))}\n");
        }
        File.WriteAllText(Path.Combine(dir, "a_eq_bed.csv"), bank.ToString());

        // a continuous log sweep 10 to 200 Hz over 12 s through the chain at each crossover, at -12 dBFS, to listen to
        foreach (var c in Crossovers)
        {
            var s = new TactileSynth { Gain = 1f, LowPassHz = c };
            double ph = 0; long n = 0; const double T = 12;
            double k = Math.Log(200 / 10.0);
            s.TestInput = () =>
            {
                double t = n++ / (double)Rate;
                double f = 10 * Math.Exp(k * t / T);
                ph += 2 * Math.PI * f / Rate;
                return (float)(0.25 * Math.Sin(ph));
            };
            var x = new float[(int)(T * Rate)];
            s.Fill(x, Rate);
            Wav(Path.Combine(dir, "wav", $"a_sweep_10-200Hz_crossover{c:0}.wav"), x);
        }
    }

    /// <summary>A steady tone through the synth (a thump of 1000 s: the EQ's gain at its frequency, then the filters), its gain in dB over the last half second.</summary>
    static double ToneDb(float hz, float lowPass, TactileEq? eq)
    {
        const float level = 0.01f;     // far under the limiter: 0.85·tanh(0.01/0.85) is 0.0004 dB off a straight line
        var s = new TactileSynth { Gain = 1f, LowPassHz = lowPass, Eq = eq };
        s.Add(new TactileSynth.Thump(hz, level, 1000f));
        var x = new float[2 * Rate];
        s.Fill(x, Rate);
        return 20 * Math.Log10(Amplitude(x.AsSpan(Rate), hz) / level);
    }

    /// <summary>The same through <see cref="TactileSynth.TestInput"/>: the filters alone.</summary>
    static double ProbeDb(float hz, float lowPass)
    {
        const float level = 0.01f;
        long n = 0;
        var s = new TactileSynth { Gain = 1f, LowPassHz = lowPass, TestInput = () => level * MathF.Sin(2 * MathF.PI * hz * (n++ / (float)Rate)) };
        var x = new float[2 * Rate];
        s.Fill(x, Rate);
        return 20 * Math.Log10(Amplitude(x.AsSpan(Rate), hz) / level);
    }

    static double[] BedPsd(TactileEq? eq, out double[] freqs)
    {
        // the 50 Hz band of before (Flat) is the widest: it has energy at every centre
        var s = new TactileSynth { Gain = 0.05f, Eq = eq, Surfaces = TactileSynth.Flat, Contrast = 1f };
        var x = Drive(s, 4, 60f, 12f);   // a dirt road at 60 mph
        return Psd(x[(2 * Rate)..], out freqs);
    }

    // ---------- (b) the surfaces against speed ----------

    static readonly (string Name, int Surface)[] Grounds =
        [("asphalt", 3), ("dirt road", 4), ("planks", 5), ("dirt", 1), ("sand", 2), ("rough", 6)];

    static void Surfaces(string dir)
    {
        var csv = new StringBuilder("surface,mps,dominant_hz,band_lo_hz,band_hi_hz,centroid_hz,spread_hz,rms_dbfs,peak_dbfs,bed_hz\n");
        var psd = new StringBuilder("surface,mps");
        for (int f = 10; f <= 150; f++) psd.Append($",{f}");
        psd.Append('\n');
        foreach (var (name, surface) in Grounds)
        {
            for (int mps = 5; mps <= 45; mps += 5)
            {
                var s = new TactileSynth { Gain = AudioOutputs.ShakerGain(100) };
                var x = Drive(s, surface, mps * Mph, 8f)[(2 * Rate)..];
                var p = Psd(x, out var fr);
                var m = Measure(p, fr);
                csv.Append($"{name},{mps},{F(m.Dominant)},{F(m.Lo)},{F(m.Hi)},{F(m.Centroid)},{F(m.Spread)},{F(Db(Rms(x)))},{F(Db(Peak(x)))},{F(s.BedHz)}\n");
                psd.Append($"{name},{mps}");
                for (int i = 0; i < fr.Length; i++) if (fr[i] >= 10 && fr[i] <= 150 && Math.Abs(fr[i] - Math.Round(fr[i])) < 1e-6) psd.Append($",{F(10 * Math.Log10(p[i] + 1e-20))}");
                psd.Append('\n');
            }
            // a ramp 5 to 45 m/s over 16 s, to listen to
            var r = new TactileSynth { Gain = AudioOutputs.ShakerGain(100) };
            Wav(Path.Combine(dir, "wav", $"b_{name.Replace(' ', '_')}_5-45ms_ramp.wav"), Drive(r, surface, t => (5 + 40 * t / 16f) * Mph, 16f));
        }
        File.WriteAllText(Path.Combine(dir, "b_surfaces.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(dir, "b_psd.csv"), psd.ToString());
    }

    record struct Spectrum(double Dominant, double Lo, double Hi, double Centroid, double Spread);

    /// <summary>From a power spectrum: its peak (smoothed over 5 Hz), the -3 dB band round it, the centroid and spread from 10 to 150 Hz.</summary>
    static Spectrum Measure(double[] p, double[] fr)
    {
        var sm = new double[p.Length];
        for (int i = 0; i < p.Length; i++)
        {
            double a = 0; int n = 0;
            for (int j = Math.Max(0, i - 2); j <= Math.Min(p.Length - 1, i + 2); j++) { a += p[j]; n++; }
            sm[i] = a / n;
        }
        int best = -1;
        for (int i = 0; i < p.Length; i++) if (fr[i] >= 10 && fr[i] <= 150 && (best < 0 || sm[i] > sm[best])) best = i;
        int lo = best, hi = best;
        while (lo > 0 && fr[lo - 1] >= 10 && sm[lo - 1] >= sm[best] / 2) lo--;
        while (hi < p.Length - 1 && fr[hi + 1] <= 150 && sm[hi + 1] >= sm[best] / 2) hi++;
        double w = 0, c = 0, c2 = 0;
        for (int i = 0; i < p.Length; i++) if (fr[i] >= 10 && fr[i] <= 150) { w += p[i]; c += p[i] * fr[i]; c2 += p[i] * fr[i] * fr[i]; }
        double mean = c / w;
        return new(fr[best], fr[lo], fr[hi], mean, Math.Sqrt(Math.Max(c2 / w - mean * mean, 0)));
    }

    // ---------- (c) the crossing knock ----------

    static void Knocks(string dir)
    {
        var csv = new StringBuilder("mps,peak_dbfs,expected_peak_dbfs,to_minus20db_ms,to_minus40db_ms,knock_over_asphalt_db,knock_over_sand_db,axle_peak_dbfs\n");
        var all = new List<float>();
        foreach (int mps in new[] { 5, 15, 30, 45 })
        {
            float mph = mps * Mph;
            var s = new TactileSynth { Gain = AudioOutputs.ShakerGain(100) };
            var e = new TactileEvents { Mph = mph };
            e.Crossings.Add(1f);
            s.Step(e);
            var x = new float[Rate];
            // (the knock's body is the first part; the expected peak is its start level through the limiter)
            s.Fill(x, Rate);
            float pk = Peak(x);
            int start = Array.FindIndex(x, v => MathF.Abs(v) >= pk * 0.5f);
            int Last(float frac) { int k = x.Length - 1; while (k > 0 && MathF.Abs(x[k]) < pk * frac) k--; return k; }
            float lvl = TactileSynth.KnockParts(TactileTuning.Default, 1f, mph).First().Level * AudioOutputs.ShakerGain(100);
            float expect = TactileSynth.Ceiling * MathF.Tanh(lvl / TactileSynth.Ceiling);
            // against the asphalt bed at that speed: how far the knock stands out of the road it lands on
            // (the bed's RMS times the square root of 2: a sine's peak at the bed's power)
            var bed = Drive(new TactileSynth { Gain = AudioOutputs.ShakerGain(100) }, 3, mph, 4f)[(2 * Rate)..];
            var sand = Drive(new TactileSynth { Gain = AudioOutputs.ShakerGain(100) }, 2, mph, 4f)[(2 * Rate)..];
            // an axle: both wheels of a pair in the same tick
            var ax = new TactileSynth { Gain = AudioOutputs.ShakerGain(100) };
            var ea = new TactileEvents { Mph = mph };
            ea.Crossings.Add(1f); ea.Crossings.Add(1f);
            ax.Step(ea);
            var xa = new float[Rate / 2];
            ax.Fill(xa, Rate);
            csv.Append($"{mps},{F(Db(pk))},{F(Db(expect))},{F((Last(0.1f) - start) * 1000.0 / Rate)},{F((Last(0.01f) - start) * 1000.0 / Rate)},{F(Db(pk) - Db(Rms(bed) * MathF.Sqrt(2)))},{F(Db(pk) - Db(Rms(sand) * MathF.Sqrt(2)))},{F(Db(Peak(xa)))}\n");
            all.AddRange(x);
        }
        File.WriteAllText(Path.Combine(dir, "c_knocks.csv"), csv.ToString());
        Wav(Path.Combine(dir, "wav", "c_knock_5_15_30_45ms.wav"), [.. all]);
    }

    // ---------- (d) the limiter ----------

    static void Limiter(string dir)
    {
        // a 40 Hz sine at the limiter's input (Gain 1, the filters under 0.2 dB at 40 Hz: measured, not assumed)
        double filt = ProbeDb(40f, 90f);
        var csv = new StringBuilder("input_dbfs,output_dbfs,linear_dbfs,reduction_db\n");
        for (double db = -30; db <= 12.01; db += 0.5)
        {
            float a = (float)Math.Pow(10, db / 20);
            long n = 0;
            var s = new TactileSynth { Gain = 1f, TestInput = () => a * MathF.Sin(2 * MathF.PI * 40f * (n++ / (float)Rate)) };
            var x = new float[Rate];
            s.Fill(x, Rate);
            double atIn = db + filt;
            double outDb = Db(Peak(x.AsSpan(Rate / 2)));
            csv.Append($"{F(atIn)},{F(outDb)},{F(atIn)},{F(atIn - outDb)}\n");
        }
        File.WriteAllText(Path.Combine(dir, "d_limiter.csv"), csv.ToString());

        // each surface at 45 m/s, LFE 100 % and 200 %, the springs still and busy: the share of time the limiter takes 1 dB or more
        // (the chain is linear before it: rendered at a gain of 1e-3, the limiter's input is the signal over that, times the LFE gain)
        float knee = (float)KneeOneDb();
        var occ = new StringBuilder("surface,lfe_percent,springs,input_peak_dbfs,input_rms_dbfs,share_over_1db,mean_reduction_db\n");
        foreach (var (name, surface) in Grounds)
            foreach (int pct in new[] { 100, 200 })
                foreach (bool busy in new[] { false, true })
                {
                    var s = new TactileSynth { Gain = 1e-3f };
                    var x = Drive(s, surface, 45 * Mph, 8f, busy)[(2 * Rate)..];
                    float g = AudioOutputs.ShakerGain(pct) / 1e-3f;
                    int over = 0; double red = 0, sq = 0; float pk = 0;
                    foreach (var v in x)
                    {
                        float u = MathF.Abs(v * g);
                        pk = MathF.Max(pk, u); sq += u * u;
                        if (u >= knee) over++;
                        if (u > 1e-9f) red += 20 * Math.Log10(u / (TactileSynth.Ceiling * MathF.Tanh(u / TactileSynth.Ceiling)));
                    }
                    occ.Append($"{name},{pct},{(busy ? "busy" : "still")},{F(Db(pk))},{F(Db((float)Math.Sqrt(sq / x.Length)))},{F(over / (double)x.Length)},{F(red / x.Length)}\n");
                }
        File.WriteAllText(Path.Combine(dir, "d_occupancy.csv"), occ.ToString());
        File.WriteAllText(Path.Combine(dir, "d_knee.txt"), $"1 dB reduction at an input of {F(Db(knee))} dBFS ({F(knee)}); ceiling {F(Db(TactileSynth.Ceiling))} dBFS\n");
    }

    /// <summary>The input where 0.85·tanh(x/0.85) is 1 dB under x.</summary>
    static double KneeOneDb()
    {
        double lo = 0.01, hi = 5;
        for (int i = 0; i < 60; i++)
        {
            double m = (lo + hi) / 2, y = TactileSynth.Ceiling * Math.Tanh(m / TactileSynth.Ceiling);
            if (20 * Math.Log10(m / y) < 1) lo = m; else hi = m;
        }
        return lo;
    }

    // ---------- the drive and the analysis ----------

    static float[] Drive(TactileSynth s, int surface, float mph, float seconds, bool busy = false) => Drive(s, surface, _ => mph, seconds, busy);

    /// <summary>The synth stepped as the game steps it (20 Hz ticks, a tick's samples each), the engine off; busy: the springs moving ±0.15 of their travel at random.</summary>
    static float[] Drive(TactileSynth s, int surface, Func<float, float> mph, float seconds, bool busy = false)
    {
        var rng = new Random(76);
        var outp = new float[(int)(seconds * Rate) / TickSamples * TickSamples];
        var e = new TactileEvents();
        for (int at = 0; at < outp.Length; at += TickSamples)
        {
            e.Clear();
            e.Surface = surface;
            e.Mph = mph(at / (float)Rate);
            e.TickSeconds = 102f / 2048f;
            if (busy) for (int k = 0; k < 4; k++) e.Travel[k] = (float)(rng.NextDouble() * 0.3 - 0.15);
            s.Step(e);
            s.Fill(outp.AsSpan(at, TickSamples), Rate);
        }
        return outp;
    }

    /// <summary>A Welch power spectrum, 1 Hz bins to 200 Hz: decimated to 2 kHz by a 24-sample average (-0.1 dB at 150 Hz), 1 s Hann segments, half overlapping.</summary>
    static double[] Psd(float[] x, out double[] freqs)
    {
        const int D = 24, R = Rate / D, N = R;
        var d = new double[x.Length / D];
        for (int i = 0; i < d.Length; i++) { double a = 0; for (int j = 0; j < D; j++) a += x[i * D + j]; d[i] = a / D; }
        int bins = 201;
        var p = new double[bins];
        freqs = [.. Enumerable.Range(0, bins).Select(i => (double)i)];
        var win = Enumerable.Range(0, N).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)).ToArray();
        double wsum = win.Sum(w => w * w);
        int segs = 0;
        for (int st = 0; st + N <= d.Length; st += N / 2, segs++)
            for (int b = 1; b < bins; b++)
            {
                double re = 0, im = 0, w0 = 2 * Math.PI * b / N;
                for (int i = 0; i < N; i++) { double v = d[st + i] * win[i]; re += v * Math.Cos(w0 * i); im -= v * Math.Sin(w0 * i); }
                p[b] += (re * re + im * im) / wsum;
            }
        for (int b = 0; b < bins; b++) p[b] /= Math.Max(segs, 1);
        return p;
    }

    static float Amplitude(ReadOnlySpan<float> x, float hz)
    {
        double k = 2 * Math.Cos(2 * Math.PI * hz / Rate), s1 = 0, s2 = 0;
        foreach (var v in x) (s1, s2) = (v + k * s1 - s2, s1);
        return (float)(Math.Sqrt(Math.Max(s1 * s1 + s2 * s2 - k * s1 * s2, 0)) * 2 / x.Length);
    }

    static float Peak(ReadOnlySpan<float> x) { float m = 0; foreach (var v in x) m = MathF.Max(m, MathF.Abs(v)); return m; }
    static float Rms(ReadOnlySpan<float> x) { double a = 0; foreach (var v in x) a += v * v; return (float)Math.Sqrt(a / Math.Max(x.Length, 1)); }
    static double Db(float a) => a > 0 ? 20 * Math.Log10(a) : -200;
    static string F(double v) => v.ToString("0.###", Inv);

    static void Wav(string path, float[] x)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = x.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var s in x) w.Write((short)Math.Clamp(MathF.Round(s * 32767f), -32768, 32767));
    }
}
