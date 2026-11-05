using Godot;
using I76.Formats.Assembly;
using I76.Sim.Damage;
using I76.Sim.Sound;

namespace I76.Game.Sim;

/// <summary>
/// What damage looks and sounds like: body parts take their damaged
/// conditions zone by zone (front BDYF/HLGT, rear BDYB/TLGT, sides BDYM,
/// top BDYT; DamageTextures.Condition of the zone's level, a dead car the worst
/// there is) and the zone's face slots their damage textures (DamageTextures.Frame), destroyed
/// structures turn into their 'A' parts or vanish, flung wheels and chunks
/// tumble as debris (Godot physics, visual only), tyres pop, cars explode.
/// </summary>
public partial class DamageView : Node3D
{
    public required DamageModel Damage { get; init; }
    public required Func<System.Numerics.Vector3> Origin { get; init; }
    public required World.MissionBuilder Builder { get; init; }
    public required Dictionary<I76.Sim.Vehicles.Vehicle, CarNode> Cars { get; init; }
    public required Dictionary<I76.Sim.Collision.StaticObject, Placement> PlacementOf { get; init; }
    public Assets.AudioBank? Audio { get; init; }
    public I76.Sim.Terrain.IGround? Ground { get; init; }
    /// <summary>How far the frame is between sim ticks (SimDriver.Alpha), for the 1997 damage smoke; unset: at the last tick.</summary>
    public Func<float>? Alpha { get; init; }
    /// <summary>The modern look's effects (CombatView's), for the deaths' own blasts (DeathVariety).</summary>
    public Func<ModernFx?>? Fx { get; init; }
    private readonly List<(RigidBody3D Body, double Until)> _debris = [];
    /// <summary>Each dying car's plan (DeathVariety) and how many wheels it has thrown so far, until it is renewed.</summary>
    private readonly Dictionary<CarNode, (DeathVariety.Plan Plan, int Thrown)> _deaths = [];
    /// <summary>The deaths' blasts still to go off: when, on which car.</summary>
    private readonly List<(double At, CarNode Car, I76.Sim.Vehicles.Vehicle Vehicle, DeathVariety.Blast Blast)> _blasts = [];
    /// <summary>The modern look's fire on each burning wreck (WreckFire), until it has burnt out or the car is renewed.</summary>
    private readonly Dictionary<CarNode, WreckFire> _fires = [];

    /// <summary>The mission's light for the wrecks' smoke, as CombatView's: 1 by day, lower at night.</summary>
    private float SceneLight => Mathf.Clamp(Builder.Light.Intensity * 1.5f, 0.25f, 1f);

    /// <summary>A wreck starts burning (the modern look; hidden in 1997's): flames, smoke, the charred body's glow.</summary>
    private void Burn(CarNode car, DeathVariety.Plan? plan = null)
    {
        if (_fires.Remove(car, out var old)) { old.Detach(); old.QueueFree(); }
        if (!WreckFire.Enabled) return;
        bool staged = plan is not null && plan != DeathVariety.Plan.Plain;
        var f = new WreckFire
        {
            Car = car, SceneLight = SceneLight, Name = $"Fire_{car.Name}", At = plan?.FireAt ?? 0, Size = plan?.FireSize ?? 1, Delay = plan?.FireDelay ?? 0,
            Tank = staged && plan!.FireDelay <= 0 ? plan.TankAt : null, SmokeScale = staged ? plan!.Smoke : 1f, FlameScale = staged ? plan!.FlameLevel : 1f,
            Spread = staged ? plan!.Spread : 1f, FullFor = staged ? plan!.FlamesFull : WreckFire.FlamesFull,
        };
        AddChild(f);
        _fires[car] = f;
        GD.Print($"wreck fire: {car.Name} burning");
    }

    /// <summary>The panels' queue: when, which car, the first panel's number and how many, from where along it, burning or not.</summary>
    private readonly List<(double At, CarNode Car, I76.Sim.Vehicles.Vehicle Vehicle, int From, int Count, float PanelAt, bool Burning)> _panels = [];
    /// <summary>Sounds held for their stage (the tank's vexplode.wav).</summary>
    private readonly List<(double At, string Name, I76.Sim.Vehicles.Vehicle Vehicle, float MinDistance)> _sounds = [];

    /// <summary>
    /// A death's plan set going (DeathVariety): the body's sag and drawn throw, stage 1's blasts and its sound, the parts it
    /// throws, the tank's one blast and vexplode.wav after the plan's delay, the cook-off's pops.
    /// </summary>
    private void Stage(CarNode car, I76.Sim.Vehicles.Vehicle v, DeathVariety.Plan plan)
    {
        var sag = plan.Sag != 0 || plan.Pitch != 0 || plan.Roll != 0
            ? new Transform3D(Basis.FromEuler(new Vector3(plan.Pitch, 0, plan.Roll)), new Vector3(0, -plan.Sag, 0)) : Transform3D.Identity;
        car.WreckPose = sag;
        if (plan.Throw.Kind != DeathVariety.ThrowKind.None) StartThrow(car, v, plan);
        foreach (var b in plan.First ?? [])
            if (b.Delay <= 0) Go(car, v, b);
            else _blasts.Add((_t + b.Delay, car, v, b));
        if (plan.Cue is { } cue) Sound(cue, v.Pos.ToVector3());
        int first = plan.Panels - plan.TankPanels;
        if (first > 0) _panels.Add((_t, car, v, 0, first, plan.PanelAt, plan.PanelsBurn));
        if (plan.TankSize > 0)
        {
            // the tank: one blast at the rear, the death's vexplode.wav with it
            var tank = new DeathVariety.Blast(plan.TankAt, 0, plan.TankY, -0.6f, plan.TankSize, DeathVariety.BlastKind.Fire, plan.TankSmoke);
            if (plan.TankAt <= 0) Go(car, v, tank);
            else
            {
                _blasts.Add((_t + plan.TankAt, car, v, tank));
                _sounds.Add((_t + plan.TankAt, "vexplode.wav", v, Ds3d.LoudMinDistance));
            }
            if (plan.TankPanels > 0) _panels.Add((_t + plan.TankAt, car, v, first, plan.TankPanels, 0, true));
        }
        foreach (var b in plan.Blasts) _blasts.Add((_t + b.Delay, car, v, b));
        GD.Print($"death-stage: {car.Name} stage1 {plan.First?.Length ?? 0} tank {plan.TankSize:0.0} m at {plan.TankAt:0.00} s pops {plan.Blasts.Length} panels {plan.Panels} throw {plan.Throw.Kind} award {plan.Award}");
    }

    /// <summary>Panels off the body (the hood, a door, a boot lid): flat, charred, thrown up and out, spinning; burning ones trail the thrown wheel's fire.</summary>
    private void ThrowPanels(CarNode car, I76.Sim.Vehicles.Vehicle v, int from, int count, float panelAt, bool burning)
    {
        var half = car.BodyBounds.Size * 0.5f;
        var body = car.BodyGlobal;
        for (int i = from; i < from + count; i++)
        {
            float side = i % 2 == 0 ? 1 : -1;
            // body-local, Godot axes (forward is -z)
            var local = new Vector3(side * half.X * 0.3f * i, half.Y * 1.6f, -panelAt * half.Z);
            var vel = body.Basis * new Vector3(side * (1.5f + i * 1.5f), 9f + i * 1.5f, -panelAt * 3f) + Coords.ToGodot(v.Vel) * 0.5f;
            var rb = new RigidBody3D { Position = body * local, LinearVelocity = vel, AngularVelocity = new Vector3(6 * side, 2, 4) };
            rb.AddChild(new MeshInstance3D { Mesh = _panel, MaterialOverride = _chunkMat });
            rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = _panel.Size } });
            AddChild(rb);
            _debris.Add((rb, _t + 8));
            if (burning) WheelFire(rb);
            GD.Print($"wreck fire: {car.Name} threw panel {i}, {(burning ? "burning" : "cold")}");
        }
    }

    /// <summary>One of a death's blasts, where it is on the car as drawn now (a thrown body too).</summary>
    private void Go(CarNode car, I76.Sim.Vehicles.Vehicle v, DeathVariety.Blast b)
    {
        if (Fx?.Invoke() is not { HasExplosions: true } fx) return;
        var half = car.BodyBounds.Size * 0.5f;
        // game axes (x right, z forward) to the body's Godot frame (forward is -z)
        var at = car.BodyGlobal * new Vector3(b.X * half.X, b.Y * half.Y * 2, -b.Z * half.Z);
        switch (b.Kind)
        {
            case DeathVariety.BlastKind.Dust:
                // the landing: dust thrown out low all round
                fx.Dust(at, b.Size, new Color(0.5f, 0.43f, 0.33f));
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.Tau / 8;
                    fx.Strike(at + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * b.Size * 0.4f, new Vector3(Mathf.Cos(a), 0.4f, Mathf.Sin(a)), true, true);
                }
                break;
            case DeathVariety.BlastKind.Pop:
                fx.Explode(at, b.Size, 0.7, new Color(0.3f, 0.29f, 0.28f), 1f, 0.5f);
                fx.Strike(at, Vector3.Up, false, true);
                break;
            case DeathVariety.BlastKind.Sparks:
                // a brake's death: a shower of sparks off the corner and a small flare, no smoke
                for (int i = 0; i < 5; i++) fx.Strike(at, new Vector3(GD.Randf() - 0.5f, 0.6f, GD.Randf() - 0.5f).Normalized(), true, true);
                fx.Flare(at, b.Size);
                break;
            default:
                fx.Explode(at, b.Size, 1.6 + b.Size * 0.2, new Color(0.16f, 0.15f, 0.14f), 1f, b.Smoke);
                break;
        }
    }

    /// <summary>A dying car's drawn throw: where it died (game coordinates and the car's turn then), when, the plan's throw, and how long it runs.</summary>
    private sealed class Thrown
    {
        public required System.Numerics.Vector3 At;
        public required Basis Turn;
        public required double Born;
        public required float Start;
        public required DeathVariety.Throw Throw;
        /// <summary>The car's own way at death (Godot, horizontal; the kick only throws it up), carried on and slowed: a car killed at speed does not stop dead.</summary>
        public required Vector3 Drift;
        public required I76.Sim.Vehicles.Vehicle Vehicle;
        /// <summary>The pose the throw ended in; once the sim's wreck has come to rest, the drawn one's place relative to it (horizontal) and when that was.</summary>
        public Transform3D Last;
        public Vector3? Anchor;
        public float SettledAt;
        public required float Duration;
    }

    /// <summary>The blend from the drawn throw back onto the sim's wreck (s); the drawn launch's gravity (m/s²).</summary>
    private const float ThrowBlend = 0.5f, ThrowGravity = 9.8f, DriftFriction = 6f;

    private void StartThrow(CarNode car, I76.Sim.Vehicles.Vehicle v, DeathVariety.Plan plan)
    {
        var th = plan.Throw;
        float horiz = new Vector2(th.Right, th.Back).Length();
        float dur = th.Kind switch
        {
            DeathVariety.ThrowKind.Slide => horiz / System.MathF.Max(th.Friction, 0.5f),
            DeathVariety.ThrowKind.DownBack => System.MathF.Max(0.6f, horiz / System.MathF.Max(th.Friction, 0.5f)),
            DeathVariety.ThrowKind.Launch => 2 * th.Up / ThrowGravity + 0.15f,
            _ => 0.8f,
        };
        var t = new Thrown { At = v.Pos.ToVector3(), Turn = car.GlobalTransform.Basis.Orthonormalized(), Born = _t, Start = th.AtTank ? plan.TankAt : 0, Throw = th, Duration = dur,
            Drift = Coords.ToGodot(v.Vel) with { Y = 0 }, Vehicle = v, Last = car.GlobalTransform };
        car.WreckThrow = carT => ThrowPose(car, t, carT);
    }

    /// <summary>
    /// After the throw: the drawn wreck stays as the throw left it (upright, turned as it slid) while the sim's wreck finishes
    /// the original's kick; once that is at rest (or <see cref="SettleCap"/> s on) the drawn one keeps its place beside it,
    /// a few metres off at most, follows its moves and comes down to its height over <see cref="ThrowBlend"/> s. The drawn
    /// wreck is not the sim's: what hits it is the sim's, where the original's kick left it.
    /// </summary>
    private Transform3D Settled(Thrown t, Transform3D carT, float tau)
    {
        var v = t.Vehicle;
        bool rest = (v.Flags & I76.Sim.Vehicles.VehicleFlags.Airborne) == 0 && v.Vel.Length() < 2f || tau > t.Duration + SettleCap;
        if (t.Anchor is null)
        {
            if (!rest) return t.Last;
            t.Anchor = (t.Last.Origin - carT.Origin) with { Y = 0 };
            t.SettledAt = tau;
        }
        var target = new Transform3D(t.Last.Basis, carT.Origin + t.Anchor.Value);
        float k = EaseInOut((tau - t.SettledAt) / ThrowBlend);
        return new Transform3D(t.Last.Basis, t.Last.Origin.Lerp(target.Origin, k) with { X = target.Origin.X, Z = target.Origin.Z });
    }

    /// <summary>The longest the drawn wreck waits after its throw for the sim's to come to rest (s).</summary>
    private const float SettleCap = 3f;

    private static float EaseOut(float x) { x = Mathf.Clamp(x, 0, 1); return 1 - (1 - x) * (1 - x); }
    private static float EaseInOut(float x) { x = Mathf.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>Where the car is drawn this frame: held where it died until the throw starts, along the throw, then blended onto the sim's wreck.</summary>
    private Transform3D? ThrowPose(CarNode car, Thrown t, Transform3D carT)
    {
        float tau = (float)(_t - t.Born) - t.Start;
        // over: the drawn wreck lies where the throw left it, on the sim's wreck's height and following its moves (below)
        if (tau > t.Duration) return Settled(t, carT, tau);
        var th = t.Throw;
        // the car's frame at death, Godot axes: right +x, back +z (forward is -z); up is the world's
        Vector3 right = t.Turn.X.Normalized(), back = t.Turn.Z.Normalized(), up = Vector3.Up;
        var hdir = right * th.Right + back * th.Back;
        hdir.Y = 0;
        float speed = hdir.Length();
        if (speed > 1e-4f) hdir /= speed;
        var offset = Vector3.Zero;
        var spin = Basis.Identity;
        float k = Mathf.Clamp(tau / System.MathF.Max(t.Duration, 0.05f), 0, 1);
        if (tau > 0)
            switch (th.Kind)
            {
                case DeathVariety.ThrowKind.Slide:
                case DeathVariety.ThrowKind.DownBack:
                {
                    // sliding on the ground, slowing at the friction's rate to a stop
                    float ts = speed / System.MathF.Max(th.Friction, 0.5f), tt = System.MathF.Min(tau, ts);
                    offset = hdir * (speed * tt - 0.5f * th.Friction * tt * tt);
                    if (th.Kind == DeathVariety.ThrowKind.DownBack)
                    {
                        // slammed down onto its springs and the nose thrown up, both springing back
                        offset += up * -th.Dip * Mathf.Sin(Mathf.Pi * Mathf.Clamp(tau / 0.4f, 0, 1));
                        spin = new Basis(right, th.Pitch * Mathf.Sin(Mathf.Pi * Mathf.Clamp(tau / 0.6f, 0, 1)));
                    }
                    spin = new Basis(up, th.Yaw * EaseOut(k)) * spin;
                    break;
                }
                case DeathVariety.ThrowKind.Drop:
                case DeathVariety.ThrowKind.Slam:
                    // dropped onto its frame, a bounce, settling (the sag holds it there after)
                    offset = up * (-th.Dip * Mathf.Exp(-4f * tau) * Mathf.Cos(tau * 11f) * Mathf.Clamp(tau / 0.06f, 0, 1));
                    break;
                case DeathVariety.ThrowKind.Launch:
                {
                    // thrown up by the tank, turning over once, and down again: it lands the way up it left
                    float air = 2 * th.Up / ThrowGravity, ta = System.MathF.Min(tau, air);
                    float h = System.MathF.Max(0, th.Up * ta - 0.5f * ThrowGravity * ta * ta);
                    offset = hdir * speed * ta + up * h;
                    float e = EaseInOut(ta / System.MathF.Max(air, 0.05f));
                    spin = new Basis(up, th.Yaw * e) * new Basis(back, th.Roll * e) * new Basis(right, th.Pitch * e);
                    break;
                }
            }
        // the way it was going, slowing to a stop
        float dv = t.Drift.Length(), td = System.MathF.Min(tau + t.Start, dv / DriftFriction);
        if (dv > 1e-3f) offset += t.Drift / dv * (dv * td - 0.5f * DriftFriction * td * td);
        var desired = new Transform3D(spin * t.Turn, G(t.At) + offset);
        t.Last = desired;
        return desired;
    }

    /// <summary>
    /// The plans of the cars dying now in the modern look (DeathVariety), by vehicle, for CombatView to draw their effect
    /// roll's explosions by (flares, pops, the award's small blasts): until the car is renewed.
    /// </summary>
    public static readonly Dictionary<I76.Sim.Vehicles.Vehicle, DeathVariety.Plan> Dying = [];

    /// <summary>How many of a dying car's effect-roll chunks have come, for the share drawn (DeathVariety.Plan.Debris).</summary>
    private readonly Dictionary<CarNode, int> _chunks = [];

    /// <summary>A thrown wheel on fire: flames and embers trailing off it as it flies and rolls.</summary>
    private void WheelFire(RigidBody3D wheel)
    {
        WreckFire.Shared.Init();
        GpuParticles3D Emit(ParticleProcessMaterial proc, Mesh mesh, int amount, double life, float box)
        {
            var p = new GpuParticles3D
            {
                ProcessMaterial = (ParticleProcessMaterial)proc.Duplicate(), DrawPass1 = mesh, Amount = amount, Lifetime = life, LocalCoords = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-6, -3, -6), new Vector3(12, 12, 12)), Emitting = true,
            };
            ((ParticleProcessMaterial)p.ProcessMaterial).EmissionBoxExtents = new Vector3(box, box, box);
            p.SetInstanceShaderParameter("level", 0.8f);
            return p;
        }
        // on a holder that follows the wheel without turning with it: the flames rise off it whichever way it tumbles, and go with it
        var holder = new Node3D { Name = "WheelFire", TopLevel = true };
        holder.AddChild(Emit(WreckFire.Shared.FlameProc, WreckFire.Shared.FlameMesh, 10, 0.5, 0.18f));
        holder.AddChild(Emit(WreckFire.Shared.EmberProc, WreckFire.Shared.EmberMesh, 6, 1.2, 0.18f));
        holder.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.52f, 0.2f), LightEnergy = 1.6f, OmniRange = 4f, ShadowEnabled = false });
        wheel.AddChild(holder);
        _wheelFires.Add((wheel, holder));
    }

    private readonly List<(RigidBody3D Wheel, Node3D Fire)> _wheelFires = [];
    private BoxMesh _panel = null!;

    private double _t;
    private BoxMesh _chunk = null!;
    private StandardMaterial3D _chunkMat = null!;

    public override void _Ready()
    {
        _chunk = new BoxMesh { Size = new Vector3(0.35f, 0.2f, 0.5f) };
        _chunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.16f, 0.15f) };
        _panel = new BoxMesh { Size = new Vector3(1.3f, 0.05f, 0.9f) };
        AddChild(new SkidMarks { Cars = Cars, Origin = Origin, Ground = Ground, Builder = Builder, SceneLight = SceneLight, Name = "SkidMarks" });
        AddChild(new SmokeLayer { Name = "SmokeLayer" });   // the modern look's smoke is drawn and composited there, capped
        AddChild(new DamageSmoke { Cars = Cars, Builder = Builder, Origin = Origin, SceneLight = SceneLight, Alpha = Alpha, Name = "DamageSmoke" });
    }

    private Vector3 G(System.Numerics.Vector3 p) => Coords.ToGodot(p - Origin());

    private static readonly Dictionary<int, string[]> ZoneParts = new()
    {
        [0] = ["BDYF", "HLGT"], [1] = ["BDYM"], [2] = ["BDYM"], [3] = ["BDYB", "TLGT", "BLGT"], [4] = ["BDYT"],
    };

    /// <summary>A car made whole again (a melee's regen): every part's condition back to intact, the wheels unblown.</summary>
    public void Restore(CarNode car)
    {
        // a driver killed at the wheel (the .45, Nitro's chemical cloud) sounded the horn until the wreck timer's end: a
        // respawn or a regen pad before then is a fresh driver
        car.StopHorn();
        car.Conditions.Clear();
        System.Array.Clear(car.ZoneFrames);
        _deaths.Remove(car);
        _blasts.RemoveAll(b => b.Car == car);
        _panels.RemoveAll(b => b.Car == car);
        _chunks.Remove(car);
        if (car.Sim is { } sv) { Dying.Remove(sv); _sounds.RemoveAll(x => x.Vehicle == sv); }
        car.WreckThrow = null;
        if (_fires.Remove(car, out var fire)) { fire.Detach(); fire.QueueFree(); GD.Print($"wreck fire: {car.Name} renewed, out"); }
        car.SetWrecked(false);
        // the intact car's meshes, as the first of its kind was made (a respawn was 50 to 200 ms of assembling them again)
        if (System.Environment.GetEnvironmentVariable("I76_CAR_CONDITION") is null && car.Definition is { } def && !IsHeli(car)
            && Builder.CarParts.Get(def) is { Wheels: { } wheels } parts)
        {
            car.SetBody(parts.Body);
            car.RefreshHood();
            car.SetLamps(parts.Lamps);
            foreach (var (slot, mesh, _) in wheels) car.SetWheelMesh(slot, mesh, false);
            return;
        }
        Rebuild(car);
        if (Builder.Assembler!.Assemble(car.Definition) is { } m)
            for (int slot = 0; slot < 6; slot++) car.SetWheelMesh(slot, Builder.Meshes!.BuildWheel(m, slot, blown: false), false);
    }

    /// <summary>
    /// After a restore (a checkpoint's retry, docs/savestate.md): every car's and structure's look from the simulation's state,
    /// quietly. The zones' damage levels, a wreck burnt out (burning still while its death timer runs), a driver slumped on the
    /// horn, the pieces a dying car threw off, an emplacement's gun gone, a stopped engine; each destroyed structure's 'A' parts.
    /// The tyres follow Wheel.Blown each frame anyway.
    /// </summary>
    public void Resync()
    {
        foreach (var (v, car) in Cars) Resync(v, car);
        foreach (var o in PlacementOf.Keys)
            if (o.Destroyed || o.Nodes.Any(n => n.Destroyed)) Destroy(o, quiet: true);
    }

    private void Resync(I76.Sim.Vehicles.Vehicle v, CarNode car)
    {
        car.WreckThrow = null;   // a restored wreck lies where the sim has it
        uint f = (uint)v.Flags;
        bool dead = (f & (uint)I76.Sim.Vehicles.VehicleFlags.Dead) != 0, slumped = dead && (f & 0x4000) != 0, dying = (f & 0x8000) != 0;
        bool changed = false;
        if (v.Health is { } h && !(dead && !slumped))
            for (int z = 0; z < 5; z++)
                changed |= ShowZone(car, z, h.ZoneLevel(z));
        if (dead && !slumped)
        {
            car.SetWrecked(true);
            changed = true;
            if (dying) Burn(car);
            if (v.IsEmplacement) car.HideWeapon(0);
        }
        if (slumped && dying && I76.Sim.Vehicles.VehicleFactory.HornSound(Builder.Vfs, v.EngineNumber) is { } horn) car.StartHorn(Audio?.Get(horn, loop: true));
        if (changed) Rebuild(car);
        if (dead || (v.Flags & I76.Sim.Vehicles.VehicleFlags.EngineOn) == 0) car.StopEngine();
        if (v.Health is { } d)
            for (int slot = 0; slot < d.Slots.Length; slot++)
                if (d.Slots[slot] is { Detached: true })
                {
                    if (slot < 6) car.FlingWheel(slot);
                    else if (slot is >= 10 and <= 16) car.FlingWeapon(slot - 10);
                }
    }

    /// <summary>
    /// A zone at <paramref name="level"/> (0..3): its parts' shape and its slots' texture,
    /// never back toward whole; whether either changed.
    /// </summary>
    private static bool ShowZone(CarNode car, int zone, int level)
    {
        bool changed = false;
        char want = DamageTextures.Condition(level);
        foreach (var part in ZoneParts[zone])
            if (car.Conditions.GetValueOrDefault(part, '1') < want) { car.Conditions[part] = want; changed = true; }
        int frame = DamageTextures.Frame(System.Math.Min(level, 3));
        if (car.ZoneFrames[zone] < frame) { car.ZoneFrames[zone] = frame; changed = true; }
        return changed;
    }

    /// <summary>
    /// Builds a car's wreck once, unseen, at the mission's start (behind the loading card), and the fire's shared materials: the
    /// first death of a game cost 160 ms to assemble and build the wreck and 60 ms to start its fire (a second one 40 and 1), so
    /// a car that fell to its death froze the game for a quarter of a second as the explosion went up.
    /// </summary>
    public void Prewarm(CarNode car)
    {
        if (!IsHeli(car)) PrewarmWrecks([car.Definition], car.Modern);
        if (WreckFire.Enabled) WreckFire.Shared.Init();
    }

    /// <summary>
    /// The burnt look of these kinds of car, made now: the first death of each kind cost 15 to 20 ms of assembling and building its
    /// wreck, a stall in the frame its explosion went up. The player's car and a melee's whole roster, behind the loading card.
    /// </summary>
    private readonly Dictionary<(string, int, bool), ArrayMesh?> _tyres = new();

    /// <summary>A wheel's mesh, whole or blown, made once per kind of car: a dying car's tyres go flat in the frame of its death (7 ms for the first, 4 for each after).</summary>
    private ArrayMesh? TyreMesh(string def, int slot, bool blown, ref AssembledModel? model)
    {
        if (_tyres.TryGetValue((def, slot, blown), out var mesh)) return mesh;
        mesh = (model ??= Builder.Assembler!.Assemble(def)) is { } am ? Builder.Meshes!.BuildWheel(am, slot, blown) : null;
        return _tyres[(def, slot, blown)] = mesh;
    }

    public void PrewarmWrecks(IEnumerable<string> defs, bool modern)
    {
        foreach (var def in defs.Distinct(System.StringComparer.OrdinalIgnoreCase))
        {
            // as Rebuild will ask for it at the death of a car with nothing damaged before
            var asm = new ModelAssembler(Builder.Vfs) { PartCondition = _ => '1', SkipParts = [], Wrecked = true, WreckShape = modern };
            try
            {
                if (asm.Assemble(def) is { } m) WreckLook(def, modern, m);
                AssembledModel? whole = null;
                for (int slot = 0; slot < 6; slot++) TyreMesh(def, slot, true, ref whole);
            }
            catch (System.Exception e) { GD.Print($"wreck prewarm {def}: {e.Message}"); }
        }
    }

    /// <summary>
    /// A rehearsal of a death behind the loading card, on a stand-in for a car: burnt out and its fire lit, kept alive while the tree is paused so the
    /// renderer makes the wreck's and the fire's pipelines and the code that builds them has run once (the first death of a
    /// game cost half a second); <see cref="EndWarm"/> takes the fire and the debris away (the caller frees the stand-in).
    /// </summary>
    public void Warm(CarNode car)
    {
        if (IsHeli(car)) return;
        car.SetWrecked(true);
        Rebuild(car);
        Burn(car);
        if (_fires.TryGetValue(car, out var f)) f.ProcessMode = ProcessModeEnum.Always;
        // the smoke is drawn by a layer of its own (SmokeLayer), whose compositor compiles on the first puff it gets
        foreach (var n in new[] { "SmokeLayer", "DamageSmoke" }) if (GetNodeOrNull(n) is { } sn) sn.ProcessMode = ProcessModeEnum.Always;
        // the chunks a wreck throws off (a Standard material on a box)
        {
            _warmChunk = new MeshInstance3D { Mesh = _chunk, MaterialOverride = _chunkMat, Position = car.GlobalPosition };
            AddChild(_warmChunk);
            // and a piece thrown as a wheel or gun is (a copy of a node on a rigid body, the first of which costs the physics server a setup)
            int before = _debris.Count;
            ThrowPiece(_warmChunk, System.Numerics.Vector3.Zero);
            _warmDebris = _debris.Count > before ? _debris[^1].Body : null;
        }
    }

    private MeshInstance3D? _warmChunk;
    private RigidBody3D? _warmDebris;

    public void EndWarm(CarNode car)
    {
        _warmChunk?.QueueFree();
        _warmChunk = null;
        if (_warmDebris is { } wd) { _debris.RemoveAll(d => d.Body == wd); wd.QueueFree(); _warmDebris = null; }
        foreach (var n in new[] { "SmokeLayer", "DamageSmoke" }) if (GetNodeOrNull(n) is { } sn) sn.ProcessMode = ProcessModeEnum.Inherit;
        if (_fires.Remove(car, out var fire)) { fire.Detach(); fire.QueueFree(); }
        DamageSmoke.Current?.Clear();
    }

    private static bool IsHeli(CarNode car) => car.Sim?.IsHeli == true;

    /// <summary>A model's burnt look (its body and lamp meshes), made once: the key is the car's kind and the parts the wreck keeps.</summary>
    private readonly Dictionary<string, (ArrayMesh? Body, CarNode.LampMeshes Lamps)> _wrecks = new();

    private (ArrayMesh? Body, CarNode.LampMeshes Lamps) WreckLook(string def, bool modern, AssembledModel m)
    {
        string key = def + "|" + modern + "|" + string.Join(',', m.Parts.Select(p => p.NodeName));
        if (_wrecks.TryGetValue(key, out var look)) return look;
        // the wreck's texture frames do not depend on the car (CarNode.TexFrame, wrecked), so every car of the kind shares them
        var body = Builder.Meshes!.Build(m, wheels: false, include: CarNode.BodyParts(m), bothSides: modern,
            frameOf: t => DamageTextures.Zone(t) >= 0 ? (modern ? 2 : DamageTextures.WreckFrame) : 0);
        var lamps = CarNode.BuildLamps(Builder.Meshes, m, new ModelAssembler(Builder.Vfs) { SkipParts = [] }.Assemble(def));
        return _wrecks[key] = (body, lamps);
    }

    public void Rebuild(CarNode car)
    {
        var asm = new ModelAssembler(Builder.Vfs) { PartCondition = part => car.Conditions.GetValueOrDefault(part, '1'), SkipParts = [], Wrecked = car.Wrecked, WreckShape = car.Modern };
        if (asm.Assemble(car.Definition) is not { } m) return;
        if (car.Wrecked)
        {
            var (body, lamps) = WreckLook(car.Definition, car.Modern, m);
            car.SetBody(body);
            car.RefreshHood();
            car.SetLamps(lamps);
            return;
        }
        // the modern wreck's shape (WreckShape) is open where its zones' damaged parts leave gaps: drawn from both sides, so its insides show
        car.SetBody(Builder.Meshes!.Build(m, wheels: false, include: CarNode.BodyParts(m), bothSides: car.Wrecked && car.Modern, frameOf: car.TexFrame));
        car.RefreshHood();
        car.SetLamps(Builder.Meshes, m, new ModelAssembler(Builder.Vfs) { SkipParts = [] }.Assemble(car.Definition));
    }

    /// <summary>
    /// A one-shot where it happened, heard as DirectSound3D heard it
    /// (Voice3D): min distance 10 for voices, 100 for sound
    /// event 7's vexplode.wav (flagged 0x200).
    /// </summary>
    private void Sound(string name, System.Numerics.Vector3 at, float minDistance = Ds3d.MinDistance)
    {
        if (Audio?.Get(name) is not { } s) return;
        var p = Voice3D.OneShot(s, name, Audio.Limits(name), minDistance);
        p.Position = G(at);
        AddChild(p);
        p.Start();
    }

    private void Throw(System.Numerics.Vector3 at, System.Numerics.Vector3 vel, Mesh? mesh = null)
    {
        var body = new RigidBody3D { Position = G(at) + new Vector3(0, 0.8f, 0), LinearVelocity = Coords.ToGodot(vel), AngularVelocity = new Vector3(3, 5, 2) };
        body.AddChild(new MeshInstance3D { Mesh = mesh ?? _chunk, MaterialOverride = mesh is null ? _chunkMat : null });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.4f, 0.4f, 0.4f) } });
        AddChild(body);
        _debris.Add((body, _t + 6));
    }

    /// <summary>A copy of a piece of a car, thrown from where it is on the car (its spin and steer as they were).</summary>
    private RigidBody3D ThrowPiece(Node3D piece, System.Numerics.Vector3 vel, float? heat = null)
    {
        var copy = (Node3D)piece.Duplicate();
        copy.Transform = Transform3D.Identity;
        copy.Visible = true;
        // a piece off a burning wreck is as charred and as hot as the car it left (the modern look's shader reads it)
        var burn = piece is MeshInstance3D pm ? pm.GetInstanceShaderParameter("wreck") : piece.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>().FirstOrDefault()?.GetInstanceShaderParameter("wreck");
        if (burn is { VariantType: Variant.Type.Vector2 } bv)
            foreach (var m in (copy is MeshInstance3D cm ? [cm] : copy.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>()))
                m.SetInstanceShaderParameter("wreck", bv.AsVector2() with { Y = heat ?? bv.AsVector2().Y * 0.6f });
        // tumbling, its open side turns to the camera: the modern look draws it from both sides (1997's as the original, culled)
        if (Builder.Materials is { Mode1997: false } mats) mats.ShowBothSides(copy);
        var body = new RigidBody3D { LinearVelocity = Coords.ToGodot(vel), AngularVelocity = new Vector3(3, 5, 2) };
        body.AddChild(copy);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.4f, 0.4f, 0.4f) } });
        AddChild(body);
        body.GlobalTransform = piece.GlobalTransform;
        _debris.Add((body, _t + 6));
        return body;
    }

    private static readonly int? HookLevel = int.TryParse(System.Environment.GetEnvironmentVariable("I76_CAR_DAMAGE"), out int l) ? System.Math.Clamp(l, 0, 3) : null;
    private readonly HashSet<CarNode> _hooked = [];
    /// <summary>I76_DEATH_SEED=N: every death drawn with that seed (a capture of one draw side by side; docs/dev-hooks.md).</summary>
    private static readonly ushort? SeedOverride = ushort.TryParse(System.Environment.GetEnvironmentVariable("I76_DEATH_SEED"), out ushort so) ? so : null;

    private AudioStreamPlayer? _ignition;
    private I76.Sim.Vehicles.Vehicle? _ignitionOf;

    private void StopIgnition()
    {
        _ignition?.Stop();
        _ignition?.QueueFree();
        _ignition = null;
    }

    public override void _Process(double delta)
    {
        using var _view = Hitch.Time(Hitch.Phase.Views);
        _t += delta;
        foreach (var e in Damage.Events)
            switch (e)
            {
                case ZoneDamaged z when Cars.TryGetValue(z.Vehicle, out var car):
                    if (ShowZone(car, z.Zone, z.Level)) Rebuild(car);
                    break;
                case VehicleKilled k when Cars.TryGetValue(k.Vehicle, out var car):
                {
                    car.StopEngine();
                    // the modern look's death by its cause (DeathVariety); the 1997 look's is the original's
                    var plan = car.Modern && !k.Vehicle.IsEmplacement && !IsHeli(car) ? DeathVariety.For(k.Cause, SeedOverride ?? k.Seed, DeathVariety.Enabled) : null;
                    if (plan is not null) _deaths[car] = (plan, 0);
                    bool staged = plan is not null && plan != DeathVariety.Plan.Plain;
                    if (staged) Dying[k.Vehicle] = plan!;
                    if (plan is not null) GD.Print($"death-plan: {car.Name} cause {k.Cause} seed {SeedOverride ?? k.Seed} plan {(staged ? $"flaming {plan.FlamingWheels} award {plan.Award} tank {plan.TankSize:0.0} at {plan.TankAt:0.00}" : "plain")}");
                    if (k.DriverKilled)
                    {
                        // the .45's kill (the vehicle's .45-kill flag): no explosion and no wreck, the car as it was, its horn stuck on
                        if (I76.Sim.Vehicles.VehicleFactory.HornSound(Builder.Vfs, k.Vehicle.EngineNumber) is { } horn)
                            car.StartHorn(Audio?.Get(horn, loop: true));
                        // the modern look: a fire takes the engine bay some seconds later
                        if (plan is { FireDelay: > 0 }) Burn(car, plan);
                        break;
                    }
                    // every zone goes to state 4: the zones' nodes take their state-3 geometry, the burnt hull
                    // alone; the lamps go out with the lights and the brake lamp's hide flag
                    car.SetWrecked(true);
                    Rebuild(car);
                    Burn(car, plan);
                    // the variety's two stages (the tank's blast and vexplode.wav held for stage 2); off, CombatView draws the original's blast alone
                    if (staged) Stage(car, k.Vehicle, plan!);
                    // a gun emplacement: the zones' state 3 is the burnt pole, and its gun is hidden
                    if (k.Vehicle.IsEmplacement) car.HideWeapon(0);
                    if (!staged || plan!.TankSize <= 0) Sound("vexplode.wav", k.Vehicle.Pos.ToVector3(), Ds3d.LoudMinDistance);   // Sound event 7
                    break;
                }
                case VehicleWrecked w when Cars.TryGetValue(w.Vehicle, out var wc):
                    wc.StopHorn();
                    break;
                case IgnitionStarted ig:
                    // The player's ignition sound, not positional; it plays for the table's time, not the file's
                    StopIgnition();
                    _ignitionOf = ig.Vehicle;
                    if (Audio?.Get(ig.Sound!) is { } igs)
                    {
                        _ignition = new AudioStreamPlayer { Bus = "SFX", Stream = igs, VolumeDb = -4 };
                        AddChild(_ignition);
                        _ignition.Play();
                    }
                    break;
                case EngineStarted es when Cars.TryGetValue(es.Vehicle, out var ec):
                    if (es.Vehicle == _ignitionOf) StopIgnition();
                    ec.RestartEngine();   // the engine loop on, the ignition sound off
                    break;
                case EngineStopped st when Cars.TryGetValue(st.Vehicle, out var sc):
                    sc.StopEngine();
                    break;
                case TyreBlown t:
                    Sound("tblow.wav", t.Vehicle.Pos.ToVector3());   // the mesh follows Wheel.Blown below
                    break;
                case PartFlung f:
                {
                    // the wheel or the weapon itself leaves the car, from where it was
                    var pieces = new List<Node3D>();
                    if (Cars.TryGetValue(f.Vehicle, out var fc))
                        if (f.Slot >= 10) pieces.AddRange(fc.FlingWeapon(f.Slot - 10));
                        else if (fc.FlingWheel(f.Slot) is { } wn) pieces.Add(wn);
                    if (pieces.Count == 0) Throw(f.Vehicle.Pos.ToVector3(), f.Velocity);
                    // a wheel off a death with a plan: the first ones thrown burn as they fly, the rest go charred and cold
                    if (f.Slot < 6 && pieces.Count == 1 && fc is not null && _deaths.TryGetValue(fc, out var dv) && dv.Plan != DeathVariety.Plan.Plain)
                    {
                        bool flaming = dv.Thrown < dv.Plan.FlamingWheels;
                        _deaths[fc] = dv with { Thrown = dv.Thrown + 1 };
                        var wb = ThrowPiece(pieces[0], f.Velocity, flaming ? null : 0f);
                        GD.Print($"wreck fire: {fc.Name} threw wheel {f.Slot}, {(flaming ? "burning" : "cold")}");
                        if (flaming) WheelFire(wb);
                        break;
                    }
                    if (f.Slot < 6 && pieces.Count == 1 && fc is not null) GD.Print($"wreck fire: {fc.Name} threw wheel {f.Slot}, {(fc.Modern ? "glowing" : "1997")}");
                    foreach (var piece in pieces) ThrowPiece(piece, f.Velocity);
                    break;
                }
                case Debris d:
                {
                    // a dying car's chunks: in an ordinary death of the variety, a share of them, slower (DeathVariety.Plan.Debris)
                    var vel = d.Velocity + new System.Numerics.Vector3((float)GD.RandRange(-4, 4), 0, (float)GD.RandRange(-4, 4));
                    var owner = _deaths.FirstOrDefault(kv => kv.Value.Plan.Debris < 1f && kv.Key.Sim is { } ds && System.Numerics.Vector3.DistanceSquared(ds.Pos.ToVector3(), d.At) < 64).Key;
                    if (owner is not null)
                    {
                        int n = _chunks.GetValueOrDefault(owner);
                        _chunks[owner] = n + 1;
                        float share = _deaths[owner].Plan.Debris;
                        if ((int)((n + 1) * share) == (int)(n * share)) break;
                        vel *= DeathVariety.DebrisSpeed;
                    }
                    // from the car as drawn (a thrown one is not where the sim has it)
                    var from = d.At;
                    if (owner is not null && owner.Sim is { } os) from += Coords.ToGame(owner.GlobalPosition - G(os.Pos.ToVector3()));
                    Throw(from, vel);
                    break;
                }
                case SignKnocked k when PlacementOf.TryGetValue(k.Object, out var sp):
                {
                    Builder.Scenery!.Destroy(sp, null);
                    var model = sp.Definition is { } d ? Builder.Assembler!.Assemble(d) : null;
                    var mesh = model is null ? null : Builder.Meshes!.Build(model);
                    var body = new RigidBody3D
                    {
                        Transform = new Transform3D(Coords.ToGodot(sp.Object.Rotation), G(Builder.World is { } sw ? sw.GroundedPosition(sp) : sp.Object.Position)),
                        LinearVelocity = Coords.ToGodot(k.Velocity),
                        AngularVelocity = new Vector3((float)GD.RandRange(-4, 4), (float)GD.RandRange(-4, 4), (float)GD.RandRange(-4, 4)),
                    };
                    if (mesh is not null) body.AddChild(new MeshInstance3D { Mesh = mesh });
                    body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.5f, 1.5f, 0.5f) }, Position = new Vector3(0, 0.75f, 0) });
                    AddChild(body);
                    _debris.Add((body, _t + 8));
                    break;
                }
                case StructureDestroyed s:
                    Destroy(s.Object, quiet: false, s.Booms);
                    break;
            }
        Damage.Events.Clear();
        // I76_CAR_DAMAGE=L: every car's zones shown at level L (0..3) as a hit would leave them, for captures of the damage looks
        if (HookLevel is { } hl)
            foreach (var car in Cars.Values)
                if (_hooked.Add(car))
                {
                    bool any = false;
                    for (int z = 0; z < 5; z++) any |= ShowZone(car, z, hl);
                    if (any) Rebuild(car);
                }
        foreach (var car in Cars.Values)
        {
            I76.Formats.Assembly.AssembledModel? m = null;
            car.SyncTyres((slot, blown) => TyreMesh(car.Definition, slot, blown, ref m));
        }
        for (int i = _wheelFires.Count - 1; i >= 0; i--)
        {
            var (wb, wf) = _wheelFires[i];
            if (!IsInstanceValid(wb) || !wb.IsInsideTree()) { _wheelFires.RemoveAt(i); continue; }
            wf.GlobalPosition = wb.GlobalPosition;
            wf.Visible = Builder.Materials is not { Mode1997: true };
        }
        for (int i = _blasts.Count - 1; i >= 0; i--)
            if (_t >= _blasts[i].At)
            {
                var b = _blasts[i];
                _blasts.RemoveAt(i);
                if (IsInstanceValid(b.Car) && b.Car.Modern) Go(b.Car, b.Vehicle, b.Blast);
            }
        for (int i = _panels.Count - 1; i >= 0; i--)
            if (_t >= _panels[i].At)
            {
                var pt = _panels[i];
                _panels.RemoveAt(i);
                if (IsInstanceValid(pt.Car) && pt.Car.Modern) ThrowPanels(pt.Car, pt.Vehicle, pt.From, pt.Count, pt.PanelAt, pt.Burning);
            }
        for (int i = _sounds.Count - 1; i >= 0; i--)
            if (_t >= _sounds[i].At)
            {
                var sd = _sounds[i];
                _sounds.RemoveAt(i);
                Sound(sd.Name, sd.Vehicle.Pos.ToVector3(), sd.MinDistance);
            }
        for (int i = _debris.Count - 1; i >= 0; i--)
            if (_t > _debris[i].Until) { _debris[i].Body.QueueFree(); _debris.RemoveAt(i); }
        foreach (var (car, fire) in _fires.Where(kv => kv.Value.Finished).ToList())
        {
            _fires.Remove(car);
            fire.QueueFree();
        }
    }

    /// <summary>
    /// A structure's destroyed look: each dead node's 'A' part or nothing, the
    /// live ones intact (one without nodes: its 'A' parts, or gone), and the
    /// sound of each explosion that went with it. Quietly, for one already
    /// down when a network client joins: no sound.
    /// </summary>
    public void Destroy(I76.Sim.Collision.StaticObject o, bool quiet, IReadOnlyList<int>? booms = null)
    {
        if (!PlacementOf.TryGetValue(o, out var p)) return;
        Mesh? alt = null;
        I76.Formats.Assembly.AssembledModel? altModel = null;
        if (p.Definition is { } def && o.Nodes.Count > 0)
        {
            bool Dead(string name) => o.Nodes.FindIndex(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? o.Nodes[i].Destroyed : o.Destroyed;
            // a gate is drawn on its own, sliding (ScenerySpawner), whatever else of the structure is down
            if (new ModelAssembler(Builder.Vfs) { DestroyedNode = Dead }.Assemble(def) is { Parts.Count: > 0 } m)
            {
                // the camera-facing cards (the pine's stump) are the spawner's to draw
                alt = Builder.Meshes!.Build(m, include: part => !Builder.Scenery!.IsGatePart(p, part.NodeName) && !Builder.Scenery.IsCardPart(p, part.NodeName), skipFace: GroundFaces(p, m));
                altModel = m;
            }
        }
        else if (p.Definition is { } d0 && new ModelAssembler(Builder.Vfs) { Destroyed = true }.Assemble(d0) is { } m
            && Builder.Assembler!.Assemble(d0) is { } intact && m.Parts.Select(x => x.NodeName).SequenceEqual(intact.Parts.Select(x => x.NodeName)) == false)
        {
            alt = Builder.Meshes!.Build(m, include: part => !Builder.Scenery!.IsCardPart(p, part.NodeName), skipFace: GroundFaces(p, m));
            altModel = m;
        }
        Builder.Scenery!.Destroy(p, alt, altModel, altModel is null ? null : GroundFaces(p, altModel));
        // A "null" SDFC sound is event 7's vexplode.wav (min distance 100), a named one (10)
        if (quiet || booms is null) return;
        foreach (int b in booms)
        {
            var at = b < 0 ? o.Marker ?? o.Centre : o.Nodes[b].Base ?? o.Centre;
            Sound(o.Sound, at, o.Sound.Equals("vexplode.wav", StringComparison.OrdinalIgnoreCase) ? Ds3d.LoudMinDistance : Ds3d.MinDistance);
        }
    }

    /// <summary>The wreck of a grounded building keeps its lot painted into the terrain (GroundDecals), so leaves it out of its mesh too.</summary>
    private Func<ModelPart, I76.Formats.Geo.GeoFace, bool>? GroundFaces(Placement p, AssembledModel m)
    {
        if (!World.GroundDecals.Applies(p)) return null;
        float lowest = World.GroundDecals.LowestY(m);
        return (part, f) => World.GroundDecals.IsGroundFace(part, f, lowest);
    }
}
