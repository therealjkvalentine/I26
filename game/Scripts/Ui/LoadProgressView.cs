using Godot;
using I76.Game.Shell;

namespace I76.Game.Ui;

/// <summary>
/// The loading card's progress (the port's, [port] load_progress; the original's card is a still picture): a wheel that fills as a
/// pie clockwise from 12 o'clock (load_wheel.gdshader) and "LOADING 62%" beside it, set as the main menu's items are, in Impact slanted
/// 18° at their 15 px, white; in the 1997 look the same Impact cut into the 640x480 frame's pixels (ImpactPixels) and the wheel at
/// that scale. Top right in the card's 4:3 frame, where no card of either game has text, scaled with it. Drawn straight into its
/// canvas items (no deferred redraw), so that while the main thread loads without frames (<see cref="LoadProgress.Blocking"/>) it can be
/// presented at the load's stamps by a forced draw of the card alone (the world's 3D off for it), at most every
/// <see cref="PresentEveryMs"/>.
/// </summary>
public partial class LoadProgressView : Control
{
    /// <summary>The look: the 1997 look's pixels, or the modern look's at the window's resolution.</summary>
    public bool Faithful { get; init; }
    /// <summary>The sheet's variants: 0 the steel wheel lit as it fills, 1 a white wedge in a dim wheel, 2 as 0 with the stage's word.</summary>
    public int Style { get; set; }

    /// <summary>The wheel's centre and radius, the text's right edge and baseline, in the 640x480 frame's pixels.</summary>
    public const float WheelX = 604, WheelY = 36, WheelR = 22, TextRight = 574, TextSize = 15;
    public static float Baseline => WheelY + 6;
    /// <summary>The forced draws' spacing while the main thread loads straight through: about five a second.</summary>
    public const double PresentEveryMs = 200;

    private Control _wheel = null!, _text = null!;
    private ShaderMaterial _mat = null!;
    private Font? _impact;
    private FontVariation? _slanted;
    private double _shown = -1;
    private string _shownWord = "";
    private long _lastDraw;
    private bool _presenting;
    private double? _fixed;
    private ImageTexture? _label;
    private Image? _labelImage;
    private readonly Dictionary<string, (bool[] Mask, int W)> _inks = [];
    private const int LabelW = 160, LabelH = 22;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 1;   // over the card's picture and its Impact line, whichever is made first
        Visible = LoadProgress.Active;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Shaders/load_wheel.gdshader") };
        AddChild(_wheel = new Control { MouseFilter = MouseFilterEnum.Ignore, Material = _mat, TextureFilter = TextureFilterEnum.Nearest });
        AddChild(_text = new Control { MouseFilter = MouseFilterEnum.Ignore, TextureFilter = Faithful ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear });
        _wheel.Draw += () => Paint(false);
        _text.Draw += () => Paint(false);
        _impact = HdAssets.Impact(Faithful ? TextServer.FontAntialiasing.None : TextServer.FontAntialiasing.Gray, TextServer.Hinting.None);
        if (_impact is not null)
            _slanted = new FontVariation { BaseFont = _impact, VariationTransform = new Transform2D(1f, Mathf.Tan(Mathf.DegToRad(ImpactLabels.Slant)), 0f, 1f, 0f, 0f) };
        // capture hooks: I76_LOAD_WHEEL=STYLE[:FRACTION] shows that variant (at that fraction, held); I76_LOAD_WHEEL_SHEET=PATH saves the variants
        if (System.Environment.GetEnvironmentVariable("I76_LOAD_WHEEL") is { Length: > 0 } hook)
        {
            var parts = hook.Split(':');
            if (int.TryParse(parts[0], out int st)) Style = st;
            if (parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f)) _fixed = f;
        }
        _lastDraw = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderingServer.FramePostDraw += Drawn;
        LoadProgress.Present = Present;
    }

    public override void _ExitTree()
    {
        RenderingServer.FramePostDraw -= Drawn;
        if (LoadProgress.Present == Present) LoadProgress.Present = null;
    }

    private void Drawn() => _lastDraw = System.Diagnostics.Stopwatch.GetTimestamp();

    private double Fraction => _fixed ?? LoadProgress.Model.Value;
    /// <summary>LOADING, or (variant 2) the stage's word.</summary>
    private string Word => Style == 2 ? LoadProgress.Model.Word : "LOADING";

    public override void _Process(double delta)
    {
        if (Size != _size) { _size = Size; _shown = -1; }
        Visible = LoadProgress.Active || _fixed is not null;
        if (!Visible) return;
        Paint(true);
        // (a few frames after the card is up: the start-up's splash, taken away as the card comes up, is gone by then)
        if (GetParent() is CanvasItem { Modulate.R: >= 0.999f }) _upFrames++;
        if (_sheet is null && _upFrames > 3 && System.Environment.GetEnvironmentVariable("I76_LOAD_WHEEL_SHEET") is { Length: > 0 } sheet)
        {
            _sheet = sheet;
            SaveSheet(sheet);
        }
    }

    private Vector2 _size;
    private string? _sheet;
    private int _upFrames;

    /// <summary>
    /// At a stamp of the load (LoadProgress.Present): while the main thread loads straight through, the card with the wheel as it is now,
    /// drawn at once, if the last frame (drawn or forced) is <see cref="PresentEveryMs"/> old and the per cent has moved. The root
    /// viewport's 3D is off for the draw (the card covers it; the world, in the tree or not, is not drawn behind it).
    /// </summary>
    private void Present()
    {
        if (!I76.Game.Boot.LoadProfiler.IsMain || !LoadProgress.Blocking || _presenting || !IsVisibleInTree()) return;   // (the bake's workers call too)
        if (System.Diagnostics.Stopwatch.GetElapsedTime(_lastDraw).TotalMilliseconds < PresentEveryMs) return;
        if (Percent(Fraction) == Percent(_shown) && Word == _shownWord) return;
        _presenting = true;
        try
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Paint(true);
            var vp = GetViewport();
            var rid = vp.GetViewportRid();
            RenderingServer.ViewportSetDisable3D(rid, true);
            RenderingServer.ForceDraw(true, 0);
            RenderingServer.ViewportSetDisable3D(rid, vp.Disable3D);
            Presented++;
            double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            PresentedMs += ms;
            if (I76.Game.Boot.LoadProfiler.On) GD.Print($"load wheel: {Percent(_shown)}% ({LoadProgress.Model.Word}) drawn in {ms:0.0} ms");
        }
        finally { _presenting = false; }
    }

    /// <summary>Forced draws made in this run and the milliseconds they took (the load profile's line).</summary>
    public static int Presented;
    public static double PresentedMs;

    public static int Percent(double f) => LoadWheel.Percent(f);

    /// <summary>The frame's scale and offset in the window (640x480 kept 4:3 and centred, as the card).</summary>
    private (float S, Vector2 Off) Frame()
    {
        var size = GetViewportRect().Size;
        float s = Mathf.Min(size.X / ShellFrame.W, size.Y / ShellFrame.H);
        return (s, (size - new Vector2(ShellFrame.W, ShellFrame.H) * s) / 2);
    }

    /// <summary>Both canvas items drawn again (only if the per cent or the word changed, unless <paramref name="onlyChanged"/> is false).</summary>
    private void Paint(bool onlyChanged)
    {
        if (_wheel is null) return;
        double f = Fraction;
        string word = Word;
        if (onlyChanged && Percent(f) == Percent(_shown) && word == _shownWord) return;
        _shown = f; _shownWord = word;
        var (s, off) = Frame();
        _mat.SetShaderParameter("fill", Percent(f) / 100f);
        _mat.SetShaderParameter("style", Style == 1 ? 1 : 0);
        _mat.SetShaderParameter("pixels", Faithful ? WheelR * 2 : 0f);
        var w = _wheel.GetCanvasItem();
        RenderingServer.CanvasItemClear(w);
        var at = off + new Vector2(WheelX - WheelR, WheelY - WheelR) * s;
        RenderingServer.CanvasItemAddRect(w, new Rect2(at, new Vector2(WheelR * 2, WheelR * 2) * s), Colors.White);
        var t = _text.GetCanvasItem();
        RenderingServer.CanvasItemClear(t);
        if (_impact is null) return;
        string pct = $"{Percent(f)}%";
        var pens = Layout(word, pct);
        if (!Faithful)
        {
            int px = Mathf.Max(1, Mathf.RoundToInt(TextSize * s));
            foreach (var (ch, x) in pens)
                _slanted!.DrawString(t, off + new Vector2(x * s, Baseline * s), ch.ToString(), HorizontalAlignment.Left, -1, px, Colors.White);
            return;
        }
        // the 1997 look: the word's and the per cent's pixels at the frame's scale (ImpactPixels, as the menu's own entries are cut), one picture drawn nearest
        _labelImage ??= Image.CreateEmpty(LabelW, LabelH, false, Image.Format.La8);
        _labelImage.Fill(new Color(0, 0, 0, 0));
        int x0 = Mathf.FloorToInt(TextRight - LabelW + 8), top = Mathf.RoundToInt(Baseline - 17);
        Blit(Ink(word), Mathf.RoundToInt(pens[0].X) - x0 - 2);
        Blit(Ink(pct), Mathf.RoundToInt(pens[word.Length].X) - x0 - 2);
        if (_label is null) _label = ImageTexture.CreateFromImage(_labelImage); else _label.Update(_labelImage);
        RenderingServer.CanvasItemAddTextureRect(t, new Rect2(off + new Vector2(x0, top) * s, new Vector2(LabelW, LabelH) * s), _label.GetRid());
    }

    private void Blit((bool[] Mask, int W)? ink, int at)
    {
        if (ink is not { } g) return;
        for (int r = 0; r < LabelH; r++)
            for (int c = 0; c < g.W; c++)
                if (g.Mask[r * g.W + c] && at + c is >= 0 and < LabelW) _labelImage!.SetPixel(at + c, r, Colors.White);
    }

    /// <summary>A string's ink in a box of the label's height, the pen 2 px in, the baseline 17 px down (the 1997 look; each kept once made).</summary>
    private (bool[] Mask, int W)? Ink(string text)
    {
        if (_inks.TryGetValue(text, out var g)) return g;
        int w = Mathf.CeilToInt(_impact!.GetStringSize(text, HorizontalAlignment.Left, -1, (int)TextSize).X) + 12;
        var mask = ImpactPixels.Mask(new ImpactLabels.Spec(text, TextSize, 0f, 2f, 17f, 0f), w, LabelH);
        if (mask is null) return null;
        _inks[text] = g = (mask, w);
        return g;
    }

    /// <summary>Each character's pen in the frame: the word, a space, the per cent right-aligned in a field of "100%", the whole ending at <see cref="TextRight"/>.</summary>
    private List<(char Ch, float X)> Layout(string word, string pct)
    {
        float Adv(string c) => _impact!.GetStringSize(c, HorizontalAlignment.Left, -1, (int)TextSize).X;
        float field = Adv("100%"), gap = Adv(" ") * 1.5f, wordW = Adv(word);
        float x = TextRight - field - gap - wordW;
        var pens = new List<(char, float)>();
        for (int i = 0; i < word.Length; i++) pens.Add((word[i], x + Adv(word[..i])));
        float px = TextRight - Adv(pct);
        for (int i = 0; i < pct.Length; i++) pens.Add((pct[i], px + Adv(pct[..i])));
        return pens;
    }

    /// <summary>I76_LOAD_WHEEL_SHEET=PATH: every variant at 25, 50, 75 and 100 % drawn and saved (PATH_s{style}_{per cent}.png), then the game quits.</summary>
    private void SaveSheet(string path)
    {
        int keep = Style;
        var vp = GetViewport();
        foreach (int st in new[] { 0, 1, 2 })
            foreach (double f in new[] { 0.25, 0.5, 0.75, 1.0 })
            {
                Style = st; _fixed = f; _shown = -1;
                Paint(false);
                RenderingServer.ViewportSetDisable3D(vp.GetViewportRid(), true);
                RenderingServer.ForceDraw(true, 0);
                RenderingServer.ForceDraw(true, 0);
                RenderingServer.ViewportSetDisable3D(vp.GetViewportRid(), vp.Disable3D);
                vp.GetTexture().GetImage().SavePng(path.Replace(".png", $"_s{st}_{(int)(f * 100):000}.png"));
            }
        Style = keep; _fixed = null;
        GD.Print($"load wheel sheet: saved {path}");
        GetTree().Quit();
    }
}
