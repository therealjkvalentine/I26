namespace I76.Game.Sim;

/// <summary>
/// I76_DASH_STATS=1: a `dash-stats:` line a second with the cockpit's own cost over it: its update's main-thread time a frame
/// (mean and worst), the bytes of texture it uploaded and how many uploads, the managed bytes it allocated, and the process's
/// garbage collections by generation (docs/perf/plan.md, "The dash"). `verify` as well reads every uploaded texture back from the
/// GPU and counts the texels that differ from the CPU's picture (a `dash-verify:` line for any that do; the count in the line).
/// </summary>
public static class DashStats
{
    public static readonly bool Verify = System.Environment.GetEnvironmentVariable("I76_DASH_STATS") == "verify";
    public static readonly bool On = Verify || System.Environment.GetEnvironmentVariable("I76_DASH_STATS") == "1";
    private static int _checked, _bad;

    /// <summary>Verify: the GPU's copy of a texture against the CPU's texels after an upload.</summary>
    public static void Check(string name, byte[] cpu, byte[]? gpu)
    {
        if (gpu is null) return;
        _checked++;
        int n = 0;
        for (int i = 0; i < System.Math.Min(cpu.Length, gpu.Length); i += 4)
            if (cpu[i] != gpu[i] || cpu[i + 1] != gpu[i + 1] || cpu[i + 2] != gpu[i + 2] || cpu[i + 3] != gpu[i + 3]) n++;
        if (n == 0 && cpu.Length == gpu.Length) return;
        _bad++;
        Godot.GD.Print($"dash-verify: {name} texels differing={n} sizes={cpu.Length}/{gpu.Length}");
    }

    private static long _bytes, _ticks, _worst, _alloc, _start, _allocStart, _since;
    private static int _uploads, _frames, _g0, _g1, _g2;

    /// <summary>An upload of <paramref name="bytes"/> bytes of texture.</summary>
    public static void Upload(long bytes)
    {
        if (!On) return;
        _bytes += bytes;
        _uploads++;
    }

    /// <summary>The cockpit's update starts.</summary>
    public static void Begin()
    {
        if (!On) return;
        _allocStart = System.GC.GetAllocatedBytesForCurrentThread();
        _start = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>The cockpit's update ends: its time and allocations counted, and the second's line printed when one has passed.</summary>
    public static void End()
    {
        if (!On) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp(), d = now - _start;
        _ticks += d;
        _worst = System.Math.Max(_worst, d);
        _alloc += System.GC.GetAllocatedBytesForCurrentThread() - _allocStart;
        _frames++;
        if (_since == 0) { _since = now; (_g0, _g1, _g2) = (System.GC.CollectionCount(0), System.GC.CollectionCount(1), System.GC.CollectionCount(2)); return; }
        double secs = (double)(now - _since) / System.Diagnostics.Stopwatch.Frequency;
        if (secs < 1) return;
        double ms = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        int g0 = System.GC.CollectionCount(0), g1 = System.GC.CollectionCount(1), g2 = System.GC.CollectionCount(2);
        Godot.GD.Print(System.FormattableString.Invariant(
            $"dash-stats: tick={SimClock.Now} s={secs:F2} frames={_frames} ms={_ticks * ms / _frames:F3} worst={_worst * ms:F2} upload_kb={_bytes / 1024.0:F0} uploads={_uploads} alloc_kb={_alloc / 1024.0:F1} gc={g0 - _g0}/{g1 - _g1}/{g2 - _g2}{(Verify ? $" verified={_checked} bad={_bad}" : "")}"));
        (_g0, _g1, _g2) = (g0, g1, g2);
        _since = now;
        _bytes = _ticks = _worst = _alloc = 0;
        _uploads = _frames = 0;
    }
}
