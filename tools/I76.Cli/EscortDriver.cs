using I76.Sim.Session;

namespace I76.Cli;

/// <summary>
/// The headless escort (I76_RUN_ESCORT): the player's car drives the leader's trail (a crumb every 4 m, the passed ones
/// dropped), full on far behind and easing off to the gap, braking when it comes up too fast; no 3 m of progress in 2.5 s
/// with the throttle on, it backs off 2 s with the wheel the other way. Shared with I76.Sim.Tests (linked), which drives the same
/// escort to measure the smarter opponents' chase (SmartAiChaseTests).
/// </summary>
public sealed class EscortDriver(float gap)
{
    private readonly List<System.Numerics.Vector3> _trail = [];
    private float _slowSince = -1, _backUntil = -1;
    private System.Numerics.Vector3 _progressAt;
    private readonly AutoReverse _reverse = new();

    private int _leg;
    /// <summary>The route's last point reached (an escort, if any, takes over).</summary>
    public bool RouteDone;

    public CarInput Next(CarInput input, I76.Sim.Vehicles.Vehicle me, I76.Sim.Vehicles.Vehicle lead, IReadOnlyList<System.Numerics.Vector3>? path, float now)
    {
        var pos = me.Pos.ToVector3(); var lp = lead.Pos.ToVector3();
        if (gap < 0 && path is { Count: > 0 } && lead.Speed > 1f) return Lead(input, me, lead, path, now);
        if (_trail.Count > 0 && System.Numerics.Vector3.DistanceSquared(_trail[^1], lp) > 60 * 60) _trail.Clear();
        if (_trail.Count == 0 || System.Numerics.Vector3.DistanceSquared(_trail[^1], lp) > 16) _trail.Add(lp);
        if (_trail.Count > 2000) _trail.RemoveAt(0);
        while (_trail.Count > 1 && (System.Numerics.Vector3.DistanceSquared(_trail[0], pos) < 100 || System.Numerics.Vector3.DistanceSquared(_trail[1], pos) < System.Numerics.Vector3.DistanceSquared(_trail[0], pos)))
            _trail.RemoveAt(0);
        var local = me.Rot.ToLocal(_trail[0] - pos);
        float angle = MathF.Atan2(local.X, local.Z);
        float dist = System.Numerics.Vector3.Distance(pos, lp) - MathF.Abs(gap);
        float closing = System.Numerics.Vector3.Dot(me.Vel - lead.Vel, System.Numerics.Vector3.Normalize(lp - pos));
        float thr = System.Math.Clamp(dist / 25f - closing / 15f, -0.8f, 1f);
        if (MathF.Abs(angle) > 1.2f && me.Speed > 12) thr = MathF.Min(thr, 0.2f);
        float steer = System.Math.Clamp(-angle / (0.6f + me.Speed * 0.02f), -1f, 1f);
        return Drive(input, me, steer, thr, now);
    }

    /// <summary>Leading: the leader's path from the point after the one nearest the car, -gap ahead of the leader along its heading.</summary>
    private CarInput Lead(CarInput input, I76.Sim.Vehicles.Vehicle me, I76.Sim.Vehicles.Vehicle lead, IReadOnlyList<System.Numerics.Vector3> path, float now)
    {
        var pos = me.Pos.ToVector3(); var lp = lead.Pos.ToVector3();
        static float Xz(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
        _leg = System.Math.Clamp(_leg, 0, path.Count - 1);
        while (_leg < path.Count - 1 && Xz(path[_leg], pos) < 25f) _leg++;
        var local = me.Rot.ToLocal(path[_leg] - pos);
        float angle = MathF.Atan2(local.X, local.Z);
        var lf = lead.Vel.LengthSquared() > 1 ? System.Numerics.Vector3.Normalize(lead.Vel) : lead.Rot.Forward;
        float ahead = System.Numerics.Vector3.Dot(pos - lp, lf);
        float rel = System.Numerics.Vector3.Dot(me.Vel - lead.Vel, lf);
        float thr = System.Math.Clamp((-gap - ahead) / 25f - rel / 15f, -0.8f, 1f);
        if (MathF.Abs(angle) > 1.2f && me.Speed > 12) thr = MathF.Min(thr, 0.2f);
        float steer = System.Math.Clamp(-angle / (0.6f + me.Speed * 0.02f), -1f, 1f);
        return Drive(input, me, steer, thr, now);
    }

    /// <summary>A route of points driven in turn at up to <paramref name="speed"/>, slowing for sharp turns; braked to a stop at its end.</summary>
    public CarInput Route(CarInput input, I76.Sim.Vehicles.Vehicle me, List<System.Numerics.Vector3> route, float speed, float now)
    {
        var pos = me.Pos.ToVector3();
        static float Xz(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
        while (_leg < route.Count && Xz(route[_leg], pos) < 20f) _leg++;
        if (_leg >= route.Count) { RouteDone = true; _leg = 0; input.Buttons |= CarButtons.Analog; input.Throttle = (short)(me.Speed > 1 ? -26000 : 0); return input; }
        var local = me.Rot.ToLocal(route[_leg] - pos);
        float angle = MathF.Atan2(local.X, local.Z);
        float want = MathF.Abs(angle) > 0.5f ? MathF.Min(speed, 18f) : speed;
        float thr = System.Math.Clamp((want - me.Speed) * 0.3f, -1f, 1f);
        float steer = System.Math.Clamp(-angle / (0.6f + me.Speed * 0.02f), -1f, 1f);
        return Drive(input, me, steer, thr, now);
    }

    private CarInput Drive(CarInput input, I76.Sim.Vehicles.Vehicle me, float steer, float thr, float now)
    {
        // no 3 m of progress in 2.5 s with the throttle on: wedged
        var at = me.Pos.ToVector3();
        if (thr <= 0.3f || now < _backUntil || System.Numerics.Vector3.DistanceSquared(at, _progressAt) > 9f) { _progressAt = at; _slowSince = now; }
        else if (now - _slowSince > 2.5f) { _backUntil = now + 2f; _slowSince = now; }
        if (now < _backUntil) { thr = -0.8f; steer = -steer; }
        input.Buttons |= CarButtons.Analog;
        input.Steer = (short)(steer * 32000);
        input.Throttle = (short)(thr * 32000);
        // the brake held on a stopped car backs up (the port's auto-reverse, as a player with it on)
        return _reverse.Apply(input, me.Gear < 0, System.Numerics.Vector3.Dot(me.Vel, me.Rot.Forward), me.Throttle);
    }
}
