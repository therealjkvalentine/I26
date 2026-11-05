using Godot;

namespace I76.Game.Shell;

/// <summary>
/// What every mission start shares, made once a run while the main menu stands idle ([port] menu_warmup, off by default: no measurable saving;
/// docs/perf/loadtime-plan.md): the modern look's effects (the explosion and fire flipbooks, their textures kept in
/// ModernFx's cache, their materials' pipelines, the code behind them run once) made on an unseen stage far below the
/// menu for a few frames and taken away, as the mission's effects rehearsal makes them behind the loading card. It starts
/// a second after the menu's first frame (after the garage's warm-up), does one step a frame, and stops at once when the
/// menu is left; a step not done is done by the mission as before.
/// </summary>
public static class MenuWarmup
{
    /// <summary>The menu's idle time before it starts.</summary>
    public const double DelaySeconds = 1.0;
    /// <summary>Frames the effects are drawn (as the rehearsal's: the pipelines on the first, the particles' on the next).</summary>
    public const int DrawFrames = 3;

    private static bool _done;
    /// <summary>The longest menu frame while it ran and its count of frames over 33 ms (I76_LOAD_PROFILE prints them).</summary>
    public static (double WorstMs, int Over33, int Frames, double TookMs) Last;

    public static bool Wanted(Shell shell) =>
        shell.Hosted is null && shell.Settings.MenuWarmup && !shell.Faithful && System.Environment.GetEnvironmentVariable("I76_NO_WARMUP") != "1";

    /// <summary>Starts it for this main menu, once a run (a menu left before it was done tries again next time).</summary>
    public static async void Start(Shell shell, ShellScreen menu)
    {
        if (_done || !Wanted(shell)) return;
        _done = true;
        var tree = shell.GetTree();
        bool Still() => GodotObject.IsInstanceValid(shell) && shell.Current == menu;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        double Since() => System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalSeconds;
        // the menu's first second its own, and the garage's warm-up (GarageWarmup) first
        while (Since() < DelaySeconds || Garage3D.GarageWarmup.Running)
        {
            await shell.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!Still()) { _done = false; return; }
        }
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double worst = 0; int over = 0, frames = 0;
        long last = System.Diagnostics.Stopwatch.GetTimestamp();
        async System.Threading.Tasks.Task<bool> Frame()
        {
            await shell.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double ms = (now - last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            last = now; frames++;
            if (ms > worst) worst = ms;
            if (ms > 33) over++;
            return Still();
        }
        Node3D? stage = null;
        bool complete = false;
        try
        {
            // far below anything the menu shows (it shows no 3D but the garage's, under its opaque frame)
            stage = new Node3D { Name = "MenuWarmup", Position = new Vector3(0, -20000, 0) };
            shell.AddChild(stage);
            if (!await Frame()) return;
            var fx = new I76.Game.Sim.ModernFx { Name = "WarmFx", SceneLight = 1f, ProcessMode = Node.ProcessModeEnum.Always };
            stage.AddChild(fx);
            if (!await Frame()) return;
            foreach (var _ in fx.WarmSteps(stage.GlobalPosition)) if (!await Frame()) return;   // a part a frame
            for (int i = 0; i < DrawFrames; i++) if (!await Frame()) return;
            complete = true;
        }
        catch (System.Exception ex) { GD.PushWarning($"menu warm-up: {ex.Message}"); }
        finally
        {
            if (stage is not null && GodotObject.IsInstanceValid(stage)) stage.QueueFree();
            double took = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Last = (worst, over, frames, took);
            if (!complete) _done = false;
            GD.Print($"menu warm-up: {(complete ? "done" : "stopped (the menu left)")} in {took:0} ms over {frames} frames, the menu's longest frame {worst:0} ms, {over} over 33 ms");
        }
    }
}
