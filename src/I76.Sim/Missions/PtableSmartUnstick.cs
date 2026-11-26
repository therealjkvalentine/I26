using System.Numerics;
using I76.Sim.Ai;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' Stuck recovery (N5): progress-based, a reverse arc toward the target, then forward.</summary>
public sealed partial class Ptable
{
    /// <summary>How far a car asking to go must get, and in how long, before Stuck recovery (N5) calls it stuck; how long its cell is avoided.</summary>
    public const float StuckDistance = 3f, StuckTime = 3f, BlockedFor = 30f;
    /// <summary>Within this of its target (ground metres) a slow car is fighting, not stuck: Stuck recovery leaves it alone.</summary>
    public const float CloseQuarters = 30f;

    /// <summary>
    /// Stuck recovery (N5), the test: a car in forward gear asking to go (throttle above 0.05), farther than
    /// <see cref="CloseQuarters"/> from its target, that has not got <see cref="StuckDistance"/> from where it last did in
    /// <see cref="StuckTime"/> s and is all but stopped, outside the escapes, backs away (back_away pushed, which then reverses on an
    /// arc: <see cref="UnstickTick"/>); the 10 m cell ahead is kept off by too far's searches for <see cref="BlockedFor"/> s, and a
    /// route being followed is planned again.
    /// </summary>
    internal void StuckTest(Entity e, Entity? t)
    {
        if (!Smart(e, SmartAi.Unstick) || e.Vehicle is not { } v || t is null) return;
        var p = Pos(e);
        // asking to go, and not at close quarters with the target (pushing it, ramming it, creeping in on it: the fight, not a wedge)
        bool asking = v.Gear > 0 && v.Throttle > 0.05f && (v.Flags & VehicleFlags.Airborne) == 0 && Dist(e, t) >= CloseQuarters;
        string top = TopName(e);
        if (!asking || EscapeStates.Contains(top) || new Vector2(p.X - e.ProgressAt.X, p.Z - e.ProgressAt.Z).LengthSquared() >= StuckDistance * StuckDistance
            || e.ProgressTime > Now)
        {
            e.ProgressAt = p; e.ProgressTime = Now;
            return;
        }
        if (Now - e.ProgressTime < StuckTime || v.Speed > 1.5f || e.Depth >= MaxDepth) return;
        var dir = Travel(e.Rot.Forward, 1f);
        e.BlockedX = PathSearch.Round(p.X + dir.X * 7f); e.BlockedZ = PathSearch.Round(p.Z + dir.Z * 7f); e.BlockedUntil = Now + BlockedFor;
        if (top == "handle too far follow") e.ReachedEnd = true;
        PushBackAway(e, t, unstick: true);
    }

    /// <summary>
    /// Stuck recovery's back_away (the one its test pushes; the table's own back_aways stay the original's): the reverse arc's
    /// goal, the target's bearing; for a car already pointing that way (within 0.6 rad), which would only try the same line again,
    /// 0.9 rad to the side of it whose ground 15 m out rises less.
    /// </summary>
    private void UnstickEnter(Entity e, Entity? t)
    {
        e.Unsticking = t is not null && Smart(e, SmartAi.Unstick);
        if (!e.Unsticking) return;
        float goal = Brg(e, t!), nose = Heading(e.Rot.Forward);
        if (MathF.Abs(Delta(nose, goal)) < 0.6f)
        {
            var p = Pos(e); var g = World.Ground;
            float h0 = g.BareHeight(p.X, p.Z);
            float Rise(float h) => MathF.Abs(g.BareHeight(p.X + MathF.Sin(h) * 15f, p.Z + MathF.Cos(h) * 15f) - h0);
            goal = Rise(goal - 0.9f) <= Rise(goal + 0.9f) ? goal - 0.9f : goal + 0.9f;
        }
        e.UnstickHeading = goal;
    }

    /// <summary>
    /// Back_away's tick with Stuck recovery: stop rolling the old way, then back on an arc that swings the nose toward
    /// <see cref="Entity.UnstickHeading"/> (the travel, the tail, steered at its opposite) at 6 m/s, and stop once the nose is
    /// within 0.5 rad of it, for the forward leg. False when back_away runs as the original's.
    /// </summary>
    private bool UnstickTick(Entity e)
    {
        if (!e.Unsticking) return false;
        var v = e.Vehicle!;
        var at = Pos(e);
        e.BackFarthest = MathF.Max(e.BackFarthest, new Vector2(at.X - e.BackStart.X, at.Z - e.BackStart.Z).Length());
        float vf = Vector3.Dot(v.Rot.Forward, v.Vel) * MathF.Sign(v.Gear);
        if (vf < -0.5f) { Brake(e); v.Steer = 0; return true; }
        if (MathF.Abs(Delta(Heading(e.Rot.Forward), e.UnstickHeading)) < 0.5f) { Brake(e); return true; }
        Throttle(e, 6f);
        if (v.Speed < 0.5f) FloorIt(e);
        Steer(e, v.Gear < 0 ? e.UnstickHeading + MathF.PI : e.UnstickHeading);
        return true;
    }

    /// <summary>Back_away's end with Stuck recovery: the nose within 0.5 rad of the goal and the car all but stopped.</summary>
    private static bool UnstickDone(Entity e) =>
        e.Unsticking && MathF.Abs(Delta(Heading(e.Rot.Forward), e.UnstickHeading)) < 0.5f && e.Vehicle!.Speed < 1.5f;

    /// <summary>Stuck recovery's forward leg once back_away ends: the goal held 2 s (the arc's cusp, then on), before the tactic steers again.</summary>
    internal static void UnstickForward(Entity e, float now)
    {
        if (!e.Unsticking) return;
        e.HoldLineUntil = now + 2f;
        e.HoldLineHeading = e.UnstickHeading;
    }
}
