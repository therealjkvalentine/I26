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
}
