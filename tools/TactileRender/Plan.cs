using System.Globalization;
using System.Text;
using I76.Formats.Input;

/// <summary>
/// `--plan OUTDIR`: where each source peaks at the limiter's input at LFE 100 %, against the headroom plan
/// (docs/lfe-surfaces.md): the hardest impact at 0 dBFS, a typical hit and an axle's knock at speed about -6, a wheel's knock,
/// the shots and the engine about -12, every road under -6. The chain is linear before the limiter, so each event is rendered
/// at a gain of 1e-3 and read back at the LFE gain. Writes plan.csv.
/// </summary>
static class Plan
{
    const int Rate = 48000;
    const float Mph = 2.2369363f;
    static readonly int TickSamples = (int)MathF.Round(Rate * 102f / 2048f);

    public record Row(string Group, string Name, double PeakDb, double RmsDb, double PlanDb);

    public static List<Row> Measure(TactileTuning tune, TactileSynth.SurfaceProfile[]? surfaces = null)
    {
        surfaces ??= TactileSynth.Profiles;
        var rows = new List<Row>();
        float g = AudioOutputs.ShakerGain(100) / 1e-3f;
        TactileSynth New() => new(76) { Gain = 1e-3f, Tune = tune, Surfaces = surfaces };
        // the developer's options (lfe_cracks, lfe_strip) at their recommended 100 %
        TactileSynth WithOptions() { var s = New(); var w = TactileSources.Defaults(); foreach (var o in TactileSources.DevOnly) w[(int)o] = 100; s.SetWeights(w); return s; }
        (double, double) Event(Action<TactileEvents> set, float seconds = 3f)
        {
            var s = New();
            var e = new TactileEvents();
            set(e);
            s.Step(e);
            var x = new float[(int)(seconds * Rate)];
            s.Fill(x, Rate);
            return Stats(x, g);
        }
        // impacts and hits (representative amounts: a machine-gun round, a cannon round, a rocket, a blast's hit, a ram, a crash)
        foreach (var (name, ch, amount) in new[] {
            ("MG round (ballistic 8)", 0, 8), ("cannon round (ballistic 30)", 0, 30), ("rocket (explosive 60)", 1, 60),
            ("near blast's hit (explosive 150)", 1, 150), ("sign (impact 15)", 3, 15), ("hard landing (impact 80)", 3, 80),
            ("ram 10 m/s (impact 120)", 3, 120), ("building at 15 m/s (impact 300)", 3, 300), ("head-on ram 25 m/s (impact 783)", 3, 783) })
        {
            var (pk, rms) = Event(e => e.Hits.Add((ch, amount)));
            rows.Add(new("impact", name, pk, rms, amount >= 150 ? 0 : amount >= 60 ? -6 : -12));
        }
        foreach (var (name, d) in new[] { ("blast at the car", 5f), ("blast at 60 m", 60f), ("blast at 500 m", 500f) })
        {
            var (pk, rms) = Event(e => e.Blasts.Add(d));
            rows.Add(new("explosion", name, pk, rms, d < 15 ? 0 : -12));
        }
        foreach (var (name, cls) in new[] { ("machine gun", 1u), ("cannon", 2u), ("mortar", 3u), ("rocket", 4u), ("flame", 5u), ("dropper", 6u) })
        {
            var (pk, rms) = Event(e => e.Shots.Add(new WeaponShot(0, cls, 1, 1)));
            rows.Add(new("shot", name, pk, rms, -12));
        }
        foreach (int mps in new[] { 15, 30 })
        {
            var (pk, rms) = Event(e => { e.Mph = mps * Mph; e.Crossings.Add(1f); });
            rows.Add(new("knock", $"one wheel, {mps} m/s", pk, rms, -12));
            var (pa, ra) = Event(e => { e.Mph = mps * Mph; e.Crossings.Add(1f); e.Crossings.Add(1f); });
            rows.Add(new("knock", $"an axle, {mps} m/s", pa, ra, -6));
        }
        {
            var s = New();
            var x = Drive(s, 0, 0, 2f, rpm: 5000);
            var (pk, rms) = Stats(x[Rate..], g);
            rows.Add(new("engine", "engine at 5000 rpm", pk, rms, -12));
        }
        foreach (var (name, surface) in new[] { ("asphalt", 3), ("dirt road", 4), ("planks", 5), ("dirt", 1), ("sand", 2), ("rough", 6) })
            foreach (var (label, mps, busy) in new[] { ("15 m/s", 15, false), ("36 m/s", 36, false), ("36 m/s, springs busy", 36, true) })
            {
                var x = Drive(New(), surface, mps * Mph, 10f, busy: busy);
                var (pk, rms) = Stats(x[(2 * Rate)..], g);
                rows.Add(new("road", $"{name}, {label}", pk, rms, -6));
            }
        foreach (int mps in new[] { 15, 30 })
        {
            // the cracks alone: asphalt with them over asphalt without, by the peaks
            var with = Drive(WithOptions(), 3, mps * Mph, 12f);
            var s0 = New(); s0.Surfaces = TactileSynth.FirstRound;
            var without = Drive(s0, 3, mps * Mph, 12f);
            var (pw, _) = Stats(with[(2 * Rate)..], g);
            var (po, ro) = Stats(without[(2 * Rate)..], g);
            rows.Add(new("road", $"asphalt cracks (lfe_cracks 100), {mps} m/s (bed alone: peak {po:0.0}, rms {ro:0.0})", pw, ro, -18));
        }
        {
            var x = Drive(WithOptions(), 3, 30 * Mph, 10f, shoulder: true);
            var (pk, rms) = Stats(x[(2 * Rate)..], g);
            rows.Add(new("road", "rumble strip (lfe_strip 100) on asphalt, 30 m/s", pk, rms, -9));
            var x2 = Drive(WithOptions(), 3, 15 * Mph, 10f, shoulder: true);
            var (pk2, rms2) = Stats(x2[(2 * Rate)..], g);
            rows.Add(new("road", "rumble strip (lfe_strip 100) on asphalt, 15 m/s", pk2, rms2, -9));
        }
        return rows;
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(dir);
        var csv = new StringBuilder("tuning,group,name,peak_dbfs,rms_dbfs,plan_dbfs\n");
        foreach (var (label, tune, surf) in new[] { ("before", TactileTuning.Before, TactileSynth.FirstRound), ("after", TactileTuning.Default, TactileSynth.Profiles) })
            foreach (var r in Measure(tune, surf))
                csv.Append(string.Create(CultureInfo.InvariantCulture, $"{label},{r.Group},\"{r.Name}\",{r.PeakDb:0.0},{r.RmsDb:0.0},{r.PlanDb:0}\n"));
        File.WriteAllText(Path.Combine(dir, "plan.csv"), csv.ToString());
        Console.Write(csv.ToString());
    }

    static (double, double) Stats(ReadOnlySpan<float> x, float g)
    {
        double pk = 0, sq = 0;
        foreach (var v in x) { double u = Math.Abs(v * g); pk = Math.Max(pk, u); sq += u * u; }
        return (Db(pk), Db(Math.Sqrt(sq / Math.Max(x.Length, 1))));
    }

    static double Db(double a) => a > 0 ? 20 * Math.Log10(a) : -200;

    public static float[] Drive(TactileSynth s, int surface, float mph, float seconds, bool busy = false, bool shoulder = false, float rpm = 0)
    {
        var rng = new Random(76);
        var outp = new float[(int)(seconds * Rate) / TickSamples * TickSamples];
        var e = new TactileEvents();
        for (int at = 0; at < outp.Length; at += TickSamples)
        {
            e.Clear();
            e.Surface = surface;
            e.Mph = mph;
            e.Shoulder = shoulder;
            if (rpm > 0) { e.EngineOn = true; e.Rpm = rpm; }
            e.TickSeconds = 102f / 2048f;
            if (busy) for (int k = 0; k < 4; k++) e.Travel[k] = (float)(rng.NextDouble() * 0.3 - 0.15);
            s.Step(e);
            s.Fill(outp.AsSpan(at, TickSamples), Rate);
        }
        return outp;
    }
}
