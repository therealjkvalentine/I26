using System.Globalization;
using System.Text;
using I76.Formats.Input;

/// <summary>
/// `--measure OUTDIR`: signals to measure a bass shaker's response with, so its tactile_eq can be set from the readings
/// (tools/TactileRender/eq_from_measurement.py): a log sine sweep 10 to 200 Hz over 60 s and stepped third-octave tones, at a
/// constant -6 dBFS, flat (no EQ, no crossover, no limiter), and the same two through the game's own chain at its defaults
/// (no tactile_eq, the 15 Hz high-pass, the 90 Hz crossover, the limiter), plus 5.1 copies with the signal on the LFE channel.
/// </summary>
static class Measure
{
    const int Rate = 48000;
    public const double LevelDb = -6;
    static readonly double Amp = Math.Pow(10, LevelDb / 20);
    public static readonly double[] Centres = [10, 12.5, 16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200];
    const double SweepSeconds = 60, Pad = 2, Tone = 5, Gap = 1, Ramp = 0.02;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The log sweep, 2 s of silence each side, 20 ms raised-cosine ends; f(t) = 10·20^(t/60).</summary>
    static float[] Sweep()
    {
        var x = new float[(int)((SweepSeconds + 2 * Pad) * Rate)];
        double k = Math.Log(200 / 10.0), ph = 0;
        int n0 = (int)(Pad * Rate), n = (int)(SweepSeconds * Rate);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate, f = 10 * Math.Exp(k * t / SweepSeconds);
            ph += 2 * Math.PI * f / Rate;
            x[n0 + i] = (float)(Amp * Env(t, SweepSeconds) * Math.Sin(ph));
        }
        return x;
    }

    /// <summary>The stepped tones: 1 s of silence, then each centre for 5 s with 1 s after it; the start times.</summary>
    static (float[] X, List<(double Start, double Hz)> Starts) Steps()
    {
        var starts = new List<(double, double)>();
        double total = Gap + Centres.Length * (Tone + Gap);
        var x = new float[(int)(total * Rate)];
        double at = Gap;
        foreach (var hz in Centres)
        {
            starts.Add((at, hz));
            int n0 = (int)Math.Round(at * Rate), n = (int)(Tone * Rate);
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                x[n0 + i] = (float)(Amp * Env(t, Tone) * Math.Sin(2 * Math.PI * hz * t));
            }
            at += Tone + Gap;
        }
        return (x, starts);
    }

    static double Env(double t, double len) =>
        t < Ramp ? 0.5 - 0.5 * Math.Cos(Math.PI * t / Ramp) : t > len - Ramp ? 0.5 - 0.5 * Math.Cos(Math.PI * (len - t) / Ramp) : 1;

    /// <summary>A signal through the game's chain at its defaults: the synth with nothing but this input (Gain 1, the LFE Level's gain left out so -6 dBFS stays -6 into the limiter).</summary>
    static float[] Chain(float[] x)
    {
        int i = 0;
        var s = new TactileSynth { Gain = 1f, TestInput = () => i < x.Length ? x[i++] : 0f };
        var y = new float[x.Length];
        s.Fill(y, Rate);
        return y;
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var sweep = Sweep();
        var (steps, starts) = Steps();
        Wav(Path.Combine(dir, "sweep_flat.wav"), sweep, 1);
        Wav(Path.Combine(dir, "steps_flat.wav"), steps, 1);
        Wav(Path.Combine(dir, "sweep_chain.wav"), Chain(sweep), 1);
        var chainSteps = Chain(steps);
        Wav(Path.Combine(dir, "steps_chain.wav"), chainSteps, 1);
        Wav(Path.Combine(dir, "sweep_flat_51.wav"), sweep, 6);
        Wav(Path.Combine(dir, "steps_flat_51.wav"), steps, 6);

        var times = new StringBuilder("# steps_flat.wav and steps_chain.wav: each tone's start and end (s) and frequency (Hz); measure the middle 3 s\nstart_s,end_s,hz,chain_level_dbfs\n");
        foreach (var (start, hz) in starts)
        {
            int a = (int)((start + 1) * Rate), b = (int)((start + 4) * Rate);
            double pk = 0;
            for (int i = a; i < b; i++) pk = Math.Max(pk, Math.Abs(chainSteps[i]));
            times.Append(string.Create(Inv, $"{start:0.000},{start + Tone:0.000},{hz},{20 * Math.Log10(pk):0.00}\n"));
        }
        File.WriteAllText(Path.Combine(dir, "steps_times.csv"), times.ToString());
        Console.WriteLine($"measurement signals in {Path.GetFullPath(dir)}");
    }

    /// <summary>16-bit PCM; with 6 channels a WAVE_FORMAT_EXTENSIBLE 5.1 file (FL FR FC LFE BL BR) with the signal on the LFE alone.</summary>
    static void Wav(string path, float[] x, int channels)
    {
        using var w = new BinaryWriter(File.Create(path));
        int block = 2 * channels, bytes = x.Length * block;
        bool ext = channels > 2;
        int fmt = ext ? 40 : 16;
        w.Write("RIFF"u8); w.Write(4 + 8 + fmt + 8 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(fmt);
        w.Write((short)(ext ? 0xFFFE : 1)); w.Write((short)channels); w.Write(Rate); w.Write(Rate * block); w.Write((short)block); w.Write((short)16);
        if (ext)
        {
            w.Write((short)22); w.Write((short)16); w.Write(0x3F);
            // KSDATAFORMAT_SUBTYPE_PCM
            w.Write([0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71]);
        }
        w.Write("data"u8); w.Write(bytes);
        foreach (var s in x)
        {
            short v = (short)Math.Clamp(Math.Round(s * 32767.0), -32768, 32767);
            for (int c = 0; c < channels; c++) w.Write(channels == 1 || c == 3 ? v : (short)0);
        }
    }
}
