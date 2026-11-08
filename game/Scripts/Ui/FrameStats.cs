using Godot;

namespace I76.Game.Ui;

/// <summary>
/// The frame times of the last few seconds (the frame-rate overlay, Show FPS): a ring of real frame deltas trimmed to a window (1 s),
/// read as the mean rate over the window. Engine-free.
/// </summary>
public sealed class FrameStats
{
    private readonly double[] _ring;
    private int _head, _count;
    private double _sum;
    public double Window { get; }

    public FrameStats(double windowSeconds = 1.0, int capacity = 4096) { Window = windowSeconds; _ring = new double[capacity]; }

    public int Count => _count;

    /// <summary>One frame's real time in seconds; the oldest frames go once the ring holds more than the window.</summary>
    public void Add(double dt)
    {
        if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;
        if (_count == _ring.Length) Drop();
        _ring[(_head + _count++) % _ring.Length] = dt;
        _sum += dt;
        while (_count > 1 && _sum > Window) Drop();
    }

    /// <summary>Every frame dropped (the Graphic Detail pads' reading after a change).</summary>
    public void Clear() { _head = _count = 0; _sum = 0; }

    private void Drop()
    {
        _sum -= _ring[_head];
        _head = (_head + 1) % _ring.Length;
        _count--;
    }

    private double At(int i) => _ring[(_head + i) % _ring.Length];

    /// <summary>Frames per second over the newest <paramref name="seconds"/> (at least one frame); 0 with none.</summary>
    public double Fps(double seconds)
    {
        double t = 0; int n = 0;
        for (int i = _count - 1; i >= 0 && (n == 0 || t < seconds); i--) { t += At(i); n++; }
        return t > 0 ? n / t : 0;
    }

    /// <summary>
    /// The overlay's text: "64 fps  15.6 ms", the mean rate over the window (frames counted over the time they took) and
    /// the mean frame time that is (its reciprocal); "-- fps" before any frame.
    /// </summary>
    public string Text() => Fps(Window) is var f and > 0
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{f:0} fps  {1000 / f:0.0} ms")
        : "-- fps";
}

/// <summary>
/// The frame-rate overlay ([port] dev_fps: Graphic Detail's Show FPS row on the Display page, and the Developer panel's
/// Settings): a small label in the window's top right corner, in the modern HUD's cream with a black outline, over the menus
/// and the game alike. One per process, on the tree's root, so a scene reload (a mission's start or end) keeps it;
/// <see cref="Apply"/> puts it up or takes it down.
/// </summary>
public partial class FpsOverlay : Control
{
    private static CanvasLayer? _layer;

    /// <summary>The overlay up while <paramref name="on"/> (idempotent; any code with the tree may call it on a change).</summary>
    public static void Apply(SceneTree? tree, bool on)
    {
        if (_layer is not null && !GodotObject.IsInstanceValid(_layer)) _layer = null;
        if (on == (_layer is not null) || tree?.Root is not { } root) return;
        if (!on) { _layer!.QueueFree(); _layer = null; return; }
        _layer = new CanvasLayer { Layer = 120, ProcessMode = ProcessModeEnum.Always, Name = "ShowFps" };
        _layer.AddChild(new FpsOverlay());
        // deferred: the root may be busy setting up its children (the first frame, a scene change)
        root.CallDeferred(Node.MethodName.AddChild, _layer);
    }

    private readonly FrameStats _stats = new();
    private Label _label = null!;
    private ulong _last;
    private double _since;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _label = new Label { Text = "-- fps", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right };
        // the modern HUD's colours (Hud.Label), a little see-through so it stays out of the way
        _label.AddThemeColorOverride("font_color", new Color(1, 0.92f, 0.75f, 0.85f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        AddChild(_label);
        GetViewport().SizeChanged += Rescale;
        TreeExiting += () => GetViewport().SizeChanged -= Rescale;
        Rescale();
        _last = Time.GetTicksUsec();
    }

    private int _size;

    private void Rescale()
    {
        // about 2 % of the window's height in whole pixels: 14 at 720 rows, 28 at 1440
        float h = GetViewportRect().Size.Y;
        int size = System.Math.Max(12, (int)System.Math.Round(h / 52));
        _label.AddThemeFontSizeOverride("font_size", size);
        _label.AddThemeConstantOverride("outline_size", System.Math.Max(2, size / 5));
        _size = size;
        Place();
    }

    /// <summary>The label's right edge against the window's, its width the text's (set by hand: anchors under a CanvasLayer settle late).</summary>
    private void Place()
    {
        _label.ResetSize();
        _label.Position = new Vector2(GetViewportRect().Size.X - _label.Size.X - _size * 0.6f, _size * 0.3f);
    }

    public override void _Process(double delta)
    {
        // real time, not the process delta: a time scale or a paused tree does not change what the machine is doing
        ulong now = Time.GetTicksUsec();
        double dt = (now - _last) / 1e6;
        _last = now;
        _stats.Add(dt);
        _since += dt;
        if (_since < 0.5) return;
        _since = 0;
        _label.Text = _stats.Text();
        Place();
    }
}
