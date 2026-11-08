using System.Globalization;

namespace I76.Game.Ui;

/// <summary>
/// The frame rate the Esc menu's Graphic Detail pads show (the port's): the frames of the view behind the pad, a one-second window
/// read twice a second, started again a quarter of a second after a row changes (the first frames after a change compile pipelines and
/// would say nothing about the setting); the change since the menu opened is against the first reading. Real time, so a paused tree
/// (the menu over a single-player mission) counts the frames the machine draws. Engine-free.
/// </summary>
public sealed class LiveFps
{
    /// <summary>The window the rate is read over, how often the shown figure changes, and how long a change is left to settle.</summary>
    public const double Window = 1.0, Every = 0.5, Settle = 0.25;
    private readonly FrameStats _stats = new(Window);
    private double _settle, _since, _counted;
    private double? _shown, _first;

    /// <summary>The figure shown (rounded when printed), null until a reading.</summary>
    public double? Fps => _shown;
    /// <summary>The first reading since the menu opened: the change is against it.</summary>
    public double? First => _first;

    /// <summary>One frame's real time in seconds.</summary>
    public void Add(double dt)
    {
        if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) return;
        if (_settle > 0) { _settle -= dt; return; }
        _stats.Add(dt);
        _counted += dt;
        _since += dt;
        // the first figure after half a second of frames, then twice a second over the last second
        if (_since < Every || _counted < Every) return;
        _since = 0;
        _shown = _stats.Fps(Window);
        _first ??= _shown;
    }

    /// <summary>A row changed: the frames before it are dropped and the next quarter second is not counted; the shown figure stays until the next reading.</summary>
    public void Changed()
    {
        _stats.Clear();
        _settle = Settle;
        _since = _counted = 0;
    }
}

/// <summary>What a frame-rate figure is, for its words: measured behind the pad, or estimated from a measured mission.</summary>
public static class FpsLabels
{
    /// <summary>"Measured 84 fps (+12)": the change since the menu opened when there is one; "Measured -- fps" before the first reading.</summary>
    public static string Measured(double? fps, double? first)
    {
        if (fps is not { } f) return "Measured -- fps";
        int now = (int)Math.Round(f), was = first is { } w ? (int)Math.Round(w) : now;
        return now == was ? $"Measured {now} fps" : $"Measured {now} fps ({(now > was ? "+" : "-")}{Math.Abs(now - was)})";
    }

    /// <summary>"Estimated ~95 fps": to the nearest 5 (the model is good to a few frames a second at best); none before a mission has been measured.</summary>
    public static string Estimated(double? fps) => fps is { } f && f > 0 ? $"Estimated ~{Math.Max(5, (int)Math.Round(f / 5) * 5)} fps" : "Estimated: after a mission";
}

/// <summary>The settings a frame's cost depends on (the modern look's), and the size the 3D view is drawn at.</summary>
public readonly record struct FrameCostSettings(int Width, int Height, int RenderScalePercent, int Terrain, int Shadow, int Aa, int Mirror, int Vegetation, bool Ao, int Range, bool Look1997)
{
    /// <summary>From the settings block and the window's size in pixels.</summary>
    public static FrameCostSettings Of(I76.Game.Shell.ShellSettings s, int width, int height) => new(
        width, height, I76.Game.Boot.RenderScale.Percents[Math.Clamp(s.RenderScale, 0, I76.Game.Boot.RenderScale.Percents.Length - 1)],
        s.Graphics[0x2], s.Graphics[0x6], s.Graphics[0x8], s.Graphics[0xd], s.Vegetation, s.AmbientOcclusion,
        I76.Formats.Imaging.DrawDistance.Resolve(s.DrawDistanceChoice, s.Look1997), s.Look1997);

    /// <summary>The pixels the 3D view shades, against 3440x1440 at 100 %.</summary>
    public double Pixels => (double)Width * Height * RenderScalePercent * RenderScalePercent / 1e4 / FrameCostModel.ReferencePixels;
    /// <summary>The window's pixels against 3440x1440 (FSR's pass runs at the window's size).</summary>
    public double WindowPixels => (double)Width * Height / FrameCostModel.ReferencePixels;
}

/// <summary>
/// A frame's cost by its settings (docs/perf/plan.md, "Settings and the frame rate"), measured on T05's route in the modern look on a
/// GTX 1080 Ti at 180 W with a 4-core processor: the graphics card's time a frame (the main view's, a fixed part and a part that follows
/// the pixels it shades, the window's size times the Render Scale squared, and each row's cost over the defaults; under 100 % the
/// upscale's pass at the window's size; the rear mirror's, a second scene every second frame) and the main thread's (its own and the
/// renderer's commands; the shadows, the plants and the mirror change it). The two overlap: the frame is their power mean
/// (<see cref="Combine"/>), the slower one mostly. A machine is fitted from one measured mission: the card's speed from the main view's
/// time measured there, the main thread's from what is left of the frame. The 1997 look draws none of it and has no estimate.
/// </summary>
public static class FrameCostModel
{
    public const double ReferencePixels = 3440.0 * 1440.0;

    /// <summary>The power the card's and the main thread's times combine with: 4 fits the route's mix of frames held by either.</summary>
    public const double Power = 4;

    // the card: the main view at the defaults, a fixed part and the pixels' part at 3440x1440 (7.26 ms there, 3.77 at 1920x1080)
    public const double MainFixedMs = 1.26, MainPixelMs = 6.00;
    /// <summary>FSR 1.0's upscale under 100 %, at 3440x1440's window (with the texture mip bias it brings).</summary>
    public const double UpscaleMs = 0.87;
    // each row's change to the main view over its default, at 3440x1440; [value] by the row's own index, the default's 0
    public static readonly double[] TerrainMs = [-0.48, 0];               // Low, High*
    public static readonly double[] ShadowMs = [-0.56, 0, 0.17];          // Off, Low*, High
    public static readonly double[] AaPixelMs = [-0.96, 0, 0.52, 1.56];   // Off, 2x*, 4x, 8x (by the pixels; 8x is 4x's step doubled, not measured)
    public static readonly double[] VegetationMs = [-0.18, 0, 0.21];      // Off (round 5's ladder), Low*, High
    public const double AoPixelMs = 3.93;                                 // On (by the pixels)
    public static readonly double[] RangeMs = [-0.34, 0, -0.17];          // Far, Max*, 2.3 km (DrawDistance's order; 2.3 km halfway, not measured)
    /// <summary>The rear mirror's share of the card a frame: Off, Low (no ground), High*.</summary>
    public static readonly double[] MirrorMs = [0, 0.48, 1.2];

    // the rest of the frame at the defaults (the main thread and the renderer's commands), and each row's change to it: fewer draw
    // calls with the shadows off, the plants fewer, the mirror off and the 600 m range; the multisampling's resolve past 2x shows here
    public const double CpuMs = 8.75;
    public static readonly double[] ShadowCpuMs = [-1.42, 0, 0.38];
    public static readonly double[] AaCpuMs = [0, 0, 0.92, 1.84];
    public static readonly double[] VegetationCpuMs = [-1.0, 0, 0.97];
    public static readonly double[] MirrorCpuMs = [-0.84, -0.50, 0];
    public static readonly double[] RangeCpuMs = [-1.60, 0, -0.80];

    private static double At(double[] t, int i) => t[Math.Clamp(i, 0, t.Length - 1)];

    /// <summary>The main view's time on the card at these settings on the model's machine.</summary>
    public static double MainMs(FrameCostSettings s)
    {
        double px = s.Pixels;
        return MainFixedMs + MainPixelMs * px
            + At(TerrainMs, s.Terrain) + At(ShadowMs, s.Shadow) + At(VegetationMs, s.Vegetation) + At(RangeMs, s.Range)
            + (At(AaPixelMs, s.Aa) + (s.Ao ? AoPixelMs : 0)) * px
            + (s.RenderScalePercent < 100 ? UpscaleMs * s.WindowPixels : 0);
    }

    /// <summary>The card's whole time a frame: the main view and the mirror.</summary>
    public static double CardMs(FrameCostSettings s) => MainMs(s) + At(MirrorMs, s.Mirror);

    /// <summary>The main thread's time a frame.</summary>
    public static double ThreadMs(FrameCostSettings s) =>
        CpuMs + At(ShadowCpuMs, s.Shadow) + At(AaCpuMs, s.Aa) + At(VegetationCpuMs, s.Vegetation) + At(MirrorCpuMs, s.Mirror) + At(RangeCpuMs, s.Range);

    /// <summary>A frame from the card's and the main thread's times: their power mean, never under the slower.</summary>
    public static double Combine(double thread, double card) => Math.Pow(Math.Pow(thread, Power) + Math.Pow(card, Power), 1 / Power);

    /// <summary>The frame's milliseconds at these settings on the model's machine.</summary>
    public static double Ms(FrameCostSettings s) => Combine(ThreadMs(s), CardMs(s));

    /// <summary>The cap on the frame rate the settings set: Max FPS, else plain VSync's refresh (<paramref name="refresh"/>; the mailbox present has none); null for none.</summary>
    public static double? Cap(I76.Game.Shell.ShellSettings s, double refresh = 0)
    {
        if (I76.Sim.Views.FrameLimit.Fps(s.MaxFps) is var f and > 0) return f;
        return refresh > 0 && I76.Sim.Views.FrameLimit.SwapFor(s.VSync, s.LowLatencyPresent) == I76.Sim.Views.FrameLimit.Swap.Fifo ? refresh : null;
    }

    /// <summary>
    /// The frame rate at <paramref name="now"/> from one measured at <paramref name="measured"/>; null for the 1997 look or no measure. The
    /// card's speed against the model's is the main view's measured time over the model's there; the main thread's is what the frame
    /// leaves. A measure without the card's time scales by the model's ratio alone.
    /// </summary>
    public static double? Estimate(FpsRecord? measured, FrameCostSettings now)
    {
        if (measured is not { } m || m.Fps <= 0 || now.Look1997 || m.At.Look1997) return null;
        double frame = 1000 / m.Fps;
        if (m.GpuMs <= 0) return m.Fps * Ms(m.At) / Ms(now);
        // the card's part of the measured frame, at most nearly all of it (a frame the card held whole leaves the main thread
        // about half the frame: what the power mean gives for 98 %)
        double cardThen = Math.Min(m.GpuMs / MainMs(m.At) * CardMs(m.At), frame * 0.98);
        double threadThen = Math.Pow(Math.Pow(frame, Power) - Math.Pow(cardThen, Power), 1 / Power);
        return 1000 / Combine(threadThen / ThreadMs(m.At) * ThreadMs(now), cardThen / CardMs(m.At) * CardMs(now));
    }
}

/// <summary>
/// The frame rate of the last mission played in the modern look and the settings it ran at (user://frame-rate.cfg, written by the
/// game, one key=value a line): the median of its seconds, the start's first seconds and the paused ones left out. Engine-free text.
/// </summary>
public readonly record struct FpsRecord(double Fps, FrameCostSettings At, string Mission, double GpuMs = 0)
{
    public string Format()
    {
        var c = CultureInfo.InvariantCulture;
        var a = At;
        return string.Join("\n",
            $"fps={Fps.ToString("0.0", c)}", $"gpu_ms={GpuMs.ToString("0.00", c)}", $"size={a.Width}x{a.Height}", $"render_scale={a.RenderScalePercent}", $"terrain={a.Terrain}",
            $"shadow={a.Shadow}", $"aa={a.Aa}", $"mirror={a.Mirror}", $"vegetation={a.Vegetation}", $"ao={(a.Ao ? 1 : 0)}", $"range={a.Range}",
            $"mission={Mission}") + "\n";
    }

    /// <summary>The record from the file's text; null when a key is missing or unreadable.</summary>
    public static FpsRecord? Parse(string? text)
    {
        if (text is null) return null;
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
            if (line.IndexOf('=') is var eq and > 0) kv[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        int I(string k) => kv.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new FormatException(k);
        try
        {
            if (!kv.TryGetValue("fps", out var f) || !double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out double fps) || fps <= 0) return null;
            if (!kv.TryGetValue("size", out var size) || size.Split('x') is not [var w, var h] || !int.TryParse(w, out int width) || !int.TryParse(h, out int height) || width <= 0 || height <= 0) return null;
            var at = new FrameCostSettings(width, height, I("render_scale"), I("terrain"), I("shadow"), I("aa"), I("mirror"), I("vegetation"), I("ao") != 0, I("range"), false);
            double gpu = kv.TryGetValue("gpu_ms", out var g) && double.TryParse(g, NumberStyles.Float, CultureInfo.InvariantCulture, out double gm) && gm > 0 ? gm : 0;
            return new FpsRecord(fps, at, kv.GetValueOrDefault("mission") ?? "", gpu);
        }
        catch (FormatException) { return null; }
    }

    /// <summary>The file's place (Godot's user:// as a path); reading and writing it are the caller's.</summary>
    public const string File = "user://frame-rate.cfg";

    public static FpsRecord? Read(string path)
    {
        try { return System.IO.File.Exists(path) ? Parse(System.IO.File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return null; }
    }

    public void Write(string path)
    {
        try { System.IO.File.WriteAllText(path, Format()); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// A mission's frame rate as it is played (the record's figure): each second's frames counted from real time, the first
/// <see cref="SkipSeconds"/> of play left out (the start compiles pipelines and streams the ground), paused time not counted; the
/// median of the seconds once there are <see cref="MinSeconds"/> of them. A second held by a frame-rate cap (Max FPS, VSync's refresh)
/// says only that the machine is faster, so a mission mostly at its cap leaves the old record. Engine-free.
/// </summary>
public sealed class PlayFps
{
    public const double SkipSeconds = 10;
    public const int MinSeconds = 30, MaxSeconds = 3600;
    private readonly List<double> _seconds = [], _gpu = [];
    private double _played, _inSecond, _gpuSum;
    private int _frames;
    private int _capped;

    public int Seconds => _seconds.Count;

    /// <summary>The median of the last <see cref="RecentSeconds"/> seconds counted (the Esc menu's "in play" figure), null before any.</summary>
    public double? Recent() => _seconds.Count == 0 ? null : MedianOf(_seconds.Skip(Math.Max(0, _seconds.Count - RecentSeconds)).ToList());
    public const int RecentSeconds = 5;

    /// <summary>One frame's real time; <paramref name="cap"/> the frame-rate cap now (0 for none); <paramref name="gpuMs"/> the main view's time on the graphics card (0 unknown).</summary>
    public void Add(double dt, double cap = 0, double gpuMs = 0)
    {
        if (dt <= 0 || double.IsNaN(dt) || dt > 1) return;   // a load or a stall is not a frame rate
        _played += dt;
        if (_played < SkipSeconds) return;
        _inSecond += dt;
        _gpuSum += gpuMs;
        _frames++;
        if (_inSecond < 1) return;
        double fps = _frames / _inSecond, gpu = _gpuSum / _frames;
        _inSecond = _gpuSum = 0;
        _frames = 0;
        if (_seconds.Count >= MaxSeconds) return;
        _seconds.Add(fps);
        _gpu.Add(gpu);
        if (cap > 0 && fps >= cap * 0.95) _capped++;
    }

    /// <summary>The median second's frame rate, or null with too few seconds or with most of them at the cap.</summary>
    public double? Median()
    {
        if (_seconds.Count < MinSeconds || _capped * 2 > _seconds.Count) return null;
        return MedianOf(_seconds);
    }

    /// <summary>The median second's main-view time on the graphics card in milliseconds (0 when it was not measured), with <see cref="Median"/>.</summary>
    public double GpuMs() => _gpu.Count == 0 ? 0 : MedianOf(_gpu);

    private static double MedianOf(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }
}
