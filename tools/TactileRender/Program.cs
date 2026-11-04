// Renders the bass-shaker synth (src/I76.Formats/Input/TactileSynth.cs) to WAV files, to listen to surface feels outside the
// game: `dotnet run --project tools/TactileRender -- OUTDIR` (`-- --sweep OUTDIR` measures the chain instead: Sweeps.cs). 48 kHz mono 16-bit, at the LFE Level of 100 % (gain 1.5), the
// engine off unless a file says otherwise. The set is the one the bass-shaker playtest compared: the surfaces as they were,
// the port's profiles and variants, the wheel-crossing knock. A README.md table of what each file is goes with them.
using System.Globalization;
using System.Text;
using I76.Formats.Input;
using I76.Sim.Vehicles;
using P = I76.Formats.Input.TactileSynth.SurfaceProfile;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
// --sweep OUTDIR: the chain's measurements (Sweeps.cs); plot them with plot_sweeps.py
if (args is ["--sweep", var sweepDir]) { Sweeps.Run(sweepDir); return; }
// --plan OUTDIR: each source's peak at the limiter's input against the headroom plan (Plan.cs)
if (args is ["--plan", var planDir]) { Plan.Run(planDir); return; }
// --round2 OUTDIR: the second set of options (Round2.cs)
if (args is ["--round2", var round2Dir]) { Round2.Run(round2Dir); return; }
// --measure OUTDIR: constant-level sweeps and tones to measure a shaker with (Measure.cs; eq_from_measurement.py reads the readings)
if (args is ["--measure", var measureDir]) { Measure.Run(measureDir); return; }
// --find-strip INSTALL MISSION...: a straight drive across asphalt, a dirt road and off-road (StripFinder.cs)
if (args is ["--find-strip", var stripInstall, .. var stripMissions]) { StripFinder.Run(stripInstall, stripMissions); return; }
string outDir = args.Length > 0 ? args[0] : "wavs";
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(Path.Combine(outDir, "matched"));

const int Rate = 48000;
const float Tick = 102f / 2048f;
const float Wheelbase = 3f;    // a mid-size car's, front axle to rear (m)
// synth surfaces (the car's + 1)
const int Dirt = 1, Sand = 2, Asphalt = 3, DirtRoad = 4, Rough = 6;

var cur = TactileSynth.Flat;
// round one's set, as round one had it (no trims, ring or randomness; asphalt without cracks; --round2 has the current synth)
var rec = TactileSynth.FirstRound;
P[] With(int surface, P p) { var a = (P[])rec.Clone(); a[surface] = p; return a; }

var renders = new List<Render>();
foreach (float mps in new[] { 15f, 30f })
{
    string v = $"{mps:0}ms";
    // the surfaces as they were (one 50 Hz band, strengths in a straight line, no lumps, no knock)
    renders.Add(new($"01_current_pavement_{v}", "current: asphalt", mps, cur, 1f, [(7, Asphalt)]));
    renders.Add(new($"02_current_gravelroad_{v}", "current: dirt road (the gravel road)", mps, cur, 1f, [(7, DirtRoad)]));
    renders.Add(new($"03_current_offroad_sand_{v}", "current: sand (most trip maps' off-road)", mps, cur, 1f, [(7, Sand)]));
    renders.Add(new($"04_current_offroad_rough_{v}", "current: rough ground (id 5, gravel-sounding)", mps, cur, 1f, [(7, Rough)]));
    // pavement
    renders.Add(new($"10_pave_A_hum56_{v}", "pavement A (RECOMMENDED): narrow 56 Hz hum, contrast 2", mps, rec, 2f, [(7, Asphalt)], Rec: true));
    renders.Add(new($"11_pave_B_joints_{v}", "pavement B: A plus a 32 Hz expansion-joint knock every 12 m", mps,
        With(Asphalt, new P(56f, 1.6f, LumpLevel: 0.08f, LumpSpacing: 12f, LumpHz: 32f, Periodic: true)), 2f, [(7, Asphalt)]));
    renders.Add(new($"12_pave_C_contrast3_{v}", "pavement C: A at contrast 3 (fainter still)", mps, rec, 3f, [(7, Asphalt)]));
    // gravel road: pitched above off-road by different amounts
    renders.Add(new($"20_gravel_A_58hz_smooth_{v}", "gravel road A: 58 Hz band, no grit (+9 semitones over dirt's 34)", mps,
        With(DirtRoad, new P(58f, 1.2f)), 2f, [(7, DirtRoad)]));
    renders.Add(new($"21_gravel_B_66hz_grit_{v}", "gravel road B (RECOMMENDED): 66 Hz band, 76 Hz grit every 0.45 m, faint lumps", mps, rec, 2f, [(7, DirtRoad)], Rec: true));
    renders.Add(new($"22_gravel_C_78hz_densegrit_{v}", "gravel road C: 78 Hz band, 82 Hz grit every 0.3 m", mps,
        With(DirtRoad, new P(78f, 1.2f, LumpLevel: 0.05f, LumpSpacing: 4f, LumpHz: 30f, GrainLevel: 0.08f, GrainSpacing: 0.3f, GrainHz: 82f)), 2f, [(7, DirtRoad)]));
    // off-road
    renders.Add(new($"30_offroad_sand_A_{v}", "off-road sand A (RECOMMENDED): 30 Hz band, 24 Hz lumps every 3.5 m", mps, rec, 2f, [(7, Sand)], Rec: true));
    renders.Add(new($"31_offroad_dirt_A_{v}", "off-road dirt A (RECOMMENDED): 34 Hz band, 26 Hz lumps every 2.5 m", mps, rec, 2f, [(7, Dirt)], Rec: true));
    renders.Add(new($"32_offroad_sand_B_heavy_{v}", "off-road sand B: lumps twice as strong every 2 m, contrast 3", mps,
        With(Sand, new P(30f, 0.8f, LumpLevel: 0.16f, LumpSpacing: 2f, LumpHz: 24f, OffRoad: true)), 3f, [(7, Sand)]));
    // bumpy dirt
    renders.Add(new($"40_bumpy_A_random_{v}", "bumpy dirt A (RECOMMENDED, rough ground): 32 Hz band, 25 Hz lumps every 1.4 m, light grit", mps, rec, 2f, [(7, Rough)], Rec: true));
    renders.Add(new($"41_bumpy_B_washboard_{v}", "bumpy dirt B: washboard, an even ripple every 0.7 m (a tone at speed/0.7)", mps,
        With(Rough, new P(32f, 0.8f, LumpLevel: 0.18f, LumpSpacing: 0.7f, LumpHz: 25f, OffRoad: true, Periodic: true)), 2f, [(7, Rough)]));
    renders.Add(new($"42_bumpy_C_ruts_{v}", "bumpy dirt C: big ruts, 22 Hz lumps of 0.35 every 4 m", mps,
        With(Rough, new P(32f, 0.8f, LumpLevel: 0.35f, LumpSpacing: 4f, LumpHz: 22f, OffRoad: true)), 2f, [(7, Rough)]));
    // the crossing knock alone (no road bed), a pair of wheels then the other pair, every 1.5 s
    renders.Add(new($"50_bump_alone_{v}", "transition knock alone: front pair, rear pair 3 m later, every 1.5 s", mps, rec, 2f, [(7, 0)], KnockEvery: 1.5f, Rec: true));
    // pavement -> dirt -> pavement
    renders.Add(new($"60_seq_current_{v}", "sequence asphalt 2.5 s, sand 2.5 s, asphalt 2.5 s: current", mps, cur, 1f, [(2.5f, Asphalt), (2.5f, Sand), (2.5f, Asphalt)], Knocks: false));
    renders.Add(new($"61_seq_rec_{v}", "sequence (RECOMMENDED): profiles, contrast 2, the knocks", mps, rec, 2f, [(2.5f, Asphalt), (2.5f, Sand), (2.5f, Asphalt)], Rec: true));
    renders.Add(new($"62_seq_rec_noknock_{v}", "sequence: profiles, contrast 2, no knocks", mps, rec, 2f, [(2.5f, Asphalt), (2.5f, Sand), (2.5f, Asphalt)], Knocks: false));
    renders.Add(new($"63_seq_contrast3_{v}", "sequence: profiles, contrast 3, the knocks", mps, rec, 3f, [(2.5f, Asphalt), (2.5f, Sand), (2.5f, Asphalt)]));
    renders.Add(new($"64_seq_gravel_{v}", "sequence asphalt, dirt road, rough, dirt road, asphalt (1.5 s each): profiles, knocks", mps, rec, 2f,
        [(1.5f, Asphalt), (1.5f, DirtRoad), (1.5f, Rough), (1.5f, DirtRoad), (1.5f, Asphalt)], Rec: true));
    renders.Add(new($"65_seq_rec_engine_{v}", "sequence (RECOMMENDED) with the engine at 3000 rpm under it, as in the game", mps, rec, 2f,
        [(2.5f, Asphalt), (2.5f, Sand), (2.5f, Asphalt)], Rpm: 3000, Rec: true));
}

// render, then one gain for the whole set (relative levels as in the game) and one per file (matched RMS, for character)
var made = renders.Select(r => (r, x: r.Run())).ToList();
float setPeak = made.Max(m => Peak(m.x));
float setGain = 0.891f / setPeak;     // the loudest file at -1 dBFS
var readme = new StringBuilder();
readme.AppendLine("# Bass-shaker surface options");
readme.AppendLine();
readme.AppendLine("**For a bass shaker or a subwoofer, not headphones at volume.** Everything here is under 90 Hz; on headphones you hear little");
readme.AppendLine("until it is loud enough to be uncomfortable, and the knocks have a fast start. Start quiet.");
readme.AppendLine();
readme.AppendLine("Rendered with the game's own synth (`tools/TactileRender`, `src/I76.Formats/Input/TactileSynth.cs`): 48 kHz mono 16-bit,");
readme.AppendLine("7.5 s each, at about 15 m/s (34 mph) and 30 m/s (67 mph), the LFE Level at 100 %, every source at 100 %, crossover 90 Hz, the");
readme.AppendLine("engine off (but in 65), the springs still (the game adds the springs' load and jolts on top).");
readme.AppendLine();
readme.AppendLine($"**Levels.** The top folder keeps the game's relative levels: one gain for the whole set ({20 * MathF.Log10(setGain):+0.0;-0.0} dB) puts the");
readme.AppendLine("loudest file's peak at -1 dBFS, so a quiet file is quiet in the game too. `matched/` has the same files each brought to");
readme.AppendLine("-20 dBFS RMS (peaks held under -1 dBFS), to compare pitch and texture at one loudness. The RMS and peak columns are the top folder's.");
readme.AppendLine();
readme.AppendLine("Surfaces: \"pavement\" is asphalt (id 2; the parking lot is the same), \"gravel road\" the dirt road (id 3; dirt junctions the same),");
readme.AppendLine("\"off-road\" sand (id 1, most trip maps) and dirt (id 0), \"bumpy dirt\" the rough ground (id 5, the gravel rolling sound).");
readme.AppendLine("Contrast is the stretch of the force model's road strengths: 1 as before, 2 recommended (asphalt 15 dB under sand: the roads softer and the ground harder about a dirt junction's strength), 3 more.");
readme.AppendLine();
readme.AppendLine("**Recommended set** (marked R): 10, 21, 30, 31, 40, 50, 61, 64, 65; it is what the branch now plays.");
readme.AppendLine();
readme.AppendLine("| | File | What it is | Speed | Contrast | RMS dBFS | Peak dBFS |");
readme.AppendLine("|---|---|---|---|---|---|---|");
foreach (var (r, x) in made)
{
    var top = x.Select(s => s * setGain).ToArray();
    float rms = Rms(top), pk = Peak(top);
    float mg = rms > 0 ? MathF.Min(0.1f / rms, 0.891f / pk) : 1f;
    Wav(Path.Combine(outDir, r.Name + ".wav"), top);
    Wav(Path.Combine(outDir, "matched", r.Name + ".wav"), top.Select(s => s * mg).ToArray());
    readme.AppendLine($"| {(r.Rec ? "R" : "")} | `{r.Name}.wav` | {r.What} | {r.Mps:0} m/s | {r.Contrast:0} | {Db(rms)} | {Db(pk)} |");
}
readme.AppendLine();
readme.AppendLine("Profiles (band centre / Q; lumps level, mean spacing, Hz; grit level, mean spacing, Hz; levels at full speed, before the LFE gain):");
readme.AppendLine();
readme.AppendLine("| Surface | Band | Lumps | Grit |");
readme.AppendLine("|---|---|---|---|");
foreach (var (name, i) in new[] { ("dirt", Dirt), ("sand", Sand), ("asphalt", Asphalt), ("dirt road", DirtRoad), ("planks", 5), ("rough", Rough) })
{
    var p = rec[i];
    readme.AppendLine($"| {name} | {p.BedHz:0} Hz / {p.BedQ:0.0} | {(p.LumpLevel > 0 ? $"{p.LumpLevel:0.00}, {p.LumpSpacing:0.0#} m, {p.LumpHz:0} Hz" : "none")} | {(p.GrainLevel > 0 ? $"{p.GrainLevel:0.00}, {p.GrainSpacing:0.0#} m, {p.GrainHz:0} Hz" : "none")} |");
}
readme.AppendLine();
readme.AppendLine($"The knock: {TactileSynth.BumpHz:0} Hz, {TactileSynth.BumpLevel:0.00} at full strength and 50 mph or more, {TactileSynth.BumpSeconds * 1000:0} ms; per wheel, the front pair at the edge and the rear pair {Wheelbase:0} m later; strength 1 asphalt or a bridge against the ground, 0.5 a dirt road against the ground, 0.35 asphalt against a dirt road.");
File.WriteAllText(Path.Combine(outDir, "README.md"), readme.ToString());
Console.WriteLine($"{made.Count} files in {Path.GetFullPath(outDir)}, set gain {20 * MathF.Log10(setGain):0.0} dB");

static string Db(float a) => a > 0 ? $"{20 * MathF.Log10(a):0.0}" : "-inf";
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

/// <summary>One file: segments of (seconds, synth surface) at a speed; knocks at each change of surface (the front pair, then the rear 3 m on), or every KnockEvery seconds.</summary>
record Render(string Name, string What, float Mps, P[] Profiles, float Contrast, (float Seconds, int Surface)[] Segments,
    bool Knocks = true, float KnockEvery = 0, float Rpm = 0, bool Rec = false)
{
    public float[] Run()
    {
        var s = new TactileSynth { Gain = AudioOutputs.ShakerGain(100), Surfaces = Profiles, Contrast = Contrast, Tune = TactileTuning.Before };
        var e = new TactileEvents();
        float mph = Mps * 2.2369363f, total = Segments.Sum(g => g.Seconds);
        int perTick = (int)MathF.Round(48000 * 102f / 2048f);
        var outp = new List<float>();
        var block = new float[perTick];
        // when each axle crosses: the front at the segment's start, the rear a wheelbase later
        var events = new List<(float At, float Strength)>();
        float t0 = 0;
        for (int i = 1; i < Segments.Length; i++)
        {
            t0 += Segments[i - 1].Seconds;
            float st = WheelCrossings.Strength(Kind(Segments[i - 1].Surface), Kind(Segments[i].Surface));
            if (Knocks && st > 0) { events.Add((t0, st)); events.Add((t0 + 3f / Mps, st)); }
        }
        if (KnockEvery > 0)
            for (float t = 0.5f; t < total; t += KnockEvery) { events.Add((t, 1f)); events.Add((t + 3f / Mps, 1f)); }
        for (float t = 0; t < total; t += 102f / 2048f)
        {
            e.Clear();
            float acc = 0; int surface = Segments[^1].Surface;
            foreach (var g in Segments) { acc += g.Seconds; if (t < acc) { surface = g.Surface; break; } }
            e.Surface = surface;
            e.Mph = mph;
            if (Rpm > 0) { e.EngineOn = true; e.Rpm = Rpm; }
            e.TickSeconds = 102f / 2048f;
            // each axle is two wheels: two knocks in the tick it crosses
            foreach (var (at, st) in events) if (at >= t && at < t + 102f / 2048f) { e.Crossings.Add(st); e.Crossings.Add(st); }
            s.Step(e);
            s.Fill(block, 48000);
            outp.AddRange(block);
        }
        return [.. outp];
    }

    private static GroundKind Kind(int synthSurface) => synthSurface <= 0 ? GroundKind.None : WheelCrossings.OfSurface(synthSurface - 1);
}
