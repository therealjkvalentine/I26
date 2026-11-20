using Godot;
using I76.Formats.Vfs;
using I76.Game.World;

namespace I76.Game.Boot;

/// <summary>
/// The port's: the next mission's world built on a thread of its own while a shell screen is up that already knows the mission (the
/// trip's field salvage and garage, the T17 CHOOSE YOUR VEHICLE), at low priority so that the screen stays smooth. The build is the
/// part of a mission's load that is the same whatever the player still picks there: the terrain, the roads, the scenery, the sky
/// and horizon, the mission's own textures, materials and meshes (MissionBuilder.Build). What does depend on the pick waits for the
/// confirm: the player's car's parts and the cockpit's frames (Main.PrefetchCars, PrefetchCockpitArt: the trip's car is written at
/// the confirm, vehscn.vcf) and the whole rest of the load in the scene (Main.AttachSteps), which nothing of the shell's may run under.
/// The mission start takes the build when it is the one it would make (<see cref="Key"/>: the mission, the install, the look, the
/// graphics settings the build reads); anything else, or leaving the screen another way, cancels it: the build stops at its next
/// stage and what it made is freed. I76_PRELOAD=0 leaves it out (the screen's frame times are still logged, to compare).
/// </summary>
public static class MissionPreload
{
    /// <summary>What a build depends on: a start whose key differs builds again.</summary>
    public sealed record Key(string Install, string Mission, bool Mode1997, bool SkipVehicles, string Settings)
    {
        /// <summary>The settings a mission's build reads (the look, the plants, the HD textures, Graphic Detail's rows).</summary>
        public static string SettingsOf(I76.Game.Shell.ShellSettings s) =>
            $"{s.Look1997}|{s.Vegetation}|{s.WorldTextures}|{s.CarTextures}|{string.Join(',', s.Graphics)}";
    }

    public sealed class Job
    {
        public required Key Key { get; init; }
        public required GameInstall Install { get; init; }
        public required AssetVfs Vfs { get; init; }
        public required string Msn { get; init; }
        public required MissionBuilder Builder { get; init; }
        public required System.Diagnostics.Stopwatch Clock { get; init; }
        public System.Threading.Thread Thread { get; set; } = null!;
        public Node3D? World;
        public System.Exception? Error;
        public readonly System.Threading.CancellationTokenSource Cts = new();
        public long MemoryBefore, VideoBefore;
        public double BuiltMs = -1;
    }

    public static readonly bool On = System.Environment.GetEnvironmentVariable("I76_PRELOAD") != "0";
    /// <summary>The textures' pace while the screen is up, bytes a second (I76_PRELOAD_MBPS overrides it, megabytes).</summary>
    private static readonly int PaceBytesPerSecond = (int.TryParse(System.Environment.GetEnvironmentVariable("I76_PRELOAD_MBPS"), out int mb) && mb > 0 ? mb : 48) << 20;
    private static Job? _job;

    /// <summary>A preload under way or done, not yet taken.</summary>
    public static bool Pending => _job is not null;

    /// <summary>Starts building <paramref name="key"/>'s mission (nothing when it is the one already building); another one is cancelled.</summary>
    public static void Start(Key key, System.Func<Job> make)
    {
        Watch.Begin(key.Mission);
        if (!On) return;
        if (_job?.Key == key) return;
        Cancel("another mission", false);
        Job job;
        try { job = make(); }
        catch (System.Exception ex) { GD.PrintErr($"preload {key.Mission}: {ex.Message}"); return; }
        (job.MemoryBefore, job.VideoBefore) = Memory();
        var builder = job.Builder;
        builder.OffMainThread = true;
        builder.Cancel = job.Cts.Token;
        job.Thread = new System.Threading.Thread(() =>
        {
            MainThread.Allowed = true;   // (the debug builds' check: this thread's engine work is the renderer's queued calls)
            // its textures sent to the card at a pace the screen's frames can take (until the start takes it: full speed then)
            I76.Game.Assets.TextureFactory.Paced = true;
            I76.Game.Assets.TextureFactory.PacedBytesPerSecond = PaceBytesPerSecond;
            I76.Game.Assets.TextureFactory.PrefetchThreads = System.Math.Max(1, System.Environment.ProcessorCount / 4);   // the screen first
            try { job.World = builder.Build(job.Msn); }
            catch (System.OperationCanceledException) { }
            catch (System.Exception ex) { job.Error = ex; }
            job.BuiltMs = job.Clock.Elapsed.TotalMilliseconds;
        }, 64 << 20) { IsBackground = true, Priority = System.Threading.ThreadPriority.Lowest, Name = "mission-preload" };
        _job = job;
        EngineThreads.Start(job.Thread);   // (a quit meanwhile waits for it: it stops at its next stage)
        RunLog.Print($"preload: {key.Mission} building beside the shell");
    }

    /// <summary>The preload of <paramref name="key"/>'s mission for the start to take over (its thread may still be running), else null; one of another is cancelled.</summary>
    public static Job? Take(Key key)
    {
        Watch.End();
        if (_job is not { } job) return null;
        if (job.Key != key) { Cancel($"the start is {key.Mission}, not {job.Key.Mission}", false); return null; }
        _job = null;
        I76.Game.Assets.TextureFactory.PacedBytesPerSecond = 0;
        if (job.Thread.IsAlive) job.Thread.Priority = System.Threading.ThreadPriority.Normal;
        var (mem, video) = Memory();
        RunLog.Print($"preload: {key.Mission} taken by the start, {(job.Thread.IsAlive ? $"still building ({job.Clock.ElapsedMilliseconds} ms in)" : $"built in {job.BuiltMs:0} ms")}, "
            + $"held meanwhile: {(mem - job.MemoryBefore) >> 20} MB more of the process's memory, {(video - job.VideoBefore) >> 20} MB more video memory");
        return job;
    }

    /// <summary>The preload dropped (the screen left another way, another mission started): its build stops at its next stage and what it made is freed.</summary>
    public static void Cancel(string why) => Cancel(why, true);

    private static void Cancel(string why, bool leaving)
    {
        if (leaving) Watch.End();
        if (_job is not { } job) return;
        _job = null;
        job.Cts.Cancel();
        RunLog.Print($"preload: {job.Key.Mission} cancelled ({why})");
        FreeWhenDone(job);
    }

    /// <summary>The process's private memory and the renderer's video memory, bytes (read once a preload's start and end: not a frame's).</summary>
    private static (long Memory, long Video) Memory() =>
        (System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64, (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed));

    private static async void FreeWhenDone(Job job)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var thread = job.Thread;
        while (thread.IsAlive) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        thread = null!;
        int freed = Free(job);
        string mission = job.Key.Mission;
        long memBefore = job.MemoryBefore, videoBefore = job.VideoBefore;
        job = null!;   // (nothing of the build held from here: what is left over after the collections below is a leak)
        RunLog.Print($"preload: {mission} freed ({freed} nodes outside the tree)");
        // the memory measured a few seconds later, the build's objects collected and the renderer's resources let go meanwhile
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 5000)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (clock.ElapsedMilliseconds > 1000 && clock.ElapsedMilliseconds < 1100) { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect(); }
        }
        var (mem, video) = Memory();
        RunLog.Print($"preload: {mission} 5 s after its free: the process {(mem - memBefore) >> 20} MB, video {(video - videoBefore) >> 20} MB over before the preload");
    }

    /// <summary>What the build made: the world's root with all under it, and its parts not yet put under it (a build stopped part-way).</summary>
    private static int Free(Job job)
    {
        int n = 0;
        void Drop(Node? node) { if (node is not null && GodotObject.IsInstanceValid(node) && node.GetParent() is null && !node.IsInsideTree()) { node.QueueFree(); n++; } }
        var b = job.Builder;
        Drop(b.Terrain); Drop(b.Roads); Drop(b.RoadsOffGround); Drop(b.OutsideNode); Drop(b.Scenery);
        if (b.HorizonBuild is { IsCompletedSuccessfully: true } h) Drop(h.Result);
        Drop(b.Root ?? job.World);
        // and what the build kept for a restart of its mission (its textures, materials and meshes; the map's arrays): nobody plays it now
        if (b.BuiltKitKey is { } kit && MissionBuilder.Kept.Key == kit) MissionBuilder.Kept.Drop("its preload dropped");
        if (!I76.Game.Editor.EditorSession.Active) I76.Formats.Vfs.SessionStore.Clear();
        return n;
    }

    /// <summary>
    /// The shell screen's frames while it is up from <see cref="Start"/> (a preload or not, I76_PRELOAD=0): their mean and the worst,
    /// logged when the screen is left, for the screen's cost of the build beside it.
    /// </summary>
    private static class Watch
    {
        private static string? _mission;
        private static long _last, _start;
        private static int _frames;
        private static double _sum, _worst;
        private static bool _hooked;

        public static void Begin(string mission)
        {
            if (_mission is not null) return;
            (_mission, _last, _start, _frames, _sum, _worst) = (mission, System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.GetTimestamp(), 0, 0, 0);
            if (!_hooked && Engine.GetMainLoop() is SceneTree tree) { _hooked = true; tree.ProcessFrame += Frame; }
        }

        private static void Frame()
        {
            if (_mission is null) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = (now - _last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _last = now;
            _frames++; _sum += ms; if (ms > _worst) _worst = ms;
        }

        public static void End()
        {
            if (_mission is null) return;
            double s = (System.Diagnostics.Stopwatch.GetTimestamp() - _start) / (double)System.Diagnostics.Stopwatch.Frequency;
            RunLog.Print($"preload screen: {_mission} {(On ? "preloading" : "no preload")} for {s:0.0} s: {_frames} frames, mean {(_frames > 0 ? _sum / _frames : 0):0.0} ms, worst {_worst:0} ms");
            _mission = null;
        }
    }
}
