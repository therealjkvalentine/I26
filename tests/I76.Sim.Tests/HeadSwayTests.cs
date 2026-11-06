using System.Numerics;
using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The in-car head sway, against the heights the real game logged
/// (measured in m01 in the Piranha: track 1.476, wheelbase 2.658).
/// </summary>
public class HeadSwayTests
{
    private const float Track = 1.476f, Wheelbase = 2.658f;

    [Fact]
    public void Pulling_away_eases_the_heights_as_the_game_did()
    {
        // frames 320 -> 321: acc 9.015 forward; the game's step was f = 0.0804 (dt 0.0402)
        var s = new HeadSway { Left = -0.0559f, Right = -0.0559f, Back = 0.8950f };
        s.Step(9.015f, 0, 0.0402f);
        Assert.Equal(-0.0572f, s.Left, 0.0002f);
        Assert.Equal(-0.0572f, s.Right, 0.0002f);
        Assert.Equal(0.9158f, s.Back, 0.0002f);
    }

    [Fact]
    public void Turning_splits_left_and_right_and_a_small_step_is_taken_at_once()
    {
        // frames 640 -> 641: acc 1.596 forward, 9.029 right, dt 0.0357. The left height's target (a - b = 0.0595) is under
        // 0.005 away and is taken at once; the right one eases.
        var s = new HeadSway { Left = 0.0568f, Right = -0.0470f, Back = 0.1260f };
        s.Step(1.596f, 9.029f, 0.0357f);
        Assert.Equal(0.0595f, s.Left, 0.0002f);
        Assert.Equal(-0.0497f, s.Right, 0.0002f);
        Assert.Equal(0.1316f, s.Back, 0.0002f);
    }

    [Fact]
    public void Held_acceleration_settles_on_the_targets()
    {
        var s = new HeadSway();
        for (int i = 0; i < 400; i++) s.Step(-10.5f, 0, 1 / 30f);   // braking
        Assert.Equal(0.084f, s.Left, 3);
        Assert.Equal(0.084f, s.Right, 3);
        Assert.Equal(-1.344f, s.Back, 3);
    }

    [Fact]
    public void Level_with_no_acceleration_and_snaps_when_the_frame_is_long()
    {
        var s = new HeadSway();
        Assert.Equal(Vector3.UnitY, s.Up(Track, Wheelbase));
        s.Step(9, 0, 0.5f);   // f = 1 > 0.95: no easing
        Assert.Equal(-0.072f, s.Left, 4);
        Assert.Equal(1.152f, s.Back, 4);
    }

    [Fact]
    public void Accelerating_tips_the_eye_forward_and_braking_back()
    {
        var s = new HeadSway();
        s.Step(9, 0, 1);
        var up = s.Up(Track, Wheelbase);
        Assert.True(up.Z > 0.1f && MathF.Abs(up.X) < 1e-6f);   // 17·0.008·9/(4·2.658) = 0.115 before normalising
        s.Step(-10, 0, 1);
        Assert.True(s.Up(Track, Wheelbase).Z < -0.1f);
    }

    [Fact]
    public void A_right_hand_acceleration_leans_the_eye_right()
    {
        var s = new HeadSway();
        s.Step(0, 17, 1);
        var up = s.Up(Track, Wheelbase);
        Assert.Equal(0.004f * 17 / Track, up.X / up.Y, 3);
        Assert.Equal(0, up.Z, 5);
        var (r, u, f) = s.Basis(Track, Wheelbase);
        Assert.Equal(0, Vector3.Dot(r, u), 5);
        Assert.Equal(0, Vector3.Dot(f, u), 5);
        Assert.Equal(1, f.Z, 5);
    }

    [Fact]
    public void Without_the_snap_a_small_step_is_eased_too()
    {
        // the same frame as the test above: the left height's 0.0027 to go is eased, not taken at once
        var s = new HeadSway { SnapSmall = false, Left = 0.0568f, Right = -0.0470f, Back = 0.1260f };
        s.Step(1.596f, 9.029f, 0.0357f);
        Assert.Equal(0.0568f + 0.0714f * (0.0595f - 0.0568f), s.Left, 0.00005f);
        Assert.Equal(-0.0497f, s.Right, 0.0002f);
    }

    [Fact]
    public void Coasting_at_180_fps_moves_the_heights_by_far_less_than_the_snap_in_any_frame()
    {
        // 24 Hz ticks drawn at 180 frames a second; the forward acceleration wanders by up to 0.2 m/s² a tick, as T01's coast does,
        // so the back height's target moves by up to 0.026 a tick. With the snap a frame took up to 0.005 at once (about 0.47 mrad
        // of pitch, a pixel at 3440x1440); eased, no frame moves a height by more than f times what is left
        float frame = 1 / 180f;
        float MaxStep(bool snap)
        {
            var s = new HeadSway { SnapSmall = snap };
            var rng = new System.Random(1);
            float acc = -0.1f, worst = 0, back = 0;
            for (int i = 0; i < 180 * 4; i++)
            {
                if (i % 7 == 0 || i % 15 == 7) acc = -0.1f + (float)(rng.NextDouble() - 0.5) * 0.2f;   // a new tick: 7 or 8 frames
                s.Step(acc, -1.88f, frame);
                if (i > 180) worst = MathF.Max(worst, MathF.Abs(s.Back - back));
                back = s.Back;
            }
            return worst;
        }
        Assert.True(MaxStep(true) > 0.002f);
        Assert.True(MaxStep(false) < 0.0004f);
    }
}
