using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>Two devices with no standard layout at once (a wheel and a stick): their bindings side by side, each edited on its own (docs/joystick.md).</summary>
public class TwoDeviceTests
{
    private sealed class Sticks : IPadSource
    {
        public List<PadRaw> List = [PadRaw.OfGeneric(new float[GenericStick.MaxAxes], 0), PadRaw.OfGeneric(new float[GenericStick.MaxAxes], 0)];
        public IReadOnlyList<PadRaw> Read() => List;
    }

    private static PadRaw Stick(float x = 0, float y = 0, params int[] buttons)
    {
        var ax = new float[GenericStick.MaxAxes];
        ax[0] = x; ax[1] = y;
        ulong b = 0;
        foreach (var k in buttons) b |= 1UL << k;
        return PadRaw.OfGeneric(ax, b);
    }

    private static BindingTable Wheel() => GenericPreset.Build(new BindingTable());   // joystick1: steers, pedals, trigger fires

    [Fact]
    public void ASecondDevicesButtonFiresBesideTheFirstsAndItsAxisIsItsOwn()
    {
        var t = Wheel();
        t.Assign("weapon_fire", "joystick2", "Button1");
        t.Assign("hardpoint2_fire", "joystick2", "Button2");
        // both devices fire: the second's button did not take the first's away
        Assert.Equal(["joystick1", "joystick2"], t.Of("weapon_fire").Where(BindingTable.IsPad).Select(b => b.Terms[0].Device));
        var src = new Sticks();
        var bank = new JoystickBank(src);
        bool Held(string c) => t.Of(c).Any(b => InputChord.Holds(b.Terms, bank, _ => false));
        src.List[1] = Stick(buttons: 0);                                   // the stick's trigger
        bank.Poll(true);
        Assert.True(Held("weapon_fire"));
        Assert.False(Held("hardpoint2_fire"));
        src.List[1] = Stick(buttons: 1);
        bank.Poll(true);
        Assert.True(Held("hardpoint2_fire"));
        // the wheel steers, the stick pulled sideways does not
        var axes = new AxisControls(t.Blocks);
        src.List[0] = Stick(x: 0); src.List[1] = Stick(x: 1f);
        bank.Poll(true); axes.Update(bank, _ => false);
        Assert.Equal(0, axes.Steer);
        src.List[0] = Stick(x: -1f);
        bank.Poll(true); axes.Update(bank, _ => false);
        Assert.Equal(65536, axes.Steer);
    }

    [Fact]
    public void ClearingRestoringAndInvertingOneDeviceLeaveTheOther()
    {
        var t = Wheel();
        t.Assign("weapon_fire", "joystick2", "Button1");
        t.AssignAxis("track_yaw_delta", "joystick2", "Left/Right");
        t.ClearInputOn("joystick2", "Button1");
        Assert.Equal(["joystick1"], t.Of("weapon_fire").Where(BindingTable.IsPad).Select(b => b.Terms[0].Device));
        // axis 1 of the stick inverted: the wheel's axis 1 (the steer) keeps its sign
        Assert.Equal("track_yaw_delta", t.RowOfAxis(1, "joystick2"));
        Assert.Equal("steer", t.RowOfAxis(1, "joystick1"));
        var steer = t.AxisOf("steer");
        Assert.True(t.InvertAxis(1, "joystick2"));
        Assert.Equal(steer, t.AxisOf("steer"));
        Assert.True(t.AxisOf("track_yaw_delta")!.Plus);
        // Restore for the stick: nothing on it; for the wheel: its preset back, the stick's kept
        t.Assign("HONK_HORN", "joystick2", "Button3");
        t.Assign("HONK_HORN", "joystick1", "Button12");
        t.ResetDevice(Wheel(), "joystick1");
        Assert.Contains(t.Of("HONK_HORN"), b => b.Terms[0].Device == "joystick2");
        Assert.Equal("Button8", t.Of("HONK_HORN").Single(b => b.Terms[0].Device == "joystick1").Terms[0].Channel);
        t.ResetDevice(new BindingTable(), "joystick2");
        Assert.DoesNotContain(t.Blocks, b => BindingTable.IsPad(b) && b.Terms[0].Device == "joystick2");
        Assert.NotNull(t.AxisOf("steer"));
    }
}
