using System.Numerics;
using I76.Formats.Bwd2;
using I76.Sim.Math;

namespace I76.Sim.Weapons;

/// <summary>How a weapon's reach is decided (<see cref="Combat.RangeOf"/>).</summary>
public enum RangeKind
{
    /// <summary>Not known: no ordnance, or a kind the step does not fly.</summary>
    None,
    /// <summary>A round that flies until its life runs out (guns, rockets, missiles): <see cref="WeaponReach.Far"/> is that flight.</summary>
    Flies,
    /// <summary>A lobbed round (gravity in its ordnance): where it comes down on level ground.</summary>
    Lands,
    /// <summary>A lobbed round from a turret, which pitches along its line to the target: where it lands depends on the target.</summary>
    Aimed,
    /// <summary>A flame stream: the length of its chain.</summary>
    Stream,
    /// <summary>A dropper: the round is laid behind the car.</summary>
    Dropped,
}

/// <summary>
/// A weapon's reach on level ground from a car standing still (<see cref="Combat.RangeOf"/>), in metres.
/// <paramref name="Near"/> is the shortest landing of a mortar that sets its own elevation (the EZK), a flame stream's on the roof mount
/// that tips it down, else <paramref name="Far"/>;
/// <paramref name="Lock"/> is how far a guided missile picks its target (0 for any other weapon).
/// </summary>
public readonly record struct WeaponReach(RangeKind Kind, float Near, float Far, float Lock = 0);

public sealed partial class Combat
{
    /// <summary>The muzzle's height over the ground <see cref="RangeOf"/> flies from: a roof or bonnet mount's on most cars (1.4 to 1.9 m).</summary>
    public const float RangeMuzzleHeight = 1.5f;

    /// <summary>A heat seeker's search (<see cref="HeatTarget"/>): 650 m.</summary>
    public const float HeatRange = 650f;

    /// <summary>
    /// A flame stream's reach to a car's near face: its chain at its longest (<see cref="FlameShot"/>) is 19 cross-sections a metre apart
    /// along the muzzle's forward (<see cref="BuildFlame"/>), its far end 18 m out, and a point burns a car whose sphere it is within a
    /// metre of and whose box its segment enters (<see cref="FlameTouch"/>), so the car's face can stand about 2 m past the end (2.5 m for
    /// a Piranha, measured: WeaponRangeTests).
    /// </summary>
    public const float FlameReach = 18f + 2f;

    /// <summary>
    /// The weapon's reach as the simulation flies it, on level ground from a car standing still, the muzzle
    /// <see cref="RangeMuzzleHeight"/> up at its mount's rest elevation (the gizmo's GPOF for a roof or bonnet mount, P; a turret's T):
    /// a round that flies (guns, the FireRite, missiles) goes on until its life runs out; a lobbed one (the mortars, the Cluster-Bomb, the
    /// howitzer, the Nitro Pack's Chemical Mortar) lands where its step brings it down to the ground; the EZK sets its own elevation per
    /// shot (<see cref="EzkElevation"/>), from level to 45 degrees, so it lands anywhere between those two flights. Each is the same step
    /// as the round's own (<see cref="Step"/>: the approach to the ordnance's terminal speed, gravity, the pose turning along the path for
    /// all but the bullet step, a radar missile held over the ground) at the tick <paramref name="dt"/> (the 20 Hz default): a lobbed
    /// round's curve depends on the step. A flame stream reaches <see cref="FlameReach"/>, or less where its mount tips it down (the roof's
    /// five degrees, <see cref="WeaponReach.Near"/>): it burns only what its line meets above the ground. A dropper lays its round behind
    /// the car. A turret's mortar (the howitzer) pitches along its line of sight to the target (<see cref="AimTurrets"/>), so it has no
    /// one landing (<see cref="RangeKind.Aimed"/>).
    /// </summary>
    public static WeaponReach RangeOf(GizmoDef def, float dt = 102f / 2048f)
    {
        if (def.Class == 6 || def.Kind is 0xc or 0xe or 0xf or 0x10 or 0x11 or 0x13) return new(RangeKind.Dropped, 0, 0);
        if (def.Kind is 9 or 10 or 11)
        {
            // the roof mount (P) tips the stream five degrees down, so from a roof's height it meets the ground first; the side (S),
            // bonnet (I, five up) and turret mounts carry it level or above
            float down = -RestElevation(def);
            float roof = down > 0.001f ? MathF.Min(FlameReach, RangeMuzzleHeight / MathF.Tan(down)) : FlameReach;
            return new(RangeKind.Stream, roof, FlameReach);
        }
        if (def.Ordnance is not { } ord || def.Kind is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 0x12 or 0x14 or 0x15 or 0x16)) return new(RangeKind.None, 0, 0);
        if (ord.Gravity != 0 && def.MountsAsTurret) return new(RangeKind.Aimed, 0, 0);
        float rest = RestElevation(def);
        var kind = ord.Gravity != 0 ? RangeKind.Lands : RangeKind.Flies;
        float lockOn = def.Kind switch
        {
            // the heat seeker finds its own (HeatTarget); the radar missile takes the radar's target, a contact within 600 m on the
            // ground (Shot, DropLostTargets); the 4Get-U-Not its arc's (ArcPick: the nearest car within 650 m and 15 degrees)
            3 => HeatRange,
            8 => ContactRange,
            0x14 => ArcRange,
            _ => 0,
        };
        if (def.Class == 3 && def.Tier == 3 && def.Speed != 0)
            return new(kind, FlyOnFlat(def, rest, dt), FlyOnFlat(def, rest + MathF.PI / 4, dt), lockOn);
        float far = FlyOnFlat(def, rest, dt);
        return new(kind, far, far, lockOn);
    }

    /// <summary>The muzzle's elevation at rest on the mount <see cref="RangeOf"/> assumes (radians, up positive).</summary>
    public static float RestElevation(GizmoDef def)
    {
        // the driver's .45 is built in code with no GPOF (its muzzle is the cockpit's GRPF / GLPF part, level on every player car)
        int slot = def.MountsAsTurret ? 2 : 0;
        if (def.GunPoints.Length <= slot || def.GunPoints[slot] is not { } gp) return 0;
        var f = Rows.FromEngine(gp.Rotation).Forward;
        return MathF.Asin(System.Math.Clamp(f.Y / MathF.Max(1e-6f, f.Length()), -1f, 1f));
    }

    /// <summary>
    /// One round of <paramref name="def"/> from <see cref="RangeMuzzleHeight"/> over level ground at <paramref name="elevation"/>, stepped
    /// as <see cref="Step"/> steps it: the distance along the ground to where it comes down, or where its life runs out.
    /// </summary>
    internal static float FlyOnFlat(GizmoDef def, float elevation, float dt)
    {
        var ord = def.Ordnance!;
        uint k = def.Kind;
        var pos = new Vector3(0, RangeMuzzleHeight, 0);
        var rot = Rows.AboutX(-elevation);
        var local = new Vector3(0, 0, k is 0xf or 0x10 or 0x13 ? 5f : def.Speed);
        var acc = new Vector3(0, -9.8f * ord.Gravity, 0);
        float life = def.Life == 0 ? 5f : def.Life;
        float kf = k == 2 ? dt * 0.5f : dt;
        bool bullet = k is 1 or 6 or 0x12 or 0x15;
        for (int n = 0; n < 1_000_000; n++)
        {
            // FlyRounds: the life first, then the step
            life -= dt;
            if (life < 0) break;
            var prev = pos;
            float vz = local.Z, vT = ord.TerminalSpeed;
            if (MathF.Abs(vT - vz) >= 0.005f && kf >= 0 && kf <= 0.95f) local.Z = vz + (vT - vz) * kf;
            if (k == 3) pos += rot.ToWorld(local) * dt;
            else
            {
                var vw = rot.ToWorld(local);
                var disp = (vw + acc * (dt / 2)) * dt;
                pos += disp;
                vw += acc * dt;
                local = rot.ToLocal(vw);
                if (!bullet) rot = LookAlong(disp, rot);
                if (k is 8 or 0x14 && pos.Y < 0.5f) pos.Y = 0.5f;
            }
            if (pos.Y <= 0)
            {
                float t = prev.Y / MathF.Max(1e-6f, prev.Y - pos.Y);
                var at = prev + (pos - prev) * t;
                return new Vector2(at.X, at.Z).Length();
            }
        }
        return new Vector2(pos.X, pos.Z).Length();
    }
}
