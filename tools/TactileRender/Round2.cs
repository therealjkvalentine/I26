using System.Globalization;
using System.Text;
using I76.Formats.Input;
using P = I76.Formats.Input.TactileSynth.SurfaceProfile;

/// <summary>
/// `--round2 OUTDIR`: the second set of WAV options: the surfaces plain and with their randomness, the crossing knock's
/// length x spectrum x level, the pavement's filled cracks, the rumble strip, the impacts with and without the cabin's ring,
/// and the whole lot in a drive. At LFE 100 % as the game sends them (no gain of the set's own: the headroom plan sets the
/// levels), 48 kHz mono 16-bit, with matched/ copies at one loudness and a README.md table.
/// </summary>
static class Round2
{
    const int Rate = 48000;
    const float Mph = 2.2369363f, Tick = 102f / 2048f;
    // synth surfaces
    const int None = 0, Dirt = 1, Sand = 2, Asphalt = 3, DirtRoad = 4, Planks = 5, Rough = 6;

    record Item(string Section, string Name, string What, bool Pick, float[] X);

    /// <summary>A scene: the surface and speed over time, and each tick's events; the synth as the game steps it.</summary>
    static float[] Scene(float seconds, Func<float, int> surface, Func<float, float> mps, Action<float, TactileEvents>? events = null,
        TactileTuning? tune = null, P[]? surfaces = null, uint seed = 76)
    {
        var s = new TactileSynth(seed) { Gain = AudioOutputs.ShakerGain(100), Tune = tune ?? TactileTuning.Default, Surfaces = surfaces ?? TactileSynth.Profiles };
        int per = (int)MathF.Round(Rate * Tick);
        var x = new float[(int)(seconds * Rate) / per * per];
        var e = new TactileEvents();
        for (int at = 0; at < x.Length; at += per)
        {
            float t = at / (float)Rate;
            e.Clear();
            e.Surface = surface(t);
            e.Mph = mps(t) * Mph;
            e.TickSeconds = Tick;
            events?.Invoke(t, e);
            s.Step(e);
            s.Fill(x.AsSpan(at, per), Rate);
        }
        return x;
    }

    static bool At(float t, float when) => t <= when && when < t + Tick;

    /// <summary>The crossings of a drive across surfaces changing at <paramref name="edges"/> (s): each axle's pair, the rear a wheelbase later.</summary>
    static void Knocks(float t, TactileEvents e, float mps, params float[] edges)
    {
        foreach (var w in edges)
        {
            if (At(t, w)) { e.Crossings.Add(1f); e.Crossings.Add(1f); }
            if (At(t, w + TactileSynth.Wheelbase / mps)) { e.Crossings.Add(1f); e.Crossings.Add(1f); }
        }
    }

    public static void Run(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "matched"));
        var items = new List<Item>();
        var def = TactileTuning.Default;
        var plain = def with { Variation = 0f };

        // ---- 1. randomness: each surface at 20 m/s for 12 s, plain and varied
        foreach (var (name, surf) in new[] { ("asphalt", Asphalt), ("dirtroad", DirtRoad), ("planks", Planks), ("dirt", Dirt), ("sand", Sand), ("rough", Rough) })
        {
            items.Add(new("1 randomness", $"1_{name}_20ms_plain", $"{name}, 20 m/s, 12 s: plain (variation 0: the lumps' jittered spacing only)", false,
                Scene(12, _ => surf, _ => 20, tune: plain)));
            items.Add(new("1 randomness", $"1_{name}_20ms_varied", $"{name}, 20 m/s, 12 s: with the randomness (bed wander, bigger lumps now and then)", true,
                Scene(12, _ => surf, _ => 20)));
        }

        // ---- 2. the knock: alone (an axle pair crossing every 2 s at 30 m/s, nothing else) and in asphalt-sand-asphalt at 20 m/s
        var knocks = new (string Id, string What, TactileTuning T, bool Pick)[]
        {
            ("K0_before_150ms_28Hz", "before: 28 Hz alone, 150 ms, 0.6", TactileTuning.Before with { RoadTrim = def.RoadTrim }, false),
            ("K1_250ms_full", "250 ms; 28 Hz + 52 Hz thud + 82 Hz click", def with { KnockSeconds = 0.25f }, false),
            ("K2_350ms_full", "350 ms; 28 + 52 + 82 Hz (the pick)", def, true),
            ("K3_500ms_full", "500 ms; 28 + 52 + 82 Hz", def with { KnockSeconds = 0.5f }, false),
            ("K4_350ms_28Hz_only", "350 ms; 28 Hz alone", def with { KnockBody = 0f, KnockClick = 0f }, false),
            ("K5_350ms_no_click", "350 ms; 28 + 52 Hz, no click", def with { KnockClick = 0f }, false),
            ("K6_350ms_full_plus4dB", "350 ms; 28 + 52 + 82 Hz, 4 dB stronger", def with { KnockLevel = def.KnockLevel * 1.585f }, false),
        };
        foreach (var (id, what, t, pick) in knocks)
        {
            items.Add(new("2 knock", $"2_{id}_alone", $"{what}: alone, an axle every 2 s at 30 m/s", pick,
                Scene(8, _ => None, _ => 30, (tt, e) => { foreach (var w in new[] { 0.5f, 2.5f, 4.5f, 6.5f }) if (At(tt, w)) { e.Crossings.Add(1f); e.Crossings.Add(1f); } }, t)));
            items.Add(new("2 knock", $"2_{id}_in_sequence", $"{what}: asphalt 3 s, sand 3 s, asphalt 3 s at 20 m/s", pick,
                Scene(9, tt => tt < 3 || tt >= 6 ? Asphalt : Sand, _ => 20, (tt, e) => Knocks(tt, e, 20, 3f, 6f), t)));
        }

        // ---- 3. the pavement's filled cracks, asphalt at 20 m/s for 12 s
        foreach (var (id, level, pick) in new[] { ("none", 0f, false), ("low_0.25", 0.25f, false), ("mid_0.45", 0.45f, true), ("high_0.8", 0.8f, false) })
        {
            var surf = (P[])TactileSynth.Profiles.Clone();
            surf[Asphalt] = surf[Asphalt] with { LumpLevel = level };
            items.Add(new("3 cracks", $"3_cracks_{id}_20ms", $"asphalt with filled cracks every 8 to 20 m (each axle), level {level}", pick, Scene(12, _ => Asphalt, _ => 20, surfaces: surf)));
        }
        items.Add(new("3 cracks", "3_cracks_mid_0.45_30ms", "asphalt with filled cracks, level 0.45, 30 m/s", true, Scene(12, _ => Asphalt, _ => 30)));

        // ---- 4. the rumble strip: asphalt 3 s, along the edge 4 s, asphalt 3 s at 25 m/s
        foreach (var (id, t, mps, pick) in new[] {
            ("0.12_fold60", def with { StripLevel = 0.12f }, 25f, false), ("0.24_fold60", def, 25f, true), ("0.36_fold60", def with { StripLevel = 0.36f }, 25f, false),
            ("0.24_fold90", def with { StripHz = 90f }, 25f, false), ("0.24_fold60_15ms", def, 15f, true), ("0.24_fold60_35ms", def, 35f, true) })
            items.Add(new("4 rumble strip", $"4_strip_{id}{(id.EndsWith("ms") ? "" : "_25ms")}", $"rumble strip, level {t.StripLevel}, a buzz at speed/{t.StripSpacing} m folded an octave down over {t.StripHz:0} Hz, {mps:0} m/s: asphalt 3 s, 4 s along the edge, asphalt", pick,
                Scene(10, _ => Asphalt, _ => mps, (tt, e) => e.Shoulder = tt >= 3 && tt < 7, t)));

        // ---- 5. impacts: the family, one every 2.5 s: a burst of 5 MG rounds, a cannon round, a rocket, a near blast, a sign,
        // a hard landing, a ram at 10 m/s, a building at 15 m/s, a head-on ram at 25 m/s
        var family = new (float At, Action<TactileEvents> Do)[]
        {
            (0.5f, e => e.Hits.Add((0, 8))), (0.6f, e => e.Hits.Add((0, 8))), (0.7f, e => e.Hits.Add((0, 8))), (0.8f, e => e.Hits.Add((0, 8))), (0.9f, e => e.Hits.Add((0, 8))),
            (3f, e => e.Hits.Add((0, 30))), (5.5f, e => e.Hits.Add((1, 60))), (8f, e => { e.Blasts.Add(5f); e.Hits.Add((1, 150)); }),
            (10.5f, e => e.Hits.Add((3, 15))), (13f, e => e.Hits.Add((3, 80))), (15.5f, e => e.Hits.Add((3, 120))),
            (18f, e => e.Hits.Add((3, 300))), (20.5f, e => e.Hits.Add((3, 783))),
        };
        void Family(TactileEvents e, float t) { foreach (var (w, d) in family) if (At(t, w)) d(e); }
        // the ring's length to -26 dB (the original's jolt ends under a pixel there): -40 dB is 1.535 times it
        var impacts = new (string Id, string What, TactileTuning T, bool Pick)[]
        {
            ("I0_before", "before: the DLL's strengths, one decaying sine", TactileTuning.Before, false),
            ("I1_plan_no_ring", "on the plan's curve with the 55 Hz part and 80 Hz click, no ring", def with { RingShare = 0f }, false),
            ("I2_ring_0.4s", "plan + cabin ring 26 Hz, gone (-26 dB) in 0.4 s", def with { RingSeconds = 0.4f * 1.535f }, false),
            ("I3_ring_0.7s", "plan + cabin ring, gone in 0.7 s", def with { RingSeconds = 0.7f * 1.535f }, false),
            ("I4_ring_1.0s", "plan + cabin ring, gone in 1.0 s", def with { RingSeconds = 1.0f * 1.535f }, false),
            ("I5_ring_1.47s_original", "plan + cabin ring, gone in 1.47 s: the original's cockpit jolt (time constant 0.5 s) (the pick)", def, true),
        };
        foreach (var (id, what, t, pick) in impacts)
            items.Add(new("5 impacts", $"5_{id}_family", $"{what}. At 0.5 s 5 MG rounds (8), 3 s a cannon round (30), 5.5 a rocket (60), 8 a blast at 5 m and its hit (150), 10.5 a sign (15), 13 a hard landing (80), 15.5 a ram at 10 m/s (120), 18 a building at 15 m/s (300), 20.5 a head-on ram at 25 m/s (783)", pick,
                Scene(23.5f, _ => None, _ => 0, (tt, e) => Family(e, tt), t)));
        items.Add(new("5 impacts", "5_I5_ram_over_sand_30ms", "the pick: driving on sand at 30 m/s, a ram (120) at 2 s and a head-on (783) at 5 s", true,
            Scene(9, _ => Sand, _ => 30, (tt, e) => { if (At(tt, 2f)) e.Hits.Add((3, 120)); if (At(tt, 5f)) e.Hits.Add((3, 783)); })));

        // ---- 6. a drive with everything: asphalt 4 s, dirt road 4 s, sand 4 s, asphalt 4 s at 20 m/s, a rocket hit at 9 s, a ram at 13 s
        foreach (var (id, t, s, pick) in new[] { ("before", TactileTuning.Before, TactileSynth.FirstRound, false), ("after", def, TactileSynth.Profiles, true) })
            items.Add(new("6 drive", $"6_drive_{id}", $"{id}: asphalt, dirt road, sand, asphalt (4 s each) at 20 m/s, the knocks, a rocket hit at 9 s, a ram (120) at 13 s", pick,
                Scene(16, tt => tt < 4 ? Asphalt : tt < 8 ? DirtRoad : tt < 12 ? Sand : Asphalt, _ => 20, (tt, e) =>
                {
                    Knocks(tt, e, 20, 4f, 8f, 12f);
                    if (At(tt, 9f)) e.Hits.Add((1, 60));
                    if (At(tt, 13f)) e.Hits.Add((3, 120));
                }, t, s)));

        var md = new StringBuilder();
        md.AppendLine("# Bass-shaker options, round 2");
        md.AppendLine();
        md.AppendLine("**For a bass shaker or a subwoofer, not headphones at volume.** The impacts reach the top of the scale; start quiet.");
        md.AppendLine();
        md.AppendLine("Rendered with the game's synth (`dotnet run --project tools/TactileRender -- --round2 OUTDIR`), 48 kHz mono 16-bit, at the LFE");
        md.AppendLine("Level of 100 % exactly as the game sends it: no gain of the set's own, since the headroom plan now sets the levels (the hardest");
        md.AppendLine("impact at full scale into the limiter, -3 dBFS out of it). `matched/` has each file at -20 dBFS RMS (peaks under -1 dBFS), to");
        md.AppendLine("compare character at one loudness. Springs still, engine off: the game adds both. Seed 76 throughout.");
        md.AppendLine();
        md.AppendLine("**Picks** (marked **R**, what the branch now plays): every `_varied` surface; knock K2 (350 ms, 28 + 52 + 82 Hz); cracks at 0.45;");
        md.AppendLine("the rumble strip at 0.24 folded under 60 Hz; impacts I5 (the plan's levels and the cabin ring timed as the original's jolt); `6_drive_after`.");
        md.AppendLine();
        string? section = null;
        foreach (var it in items)
        {
            if (it.Section != section)
            {
                section = it.Section;
                md.AppendLine($"## {section}");
                md.AppendLine();
                md.AppendLine("| | File | What it is | RMS dBFS | Peak dBFS |");
                md.AppendLine("|---|---|---|---|---|");
            }
            float rms = Rms(it.X), pk = Peak(it.X);
            float mg = rms > 0 ? MathF.Min(0.1f / rms, 0.891f / pk) : 1f;
            Wav(Path.Combine(dir, it.Name + ".wav"), it.X);
            Wav(Path.Combine(dir, "matched", it.Name + ".wav"), [.. it.X.Select(v => v * mg)]);
            md.AppendLine($"| {(it.Pick ? "**R**" : "")} | `{it.Name}.wav` | {it.What} | {Db(rms)} | {Db(pk)} |");
            if (it == items.Last() || items[items.IndexOf(it) + 1].Section != section) md.AppendLine();
        }
        File.WriteAllText(Path.Combine(dir, "README.md"), md.ToString());
        Console.WriteLine($"{items.Count} files in {Path.GetFullPath(dir)}");
    }

    static string Db(float a) => a > 0 ? (20 * MathF.Log10(a)).ToString("0.0", CultureInfo.InvariantCulture) : "-inf";
    static float Peak(float[] x) => x.Length == 0 ? 0 : x.Max(MathF.Abs);
    static float Rms(float[] x) => MathF.Sqrt(x.Sum(v => v * v) / Math.Max(x.Length, 1));

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
