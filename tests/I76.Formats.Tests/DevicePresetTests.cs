using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The presets for known wheels and sticks, chosen by the device, and the axis threshold binding (docs/joystick.md).</summary>
public class DevicePresetTests
{
    private sealed class Sticks : IPadSource
    {
        public List<PadRaw> List = [PadRaw.OfGeneric(new float[GenericStick.MaxAxes], 0)];
        public IReadOnlyList<PadRaw> Read() => List;
    }

    [Theory]
    [InlineData("T300RS Racing Wheel", 0x044F, 0xB66E, "Thrustmaster T300RS")]
    [InlineData("Thrustmaster FFB Wheel", 0x044F, 0xB65D, "Thrustmaster T300RS")]      // before the driver: the ids, not the name
    [InlineData("Thrustmaster T300RS", 0, 0, "Thrustmaster T300RS")]                     // no ids: the name
    [InlineData("HID-compliant game controller", 0x068E, 0x00F3, "CH Fighterstick")]     // Windows names it nothing useful
    [InlineData("CH FIGHTERSTICK USB", 0, 0, "CH Fighterstick")]
    [InlineData("Logitech Extreme 3D", 0x046D, 0xC215, null)]
    [InlineData("T150 Racing Wheel", 0x044F, 0xB677, null)]
    public void ADeviceIsMatchedByItsIdsOrItsName(string name, int vendor, int product, string? preset) =>
        Assert.Equal(preset, DevicePresets.Match(new DevicePresets.Device(name, (uint)vendor, (uint)product))?.Name);

    [Fact]
    public void TheWheelSteersOnItsCombinedPedalsAndButtonSixIsAShift()
    {
        var t = DevicePresets.Apply(new BindingTable(), DevicePresets.T300RS, "joystick1", true);
        Assert.Equal(new InputTerm(false, "joystick1", "Left/Right"), t.AxisOf("steer"));
        Assert.Equal(new InputTerm(false, "joystick1", "Down/Up"), t.AxisOf("throttle"));    // accelerator one way, brake the other
        Assert.True(t.Rules.IsEmpty);
        // R1 fires, and with 6 held targets the front: the original's own chords
        Assert.Equal([new InputTerm(true, "joystick1", "Button2"), new InputTerm(false, "joystick1", "Button6")], t.Of("weapon_fire").First(b => b.Terms[0].Channel == "Button2").Terms);
        Assert.Equal([new InputTerm(true, "joystick1", "Button2"), new InputTerm(true, "joystick1", "Button6")], t.Of("frontal_target").Single().Terms);
        Assert.DoesNotContain(t.Blocks, b => b.Terms[0].Channel == "Button6");                 // the shift alone does nothing
        Assert.Equal(3, t.Of("HONK_HORN").Count);                                           // three horns, as laid out
        Assert.Equal("HatRight", t.Of("SHOW_MAP").Single().Terms[0].Channel);                // the map on the D-pad, never on a rim button
        // the file keeps it
        Assert.Equal(t.Blocks.Count(), BindingTable.Parse(t.ToText()).Blocks.Count());
    }

    [Fact]
    public void TheStickBesideAWheelIsAGearboxAndAloneItSteers()
    {
        var beside = DevicePresets.Apply(new BindingTable(), DevicePresets.Fighterstick, "joystick2", false);
        Assert.Null(beside.AxisOf("steer"));
        var th = beside.Rules.Rules.OfType<PadRules.AxisThreshold>().ToList();
        Assert.Equal(["e_brake", "reverse_direction", "shift_down", "shift_up"], th.Select(r => r.Command));
        Assert.All(th, r => Assert.Equal("joystick2", r.Axis.Device));
        Assert.Equal("Button6", beside.Of("hardpoint3_fire").Single().Terms[0].Channel);       // the convex serrated hat's right
        Assert.Equal("pilot_glance_down", beside.Blocks.Single(b => b.Terms[0].Channel == "HatUp").Command);   // the cone hat's up looks down
        var alone = DevicePresets.Apply(new BindingTable(), DevicePresets.Fighterstick, "joystick1", true);
        Assert.Equal(new InputTerm(false, "joystick1", "Left/Right"), alone.AxisOf("steer"));
        Assert.Equal(new InputTerm(false, "joystick1", "Axis3"), alone.AxisOf("throttle"));
        Assert.Equal(["e_brake", "reverse_direction"], alone.Rules.Rules.OfType<PadRules.AxisThreshold>().Select(r => r.Command));
    }

    [Fact]
    public void APresetReplacesOnlyItsOwnDeviceAndOnlyWhereThereIsNothing()
    {
        var t = PadPreset.Build("weapon_fire {\n   + keyboard Space\n}\n");
        Assert.True(DevicePresets.Unbound(t, "joystick2"));
        Assert.False(DevicePresets.Unbound(t, "joystick1"));
        var both = DevicePresets.Apply(t, DevicePresets.Fighterstick, "joystick2", false);
        Assert.Equal("Space", both.KeyText("weapon_fire"));
        Assert.Contains(both.Of("weapon_fire"), b => b.Terms[0].Device == "joystick1");      // the pad keeps its own
        Assert.Contains(both.Of("weapon_fire"), b => b.Terms[0].Device == "joystick2");
        Assert.False(DevicePresets.Unbound(both, "joystick2"));
        // applied again (Restore): the same, not twice
        var again = DevicePresets.Apply(both, DevicePresets.Fighterstick, "joystick2", false);
        Assert.Equal(both.Blocks.Count(), again.Blocks.Count());
        Assert.Equal(both.Rules.Rules.Count, again.Rules.Rules.Count);
    }

    private static List<string> Pulses(PadRules.AxisThreshold rule, string axis, params float[] positions)
    {
        var src = new Sticks();
        var bank = new JoystickBank(src);
        var rules = new PadRules();
        rules.Add(rule);
        var o = new List<string>();
        int n = GenericStick.AxisNumber(axis);
        foreach (var p in positions)
        {
            var ax = new float[GenericStick.MaxAxes];
            ax[n - 1] = p;
            src.List[0] = PadRaw.OfGeneric(ax, 0);
            bank.Poll(true);
            rules.Update(new PadRules.Frame(bank, 0));
            o.Add(string.Join("+", rules.Pulses) + (rules.Held(rule.Command) ? "#" : ""));
        }
        return o;
    }

    [Fact]
    public void ALevelThresholdHoldsWhilePastAndLetsGoUnderTheLowerLine()
    {
        var back = new PadRules.AxisThreshold(new InputTerm(true, "joystick1", "Down/Up"), true, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Level, "e_brake");
        // 0.44 is short, 0.46 engages (pressed and held), 0.35 still held (over the release), 0.29 lets go; forward does nothing
        Assert.Equal(["", "e_brake#", "#", "", "", ""], Pulses(back, "Down/Up", 0.44f, 0.46f, 0.35f, 0.29f, 0.40f, -1f));
    }

    [Fact]
    public void AnEdgeThresholdPressesOncePerExcursion()
    {
        var right = new PadRules.AxisThreshold(new InputTerm(true, "joystick1", "Left/Right"), true, 0.55f, 0.40f, PadRules.AxisThreshold.Mode.Edge, "shift_up");
        // held over does not shift again; back under 40 % and out again does
        Assert.Equal(["shift_up", "", "", "", "shift_up", ""], Pulses(right, "Left/Right", 0.6f, 1f, 0.45f, 0.3f, 0.56f, 0.9f));
    }

    [Fact]
    public void AMomentaryThresholdPressesGoingInAndAgainComingOut()
    {
        var fwd = new PadRules.AxisThreshold(new InputTerm(true, "joystick2", "Down/Up"), false, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Momentary, "reverse_direction");
        // the second stick: reverse (a toggle) in on the push, out again when it comes back
        var src = new Sticks { List = [PadRaw.OfGeneric(new float[GenericStick.MaxAxes], 0), PadRaw.OfGeneric(new float[GenericStick.MaxAxes], 0)] };
        var bank = new JoystickBank(src);
        var rules = new PadRules();
        rules.Add(fwd);
        var o = new List<string>();
        foreach (var y in new[] { -0.2f, -0.7f, -0.9f, -0.31f, -0.1f })
        {
            var ax = new float[GenericStick.MaxAxes];
            ax[1] = y;
            src.List[1] = PadRaw.OfGeneric(ax, 0);
            bank.Poll(true);
            rules.Update(new PadRules.Frame(bank, 0));
            o.Add(string.Join("+", rules.Pulses));
        }
        Assert.Equal(["", "reverse_direction", "", "", "reverse_direction"], o);
    }

    [Fact]
    public void ThresholdsAreLinesAndOneEachWayOnAnAxis()
    {
        var y = new InputTerm(true, "joystick1", "Down/Up");
        var rules = new PadRules();
        rules.Add(new PadRules.AxisThreshold(y, true, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Level, "e_brake"));
        rules.Add(new PadRules.AxisThreshold(y, false, 0.45f, 0.30f, PadRules.AxisThreshold.Mode.Momentary, "reverse_direction"));
        Assert.Equal(2, rules.Rules.Count);                                                  // back and forward both
        rules.Add(new PadRules.AxisThreshold(y, true, 0.5f, 0.33f, PadRules.AxisThreshold.Mode.Edge, "shift_down"));
        Assert.Equal(2, rules.Rules.Count);                                                  // a new one back replaces the old back
        var text = rules.ToText();
        Assert.Contains("threshold joystick1 Down/Up - 0.45 0.3 momentary reverse_direction", text);
        Assert.Contains("threshold joystick1 Down/Up + 0.5 0.33 edge shift_down", text);
        Assert.Equal(text, PadRules.Parse(text).ToText());
    }
}
