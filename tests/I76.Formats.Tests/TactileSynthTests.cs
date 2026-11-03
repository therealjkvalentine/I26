using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

public class TactileSynthTests
{
    private const int Rate = 48000;

    private static float[] Run(TactileSynth s, double seconds)
    {
        var b = new float[(int)(seconds * Rate)];
        s.Fill(b, Rate);
        return b;
    }

    private static float Rms(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (var v in x) sum += v * v;
        return (float)Math.Sqrt(sum / x.Length);
    }

    /// <summary>The amplitude of the <paramref name="hz"/> component (Goertzel).</summary>
    private static float Amplitude(ReadOnlySpan<float> x, float hz)
    {
        double k = 2 * Math.Cos(2 * Math.PI * hz / Rate), s1 = 0, s2 = 0;
        foreach (var v in x) (s1, s2) = (v + k * s1 - s2, s1);
        return (float)(Math.Sqrt(Math.Max(s1 * s1 + s2 * s2 - k * s1 * s2, 0)) * 2 / x.Length);
    }

    [Fact]
    public void Nothing_going_on_is_silence()
    {
        var s = new TactileSynth();
        s.Step(new TactileEvents());
        Assert.All(Run(s, 0.5), v => Assert.Equal(0f, v));
    }

    [Fact]
    public void The_engine_is_a_sine_by_rpm_inside_the_band()
    {
        var s = new TactileSynth();
        s.Step(new TactileEvents { EngineOn = true, Rpm = 600 });
        var idle = Run(s, 1.0).AsSpan(Rate / 2);
        Assert.InRange(Amplitude(idle, 25f) / TactileSynth.EngineLevel(true, 600), 0.9f, 1.02f);   // the high-pass costs 25 Hz 0.5 dB
        s.Step(new TactileEvents { EngineOn = true, Rpm = 5000 });
        var top = Run(s, 1.0).AsSpan(Rate / 2);
        Assert.Equal(45f, TactileSynth.EngineHz(5000));
        Assert.InRange(Amplitude(top, 45f) / TactileSynth.EngineLevel(true, 5000), 0.95f, 1.02f);
        Assert.True(Amplitude(top, 25f) < 0.005f);
    }

    [Fact]
    public void The_road_bed_follows_surface_and_speed_and_stops_in_the_air()
    {
        float Bed(int surface, float mph)
        {
            var s = new TactileSynth();
            s.Step(new TactileEvents { Surface = surface, Mph = mph });
            return Rms(Run(s, 2.0).AsSpan(Rate / 2));
        }
        // rocky ground (the roughest) at 80 mph: about the rms of a sine at the bed's level
        float lot = Bed(6, 80);
        Assert.InRange(lot, 0.5f * TactileSynth.RoadLevel(6, 80, 0) / MathF.Sqrt(2), 1.5f * TactileSynth.RoadLevel(6, 80, 0) / MathF.Sqrt(2));
        Assert.True(Bed(3, 80) < lot / 2);      // a paved road, 15 against 40
        Assert.True(Bed(6, 20) < lot / 3);
        Assert.Equal(0f, Bed(9, 80));          // airborne
        Assert.Equal(0f, Bed(0, 0));           // standing
    }

    [Fact]
    public void Sources_outside_20_to_90_hz_are_cut()
    {
        float Through(float hz)
        {
            var s = new TactileSynth();
            s.Add(new TactileSynth.Thump(hz, 0.4f, 10000f));
            return Amplitude(Run(s, 1.0).AsSpan(Rate / 2), hz) / 0.4f;
        }
        Assert.InRange(Through(45f), 0.85f, 1.05f);
        Assert.True(Through(300f) < 0.1f);     // 20 dB down
        Assert.True(Through(6f) < 0.2f);       // 14 dB down
    }

    [Fact]
    public void A_pile_of_hits_stays_under_the_ceiling()
    {
        var s = new TactileSynth();
        var e = new TactileEvents { EngineOn = true, Rpm = 4000, Surface = 6, Mph = 120, FlatTyres = 4 };
        for (int i = 0; i < 6; i++) { e.Hits.Add((1, 200)); e.Blasts.Add(0f); e.Shots.Add(new WeaponShot(i, 3, 1, 1)); }
        s.Step(e);
        Run(s, 0.3);
        Assert.True(s.Peak > 0.7f);
        Assert.True(s.Peak <= TactileSynth.Ceiling);
    }

    [Fact]
    public void Hits_take_the_dll_strengths_and_blasts_end_at_their_range()
    {
        Assert.Equal(0.35f, TactileSynth.HitThump(1, 0)!.Value.Level, 3);
        Assert.Equal(1f, TactileSynth.HitThump(1, 150)!.Value.Level, 3);
        Assert.Null(TactileSynth.HitThump(2, 50));                 // fire
        Assert.Null(TactileSynth.BlastThump(TactileSynth.BlastRange));
        Assert.True(TactileSynth.BlastThump(10f)!.Value.Level > TactileSynth.BlastThump(100f)!.Value.Level);
    }

    [Fact]
    public void A_thump_dies_away_and_reset_silences_the_beds()
    {
        var s = new TactileSynth();
        s.Step(new TactileEvents { Shots = { new WeaponShot(0, 2, 1, 1) } });
        Run(s, 0.05);
        Assert.True(s.Peak > 0.2f);
        Run(s, 0.1);   // its 0.12 s out
        Run(s, 0.2);
        Assert.True(s.Peak < 0.01f);
        s.Step(new TactileEvents { EngineOn = true, Rpm = 3000 });
        Run(s, 0.3);
        s.Reset();
        Run(s, 0.2);   // the 30 ms fade
        Run(s, 0.3);
        Assert.True(s.Peak < 0.002f);
    }
    [Fact]
    public void The_eq_curve_interpolates_in_log_frequency_and_holds_its_ends()
    {
        var eq = TactileEq.Parse("40:0, 20:6, 80:-6, junk, 60:x")!;
        Assert.Equal(3, eq.Count);
        Assert.Equal(6f, eq.Db(10f), 3);                       // held below the first point
        Assert.Equal(-6f, eq.Db(200f), 3);                     // and above the last
        Assert.Equal(3f, eq.Db(MathF.Sqrt(20f * 40f)), 3);    // half way in log frequency, not at 30 Hz
        Assert.Equal(-3f, eq.Db(MathF.Sqrt(40f * 80f)), 3);
        Assert.Equal(2f, eq.Gain(20f), 2);                     // +6 dB
        Assert.Equal(TactileEq.MaxDb, TactileEq.Parse("30:20")!.Db(30f));   // capped
        Assert.Null(TactileEq.Parse(""));
        Assert.Null(TactileEq.Parse("off"));
    }

    [Fact]
    public void The_eq_acts_on_each_tone_at_its_frequency()
    {
        float Out(float hz, string? eq)
        {
            var s = new TactileSynth { Eq = TactileEq.Parse(eq) };
            s.Add(new TactileSynth.Thump(hz, 0.05f, 10000f));   // well under the limiter
            return Amplitude(Run(s, 1.0).AsSpan(Rate / 2), hz);
        }
        // a sweep of tones through a curve that cuts 35 Hz by 6 dB and lifts 20 Hz by 4
        const string curve = "20:4, 35:-6, 60:0";
        foreach (float hz in new[] { 20f, 25f, 35f, 45f, 60f, 80f })
        {
            float db = 20 * MathF.Log10(Out(hz, curve) / Out(hz, null));
            Assert.InRange(db, TactileEq.Parse(curve)!.Db(hz) - 0.1f, TactileEq.Parse(curve)!.Db(hz) + 0.1f);
        }
        // the engine's sine too
        var e = new TactileSynth { Eq = TactileEq.Parse("25:-6") };
        e.Step(new TactileEvents { EngineOn = true, Rpm = 600 });
        Assert.InRange(Amplitude(Run(e, 1.0).AsSpan(Rate / 2), 25f) / TactileSynth.EngineLevel(true, 600), 0.45f, 0.51f);
    }

    [Fact]
    public void The_eq_shapes_the_road_bed_by_its_curve()
    {
        // the same noise with and without the curve: the ratio of their spectra, third-octave bands, against the curve
        const string curve = "20:4, 35:-6, 60:0, 80:3";
        float[] Bed(string? eq)
        {
            var s = new TactileSynth { Eq = TactileEq.Parse(eq) };
            s.Step(new TactileEvents { Surface = 6, Mph = 30 });     // a low bed, under the limiter
            return Run(s, 8.0)[Rate..];
        }
        float[] flat = Bed(null), shaped = Bed(curve);
        float Band(float[] x, float hz)
        {
            double sum = 0;
            for (float f = hz * 0.95f; f <= hz * 1.05f; f += 0.5f) { float a = Amplitude(x, f); sum += a * a; }
            return (float)sum;
        }
        var c = TactileEq.Parse(curve)!;
        foreach (float hz in new[] { 25f, 31.5f, 40f, 50f, 63f })
            Assert.InRange(10 * MathF.Log10(Band(shaped, hz) / Band(flat, hz)), c.Db(hz) - 1f, c.Db(hz) + 1f);
    }

    [Fact]
    public void Explosions_fall_off_as_the_air_then_the_ground_carry_them()
    {
        float L(float m) => TactileSynth.BlastThump(m)!.Value.Level;
        Assert.Equal(0.7f, L(0f), 3);
        Assert.Equal(0.7f, L(TactileSynth.BlastNear), 3);
        Assert.Equal(L(30f) / 2, L(60f), 3);                    // 1/r to the knee
        Assert.Equal(L(250f) / 2, L(1000f), 3);                 // 1/sqrt(r) past it
        Assert.InRange(20 * MathF.Log10(L(1999f) / L(0f)), -31f, -29f);   // 30 dB under a blast at the car, at 2 km
        Assert.Null(TactileSynth.BlastThump(TactileSynth.BlastRange));
        // far ones lower and longer
        Assert.Equal(TactileSynth.BlastHz, TactileSynth.BlastThump(100f)!.Value.Hz);
        Assert.True(TactileSynth.BlastThump(1500f)!.Value.Hz < 20f && TactileSynth.BlastThump(1500f)!.Value.Seconds > 1f);
        // a blast at 1 km is still felt over the 15 Hz high-pass, and at 150 m where the old cut was it is no longer zero
        var s = new TactileSynth();
        s.Step(new TactileEvents { Blasts = { 1000f } });
        Run(s, 0.1);
        Assert.InRange(s.Peak, 0.5f * L(1000f), 1.1f * L(1000f));
        Assert.True(L(150f) > 0.05f);
    }

    [Fact]
    public void A_full_bank_drops_its_quietest_thump()
    {
        var s = new TactileSynth();
        for (int i = 0; i < 8; i++) s.Add(new TactileSynth.Thump(30f, 0.5f, 10f));
        s.Add(new TactileSynth.Thump(30f, 0.01f, 10f));   // a far blast: no room, not added
        s.Add(new TactileSynth.Thump(30f, 0.9f, 10f));    // a hit: replaces one
        Run(s, 0.05);
        Assert.True(s.Peak > 0.84f);
    }

    [Fact]
    public void The_low_pass_follows_the_crossover()
    {
        float Through(float hz, float corner)
        {
            var s = new TactileSynth { LowPassHz = corner };
            s.Add(new TactileSynth.Thump(hz, 0.4f, 10000f));
            return Amplitude(Run(s, 1.0).AsSpan(Rate / 2), hz) / 0.4f;
        }
        Assert.InRange(Through(60f, 60f), 0.66f, 0.76f);     // -3 dB at the corner
        Assert.InRange(Through(60f, 120f), 0.9f, 1.05f);
    }

    [Fact]
    public void Output_and_level_keys_read_back()
    {
        foreach (AudioOutput o in Enum.GetValues<AudioOutput>()) Assert.Equal(o, AudioOutputs.Parse(AudioOutputs.Key(o)));
        Assert.Equal(AudioOutput.Stereo, AudioOutputs.Parse("quad"));
        Assert.False(AudioOutputs.Tactile(AudioOutput.Headphones));
        Assert.True(AudioOutputs.Tactile(AudioOutput.Sub21));
        Assert.Equal(125, AudioOutputs.ClampLevel(130));
        Assert.Equal(200, AudioOutputs.ClampLevel(999));
        Assert.Equal(2f, AudioOutputs.BassBoost(100));
        Assert.Equal(90f, AudioOutputs.ClampCrossover(float.NaN));
    }
}
