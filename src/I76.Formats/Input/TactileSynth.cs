namespace I76.Formats.Input;

/// <summary>
/// Audio Control's Output ([port] output): what the port does with the device the system gives it (Godot takes the device's
/// channel count; the game cannot choose it). Stereo and Headphones play as the original does (nothing headphone-specific: they
/// are the same); 2.1 adds the bass-shaker signal and a boost of the effects' bass under the crossover to both channels, for a
/// sub or shaker fed from the stereo output; 5.1 and 7.1 put the shaker signal on the LFE channel alone (the same path: the LFE
/// of whatever surround layout the device has; nothing on a stereo device).
/// </summary>
public enum AudioOutput { Stereo, Headphones, Sub21, Surround51, Surround71 }

public static class AudioOutputs
{
    /// <summary>The rows' values: Headphones is "Phones", the word being wider than the pads' value column.</summary>
    public static readonly string[] Names = ["Stereo", "Phones", "2.1", "5.1", "7.1"];
    private static readonly string[] Keys = ["stereo", "headphones", "2.1", "5.1", "7.1"];

    public static string Key(AudioOutput o) => Keys[(int)o];

    /// <summary>A [port] output value; anything else is Stereo.</summary>
    public static AudioOutput Parse(string? key)
    {
        int i = Array.IndexOf(Keys, key?.Trim().ToLowerInvariant());
        return i < 0 ? AudioOutput.Stereo : (AudioOutput)i;
    }

    /// <summary>The shaker signal plays: on both channels (2.1) or on the LFE (5.1, 7.1).</summary>
    public static bool Tactile(AudioOutput o) => o is AudioOutput.Sub21 or AudioOutput.Surround51 or AudioOutput.Surround71;

    /// <summary>Audio Control's LFE Level: 0 to 200 % in steps of 25, 100 the default.</summary>
    public static readonly int[] Levels = [0, 25, 50, 75, 100, 125, 150, 175, 200];
    public static readonly string[] LevelNames = Levels.Select(l => $"{l} %").ToArray();
    public const int DefaultLevel = 100;
    public static int ClampLevel(int percent) => Levels.MinBy(l => Math.Abs(l - percent));

    /// <summary>[port] crossover: the top of the shaker signal and of 2.1's bass boost, 40 to 160 Hz (90 by default).</summary>
    public const float DefaultCrossover = 90f;
    public static float ClampCrossover(float hz) => float.IsFinite(hz) ? Math.Clamp(hz, 40f, 160f) : DefaultCrossover;

    /// <summary>2.1's boost of the effects under the crossover, as an amplitude factor: the LFE level's share added once more (100 %: twice, +6 dB).</summary>
    public static float BassBoost(int percent) => 1f + Math.Clamp(percent, 0, 200) / 100f;
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
    public float Rpm, Mph;
    /// <summary>The surface as the force block has it: the car's + 1 while moving, 0 standing, 9 in the air.</summary>
    public int Surface;
    public int FlatTyres;
    public readonly List<WeaponShot> Shots = [];
    /// <summary>The player's damage packets: channel (0 ballistic, 1 explosive, 2 fire, 3 impact) and amount.</summary>
    public readonly List<(int Channel, int Amount)> Hits = [];
    /// <summary>Explosions this tick, by their distance from the car (m).</summary>
    public readonly List<float> Blasts = [];

    public void Clear()
    {
        EngineOn = Cranking = false;
        Rpm = Mph = 0;
        Surface = FlatTyres = 0;
        Shots.Clear();
        Hits.Clear();
        Blasts.Clear();
    }
}

/// <summary>
/// The port's bass-shaker signal (the original has none): sines and a noise bed between 20 and 90 Hz. Continuous: the engine,
/// a sine from 25 Hz at idle to 45 Hz at 5000 rpm (the starter a 24 Hz shudder); the road, noise around 50 Hz by surface (the
/// force model's road strengths) and speed, rougher with a flat tyre. Transients: a decaying sine per shot by weapon class, per
/// hit (at the original force-feedback DLL's strengths) and per explosion within <see cref="BlastRange"/>, each through the
/// shaker's <see cref="Eq"/> at its frequency. Then a 15 Hz high-pass, a low-pass at <see cref="LowPassHz"/> (90 Hz) and a soft
/// limiter under <see cref="Ceiling"/>. Presentation only: its parameters are set once a sim tick, its samples made a block at a time.
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
    /// <summary>The road bed's centre.</summary>
    public const float RoadHz = 50f;
    private const int MaxThumps = 8;

    /// <summary>A decaying sine: <see cref="Level"/> at its start, -40 dB after <see cref="Seconds"/>, then gone.</summary>
    public readonly record struct Thump(float Hz, float Level, float Seconds);

    private static float RpmShare(float rpm) => Math.Clamp((rpm - 600f) / 4400f, 0f, 1f);
    public static float EngineHz(float rpm) => 25f + 20f * RpmShare(rpm);
    public static float EngineLevel(bool on, float rpm) => on ? 0.08f + 0.10f * RpmShare(rpm) : 0f;

    /// <summary>The road bed: none standing or in the air, up to 0.3 on the roughest surface at 80 mph and over; each flat tyre adds a third.</summary>
    public static float RoadLevel(int surface, float mph, int flats)
    {
        if (surface <= 0 || surface >= ForceModel.RoadGain.Length) return 0f;
        float l = 0.3f * ForceModel.RoadGain[surface] / 40f * Math.Clamp(mph / 80f, 0f, 1f);
        return l * (1f + Math.Clamp(flats, 0, 4) / 3f);
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
    private readonly List<Voice> _thumps = [];
    private float _engHz = EngineHz(0), _engTarget, _roadTarget, _eng, _road;
    private double _engPhase;
    private uint _noise = 0x9E3779B9u;
    private Biquad _roadBand, _hp, _lp;
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

    /// <summary>The largest sample of the last <see cref="Fill"/> (after the limiter).</summary>
    public float Peak { get; private set; }

    /// <summary>One sim tick: the continuous sources' targets and this tick's transients.</summary>
    public void Step(TactileEvents e)
    {
        _engHz = e.Cranking ? CrankHz : EngineHz(e.Rpm);
        _engTarget = (e.Cranking ? CrankLevel : EngineLevel(e.EngineOn, e.Rpm)) * EqGain(_engHz);
        _roadTarget = RoadLevel(e.Surface, e.Mph, e.FlatTyres);
        foreach (var s in e.Shots) if (ShotThump(s.Class) is { } t) Add(t);
        foreach (var (ch, amount) in e.Hits) if (HitThump(ch, amount) is { } t) Add(t);
        foreach (var d in e.Blasts) if (BlastThump(d) is { } t) Add(t);
    }

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
        _thumps.Clear();
    }

    /// <summary>The next <paramref name="dst"/>.Length samples at <paramref name="rate"/> Hz.</summary>
    public void Fill(Span<float> dst, int rate)
    {
        if (rate != _rate)
        {
            _rate = rate;
            _roadBand = Biquad.BandPass(RoadHz, 0.7f, rate);
            _hp = Biquad.HighPass(LowHz, rate);
            _lp = Biquad.LowPass(_lowPassHz, rate);
            _roadEq = _eq is null ? [] : RoadEq(_eq, rate);
        }
        float dt = 1f / rate;
        float glide = 1f - MathF.Exp(-dt / 0.03f);   // 30 ms: a tick's step without a click
        float peak = 0;
        for (int i = 0; i < dst.Length; i++)
        {
            _eng += (_engTarget - _eng) * glide;
            _road += (_roadTarget - _road) * glide;
            _engPhase += 2 * Math.PI * _engHz * dt;
            if (_engPhase > 2 * Math.PI) _engPhase -= 2 * Math.PI;
            float x = _eng * (float)Math.Sin(_engPhase);
            _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5;
            // the filters leave white noise of ±1 at about 0.025 rms: ×28 is the 0.71 rms of a sine at the bed's level
            float bed = _roadBand.Run(_noise / 2147483648f - 1f);
            for (int k = 0; k < _roadEq.Length; k++) bed = _roadEq[k].Run(bed);
            x += _road * 28f * bed;
            for (int v = _thumps.Count - 1; v >= 0; v--)
            {
                var t = _thumps[v];
                x += t.Level * (float)Math.Sin(t.Phase);
                t.Phase += 2 * Math.PI * t.Hz * dt;
                t.Level *= MathF.Exp(-t.Fall * dt);
                if ((t.Left -= dt) <= 0) _thumps.RemoveAt(v);
            }
            float y = Ceiling * MathF.Tanh(_lp.Run(_hp.Run(x)) / Ceiling);
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
