using I76.Formats.Remix;
using I76.Sim.Damage;
using I76.Sim.Vehicles;

namespace I76.Sim.Trip;

/// <summary>
/// A road trip's car between jobs (docs/remix-missions.md "Road trip"): its damage read off the player's car at a job's end and put
/// back at the next job's load, before the first tick (as the trip's carried state is, <see cref="CarState"/>), in the car's own
/// units after the player's rescale. Armour and chassis carry too (the trip's do not: the original's rule; the run is the port's).
/// -1 leaves a part as built. Nothing here runs during a mission: the state it sets is the vehicle's own, saved as it is.
/// </summary>
public static class RunCarry
{
    public static RunCar Capture(Vehicle v)
    {
        var c = new RunCar();
        if (v.Health is not { } d) return c;
        c.Armour = [.. d.Armour];
        c.Chassis = [.. d.Chassis];
        c.Engine = d.Engine?.Hp ?? -1;
        c.Brake = d.Brake?.Hp ?? -1;
        c.Suspension = d.Suspension?.Hp ?? -1;
        for (int k = 0; k < 6; k++) c.Wheels[k] = d.Slots[k] is WheelPart w ? w.Hp : -1;
        c.Weapons = [.. (v.Armory?.LoadOrder ?? []).Select(w => new[] { (int)w.Hardpoint.Id, w.Hp, w.Ammo })];
        c.ArmourLeft = Percent(d.Armour.Sum(), d.ArmourMax.Sum());
        c.ChassisLeft = Percent(d.Chassis.Sum(), d.ChassisMax.Sum());
        int full = (v.Armory?.LoadOrder ?? []).Sum(w => System.Math.Max(0, (int)w.Def.Ammo));
        c.AmmoLeft = full <= 0 ? 100 : Percent((v.Armory?.LoadOrder ?? []).Sum(w => System.Math.Max(0, w.Ammo)), full);
        return c;
    }

    public static void Apply(RunCar c, Vehicle v)
    {
        if (v.Health is not { } d) return;
        for (int z = 0; z < 4; z++)
        {
            // the mirrors fall with the pools, as a hit takes them (VehicleDamage.ArmourStep)
            if (z < c.Armour.Length && c.Armour[z] >= 0) { int now = System.Math.Min(c.Armour[z], d.ArmourMax[z]); d.ArmourMirror[z] -= d.Armour[z] - now; d.Armour[z] = now; }
            if (z < c.Chassis.Length && c.Chassis[z] >= 0) { int now = System.Math.Min(c.Chassis[z], d.ChassisMax[z]); d.ChassisMirror[z] -= d.Chassis[z] - now; d.Chassis[z] = now; }
        }
        static void Set(Part? p, int hp) { if (p is not null && hp >= 0) p.Hp = System.Math.Min(hp, p.Max); }
        Set(d.Engine, c.Engine);
        Set(d.Brake, c.Brake);
        Set(d.Suspension, c.Suspension);
        for (int k = 0; k < 6 && k < c.Wheels.Length; k++) if (d.Slots[k] is WheelPart w) Set(w, c.Wheels[k]);
        foreach (var g in c.Weapons)
            if (g is [var hp, var whp, var ammo] && v.Armory?.LoadOrder.FirstOrDefault(w => w.Hardpoint.Id == hp) is { } weapon)
            {
                if (whp >= 0) weapon.Hp = System.Math.Min(whp, weapon.MaxHp);
                if (ammo >= 0) weapon.Ammo = System.Math.Min(ammo, weapon.Ammo);
            }
    }

    private static int Percent(int have, int of) => of <= 0 ? 100 : System.Math.Clamp((int)System.Math.Round(100.0 * have / of), 0, 100);
}
