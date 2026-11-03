namespace I76.Formats.Input;

/// <summary>shell.cfg [port] tactile: a bass shaker's signal on the LFE channel of a 3.1, 5.1 or 7.1 output (lfe), or none (off, the default).</summary>
public enum TactileMode { Off, Lfe }

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
/// hit (at the original force-feedback DLL's strengths) and per explosion within <see cref="BlastRange"/>. Then a 15 Hz
/// high-pass, a 90 Hz low-pass and a soft limiter under <see cref="Ceiling"/>. Presentation only: its parameters are set once
/// a sim tick, its samples made a block at a time.
/// </summary>
public sealed class TactileSynth
{
    /// <summary>The filters' corners: the sources sit from 24 to 60 Hz; the high-pass at 15 Hz costs a 24 Hz one under 1 dB.</summary>
    public const float LowHz = 15f, HighHz = 90f;
    /// <summary>The limiter's top: under the Master bus's -1 dB limiter, so that one never acts on the shaker.</summary>
    public const float Ceiling = 0.85f;
    public const float BlastRange = 150f;
    public const float CrankHz = 24f, CrankLevel = 0.2f;
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

    /// <summary>An explosion seen from the car: falling with the square of the distance's share of <see cref="BlastRange"/>, none past it.</summary>
    public static Thump? BlastThump(float metres)
    {
        if (!(metres < BlastRange)) return null;
        float t = 1f - Math.Max(metres, 0f) / BlastRange;
        return new Thump(25f, 0.7f * t * t, 0.5f);
    }

    private sealed class Voice { public float Hz, Level, Fall, Left; public double Phase; }
    private readonly List<Voice> _thumps = [];
    private float _engHz = EngineHz(0), _engTarget, _roadTarget, _eng, _road;
    private double _engPhase;
    private uint _noise = 0x9E3779B9u;
    private Biquad _roadBand, _hp, _lp;
    private int _rate;

    /// <summary>The largest sample of the last <see cref="Fill"/> (after the limiter).</summary>
    public float Peak { get; private set; }

    /// <summary>One sim tick: the continuous sources' targets and this tick's transients.</summary>
    public void Step(TactileEvents e)
    {
        _engHz = e.Cranking ? CrankHz : EngineHz(e.Rpm);
        _engTarget = e.Cranking ? CrankLevel : EngineLevel(e.EngineOn, e.Rpm);
        _roadTarget = RoadLevel(e.Surface, e.Mph, e.FlatTyres);
        foreach (var s in e.Shots) if (ShotThump(s.Class) is { } t) Add(t);
        foreach (var (ch, amount) in e.Hits) if (HitThump(ch, amount) is { } t) Add(t);
        foreach (var d in e.Blasts) if (BlastThump(d) is { } t) Add(t);
    }

    /// <summary>A transient; past <see cref="MaxThumps"/> playing, the oldest goes.</summary>
    public void Add(Thump t)
    {
        if (t.Level <= 0 || t.Seconds <= 0) return;
        if (_thumps.Count >= MaxThumps) _thumps.RemoveAt(0);
        _thumps.Add(new Voice { Hz = t.Hz, Level = t.Level, Fall = MathF.Log(100f) / t.Seconds, Left = t.Seconds });
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
            _roadBand = Biquad.BandPass(50f, 0.7f, rate);
            _hp = Biquad.HighPass(LowHz, rate);
            _lp = Biquad.LowPass(HighHz, rate);
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
            x += _road * 28f * _roadBand.Run(_noise / 2147483648f - 1f);
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

        public float Run(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
            return y;
        }
    }
}
