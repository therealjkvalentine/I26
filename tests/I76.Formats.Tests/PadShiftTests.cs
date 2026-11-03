using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>The pad's shift layer: a button that, held, gives the other pad inputs their second command (docs/joystick.md).</summary>
public class PadShiftTests
{
    private sealed class Pads : IPadSource
    {
        public List<PadRaw> List = [new PadRaw()];
        public IReadOnlyList<PadRaw> Read() => List;
    }

    private static (JoystickBank Bank, Pads Src) Bank() { var p = new Pads(); return (new JoystickBank(p), p); }

    private static void Press(JoystickBank bank, Pads src, params int[] buttons)
    {
        ulong b = 0;
        foreach (var k in buttons) b |= 1UL << k;
        src.List[0] = new PadRaw(0, 0, 0, 0, 0, 0, b);
        bank.Poll(true);
    }

    private static bool NoKey(InputTerm _) => false;

    private static string[] HeldCommands(BindingTable t, JoystickBank bank) =>
        [.. t.Blocks.Select(b => b.Command).Distinct(StringComparer.OrdinalIgnoreCase).Where(c => AxisControls.RowOf(c) < 0 && t.Holds(c, bank, NoKey))];   // the axis rows are read as axes

    [Fact]
    public void ShiftHeldSelectsTheSecondCommandAndReleasedTheFirst()
    {
        var t = PadPreset.Build((string?)null);
        var (bank, src) = Bank();
        Press(bank, src, PadButton.A);
        Assert.Equal(["weapon_fire"], HeldCommands(t, bank));
        Press(bank, src, PadButton.A, PadButton.LeftShoulder);
        Assert.Equal(["hardpoint3_fire"], HeldCommands(t, bank));          // the rear gun, not the selected weapon as well
        Press(bank, src, PadButton.A);
        Assert.Equal(["weapon_fire"], HeldCommands(t, bank));
        // the D-pad too: right is next target, with the shift the gear up
        Press(bank, src, PadButton.DpadRight);
        Assert.Equal(["NEXT_TARGET"], HeldCommands(t, bank));
        Press(bank, src, PadButton.DpadRight, PadButton.LeftShoulder);
        Assert.Equal(["shift_up"], HeldCommands(t, bank));
    }

    [Fact]
    public void TheShiftAloneDoesNothing()
    {
        var t = PadPreset.Build((string?)null);
        var (bank, src) = Bank();
        Press(bank, src, PadButton.LeftShoulder);
        Assert.Empty(HeldCommands(t, bank));
        // a button that had a command of its own loses it when it becomes the shift
        var m = BindingTable.Parse("e_brake {\n   + joystick1   Button5\n}\nweapon_fire {\n   + joystick1   Button1\n}\n");
        m.SetShift("joystick1", "Button5");
        Assert.Empty(m.Of("e_brake"));
        Assert.Empty(HeldCommands(m, bank));
    }

    [Fact]
    public void AnInputWithoutASecondCommandKeepsItsFirstWithTheShiftHeld()
    {
        var t = PadPreset.Build((string?)null);
        var (bank, src) = Bank();
        Press(bank, src, PadButton.RightShoulder, PadButton.LeftShoulder);
        Assert.Equal(["e_brake"], HeldCommands(t, bank));
        Assert.Single(t.Of("e_brake").Single(BindingTable.IsPad).Terms);       // no exclusion: nothing to give way to
    }

    [Fact]
    public void TheLayerIsTheOriginalsChordsInTheFileAndComesBackFromIt()
    {
        var t = PadPreset.Build((string?)null);
        var text = t.ToText();
        Assert.Contains("hardpoint3_fire {\n   + joystick1  Button1\n\t+ joystick1\tButton5\n}", text);
        Assert.Contains("weapon_fire {\n   + joystick1  Button1\n\t- joystick1\tButton5\n}", text);
        var again = BindingTable.Parse(text);
        Assert.Null(again.Shift);
        Assert.Equal(new InputTerm(true, "joystick1", "Button5"), again.DeriveShift());
        again.Shift = again.DeriveShift();
        Assert.Equal(t.PadLayer(true).Count(), again.PadLayer(true).Count());
        // chords on two different buttons imply no shift
        Assert.Null(BindingTable.Parse("a {\n + joystick1 Button1\n + joystick1 Button5\n}\nb {\n + joystick1 Button2\n + joystick1 Button6\n}\n").DeriveShift());
    }

    [Fact]
    public void EditingALayerLeavesTheOtherAndTheExclusionsFollow()
    {
        var t = BindingTable.Parse("weapon_fire {\n   + joystick1   Button1\n}\nweapon_cycle {\n   + joystick1   Button2\n}\n");
        t.SetShift("joystick1", "Button5");
        Assert.Single(t.Of("weapon_fire").Single().Terms);
        t.AssignShifted("hardpoint1_fire", "joystick1", "Button1");
        Assert.Equal(2, t.Of("weapon_fire").Single().Terms.Length);              // A now gives way to its second command
        Assert.False(t.Of("weapon_fire").Single().Terms[1].Plus);
        // the second command moves to another input: the first loses its exclusion again
        t.AssignShifted("hardpoint1_fire", "joystick1", "Button2");
        Assert.Single(t.Of("weapon_fire").Single().Terms);
        Assert.Equal("Button2", t.Of("hardpoint1_fire").Single().Terms[0].Channel);
        // a first command on the same input leaves its second command alone
        t.Assign("special1", "joystick1", "Button2");
        Assert.Empty(t.Of("weapon_cycle"));
        Assert.Single(t.Of("hardpoint1_fire"));
        Assert.Equal(2, t.Of("special1").Single().Terms.Length);
        // clearing the second command only
        t.ClearShifted("Button2");
        Assert.Empty(t.Of("hardpoint1_fire"));
        Assert.Single(t.Of("special1").Single().Terms);
        // the shift given a command of its own stops being the shift, and the layer goes
        t.AssignShifted("hardpoint2_fire", "joystick1", "Button1");
        t.Assign("e_brake", "joystick1", "Button5");
        Assert.Null(t.Shift);
        Assert.Empty(t.Of("hardpoint2_fire"));
        Assert.Single(t.Of("weapon_fire").Single().Terms);
    }

    [Fact]
    public void RestoreBringsThePresetsShiftAndAStickPresetHasNone()
    {
        var t = BindingTable.Parse("weapon_fire {\n   + keyboard   Space\n}\n");
        t.ResetSide(PadPreset.Build((string?)null), true);
        Assert.Equal(new InputTerm(true, "joystick1", PadPreset.ShiftChannel), t.Shift);
        t.ResetSide(GenericPreset.Build(new BindingTable()), true);
        Assert.Null(t.Shift);
        var p = PadPreset.Build((string?)null);
        Assert.Equal(p.Shift, p.Clone().Shift);
    }
}
