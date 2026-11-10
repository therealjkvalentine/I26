using Godot;

namespace I76.Game.Boot;

/// <summary>
/// [port] target_fps (the port's own, off by default; docs/perf/plan.md "A target frame rate"): while the graphics card is what
/// holds the frame rate under the target, the 3D view is drawn at the next lower Render Scale step (down to 67 %, scaled up by FSR as
/// the row's own steps are), and back up a step at a time, never past the player's own Render Scale, once the card has room. Frames
/// held by the main thread (the simulation, the scripts) are left alone: fewer pixels would not help them.
/// </summary>
public sealed class FrameTarget(int fps)
{
    /// <summary>Frames a decision looks at (their medians), half a second at 60 fps.</summary>
    public const int Window = 30;
    /// <summary>Decisions in a row the next step up must fit in before it is taken (so it does not step up and down each second).</summary>
    public const int Calm = 3;
    /// <summary>The lowest step it goes to: 67 %.</summary>
    public static readonly int Floor = System.Array.IndexOf(RenderScale.Percents, 67);

    private readonly double _budget = 1000.0 / System.Math.Max(1, fps);
    private readonly List<double> _gpu = [], _wall = [];
    private int _calm;

    public int Fps => (int)System.Math.Round(1000.0 / _budget);

    /// <summary>
    /// One frame's GPU time and wall time (ms) at Render Scale step <paramref name="current"/>, the player's own step being
    /// <paramref name="player"/>: the step to change to when a window ends with a change due, else null.
    /// </summary>
    public int? Add(double gpuMs, double wallMs, int current, int player)
    {
        _gpu.Add(gpuMs);
        _wall.Add(wallMs);
        if (_gpu.Count < Window) return null;
        double g = Median(_gpu), w = Median(_wall);
        _gpu.Clear();
        _wall.Clear();
        int floor = System.Math.Min(Floor, player);
        if (current > player) { _calm = 0; return player; }
        // the target missed and the card near the whole frame: the GPU holds it, a step down
        if (w > _budget && g > 0.85 * _budget && current > floor) { _calm = 0; return current - 1; }
        if (current < player)
        {
            // the next step up's GPU time, by its pixels, inside 80 % of the frame, for Calm decisions in a row
            double k = RenderScale.Scale(current + 1) / RenderScale.Scale(current);
            if (g * k * k < 0.8 * _budget) { if (++_calm >= Calm) { _calm = 0; return current + 1; } }
            else _calm = 0;
        }
        return null;
    }

    private static double Median(List<double> xs)
    {
        var s = xs.ToArray();
        System.Array.Sort(s);
        return s[s.Length / 2];
    }
}

/// <summary>The mission's <see cref="FrameTarget"/>: the main viewport's measured GPU time a frame, the step applied to the window's 3D scale.</summary>
public partial class FrameTargetNode : Node
{
    private FrameTarget _target = new(60);
    private int _player = RenderScale.Default, _current = RenderScale.Default;

    /// <summary>The Render Scale step now drawn.</summary>
    public int Current => _current;

    /// <summary>The settings as applied (a mission's start, a change in the menus): the player's Render Scale is the ceiling and the start.</summary>
    public void Reset(I76.Game.Shell.ShellSettings s)
    {
        _target = new FrameTarget(s.TargetFps);
        _player = _current = s.RenderScale;
    }

    public override void _Ready()
    {
        Name = "FrameTarget";
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
    }

    public override void _Process(double delta)
    {
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
        if (gpu <= 0 || _target.Add(gpu, delta * 1000, _current, _player) is not { } step) return;
        _current = step;
        RenderScale.Draw(step);   // FSR up from under 100 %, and the video memory cap over it, as the row's own steps
    }
}
