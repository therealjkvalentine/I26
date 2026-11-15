using System.Diagnostics;
using System.Globalization;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_FRAME_SECTIONS=PATH.csv: a row a frame of where the main thread's time went, for finding the slow frames' cause (the 1 % lows).
/// Each section is a <see cref="Time"/> scope round a node's process step or a part of one (the sim's ticks, the cockpit's radar and
/// panel uploads, the vegetation, the terrain, ...): its milliseconds and the managed bytes it allocated on the main thread. A nested
/// section counts in its parent too (sim.tick in sim, the cockpit's parts in cockpit). With them: the frame's wall time (from the start of
/// one frame's process step to the next's: render and swap included), the span of every node's process step (nodes), the engine's process
/// and physics times, the renderer's CPU and GPU time, the sim ticks run, the garbage collections by generation and their pause, the
/// bytes allocated (main thread and every thread) and the texture uploads counted by <see cref="Upload"/>.
///
/// Off (the variable unset) a scope is a struct whose Dispose tests one static field: nothing is allocated, nothing timed. On, the rows
/// go into an array made at the start and are written when the game quits, so the measuring allocates nothing a frame either.
/// I76_FRAME_SECTIONS_SKIP_MS (default 8000) skips the frames until that long after the mission counted as loaded;
/// I76_FRAME_SECTIONS_RUN_MS (default 0: until the game quits) quits the game after that long; the player's car destroyed ends it too.
/// I76_FRAME_SECTIONS_TYPES=1 also listens to the runtime's allocation samples (one about every 100 kB allocated, with the type) and
/// prints the types that took the most, by thread, when the game quits: what the allocations are (the listener costs time: not for numbers).
/// </summary>
public static partial class FrameSections
{
    public enum Sec
    {
        Sim, SimTick, Main, Cockpit, CockpitRadar, CockpitPanels, CockpitMirror, Hud, Overlays, Cameras, CombatView, DamageView, Smoke,
        ModernFx, SkidMarks, Vegetation, Terrain, Outside, Scenery, PlacedTrees, Tumbleweeds, Brush, Clutter, DropShadows, Lights,
        SoftwareLook, CabClip, Voices, Feedback, Telemetry, Ui, Recorders, Physics, RadarUpload, LivePack, LiveSend, LiveLevels, RadarCompose, UiExt, UiWiggle, UiFps, UiStandBy, Count,
    }

    private static readonly string[] SecNames =
    [
        "sim", "sim.tick", "main", "cockpit", "cockpit.radar", "cockpit.panels", "cockpit.mirror", "hud", "overlays", "cameras", "combat",
        "damage", "smoke", "modernfx", "skids", "veg", "terrain", "outside", "scenery", "trees", "tumbleweeds", "brush", "clutter",
        "dropshadows", "lights", "software", "cabclip", "voices", "feedback", "telemetry", "ui", "recorders", "physics.modern", "cockpit.radar.upload", "live.pack", "live.send", "live.levels", "cockpit.radar.compose", "ui.ext", "ui.wiggle", "ui.fps", "ui.standby",
    ];

    public static readonly string? Path = System.Environment.GetEnvironmentVariable("I76_FRAME_SECTIONS") is { Length: > 0 } p ? p : null;
    public static readonly bool On = Path is not null;

    private static readonly long[] Ticks = new long[(int)Sec.Count], Bytes = new long[(int)Sec.Count];
    private static int _simTicks, _uploads;
    private static long _uploadBytes;

    /// <summary>Time a section until the scope is disposed (a struct: nothing allocated, nothing done when off).</summary>
    public static Scope Time(Sec s) => On ? new Scope(s) : default;

    public readonly struct Scope : IDisposable
    {
        private readonly int _s;   // the section + 1; 0: off
        private readonly long _t, _b;
        public Scope(Sec s) { _s = (int)s + 1; _b = System.GC.GetAllocatedBytesForCurrentThread(); _t = Stopwatch.GetTimestamp(); }
        public void Dispose()
        {
            if (_s == 0) return;
            Ticks[_s - 1] += Stopwatch.GetTimestamp() - _t;
            Bytes[_s - 1] += System.GC.GetAllocatedBytesForCurrentThread() - _b;
            if (_s - 1 == (int)Sec.SimTick) _simTicks++;
        }
    }

    /// <summary>A texture upload of that many bytes (the cockpit's live textures).</summary>
    public static void Upload(long bytes)
    {
        if (!On) return;
        _uploads++;
        _uploadBytes += bytes;
    }

    // the row's fixed columns, then each section's ms and its kB
    private static readonly string[] Fixed =
    [
        "frame", "t_ms", "wall_ms", "nodes_ms", "deferred_ms", "draw_ms", "after_ms", "process_ms", "physics_ms", "render_cpu_ms", "gpu_ms", "ticks", "gc0", "gc1", "gc2",
        "gc_pause_ms", "alloc_main_kb", "alloc_all_kb", "uploads", "upload_kb",
    ];

    private static bool _installed;

    /// <summary>Once a run, under the root (a mission's reload keeps it).</summary>
    public static void Install(SceneTree tree)
    {
        if (!On || _installed) return;
        _installed = true;
        tree.Root.CallDeferred(Node.MethodName.AddChild, new Writer { Name = "FrameSections", ProcessMode = Node.ProcessModeEnum.Always, ProcessPriority = int.MinValue });
    }

    /// <summary>Closes each frame at the start of the next (render and swap in it) and keeps its row.</summary>
    private sealed partial class Writer : Node
    {
        private const int MaxRows = 60000;
        private readonly int _skipMs = int.TryParse(System.Environment.GetEnvironmentVariable("I76_FRAME_SECTIONS_SKIP_MS"), out int s) ? s : 8000;
        private readonly int _runMs = int.TryParse(System.Environment.GetEnvironmentVariable("I76_FRAME_SECTIONS_RUN_MS"), out int r) ? r : 0;
        private readonly int _cols = Fixed.Length + 2 * (int)Sec.Count;
        private double[] _rows = [];
        private int _n;
        private long _start, _nodesEnd, _preDraw, _postDraw, _first, _alloc, _allAlloc, _frame;
        private int _g0, _g1, _g2;
        private System.TimeSpan _pause;
        private bool _measuring, _done;
        private AllocTypes? _types;

        public override void _Ready()
        {
            _rows = new double[MaxRows * _cols];
            if (System.Environment.GetEnvironmentVariable("I76_FRAME_SECTIONS_TYPES") == "1") _types = new AllocTypes();
            RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
            AddChild(new EndProbe { Name = "FrameSectionsEnd", ProcessMode = ProcessModeEnum.Always, ProcessPriority = int.MaxValue, Owner2 = this });
            RenderingServer.FramePreDraw += PreDraw;
            RenderingServer.FramePostDraw += PostDraw;
            Reset(Stopwatch.GetTimestamp());
        }

        private void PreDraw() => _preDraw = Stopwatch.GetTimestamp();
        private void PostDraw() => _postDraw = Stopwatch.GetTimestamp();

        private void Reset(long now)
        {
            System.Array.Clear(Ticks);
            System.Array.Clear(Bytes);
            _simTicks = _uploads = 0;
            _uploadBytes = 0;
            (_g0, _g1, _g2) = (System.GC.CollectionCount(0), System.GC.CollectionCount(1), System.GC.CollectionCount(2));
            _pause = System.GC.GetTotalPauseDuration();
            _alloc = System.GC.GetAllocatedBytesForCurrentThread();
            _allAlloc = System.GC.GetTotalAllocatedBytes(false);
            _start = now;
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        public override void _Process(double delta)
        {
            long now = Stopwatch.GetTimestamp();
            _frame++;
            if (_done) return;
            if (!_measuring)
            {
                double loaded = FrameProfiler.LoadedAt;
                if (loaded > 0 && now * 1000.0 / Stopwatch.Frequency - loaded >= _skipMs) { _measuring = AllocTypes.Measuring = true; _first = now; }
                Reset(now);
                return;
            }
            if (_n < MaxRows)
            {
                int o = _n * _cols, c = 0;
                var vp = GetViewport().GetViewportRid();
                _rows[o + c++] = _frame - 1;
                _rows[o + c++] = Ms(_start - _first);
                _rows[o + c++] = Ms(now - _start);
                _rows[o + c++] = Ms(_nodesEnd - _start);
                // after the nodes: the deferred calls and redraws up to the renderer's draw, the draw, and from its end to this frame
                // (the next frame's physics steps, the process_frame signal, the frame pacing)
                bool drawn = _preDraw > _nodesEnd && _postDraw > _preDraw && _postDraw < now;
                _rows[o + c++] = drawn ? Ms(_preDraw - _nodesEnd) : -1;
                _rows[o + c++] = drawn ? Ms(_postDraw - _preDraw) : -1;
                _rows[o + c++] = drawn ? Ms(now - _postDraw) : -1;
                _rows[o + c++] = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
                _rows[o + c++] = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000;
                _rows[o + c++] = RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
                _rows[o + c++] = RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                _rows[o + c++] = _simTicks;
                _rows[o + c++] = System.GC.CollectionCount(0) - _g0;
                _rows[o + c++] = System.GC.CollectionCount(1) - _g1;
                _rows[o + c++] = System.GC.CollectionCount(2) - _g2;
                _rows[o + c++] = (System.GC.GetTotalPauseDuration() - _pause).TotalMilliseconds;
                _rows[o + c++] = (System.GC.GetAllocatedBytesForCurrentThread() - _alloc) / 1024.0;
                _rows[o + c++] = (System.GC.GetTotalAllocatedBytes(false) - _allAlloc) / 1024.0;
                _rows[o + c++] = _uploads;
                _rows[o + c++] = _uploadBytes / 1024.0;
                for (int i = 0; i < (int)Sec.Count; i++) _rows[o + c++] = Ms(Ticks[i]);
                for (int i = 0; i < (int)Sec.Count; i++) _rows[o + c++] = Bytes[i] / 1024.0;
                _n++;
            }
            Reset(now);
            if (_runMs > 0 && (Ms(now - _first) >= _runMs || FrameProfiler.Died))
            {
                if (FrameProfiler.Died) GD.Print("frame-sections: the player's car destroyed: the window ends there");
                Write();
                GetTree().Quit();
            }
        }

        public override void _ExitTree()
        {
            _types?.Print();
            RenderingServer.FramePreDraw -= PreDraw;
            RenderingServer.FramePostDraw -= PostDraw;
            Write();
        }

        private void Write()
        {
            if (_done) return;
            _done = true;
            AllocTypes.Measuring = false;
            using var w = new System.IO.StreamWriter(Path!);
            w.WriteLine(string.Join(",", Fixed) + "," + string.Join(",", SecNames.Select(n => n + "_ms")) + "," + string.Join(",", SecNames.Select(n => n + "_kb")));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _n; i++)
            {
                sb.Clear();
                for (int c = 0; c < _cols; c++) { if (c > 0) sb.Append(','); sb.Append(_rows[i * _cols + c].ToString("0.###", CultureInfo.InvariantCulture)); }
                w.WriteLine(sb.ToString());
            }
            GD.Print($"frame-sections: {_n} frames to {Path}");
        }

        private sealed partial class EndProbe : Node
        {
            public Writer? Owner2;
            public override void _Process(double delta) { if (Owner2 is { } o) o._nodesEnd = Stopwatch.GetTimestamp(); }
        }
    }

    /// <summary>The runtime's allocation samples (GCAllocationTick: about one every 100 kB, naming the type that crossed it), tallied by thread and type.</summary>
    private sealed class AllocTypes : System.Diagnostics.Tracing.EventListener
    {
        /// <summary>Only the measured window's samples are counted (the samples come a little late, on the listener's thread).</summary>
        public static volatile bool Measuring;
        private readonly Dictionary<(long Thread, string Type), long> _bytes = [];
        private readonly Dictionary<long, long> _threads = [];
        private readonly object _lock = new();

        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
        }

        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (!Measuring || e.EventName is null || !e.EventName.StartsWith("GCAllocationTick") || e.PayloadNames is null || e.Payload is null) return;
            int ti = e.PayloadNames.IndexOf("TypeName"), ai = e.PayloadNames.IndexOf("AllocationAmount64");
            if (ai < 0) ai = e.PayloadNames.IndexOf("AllocationAmount");
            string type = ti >= 0 ? e.Payload[ti] as string ?? "?" : "?";
            long amount = ai >= 0 ? System.Convert.ToInt64(e.Payload[ai]) : 100_000;
            lock (_lock)
            {
                _bytes[(e.OSThreadId, type)] = _bytes.GetValueOrDefault((e.OSThreadId, type)) + amount;
                _threads[e.OSThreadId] = _threads.GetValueOrDefault(e.OSThreadId) + amount;
            }
        }

        public void Print()
        {
            lock (_lock)
                foreach (var (th, total) in _threads.OrderByDescending(kv => kv.Value).Take(4))
                {
                    GD.Print($"frame-sections types: thread {th} {total / 1048576.0:0.0} MB sampled");
                    foreach (var ((_, type), b) in _bytes.Where(kv => kv.Key.Thread == th).OrderByDescending(kv => kv.Value).Take(25))
                        GD.Print($"frame-sections types:   {b / 1048576.0,7:0.00} MB {type}");
                }
            Dispose();
        }
    }
}