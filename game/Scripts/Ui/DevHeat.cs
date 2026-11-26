using Godot;
using I76.Game.World;

namespace I76.Game.Ui;

/// <summary>
/// The Developer panel's Heat shimmer group (docs/dev-hooks.md): Heat Shimmer's numbers (World.HeatTune) on sliders, live in the running
/// mission and never saved (a new run starts from the shipped values), with the mission's sun, palette warmth and hot day, and a button
/// that prints the numbers and copies them to the clipboard so they can be handed back. While the group is up the pass runs with the game
/// paused (HeatTune.Live), so a change shows at once; closed, nothing of it exists.
/// </summary>
public static class DevHeat
{
    private sealed record Knob(string Name, float Min, float Max, float Step, System.Func<float> Get, System.Action<float> Set, string Unit = "");

    private static readonly Knob[] Knobs =
    [
        new("fire strength", 0, 4, 0.05f, () => HeatTune.Fire, v => HeatTune.Fire = v, "x"),
        new("road ripple strength", 0, 4, 0.05f, () => HeatTune.Road, v => HeatTune.Road = v, "x"),
        new("mirage strength", 0, 4, 0.05f, () => HeatTune.Mirage, v => HeatTune.Mirage = v, "x"),
        new("ripple on paved road", 0, 1, 0.05f, () => HeatTune.PavedShare, v => HeatTune.PavedShare = v, "x"),
        new("ripple on open ground", 0, 1, 0.05f, () => HeatTune.GroundShare, v => HeatTune.GroundShare = v, "x"),
        new("mirage off the road", 0, 1, 0.05f, () => HeatTune.MirageGround, v => HeatTune.MirageGround = v, "x"),
        new("road band from", 10, 400, 5, () => HeatTune.RoadNear, v => HeatTune.RoadNear = v, " m"),
        new("ripple band under level", 0.2f, 8, 0.05f, () => HeatTune.RippleDeg, v => HeatTune.RippleDeg = v, " deg"),
        new("ripple band over level", 0.2f, 8, 0.05f, () => HeatTune.RippleAboveDeg, v => HeatTune.RippleAboveDeg = v, " deg"),
        new("mirage band", 0.2f, 6, 0.05f, () => HeatTune.MirageDeg, v => HeatTune.MirageDeg = v, " deg"),
        new("fire full to", 5, 300, 5, () => HeatTune.FireFull, v => HeatTune.FireFull = v, " m"),
        new("fire gone at", 20, 600, 10, () => HeatTune.FireFar, v => HeatTune.FireFar = v, " m"),
        new("fire column size", 0.25f, 3, 0.05f, () => HeatTune.FireColumn, v => HeatTune.FireColumn = v, "x"),
        new("hot day: sun at least", 0, 90, 1, () => HotDay.MinElevation, v => { HotDay.MinElevation = v; HeatShimmer.Reevaluate(); }, " deg"),
        new("hot day: warmth at least", 0, 0.8f, 0.005f, () => HotDay.MinWarmth, v => { HotDay.MinWarmth = v; HeatShimmer.Reevaluate(); }),
    ];

    private static string Show(Knob k, float v) => k.Unit == "x" ? $"{v:0.##}x" : $"{v:0.###}{k.Unit}";

    /// <summary>The group into <paramref name="col"/>; <paramref name="say"/> shows a line in the panel.</summary>
    public static void Build(VBoxContainer col, System.Action<string> say)
    {
        var heading = new Label { Text = "Heat shimmer (live, not saved)" };
        heading.AddThemeColorOverride("font_color", new Color(0.95f, 0.65f, 0.25f));
        col.AddChild(heading);
        var readout = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        readout.AddThemeColorOverride("font_color", new Color(0.75f, 0.9f, 0.75f));
        DevScale.Min(readout, 400);
        void Refresh() => readout.Text = $"setting: {(HeatShimmer.Setting switch { 0 => "Off (the sliders show nothing until Heat Shimmer is on)", 1 => "Fires", _ => "Fires + Road" })}, Shimmer Strength {I76.Game.Shell.HeatStrengthRow.NameOf(HeatShimmer.Strength)} (x{HeatShimmer.Strength:0.##}){(HeatShimmer.Look1997 ? ", 1997 look: none" : "")}\n{HeatShimmer.Readout()}";
        Refresh();
        col.AddChild(readout);
        var grid = DevScale.Grid(new GridContainer { Columns = 3 }, 8, 2);
        var sliders = new List<(Knob K, HSlider S, Label V)>();
        foreach (var k in Knobs)
        {
            var name = new Label { Text = k.Name };
            var value = DevScale.Min(new Label { Text = Show(k, k.Get()) }, 70);
            var slider = DevScale.Min(new HSlider { MinValue = k.Min, MaxValue = k.Max, Step = k.Step, Value = k.Get(), FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }, 200);
            slider.ValueChanged += v =>
            {
                k.Set((float)v);
                value.Text = Show(k, (float)v);
                Refresh();
            };
            grid.AddChild(name); grid.AddChild(slider); grid.AddChild(value);
            sliders.Add((k, slider, value));
        }
        col.AddChild(grid);
        var force = new OptionButton { FocusMode = Control.FocusModeEnum.None };
        force.AddItem("hot day: found by the rule"); force.AddItem("hot day: forced on"); force.AddItem("hot day: forced off");
        force.Selected = HeatTune.ForceHot switch { true => 1, false => 2, null => 0 };
        force.ItemSelected += i => { HeatTune.ForceHot = i switch { 1 => true, 2 => false, _ => null }; HeatShimmer.Reevaluate(); Refresh(); };
        // by the ground's grazing angle and surface (the shipped), or section IJ's band round level over any ground, to compare
        var roadOnly = new CheckBox { Text = "by the ground", ButtonPressed = HeatTune.ByGround, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "On: the ripple by the angle between the view and the ground, full on paved road, the shares below elsewhere. Off: the band round level over any ground (section IJ's)." };
        roadOnly.Toggled += on => { HeatTune.ByGround = on; Refresh(); };
        var print = new Button { Text = "Print & copy the numbers", FocusMode = Control.FocusModeEnum.None };
        print.Pressed += () =>
        {
            var line = $"{HeatTune.Line()} ({HeatShimmer.Readout()})";
            DisplayServer.ClipboardSet(line);
            say(line + " - copied");
        };
        var reset = new Button { Text = "Shipped values", FocusMode = Control.FocusModeEnum.None };
        reset.Pressed += () =>
        {
            HeatTune.Reset();
            foreach (var (k, s, v) in sliders) { s.SetValueNoSignal(k.Get()); v.Text = Show(k, k.Get()); }
            force.Selected = HeatTune.ForceHot switch { true => 1, false => 2, null => 0 };
            roadOnly.SetPressedNoSignal(HeatTune.ByGround);
            Refresh();
            say("heat shimmer: the shipped values");
        };
        var row = DevScale.Sep(new HBoxContainer(), 6);
        row.AddChild(force); row.AddChild(roadOnly); row.AddChild(print); row.AddChild(reset);
        col.AddChild(row);
        HeatTune.Live = true;
        col.TreeExiting += () => HeatTune.Live = false;
    }
}
