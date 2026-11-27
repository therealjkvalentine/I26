using Godot;

namespace I76.Game.Ui;

/// <summary>
/// A remix's radio line (the port's; docs/remix-missions.md "Briefing"): when a job's next objective opens, its line is lettered
/// across the top of the screen for a few seconds, as a CB message, over the note sound the objectives already play. Every step
/// has a spoken instruction where the install has a fitting clip; this says the step's own words, which no clip can. Remixes only:
/// a stock mission never shows it.
/// </summary>
public partial class RemixRadioLine : Control
{
    /// <summary>Seconds a line stays, then fades over one more.</summary>
    public const float Hold = 8f;

    public Font? Lee { get; init; }
    private string _text = "";
    private float _left;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ProcessMode = ProcessModeEnum.Pausable;
    }

    /// <summary>Shows a line (an objective's text, its "(hidden)" mark already gone).</summary>
    public void Say(string text)
    {
        _text = text;
        _left = Hold + 1;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_left <= 0) return;
        _left -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_left <= 0 || _text.Length == 0 || Lee is null) return;
        float s = Mathf.Min(Size.X / 640f, Size.Y / 480f);
        int size = Mathf.Max(8, Mathf.RoundToInt(15 * s));
        float a = Mathf.Clamp(_left, 0, 1);
        float w = Mathf.Min(Size.X - 40 * s, Lee.GetStringSize(_text, HorizontalAlignment.Left, -1, size).X);
        // under the cockpit's panels (they reach 140 of 480), over the sky
        var at = new Vector2((Size.X - w) / 2, 168 * s);
        DrawRect(new Rect2(at - new Vector2(10 * s, size + 4 * s), new Vector2(w + 20 * s, size + 14 * s)), new Color(0, 0, 0, 0.55f * a));
        DrawString(Lee, at, _text, HorizontalAlignment.Left, Size.X - 40 * s, size, new Color(1f, 0.93f, 0.62f, a));
    }
}
