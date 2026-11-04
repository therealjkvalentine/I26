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
    public void The_road_follows_the_springs_and_not_the_engine()
    {
        float Bed(TactileEvents e, int ticks = 1, Action<TactileEvents, int>? each = null)
        {
            var s = new TactileSynth();
            float rms = 0;
            for (int t = 0; t < ticks; t++)
            {
                each?.Invoke(e, t);
                s.Step(e);
                rms = Rms(Run(s, 0.25).AsSpan(Rate / 10));
            }
            return rms;
        }
        // the engine off, rolling on a paved road: felt, and less than on a dirt road
        float paved = Bed(new TactileEvents { Surface = 3, Mph = 40 });
        Assert.True(paved > 0.01f);
        Assert.True(paved < Bed(new TactileEvents { Surface = 4, Mph = 40 }));
        Assert.True(TactileSynth.RoadLevel(3, 40, 0) < TactileSynth.RoadLevel(4, 40, 0));

        // the load: the mean deflection of the four corners; a turn's (two compressed, two extended) adds to the bed
        Assert.Equal(0.35f, TactileSynth.LoadOf([0.5f, -0.5f, 0.2f, -0.2f]), 4);
        Assert.Equal(1f, TactileSynth.LoadOf([3f, -3f, 1f, 1f]));
        float flat = TactileSynth.RoadLevel(3, 40, 0, 0f, 0f);
        Assert.Equal(TactileSynth.RoadLevel(3, 40, 0), flat);
        Assert.Equal(flat * 1.8f + TactileSynth.LoadGain * 0.8f, TactileSynth.RoadLevel(3, 40, 0, 0.8f, 0f), 5);
        var turn = new TactileEvents { Surface = 3, Mph = 40 };
        turn.Travel[0] = turn.Travel[2] = 0.8f; turn.Travel[1] = turn.Travel[3] = -0.8f;
        Assert.True(Bed(turn) > 1.5f * paved);
        // standing, loaded (braked to a stop on a slope): the load alone; in the air nothing, however the springs hang
        Assert.Equal(TactileSynth.LoadGain * 0.5f, TactileSynth.RoadLevel(3, 0, 0, 0.5f, 0f), 5);
        Assert.Equal(0f, TactileSynth.RoadLevel(9, 60, 0, 1f, 40f));

        // the springs' rate, in limits a second: the full jolt at JoltFull, so the tick rate does not change what a bump is worth
        Assert.Equal(TactileSynth.JoltGain, TactileSynth.RoadLevel(3, 0, 0, 0f, TactileSynth.JoltFull), 5);
        // standing on the ground, the corners swinging ±0.02 of their limit each tick
        float Bumps(float tick) => Bed(new TactileEvents { Surface = 3, TickSeconds = tick }, 6, (e, t) => Array.Fill(e.Travel, t % 2 == 0 ? 0.02f : -0.02f));
        float still = Bed(new TactileEvents { Surface = 3 }, 6, (e, _) => Array.Fill(e.Travel, 0.02f));
        Assert.True(Bumps(102f / 2048f) > 4f * still);
        Assert.InRange(Bumps(1f / 60f) / Bumps(1f / 30f), 1.8f, 2.1f);   // the same swing twice as often: twice the rate
        // everything at once stays under the bed's top
        Assert.Equal(TactileSynth.RoadMax, TactileSynth.RoadLevel(6, 120, 4, 1f, 100f));
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
    public void The_lfe_level_is_half_as_strong_again_inside_the_limiter()
    {
        Assert.Equal(1.5f, AudioOutputs.ShakerGain(100));
        Assert.Equal(3f, AudioOutputs.ShakerGain(200));
        Assert.Equal(0f, AudioOutputs.ShakerGain(0));
        float Engine(float gain)
        {
            var s = new TactileSynth { Gain = gain };
            s.Step(new TactileEvents { EngineOn = true, Rpm = 600 });
            Run(s, 0.5);
            return Amplitude(Run(s, 1.0), TactileSynth.EngineHz(600));
        }
        Assert.InRange(Engine(AudioOutputs.ShakerGain(100)) / Engine(1f), 1.45f, 1.5f);   // the idle's 0.08: barely limited
        // 200 %: everything at once, for a second of new hits each tick, stays under the ceiling
        var p = new TactileSynth { Gain = AudioOutputs.ShakerGain(200) };
        float peak = 0;
        for (int tick = 0; tick < 20; tick++)
        {
            var e = new TactileEvents { EngineOn = true, Rpm = 5000, Surface = 6, Mph = 120, FlatTyres = 4 };
            for (int i = 0; i < 4; i++) { e.Hits.Add((1, 200)); e.Hits.Add((3, 200)); e.Blasts.Add(0f); e.Shots.Add(new WeaponShot(i, 2, 1, 1)); }
            p.Step(e);
            foreach (var v in Run(p, 0.05)) peak = Math.Max(peak, Math.Abs(v));
        }
        Assert.True(peak > 0.84f);
        Assert.True(peak <= TactileSynth.Ceiling);
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
    public void Each_source_takes_its_own_weight()
    {
        // one source at a time, at a weight: the level it reaches over the same source at 100 % (a low gain keeps the limiter out of it)
        float Level(TactileSource src, int percent)
        {
            var s = new TactileSynth { Gain = 0.25f };
            var w = TactileSources.Defaults();
            w[(int)src] = percent;
            s.SetWeights(w);
            var e = new TactileEvents();
            switch (src)
            {
                case TactileSource.Engine: e.EngineOn = true; e.Rpm = 600; break;
                case TactileSource.Road: e.Surface = 6; e.Mph = 40; break;
                case TactileSource.Hits: e.Hits.Add((3, 20)); break;
                case TactileSource.Shots: e.Shots.Add(new WeaponShot(0, 2, 1, 1)); break;
                case TactileSource.Explosions: e.Blasts.Add(60f); break;
                case TactileSource.Offroad: e.Surface = 2; e.Mph = 40; break;
                case TactileSource.Bumps: e.Mph = 40; e.Crossings.Add(1f); break;
            }
            s.Step(e);
            bool bed = src is TactileSource.Engine or TactileSource.Road or TactileSource.Offroad;
            return Rms(Run(s, bed ? 2.0 : 0.3).AsSpan(bed ? Rate / 2 : 0));
        }
        foreach (var src in Enum.GetValues<TactileSource>())
        {
            float full = Level(src, 100);
            Assert.True(full > 0.002f, $"{src}");
            Assert.Equal(0f, Level(src, 0));
            Assert.InRange(Level(src, 50) / full, 0.45f, 0.55f);       // the road's noise is the same sequence each run
            Assert.InRange(Level(src, 200) / full, 1.9f, 2.02f);
        }
        // the others are untouched by one source's weight
        var only = new TactileSynth();
        only.SetWeights([0, 100, 100, 100, 100, 100, 100]);
        only.Step(new TactileEvents { EngineOn = true, Rpm = 600, Surface = 6, Mph = 40 });
        Assert.True(Rms(Run(only, 2.0).AsSpan(Rate / 2)) > 0.01f);
        Assert.Equal(["lfe_engine", "lfe_road", "lfe_hits", "lfe_shots", "lfe_explosions", "lfe_offroad", "lfe_bumps"], TactileSources.Keys);
        Assert.Equal(Enum.GetValues<TactileSource>().Length, TactileSources.Keys.Length);
        Assert.Equal(200, TactileSources.Clamp(999));
        Assert.Equal(0, TactileSources.Clamp(-5));
    }

    [Fact]
    public void Output_and_level_keys_read_back()
    {
        foreach (AudioOutput o in Enum.GetValues<AudioOutput>()) Assert.Equal(o, AudioOutputs.Parse(AudioOutputs.Key(o)));
        Assert.Equal(AudioOutput.Stereo, AudioOutputs.Parse("quad"));
        Assert.Equal(["Stereo", "Phones", "5.1", "7.1"], AudioOutputs.Names);
        // the LFE Level alone says whether the signal plays; 5.1 and 7.1 put it on the LFE, the rest mix it into both channels
        Assert.Equal(0, AudioOutputs.DefaultLevel);
        Assert.False(AudioOutputs.Tactile(AudioOutputs.DefaultLevel));
        Assert.True(AudioOutputs.Tactile(25));
        Assert.False(AudioOutputs.Discrete(AudioOutput.Headphones));
        Assert.True(AudioOutputs.Discrete(AudioOutput.Surround71));
        // an older file's 2.1 (the stereo mix) is Stereo, and with 5.1 and 7.1 keeps the old absent level, 100
        Assert.Equal(AudioOutput.Stereo, AudioOutputs.Parse("2.1"));
        Assert.True(AudioOutputs.HadShaker("2.1"));
        Assert.True(AudioOutputs.HadShaker("5.1"));
        Assert.False(AudioOutputs.HadShaker("stereo"));
        Assert.False(AudioOutputs.HadShaker(null));
        Assert.Equal(100, AudioOutputs.OldDefaultLevel);
        Assert.Equal(125, AudioOutputs.ClampLevel(130));
        Assert.Equal(200, AudioOutputs.ClampLevel(999));
        Assert.Equal(2f, AudioOutputs.BassBoost(100));
        Assert.Equal(90f, AudioOutputs.ClampCrossover(float.NaN));
    }

    /// <summary>The road alone on <paramref name="surface"/> at <paramref name="mph"/> for 3 s, its last 2 s (a low gain keeps the limiter out).</summary>
    private static float[] Road(int surface, float mph, Action<TactileSynth>? set = null)
    {
        var s = new TactileSynth { Gain = 0.25f };
        set?.Invoke(s);
        var b = new float[3 * Rate];
        int tick = (int)(Rate * 102f / 2048f);
        for (int at = 0; at + tick <= b.Length; at += tick)
        {
            s.Step(new TactileEvents { Surface = surface, Mph = mph });
            s.Fill(b.AsSpan(at, tick), Rate);
        }
        return b[Rate..];
    }

    /// <summary>The power-weighted centre of the signal between 16 and 100 Hz, from its components every 2 Hz.</summary>
    private static float Centroid(float[] x)
    {
        double sum = 0, w = 0;
        for (float hz = 16; hz <= 100; hz += 2) { double a = Amplitude(x, hz); sum += a * a * hz; w += a * a; }
        return (float)(sum / w);
    }

    [Fact]
    public void A_dirt_road_sits_above_the_ground_off_it_in_pitch()
    {
        // synth surfaces: dirt 1, sand 2, asphalt 3, dirt road 4, rough 6
        float road = Centroid(Road(4, 40)), sand = Centroid(Road(2, 40)), dirt = Centroid(Road(1, 40)), rough = Centroid(Road(6, 40));
        Assert.True(road > sand + 15f, $"dirt road {road:0} Hz, sand {sand:0} Hz");
        Assert.True(road > dirt + 15f, $"dirt road {road:0} Hz, dirt {dirt:0} Hz");
        Assert.True(road > rough + 15f, $"dirt road {road:0} Hz, rough {rough:0} Hz");
        // as before the profiles, one band for all: within a few Hz of each other
        float flatRoad = Centroid(Road(4, 40, s => s.Surfaces = TactileSynth.Flat)), flatSand = Centroid(Road(2, 40, s => s.Surfaces = TactileSynth.Flat));
        Assert.InRange(flatRoad - flatSand, -4f, 4f);
    }

    [Fact]
    public void The_roads_and_the_ground_stand_further_apart()
    {
        float Db(float[] a, float[] b) => 20 * MathF.Log10(Rms(a) / Rms(b));
        float now = Db(Road(2, 40), Road(3, 40));
        float was = Db(Road(2, 40, s => { s.Surfaces = TactileSynth.Flat; s.Contrast = 1; }), Road(3, 40, s => { s.Surfaces = TactileSynth.Flat; s.Contrast = 1; }));
        Assert.InRange(was, 6f, 8.5f);          // the force model's 35 against 15
        Assert.True(now > 13f, $"sand over asphalt {now:0.0} dB");
        // the stretch pivots on a dirt junction's strength (30): it keeps its level, the roads go down, the ground up
        Assert.Equal(TactileSynth.RoadLevel(8, 60, 0, 1f), TactileSynth.RoadLevel(8, 60, 0), 5);
        Assert.True(TactileSynth.RoadLevel(3, 60, 0) < TactileSynth.RoadLevel(3, 60, 0, 1f));
        Assert.True(TactileSynth.RoadLevel(2, 60, 0) > TactileSynth.RoadLevel(2, 60, 0, 1f));
        // contrast 1 is the earlier straight line
        Assert.Equal(0.3f * 15f / 40f * 0.5f, TactileSynth.RoadLevel(3, 40, 0, 1f), 5);
    }

    [Fact]
    public void Lfe_offroad_moves_the_ground_and_leaves_the_roads()
    {
        Action<TactileSynth> Off(int percent) => s => { var w = TactileSources.Defaults(); w[(int)TactileSource.Offroad] = percent; s.SetWeights(w); };
        Assert.Equal(Rms(Road(3, 40)), Rms(Road(3, 40, Off(0))));
        Assert.Equal(Rms(Road(4, 40)), Rms(Road(4, 40, Off(200))));
        Assert.Equal(0f, Rms(Road(2, 40, Off(0))));
        Assert.InRange(Rms(Road(6, 40, Off(150))) / Rms(Road(6, 40)), 1.45f, 1.55f);
    }

    [Fact]
    public void The_band_follows_the_surface_without_a_click()
    {
        var s = new TactileSynth { Gain = 0.25f };
        int tick = (int)(Rate * 102f / 2048f);
        var b = new float[tick];
        float jump = 0, last = 0;
        for (int i = 0; i < 80; i++)
        {
            s.Step(new TactileEvents { Surface = i < 40 ? 3 : 2, Mph = 50 });
            s.Fill(b, Rate);
            foreach (var v in b) { jump = Math.Max(jump, Math.Abs(v - last)); last = v; }
            if (i == 39) Assert.Equal(56f, s.BedHz);
        }
        Assert.Equal(30f, s.BedHz);
        Assert.Equal(2, s.Surface);
        // a 30 Hz band moves little from one sample to the next; a click would be a step of the signal's own size
        Assert.True(jump < 0.02f, $"largest step {jump}");
    }

    [Fact]
    public void A_crossing_knocks_by_speed_and_lfe_bumps()
    {
        Assert.Null(TactileSynth.BumpThump(1f, 3f));
        Assert.Null(TactileSynth.BumpThump(0f, 60f));
        Assert.Equal(TactileSynth.BumpLevel, TactileSynth.BumpThump(1f, 50f)!.Value.Level, 5);
        Assert.Equal(TactileSynth.BumpLevel, TactileSynth.BumpThump(1f, 120f)!.Value.Level, 5);
        Assert.Equal(TactileSynth.BumpLevel * 0.5f * 0.6f, TactileSynth.BumpThump(0.5f, 30f)!.Value.Level, 5);
        Assert.Equal(TactileSynth.BumpLevel * 0.15f, TactileSynth.BumpThump(1f, 5f)!.Value.Level, 5);
        float Knock(int percent)
        {
            var s = new TactileSynth { Gain = 0.25f };
            var w = TactileSources.Defaults();
            w[(int)TactileSource.Bumps] = percent;
            s.SetWeights(w);
            var e = new TactileEvents { Mph = 50 };
            e.Crossings.Add(1f);
            s.Step(e);
            var x = Run(s, 0.3);
            Assert.Equal(1, s.Bumps);
            return Amplitude(x.AsSpan(0, Rate / 10), TactileSynth.BumpHz);
        }
        Assert.True(Knock(100) > 0.01f);
        Assert.Equal(0f, Knock(0));
        Assert.InRange(Knock(200) / Knock(100), 1.9f, 2.05f);
    }

    [Fact]
    public void The_lumps_come_with_the_distance_driven_and_stop_with_the_car()
    {
        // rough ground's lumps at 25 Hz: twice the speed, about twice as many in the same time (more of the 25 Hz component
        // than the bed's growth alone gives); standing, none at all
        float Lumps(float mph) => Amplitude(Road(6, mph), 25f) / TactileSynth.RoadLevel(6, mph, 0);
        Assert.True(Lumps(40) > Lumps(20) * 1.1f);
        Assert.Equal(0f, Rms(Road(6, 0)));
        // the old profile has no lumps: asphalt and every surface of Flat is the bed alone
        Assert.Equal(0f, TactileSynth.Profiles[3].LumpLevel);
        Assert.All(TactileSynth.Flat, p => Assert.Equal(0f, p.LumpLevel + p.GrainLevel));
    }
}
