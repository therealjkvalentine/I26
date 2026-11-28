using Godot;
using I76.Formats.Remix;

namespace I76.Game.Shell;

/// <summary>
/// The modern look's region on ROGUETRIP's choice screen (docs/remix-missions.md "The choice map"): its lines, dots and rings drawn
/// at the window's resolution, antialiased, over the sheet and under the shell's words (which the text layer sets over it). With the
/// player's road atlas under them (<see cref="RoguetripAtlasLayer"/>) the run's road, the offers' rings and the trip's maps are drawn
/// with a halo of paper so they stand clear of its roads. The 1997 look draws the same plan into the frame's pixels instead.
/// </summary>
public partial class RoguetripMapLayer : Control
{
    public required Func<RoguetripLayout.RegionPlan?> Plan { get; init; }
    /// <summary>Hidden while a pop-up or an overlay is up (they are the frame's pixels, under this layer).</summary>
    public required Func<bool> Covered { get; init; }
    /// <summary>An atlas lies under the drawing (the marks get their halo).</summary>
    public Func<bool>? OnAtlas { get; init; }

    public static readonly Color Ink = Color.Color8(40, 30, 18), Road = Color.Color8(128, 88, 50), Pencil = Color.Color8(120, 110, 96),
        Marker = Color.Color8(222, 96, 38), RedPencil = Color.Color8(150, 36, 22), Paper = Color.Color8(246, 214, 140);

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
        bool halo = OnAtlas?.Invoke() == true;
        var glow = new Color(Paper, 0.85f);
        foreach (var sg in plan.Segments)
        {
            var (c, w, on, off) = sg.Kind switch
            {
                RoguetripLayout.Ink.State => (new Color(Pencil, 0.7f), 0.8f, 5f, 3f),
                RoguetripLayout.Ink.Run => (new Color(Marker, 0.9f), 1.8f, 3f, 2f),
                RoguetripLayout.Ink.Approach => (new Color(Marker, 0.85f), 1.6f, 4f, 3f),
                _ => (new Color(Road, 0.7f), 1.1f, 0f, 0f),
            };
            if (halo) Pen(sg.X0, sg.Y0, sg.X1, sg.Y1, glow, w + 2.2f, 0, 0);
            Pen(sg.X0, sg.Y0, sg.X1, sg.Y1, c, w, on, off);
        }
        foreach (var d in plan.Dots)
        {
            if (halo) DrawCircle(T(d.X, d.Y), 3.6f * _s, glow, true, -1, true);
            if (d.Kind == "town") DrawCircle(T(d.X, d.Y), 1.4f * _s, Ink, true, -1, true);
            else if (d.Kind == "visited") DrawCircle(T(d.X, d.Y), 2.4f * _s, Ink, true, -1, true);
            else DrawArc(T(d.X, d.Y), 2.2f * _s, 0, Mathf.Tau, 24, halo ? Ink : Pencil, (halo ? 1f : 0.8f) * _s, true);
        }
        foreach (var o in plan.Offers)
        {
            var c = o.Selected ? Marker : Ink;
            // inside the ring the atlas is covered with paper, so its number reads
            if (halo) DrawCircle(T(o.X, o.Y), (o.Selected ? 13.5f : 9.5f) * _s, glow, true, -1, true);
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
/// The player's road atlas laid on the sheet (the modern look): multiplied onto it, as print on paper, its own paper fitted out to
/// white so the sheet's shows through and its edges faded into the sheet. Under the region's drawing.
/// </summary>
public partial class RoguetripAtlasLayer : Control
{
    public required Func<Texture2D?> Picture { get; init; }
    public required Func<bool> Covered { get; init; }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Mul };
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (Covered() || Picture() is not { } tex) return;
        var size = GetViewportRect().Size;
        float s = Mathf.Min(size.X / ShellFrame.W, size.Y / ShellFrame.H);
        var off = (size - new Vector2(ShellFrame.W, ShellFrame.H) * s) / 2;
        var b = RoguetripLayout.RegionBox;
        DrawTextureRect(tex, new Rect2(off + new Vector2(b.X, b.Y) * s, new Vector2(b.W, b.H) * s), false);
    }
}

/// <summary>
/// The player's road atlas under the region (optional; docs/remix-missions.md "The choice map"): a picture in the user folder's
/// remix/ folder, roguetrip-atlas.png (or .jpg, .webp), registered to the map by roguetrip-atlas.txt (control points: latitude,
/// longitude, the picture's x and y, one a line). Nothing ships: with no files, or while the picture is being read, or when it
/// cannot be, the drawn map stands alone (<see cref="RoguetripLayout.AtlasShown"/>). Read and fitted off the main thread (a scanned
/// atlas can be 7000 px across, a second or two), the region box's part of it resampled to the box, its paper made white, its edges
/// faded; the result kept beside it (roguetrip-atlas.fit-*.png, by the picture's size and time) so later sessions load it at once.
/// </summary>
public static class RoguetripAtlas
{
    public const string Picture = "roguetrip-atlas", Points = "roguetrip-atlas.txt";
    /// <summary>Texels a shell pixel the backdrop is resampled to (the region box at 3440 x 1440 is shown 3 times its size).</summary>
    public const int Scale = 4;
    /// <summary>The edges' fade into the sheet, shell pixels.</summary>
    private const float Fade = 6;

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

    /// <summary>Where the atlas is; <paramref name="start"/> starts its reading when it is there and not yet read (the screen polls it each frame while it loads).</summary>
    public static RoguetripLayout.AtlasState State(string dir, bool start = true)
    {
        if (Files(dir) is not { } f) return RoguetripLayout.AtlasState.Absent;
        if (_from != f.Picture) { _from = f.Picture; _tex = null; _px1997 = null; _job = null; }
        if (_job is null)
        {
            if (!start) return RoguetripLayout.AtlasState.Loading;
            _job = System.Threading.Tasks.Task.Run(() => Fit(dir, f.Picture, f.Points));
        }
        if (!_job.IsCompleted) return RoguetripLayout.AtlasState.Loading;
        return _job.IsCompletedSuccessfully && _job.Result is not null ? RoguetripLayout.AtlasState.Ready : RoguetripLayout.AtlasState.Failed;
    }

    /// <summary>The fitted picture as a texture once ready, else null.</summary>
    public static ImageTexture? Texture(string dir)
    {
        if (State(dir) != RoguetripLayout.AtlasState.Ready) return null;
        if (_tex is not null) return _tex;
        var r = _job!.Result!.Value;
        var img = Image.CreateFromData(r.W, r.H, false, Image.Format.Rgba8, r.Rgba);
        img.GenerateMipmaps();
        return _tex = ImageTexture.CreateFromImage(img);
    }

    /// <summary>The fitted picture at one texel a shell pixel (the 1997 look's), once ready, else null.</summary>
    public static (byte[] Rgba, int W, int H)? Small(string dir)
    {
        if (State(dir) != RoguetripLayout.AtlasState.Ready) return null;
        int w = (int)RoguetripLayout.RegionBox.W, h = (int)RoguetripLayout.RegionBox.H;
        if (_px1997 is not null) return (_px1997, w, h);
        var r = _job!.Result!.Value;
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

    /// <summary>A string's FNV-1a hash (the same every session, unlike string.GetHashCode).</summary>
    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>
    /// The region box's part of the picture: each texel's latitude and longitude (the region's projection), through the fit to the
    /// picture, sampled bilinear; then its paper (the brightest tenth's level, per channel) taken to white and the edges faded to white
    /// (multiplied onto the sheet, white is the sheet). Kept beside the picture by its size and time, and read from there next time.
    /// </summary>
    private static (byte[] Rgba, int W, int H)? Fit(string dir, string picture, string points)
    {
        var box = RoguetripLayout.RegionBox;
        int w = (int)box.W * Scale, h = (int)box.H * Scale;
        try
        {
            var info = new FileInfo(picture);
            string kept = Path.Combine(dir, $"{Picture}.fit-{info.Length}-{info.LastWriteTimeUtc.Ticks}-{w}x{h}-{Fnv(File.ReadAllText(points)):x8}.png");
            if (File.Exists(kept) && Image.LoadFromFile(kept) is { } done && done.GetWidth() == w && done.GetHeight() == h)
            {
                done.Convert(Image.Format.Rgba8);
                GD.Print($"roguetrip: atlas {Path.GetFileName(picture)} (fitted before)");
                return (done.GetData(), w, h);
            }
            if (RoguetripLayout.AtlasFit.Parse(File.ReadAllText(points)) is not { } fit) { GD.PrintErr($"roguetrip: {points}: three control points or more needed"); return null; }
            var img = Image.LoadFromFile(picture);
            if (img is null || img.IsEmpty()) return null;
            img.Convert(Image.Format.Rgba8);
            int sw = img.GetWidth(), sh = img.GetHeight();
            var src = img.GetData();
            var reg = RemixRegion.Builtin();
            var o = new byte[w * h * 4];
            var inside = new bool[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var (lat, lon) = RoguetripLayout.Unproject(reg, (x + 0.5) / Scale, (y + 0.5) / Scale, box.W, box.H);
                    var (px, py) = fit.ToPicture(lat, lon);
                    px -= 0.5; py -= 0.5;
                    int x0 = (int)Math.Floor(px), y0 = (int)Math.Floor(py);
                    double fx = px - x0, fy = py - y0;
                    if (x0 < 0 || y0 < 0 || x0 + 1 >= sw || y0 + 1 >= sh) continue;
                    inside[y * w + x] = true;
                    for (int c = 0; c < 3; c++)
                    {
                        double v = src[(y0 * sw + x0) * 4 + c] * (1 - fx) * (1 - fy) + src[(y0 * sw + x0 + 1) * 4 + c] * fx * (1 - fy)
                                 + src[((y0 + 1) * sw + x0) * 4 + c] * (1 - fx) * fy + src[((y0 + 1) * sw + x0 + 1) * 4 + c] * fx * fy;
                        o[(y * w + x) * 4 + c] = (byte)Math.Clamp(v + 0.5, 0, 255);
                    }
                }
            // its paper: per channel, the level the brightest tenth of the inside reaches
            var paper = new double[3];
            for (int c = 0; c < 3; c++)
            {
                var hist = new int[256];
                int n = 0;
                for (int i = 0; i < w * h; i++) if (inside[i]) { hist[o[i * 4 + c]]++; n++; }
                int acc = 0, lv = 255;
                for (; lv > 0 && acc < n / 10; lv--) acc += hist[lv];
                paper[c] = Math.Max(lv, 64);
            }
            // the distance to the box's edge or to the picture's, for the fade (a pass each way along rows and columns)
            var dist = new float[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = inside[i] ? float.MaxValue : 0;
            for (int y = 0; y < h; y++)
            {
                float d = 0;
                for (int x = 0; x < w; x++) { int i = y * w + x; d = inside[i] ? d + 1 : 0; dist[i] = Math.Min(dist[i], d); }
                d = 0;
                for (int x = w - 1; x >= 0; x--) { int i = y * w + x; d = inside[i] ? d + 1 : 0; dist[i] = Math.Min(dist[i], d); }
            }
            for (int x = 0; x < w; x++)
            {
                float d = 0;
                for (int y = 0; y < h; y++) { int i = y * w + x; d = inside[i] ? d + 1 : 0; dist[i] = Math.Min(dist[i], d); }
                d = 0;
                for (int y = h - 1; y >= 0; y--) { int i = y * w + x; d = inside[i] ? d + 1 : 0; dist[i] = Math.Min(dist[i], d); }
            }
            for (int i = 0; i < w * h; i++)
            {
                float t = Math.Clamp(dist[i] / (Fade * Scale), 0, 1);
                t = t * t * (3 - 2 * t);
                for (int c = 0; c < 3; c++)
                {
                    double v = Math.Min(255, o[i * 4 + c] * 255.0 / paper[c]);
                    o[i * 4 + c] = (byte)Math.Clamp(255 - (255 - v) * t + 0.5, 0, 255);
                }
                o[i * 4 + 3] = 255;
            }
            GD.Print($"roguetrip: atlas {Path.GetFileName(picture)} {sw}x{sh}, {fit.Points} points, fit within {fit.MedianMiss:0} px (median), {fit.MaxMiss:0} px (worst); its paper ({paper[0]:0}, {paper[1]:0}, {paper[2]:0})");
            try { Image.CreateFromData(w, h, false, Image.Format.Rgba8, o).SavePng(kept); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return (o, w, h);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            GD.PrintErr($"roguetrip: atlas not read: {ex.Message}");
            return null;
        }
    }
}
