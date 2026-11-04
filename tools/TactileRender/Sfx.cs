using System.Globalization;
using System.Text;
using I76.Formats.Audio;
using I76.Formats.Vfs;

/// <summary>
/// `--sfx INSTALL OUTDIR [name[@rate]]...`: where the game's own sounds have their low-frequency energy, for the shaker's band
/// allocation (bands.md). Each sound is decoded from the install and played at its rate (an engine loop at its idle and
/// rev rates: EngineSoundPitch, 8268.75 Hz at 1050 rpm, 14867 at 5000), and its power read in third-octave bands from
/// 10 to 200 Hz (Welch, 1 s Hann segments). Writes sfx_bands.csv: each sound's band levels in dB under its loudest band
/// below 200 Hz, and that band's level against the sound's whole power. Reads only; writes no game data.
/// </summary>
static class Sfx
{
    public static readonly double[] Centres = [10, 12.5, 16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200];

    /// <summary>The default set: engine loops (idle and 5000 rpm), the guns, a rocket, explosions, a crash, a landing, a skid.</summary>
    public static readonly string[] DefaultSet =
    [
        "vvcre2@idle", "vvcre2@rev", "vvch2@idle", "vvch2@rev", "vvbo1@idle", "vvbo1@rev", "vnvcs1@idle", "vnvcs1@rev",
        "whmgun", "wlmgun", "wmcan", "whcan", "wrock", "wmiss", "whowitz", "vexplode", "xcar", "xbuild", "vtcoll", "vland", "tskid1",
    ];

    public static void Run(string installDir, string dir, IEnumerable<string> names)
    {
        Directory.CreateDirectory(dir);
        var vfs = new AssetVfs(new GameInstall(installDir));
        var inv = CultureInfo.InvariantCulture;
        var csv = new StringBuilder("sound,play_rate_hz,below_200_db," + string.Join(",", Centres.Select(c => c.ToString(inv))) + "\n");
        foreach (var arg in names)
        {
            var parts = arg.Split('@');
            string name = parts[0] + ".wav";
            var pcm = SoundFiles.Read(vfs, name);
            if (pcm is null) { Console.WriteLine($"{name}: not found"); continue; }
            double rate = parts.Length < 2 ? pcm.Rate : parts[1] switch
            {
                "idle" => 8268.75,
                "rev" => 8268.75 * (1 + (5000 - 1050) / 4950.0),
                var r => double.Parse(r, inv),
            };
            var x = Mono(pcm);
            var (bands, below) = Bands(x, rate);
            double top = bands.Max();
            csv.Append($"{arg},{rate.ToString("0", inv)},{(10 * Math.Log10(below)).ToString("0.0", inv)},"
                + string.Join(",", bands.Select(b => (10 * Math.Log10(b / top)).ToString("0.0", inv))) + "\n");
        }
        File.WriteAllText(Path.Combine(dir, "sfx_bands.csv"), csv.ToString());
        Console.Write(csv.ToString());
        Allocation(dir);
    }

    /// <summary>
    /// lfe_bands.csv: each shaker source's band from the synth's own constants (a tone's frequency, a noise band's -3 dB edges,
    /// a range over rpm or distance), whether it is continuous or an event, and its plan level (docs/lfe-bands.md).
    /// </summary>
    public static void Allocation(string dir)
    {
        var inv = CultureInfo.InvariantCulture;
        var k = I76.Formats.Input.TactileTuning.Default;
        var p = I76.Formats.Input.TactileSynth.Profiles;
        var rows = new List<(string Group, string Name, double Lo, double Hi, string Kind)>();
        (double, double) Band(double hz, double q) { double bw = hz / q; return (Math.Sqrt(bw * bw / 4 + hz * hz) - bw / 2, Math.Sqrt(bw * bw / 4 + hz * hz) + bw / 2); }
        foreach (var (name, i) in new[] { ("sand", 2), ("rough", 6), ("dirt", 1), ("planks", 5), ("asphalt", 3), ("dirt road", 4) })
        {
            var (lo, hi) = Band(p[i].BedHz, p[i].BedQ);
            rows.Add(("road", $"{name} bed", lo, hi, "continuous"));
            if (p[i].LumpLevel > 0 && !p[i].Paired) rows.Add(("road", $"{name} lumps", p[i].LumpHz * 0.9, p[i].LumpHz * 1.1, "events"));
            if (p[i].GrainLevel > 0) rows.Add(("road", $"{name} grit", p[i].GrainHz * 0.88, p[i].GrainHz * 1.12, "events"));
        }
        rows.Add(("engine", "engine (idle to 5000 rpm)", I76.Formats.Input.TactileSynth.EngineHz(600), I76.Formats.Input.TactileSynth.EngineHz(5000), "continuous"));
        rows.Add(("knock", "wheel knock body", k.KnockHz, k.KnockHz, "events"));
        rows.Add(("knock", "wheel knock click", k.KnockClickHz, k.KnockClickHz, "events"));
        foreach (var (name, ch) in new[] { ("explosive hit", 1), ("ballistic hit", 0), ("impact hit", 3) })
        {
            double hz = I76.Formats.Input.TactileSynth.HitThump(ch, 50)!.Value.Hz;
            rows.Add(("hit", name, hz, hz, "events"));
        }
        rows.Add(("hit", "cabin ring (impact over 20)", k.RingHz, k.RingHz, "events"));
        rows.Add(("explosion", "explosion (near to 2 km)", I76.Formats.Input.TactileSynth.BlastFarHz, I76.Formats.Input.TactileSynth.BlastHz, "events"));
        foreach (var (name, cls) in new[] { ("mortar", 3u), ("cannon", 2u), ("rocket", 4u), ("dropper", 6u), ("machine gun", 1u), ("flame", 5u) })
        {
            double hz = I76.Formats.Input.TactileSynth.ShotThump(cls)!.Value.Hz;
            rows.Add(("shot", $"{name} shot", hz, hz, "events"));
        }
        var csv = new StringBuilder("group,name,lo_hz,hi_hz,kind\n");
        foreach (var r in rows) csv.Append($"{r.Group},{r.Name},{r.Lo.ToString("0.0", inv)},{r.Hi.ToString("0.0", inv)},{r.Kind}\n");
        File.WriteAllText(Path.Combine(dir, "lfe_bands.csv"), csv.ToString());
    }

    static double[] Mono(Pcm p)
    {
        int bytes = p.Bits / 8, frames = p.Data.Length / (bytes * p.Channels);
        var x = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            double a = 0;
            for (int c = 0; c < p.Channels; c++)
            {
                int o = (i * p.Channels + c) * bytes;
                a += bytes == 1 ? (p.Data[o] - 128) / 128.0 : BitConverter.ToInt16(p.Data, o) / 32768.0;
            }
            x[i] = a / p.Channels;
        }
        return x;
    }

    /// <summary>The power in each third-octave band (a Welch spectrum at 1 Hz bins below 220 Hz) and the share of the whole power below 200 Hz.</summary>
    static (double[] Bands, double Below) Bands(double[] x, double rate)
    {
        int n = (int)Math.Round(rate);              // 1 s segments: 1 Hz bins
        if (x.Length < n) { var y = new double[n]; Array.Copy(x, y, x.Length); x = y; }   // a short sound padded to a second
        var win = Enumerable.Range(0, n).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n)).ToArray();
        var p = new double[221];
        double total = 0;
        int segs = 0;
        for (int st = 0; st + n <= x.Length; st += n / 2, segs++)
        {
            for (int i = 0; i < n; i++) total += x[st + i] * x[st + i] * win[i] * win[i];
            for (int b = 5; b <= 220; b++)
            {
                double re = 0, im = 0, w = 2 * Math.PI * b / n;
                for (int i = 0; i < n; i++) { double v = x[st + i] * win[i]; re += v * Math.Cos(w * i); im -= v * Math.Sin(w * i); }
                p[b] += 2 * (re * re + im * im) / n;
            }
        }
        var bands = new double[Centres.Length];
        double below = 0;
        for (int k = 0; k < Centres.Length; k++)
            for (int b = 5; b <= 220; b++)
                if (b >= Centres[k] / Math.Pow(2, 1 / 6.0) && b < Centres[k] * Math.Pow(2, 1 / 6.0)) bands[k] += p[b] + 1e-30;
        for (int b = 5; b < 200; b++) below += p[b];
        return (bands, below / Math.Max(total, 1e-30));
    }
}
