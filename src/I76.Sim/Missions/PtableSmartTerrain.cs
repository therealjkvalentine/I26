using System.Numerics;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' terrain (group N): Hill pursuit (N1), the climb-aware probe (N2) and Ledge caution (N6).</summary>
public sealed partial class Ptable
{
    /// <summary>Hill pursuit (N1): close again only within 30 m of height too, so a car far below its target does not flick between too far and its tactic.</summary>
    internal bool HillHeld(Entity e, Entity t) =>
        Smart(e, SmartAi.HillPursuit) && !e.IsHeli && IsVehicle(t) && MathF.Abs(t.Position.Y - Pos(e).Y) > 30f;

    /// <summary>
    /// Climb-aware probe (N2): the grade the car can climb on the ground it is on, tan 28° to tan 35° by the surface's grip,
    /// for the probe's rise per step; the cross-track limit 24° of tilt, and the climbable grade where both tracks rise or fall together.
    /// The original's limits (1.5 m per 2.5 m step either way) read a side slope of 17° as a wall.
    /// </summary>
    internal (float Rise, float Cross, float Together) ProbeLimits(Entity e)
    {
        if (!Smart(e, SmartAi.ClimbProbe)) return (1.5f, 1.5f, 1.5f);
        float mu = World.Ground.Surface(e.Vehicle!.Surface).Grip;
        float grade = System.Math.Clamp(0.65f * mu, 0.532f, 0.700f);
        return (2.5f * grade, 5f * 0.445f, 5f * grade);
    }

    /// <summary>
    /// Ledge caution (N6), on the ground: a cliff edge (a fall of more than 2.5 m from one 2.5 m step to the next, with the ground
    /// <see cref="CliffDrop"/> or more below the rim within 15 m past it) along the way the car is moving, within its braking distance and
    /// 10 m more, is braked for so that the car stops 3 m short of it, in any state, the escapes too (a car backing away or sliding off a
    /// rim fell 30 to 80 m in the play fork's probe on Jorczak's Peak, more of them with Hill pursuit). Outside the escapes, above 8 m/s
    /// in forward gear, it also goes no faster than keeps the car within <see cref="CrestHop"/> of the ground ahead launched along the
    /// slope it is on (v² ≤ g·d² / 2·drop at each 2.5 m out), so it slows for a crest it would fly off instead of flying it. Never faster
    /// than the state asked. True when the car, stopped at a rim the state asks it on over, should back away.
    /// </summary>
    internal bool LedgeCaution(Entity e, bool escape)
    {
        if (!Smart(e, SmartAi.LedgeCaution)) return false;
        var v = e.Vehicle!;
        if ((v.Flags & VehicleFlags.Airborne) != 0) return false;
        var g = World.Ground; var p = Pos(e);
        // the way the car is moving (sliding back in forward gear too), or the gear's way at a standstill
        var vel = new Vector3(v.Vel.X, 0, v.Vel.Z);
        float sp = vel.Length();
        var dir = sp > 1f ? vel / sp : Travel(e.Rot.Forward, v.Gear);
        const float decel = 6f, hop = CrestHop;
        float look = MathF.Min(60f, sp * sp / (2 * decel) + 10f), cap2 = float.MaxValue;
        float h0 = g.BareHeight(p.X, p.Z);
        if (CliffAt(p, dir, look) is { } edge) cap2 = 2 * decel * MathF.Max(0f, edge - 3f);
        bool forward = !escape && v.Gear > 0 && Vector3.Dot(dir, e.Rot.Forward) > 0;
        if (forward && sp >= 8f)
        {
            float s0 = (h0 - g.BareHeight(p.X - dir.X * 2.5f, p.Z - dir.Z * 2.5f)) / 2.5f;
            for (float d = 5f; d <= look; d += 2.5f)
            {
                float drop = h0 + s0 * d - g.BareHeight(p.X + dir.X * d, p.Z + dir.Z * d) - hop;
                if (drop > 0) cap2 = MathF.Min(cap2, MathF.Max(64f, 9.8f * d * d / (2 * drop)));
            }
        }
        if (cap2 < 1f)
        {
            // at the rim: stop; asked on over it (a stopped car cannot turn away) outside the escapes, it backs away instead
            bool onward = v.Throttle > 0;
            if (sp > 0.5f || onward) Brake(e);
            return onward && !escape && sp < 2f && Vector3.Dot(dir, Travel(e.Rot.Forward, v.Gear)) > 0;
        }
        if (sp * sp <= cap2) return false;
        float want = MathF.Sqrt(cap2);
        // the throttle for the lower speed, unless the state already asks for less
        float asked = v.Throttle;
        Throttle(e, want);
        if (v.Throttle > asked) v.Throttle = asked;
        return false;
    }

    /// <summary>
    /// How deep the ground past a rim must fall for Ledge caution to brake for it, and how far the crest test lets a car fly above
    /// the ground. The first rule (any fall of 2.5 m in a step, a metre's hop) braked for every steep bank, ditch and crest: a car
    /// chasing one that drives on fell behind, and backed away from banks and drove at them again (T14, the cops and SWAT vans after
    /// the escorted van, 30 seeds at 24 Hz with solid cliffs: mean gap 377 m against the original's 336, within 60 m of the target
    /// 12.4 % of the time against 15.5; with these 331 m and 15.1 %). Jorczak's Peak (24 seeds, 7 cars climbing to the player on the
    /// summit): cars killed 110 with the original's AI, 43 with the terrain group by the first rule, 51 with these.
    /// </summary>
    public const float CliffDrop = 10f, CrestHop = 6f;

    /// <summary>
    /// How far along <paramref name="dir"/> from <paramref name="p"/>, within <paramref name="reach"/>, the ground first falls more than
    /// 2.5 m in one 2.5 m step with the ground <see cref="CliffDrop"/> or more below that rim within 15 m past it (a cliff's rim); null for none.
    /// </summary>
    private float? CliffAt(Vector3 p, Vector3 dir, float reach)
    {
        var g = World.Ground;
        float prev = g.BareHeight(p.X, p.Z);
        for (float d = 2.5f; d <= reach; d += 2.5f)
        {
            float h = g.BareHeight(p.X + dir.X * d, p.Z + dir.Z * d);
            if (prev - h > 2.5f)
            {
                float low = h;
                for (float k = d + 2.5f; k <= d + 15f && prev - low < CliffDrop; k += 2.5f) low = MathF.Min(low, g.BareHeight(p.X + dir.X * k, p.Z + dir.Z * k));
                if (prev - low >= CliffDrop) return d - 2.5f;
            }
            prev = h;
        }
        return null;
    }

    /// <summary>Ledge caution (N6) in back_away: a cliff's rim within 6 m the way the gear goes sends it to just do it (the other way) at once.</summary>
    internal bool RimInGear(Entity e) =>
        Smart(e, SmartAi.LedgeCaution) && e.Vehicle is { } v && (v.Flags & VehicleFlags.Airborne) == 0 && CliffAt(Pos(e), Travel(e.Rot.Forward, v.Gear), 6f) is not null;
}
