using Godot;
using I76.Sim.Views;

namespace I76.Game;

/// <summary>
/// Graphic Detail's Max FPS and VSync, applied to the engine (live, from the shell sheet and the in-game menu, and at start). Only the
/// drawing is limited: the simulation steps on its own clock (SimDriver's accumulator takes the frame's delta, however long, up to a
/// quarter second) and the cars are drawn interpolated between its ticks, so a cap under the tick rate runs several ticks a frame.
/// </summary>
public static class FrameLimiter
{
    /// <summary>tools/shot.sh's I76_SHOT_MAX_FPS: at most that many frames a real second, paced by <see cref="Pace"/>.</summary>
    private static readonly int? ShotMaxFps = int.TryParse(System.Environment.GetEnvironmentVariable("I76_SHOT_MAX_FPS"), out int f) && f > 0 ? f : null;
    private static ulong _next;

    /// <summary>
    /// Called once a frame: holds a capture to I76_SHOT_MAX_FPS frames a real second. Done here, by hand, because Godot's own cap
    /// (Engine.MaxFps, --max-fps) does nothing under --fixed-fps, which a capture always has. A frame that runs late leaves the
    /// next ones unpaced until they are back on time; one far behind (a load) starts the count again.
    /// </summary>
    public static void Pace()
    {
        if (ShotMaxFps is not { } fps) return;
        ulong now = Time.GetTicksUsec(), step = 1_000_000UL / (ulong)fps;
        if (_next == 0 || now > _next + step * 4) _next = now;
        else if (now < _next) OS.DelayUsec((int)(_next - now));
        _next += step;
    }

    /// <summary>
    /// The main thread's frames while a mission builds on another thread under its briefing (Main.BuildUnderBriefing): the
    /// renderer takes that thread's calls that return a value once a frame, so a frame held to the display's refresh makes
    /// the build three times as long. Mailbox (no tearing; plain VSync where the driver has none) at up to
    /// <see cref="BuildFps"/>; <see cref="Apply"/> puts the player's settings back.
    /// </summary>
    public static void ForBuild()
    {
        Engine.MaxFps = BuildFps;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Mailbox);
    }

    public const int BuildFps = 240;

    public static void Apply(I76.Game.Shell.ShellSettings s)
    {
        Engine.MaxFps = FrameLimit.Fps(s.MaxFps);
        DisplayServer.WindowSetVsyncMode(s.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
    }
}
