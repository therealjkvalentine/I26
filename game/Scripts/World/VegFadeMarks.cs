using Godot;

namespace I76.Game.World;

/// <summary>
/// I76_VEG_MARKS=1 (a capture hook): a ring and its distance from the camera over every plant that is fading in or out
/// now (between its layer's fade start and end) and lies in view, so a clip shows where and how far off the plants appear.
/// Yellow: the shrubs; orange: the ground layer's larger plants; grey: the grass tufts (only the nearest few of those).
/// </summary>
public partial class VegFadeMarks : Control
{
    public static readonly bool On = System.Environment.GetEnvironmentVariable("I76_VEG_MARKS") == "1";

    public readonly List<(Vector3 Global, float Distance, int Kind)> Marks = [];

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (GetViewport()?.GetCamera3D() is not { } cam) return;
        var font = ThemeDB.FallbackFont;
        foreach (var (g, d, kind) in Marks)
        {
            if (cam.IsPositionBehind(g)) continue;
            var p = cam.UnprojectPosition(g);
            var col = kind == 0 ? new Color(1, 0.9f, 0.1f) : kind == 1 ? new Color(1, 0.5f, 0.1f) : new Color(0.7f, 0.7f, 0.7f);
            DrawArc(p, 9, 0, Mathf.Tau, 20, col, 2);
            DrawString(font, p + new Vector2(11, -6), $"{d:0}", HorizontalAlignment.Left, -1, 15, col);
        }
    }
}
