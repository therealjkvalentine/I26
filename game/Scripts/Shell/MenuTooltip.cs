using Godot;

namespace I76.Game.Shell;

/// <summary>
/// When a tooltip shows (the port's; the original's menus had no help): after the pointer, or the pad's or keyboard's focus, has
/// rested on the same row for <see cref="Delay"/>, until it leaves the row or a click changes something. Pure, so it is tested without the engine.
/// </summary>
public sealed class TooltipTimer
{
    /// <summary>The usual hover delay of a desktop tooltip.</summary>
    public const double Delay = 0.5;
    private object? _key;
    private double _held;

    /// <summary>The row under the pointer or focus this frame (null for none): true while its tooltip shows.</summary>
    public bool Update(object? key, double dt)
    {
        if (!Equals(key, _key)) { _key = key; _held = 0; return false; }
        if (key is null) return false;
        _held += dt;
        return _held >= Delay;
    }

    /// <summary>A click: the tooltip goes, and comes back only after the delay again.</summary>
    public void Restart() => _held = 0;
}

/// <summary>A box on the screen, in window pixels.</summary>
public readonly record struct TipBox(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
}

/// <summary>
/// Where a tooltip goes (pure, window pixels): under and right of the pointer, or for the pad and the keys beside the focused row
/// (right of it, else left of it, else under it); turned to the other side where it would leave the screen, and held inside the
/// screen with a margin whatever the window's shape (4:3, 16:9, an ultrawide's pillarboxed menus).
/// </summary>
public static class TooltipPlacement
{
    /// <summary>The gap to the screen's edge, and the pointer's offset (the cursor's own size, so the box does not sit under it).</summary>
    public const float Margin = 4, PointerDx = 14, PointerDy = 20, Gap = 8;

    /// <summary>The box's place for a box <paramref name="w"/> x <paramref name="h"/>, pointer at (<paramref name="px"/>, <paramref name="py"/>) or, with <paramref name="focus"/>, beside the row <paramref name="row"/>.</summary>
    public static TipBox Place(float w, float h, float screenW, float screenH, float px, float py, bool focus, TipBox row)
    {
        float x, y;
        if (focus)
        {
            x = row.Right + Gap;
            y = row.Y;
            if (x + w > screenW - Margin) x = row.X - Gap - w;
            if (x < Margin) { x = row.X; y = row.Bottom + Gap / 2; if (y + h > screenH - Margin) y = row.Y - Gap / 2 - h; }
        }
        else
        {
            x = px + PointerDx;
            y = py + PointerDy;
            if (x + w > screenW - Margin) x = px - Gap - w;
            if (y + h > screenH - Margin) y = py - Gap - h;
        }
        x = Math.Clamp(x, Margin, Math.Max(Margin, screenW - Margin - w));
        y = Math.Clamp(y, Margin, Math.Max(Margin, screenH - Margin - h));
        return new TipBox(x, y, w, h);
    }

    /// <summary>The widest a box may be: about 300 of the menu's 640 pixels, never wider than the screen less its margins.</summary>
    public static float MaxWidth(float scale, float screenW) => Math.Min(300 * scale, screenW - 2 * Margin);

    /// <summary>
    /// Words into lines no wider than <paramref name="width"/> (a word wider than that keeps a line of its own); a figure keeps its unit.
    /// A line break in the text starts a new line (the equipment tooltips' second line, its numbers).
    /// </summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float width) =>
        text.Contains('\n') ? [.. text.Split('\n').SelectMany(part => GraphicsHelp.Wrap(part, measure, width))] : GraphicsHelp.Wrap(text, measure, width);
}

/// <summary>
/// The menus' tooltip (the port's): a small box of paper with the menus' ink, the setting's help in Arial (the face of the pads'
/// titles and the host form's labels), drawn at the window's resolution over everything. One per menu: the shell's
/// (<see cref="Shell"/>: every notepad sheet, the host forms, the key map, Mouse Options) and the Esc menu's pad. The owner calls
/// <see cref="Track"/> every frame with the row under the pointer or the focus; the box appears after <see cref="TooltipTimer.Delay"/>,
/// where it first appeared, and goes when the row is left.
/// </summary>
public partial class MenuTooltip : Control
{
    /// <summary>The 1997 look: the text one-bit, as the pads' own there.</summary>
    public bool Faithful { get; init; }
    private readonly TooltipTimer _timer = new();
    private Font? _font;
    private string? _text;
    private List<string> _lines = [];
    private TipBox _box;
    private int _size;
    private float _pad;
    private object? _shown;

    private static readonly Color Paper = Color.Color8(250, 246, 230, 248), Ink = Color.Color8(24, 22, 18), Edge = Color.Color8(60, 52, 40), Shadow = new(0, 0, 0, 0.35f);

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = HdAssets.Arial(false, Faithful ? TextServer.FontAntialiasing.None : TextServer.FontAntialiasing.Gray) ?? new SystemFont();
    }

    /// <summary>
    /// The row under the pointer or the focus this frame: <paramref name="key"/> names it (null for none), <paramref name="text"/> is its help,
    /// <paramref name="row"/> its box and <paramref name="pointer"/> the pointer, in window pixels; <paramref name="focus"/> places the box beside the row (the pad,
    /// the keys) rather than by the pointer; <paramref name="scale"/> is the menu's (window pixels per menu pixel).
    /// </summary>
    public void Track(object? key, string? text, TipBox row, Vector2 pointer, bool focus, float scale, double dt)
    {
        if (string.IsNullOrWhiteSpace(text)) key = null;
        bool show = _timer.Update(key is null ? null : (key, text), dt);
        if (!show) { if (_shown is not null) { _shown = null; QueueRedraw(); } return; }
        if (_shown is not null && Equals(_shown, key) && _text == text) return;
        _shown = key;
        _text = text;
        Layout(text!, row, pointer, focus, scale);
        QueueRedraw();
    }

    /// <summary>A click: the box goes until the delay has passed again.</summary>
    public void Restart()
    {
        _timer.Restart();
        if (_shown is not null) { _shown = null; QueueRedraw(); }
    }

    public bool Showing => _shown is not null;

    private void Layout(string text, TipBox row, Vector2 pointer, bool focus, float scale)
    {
        var screen = GetViewportRect().Size;
        _size = Math.Max(12, (int)MathF.Round(9.5f * scale));
        _pad = Math.Max(4, MathF.Round(4 * scale));
        float maxW = TooltipPlacement.MaxWidth(scale, screen.X) - 2 * _pad;
        float Measure(string s) => _font!.GetStringSize(s, HorizontalAlignment.Left, -1, _size).X;
        _lines = TooltipPlacement.Wrap(text, Measure, maxW);
        float w = _lines.Count == 0 ? 0 : _lines.Max(Measure) + 2 * _pad, lineH = _font!.GetHeight(_size);
        float h = lineH * _lines.Count + 2 * _pad;
        _box = TooltipPlacement.Place(w, h, screen.X, screen.Y, pointer.X, pointer.Y, focus, row);
    }

    public override void _Draw()
    {
        if (_shown is null || _font is null) return;
        var r = new Rect2(_box.X, _box.Y, _box.W, _box.H);
        float px = Math.Max(1, _pad / 4);
        DrawRect(new Rect2(r.Position + new Vector2(px * 2, px * 2), r.Size), Shadow);
        DrawRect(r, Paper);
        DrawRect(r, Edge, false, px);
        float lineH = _font.GetHeight(_size), y = _box.Y + _pad + _font.GetAscent(_size);
        foreach (var line in _lines)
        {
            DrawString(_font, new Vector2(_box.X + _pad, y), line, HorizontalAlignment.Left, -1, _size, Ink);
            y += lineH;
        }
    }
}
