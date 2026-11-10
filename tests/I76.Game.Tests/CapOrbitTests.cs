using Godot;
using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>I76_CAP_ORBIT: the capture camera circling a car and looking at it (Main.OrbitPose).</summary>
public class CapOrbitTests
{
    [Fact]
    public void The_camera_starts_ahead_and_looks_back_at_the_car()
    {
        var (off, yaw, pitch) = Main.OrbitPose([6f, 1.6f, 36f], 0);
        Assert.Equal(0f, off.X, 4);
        Assert.Equal(1.6f, off.Y, 4);
        Assert.Equal(6f, off.Z, 4);
        Assert.Equal(0f, yaw, 3);                    // Godot's yaw 0 looks down -Z: from +Z toward the car
        Assert.Equal(-Mathf.RadToDeg(Mathf.Atan2(1.0f, 6f)), pitch, 3);   // down at the body, 0.6 m up
    }

    [Fact]
    public void A_quarter_turn_at_36_degrees_a_second_takes_two_and_a_half_seconds()
    {
        var (off, yaw, _) = Main.OrbitPose([6f, 1.6f, 36f], 2.5);
        Assert.Equal(6f, off.X, 3);
        Assert.Equal(0f, off.Z, 3);
        Assert.Equal(90f, yaw, 3);                   // from +X, looking down -X
    }

    [Fact]
    public void The_start_angle_offsets_the_orbit_and_the_radius_is_kept()
    {
        var (off, _, _) = Main.OrbitPose([5f, 1f, 36f, 180f], 0);
        Assert.Equal(-5f, off.Z, 3);
        for (double t = 0; t < 10; t += 0.7)
            Assert.Equal(5f, Main.OrbitPose([5f, 1f, 36f, 180f], t).Offset with { Y = 0 } is var o ? o.Length() : 0, 3);
    }

    [Fact]
    public void A_fifth_value_brings_the_camera_in_and_stops_at_half_a_metre()
    {
        float Radius(double t) => Main.OrbitPose([14f, 1.4f, 0f, 90f, -1.2f], t).Offset with { Y = 0 } is var o ? o.Length() : 0;
        Assert.Equal(14f, Radius(0), 3);
        Assert.Equal(2f, Radius(10), 3);
        Assert.Equal(0.5f, Radius(20), 3);
        Assert.Equal(14f, Main.OrbitPose([14f, 1.4f, 0f, 90f, -1.2f], 0).Offset.X, 3);
    }
}
