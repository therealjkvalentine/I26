using Godot;

namespace I76.Game.Ui;

/// <summary>
/// A remix's radio line (the port's; docs/remix-missions.md "Briefing"): when a job's next objective opens, its line is lettered
/// across the top of the screen for a few seconds, as a CB message, over the note sound the objectives already play. Every step
/// has a spoken instruction where the install has a fitting clip; this says the step's own words, which no clip can. Remixes only:
/// a stock mission never shows it. Under it, the spoken line on the air ("Spoken lines"): who says it and its words, for as long
/// as it plays and a little after, whether or not its audio could be built on this machine.
/// </summary>
public partial class RemixRadioLine : Control
{
    /// <summary>Seconds a line stays, then fades over one more.</summary>
    public const float Hold = 8f;

    public Font? Lee { get; init; }
    private string _text = "", _radio = "";
    private float _left, _radioLeft;

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

    /// <summary>Shows a spoken line on the air: the speaker's name and its words, for its length and two seconds more.</summary>
    public void Radio(string speaker, string text, float seconds)
    {
        _radio = speaker.Length > 0 ? $"{speaker}: {text}" : text;
        _radioLeft = Mathf.Max(3f, seconds + 2f) + 1;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_left <= 0 && _radioLeft <= 0) return;
        _left -= (float)delta;
        _radioLeft -= (float)delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Lee is null) return;
        float s = Mathf.Min(Size.X / 640f, Size.Y / 480f);
        int size = Mathf.Max(8, Mathf.RoundToInt(15 * s));
        // under the cockpit's panels (they reach 140 of 480), over the sky; the radio's row below the objective's
        float y = 168 * s;
        if (_left > 0 && _text.Length > 0) { Row(_text, y, Mathf.Clamp(_left, 0, 1), new Color(1f, 0.93f, 0.62f)); }
        if (_radioLeft > 0 && _radio.Length > 0) Row(_radio, y + size + 18 * s, Mathf.Clamp(_radioLeft, 0, 1), new Color(0.82f, 0.95f, 1f));

        void Row(string text, float at, float a, Color c)
        {
            float w = Mathf.Min(Size.X - 40 * s, Lee.GetStringSize(text, HorizontalAlignment.Left, -1, size).X);
            var p = new Vector2((Size.X - w) / 2, at);
            DrawRect(new Rect2(p - new Vector2(10 * s, size + 4 * s), new Vector2(w + 20 * s, size + 14 * s)), new Color(0, 0, 0, 0.55f * a));
            DrawString(Lee, p, text, HorizontalAlignment.Left, Size.X - 40 * s, size, c with { A = a });
        }
    }
}
