using System.Globalization;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_FRAME_PROFILE=1: where a frame's time goes. On the GPU, the rendering
/// server's own timestamps (the ones Godot's visual profiler and its
/// <c>--gpu-profile</c> print read) grouped into passes: shadow maps, depth
/// pre-pass, SSAO, opaque, sky, transparent, post, 2D; on the CPU, the
/// nodes' process step (all _Process calls), the wall time between frames, the sim's own step (SimDriver), the terrain's level update
/// and the renderer's frame setup and command recording.
///
/// Godot only captures the timestamps when its GPU profile is on, which only
/// the command line (<c>--gpu-profile</c>) or the project setting
/// debug/settings/stdout/print_gpu_profile turn on: tools/shot.sh adds the
/// flag when I76_FRAME_PROFILE is set. Without it only the totals are there.
///
/// Prints a line every I76_FRAME_PROFILE_EVERY frames (default 60) and a
/// table of medians when the game quits, past the first
/// I76_FRAME_PROFILE_SKIP frames (default 30: loading, shader compiles).
/// I76_HIDE=Name,Name hides the 3D nodes of those names (Terrain, HorizonTerrain, Placements, Vegetation, ...) as soon as they exist (set I76_FRAME_PROFILE_SKIP past that), to cost a part of the scene.
/// The last line, <c>frame-profile-result:</c>, is key=value medians for
/// tools/frame-profile.sh.
///
/// For a timed window in a live run (vsync off, no fixed fps): I76_FRAME_PROFILE_SKIP_MS=N skips every frame until N ms after
/// the mission counted as loaded (<see cref="MarkLoaded"/>; from the first frame when nothing is loaded), I76_FRAME_PROFILE_RUN_MS=N
/// quits the game N ms after the first measured frame, printing the table and the result line as a quit does; I76_FRAME_PROFILE_CSV=PATH
/// writes every measured frame's values (one row a frame, a column a key) and I76_FRAME_PROFILE_SHOT=PATH saves the last measured
/// frame as a PNG. The VRAM monitors (vram_tex_mb, vram_video_mb) and the process step (cpu.process) are in the series too.
/// </summary>
public partial class FrameProfiler : Node
{
    public static bool Enabled => System.Environment.GetEnvironmentVariable("I76_FRAME_PROFILE") == "1";
    private static int Env(string name, int fallback) => int.TryParse(System.Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;

    /// <summary>Milliseconds of the terrain's level update last frame (TerrainNode).</summary>
    public static double TerrainUpdateMs;
    /// <summary>The vegetation's chunk update this frame (VegetationNode: placing, attaching, tiers), milliseconds.</summary>
    public static double VegUpdateMs;

    private readonly int _skip = Env("I76_FRAME_PROFILE_SKIP", 30), _every = Env("I76_FRAME_PROFILE_EVERY", 60);
    private readonly int _skipMs = Env("I76_FRAME_PROFILE_SKIP_MS", 0), _runMs = Env("I76_FRAME_PROFILE_RUN_MS", 0);
    private readonly string? _csv = System.Environment.GetEnvironmentVariable("I76_FRAME_PROFILE_CSV"), _shot = System.Environment.GetEnvironmentVariable("I76_FRAME_PROFILE_SHOT");
    private static double _loadedAt;
    private double _firstMeasured;
    private bool _quitting;

    /// <summary>The player's car was destroyed (Main): a timed window ends there, as a partial result (<c>frame-profile-death:</c> before the result line), rather than measuring the death orbit to its end.</summary>
    public static void PlayerDied() => _died = true;
    private static bool _died;

    /// <summary>The mission is built and its first tick done (Main): I76_FRAME_PROFILE_SKIP_MS counts from here.</summary>
    public static void MarkLoaded() => _loadedAt = Now();
    /// <summary>When the mission counted as loaded (Stopwatch milliseconds; 0: not yet), for <see cref="FrameSections"/>'s window too.</summary>
    public static double LoadedAt => _loadedAt;
    /// <summary>The player's car was destroyed since the start (<see cref="PlayerDied"/>).</summary>
    public static bool Died => _died;
    private readonly Dictionary<string, List<double>> _series = [];
    private readonly Dictionary<string, double> _frame = [];
    private readonly List<string> _order = [];
    private ulong _lastCaptured;
    private readonly List<string> _hide = (System.Environment.GetEnvironmentVariable("I76_HIDE") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    private int _frames, _gc0;
    private bool _sawTimestamps;
    private Probe? _simStart;
    private double _simMs, _nodesMs, _start, _lastWall;

    /// <summary>The order passes print in; anything else lands in "other".</summary>
    private static readonly string[] Passes = ["shadows", "depth", "ssao", "opaque", "sky", "msaa", "transparent", "post", "setup", "2d", "smoke", "other"];

    private static string PassOf(string name) => name switch
    {
        "Smoke Layer" => "smoke",   // SmokeLayer's own passes, timed with --gpu-profile
        _ when name.Contains("Shadow") && !name.StartsWith("Setup") => "shadows",
        _ when name.Contains("Depth Pre-Pass") => "depth",
        _ when name.Contains("SSAO") || name.Contains("SSIL") => "ssao",
        "Render Opaque Pass" or "Render Motion Pass" => "opaque",
        _ when name.Contains("Sky") => "sky",
        "Resolve MSAA" or "Resolve" => "msaa",
        "Render 3D Transparent Pass" or "Copy Screen Texture" or "Copy Depth Texture" => "transparent",
        "Tonemap" or "Glow" or "Auto exposure" or "Depth of Field" or "SMAA" or "TAA" or "FSR2" => "post",
        _ when name.StartsWith("Render Canvas") || name.Contains("2D") => "2d",
        _ when name.StartsWith("Setup") || name.StartsWith("Prepare") || name.StartsWith("Pre Opaque") || name.StartsWith("Cull") => "setup",
        _ => "other",
    };

    public override void _Ready()
    {
        ProcessPriority = int.MinValue;   // first in the frame: its process time is the whole frame's
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GetTree().NodeAdded += OnNodeAdded;
        // the last node to process: the span from this one's start is every node's _Process
        AddChild(new Probe { ProcessPriority = int.MaxValue, Name = "ProcessEnd", OnTick = () => _nodesMs = Now() - _start });
    }

    /// <summary>A probe each side of the sim's driver, which is moved to a priority of its own for that.</summary>
    private void OnNodeAdded(Node n)
    {
        if (n is not I76.Game.Sim.SimDriver sd || _simStart is not null) return;
        sd.ProcessPriority = -1000;   // still before Main (0), as it is at -1
        _simStart = new Probe { ProcessPriority = -1001, Name = "SimProbeStart", OnTick = () => _simMs = -Now() };
        var end = new Probe { ProcessPriority = -999, Name = "SimProbeEnd", OnTick = () => _simMs += Now() };
        CallDeferred(Node.MethodName.AddChild, _simStart);
        CallDeferred(Node.MethodName.AddChild, end);
    }

    private static double Now() => System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private void Add(string key, double v)
    {
        if (!_series.ContainsKey(key)) { _series[key] = []; _order.Add(key); }
        _series[key].Add(v);
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_hide.Count > 0 && _frames % 5 == 0)
            foreach (var name in _hide.ToArray())
                foreach (var hn in GetTree().Root.FindChildren(name, "Node3D", true, false)) { ((Node3D)hn).Visible = false; _hide.Remove(name); GD.Print($"hidden at frame {_frames}: {hn.GetPath()}"); }
        double now = Now(), wall = _lastWall > 0 ? now - _lastWall : 0;
        _lastWall = _start = now;
        var vp = GetViewport().GetViewportRid();
        _frame.Clear();
        double gpuTotal = ReadTimestamps();
        bool warm = _skipMs > 0 && now - (_loadedAt > 0 ? _loadedAt : _start) < _skipMs;
        if (_frames <= _skip || warm) { _simMs = 0; _gc0 = GC.CollectionCount(0); return; }
        if (_firstMeasured == 0) { _firstMeasured = now; GD.Print($"frame-profile: window starts at stopwatch {now:0.0} ms"); }   // t_ms + this: the clock of an in-run A | B (I76_SMOKE_HAZE_AB)
        int n0 = _frames - _skip - 1;   // frames measured before this one: the collections so far, per 100 frames
        foreach (var p in Passes) Add("gpu." + p, _frame.GetValueOrDefault(p));
        foreach (var (k, v) in _frame) if (k.StartsWith("vp")) Add("gpu." + k, v);
        Add("gpu.total", gpuTotal);
        Add("gpu.viewport", RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        Add("cpu.render", RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
        Add("cpu.setup", RenderingServer.GetFrameSetupTimeCpu());
        Add("cpu.wall", wall);
        Add("cpu.nodes", _nodesMs);
        Add("cpu.physics", Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
        Add("cpu.sim", _simMs);
        Add("cpu.terrain", TerrainUpdateMs);
        Add("cpu.veg", VegUpdateMs);
        Add("prims_k", Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame) / 1000);
        Add("gc0_per_100", (GC.CollectionCount(0) - _gc0) * 100.0 / (n0 + 1));
        Add("draws", Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        Add("objects", Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
        Add("cpu.process", Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
        Add("vram_tex_mb", Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0);
        Add("vram_video_mb", Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0);
        Add("t_ms", now - _firstMeasured);
        if (Views) AddViews();
        if (Census && !_censusDone && now - _firstMeasured > 2000) { _censusDone = true; DrawCensus(); }
        _simMs = 0;
        if (_runMs > 0 && !_quitting && (now - _firstMeasured >= _runMs || _died))
        {
            _quitting = true;
            if (_died) GD.Print($"frame-profile-death: the player's car destroyed {now - _firstMeasured:0} ms into the window");
            if (_shot is { Length: > 0 }) GetViewport().GetTexture().GetImage().SavePng(_shot);
            GetTree().Quit();
            return;
        }
        int n = _series["gpu.total"].Count;
        if (n % _every == 0) GD.Print($"frame-profile: {n} frames, " + Line(k => k is "gpu.total" or "gpu.viewport" or "cpu.render" or "cpu.wall" or "cpu.nodes" or "cpu.sim" || k.StartsWith("gpu.") && Median(_series[k]) >= 0.05));
    }

    /// <summary>I76_FRAME_PROFILE_VIEWS=1: each viewport's objects, primitives and draw calls, seen and in its shadow passes, as columns (where the frame's draws come from).</summary>
    private static readonly bool Views = System.Environment.GetEnvironmentVariable("I76_FRAME_PROFILE_VIEWS") == "1";
    private readonly List<(string Name, Rid Vp)> _views = [];
    private int _viewsAt = -1000;

    private void AddViews()
    {
        if (_frames - _viewsAt >= 120)
        {
            _viewsAt = _frames;
            _views.Clear();
            _views.Add(("root", GetViewport().GetViewportRid()));
            foreach (var n in GetTree().Root.FindChildren("*", "SubViewport", true, false))
                if (n is SubViewport sv && !sv.Disable3D) { _views.Add((sv.Name, sv.GetViewportRid())); RenderingServer.ViewportSetMeasureRenderTime(sv.GetViewportRid(), true); }
        }
        foreach (var (name, vp) in _views)
        {
            if (!vp.IsValid) continue;
            Add($"v.{name}.draws", RenderingServer.ViewportGetRenderInfo(vp, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.DrawCallsInFrame));
            Add($"v.{name}.objs", RenderingServer.ViewportGetRenderInfo(vp, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.ObjectsInFrame));
            Add($"v.{name}.prims_k", RenderingServer.ViewportGetRenderInfo(vp, RenderingServer.ViewportRenderInfoType.Visible, RenderingServer.ViewportRenderInfo.PrimitivesInFrame) / 1000.0);
            Add($"v.{name}.sh_draws", RenderingServer.ViewportGetRenderInfo(vp, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.DrawCallsInFrame));
            Add($"v.{name}.sh_prims_k", RenderingServer.ViewportGetRenderInfo(vp, RenderingServer.ViewportRenderInfoType.Shadow, RenderingServer.ViewportRenderInfo.PrimitivesInFrame) / 1000.0);
            // the renderer's own measure of the view: its culling and command recording on the main thread, and its time on the card
            Add($"v.{name}.cpu_ms", RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp));
            Add($"v.{name}.gpu_ms", RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        }
    }

    /// <summary>
    /// I76_FRAME_PROFILE_CENSUS=1: once, 2 s into the window, the visible geometry in the main camera's frustum (an AABB test, as the
    /// engine's culling before its occlusion and visibility ranges) counted by kind: nodes, surfaces (a draw each, before the engine's
    /// own instancing of repeats), shadow casters, and how many distinct mesh+material pairs (what the engine can instance).
    /// </summary>
    private static readonly bool Census = System.Environment.GetEnvironmentVariable("I76_FRAME_PROFILE_CENSUS") == "1";
    private bool _censusDone;

    private void DrawCensus()
    {
        var cam = GetViewport().GetCamera3D();
        if (cam is null) return;
        var planes = cam.GetFrustum();
        var rows = new SortedDictionary<string, CensusRow>();
        void Walk(Node n, string kind)
        {
            if (n is Node3D n3 && !n3.Visible) return;
            if (n is SubViewport) return;
            string k = kind;
            if (n is GeometryInstance3D gi)
            {
                k = CensusKind(n, kind);
                var box = gi.GlobalTransform * gi.GetAabb();
                bool inside = true;
                foreach (var p in planes)
                {
                    // the box's corner farthest along the plane's normal (planes point out of the frustum)
                    var c = box.Position + new Vector3(p.Normal.X < 0 ? box.Size.X : 0, p.Normal.Y < 0 ? box.Size.Y : 0, p.Normal.Z < 0 ? box.Size.Z : 0);
                    if (p.DistanceTo(c) > 0) { inside = false; break; }
                }
                if (inside && (gi.Layers & cam.CullMask) != 0)
                {
                    var r = rows.TryGetValue(k, out var e) ? e : rows[k] = new CensusRow();
                    int surfaces = 1;
                    if (gi is MeshInstance3D mi && mi.Mesh is { } mesh)
                    {
                        surfaces = mesh.GetSurfaceCount();
                        for (int s = 0; s < surfaces; s++)
                        {
                            var mat = mi.MaterialOverride ?? mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s);
                            r.Pairs.Add((mesh.GetRid().Id, mat?.GetRid().Id ?? 0));
                        }
                    }
                    else if (gi is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } m2) { surfaces = m2.GetSurfaceCount(); r.Pairs.Add((m2.GetRid().Id, mm.MaterialOverride?.GetRid().Id ?? 0)); }
                    if (r.Nodes == 0) r.Example = n.GetPath();
                    r.Nodes++; r.Surfaces += surfaces;
                    if (gi.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off) r.Casters += surfaces;
                }
            }
            foreach (var c in n.GetChildren()) Walk(c, k);
        }
        Walk(GetTree().Root, "root");
        var sb = new System.Text.StringBuilder("frame-census: kind nodes surfaces casters distinct-pairs (main camera's frustum)\n");
        foreach (var (k, r) in rows.OrderByDescending(kv => kv.Value.Surfaces))
            sb.Append($"frame-census: {k,-40} {r.Nodes,6} {r.Surfaces,6} {r.Casters,6} {r.Pairs.Count,6}\n");
        GD.Print(sb.ToString().TrimEnd());
    }

    private sealed class CensusRow { public int Nodes, Surfaces, Casters; public string Example = ""; public readonly HashSet<(ulong, ulong)> Pairs = []; }

    /// <summary>A node's kind for the census: the first named ancestor below the mission's roots, its digits dropped (chunk_12_40 is chunk).</summary>
    private static string CensusKind(Node n, string parentKind)
    {
        string Strip(string s) => new string(s.Where(ch => !char.IsDigit(ch)).ToArray()).TrimEnd('_', '-', '@');
        var path = n.GetPath().ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        // root/Main/<a>/<b>/...: keep three levels below Main
        int i = Array.IndexOf(path, "Main");
        var parts = (i >= 0 ? path.Skip(i + 1) : path.Skip(1)).Take(3).Select(Strip);
        return string.Join("/", parts) + ":" + n.GetClass();
    }

    /// <summary>The previous frame's captured timestamps into this frame's passes; returns the GPU span of them all.</summary>
    private double ReadTimestamps()
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return 0;
        uint count = rd.GetCapturedTimestampsCount();
        ulong frame = rd.GetCapturedTimestampsFrame();
        if (count < 2 || frame == _lastCaptured) return 0;
        _lastCaptured = frame;
        _sawTimestamps = true;
        ulong first = rd.GetCapturedTimestampGpuTime(0), last = first;
        string vpName = "";
        for (uint i = 0; i + 1 < count; i++)
        {
            string name = rd.GetCapturedTimestampName(i);
            ulong a = rd.GetCapturedTimestampGpuTime(i), b = rd.GetCapturedTimestampGpuTime(i + 1);
            last = Math.Max(last, b);
            if (name.StartsWith("> Render Viewport ")) vpName = "vp" + name["> Render Viewport ".Length..];
            if (name.Length > 0 && (name[0] == '<' || name[0] == '>')) continue;
            double ms = (b - a) / 1e6;   // nanoseconds
            if (b < a) continue;
            string pass = PassOf(name);
            _frame[pass] = _frame.GetValueOrDefault(pass) + ms;
            if (vpName != "") _frame[vpName] = _frame.GetValueOrDefault(vpName) + ms;
        }
        return (last - first) / 1e6;
    }

    private static double Median(List<double> v)
    {
        if (v.Count == 0) return 0;
        var s = v.Order().ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    private string Line(Func<string, bool> keep) =>
        string.Join(" ", _order.Where(keep).Select(k => string.Create(CultureInfo.InvariantCulture, $"{k}={Median(_series[k]):0.000}")));

    public override void _ExitTree()
    {
        if (!_series.TryGetValue("gpu.total", out var tot) || tot.Count == 0) { GD.Print("frame-profile: no frames past the warm-up"); return; }
        if (!_sawTimestamps) GD.Print("frame-profile: no GPU timestamps (run Godot with --gpu-profile; tools/shot.sh adds it when I76_FRAME_PROFILE=1)");
        var sb = new System.Text.StringBuilder($"frame-profile: {tot.Count} frames past the first {_skip}, medians (mean, p90) in ms\n");
        foreach (var k in _order)
        {
            var s = _series[k].Order().ToList();
            if (s[^1] <= 0 && k.StartsWith("gpu.")) continue;
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"  {k,-18} {Median(s),8:0.000}  ({s.Average():0.000}, {s[(int)(0.9 * (s.Count - 1))]:0.000})\n"));
        }
        GD.Print(sb.ToString().TrimEnd());
        // the slow frames too: hitches show in the 95th percentile, not the median
        string P95(string k) { var s = _series[k].Order().ToList(); return s[(int)(0.95 * (s.Count - 1))].ToString("0.000", CultureInfo.InvariantCulture); }
        GD.Print("frame-profile-result: frames=" + tot.Count + " " + Line(_ => true) + $" cpu.wall.p95={P95("cpu.wall")} cpu.nodes.p95={P95("cpu.nodes")} gpu.total.p95={P95("gpu.total")}");
        if (_csv is { Length: > 0 })
        {
            using var w = new System.IO.StreamWriter(_csv);
            w.WriteLine(string.Join(",", _order));
            for (int i = 0; i < tot.Count; i++)
                w.WriteLine(string.Join(",", _order.Select(k => i < _series[k].Count ? _series[k][i].ToString("0.###", CultureInfo.InvariantCulture) : "")));
        }
    }

    /// <summary>A node that only calls back when it processes.</summary>
    private sealed partial class Probe : Node
    {
        public Action? OnTick;
        public override void _Process(double delta) => OnTick?.Invoke();
    }
}
