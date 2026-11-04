using I76.Formats.Input;

namespace I76.Game.Ui;

/// <summary>One signal's recent values against the wall clock (seconds), kept for <see cref="DevSignals.Window"/>.</summary>
public sealed class SignalTrace
{
    private readonly (double T, float V)[] _ring;
    private int _head, _count;

    public SignalTrace(string name, float min, float max, int capacity = 512) { Name = name; Min = min; Max = max; _ring = new (double, float)[capacity]; }

    public string Name { get; }
    /// <summary>The plot's range.</summary>
    public float Min { get; }
    public float Max { get; }
    public float Last => _count == 0 ? 0 : _ring[(_head - 1 + _ring.Length) % _ring.Length].V;
    public double LastTime => _count == 0 ? double.NegativeInfinity : _ring[(_head - 1 + _ring.Length) % _ring.Length].T;
    public int Count => _count;

    public void Add(double t, float v)
    {
        _ring[_head] = (t, v);
        _head = (_head + 1) % _ring.Length;
        if (_count < _ring.Length) _count++;
    }

    public void Clear() { _head = 0; _count = 0; }

    /// <summary>The samples from <paramref name="from"/> on, oldest first.</summary>
    public IEnumerable<(double T, float V)> Since(double from)
    {
        for (int i = 0; i < _count; i++)
        {
            var s = _ring[(_head - _count + i + _ring.Length) % _ring.Length];
            if (s.T >= from) yield return s;
        }
    }

    /// <summary>The largest magnitude from <paramref name="from"/> on.</summary>
    public float Peak(double from) => Since(from).Select(s => MathF.Abs(s.V)).DefaultIfEmpty(0).Max();
}

/// <summary>
/// The Developer panel's Signals (docs/dev-hooks.md): what the game sends to the player's devices over the last few seconds,
/// for checking the force feedback, the rumble and the telemetry feed without the hardware. The producers report here
/// (the wheel's effects as ForceFeedback builds them, the pad's motor levels as they go to Godot, each telemetry packet, the
/// bass shaker's peak as BassShaker sends it);
/// <see cref="Sample"/> runs once a frame while the game runs, so the traces stop while it is paused (the panel open) and
/// show the seconds before. Only while <see cref="Watching"/> (the panel is on for the run); nothing here feeds back.
/// </summary>
public static class DevSignals
{
    /// <summary>The seconds a plot shows.</summary>
    public const double Window = 3;

    /// <summary>The panel is on for this mission: the producers report and the traces run (force feedback runs without a pad then, sending nowhere).</summary>
    public static bool Watching { get; set; }

    public static readonly SignalTrace WheelForce = new("wheel force", -1, 1), WheelSpring = new("wheel spring", 0, 1), WheelEffects = new("wheel effects", 0, 8),
        PadLow = new("pad low (strong)", 0, 1), PadHigh = new("pad high (weak)", 0, 1),
        TelRate = new("telemetry rate", 0, 120), TelPitch = new("pitch", -0.5f, 0.5f), TelRoll = new("roll", -0.5f, 0.5f),
        TelSurge = new("surge", -20, 20), TelSway = new("sway", -20, 20), TelHeave = new("heave", -20, 20),
        Lfe = new("bass shaker (LFE)", 0, 1);

    public static readonly SignalTrace[] All = [WheelForce, WheelSpring, WheelEffects, PadLow, PadHigh, TelRate, TelPitch, TelRoll, TelSurge, TelSway, TelHeave, Lfe];

    /// <summary>The bass shaker's signal: the peak of the block just sent to the Tactile bus (0 while the LFE Level is 0 or nothing plays).</summary>
    public static void Shaker(float peak) => _lfe = peak;

    /// <summary>The telemetry feed's targets as the log line names them, or null without a feed.</summary>
    public static string? TelemetryTargets { get; set; }

    private static float _weak, _strong, _force, _spring;
    private static int _effects;
    private static readonly Dictionary<string, (int Generation, double Start)> Started = [];
    private static readonly Queue<double> Packets = new();
    private static float _pitch, _roll, _surge, _sway, _heave, _lfe;

    /// <summary>The levels just sent to the pad's motors (0..1), as Godot's weak (high-frequency) and strong (low-frequency) motor.</summary>
    public static void Pad(float weak, float strong) { _weak = weak; _strong = strong; }

    /// <summary>
    /// The wheel's effects after a force-feedback frame at <paramref name="now"/>: the sum of the playing forces (each effect's
    /// value since its slot last changed, −1..1), the centring spring's coefficient (0..1) and the effects still running.
    /// </summary>
    public static void Wheel(WheelFrame f, double now)
    {
        double force = 0, spring = 0;
        int running = 0;
        foreach (var (key, slot) in f.Slots)
        {
            if (!Started.TryGetValue(key, out var s) || s.Generation != slot.Generation) Started[key] = s = (slot.Generation, now);
            var e = slot.Effect;
            double us = (now - s.Start) * 1e6;
            bool on = us >= e.StartDelayUs && (e.DurationUs == WheelEffect.Infinite || us < e.StartDelayUs + (double)e.DurationUs);
            if (!on) continue;
            running++;
            if (e.Kind == WheelKind.Spring) spring = Math.Max(spring, Math.Abs(e.Magnitude) / 10000.0);
            else force += e.At(us) / 10000.0;
        }
        foreach (var gone in Started.Keys.Where(k => !f.Slots.ContainsKey(k)).ToList()) Started.Remove(gone);
        (_force, _spring, _effects) = ((float)Math.Clamp(force, -1, 1), (float)spring, running);
    }

    /// <summary>A telemetry packet went at <paramref name="now"/>: the car's pitch and roll (radians) and its acceleration in its own frame (m/s²: surge forward, sway right, heave up).</summary>
    public static void Telemetry(double now, float pitch, float roll, float surge, float sway, float heave)
    {
        Packets.Enqueue(now);
        (_pitch, _roll, _surge, _sway, _heave) = (pitch, roll, surge, sway, heave);
    }

    /// <summary>One frame of the running game at <paramref name="now"/>: every trace takes its current value.</summary>
    public static void Sample(double now)
    {
        while (Packets.Count > 0 && Packets.Peek() < now - 1) Packets.Dequeue();
        WheelForce.Add(now, _force);
        WheelSpring.Add(now, _spring);
        WheelEffects.Add(now, _effects);
        PadLow.Add(now, _strong);
        PadHigh.Add(now, _weak);
        TelRate.Add(now, Packets.Count);
        TelPitch.Add(now, _pitch);
        TelRoll.Add(now, _roll);
        TelSurge.Add(now, _surge);
        TelSway.Add(now, _sway);
        TelHeave.Add(now, _heave);
        Lfe.Add(now, _lfe);
    }

    /// <summary>A new mission: the traces and the levels start empty.</summary>
    public static void Reset()
    {
        foreach (var t in All) t.Clear();
        Started.Clear();
        Packets.Clear();
        _weak = _strong = _force = _spring = _pitch = _roll = _surge = _sway = _heave = _lfe = 0;
        _effects = 0;
    }
}
