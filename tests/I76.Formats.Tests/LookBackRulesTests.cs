using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The context bindings: commands that stand for others while the head looks back or the stick is pulled back (docs/joystick.md).</summary>
public class LookBackRulesTests
{
    private sealed class Pads : IPadSource
    {
        public List<PadRaw> List = [new PadRaw()];
        public IReadOnlyList<PadRaw> Read() => List;
    }

    private static readonly Func<InputTerm, bool> NoKey = _ => false;

    private sealed class Rig
    {
        public readonly Pads Src = new();
        public readonly JoystickBank Bank;
        public readonly BindingTable Table;
        public Rig(params PadRules.Rule[] rules) : this(PadPreset.Build((string?)null), rules) { }
        public Rig(BindingTable table, params PadRules.Rule[] rules) { Table = table; Bank = new JoystickBank(Src); foreach (var r in rules) Table.Rules.Add(r); }

        public void Frame(bool back, float ly = 0, params int[] buttons)
        {
            ulong b = 0;
            foreach (var k in buttons) b |= 1UL << k;
            Src.List[0] = new PadRaw(0, ly, 0, 0, 0, 0, b);
            Bank.Poll(true);
            Table.Rules.Update(new PadRules.Frame(Bank, 0, back, PadRules.ShiftHeld(Table, Bank), PadRules.DownOf(Table, Bank, NoKey)));
        }

        public bool Holds(string command) => Table.Rules.Holds(Table, command, Bank, NoKey);
    }

    [Fact]
    public void LookingBackTheFireButtonFiresTheRearHardpoint()
    {
        // fire on A as the preset has it and on Start as well (the preset's second fire, RB, is the e-brake since the shift layer)
        var t = PadPreset.Build((string?)null);
        var r = new Rig(BindingTable.FromBlocks([.. t.Blocks, BindingTable.NewBlock("weapon_fire", "joystick1", "Button8")]), PadRules.LookBackPair());
        r.Frame(back: false, buttons: PadButton.A);
        Assert.True(r.Holds("weapon_fire"));
        Assert.False(r.Holds("hardpoint3_fire"));
        r.Frame(back: true, buttons: PadButton.A);
        Assert.False(r.Holds("weapon_fire"));
        Assert.True(r.Holds("hardpoint3_fire"));
        // every input of fire, Start as well: the rule is on the command
        r.Frame(back: true, buttons: PadButton.Start);
        Assert.True(r.Holds("hardpoint3_fire"));
        Assert.Equal(["weapon_fire"], r.Table.Rules.Sources("hardpoint3_fire").Skip(1));
        r.Frame(back: false, buttons: PadButton.A);
        Assert.True(r.Holds("weapon_fire"));
    }

    [Fact]
    public void LookingBackTheLookSticksClickDropsTheDropperAndNotItsOwnCommand()
    {
        var r = new Rig(PadRules.LookBackPair());
        var view = r.Table.Of("toggle_cmbt_view").Single(BindingTable.IsPad);   // R3's own binding
        r.Frame(back: false, buttons: PadButton.RightStick);
        Assert.False(r.Holds("hardpoint4_fire"));
        Assert.False(r.Table.Rules.Masks(view));
        Assert.Empty(r.Table.Rules.Pulses);
        r.Frame(back: true);
        r.Frame(back: true, buttons: PadButton.RightStick);
        Assert.True(r.Holds("hardpoint4_fire"));
        Assert.Equal(["hardpoint4_fire"], r.Table.Rules.Pulses);            // pressed once, then held
        Assert.True(r.Table.Rules.Masks(view));                              // the view does not change as well
        r.Frame(back: true, buttons: PadButton.RightStick);
        Assert.Empty(r.Table.Rules.Pulses);
        Assert.True(r.Holds("hardpoint4_fire"));
        r.Frame(back: true);
        Assert.False(r.Holds("hardpoint4_fire"));
    }

    [Fact]
    public void WithTheStickPulledBackTheHandbrakeButtonMeansReverse()
    {
        var r = new Rig(PadRules.StickBackReverse());
        r.Frame(back: false, ly: 0f, buttons: PadButton.RightShoulder);       // the preset's e-brake (RB, LB being the shift)
        Assert.True(r.Holds("e_brake"));
        Assert.False(r.Holds("reverse_direction"));
        r.Frame(back: false, ly: 0.8f, buttons: PadButton.RightShoulder);
        Assert.True(r.Holds("e_brake"));                                     // held before the stick came back: still the handbrake
        r.Frame(back: false, ly: 0.8f);
        r.Frame(back: false, ly: 0.8f, buttons: PadButton.RightShoulder);
        Assert.False(r.Holds("e_brake"));
        Assert.Contains("e_brake", r.Table.Rules.Sources("reverse_direction"));   // its press is reverse's (Controls.Pressed)
        Assert.Empty(r.Table.Rules.Sources("e_brake"));
        r.Frame(back: false, ly: -0.9f, buttons: PadButton.RightShoulder);
        Assert.False(r.Holds("e_brake"));                                    // still held: still reverse's press, no handbrake
        // pressed again pushed forward, or only a little back: the handbrake
        r.Frame(back: false, ly: -0.9f);
        r.Frame(back: false, ly: -0.9f, buttons: PadButton.RightShoulder);
        Assert.True(r.Holds("e_brake"));
        r.Frame(back: false, ly: 0.45f);
        r.Frame(back: false, ly: 0.45f, buttons: PadButton.RightShoulder);
        Assert.True(r.Holds("e_brake"));
    }

    [Fact]
    public void TheRulesAreLinesAndSwitchOnAndOffTogether()
    {
        var rules = new PadRules();
        rules.Toggle(PadRules.LookBackPair());
        rules.Toggle(PadRules.StickBackReverse());
        var text = rules.ToText();
        Assert.Contains("lookback weapon_fire hardpoint3_fire", text);
        Assert.Contains("lookbackinput joystick1 Button10 hardpoint4_fire", text);
        Assert.Contains("pulledback joystick1 Down/Up e_brake reverse_direction", text);
        var again = PadRules.Parse(text);
        Assert.Equal(3, again.Rules.Count);
        Assert.True(PadRules.LookBackPair().All(again.Has));
        again.Toggle(PadRules.LookBackPair());
        Assert.Single(again.Rules);
        // a context rule takes no input: R3 given a command keeps the rule
        var t = PadPreset.Build((string?)null);
        t.Rules.Toggle(PadRules.LookBackPair());
        t.Assign("HONK_HORN", "joystick1", "Button10");
        Assert.Equal(2, t.Rules.Rules.Count);
    }
}
