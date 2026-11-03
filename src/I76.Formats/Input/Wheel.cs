namespace I76.Formats.Input;

/// <summary>The DirectInput effect types a wheel or stick is given.</summary>
public enum WheelKind { Constant, Ramp, Square, Sine, Triangle, SawtoothUp, SawtoothDown, Spring, Custom }

/// <summary>
/// One force-feedback effect in DirectInput's units: magnitudes −10000..10000, times in µs, angles in hundredths of a degree
/// (polar: 0 the stick's forward, 9000 its right; the force comes from there). What a wheel sink creates, updates and starts.
/// </summary>
public sealed record WheelEffect
{
    public const uint Infinite = uint.MaxValue;

    public required WheelKind Kind { get; init; }
    /// <summary>Constant: the force; Ramp: the start; periodic: the magnitude (≥ 0); Spring: the coefficient.</summary>
    public int Magnitude { get; init; }
    /// <summary>Ramp: the end.</summary>
    public int End { get; init; }
    /// <summary>Periodic: the offset; Spring: the centre.</summary>
    public int Offset { get; init; }
    public uint PeriodUs { get; init; }
    /// <summary>Periodic: where in the cycle it starts, 0..35999.</summary>
    public int Phase { get; init; }
    public uint DurationUs { get; init; } = Infinite;
    public uint StartDelayUs { get; init; }
    /// <summary>0..10000.</summary>
    public int Gain { get; init; } = 10000;
    public int Direction { get; init; }
    public int AttackLevel { get; init; }
    public uint AttackUs { get; init; }
    public int FadeLevel { get; init; }
    public uint FadeUs { get; init; }
    /// <summary>Custom: the curve, −10000..10000 per sample (shared, never written).</summary>
    public int[]? Samples { get; init; }
    public uint SamplePeriodUs { get; init; }

    public bool Periodic => Kind is WheelKind.Square or WheelKind.Sine or WheelKind.Triangle or WheelKind.SawtoothUp or WheelKind.SawtoothDown;
    public bool HasEnvelope => AttackUs > 0 || FadeUs > 0;

    /// <summary>
    /// The force <paramref name="us"/> after the start, −10000..10000 with the gain, as the device would compute it: for a
    /// sink that has to play an effect type the device lacks as a constant force it rewrites (a spring has no such value: 0).
    /// </summary>
    public double At(double us)
    {
        us -= StartDelayUs;
        if (us < 0 || DurationUs != Infinite && us >= DurationUs || Kind == WheelKind.Spring) return 0;
        double v;
        if (Kind == WheelKind.Custom)
        {
            if (Samples is not { Length: > 0 } s || SamplePeriodUs == 0) return 0;
            v = s[(int)(us / SamplePeriodUs) % s.Length];
        }
        else if (Kind == WheelKind.Ramp)
            v = DurationUs == Infinite ? Magnitude : Magnitude + (End - Magnitude) * us / DurationUs;
        else
        {
            double level = Envelope(us, Math.Abs(Magnitude));
            if (Kind == WheelKind.Constant) v = Math.Sign(Magnitude) * level;
            else
            {
                double p = PeriodUs == 0 ? 0 : us / PeriodUs + Phase / 36000.0;
                p -= Math.Floor(p);
                double w = Kind switch
                {
                    WheelKind.Square => p < 0.5 ? 1 : -1,
                    WheelKind.Sine => Math.Sin(2 * Math.PI * p),
                    WheelKind.Triangle => p < 0.25 ? 4 * p : p < 0.75 ? 2 - 4 * p : 4 * p - 4,
                    WheelKind.SawtoothUp => 2 * p - 1,
                    _ => 1 - 2 * p,
                };
                v = Offset + level * w;
            }
        }
        return Math.Clamp(v * Gain / 10000.0, -10000, 10000);
    }

    /// <summary>The envelope's level at <paramref name="us"/> over a sustain of <paramref name="sustain"/>: the attack from its level, the fade to its level.</summary>
    private double Envelope(double us, double sustain)
    {
        if (AttackUs > 0 && us < AttackUs) return AttackLevel + (sustain - AttackLevel) * us / AttackUs;
        if (FadeUs > 0 && DurationUs != Infinite && us > DurationUs - FadeUs) return FadeLevel + (sustain - FadeLevel) * (DurationUs - us) / FadeUs;
        return sustain;
    }

    // the curve is compared by its values, so a remapped file is the same effect
    public bool Equals(WheelEffect? o) => o is not null && Kind == o.Kind && Magnitude == o.Magnitude && End == o.End && Offset == o.Offset
        && PeriodUs == o.PeriodUs && Phase == o.Phase && DurationUs == o.DurationUs && StartDelayUs == o.StartDelayUs && Gain == o.Gain
        && Direction == o.Direction && AttackLevel == o.AttackLevel && AttackUs == o.AttackUs && FadeLevel == o.FadeLevel && FadeUs == o.FadeUs
        && SamplePeriodUs == o.SamplePeriodUs && (ReferenceEquals(Samples, o.Samples) || Samples is not null && o.Samples is not null && Samples.AsSpan().SequenceEqual(o.Samples));

    public override int GetHashCode() => HashCode.Combine(Kind, Magnitude, Offset, PeriodUs, Gain, Direction, DurationUs, Samples?.Length ?? 0);
}

/// <summary>
/// What the original's force-feedback DLL had on the device: the effects it made itself and the force files' effects as the
/// SideWinder driver played them, in DirectInput's terms. <see cref="WheelMap"/> builds them; a sink compares one frame with the
/// next (<see cref="WheelCoalescer"/>).
/// </summary>
public readonly record struct WheelSlot(WheelEffect Effect, int Generation);

public sealed class WheelFrame
{
    /// <summary>The device's force-feedback axes: 1 a wheel, 2 a stick.</summary>
    public int Axes { get; set; } = 1;
    public Dictionary<string, WheelSlot> Slots { get; } = [];
}

/// <summary>The original's effects as DirectInput effects (<see cref="WheelEffect"/>).</summary>
public static class WheelMap
{
    private const int Full = 10000;

    /// <summary>
    /// On a wheel (one axis) an effect aimed along the stick's forward axis has no part on the wheel's: it is played on the
    /// wheel's axis whole, and one aimed to a side pushes away from that side (the force comes from its direction).
    /// </summary>
    public static int OneAxisSign(int degrees)
    {
        double s = Math.Sin(degrees * Math.PI / 180);
        return s > 0.01 ? -1 : 1;
    }

    /// <summary>Where a point lies from the car, degrees clockwise from the nose (0..360), from its <paramref name="right"/> and <paramref name="forward"/> parts in the car's frame.</summary>
    public static float Bearing(float right, float forward)
    {
        if (right == 0 && forward == 0) return 0;
        double d = Math.Atan2(right, forward) * 180 / Math.PI;
        return (float)(d < 0 ? d + 360 : d);
    }

    /// <summary>
    /// A force file's effects (what the driver's file effect played), at <paramref name="gain"/> 0..1. Each primitive is
    /// one effect; a Sequence plays them one after another (start delays), a Superimpose together; the two
    /// composites themselves, and a constant of nothing (the sequences' rests), are no effect.
    /// The block, read off the fourteen shipped files: the waveform's frequency in Hz (one cycle of the missiles' cosine),
    /// the envelope's attack, sustain and fade as percent of the duration (they add up to 100 in every file; this reader's
    /// <see cref="FrcEffect.HighPercent"/> and <see cref="FrcEffect.LowPercent"/> are the sustain and the fade), the levels
    /// in percent (a periodic swings between the two, a ramp goes from the first to the second).
    /// </summary>
    public static List<WheelEffect> File(IReadOnlyList<FrcEffect> fx, double gain, int axes)
    {
        var list = new List<WheelEffect>();
        bool sequence = fx.Any(e => e.Waveform == FrcEffect.Sequence);
        uint at = 0;
        foreach (var e in fx)
        {
            if (e.Waveform is FrcEffect.Sequence or FrcEffect.Superimpose) continue;
            var w = Effect(e, gain, axes);
            uint dur = (uint)Math.Max(e.DurationMs, 0) * 1000;
            if (w is not null) list.Add(sequence ? w with { StartDelayUs = at } : w);
            if (sequence) at += dur;
        }
        return list;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrcEffect, int[][]> Curves = new();

    /// <summary>One primitive of a force file; null for one that pushes nothing.</summary>
    public static WheelEffect? Effect(FrcEffect e, double gain, int axes)
    {
        if (e.DurationMs <= 0) return null;
        int sign = axes == 1 ? OneAxisSign(e.DirectionDegrees) : 1;
        double sustain = Math.Clamp(e.MagnitudePercent, 0, 100) / 100.0;
        int Lvl(int percent) => (int)Math.Round(Math.Clamp(percent, -100, 100) * 100 * sustain);
        uint dur = (uint)e.DurationMs * 1000;
        int fade = Math.Clamp(e.LowPercent, 0, 100), attack = Math.Clamp(100 - e.HighPercent - e.LowPercent, 0, 100);
        var w = new WheelEffect
        {
            Kind = WheelKind.Constant,
            DurationUs = dur,
            Gain = (int)Math.Round(Math.Clamp(gain, 0, 1) * Math.Clamp(e.GainPercent, 0, 100) * 100),
            Direction = axes == 1 ? 0 : (((e.DirectionDegrees % 360) + 360) % 360) * 100,
            AttackUs = (uint)(dur / 100.0 * attack),
            FadeUs = (uint)(dur / 100.0 * fade),
        };
        uint period = (uint)Math.Round(1e6 / Math.Max(e.Cycles, 1));
        WheelEffect Periodic(WheelKind k, int phase)
        {
            int mag = (Lvl(e.MaxLevel) - Lvl(e.MinLevel)) / 2, off = (Lvl(e.MaxLevel) + Lvl(e.MinLevel)) / 2;
            if (mag < 0) { mag = -mag; phase += 18000; }
            if (sign < 0) { off = -off; phase += 18000; }
            return w with { Kind = k, Magnitude = mag, Offset = off, PeriodUs = period, Phase = phase % 36000 };
        }
        switch (e.Waveform)
        {
            case FrcEffect.Constant:
                return e.MaxLevel == 0 ? null : w with { Magnitude = sign * Lvl(e.MaxLevel) };
            case FrcEffect.Sine: return Periodic(WheelKind.Sine, 0);
            case FrcEffect.Cosine: return Periodic(WheelKind.Sine, 9000);
            case FrcEffect.SquareHigh: return Periodic(WheelKind.Square, 0);
            case FrcEffect.SquareLow: return Periodic(WheelKind.Square, 18000);
            case FrcEffect.SawtoothUp: return Periodic(WheelKind.SawtoothUp, 0);
            case FrcEffect.SawtoothDown: return Periodic(WheelKind.SawtoothDown, 0);
            case FrcEffect.RampUp or FrcEffect.RampDown:
                return w with { Kind = WheelKind.Ramp, Magnitude = sign * Lvl(e.MaxLevel), End = sign * Lvl(e.MinLevel), AttackUs = 0, FadeUs = 0 };
            case FrcEffect.UserDefined:
                if (e.Samples.Length == 0) return null;
                var c = Curves.GetValue(e, k => [k.Samples.Select(s => Math.Clamp(s, -100, 100) * 100).ToArray(), k.Samples.Select(s => -Math.Clamp(s, -100, 100) * 100).ToArray()]);
                return w with { Kind = WheelKind.Custom, Samples = c[sign < 0 ? 1 : 0], SamplePeriodUs = dur / (uint)e.Samples.Length, AttackUs = 0, FadeUs = 0 };
            default:
                return null;
        }
    }

    private static uint Period(double hz) => (uint)Math.Round(1e6 / Math.Max(hz, 0.5));

    /// <summary>The machine guns' rattle: a square at the weapon's rate, half strength, from full over its first period.</summary>
    public static WheelEffect Rattle(int rate, int gainPercent) => new()
    {
        Kind = WheelKind.Square, Magnitude = Full / 2, PeriodUs = Period(rate), Gain = Pct(gainPercent),
        AttackLevel = Full, AttackUs = Period(rate),
    };

    /// <summary>The flame weapons' and droppers' triangle at the rate, half strength about a quarter's offset.</summary>
    public static WheelEffect Triangle(int rate, int gainPercent) => new()
    {
        Kind = WheelKind.Triangle, Magnitude = Full / 2, Offset = Full / 4, PeriodUs = Period(rate), Gain = Pct(gainPercent),
    };

    /// <summary>The thump (mortars): 0.2 s of a 47 Hz triangle at full strength, 2.1 ms in from nothing and 6.3 ms out.</summary>
    public static WheelEffect Thump(int gainPercent) => new()
    {
        Kind = WheelKind.Triangle, Magnitude = Full, PeriodUs = Period(47), DurationUs = 200_000, Gain = Pct(gainPercent),
        AttackUs = 2100, FadeUs = 6300,
    };

    /// <summary>A hit's push: a constant force of <paramref name="magnitude"/> (it dies away, and swings past nothing into a pull) from <paramref name="degrees"/>.</summary>
    public static WheelEffect Jolt(int magnitude, int degrees, int axes) => axes == 1
        ? new() { Kind = WheelKind.Constant, Magnitude = Math.Clamp(magnitude, -Full, Full) * OneAxisSign(degrees) }
        : new() { Kind = WheelKind.Constant, Magnitude = Math.Clamp(magnitude, -Full, Full), Direction = ((degrees % 360) + 360) % 360 * 100 };

    /// <summary>
    /// The engine's vibration: a sine at <paramref name="gainPercent"/>, quicker with the rpm. The DLL's rate falls from 8 by a
    /// product of the rpm whose constants are not known here; this one goes from 8 Hz at idle to 14 Hz at 5000 rpm.
    /// </summary>
    public static WheelEffect Engine(int gainPercent, float rpm) => new()
    {
        Kind = WheelKind.Sine, Magnitude = Pct(gainPercent), PeriodUs = Period(Math.Clamp(8 + 6 * (rpm - 1000) / 4000.0, 8, 14)),
    };

    /// <summary>The road: a triangle at <paramref name="gainPercent"/>, <paramref name="hz"/> by the speed.</summary>
    public static WheelEffect Road(int gainPercent, int hz) => new()
    {
        Kind = WheelKind.Triangle, Magnitude = Pct(gainPercent), PeriodUs = Period(hz),
    };

    /// <summary>The flat tyre: a 4 Hz side-to-side shake of <paramref name="level"/> 0..1.</summary>
    public static WheelEffect Tyre(double level, int axes) => new()
    {
        Kind = WheelKind.Sine, Magnitude = (int)Math.Round(Math.Clamp(level, 0, 1) * Full), PeriodUs = Period(4), Direction = axes == 1 ? 0 : 9000,
    };

    /// <summary>
    /// The centring spring, stiffer with speed. The DLL's spring and friction follow the speed by constants not known here: this
    /// one goes from 15% standing to 60% at 90 mph and on.
    /// </summary>
    public static WheelEffect Spring(float mph) => new()
    {
        Kind = WheelKind.Spring, Magnitude = (int)Math.Round(Math.Clamp(1500 + 50 * mph, 1500, 6000)),
    };

    private static int Pct(int percent) => Math.Clamp(percent, 0, 100) * 100;
}

/// <summary>What a sink does to the device for one slot.</summary>
public enum WheelOp { Create, Update, Restart, Remove }

/// <summary>DirectInput's DIEP_* parameter flags an update carries.</summary>
[Flags]
public enum WheelParams
{
    None = 0, Duration = 0x1, Gain = 0x4, Direction = 0x40, Envelope = 0x80, TypeSpecific = 0x100, StartDelay = 0x200,
    All = Duration | Gain | Direction | Envelope | TypeSpecific | StartDelay,
}

public readonly record struct WheelCommand(WheelOp Op, string Slot, WheelEffect? Effect, WheelParams Params = WheelParams.None);

/// <summary>
/// Puts a rate limit between the force model (a frame of slots every sim tick) and a DirectInput device, where every call is a
/// USB report and can block: a new slot is created and started, a gone one removed, a restarted one (a new
/// <see cref="WheelSlot.Generation"/>) started again with its parameters; a changed one is updated with only the parameters
/// that changed (a magnitude alone is DIEP_TYPESPECIFICPARAMS, the one report), and not when the change is under a step
/// (<see cref="Steps"/>) or comes sooner than <see cref="MinGap"/> s after the slot's last update. Nothing changed, nothing sent.
/// </summary>
public sealed class WheelCoalescer
{
    public const int Steps = 100;
    public const double MinGap = 0.03;
    private const int Step = 10000 / Steps;

    private readonly Dictionary<string, (WheelEffect Effect, int Generation, double At)> _sent = [];

    public IReadOnlyCollection<string> Live => _sent.Keys;

    /// <summary>The commands that take the device from what was sent to <paramref name="want"/> at <paramref name="now"/> s.</summary>
    public List<WheelCommand> Diff(IReadOnlyDictionary<string, WheelSlot> want, double now)
    {
        var cmds = new List<WheelCommand>();
        foreach (var key in _sent.Keys.Where(k => !want.ContainsKey(k)).ToList())
        {
            _sent.Remove(key);
            cmds.Add(new(WheelOp.Remove, key, null));
        }
        foreach (var (key, slot) in want)
        {
            if (!_sent.TryGetValue(key, out var was))
            {
                _sent[key] = (slot.Effect, slot.Generation, now);
                cmds.Add(new(WheelOp.Create, key, slot.Effect, WheelParams.All));
                continue;
            }
            if (was.Generation != slot.Generation)
            {
                _sent[key] = (slot.Effect, slot.Generation, now);
                cmds.Add(new(WheelOp.Restart, key, slot.Effect, WheelParams.All));
                continue;
            }
            var p = Changed(was.Effect, slot.Effect);
            if (p == WheelParams.None || now - was.At < MinGap) continue;
            _sent[key] = (slot.Effect, slot.Generation, now);
            cmds.Add(new(WheelOp.Update, key, slot.Effect, p));
        }
        return cmds;
    }

    /// <summary>Every slot removed (the pause, a stop, the device going).</summary>
    public List<WheelCommand> Clear()
    {
        var cmds = _sent.Keys.Select(k => new WheelCommand(WheelOp.Remove, k, null)).ToList();
        _sent.Clear();
        return cmds;
    }

    /// <summary>The parameters that differ by more than a step; a kind change (never made by the model) counts as everything.</summary>
    public static WheelParams Changed(WheelEffect a, WheelEffect b)
    {
        if (a.Kind != b.Kind) return WheelParams.All;
        var p = WheelParams.None;
        static bool Far(int x, int y) => Math.Abs(x - y) >= Step || (x == 0) != (y == 0);
        if (Far(a.Magnitude, b.Magnitude) || Far(a.End, b.End) || Far(a.Offset, b.Offset) || a.Phase != b.Phase
            || Math.Abs((double)a.PeriodUs - b.PeriodUs) > 0.02 * Math.Max(a.PeriodUs, 1) || a.SamplePeriodUs != b.SamplePeriodUs
            || !(ReferenceEquals(a.Samples, b.Samples) || a.Samples is not null && b.Samples is not null && a.Samples.AsSpan().SequenceEqual(b.Samples)))
            p |= WheelParams.TypeSpecific;
        if (Far(a.Gain, b.Gain)) p |= WheelParams.Gain;
        if (a.Direction != b.Direction) p |= WheelParams.Direction;
        if (a.AttackLevel != b.AttackLevel || a.AttackUs != b.AttackUs || a.FadeLevel != b.FadeLevel || a.FadeUs != b.FadeUs) p |= WheelParams.Envelope;
        if (a.DurationUs != b.DurationUs) p |= WheelParams.Duration;
        if (a.StartDelayUs != b.StartDelayUs) p |= WheelParams.StartDelay;
        return p;
    }
}

/// <summary>
/// The hand-over between the game's frame and a sink's own thread: frames coalesce (only the newest is kept; a thread that was
/// held up by a driver call skips the ones it missed), control messages queue in order.
/// </summary>
public sealed class WheelMailbox<TFrame, TControl> where TFrame : class
{
    private readonly object _lock = new();
    private readonly Queue<TControl> _controls = new();
    private readonly AutoResetEvent _signal = new(false);
    private TFrame? _frame;

    /// <summary>Frames posted that were replaced before the thread took them (tests, the log).</summary>
    public int Dropped { get; private set; }

    public void Post(TFrame frame)
    {
        lock (_lock) { if (_frame is not null) Dropped++; _frame = frame; }
        _signal.Set();
    }

    public void Send(TControl control)
    {
        lock (_lock) _controls.Enqueue(control);
        _signal.Set();
    }

    /// <summary>Waits up to <paramref name="ms"/> for anything, then takes the control messages in order and the newest frame (or null).</summary>
    public (List<TControl> Controls, TFrame? Frame) Take(int ms)
    {
        _signal.WaitOne(ms);
        lock (_lock)
        {
            var c = _controls.ToList();
            _controls.Clear();
            var f = _frame;
            _frame = null;
            return (c, f);
        }
    }
}
