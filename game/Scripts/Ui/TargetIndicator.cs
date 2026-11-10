using Godot;
using I76.Game.Sim;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;

namespace I76.Game.Ui;

/// <summary>
/// The box around the radar's current target, as the original draws one:
/// four corner brackets sized by the original's rule (TargetBox: the
/// collider's radius over its depth, 23 to 150 of the 640x480 frame's pixels,
/// so a fixed 26x34 beyond about 40 m), one frame pixel thick and drawn crisp
/// at the window's scale; its health as a bar just over the box (coloured by
/// thirds, white while unhurt), and its distance beneath. Red for the other side, green
/// for ours, grey for nobody's; when the target is off screen, an arrow at the
/// screen's edge points the way.
/// </summary>
public partial class TargetIndicator : Control
{
    public required Func<Vehicle?> Player { get; init; }
    public required Combat Combat { get; init; }
    public required Func<System.Numerics.Vector3> Origin { get; init; }
    /// <summary>
    /// The node the target is drawn as. The box follows it rather than the
    /// sim's position: a far car (the cheap path, flag 0x20000) keeps its
    /// height while it drives and is drawn on the ground, and drawn cars are
    /// interpolated between ticks.
    /// </summary>
    public Func<Vehicle, Node3D?>? CarOf { get; init; }
    /// <summary>The 1997 look's box (the original's sprites, bar and starburst), drawn instead while it is on.</summary>
    public TargetIndicator1997? Look1997 { get; init; }

    /// <summary>
    /// Drawn inside the rear-view mirror's viewport (inMirror = 1): the same box and health bar, no
    /// off-screen marker and no distance. The mirror's shader flips the picture left-right, so the bar fills from the other side.
    /// </summary>
    public bool Mirror { get; init; }

    private static readonly bool BoxLog = System.Environment.GetEnvironmentVariable("I76_TARGET_BOX_LOG") == "1";

    public override void _Ready()
    {
        AnchorRight = 1; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    /// <summary>The target's health as a 0–1 fraction for the bar (the health percentage: HealthGauge); none left once it is dead.</summary>
    private static float Health(Vehicle v) => Combat.IsDead(v) ? 0 : I76.Sim.Damage.HealthGauge.Fraction(v);

    public override void _Draw()
    {
        if (Player() is not { } p || Combat.RadarTarget.GetValueOrDefault(p) is not { } t || Combat.IsDead(t)) return;
        var cam = GetViewport().GetCamera3D();
        if (cam is null) return;
        if (Look1997?.On() == true) { Look1997.Draw(this, cam, t, Health(t), Mirror); return; }
        var origin = Origin();
        int team = Combat.Team(t), mine = Combat.Team(p);
        var col = Combat.IsDead(t) ? new Color(0.5f, 0.5f, 0.5f) : team == 0 ? new Color(0.85f, 0.85f, 0.85f) : team == mine ? new Color(0.35f, 1f, 0.45f) : new Color(1f, 0.3f, 0.2f);
        var size = GetViewportRect().Size;

        var node = CarOf?.Invoke(t) is { } n && IsInstanceValid(n) ? n : null;
        // the original's box (TargetBox): centred on the collider record's centre, on the car as drawn, sized by its radius over
        // its depth, in the 640x480 frame's pixels (the window's height is 480 of them; the mirror's picture 256 across)
        var (centreGame, radius) = Combat.Collider(t);
        var midBox = new System.Numerics.Vector3((t.BoxX[0] + t.BoxX[3]) / 2, (t.BoxY[1] + t.BoxY[3]) / 2, (t.BoxZ[0] + t.BoxZ[3]) / 2);
        var at = node is not null ? node.GlobalTransform * Coords.ToGodot(midBox) : Coords.ToGodot(centreGame - origin);
        float depth = -(cam.GlobalTransform.AffineInverse() * at).Z;
        float dist = (t.Pos.ToVector3() - p.Pos.ToVector3()).Length();
        var font = GetThemeDefaultFont();   // the root window's theme: Arial, the menus' face (Godot's own without it)

        var sc = depth > 0 ? cam.UnprojectPosition(at) : Vector2.Zero;
        bool onScreen = depth > 0 && sc.X >= 0 && sc.Y >= 0 && sc.X < size.X && sc.Y < size.Y;
        if (!onScreen && Mirror) return;
        if (!onScreen)
        {
            // an arrow at the edge, toward the target
            var local = cam.GlobalTransform.AffineInverse() * at;
            var dir = new Vector2(local.X, -local.Y);
            if (dir.LengthSquared() < 1e-6f) dir = Vector2.Up;
            dir = dir.Normalized();
            var c = size / 2;
            float r = Mathf.Min(size.X, size.Y) * 0.42f;
            var tip = c + dir * r;
            var side = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon([tip + dir * 14, tip - dir * 6 + side * 10, tip - dir * 6 - side * 10], col);
            DrawString(font, tip - dir * 26 - new Vector2(20, -5), $"{dist:0} m", HorizontalAlignment.Center, 40, 13, col);
            return;
        }

        float k = Mirror ? size.X / 256f : TargetBox.Scale(size);
        float f = TargetBox.Focal(size.X / k, size.Y / k, cam.Fov, cam.KeepAspect == Camera3D.KeepAspectEnum.Width);
        var box = TargetBox.Rect((int)(sc.X / k), (int)(sc.Y / k), TargetBox.Size(radius, depth, f));
        // capture hook: I76_TARGET_BOX_LOG=1 prints the box's inputs and result once a second (frame pixels)
        if (BoxLog && !Mirror && Godot.Engine.GetProcessFrames() % 30 == 0)
            GD.Print($"target box: {t.Definition} R {radius:0.00} depth {depth:0.00} f {f:0.0} S {TargetBox.Size(radius, depth, f)} box {box.Size.X}x{box.Size.Y} at {box.Position} k {k:0.###}");
        // the corners as the original's 8x8 sprites: one frame pixel thick, eight long, drawn crisp at the window's scale
        float x0 = Mathf.Round(box.Position.X * k), y0 = Mathf.Round(box.Position.Y * k);
        float x1 = Mathf.Round(box.End.X * k), y1 = Mathf.Round(box.End.Y * k);
        float t1 = Mathf.Max(1, Mathf.Round(k)), arm = Mathf.Round(TargetBox.Corner * k);
        DrawRect(new Rect2(x0, y0, arm, t1), col); DrawRect(new Rect2(x0, y0, t1, arm), col);
        DrawRect(new Rect2(x1 - arm, y0, arm, t1), col); DrawRect(new Rect2(x1 - t1, y0, t1, arm), col);
        DrawRect(new Rect2(x0, y1 - t1, arm, t1), col); DrawRect(new Rect2(x0, y1 - arm, t1, arm), col);
        DrawRect(new Rect2(x1 - arm, y1 - t1, arm, t1), col); DrawRect(new Rect2(x1 - t1, y1 - arm, t1, arm), col);

        // the health bar over the box, the original's size and place (TargetHealthBar) at the window's scale; the distance under it
        var outline = new Color(0, 0, 0, 0.55f);
        float hp = TargetHealthBar.Percent(Health(t));
        var (outer, inner) = TargetHealthBar.Rects(hp, (int)(sc.X / k) * k, y0, k);
        // white unhurt, then red, yellow and green by thirds (the colours the original builds)
        var fill = TargetHealthBar.Band(hp) switch { 3 => new Color(1f, 1f, 1f), 2 => new Color(0.32f, 0.61f, 0.32f), 1 => new Color(0.8f, 0.78f, 0.13f), _ => new Color(0.82f, 0f, 0.16f) };
        if (outer.HasArea()) DrawRect(outer.Grow(1), outline);
        if (inner.HasArea()) DrawRect(inner, fill);
        if (!Mirror) DrawString(font, new Vector2((x0 + x1) / 2 - 40, y1 + 18), $"{dist:0} m", HorizontalAlignment.Center, 80, 13, col);
    }
}
