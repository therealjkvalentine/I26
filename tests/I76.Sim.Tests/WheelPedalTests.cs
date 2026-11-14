using I76.Formats.Input;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// A racing wheel with separate pedals (#173 and its report on #166: pressing the gas pedal, the car stops steering; the brake does not). On
/// the Joystick screen a pedal that rests at one end of its axis can be the throttle axis, or hold a command past a line of its travel (an
/// axis threshold). With the brake as the throttle axis and the gas holding Accelerate, every gas press was a driving key held, and the
/// original's rule gives the keys both rows: the wheel's steering was dropped for as long as the pedal was down.
/// </summary>
public class WheelPedalTests
{
    private sealed class Wheel : IPadSource
    {
        public float Steer, Gas = -1, Brake = -1;   // a separate pedal rests at -1 and reads +1 pressed
        public IReadOnlyList<PadRaw> Read() => [PadRaw.OfGeneric([Steer, Gas, Brake], 0)];
    }

    private const string Map = "steer {\n - joystick1 Left/Right\n}\nthrottle {\n - joystick1 Axis3\n}\n";

    /// <summary>The pad side of one tick as SimDriver samples it: the rules, the rows, the driving commands and the mix.</summary>
    private static CarInput Tick(Wheel w, BindingTable table, AxisControls axes, JoystickBank bank, long ms)
    {
        bank.Poll(true);
        table.Rules.Update(new PadRules.Frame(bank, ms));
        axes.Update(bank, _ => false);
        var held = CarButtons.None; var rule = CarButtons.None;
        foreach (var (cmd, b) in new[] { ("throttle_up", CarButtons.ThrottleUp), ("throttle_down", CarButtons.ThrottleDown), ("steer_left", CarButtons.SteerLeft), ("steer_right", CarButtons.SteerRight) })
        {
            if (table.Rules.Holds(table, cmd, bank, _ => false)) held |= b;
            if (table.Rules.Held(cmd) && !table.Rules.BindingHolds(table, cmd, bank, _ => false)) rule |= b;
        }
        return StickRows.Mix(held, rule, axes.Active(bank), AxisControls.Wire(axes.Throttle), AxisControls.Wire(axes.Steer));
    }

    [Fact]
    public void TheGasPedalOnAThresholdNoLongerDropsTheWheel()
    {
        var w = new Wheel { Steer = -0.5f };
        var table = BindingTable.Parse(Map);
        table.Rules.Add(new PadRules.AxisThreshold(new InputTerm(true, "joystick1", "Axis2"), true, 0.5f, 0.33f, PadRules.AxisThreshold.Mode.Level, "throttle_up"));
        var bank = new JoystickBank(w);
        var axes = new AxisControls(table.Blocks);
        var rest = Tick(w, table, axes, bank, 0);
        Assert.True(rest.Has(CarButtons.Analog));
        short steer = rest.Steer;
        Assert.True(steer > 0);   // turned left, + left

        // the gas pedal pressed down, frame by frame: past its line the car accelerates, and the wheel still steers
        foreach (var (gas, ms) in new[] { (0f, 50L), (0.6f, 100L), (1f, 150L) })
        {
            w.Gas = gas;
            var t = Tick(w, table, axes, bank, ms);
            Assert.True(t.Has(CarButtons.Analog));
            Assert.Equal(steer, t.Steer);
            if (gas > 0.5f) { Assert.Equal(32767, t.Throttle); Assert.False(t.Has(CarButtons.ThrottleUp)); }
        }
    }

    /// <summary>The original's rule stands for keys and buttons: a key held, the keys have both rows (the stick's steering dropped).</summary>
    [Fact]
    public void AKeyStillTakesBothRows()
    {
        var t = StickRows.Mix(CarButtons.ThrottleUp, CarButtons.None, true, -32767, 12000);
        Assert.False(t.Has(CarButtons.Analog));
        Assert.True(t.Has(CarButtons.ThrottleUp));
        Assert.Equal(0, t.Steer);
        // a key and a rule together: the key's way, as before
        Assert.False(StickRows.Mix(CarButtons.ThrottleUp | CarButtons.SteerLeft, CarButtons.ThrottleUp, true, 0, 9000).Has(CarButtons.Analog));
    }

    [Fact]
    public void RulesAloneTakeOnlyTheirRows()
    {
        var brake = StickRows.Mix(CarButtons.ThrottleDown | CarButtons.Fire, CarButtons.ThrottleDown, true, 5000, -7000);
        Assert.Equal((short)-32767, brake.Throttle);
        Assert.Equal((short)-7000, brake.Steer);
        Assert.True(brake.Has(CarButtons.Fire));
        Assert.False(brake.Has(CarButtons.ThrottleDown));
        var both = StickRows.Mix(CarButtons.ThrottleUp | CarButtons.ThrottleDown, CarButtons.ThrottleUp | CarButtons.ThrottleDown, true, 5000, 0);
        Assert.Equal((short)0, both.Throttle);
        // no stick bound: the rule's command is a held key as ever
        var keys = StickRows.Mix(CarButtons.ThrottleUp, CarButtons.ThrottleUp, false, 0, 0);
        Assert.False(keys.Has(CarButtons.Analog));
        Assert.True(keys.Has(CarButtons.ThrottleUp));
    }
}
