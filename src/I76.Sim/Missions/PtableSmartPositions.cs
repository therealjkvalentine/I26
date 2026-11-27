using System.Numerics;
using I76.Formats;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' coordination (group G): waiting cars take positions (G1), with P1's offset slot.</summary>
public sealed partial class Ptable
{
    /// <summary>The slots round the target, degrees off its nose, in the order the waiting cars take them: its rear quarters, its tail, its flanks.</summary>
    private static readonly float[] SlotAngles = [135f, -135f, 180f, 100f, -100f, 160f, -160f, 120f];
    public const float SlotRange = 70f;

    /// <summary>
    /// Waiting cars take positions (G1): a car on deck goes to its slot round the target instead of the original's flee-and-return
    /// between 50 and 160 m: the waiting cars on that target in the entities' order take <see cref="SlotAngles"/> at
    /// <see cref="SlotRange"/> m. On the way, a car within 150 m and 45° of the target's nose leaves its gun cone sideways first (P1's
    /// offset pursuit). At its slot it keeps station, or faces a target that has stopped. The slot cap and its release are the original's.
    /// </summary>
    private bool TakePosition(Entity e, Entity? t)
    {
        if (!SmartVs(e, SmartAi.Positions, t) || t?.Vehicle is not { } tv) return false;
        int index = 0;
        foreach (var o in host.Entities)
        {
            if (o == e) break;
            if (o.Vehicle is not null && !o.Dead && !o.Removed && o.LastTarget == t && TopName(o) == "tactic on deck") index++;
        }
        float ht = Hdg(t), a = ht + SlotAngles[index % SlotAngles.Length] * MathF.PI / 180f;
        var tp = t.Position;
        var slot = tp + new Vector3(DetMath.Sin(a), 0, DetMath.Cos(a)) * SlotRange + tv.Vel;
        var P = Pos(e);
        float off = Delta(ht, Brg(t, e));
        var aim = slot;
        if (MathF.Abs(off) < MathF.PI / 4 && Dist(e, t) < 150f)
        {
            float side = ht + (off >= 0 ? 1.75f : -1.75f);
            aim = tp + new Vector3(DetMath.Sin(side), 0, DetMath.Cos(side)) * 90f;
        }
        var to = aim - P;
        float ds = new Vector2(slot.X - P.X, slot.Z - P.Z).Length(), tSpd = tv.Speed;
        if (ds > 12f || tSpd > 3f)
        {
            Steer(e, Heading(to));
            if (MathF.Abs(e.Vehicle!.Steer) > 0.6f) Throttle(e, Cruise(e));
            else Throttle(e, MathF.Min(35f, tSpd + MathF.Max(0f, ds - 5f) * 0.3f));
        }
        else
        {
            float b = Brg(e, t);
            Steer(e, b);
            if (MathF.Abs(Delta(Hdg(e), b)) < 0.15f) Brake(e); else Throttle(e, 6f);
        }
        return true;
    }
}
