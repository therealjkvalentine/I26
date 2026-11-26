using I76.Sim.Vehicles;

namespace I76.Sim.Missions;

/// <summary>The smarter opponents' lining up (group T): Gun lay (T1) and Settle (T5).</summary>
public sealed partial class Ptable
{
    /// <summary>The tactics Gun lay acts in: those that fight with the forward guns.</summary>
    private static readonly HashSet<string> LayStates = ["road_ram", "road_side", "dirt_ram", "dirt_brave", "dirt_gunner", "dirt_standoff"];

    /// <summary>A fixed (not turret) forward gun or unguided rocket: what the hull has to aim.</summary>
    private static bool LayGun(Weapons.Weapon w) =>
        w.Style != 3 && w.TurretPivot is null && w.BaseRot.Forward.Z > 0.96f
        && (w.Def.Class is 1 or 2 || (w.Def.Class == 4 && w.Def.Tier is not (1 or 2 or 3 or 4 or 0x65 or 0x66 or 0x67)));

    /// <summary>
    /// Gun lay (T1): within reach of the first loaded fixed forward gun (80 m at the worst tier to 120 m at the best) and with its
    /// lead point within the cone (0.8 to 1.3 rad off the nose), steer the nose onto the lead point: the intercept of the gun's round
    /// (its speed along the barrel) with the target's predicted course. A stopped car cannot turn (its steering writes 0 at a
    /// standstill), so it rolls at 8 m/s while the nose is more than 0.15 rad off; aligned, the tactic's speed stands (the
    /// prototype's first version, which always rolled, made a parked target harder to kill).
    /// </summary>
    internal void GunLay(Entity e, Entity? t)
    {
        if (!Smart(e, SmartAi.GunLay) || t?.Vehicle is not { } tv || e.Vehicle is not { } v || (v.Flags & VehicleFlags.Airborne) != 0) return;
        var w = ForwardWeapons(e).FirstOrDefault(x => x.Ammo != 0 && x.Hp > 0 && LayGun(x));
        if (w is null) { e.LayAlignedAt = -1; return; }
        float k = TierShare(e), d = Dist(e, t);
        if (d > 80f + 40f * k || d < 8f) { e.LayAlignedAt = -1; return; }
        var P = Pos(e);
        var muzzle = P + v.Rot.ToWorld(w.MuzzlePos);
        float speed = w.Def.Ordnance?.TerminalSpeed ?? w.Def.Speed;
        float time = InterceptTime(muzzle, speed, AimPointOf(t), tv.Vel);
        float h = Heading(Predict(t, time) - P);
        float err = MathF.Abs(Delta(Hdg(e), h));
        if (err > 0.8f + 0.5f * k) { e.LayAlignedAt = -1; return; }
        Steer(e, h);
        e.LayActive = true;
        // on the lead point: within 2.5 m of it at the range (or 0.03 rad)
        if (err < MathF.Max(0.03f, 2.5f / d)) { if (e.LayAlignedAt < 0) e.LayAlignedAt = Now; }
        else e.LayAlignedAt = -1;
        if (v.Speed < 6f && err > 0.15f) Throttle(e, 8f);
    }

    /// <summary>Settle (T5): the reaction time a laid gun waits once on the lead point, 0.4 s at the worst tier to 0.2 s at the best.</summary>
    internal static float SettleTime(Entity e) => 0.4f - 0.2f * TierShare(e);

    /// <summary>Settle (T5) holds the fixed forward guns while the lay has not yet held on the lead point its settle time.</summary>
    internal bool SettleHolds(Entity e) =>
        e.LayActive && Smart(e, SmartAi.Settle) && !(e.LayAlignedAt >= 0 && Now - e.LayAlignedAt >= SettleTime(e));
}
