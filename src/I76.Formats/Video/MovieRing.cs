namespace I76.Formats.Video;

/// <summary>
/// The movie player's frames decoded ahead, in their order, between its decoder thread (<see cref="Add"/>,
/// <see cref="DropPassed"/>) and the main thread (<see cref="TakeDue"/>). The main thread takes the latest frame whose turn has
/// come and passes over the ones before it; the decoder, while the main thread is held in a long step, drops what that step has
/// already passed by, so when the main thread comes back the ring holds frames from its due one on, not a backlog of old ones.
/// A frame dropped or passed over goes back through <c>release</c> (the player's buffer pool).
/// </summary>
public sealed class MovieRing<T>
{
    private readonly Queue<(int Index, T Frame)> _q = new();
    private readonly object _lock = new();

    public int Count { get { lock (_lock) return _q.Count; } }
    public bool IsEmpty => Count == 0;

    /// <summary>A decoded frame, after every one already in (frames come in their order).</summary>
    public void Add(int index, T frame) { lock (_lock) _q.Enqueue((index, frame)); }

    /// <summary>
    /// The main thread at frame <paramref name="want"/> of the clock: the latest frame at or before it taken out (true), those
    /// before that passed over (counted in <paramref name="passed"/>, released); false with none due yet.
    /// </summary>
    public bool TakeDue(int want, out int index, out T frame, out int passed, Action<T>? release = null)
    {
        index = -1; frame = default!; passed = 0;
        bool got = false;
        lock (_lock)
        {
            while (_q.Count > 0 && _q.Peek().Index <= want)
            {
                var f = _q.Dequeue();
                if (got) { passed++; release?.Invoke(frame); }
                (index, frame, got) = (f.Index, f.Frame, true);
            }
        }
        return got;
    }

    /// <summary>
    /// The decoder with the clock at frame <paramref name="due"/>: every frame before the last one at or before it dropped
    /// (released), that one kept to go up next. Returns how many went.
    /// </summary>
    public int DropPassed(int due, Action<T>? release = null)
    {
        int n = 0;
        lock (_lock)
        {
            while (_q.Count > 1)
            {
                var (first, _) = _q.Peek();
                // the second frame is due too: the first will never be shown
                var second = _q.ElementAt(1).Index;
                if (first > due || second > due) break;
                var f = _q.Dequeue().Frame;
                release?.Invoke(f);
                n++;
            }
        }
        return n;
    }

    /// <summary>Everything out (released): the movie is over.</summary>
    public void Clear(Action<T>? release = null)
    {
        lock (_lock)
            while (_q.Count > 0)
            {
                var f = _q.Dequeue().Frame;   // (taken out first: a null release must not skip the dequeue)
                release?.Invoke(f);
            }
    }
}

/// <summary>
/// How far ahead the movie player decodes and which frames it bothers to copy out: the rules apart from the engine, so they are
/// tested (MovieAheadTests).
/// </summary>
public static class MovieAhead
{
    /// <summary>Seconds of picture decoded ahead over a mission's load (its main-thread steps can hold the game for a few hundred ms).</summary>
    public const double LoadingSeconds = 2.0;
    /// <summary>Seconds of picture decoded ahead otherwise (the intro, the credits, a briefing with nothing under it).</summary>
    public const double PlainSeconds = 0.75;
    /// <summary>The frames' memory at most (bytes): a 1920x1440 4:2:0 frame is 4.1 MB, so 2 s at 15 a second (31 frames) is 123 MB.</summary>
    public const long BudgetBytes = 160L << 20;
    /// <summary>Seconds of sound decoded ahead of the picture decoded: with the picture's ring, the sound reaches past the generator's 4.5 s buffer.</summary>
    public const double SoundLeadSeconds = 3.0;
    /// <summary>A run of frames passed by uncopied this long (a decoder slower than the clock) copies the next one anyway, so the picture still moves.</summary>
    public const int MaxSkipRun = 15;

    /// <summary>Frames to hold ahead: <paramref name="seconds"/> of them, within <paramref name="budgetBytes"/>, at least 3.</summary>
    public static int Frames(double seconds, double frameDuration, long frameBytes, long budgetBytes = BudgetBytes)
    {
        if (frameDuration <= 0 || frameBytes <= 0) return 3;
        int byTime = (int)Math.Ceiling(seconds / frameDuration - 1e-9);
        long byBytes = budgetBytes / frameBytes;
        return (int)Math.Max(3, Math.Min(byTime, byBytes));
    }

    /// <summary>The bytes <paramref name="frames"/> frames hold.</summary>
    public static long Bytes(int frames, long frameBytes) => frames * frameBytes;

    /// <summary>
    /// The frame due now from the main thread's last word: its clock (<paramref name="clockSeconds"/>) and the time since it said
    /// so (<paramref name="sinceSeconds"/>; a long step of the main thread does not stop the sound, which the clock follows).
    /// -1 before the first frame is up.
    /// </summary>
    public static int Due(bool started, double clockSeconds, double sinceSeconds, double frameDuration) =>
        !started || frameDuration <= 0 ? -1 : (int)Math.Floor((clockSeconds + Math.Max(0, sinceSeconds)) / frameDuration);

    /// <summary>
    /// The frame about to be decoded is worth copying out: not one whose turn has already passed (the player shows the latest due
    /// frame, so one before the due one never goes up), unless <see cref="MaxSkipRun"/> have just been passed by.
    /// </summary>
    public static bool ShouldCopy(long index, int due, int skippedRun) => index >= due || skippedRun >= MaxSkipRun;
}
