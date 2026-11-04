namespace I76.Formats.Input;

/// <summary>
/// Audio Control's Output ([port] output): what the port does with the device the system gives it (Godot takes the device's
/// channel count; the game cannot choose it). With the LFE Level at 0 (the default) every output plays as the original does.
/// Above it, Stereo and Headphones (the same: nothing headphone-specific) mix the bass-shaker signal into both channels and boost
/// the effects' bass under the crossover, for a sub or shaker fed from the stereo output, or whatever a player's rig makes of
/// it; 5.1 and 7.1 put the shaker signal on the LFE channel alone (the same path: the LFE of whatever surround layout the device
/// has; nothing on a stereo device).
/// </summary>
public enum AudioOutput { Stereo, Headphones, Surround51, Surround71 }

public static class AudioOutputs
{
    /// <summary>The rows' values: Headphones is "Phones", the word being wider than the pads' value column.</summary>
    public static readonly string[] Names = ["Stereo", "Phones", "5.1", "7.1"];
    private static readonly string[] Keys = ["stereo", "headphones", "5.1", "7.1"];

    public static string Key(AudioOutput o) => Keys[(int)o];

    /// <summary>A [port] output value; anything else is Stereo. A build before this one had 2.1 for the stereo mix: Stereo now.</summary>
    public static AudioOutput Parse(string? key)
    {
        int i = Array.IndexOf(Keys, key?.Trim().ToLowerInvariant());
        return i < 0 ? AudioOutput.Stereo : (AudioOutput)i;
    }

    /// <summary>An output value that played the shaker signal while an absent lfe_level meant 100 % (2.1, 5.1, 7.1): such a file keeps 100.</summary>
    public static bool HadShaker(string? key) => key?.Trim().ToLowerInvariant() is "2.1" or "5.1" or "7.1";

    /// <summary>The shaker signal on the LFE channel alone (5.1, 7.1), not mixed into both channels.</summary>
    public static bool Discrete(AudioOutput o) => o is AudioOutput.Surround51 or AudioOutput.Surround71;

    /// <summary>The shaker signal plays: any output with the LFE Level above 0.</summary>
    public static bool Tactile(int levelPercent) => levelPercent > 0;

    /// <summary>Audio Control's LFE Level: 0 to 200 % in steps of 25, 0 (none) the default.</summary>
    public static readonly int[] Levels = [0, 25, 50, 75, 100, 125, 150, 175, 200];
    public static readonly string[] LevelNames = Levels.Select(l => $"{l} %").ToArray();
    public const int DefaultLevel = 0;
    /// <summary>The level an older file that chose a shaker output had without the key.</summary>
    public const int OldDefaultLevel = 100;
    public static int ClampLevel(int percent) => Levels.MinBy(l => Math.Abs(l - percent));

    /// <summary>[port] crossover: the top of the shaker signal and of the stereo mix's bass boost, 40 to 160 Hz (90 by default).</summary>
    public const float DefaultCrossover = 90f;
    public static float ClampCrossover(float hz) => float.IsFinite(hz) ? Math.Clamp(hz, 40f, 160f) : DefaultCrossover;

    /// <summary>
    /// The shaker signal's gain at an LFE Level, before the synth's limiter: 1.5 at 100 % (a playtest on a 2.1 rig found the
    /// signal at 1.0 too faint, and 200 % of that not too much), 3 at 200 %. Inside the limiter, so no level clips or reaches
    /// the Master bus's limiter on the shaker's own.
    /// </summary>
    public static float ShakerGain(int percent) => 1.5f * Math.Clamp(percent, 0, 200) / 100f;

    /// <summary>The stereo mix's boost of the effects under the crossover, as an amplitude factor: the LFE level's share added once more (100 %: twice, +6 dB).</summary>
    public static float BassBoost(int percent) => 1f + Math.Clamp(percent, 0, 200) / 100f;
}

/// <summary>
/// The shaker signal's sources, each with a level of its own ([port] lfe_engine ... lfe_bumps, 0 to 200 %, 100 by default).
/// Off-road is a share of the road's on off-road ground (dirt, sand, rough), on top of lfe_road; Bumps the knock of a wheel
/// crossing onto or off a road or a bridge.
/// </summary>
public enum TactileSource { Engine, Road, Hits, Shots, Explosions, Offroad, Bumps }

public static class TactileSources
{
    public static readonly string[] Keys = ["lfe_engine", "lfe_road", "lfe_hits", "lfe_shots", "lfe_explosions", "lfe_offroad", "lfe_bumps"];
    public const int Default = 100, Max = 200;
    public static int Clamp(int percent) => Math.Clamp(percent, 0, Max);
    /// <summary>Every source at 100 %.</summary>
    public static int[] Defaults() => [.. Keys.Select(_ => Default)];
}

/// <summary>
/// [port] tactile_eq: a shaker's correction curve, "hz:db" points ("20:3.6, 35:-3.5, 60:0"), interpolated in log frequency
/// (feel is ratio-based: 20 to 25 Hz is a bigger step than 60 to 65), held flat past the ends, each point within ±12 dB.
/// Applied to the shaker signal alone: each of the synth's tones at its own frequency, the road bed's noise through a filter bank.
/// </summary>
public sealed class TactileEq
{
    public const float MaxDb = 12f;
    private readonly float[] _hz, _db;

    public TactileEq(IEnumerable<(float Hz, float Db)> points)
    {
        var p = points.Where(q => q.Hz > 0 && float.IsFinite(q.Hz) && float.IsFinite(q.Db)).OrderBy(q => q.Hz).ToArray();
        _hz = p.Select(q => q.Hz).ToArray();
        _db = p.Select(q => Math.Clamp(q.Db, -MaxDb, MaxDb)).ToArray();
    }

    public int Count => _hz.Length;

    /// <summary>The curve from its config text, or null when it has no point (empty, "off" or unreadable).</summary>
    public static TactileEq? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var num = System.Globalization.NumberStyles.Float;
        var pts = new List<(float, float)>();
        foreach (var part in text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split(':', StringSplitOptions.TrimEntries);
            if (kv.Length == 2 && float.TryParse(kv[0], num, inv, out float hz) && float.TryParse(kv[1], num, inv, out float db))
                pts.Add((hz, db));
        }
        var eq = new TactileEq(pts);
        return eq.Count == 0 ? null : eq;
    }

    /// <summary>The curve's gain at <paramref name="hz"/>, in dB.</summary>
    public float Db(float hz)
    {
        if (_hz.Length == 0) return 0f;
        if (!(hz > _hz[0])) return _db[0];
        if (hz >= _hz[^1]) return _db[^1];
        int k = 0;
        while (_hz[k + 1] < hz) k++;
        float u = MathF.Log(hz / _hz[k]) / MathF.Log(_hz[k + 1] / _hz[k]);
        return _db[k] + (_db[k + 1] - _db[k]) * u;
    }

    /// <summary>The curve's gain at <paramref name="hz"/>, as an amplitude factor.</summary>
    public float Gain(float hz) => MathF.Pow(10f, Db(hz) / 20f);
}

/// <summary>What <see cref="TactileSynth"/> reads each tick from the player's car.</summary>
public sealed class TactileEvents
{
    public bool EngineOn, Cranking;
    /// <summary>The engine's rpm, and the car's own speed (mph, from its velocity: the engine need not run).</summary>
    public float Rpm, Mph;
    /// <summary>The surface: the car's + 1 on the ground, 0 for none, 9 in the air.</summary>
    public int Surface;
    public int FlatTyres;
    /// <summary>Each corner wheel's suspension travel over its limit (−1 to 1, 0 at rest): the springs' load, cornering and braking included.</summary>
    public readonly float[] Travel = new float[4];
    /// <summary>The sim tick's length, seconds: the travel's rate is per second whatever the rate setting.</summary>
    public float TickSeconds = 102f / 2048f;
    public readonly List<WeaponShot> Shots = [];
    /// <summary>The player's damage packets: channel (0 ballistic, 1 explosive, 2 fire, 3 impact) and amount.</summary>
    public readonly List<(int Channel, int Amount)> Hits = [];
    /// <summary>Explosions this tick, by their distance from the car (m).</summary>
    public readonly List<float> Blasts = [];
    /// <summary>Wheels crossing onto or off a road or a bridge this tick, each by its strength (0 to 1, <see cref="TactileSynth.BumpThump"/>).</summary>
    public readonly List<float> Crossings = [];

    public void Clear()
    {
        EngineOn = Cranking = false;
        Rpm = Mph = 0;
        Surface = FlatTyres = 0;
        Array.Clear(Travel);
        Shots.Clear();
        Hits.Clear();
        Blasts.Clear();
        Crossings.Clear();
    }
}

/// <summary>
/// The port's bass-shaker signal (the original has none): sines and a noise bed between 20 and 90 Hz. Continuous: the engine,
/// a sine from 25 Hz at idle to 45 Hz at 5000 rpm (the starter a 24 Hz shudder); the road, a noise band by surface (its
/// <see cref="SurfaceProfile"/>: low and lumpy off-road, higher and gritty on a dirt road, a faint smooth hum on asphalt) at the
/// force model's road strengths stretched by <see cref="Contrast"/>, by the car's speed, rougher with a flat tyre, stronger with
/// the springs' load (a turn, a stop) and their movement (bumps), engine or no engine; the surface's lumps and grit as short
/// decaying sines spaced by the distance driven; a knock as a wheel crosses onto or off a road or a bridge. Transients: a decaying sine per shot by weapon class, per
/// hit (at the original force-feedback DLL's strengths) and per explosion within <see cref="BlastRange"/>, each through the
/// shaker's <see cref="Eq"/> at its frequency. Each source takes its <see cref="Weights"/> share. Then a 15 Hz high-pass, a low-pass at <see cref="LowPassHz"/> (90 Hz), the LFE
/// Level's <see cref="Gain"/> and a soft limiter under <see cref="Ceiling"/>. Presentation only: its parameters are set once a sim tick, its samples made a block at a time.
/// </summary>
public sealed class TactileSynth
{
    /// <summary>The filters' corners: the sources sit from 24 to 60 Hz; the high-pass at 15 Hz costs a 24 Hz one under 1 dB.</summary>
    public const float LowHz = 15f, HighHz = 90f;
    /// <summary>The limiter's top: under the Master bus's -1 dB limiter, so that one never acts on the shaker.</summary>
    public const float Ceiling = 0.85f;
    /// <summary>
    /// The explosions' falloff: full within <see cref="BlastNear"/>; then the air blast's 1/r (spherical spreading) to
    /// <see cref="BlastKnee"/>; past it the ground's 1/sqrt(r) (a wave along the surface spreads over a ring, not a sphere) out
    /// to <see cref="BlastRange"/>, where it is 30 dB under a blast at the car: faint, but over a quiet engine still felt. No
    /// terrain cut: a 20 Hz wave is 17 m long and bends round hills, and the ground carries it under them.
    /// </summary>
    public const float BlastNear = 15f, BlastKnee = 120f, BlastRange = 2000f;
    /// <summary>A blast at the car: 25 Hz, 0.5 s; at <see cref="BlastRange"/> a lower, longer roll (the ground and air lose the top first).</summary>
    public const float BlastHz = 25f, BlastFarHz = 18f, BlastSeconds = 0.5f, BlastFarSeconds = 1.2f;
    public const float CrankHz = 24f, CrankLevel = 0.2f;
    /// <summary>The road bed's centre before the surfaces had their own (<see cref="Flat"/>), and the band its level is calibrated on.</summary>
    public const float RoadHz = 50f, RoadQ = 0.7f;
    private const int MaxThumps = 8, MaxGrains = 12;

    /// <summary>
    /// A surface's feel: the road bed's band (centre and Q), its lumps (a decaying sine at <see cref="LumpHz"/> every
    /// <see cref="LumpSpacing"/> metres on average, the stones and ruts the tyre cannot swallow) and its grit (short sines at
    /// <see cref="GrainHz"/> every <see cref="GrainSpacing"/> metres: loose aggregate). Levels are at full speed; each spacing
    /// is jittered from half to one and a half times, so the rate follows the speed without a tone (unless
    /// <see cref="Periodic"/>: a washboard's even ripples, which are a tone). <see cref="OffRoad"/> surfaces take lfe_offroad.
    /// </summary>
    public readonly record struct SurfaceProfile(float BedHz, float BedQ, float LumpLevel = 0, float LumpSpacing = 0, float LumpHz = 26f,
        float GrainLevel = 0, float GrainSpacing = 0, float GrainHz = 75f, bool OffRoad = false, bool Periodic = false);

    /// <summary>The surfaces' names for the Developer panel, by the synth's surface (the car's + 1; 0 none, 9 in the air).</summary>
    public static readonly string[] SurfaceNames = ["none", "dirt", "sand", "asphalt", "dirt road", "planks", "rough", "parking lot", "dirt junction", "air"];

    private static readonly SurfaceProfile Asphalt = new(56f, 1.6f);
    private static readonly SurfaceProfile DirtRoad = new(66f, 1.2f, LumpLevel: 0.05f, LumpSpacing: 4f, LumpHz: 30f, GrainLevel: 0.07f, GrainSpacing: 0.45f, GrainHz: 76f);

    /// <summary>
    /// The port's surfaces (playtest: a dirt road above the off-road ground in pitch, roads and off-road further apart). Off-road
    /// (dirt, sand, rough) sits low, 30 to 34 Hz, with lumps; a dirt road (the gravel road) at 66 Hz with grit; asphalt a narrow
    /// faint 56 Hz hum and nothing else; planks a hollow 46 Hz with the beams' knock. docs/lfe-surfaces.md has the reasoning.
    /// </summary>
    public static readonly SurfaceProfile[] Profiles =
    [
        new(RoadHz, RoadQ),
        new(34f, 0.9f, LumpLevel: 0.14f, LumpSpacing: 2.5f, LumpHz: 26f, OffRoad: true),                                         // dirt
        new(30f, 0.8f, LumpLevel: 0.08f, LumpSpacing: 3.5f, LumpHz: 24f, OffRoad: true),                                         // sand
        Asphalt,
        DirtRoad,
        new(46f, 2.5f, LumpLevel: 0.08f, LumpSpacing: 1.2f, LumpHz: 40f),                                                        // planks
        new(32f, 0.8f, LumpLevel: 0.22f, LumpSpacing: 1.4f, LumpHz: 25f, GrainLevel: 0.05f, GrainSpacing: 0.6f, GrainHz: 70f, OffRoad: true), // rough
        Asphalt,                                                                                                                  // parking lot
        DirtRoad,                                                                                                                 // dirt junction
        new(RoadHz, RoadQ),
    ];

    /// <summary>Every surface as before the profiles: one 50 Hz band, no lumps or grit (with <see cref="Contrast"/> 1, the earlier signal).</summary>
    public static readonly SurfaceProfile[] Flat = [.. Enumerable.Repeat(new SurfaceProfile(RoadHz, RoadQ), 10)];

    /// <summary>
    /// The road strengths' stretch: the bed goes as strength/40 times (strength/<see cref="ContrastPivot"/>)^(contrast - 1), so 2
    /// puts asphalt (15) 15 dB under sand (35) where the force model's straight line (1) had 7: the roads softer (asphalt -6 dB,
    /// a dirt road -3.5), the ground harder (sand +1.3, rough +2.5), a dirt junction (30) as it was.
    /// </summary>
    public const float DefaultContrast = 2f, ContrastPivot = 30f;

    /// <summary>A decaying sine: <see cref="Level"/> at its start, -40 dB after <see cref="Seconds"/>, then gone.</summary>
    public readonly record struct Thump(float Hz, float Level, float Seconds);

    private static float RpmShare(float rpm) => Math.Clamp((rpm - 600f) / 4400f, 0f, 1f);
    public static float EngineHz(float rpm) => 25f + 20f * RpmShare(rpm);
    public static float EngineLevel(bool on, float rpm) => on ? 0.08f + 0.10f * RpmShare(rpm) : 0f;

    /// <summary>
    /// The road bed: none standing or in the air, at 80 mph and over 0.3 · strength/40 (the force model's road strengths) stretched
    /// by <paramref name="contrast"/> about <see cref="ContrastPivot"/> (rough ground 0.4, asphalt 0.056 at 2); each flat tyre adds a third.
    /// </summary>
    public static float RoadLevel(int surface, float mph, int flats, float contrast = DefaultContrast)
    {
        if (!OnGround(surface)) return 0f;
        float g = ForceModel.RoadGain[surface];
        float l = 0.3f * g / 40f * MathF.Pow(g / ContrastPivot, contrast - 1f) * SpeedShare(mph);
        return l * (1f + Math.Clamp(flats, 0, 4) / 3f);
    }

    private static float SpeedShare(float mph) => Math.Clamp(mph / 80f, 0f, 1f);

    /// <summary>A lump's or a grain's level from its profile's at the car's speed: a lump keeps 0.3 of it at walking pace, the grit falls with the bed.</summary>
    public static float LumpLevel(in SurfaceProfile p, float mph) => p.LumpLevel * (0.3f + 0.7f * SpeedShare(mph));
    public static float GrainLevel(in SurfaceProfile p, float mph) => p.GrainLevel * (0.2f + 0.8f * SpeedShare(mph));

    /// <summary>
    /// A wheel crossing onto or off a road or a bridge (<paramref name="strength"/> 1 for asphalt or a bridge against the
    /// ground, less between kinds of road): a 28 Hz knock of up to <see cref="BumpLevel"/> by the car's speed, full at 50 mph
    /// and over, nothing under 4 mph (a kerb's lip, a deck's joint, felt in the wheel's hop before the springs take it). A
    /// decaying sine from zero peaks at 0.57 of its level after the filters (measured, tools/TactileRender --sweep): at 0.6 a
    /// wheel's knock stands a little over the sand's rumble at speed, where 0.35 sat under it.
    /// </summary>
    public static Thump? BumpThump(float strength, float mph)
    {
        if (!(strength > 0) || !(mph > 4f)) return null;
        float share = Math.Clamp(mph / 50f, 0.15f, 1f);
        return new Thump(BumpHz, BumpLevel * Math.Min(strength, 1f) * share, BumpSeconds);
    }
    public const float BumpHz = 28f, BumpLevel = 0.6f, BumpSeconds = 0.15f;

    private static bool OnGround(int surface) => surface > 0 && surface < ForceModel.RoadGain.Length - 1;

    /// <summary>
    /// The springs' load on the road bed: the mean deflection of the four corners over their limit, 0 to 1. The vehicle model's
    /// weight transfer (the acceleration along and across the car) drives each spring, so a hard turn or a stop loads the bed:
    /// the bed grows by the load's share and gains <see cref="LoadGain"/> at full load, so a turn is felt on any surface.
    /// </summary>
    public const float LoadGain = 0.12f;
    /// <summary>The springs' movement on the road bed: the corners' mean travel rate, limits a second; <see cref="JoltGain"/> at <see cref="JoltFull"/> and over (a bump, a kerb, a landing).</summary>
    public const float JoltGain = 0.2f, JoltFull = 4f;
    /// <summary>The bed's top, everything together: under a lone hit's thump.</summary>
    public const float RoadMax = 0.5f;

    public static float LoadOf(ReadOnlySpan<float> travel)
    {
        float sum = 0;
        foreach (var t in travel) sum += Math.Min(Math.Abs(t), 1f);
        return travel.Length == 0 ? 0f : sum / travel.Length;
    }

    /// <summary>The road bed from the surface, the car's speed, flats, the springs' load (0 to 1) and their rate (limits a second).</summary>
    public static float RoadLevel(int surface, float mph, int flats, float load, float jolt, float contrast = DefaultContrast)
    {
        if (!OnGround(surface)) return 0f;
        load = Math.Clamp(load, 0f, 1f);
        float l = RoadLevel(surface, mph, flats, contrast) * (1f + load) + LoadGain * load + JoltGain * Math.Clamp(jolt / JoltFull, 0f, 1f);
        return Math.Min(l, RoadMax);
    }

    /// <summary>A shot by weapon class (1 machine guns, 2 cannons, 3 mortars, 4 rockets and missiles, 5 flame weapons, 6 droppers); none for any other.</summary>
    public static Thump? ShotThump(uint weaponClass) => weaponClass switch
    {
        1 => new Thump(55f, 0.25f, 0.06f),
        2 => new Thump(40f, 0.5f, 0.12f),
        3 => new Thump(30f, 0.6f, 0.15f),
        4 => new Thump(35f, 0.4f, 0.18f),
        5 => new Thump(60f, 0.15f, 0.10f),
        6 => new Thump(45f, 0.3f, 0.08f),
        _ => null,
    };

    /// <summary>A hit at the DLL's strengths, held to full: explosive 35 % + damage·2/3 %, ballistic 40 % + damage·2/3 %, impact 55 % + damage·9/16 %; fire none.</summary>
    public static Thump? HitThump(int channel, int amount) => channel switch
    {
        1 => new Thump(25f, Math.Clamp((35f + amount * 2f / 3f) / 100f, 0f, 1f), 0.6f),
        0 => new Thump(32f, 0.7f * Math.Clamp((40f + amount * 2f / 3f) / 100f, 0f, 1f), 0.15f),
        3 => new Thump(30f, 0.8f * Math.Clamp((55f + amount * 0.5625f) / 100f, 0f, 1f), 0.25f),
        _ => null,
    };

    /// <summary>An explosion seen from the car, by the falloff of <see cref="BlastRange"/>; none at or past it.</summary>
    public static Thump? BlastThump(float metres)
    {
        if (!(metres < BlastRange)) return null;
        float r = Math.Max(metres, BlastNear);
        float level = r <= BlastKnee ? BlastNear / r : BlastNear / BlastKnee * MathF.Sqrt(BlastKnee / r);
        // past the knee the roll lowers and lengthens with the log of the distance
        float far = r <= BlastKnee ? 0f : MathF.Log(r / BlastKnee) / MathF.Log(BlastRange / BlastKnee);
        return new Thump(BlastHz + (BlastFarHz - BlastHz) * far, 0.7f * level, BlastSeconds + (BlastFarSeconds - BlastSeconds) * far);
    }

    private sealed class Voice { public float Hz, Level, Fall, Left; public double Phase; }
    private readonly List<Voice> _thumps = [], _grains = [];
    private float _engHz = EngineHz(0), _engTarget, _roadTarget, _eng, _road;
    // the road bed in two slots: a change of surface starts the other on the new band and the old one fades
    private readonly Biquad[] _bands = new Biquad[2];
    private readonly float[] _slotHz = [RoadHz, RoadHz], _slotQ = [RoadQ, RoadQ], _slotMix = [1f, 0f];
    private int _slot, _surface;
    private float _mps, _lumpLevel, _grainLevel, _toLump, _toGrain;
    private SurfaceProfile _profile = Flat[0];
    private int _bumps;
    private readonly float[] _travel = new float[4];
    private bool _travelSeen;
    private double _engPhase;
    private uint _noise = 0x9E3779B9u;
    private Biquad _hp, _lp;
    private Biquad[] _roadEq = [];
    private TactileEq? _roadEqFor;
    /// <summary>The road bed's correction: a third-octave bank (ISO centres) whose gains are trimmed until each centre reads the curve.</summary>
    private static readonly float[] EqCentres = [16f, 20f, 25f, 31.5f, 40f, 50f, 63f, 80f, 100f, 125f];
    private int _rate;

    /// <summary>The shaker's correction curve ([port] tactile_eq), or none.</summary>
    public TactileEq? Eq { get => _eq; set { _eq = value; _rate = 0; } }
    private TactileEq? _eq;
    private float EqGain(float hz) => Eq?.Gain(hz) ?? 1f;

    /// <summary>The low-pass corner ([port] crossover, 90 Hz by default).</summary>
    public float LowPassHz
    {
        get => _lowPassHz;
        set { if (value != _lowPassHz) { _lowPassHz = value; _rate = 0; } }
    }
    private float _lowPassHz = HighHz;

    /// <summary>Each <see cref="TactileSource"/>'s weight (1 as made; [port] lfe_engine and the rest over 100), applied as a tick's events are read.</summary>
    public readonly float[] Weights = [.. TactileSources.Keys.Select(_ => 1f)];
    private float W(TactileSource s) => Weights[(int)s];

    /// <summary>The weights from the keys' percentages, in <see cref="TactileSource"/> order.</summary>
    public void SetWeights(IReadOnlyList<int> percent)
    {
        for (int i = 0; i < Weights.Length; i++) Weights[i] = i < percent.Count ? TactileSources.Clamp(percent[i]) / 100f : 1f;
    }

    /// <summary>The surfaces' feel (<see cref="Profiles"/>; <see cref="Flat"/> the earlier one), by the synth's surface.</summary>
    public SurfaceProfile[] Surfaces { get; set; } = Profiles;
    /// <summary>The road strengths' stretch (<see cref="DefaultContrast"/>; 1 the force model's straight line).</summary>
    public float Contrast { get; set; } = DefaultContrast;

    /// <summary>The surface of the last tick (the car's + 1), the bed's band now (Hz) and its level (before the LFE Level), for the Developer panel.</summary>
    public int Surface => _surface;
    public float BedHz => _slotHz[_slot];
    public float RoadNow => _road;
    /// <summary>The crossing knocks played since the synth was made.</summary>
    public int Bumps => _bumps;

    /// <summary>
    /// A signal of the caller's, a sample at a time, summed in before the filters, the gain and the limiter (not through the
    /// <see cref="Eq"/>, which acts on each source at its frequency): the chain's sweeps (tools/TactileRender --sweep). The game sets none.
    /// </summary>
    public Func<float>? TestInput { get; set; }

    /// <summary>The LFE Level's gain (<see cref="AudioOutputs.ShakerGain"/>), applied before the limiter.</summary>
    public float Gain { get; set; } = 1f;

    /// <summary>The largest sample of the last <see cref="Fill"/> (after the limiter).</summary>
    public float Peak { get; private set; }

    /// <summary>One sim tick: the continuous sources' targets and this tick's transients.</summary>
    public void Step(TactileEvents e)
    {
        _engHz = e.Cranking ? CrankHz : EngineHz(e.Rpm);
        _engTarget = (e.Cranking ? CrankLevel : EngineLevel(e.EngineOn, e.Rpm)) * EqGain(_engHz) * W(TactileSource.Engine);
        // the springs' rate from the last tick's travel; the first tick (or the first back on the ground) has none
        float jolt = 0;
        bool ground = OnGround(e.Surface);
        if (ground && _travelSeen && e.TickSeconds > 0)
        {
            for (int i = 0; i < 4; i++) jolt += Math.Abs(e.Travel[i] - _travel[i]);
            jolt /= 4 * e.TickSeconds;
        }
        Array.Copy(e.Travel, _travel, 4);
        _travelSeen = ground;
        _surface = e.Surface;
        var prof = (uint)e.Surface < (uint)Surfaces.Length ? Surfaces[e.Surface] : Flat[0];
        float road = W(TactileSource.Road) * (prof.OffRoad ? W(TactileSource.Offroad) : 1f);
        _roadTarget = RoadLevel(e.Surface, e.Mph, e.FlatTyres, LoadOf(e.Travel), jolt, Contrast) * road;
        if (ground && (prof.BedHz != _slotHz[_slot] || prof.BedQ != _slotQ[_slot])) NewBed(prof.BedHz, prof.BedQ);
        _profile = prof;
        _mps = ground ? e.Mph / 2.2369363f : 0f;
        _lumpLevel = ground ? LumpLevel(prof, e.Mph) * road : 0f;
        _grainLevel = ground ? GrainLevel(prof, e.Mph) * road : 0f;
        foreach (var c in e.Crossings) if (BumpThump(c, e.Mph) is { } t) { Add(t, W(TactileSource.Bumps)); _bumps++; }
        foreach (var s in e.Shots) if (ShotThump(s.Class) is { } t) Add(t, W(TactileSource.Shots));
        foreach (var (ch, amount) in e.Hits) if (HitThump(ch, amount) is { } t) Add(t, W(TactileSource.Hits));
        foreach (var d in e.Blasts) if (BlastThump(d) is { } t) Add(t, W(TactileSource.Explosions));
    }

    private void Add(Thump t, float weight) => Add(t with { Level = t.Level * weight });

    /// <summary>A transient, through the <see cref="Eq"/>; past <see cref="MaxThumps"/> playing, the quietest goes (a far blast before a near hit).</summary>
    public void Add(Thump t)
    {
        if (t.Level <= 0 || t.Seconds <= 0) return;
        float level = t.Level * EqGain(t.Hz);
        if (_thumps.Count >= MaxThumps)
        {
            int q = 0;
            for (int i = 1; i < _thumps.Count; i++) if (_thumps[i].Level < _thumps[q].Level) q = i;
            if (_thumps[q].Level >= level) return;
            _thumps.RemoveAt(q);
        }
        _thumps.Add(new Voice { Hz = t.Hz, Level = level, Fall = MathF.Log(100f) / t.Seconds, Left = t.Seconds });
    }

    /// <summary>Everything silent (the pause, a menu over the game): the transients stop, the beds fade out over 30 ms.</summary>
    public void Reset()
    {
        _engTarget = _roadTarget = 0;
        _lumpLevel = _grainLevel = _mps = 0;
        _thumps.Clear();
        _grains.Clear();
        _travelSeen = false;
    }

    /// <summary>The road bed onto a new band: the other slot, from rest, while the current one fades.</summary>
    private void NewBed(float hz, float q)
    {
        _slot ^= 1;
        _slotHz[_slot] = hz;
        _slotQ[_slot] = q;
        if (_rate > 0) _bands[_slot] = Biquad.BandPass(hz, q, _rate);
    }

    /// <summary>A band's gain onto the bed's level: the noise through a narrower or lower band is weaker, as the square root of its width.</summary>
    private static float BandNorm(float hz, float q) => 28f * MathF.Sqrt(RoadHz / RoadQ / (hz / q));

    /// <summary>The next spacing: the mean from half to one and a half times it, or the mean itself for a periodic surface.</summary>
    private float Spacing(float mean, bool periodic) => periodic ? mean : mean * (0.5f + Rand());

    /// <summary>The noise source's next value, 0 to 1.</summary>
    private float Rand()
    {
        _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
        return _noise / 4294967296f;
    }

    /// <summary>A lump or a grain: its own pool, so the road never pushes a hit's thump out; past <see cref="MaxGrains"/> the oldest goes.</summary>
    private void Grain(float hz, float level, float seconds)
    {
        if (level <= 0) return;
        level *= EqGain(hz);
        if (_grains.Count >= MaxGrains) _grains.RemoveAt(0);
        _grains.Add(new Voice { Hz = hz, Level = level, Fall = MathF.Log(100f) / seconds, Left = seconds });
    }

    /// <summary>The next <paramref name="dst"/>.Length samples at <paramref name="rate"/> Hz.</summary>
    public void Fill(Span<float> dst, int rate)
    {
        if (rate != _rate)
        {
            _rate = rate;
            for (int k = 0; k < 2; k++) _bands[k] = Biquad.BandPass(_slotHz[k], _slotQ[k], rate);
            _hp = Biquad.HighPass(LowHz, rate);
            _lp = Biquad.LowPass(_lowPassHz, rate);
            _roadEq = _eq is null ? [] : RoadEq(_eq, rate);
        }
        float dt = 1f / rate;
        float glide = 1f - MathF.Exp(-dt / 0.03f);   // 30 ms: a tick's step without a click
        float fade = 1f - MathF.Exp(-dt / 0.06f);    // 60 ms: one band into the next
        float peak = 0;
        for (int i = 0; i < dst.Length; i++)
        {
            _eng += (_engTarget - _eng) * glide;
            _road += (_roadTarget - _road) * glide;
            _slotMix[_slot] += (1f - _slotMix[_slot]) * fade;
            _slotMix[_slot ^ 1] -= _slotMix[_slot ^ 1] * fade;
            _engPhase += 2 * Math.PI * _engHz * dt;
            if (_engPhase > 2 * Math.PI) _engPhase -= 2 * Math.PI;
            float x = _eng * (float)Math.Sin(_engPhase);
            _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
            // the filters leave white noise of ±1 at about 0.025 rms through the 50 Hz band: ×28 is the 0.71 rms of a sine at the bed's level
            float white = _noise / 2147483648f - 1f, bed = 0;
            for (int k = 0; k < 2; k++)
            {
                float b = _bands[k].Run(white);
                if (_slotMix[k] > 1e-4f) bed += _slotMix[k] * BandNorm(_slotHz[k], _slotQ[k]) * b;
            }
            for (int k = 0; k < _roadEq.Length; k++) bed = _roadEq[k].Run(bed);
            x += _road * bed;
            // the lumps and the grit, by the distance driven
            if (_mps > 0.5f)
            {
                float step = _mps * dt;
                if (_lumpLevel > 0 && _profile.LumpSpacing > 0 && (_toLump -= step) <= 0)
                {
                    _toLump = Math.Max(_toLump, 0) + Spacing(_profile.LumpSpacing, _profile.Periodic);
                    float hz = _profile.Periodic ? _profile.LumpHz : _profile.LumpHz * (0.9f + 0.2f * Rand());
                    Grain(hz, _lumpLevel * (_profile.Periodic ? 1f : 0.6f + 0.4f * Rand()), 0.14f);
                }
                if (_grainLevel > 0 && _profile.GrainSpacing > 0 && (_toGrain -= step) <= 0)
                {
                    _toGrain = Math.Max(_toGrain, 0) + Spacing(_profile.GrainSpacing, false);
                    Grain(_profile.GrainHz * (0.88f + 0.24f * Rand()), _grainLevel * (0.5f + 0.5f * Rand()), 0.05f);
                }
            }
            for (int v = _grains.Count - 1; v >= 0; v--)
            {
                var t = _grains[v];
                x += t.Level * (float)Math.Sin(t.Phase);
                t.Phase += 2 * Math.PI * t.Hz * dt;
                t.Level *= MathF.Exp(-t.Fall * dt);
                if ((t.Left -= dt) <= 0) _grains.RemoveAt(v);
            }
            for (int v = _thumps.Count - 1; v >= 0; v--)
            {
                var t = _thumps[v];
                x += t.Level * (float)Math.Sin(t.Phase);
                t.Phase += 2 * Math.PI * t.Hz * dt;
                t.Level *= MathF.Exp(-t.Fall * dt);
                if ((t.Left -= dt) <= 0) _thumps.RemoveAt(v);
            }
            if (TestInput is { } probe) x += probe();
            float y = Ceiling * MathF.Tanh(Gain * _lp.Run(_hp.Run(x)) / Ceiling);
            dst[i] = y;
            peak = Math.Max(peak, Math.Abs(y));
        }
        Peak = peak;
    }

    /// <summary>
    /// The road bed's noise through <paramref name="eq"/>: the tones take the curve at their own frequency, a noise band needs a
    /// filter. Peaking sections at third-octave centres overlap, so their gains are trimmed four times by what the cascade reads
    /// at each centre; the result follows the curve within about half a dB at the centres.
    /// </summary>
    private static Biquad[] RoadEq(TactileEq eq, int rate)
    {
        int n = EqCentres.Length;
        var target = EqCentres.Select(eq.Db).ToArray();
        var gain = (float[])target.Clone();
        var bank = new Biquad[n];
        for (int pass = 0; pass < 5; pass++)
        {
            for (int i = 0; i < n; i++) bank[i] = Biquad.Peaking(EqCentres[i], 4.32f, gain[i], rate);
            if (pass == 4) break;
            for (int i = 0; i < n; i++)
            {
                float at = 0;
                foreach (var b in bank) at += b.Db(EqCentres[i], rate);
                gain[i] += target[i] - at;
            }
        }
        return bank;
    }

    /// <summary>A second-order section (the audio EQ cookbook's), direct form I.</summary>
    private struct Biquad
    {
        private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        private static Biquad Make(float b0, float b1, float b2, float a0, float a1, float a2) =>
            new() { _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0 };

        public static Biquad LowPass(float hz, int rate)
        {
            float w = 2 * MathF.PI * hz / rate, c = MathF.Cos(w), al = MathF.Sin(w) / (2 * 0.70710678f);
            return Make((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
        }

        public static Biquad HighPass(float hz, int rate)
        {
            float w = 2 * MathF.PI * hz / rate, c = MathF.Cos(w), al = MathF.Sin(w) / (2 * 0.70710678f);
            return Make((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
        }

        public static Biquad BandPass(float hz, float q, int rate)
        {
            float w = 2 * MathF.PI * hz / rate, c = MathF.Cos(w), al = MathF.Sin(w) / (2 * q);
            return Make(al, 0, -al, 1 + al, -2 * c, 1 - al);
        }

        public static Biquad Peaking(float hz, float q, float db, int rate)
        {
            float a = MathF.Pow(10f, db / 40f), w = 2 * MathF.PI * hz / rate, c = MathF.Cos(w), al = MathF.Sin(w) / (2 * q);
            return Make(1 + al * a, -2 * c, 1 - al * a, 1 + al / a, -2 * c, 1 - al / a);
        }

        /// <summary>The section's gain at <paramref name="hz"/>, dB.</summary>
        public readonly float Db(float hz, int rate)
        {
            double w = 2 * Math.PI * hz / rate;
            var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -w);
            var z2 = z1 * z1;
            var h = (_b0 + _b1 * z1 + _b2 * z2) / (1 + _a1 * z1 + _a2 * z2);
            return (float)(20 * Math.Log10(h.Magnitude));
        }

        public float Run(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
