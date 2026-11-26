using System.Numerics;
using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' defence (group D): Drop when chased (D1) and Rear guns (D2).</summary>
public sealed partial class Ptable
{
    /// <summary>The tactics the defensive steering acts in: the forward-gear fights and flights, and on deck.</summary>
    private static readonly HashSet<string> DefendStates =
        ["dirt_ram", "dirt_gunner", "dirt_miner", "dirt_brave", "dirt_flee", "dirt_circle", "road_ram", "road_side", "rw_flee", "road_flee", "tactic on deck"];

    /// <summary>A fixed rear gun (the muzzle back along the car), loaded and whole.</summary>
    private static bool RearGun(Weapons.Weapon w) =>
        w.Ammo != 0 && w.Hp > 0 && w.Style != 3 && w.TurretPivot is null && w.BaseRot.Forward.Z < -0.96f && w.Def.Class is 1 or 2 or 4;

    /// <summary>
    /// Drop when chased (D1): every loaded dropper fires at a chaser (its nose within 0.5 rad of this car) in the rear cone
    /// (within 26°, cos 0.9), within 50 m and not falling back, at most once a second, the car in forward gear on the ground;
    /// then it holds its line for 2 s. The original drops only on a chaser predicted to pass within 2.5 m of the dropper's own
    /// spot within 7 s.
    /// </summary>
    internal void DropWhenChased(Entity e, Entity t)
    {
        if (!SmartVs(e, SmartAi.DropChased, t) || t.Vehicle is not { } tv || e.Vehicle is not { Armory: { } a } v) return;
        if (v.Gear < 1f || (v.Flags & VehicleFlags.Airborne) != 0 || Now < e.LastDropAt + 1f) return;
        var rel = t.Position - Pos(e);
        var r2 = new Vector2(rel.X, rel.Z);
        float d = r2.Length();
        if (d > 50f || d < 1f) return;
        var f = new Vector2(e.Rot.Forward.X, e.Rot.Forward.Z);
        if (f.LengthSquared() < 1e-6f || Vector2.Dot(r2 / d, -Vector2.Normalize(f)) < 0.9f) return;
        if (MathF.Abs(Delta(Hdg(t), Brg(t, e))) > 0.5f) return;
        var rv = tv.Vel - v.Vel;
        if (Vector2.Dot(r2 / d, new Vector2(rv.X, rv.Z)) > 0f) return;
        bool dropped = false;
        foreach (var w in a.Weapons)
            if (w.Def.Class == 6 && w.Hp > 0 && w.Ammo > 0) { w.Trigger = true; dropped = true; }
        if (!dropped) return;
        e.LastDropAt = Now;
        e.HoldLineUntil = Now + 2f;
        e.HoldLineHeading = Hdg(e);
    }

    /// <summary>
    /// Rear guns (D2) in the combat tactics (forward gear): with a loaded fixed rear gun and the target within 8..100 m in the rear
    /// cone (0.8 rad), the tail is steered onto the rear gun's lead point and the gap held at about 25 m, so the gun has a shot.
    /// It goes before the line Drop when chased (or Stuck recovery's forward leg) holds.
    /// </summary>
    internal bool RearLay(Entity e, Entity? t)
    {
        if (!SmartVs(e, SmartAi.RearGuns, t) || t?.Vehicle is not { } tv || e.Vehicle is not { Armory: { } a } v) return false;
        var w = a.Weapons.FirstOrDefault(RearGun);
        if (w is null) return false;
        float d = Dist(e, t);
        if (d < 8f || d > 100f) return false;
        var P = Pos(e);
        if (MathF.Abs(Delta(Hdg(e) + MathF.PI, Brg(e, t))) > 0.8f) return false;
        var muzzle = P + v.Rot.ToWorld(w.MuzzlePos);
        float time = InterceptTime(muzzle, w.Def.Ordnance?.TerminalSpeed ?? w.Def.Speed, AimPointOf(t), tv.Vel);
        Steer(e, Heading(P - Predict(t, time)));
        Throttle(e, MathF.Max(5f, tv.Speed + (25f - d) * 0.3f));
        return true;
    }

    /// <summary>Rear guns (D2) in the tactic draw: dirt_miner (which runs ahead of the target) weighs twice as much for a car with a loaded rear gun.</summary>
    internal void FavourRearGuns(Entity e, Entity? t, int[] options, int[] weights)
    {
        if (!SmartVs(e, SmartAi.RearGuns, t) || e.Vehicle?.Armory is not { } a || !a.Weapons.Any(RearGun)) return;
        for (int i = 0; i < options.Length && i < weights.Length; i++)
            if (NameOf(options[i]) == "dirt_miner") weights[i] *= 2;
    }
}
