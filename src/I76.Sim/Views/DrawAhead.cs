using System.Numerics;
using I76.Sim.Math;

namespace I76.Sim.Views;

/// <summary>
/// Draw Ahead ([port] draw_ahead): the player's car drawn at the sim's newest pose carried forward by the time since that tick, at
/// the motion of the last tick (its displacement and its turn, from the tick before to the newest), instead of between the last two
/// ticks. Interpolation draws the car one tick in the past (41.5 ms at 24 Hz); this draws it at the present, at the cost of a small
/// correction at each tick where the motion changed (the change of velocity times the tick). Drawing only: the sim, its ticks and
/// its inputs are untouched. Game axes; the rows are the body's right, up and forward in the world.
/// </summary>
public static class DrawAhead
{
    /// <summary>A tick's displacement past this is a jump (a respawn, a teleport), not motion: nothing is carried forward.</summary>
    public const double JumpMetres = 20;

    /// <summary>
    /// The pose <paramref name="ahead"/> seconds after the newest tick (negative while an early tick is ahead of the clock), the motion
    /// from (<paramref name="prevRot"/>, <paramref name="prevPos"/>) a tick before to (<paramref name="rot"/>, <paramref name="pos"/>)
    /// carried on: position by the displacement, attitude by the turn, both scaled by ahead / tick (clamped to −1..1).
    /// </summary>
    public static (Rows Rot, Double3 Pos) Pose(Rows prevRot, Double3 prevPos, Rows rot, Double3 pos, double tick, double ahead)
    {
        if (tick <= 0) return (rot, pos);
        double s = System.Math.Clamp(ahead / tick, -1, 1);
        double dx = pos.X - prevPos.X, dy = pos.Y - prevPos.Y, dz = pos.Z - prevPos.Z;
        if (s == 0 || dx * dx + dy * dy + dz * dz > JumpMetres * JumpMetres) return (rot, pos);
        var p = new Double3(pos.X + dx * s, pos.Y + dy * s, pos.Z + dz * s);
        // the turn of the last tick, dq = q · q0⁻¹ (q0 then dq gives q), as an angle about an axis, scaled
        Quaternion q0 = Of(prevRot), q = Of(rot);
        var dq = Quaternion.Normalize(q * Quaternion.Inverse(q0));
        if (dq.W < 0) dq = -dq;   // the short way round
        float sinHalf = new Vector3(dq.X, dq.Y, dq.Z).Length();
        if (sinHalf < 1e-7f) return (rot, p);
        float angle = 2 * MathF.Atan2(sinHalf, dq.W);
        var turn = Quaternion.CreateFromAxisAngle(new Vector3(dq.X, dq.Y, dq.Z) / sinHalf, angle * (float)s);
        var r = Quaternion.Normalize(turn * q);
        return (ToRows(r), p);
    }

    /// <summary>The rotation taking the world axes to the rows (x to right, y to up, z to forward).</summary>
    public static Quaternion Of(Rows r) => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
        r.Right.X, r.Right.Y, r.Right.Z, 0, r.Up.X, r.Up.Y, r.Up.Z, 0, r.Forward.X, r.Forward.Y, r.Forward.Z, 0, 0, 0, 0, 1)));

    public static Rows ToRows(Quaternion q) => new(Vector3.Transform(Vector3.UnitX, q), Vector3.Transform(Vector3.UnitY, q), Vector3.Transform(Vector3.UnitZ, q));
}
