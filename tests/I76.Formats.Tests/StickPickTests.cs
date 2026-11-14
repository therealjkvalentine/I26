using I76.Formats.Input;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// A player report: binding a pad's stick up / down to accelerate and brake on the controller screen "always goes to left and right,
/// on both sticks". In the pad's preset the right stick's two axes are the camera, so they share one row, its X's (Rudder), and the
/// left stick's up / down has nothing (the triggers accelerate and brake), so no row: that stick shows its X's row alone. A click on a
/// stick's row could only ever give its X. Now pushing the stick picks the axis it is pushed along.
/// </summary>
public class StickPickTests
{
    private static PadRaw Left(float x, float y) => new(x, y, 0, 0, 0, 0, 0);
    private static PadRaw Right(float x, float y) => new(0, 0, x, y, 0, 0, 0);

    [Fact]
    public void AStickNearTheMiddlePicksNothing()
    {
        Assert.Null(StickPick.Pushed(default));
        Assert.Null(StickPick.Pushed(Right(0.3f, -0.35f)));
    }

    /// <summary>The frames of a push up that is not quite straight (the stick's travel as a pad reports it): up / down from the first frame past half.</summary>
    [Fact]
    public void ASlightlyCrookedPushUpIsTheUpDownAxis()
    {
        (float X, float Y)[] push = [(0.04f, -0.12f), (0.12f, -0.33f), (0.22f, -0.58f), (0.31f, -0.84f), (0.34f, -0.94f)];
        var picked = push.Select(p => StickPick.Pushed(Right(p.X, p.Y))).ToList();
        Assert.Equal([null, null, "RSY", "RSY", "RSY"], picked);
        Assert.Equal("LSY", StickPick.Pushed(Left(-0.3f, 0.9f)));   // and down, on the left stick
    }

    [Fact]
    public void APushAcrossIsTheLeftRightAxisAndTheLargerWins()
    {
        Assert.Equal("RSX", StickPick.Pushed(Right(0.9f, -0.3f)));
        Assert.Equal("LSX", StickPick.Pushed(Left(-0.7f, 0.6f)));
        Assert.Equal("LSY", StickPick.Pushed(Left(0.6f, 0.6f)));     // a tie: up / down
    }

    /// <summary>The report's binding, end to end on the pad preset: right stick up / down accelerates, left stick across steers.</summary>
    [Fact]
    public void TheRightStickUpDownCanBeTheThrottle()
    {
        var t = PadPreset.Build(new BindingTable());
        Assert.Equal("Rudder", t.AxisOf("track_yaw_delta")!.Channel);       // the right stick is the camera: one row, its X's
        Assert.Equal("5thAxis", t.AxisOf("track_pitch_delta")!.Channel);
        Assert.Equal("Throttle", t.AxisOf("throttle")!.Channel);                 // the triggers: the left stick's Y has no row
        // the right stick pushed up while its row is clicked: the controller screen opens the axis StickPick names
        string channel = StickPick.Pushed(Right(0.2f, -0.8f)) switch { "RSY" => "5thAxis", "RSX" => "Rudder", var k => k! };
        t.AssignAxis("throttle", "joystick1", channel);
        Assert.Equal("5thAxis", t.AxisOf("throttle")!.Channel);
        Assert.Equal("Left/Right", t.AxisOf("steer")!.Channel);
        Assert.Equal("Rudder", t.AxisOf("track_yaw_delta")!.Channel);        // the camera keeps the stick's X
    }
}
