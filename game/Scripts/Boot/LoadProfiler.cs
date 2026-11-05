using System.Collections.Generic;
using System.Linq;
using Godot;

namespace I76.Game.Boot;

/// <summary>
/// I76_LOAD_PROFILE=1: a mission start's phases, timed (docs/perf/loadtime.md). Every <see cref="I76.Game.World.MissionBuilder.Trace"/>
/// stamp (and the few of Main's own) closes a phase on the thread that stamps it: the time since that thread's previous stamp,
/// with the thread (main or the build's) and what was made in it (textures, original or a pack's upscale, and their bytes; the
/// renderer's pipeline compilations, counted on the main thread). The clock starts at the mission's click in the shell (or the
/// engine's start for a command-line mission). When the loading card goes, the phases go to a CSV
/// (I76_LOAD_PROFILE_CSV, default user://load-profile.csv) and a one-line summary to the log ("load-profile-result: ...").
/// I76_LOAD_PROFILE_QUIT=1 then quits, for the harness (tools/load-time/).
/// </summary>
public static class LoadProfiler
{
    public static readonly bool On = System.Environment.GetEnvironmentVariable("I76_LOAD_PROFILE") == "1";

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static long _origin;   // ms on Clock of the click (0: the engine's start)
    private static int _mainThread = -1;

    private sealed class Acc { public long Last = -1; public int TexOrig, TexHd; public long BytesOrig, BytesHd; public double TexMs; }
    [System.ThreadStatic] private static Acc? _acc;
    private static Acc Mine => _acc ??= new Acc();

    public sealed record Row(long StartMs, long EndMs, string Phase, string Thread, int TexOrig, int TexHd, long BytesOrig, long BytesHd, double TexMs, long Pipelines);
    private static readonly List<Row> Rows = [];
    private static long _pipelines = -1;
    private static readonly Dictionary<string, long> Events = [];

    /// <summary>The main thread is the one that calls this first (Main._Ready).</summary>
    public static void SetMain() { if (_mainThread < 0) _mainThread = System.Environment.CurrentManagedThreadId; }

    public static bool IsMain => System.Environment.CurrentManagedThreadId == _mainThread;

    /// <summary>The player chose the mission: the clock's zero, everything before it dropped.</summary>
    public static void Click()
    {
        if (!On) return;
        lock (Rows) { Rows.Clear(); Events.Clear(); _origin = Clock.ElapsedMilliseconds; }
        _acc = null;
        _pipelines = -1;
        Event("click");
    }

    private static long Now => Clock.ElapsedMilliseconds - _origin;

    /// <summary>A moment, not a phase (the card's first frame, the movie's start): kept by name, the first of each.</summary>
    public static void Event(string what)
    {
        if (!On) return;
        lock (Rows) Events.TryAdd(what, Now);
    }

    public static long? EventAt(string what) { lock (Rows) return Events.TryGetValue(what, out var t) ? t : null; }

    /// <summary>Closes the phase that ends at this stamp on the calling thread.</summary>
    public static void Mark(string stage)
    {
        if (!On) return;
        var a = Mine;
        long now = Now;
        long start = a.Last < 0 ? now : a.Last;
        long pipes = 0;
        if (IsMain)
        {
            long p = Pipelines();
            pipes = _pipelines < 0 ? 0 : p - _pipelines;
            _pipelines = p;
        }
        string thread = IsMain ? "main" : System.Threading.Thread.CurrentThread.Name is { Length: > 0 } n ? n : $"worker{System.Environment.CurrentManagedThreadId}";
        lock (Rows) Rows.Add(new Row(start, now, stage, thread, a.TexOrig, a.TexHd, a.BytesOrig, a.BytesHd, a.TexMs, pipes));
        a.Last = now; a.TexOrig = a.TexHd = 0; a.BytesOrig = a.BytesHd = 0; a.TexMs = 0;
    }

    /// <summary>The calling thread's next phase starts now (a thread's first stamp otherwise opens it).</summary>
    public static void Open() { if (On) Mine.Last = Now; }

    /// <summary>A texture made for the GPU (TextureFactory): counted in the calling thread's open phase.</summary>
    public static void Texture(bool hd, long bytes, double ms)
    {
        if (!On) return;
        var a = Mine;
        if (hd) { a.TexHd++; a.BytesHd += bytes; } else { a.TexOrig++; a.BytesOrig += bytes; }
        a.TexMs += ms;
    }

    private static long Pipelines()
    {
        long sum = 0;
        foreach (var k in new[] { RenderingServer.RenderingInfo.PipelineCompilationsCanvas, RenderingServer.RenderingInfo.PipelineCompilationsMesh,
                     RenderingServer.RenderingInfo.PipelineCompilationsSurface, RenderingServer.RenderingInfo.PipelineCompilationsDraw, RenderingServer.RenderingInfo.PipelineCompilationsSpecialization })
            sum += (long)RenderingServer.GetRenderingInfo(k);
        return sum;
    }

    /// <summary>The card is gone: the CSV and the summary line.</summary>
    public static void Finish(string mission, string extra = "")
    {
        if (!On) return;
        Mark("loading card gone");
        Event("mission");
        List<Row> rows; Dictionary<string, long> ev;
        lock (Rows) { rows = [.. Rows]; ev = new(Events); }
        var path = System.Environment.GetEnvironmentVariable("I76_LOAD_PROFILE_CSV") is { Length: > 0 } p ? p : ProjectSettings.GlobalizePath("user://load-profile.csv");
        try
        {
            using var w = new System.IO.StreamWriter(path);
            w.WriteLine("mission,start_ms,end_ms,ms,phase,thread,tex_orig,tex_hd,mb_orig,mb_hd,tex_ms,pipelines");
            foreach (var r in rows)
                w.WriteLine($"{mission},{r.StartMs},{r.EndMs},{r.EndMs - r.StartMs},\"{r.Phase.Replace("\"", "'")}\",{r.Thread},{r.TexOrig},{r.TexHd},{r.BytesOrig / 1048576.0:0.0},{r.BytesHd / 1048576.0:0.0},{r.TexMs:0},{r.Pipelines}");
            foreach (var (k, v) in ev.OrderBy(e => e.Value)) w.WriteLine($"{mission},{v},{v},0,\"event: {k}\",event,0,0,0,0,0,0");
        }
        catch (System.Exception ex) { GD.PrintErr($"load profile: {path}: {ex.Message}"); }
        int tex = rows.Sum(r => r.TexOrig + r.TexHd), hd = rows.Sum(r => r.TexHd);
        double mb = rows.Sum(r => r.BytesOrig + r.BytesHd) / 1048576.0;
        string E(string k) => ev.TryGetValue(k, out var t) ? t.ToString() : "-";
        GD.Print($"load-profile-result: mission={mission} total_ms={E("mission")} card_ms={E("card first frame")} movie_ms={E("movie start")} movie_over_ms={E("movie over")} built_ms={E("build done")} "
            + $"movie_shown={I76.Game.Ui.MoviePlayer.Last.Shown} movie_dropped={I76.Game.Ui.MoviePlayer.Last.Dropped} movie_late={I76.Game.Ui.MoviePlayer.Last.Late} movie_worst_ms={I76.Game.Ui.MoviePlayer.Last.WorstMs:0} audio_skips={I76.Game.Ui.MoviePlayer.Last.AudioSkips} "
            + $"attach_worst_step_ms={Main.AttachWorstStepMs:0} attach_over_budget={Main.AttachStepsOverBudget:0} card_overrun_ms={Main.CardOverrunMs:0} "
            + $"ws_mb={System.Environment.WorkingSet >> 20} managed_mb={System.GC.GetTotalMemory(false) >> 20} vram_tex_mb={(long)Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) >> 20} vram_mb={(long)Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) >> 20} "
            + $"main_ms={rows.Where(r => r.Thread == "main").Sum(r => r.EndMs - r.StartMs)} build_ms={rows.Where(r => r.Thread == "mission-build").Sum(r => r.EndMs - r.StartMs)} textures={tex} hd={hd} tex_mb={mb:0} pipelines={rows.Sum(r => r.Pipelines)} {extra}csv={path}");
        foreach (var r in rows.OrderByDescending(r => r.EndMs - r.StartMs).Take(8))
            GD.Print($"load-profile: {r.EndMs - r.StartMs,6} ms  {r.Thread,-13} {r.Phase} (tex {r.TexOrig}+{r.TexHd} hd, {(r.BytesOrig + r.BytesHd) / 1048576.0:0} MB, {r.Pipelines} pipelines)");
    }

    /// <summary>I76_LOAD_PROFILE_QUIT=1: the harness's run is over once the mission is up (after a few frames of play).</summary>
    public static bool QuitAfter => On && System.Environment.GetEnvironmentVariable("I76_LOAD_PROFILE_QUIT") == "1";
}
