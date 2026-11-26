using System.Numerics;
using I76.Sim.Ai;

namespace I76.Sim.Missions;

/// <summary>
/// The smarter opponents (the play fork's; <see cref="Missions.SmartAi"/>, docs/deviations.md "Smarter opponents"): behaviours the
/// original's AI does not have, each behind its own bit, all off by default. With no bit on nothing here runs, draws a random
/// number or writes a field, so the original's AI is unchanged. Each acts only while the car's order is an attack (its tactics, on
/// deck, too far and the escapes they push) and never on a human's car; each changes only what the AI asks of the car. The
/// behaviours are in PtableSmart*.cs, one file a group; here are the switch and the hooks the state machine calls.
/// </summary>
public sealed partial class Ptable
{
    // ---- the switch ----

    /// <summary>Any bit for this car: the session's rule or its own.</summary>
    private bool SmartOn(Entity e) => (host.SmartAi | e.SmartAi) != SmartAi.None;

    /// <summary>The bit is on for this car, and its order is an attack (a scripted goto, follow, race or evade keeps the original's driving).</summary>
    private bool Smart(Entity e, SmartAi bit) =>
        ((host.SmartAi | e.SmartAi) & bit) != 0 && e.Order is { } o && IsAttack((int)o) && e.Vehicle is { } v && !World.IsHuman(v);

    /// <summary>The tier's share between the worst (setSkill 5, drive skill 0.4: 0) and the best (1: 1).</summary>
    private static float TierShare(Entity e) => System.Math.Clamp((e.Ai.Skill - 0.4f) / 0.6f, 0f, 1f);

    /// <summary>The name of the state on top of a car's stack ("" for none).</summary>
    private string TopName(Entity e) => e.Depth >= 0 && e.Depth < e.Stack.Count ? NameOf(e.Stack[e.Depth]) : "";

    /// <summary>The escape states (the table's own way out of a block): a behaviour that steers in the tactics leaves them be.</summary>
    private static readonly HashSet<string> EscapeStates = ["tactic_skid", "stop_sliding_backward", "avoid clsn", "back_away", "just do it"];

    // ---- the hooks ----

    /// <summary>
    /// After a state's own tick, before its transitions (which the escapes win): the steering and speed behaviours. True when a
    /// behaviour wants the car backed away (<see cref="SmartAfter"/> pushes back_away once the transitions have had their say).
    /// </summary>
    private bool SmartSteer(Entity e, Entity? t, int s)
    {
        e.LayActive = false;
        if (e.Vehicle is not { } v || e.IsHeli) return false;
        string name = NameOf(s);
        bool escape = EscapeStates.Contains(name);
        if (LayStates.Contains(name)) GunLay(e, t);
        if (!e.LayActive && v.Gear > 0 && !escape)
        {
            bool rear = false;
            if (!rear && e.HoldLineUntil > Now && Smart(e, SmartAi.Unstick)) Steer(e, e.HoldLineHeading);
        }
        return LedgeCaution(e, escape);
    }

    /// <summary>After the transitions: an escape taken ends the lay; a car held at a cliff's rim backs away; the stuck test.</summary>
    private void SmartAfter(Entity e, Entity? t, int s, bool back)
    {
        bool same = e.Depth >= 0 && e.Depth < e.Stack.Count && e.Stack[e.Depth] == s;
        if (!same) e.LayActive = false;
        if (back && same && t is not null) { PushBackAway(e, t); return; }
        StuckTest(e, t);
    }

    /// <summary>
    /// Back_away pushed onto the stack (entered and ticked as a transition pushes it), unless the stack is full; with
    /// <paramref name="unstick"/>, as Stuck recovery's reverse arc (<see cref="UnstickEnter"/>).
    /// </summary>
    private void PushBackAway(Entity e, Entity t, bool unstick = false)
    {
        int back = host.Tactics is { } table ? Array.FindIndex(table, d => d.Name == "back_away") : -1;
        if (back < 0 || e.Depth >= MaxDepth) return;
        e.ProgressAt = Pos(e); e.ProgressTime = Now;
        e.Depth++;
        e.Stack.Add(back);
        S[back].Enter?.Invoke(e, t);
        if (unstick) UnstickEnter(e, t);
        S[back].Tick(e, t);
    }

    /// <summary>The tail of the AI order with the smarter opponents: Settle's hold on the laid guns, then Drop when chased.</summary>
    private void SmartFire(Entity e, Entity at)
    {
        if (SettleHolds(e)) host.FireAt(e, at, w => !LayGun(w));
        else host.FireAt(e, at);
    }

    /// <summary>The smarter opponents' costs on a search too far starts (its own one-point path), and a stuck car's cell.</summary>
    private void SearchCosts(Entity e, PathSearch search)
    {
        if (!SmartOn(e) || !ReferenceEquals(e.NavPath, e.SnapshotPath)) return;
        search.SlopeCost = Smart(e, SmartAi.HillPursuit);
        search.FaceMargin = Smart(e, SmartAi.LedgeCaution);
        if (Smart(e, SmartAi.Unstick) && e.BlockedUntil > Now) search.Blocked = (e.BlockedX, e.BlockedZ);
    }

    /// <summary>
    /// The time a round at <paramref name="speed"/> from <paramref name="from"/> meets a target at <paramref name="at"/> moving at
    /// <paramref name="vel"/> (the smaller positive root of |at + vel·t − from| = speed·t), held to 0.01..5 s.
    /// </summary>
    private static float InterceptTime(Vector3 from, float speed, Vector3 at, Vector3 vel)
    {
        var d = at - from;
        float a = vel.LengthSquared() - speed * speed, b = 2 * Vector3.Dot(d, vel), c = d.LengthSquared();
        float t;
        if (MathF.Abs(a) < 1e-3f) t = b < 0 ? -c / b : MathF.Sqrt(c) / MathF.Max(speed, 1f);
        else
        {
            float disc = b * b - 4 * a * c;
            if (disc < 0) t = MathF.Sqrt(c) / MathF.Max(speed, 1f);
            else
            {
                float q = MathF.Sqrt(disc), t1 = (-b - q) / (2 * a), t2 = (-b + q) / (2 * a);
                t = t1 > 0 && t2 > 0 ? MathF.Min(t1, t2) : MathF.Max(t1, t2);
            }
        }
        return System.Math.Clamp(t, 0.01f, 5f);
    }
}
