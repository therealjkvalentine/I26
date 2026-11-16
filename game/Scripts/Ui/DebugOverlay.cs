using Godot;

namespace I76.Game.Ui;

/// <summary>Top-left text: frame rate, camera position in game units, and what did not load.</summary>
public partial class DebugOverlay : CanvasLayer
{
    private Label _label = null!;
    public Func<string>? Text { get; set; }

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(8, 8) };
        _label.AddThemeColorOverride("font_color", Colors.White);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.Ui);
        // made only while it shows (Ctrl+F3 sets Visible before the frame's process step): hidden, it was some 10 kB of strings a frame
        if (!Visible) return;
        _label.Text = $"{Engine.GetFramesPerSecond():0} fps\n" + (Text?.Invoke() ?? "");
    }
}
