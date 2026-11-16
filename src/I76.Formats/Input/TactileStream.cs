using System.Diagnostics;

namespace I76.Formats.Input;

/// <summary>Where <see cref="TactileStream"/>'s samples go: the game's audio generator (a ring the mixer reads) or a test's.</summary>
public interface ITactileSink
{
    /// <summary>Frames the sink holds when full.</summary>
    int Capacity { get; }
    /// <summary>Frames that can be pushed now.</summary>
    int Space { get; }
    /// <summary>Exactly <see cref="TactileStream.Block"/> samples (mono; the sink makes its frames).</summary>
    void Push(ReadOnlySpan<float> block);
}

/// <summary>The settings <see cref="TactileSynth"/> takes from Audio Control and [port] (the LFE Level's gain, tactile_eq, crossover, lfe_*), handed over whole.</summary>
public sealed record TactileSettings(float Gain, TactileEq? Eq, float LowPassHz, int[] Sources)
{
    public void ApplyTo(TactileSynth s) { s.Eq = Eq; s.LowPassHz = LowPassHz; s.Gain = Gain; s.SetWeights(Sources); }
}

/// <summary>
/// The bass shaker's synthesis off the main thread. The game's thread gathers a tick's inputs into a <see cref="TactileEvents"/> slot
/// (<see cref="Begin"/>, <see cref="Commit"/>: a single-producer, single-consumer ring of reused slots, nothing allocated) and sets
/// <see cref="Settings"/> and <see cref="Hold"/>; a worker thread of its own runs <see cref="TactileSynth.Step"/> on each slot in order and
/// <see cref="TactileSynth.Fill"/>, and keeps the sink filled to <see cref="Depth"/> frames in blocks of <see cref="Block"/>. The synth
/// belongs to the worker while it runs. A slot's <c>at</c> places it at that sample of the stream (the tests' and the offline
/// render's fixed schedules); the game's are placed at once, at the next sample the worker makes.
///
/// Starved (the worker not run in time) the sink runs dry and the mixer plays silence (Godot's generator fills an underrun with
/// zeros); the synth does not advance meanwhile, so the stream resumes where it stopped and never repeats or skips a sample.
/// </summary>
public sealed class TactileStream : IDisposable
{
    /// <summary>The frames pushed at once (5.3 ms at 48 kHz).</summary>
    public const int Block = 256;
    /// <summary>Slots in the events' ring: 1.3 s of 24 Hz ticks with the worker stalled; past that a tick's events are dropped.</summary>
    public const int Slots = 32;

    private readonly TactileSynth _synth;
    private readonly ITactileSink _sink;
    private readonly int _rate;
    private readonly TactileEvents[] _slot = new TactileEvents[Slots];
    private readonly long[] _at = new long[Slots];
    private int _head, _tail;   // _head: the next slot the producer fills (its own); _tail: the next the worker reads (its own)
    private readonly float[] _block = new float[Block];
    private int _blockFill;
    private long _made;
    private TactileSettings? _settings, _applied;
    private volatile bool _hold, _stop;
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;

    /// <summary>The frames kept queued in the sink: the delay from a tick's step to its being felt, past the mixer's own.</summary>
    public int Depth { get; }

    /// <summary>A stream for <paramref name="synth"/> at <paramref name="rate"/> Hz into <paramref name="sink"/>, keeping <paramref name="depthSeconds"/> queued (rounded up to blocks, at most the sink's capacity).</summary>
    public TactileStream(TactileSynth synth, ITactileSink sink, int rate, float depthSeconds)
    {
        _synth = synth;
        _sink = sink;
        _rate = rate;
        int blocks = Math.Max(1, (int)MathF.Ceiling(depthSeconds * rate / Block));
        Depth = Math.Min(blocks * Block, sink.Capacity / Block * Block);
        for (int i = 0; i < Slots; i++) _slot[i] = new TactileEvents();
    }

    /// <summary>Live worker threads in the process (all streams): 0 when nothing plays, at most 1 in the game.</summary>
    public static int Running => _running;
    private static int _running;

    /// <summary>The samples made so far.</summary>
    public long Made => Volatile.Read(ref _made);
    /// <summary>The largest sample of the last block pushed.</summary>
    public float Peak { get; private set; }
    /// <summary>The sum of the squares of every sample made, for I76_LFE_LOG's RMS (with <see cref="Made"/>).</summary>
    public double SumSquares => Volatile.Read(ref _sumSq);
    private double _sumSq;
    /// <summary>Ticks dropped because the ring was full (the worker stalled over a second).</summary>
    public int Dropped { get; private set; }
    /// <summary>The synth's settings, applied before the next sample.</summary>
    public TactileSettings? Settings { get => Volatile.Read(ref _settings); set { Volatile.Write(ref _settings, value); _wake.Set(); } }
    /// <summary>Something holds the game (the pause, a menu over it): the synth silent and its transients cleared, as long as it is set.</summary>
    public bool Hold { get => _hold; set => _hold = value; }

    /// <summary>Called on the worker after a slot's step, with the frames the sink held then (the step's delay): the latency probe.</summary>
    public Action<int>? Stepped { get; set; }
    /// <summary>Called on the worker with each pass's ticks (Stopwatch) and the blocks it pushed: the cost probe.</summary>
    public Action<long, int>? Passed { get; set; }

    /// <summary>The producer's next slot, cleared, or null with the ring full (the tick's events are dropped).</summary>
    public TactileEvents? Begin()
    {
        int next = (_head + 1) % Slots;
        if (next == Volatile.Read(ref _tail)) { Dropped++; return null; }
        var e = _slot[_head];
        e.Clear();
        return e;
    }

    /// <summary>Publishes the slot <see cref="Begin"/> gave, to step at sample <paramref name="at"/> (negative: at once) and wakes the worker.</summary>
    public void Commit(long at = -1)
    {
        _at[_head] = at;
        Volatile.Write(ref _head, (_head + 1) % Slots);
        _wake.Set();
    }

    /// <summary>Starts the worker (above normal priority: a late worker is a silent shaker, not a late frame).</summary>
    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Loop) { Name = "BassShaker", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        Interlocked.Increment(ref _running);
        _thread.Start();
    }

    /// <summary>Stops the worker and waits for it: the synth is the caller's again.</summary>
    public void Stop()
    {
        if (_thread is not { } t) return;
        _stop = true;
        _wake.Set();
        t.Join();
        _thread = null;
    }

    public void Dispose()
    {
        Stop();
        _wake.Dispose();
    }

    /// <summary>The wake interval: under half a block, so the sink never falls a block below the depth for long.</summary>
    private int WaitMs => Math.Clamp(Block * 1000 / 2 / Math.Max(_rate, 1), 1, 20);

    private void Loop()
    {
        try
        {
            while (!_stop)
            {
                Pass();
                _wake.WaitOne(WaitMs);
            }
        }
        catch (Exception ex) { Error = ex; }   // the shaker goes silent; the owner prints it
        finally { Interlocked.Decrement(ref _running); }
    }

    /// <summary>What stopped the worker, if it failed (the sink threw).</summary>
    public Exception? Error { get; private set; }

    /// <summary>
    /// One pass (the worker's, or a test's on its own thread with the worker not started): the settings, the hold, the ticks due,
    /// then blocks until the sink holds <see cref="Depth"/> frames. Returns the blocks pushed.
    /// </summary>
    public int Pass()
    {
        long t0 = Stopwatch.GetTimestamp();
        if (Volatile.Read(ref _settings) is { } s && !ReferenceEquals(s, _applied)) { s.ApplyTo(_synth); _applied = s; }
        if (_hold) _synth.Reset();
        int pushed = 0;
        int queued = _sink.Capacity - _sink.Space;
        while (queued + Block <= Depth && !_stop)
        {
            // the block, stepping each tick at its sample
            while (_blockFill < Block)
            {
                StepDue();
                int n = Block - _blockFill;
                if (Next() is { } at && at > _made) n = (int)Math.Min(n, at - _made);
                else if (Next() is not null) continue;
                _synth.Fill(_block.AsSpan(_blockFill, n), _rate);
                _blockFill += n;
                Volatile.Write(ref _made, _made + n);
            }
            float peak = 0;
            double sq = 0;
            foreach (float v in _block) { peak = MathF.Max(peak, MathF.Abs(v)); sq += v * v; }
            _sink.Push(_block);
            _blockFill = 0;
            Peak = peak;
            Volatile.Write(ref _sumSq, _sumSq + sq);
            queued += Block;
            pushed++;
        }
        // the ticks that came while the sink was full are stepped now, not a block later
        StepDue();
        Passed?.Invoke(Stopwatch.GetTimestamp() - t0, pushed);
        return pushed;
    }

    /// <summary>The next published slot's sample (negative: at once), or null for none.</summary>
    private long? Next() => _tail == Volatile.Read(ref _head) ? null : _at[_tail] < 0 ? -1 : _at[_tail];

    private void StepDue()
    {
        while (Next() is { } at && at <= _made)
        {
            if (!_hold) _synth.Step(_slot[_tail]);
            Volatile.Write(ref _tail, (_tail + 1) % Slots);
            Stepped?.Invoke(_sink.Capacity - _sink.Space);
        }
    }
}
