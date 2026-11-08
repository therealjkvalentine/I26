using Godot;
using I76.Game.Ui;

namespace I76.Game.Boot;

/// <summary>
/// The mission's frame rate as it is played, kept with the settings it ran at (FpsRecord, user://frame-rate.cfg) for the main menu's
/// estimate: the median of its seconds (PlayFps), written every 10 s of play (a quit does not reach the node's exit) and when the mission ends, in the modern look only and not while
/// [port] target_fps moves the Render Scale. A change of the settings or the window starts the count again. Paused, it does not run.
/// </summary>
public partial class PlayFpsRecorder : Node
{
    public required string Mission { get; init; }
    /// <summary>The mission draws in the 1997 look (a run's --1997 or F2 as well as the setting).</summary>
    public required Func<bool> Look1997 { get; init; }
    private PlayFps _play = new();
    private FrameCostSettings _at;
    private bool _off;
    private ulong _last, _frame;
    private double _sinceSave;

    public override void _Ready()
    {
        Name = "PlayFpsRecorder";
        // the main view's time on the card, as [port] target_fps reads it: whether the card or the main thread held the frame
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
    }

    /// <summary>The settings as applied (the start, the menus' changes): new ones start the count again, the old count kept if it has enough.</summary>
    public void Apply(I76.Game.Shell.ShellSettings s)
    {
        var size = DisplayServer.WindowGetSize();
        var at = FrameCostSettings.Of(s, size.X, size.Y);
        _off = s.TargetFps > 0;
        if (at == _at) return;
        Save();
        _at = at;
        _play = new PlayFps();
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec(), frame = Engine.GetProcessFrames();
        // the first frame after a pause or a stall would carry the paused time
        bool steady = _last != 0 && frame == _frame + 1;
        double dt = (now - _last) / 1e6;
        (_last, _frame) = (now, frame);
        if (!steady || _off || Look1997()) return;
        var size = DisplayServer.WindowGetSize();
        if (size.X != _at.Width || size.Y != _at.Height) { Save(); _at = _at with { Width = size.X, Height = size.Y }; _play = new PlayFps(); return; }
        _play.Add(dt, Cap(), RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
        _sinceSave += dt;
        if (_sinceSave >= 10) { _sinceSave = 0; Save(); }
    }

    public override void _ExitTree() => Save();

    /// <summary>The frame rate of the last few seconds played (the Esc menu's tooltip), null before any.</summary>
    public double? Recent => _play.Recent();

    /// <summary>The frame-rate cap now: Max FPS, else VSync's refresh (the mailbox present has none), else none.</summary>
    private static double Cap()
    {
        if (Engine.MaxFps > 0) return Engine.MaxFps;
        return DisplayServer.WindowGetVsyncMode() is DisplayServer.VSyncMode.Enabled or DisplayServer.VSyncMode.Adaptive
            ? DisplayServer.ScreenGetRefreshRate() : 0;
    }

    private void Save()
    {
        if (_play.Median() is not { } fps || _at.Width <= 0) return;
        new FpsRecord(fps, _at, Mission, _play.GpuMs()).Write(ProjectSettings.GlobalizePath(FpsRecord.File));
    }
}
