using Godot;
using I76.Formats.Remix;

namespace I76.Game.Shell;

/// <summary>
/// The modern look's region on ROGUETRIP's choice screen (docs/remix-missions.md "The choice map"): its lines, dots and rings drawn
/// at the window's resolution, antialiased, over the sheet and under the shell's words (which the text layer sets over it), and the
/// player's road atlas under them when there is one. The 1997 look draws the same plan into the frame's pixels instead.
/// </summary>
public partial class RoguetripMapLayer : Control
{
    public required Func<RoguetripLayout.RegionPlan?> Plan { get; init; }
    /// <summary>Hidden while a pop-up or an overlay is up (they are the frame's pixels, under this layer).</summary>
    public required Func<bool> Covered { get; init; }
    public Func<Texture2D?>? Backdrop { get; init; }

    public static readonly Color Ink = Color.Color8(40, 30, 18), Road = Color.Color8(128, 88, 50), Pencil = Color.Color8(120, 110, 96),
        Marker = Color.Color8(222, 96, 38), RedPencil = Color.Color8(150, 36, 22);

    private float _s;
    private Vector2 _off;
    private Vector2 T(float x, float y) => _off + new Vector2(x, y) * _s;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Covered() || Plan() is not { } plan) return;
        var size = GetViewportRect().Size;
        _s = Mathf.Min(size.X / ShellFrame.W, size.Y / ShellFrame.H);
        _off = (size - new Vector2(ShellFrame.W, ShellFrame.H) * _s) / 2;
        var b = RoguetripLayout.RegionBox;
        if (Backdrop?.Invoke() is { } tex) DrawTextureRect(tex, new Rect2(T(b.X, b.Y), new Vector2(b.W, b.H) * _s), false, new Color(0.98f, 0.93f, 0.84f, 0.95f));   // a little warmer, toward the sheet
        foreach (var sg in plan.Segments)
        {
            var (c, w, on, off) = sg.Kind switch
            {
                RoguetripLayout.Ink.State => (new Color(Pencil, 0.7f), 0.8f, 5f, 3f),
                RoguetripLayout.Ink.Run => (new Color(Marker, 0.9f), 1.8f, 3f, 2f),
                RoguetripLayout.Ink.Approach => (new Color(Marker, 0.85f), 1.6f, 4f, 3f),
                _ => (new Color(Road, 0.7f), 1.1f, 0f, 0f),
            };
            Pen(sg.X0, sg.Y0, sg.X1, sg.Y1, c, w, on, off);
        }
        foreach (var d in plan.Dots)
        {
            if (d.Kind == "town") DrawCircle(T(d.X, d.Y), 1.4f * _s, Ink, true, -1, true);
            else if (d.Kind == "visited") DrawCircle(T(d.X, d.Y), 2.4f * _s, Ink, true, -1, true);
            else DrawArc(T(d.X, d.Y), 2.2f * _s, 0, Mathf.Tau, 24, Pencil, 0.8f * _s, true);
        }
        foreach (var o in plan.Offers)
        {
            var c = o.Selected ? Marker : Ink;
            DrawArc(T(o.X, o.Y), 8 * _s, 0, Mathf.Tau, 48, c, (o.Selected ? 1.8f : 1.1f) * _s, true);
            if (o.Selected) DrawArc(T(o.X, o.Y), 12 * _s, 0, Mathf.Tau, 64, new Color(Marker, 0.8f), 1f * _s, true);
        }
    }

    /// <summary>A pen line in shell pixels, solid or dashed (on, off shell pixels).</summary>
    private void Pen(float x0, float y0, float x1, float y1, Color c, float w, float on, float off)
    {
        var a = T(x0, y0);
        var b = T(x1, y1);
        if (on <= 0) { DrawLine(a, b, c, w * _s, true); return; }
        float len = a.DistanceTo(b), step = (on + off) * _s;
        var dir = (b - a) / Mathf.Max(len, 0.001f);
        for (float t = 0; t < len; t += step) DrawLine(a + dir * t, a + dir * Mathf.Min(len, t + on * _s), c, w * _s, true);
    }
}

/// <summary>
/// The player's road atlas under the region (optional; docs/remix-missions.md "The choice map"): a picture in the user folder's
/// remix/ folder, roguetrip-atlas.png (or .jpg, .webp), registered to the map by roguetrip-atlas.txt (control points: latitude,
/// longitude, the picture's x and y, one a line). Nothing ships: with no files the drawn map stands alone. Read and fitted once a
/// session, off the main thread (a scanned atlas can be 7000 px across); the region box's part of it, resampled to the box.
/// </summary>
public static class RoguetripAtlas
{
    public const string Picture = "roguetrip-atlas", Points = "roguetrip-atlas.txt";
    /// <summary>Texels a shell pixel the backdrop is resampled to (the region box at 3440 x 1440 is shown 3 times its size).</summary>
    public const int Scale = 4;

    private static System.Threading.Tasks.Task<(byte[] Rgba, int W, int H)?>? _job;
    private static ImageTexture? _tex;
    private static byte[]? _px1997;
    private static string? _from;

    /// <summary>The picture and its points in the folder, or null.</summary>
    public static (string Picture, string Points)? Files(string dir)
    {
        var pts = Path.Combine(dir, Points);
        if (!File.Exists(pts)) return null;
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".webp" })
            if (Path.Combine(dir, Picture + ext) is var p && File.Exists(p)) return (p, pts);
        return null;
    }

    /// <summary>Starts the fitting (once); later calls return the texture once it is ready, else null.</summary>
    public static ImageTexture? Texture(string dir, Action? ready = null)
    {
        if (Files(dir) is not { } f) return null;
        if (_from != f.Picture) { _from = f.Picture; _tex = null; _px1997 = null; _job = null; }
        if (_tex is not null) return _tex;
        if (Result(dir, ready) is not { } r) return null;
        var img = Image.CreateFromData(r.W, r.H, false, Image.Format.Rgba8, r.Rgba);
        img.GenerateMipmaps();
        return _tex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The fitted picture at one texel a shell pixel (the 1997 look's, before it is matched to the palette), or null while not ready.</summary>
    public static (byte[] Rgba, int W, int H)? Small(string dir, Action? ready = null)
    {
        if (Files(dir) is null) return null;
        if (_px1997 is not null) return (_px1997, (int)RoguetripLayout.RegionBox.W, (int)RoguetripLayout.RegionBox.H);
        if (Result(dir, ready) is not { } r) return null;
        int w = (int)RoguetripLayout.RegionBox.W, h = (int)RoguetripLayout.RegionBox.H;
        var o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                {
                    int acc = 0;
                    for (int j = 0; j < Scale; j++) for (int i = 0; i < Scale; i++) acc += r.Rgba[((y * Scale + j) * r.W + x * Scale + i) * 4 + c];
                    o[(y * w + x) * 4 + c] = (byte)(acc / (Scale * Scale));
                }
        return (_px1997 = o, w, h);
    }

    private static (byte[] Rgba, int W, int H)? Result(string dir, Action? ready)
    {
        if (_job is null)
        {
            var f = Files(dir)!.Value;
            _job = System.Threading.Tasks.Task.Run(() => Fit(f.Picture, f.Points));
            if (ready is not null) _job.ContinueWith(_ => Callable.From(ready).CallDeferred());
        }
        return _job.IsCompletedSuccessfully ? _job.Result : null;
    }

    /// <summary>The region box's part of the picture: each texel's latitude and longitude (the region's projection), through the fit to the picture, sampled bilinear.</summary>
    private static (byte[] Rgba, int W, int H)? Fit(string picture, string points)
    {
        try
        {
            if (RoguetripLayout.AtlasFit.Parse(File.ReadAllText(points)) is not { } fit) { GD.PrintErr($"roguetrip: {points}: three control points or more needed"); return null; }
            var img = Image.LoadFromFile(picture);
            if (img is null || img.IsEmpty()) return null;
            img.Convert(Image.Format.Rgba8);
            int sw = img.GetWidth(), sh = img.GetHeight();
            var src = img.GetData();
            var reg = RemixRegion.Builtin();
            var box = RoguetripLayout.RegionBox;
            int w = (int)box.W * Scale, h = (int)box.H * Scale;
            var o = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var (lat, lon) = RoguetripLayout.Unproject(reg, (x + 0.5) / Scale, (y + 0.5) / Scale, box.W, box.H);
                    var (px, py) = fit.ToPicture(lat, lon);
                    px -= 0.5; py -= 0.5;
                    int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
                    double fx = px - x0, fy = py - y0;
                    if (x0 < 0 || y0 < 0 || x0 + 1 >= sw || y0 + 1 >= sh) continue;
                    for (int c = 0; c < 4; c++)
                    {
                        double v = src[(y0 * sw + x0) * 4 + c] * (1 - fx) * (1 - fy) + src[(y0 * sw + x0 + 1) * 4 + c] * fx * (1 - fy)
                                 + src[((y0 + 1) * sw + x0) * 4 + c] * (1 - fx) * fy + src[((y0 + 1) * sw + x0 + 1) * 4 + c] * fx * fy;
                        o[(y * w + x) * 4 + c] = (byte)Math.Clamp(v + 0.5, 0, 255);
                    }
                }
            GD.Print($"roguetrip: atlas {Path.GetFileName(picture)} {sw}x{sh}, {fit.Points} points, fit within {fit.MedianMiss:0} px (median), {fit.MaxMiss:0} px (worst)");
            return (o, w, h);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            GD.PrintErr($"roguetrip: atlas not read: {ex.Message}");
            return null;
        }
    }
}
