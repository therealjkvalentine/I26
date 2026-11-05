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
    /// <summary>The modern look's deaths waiting out DeathVariety.Delay: when the wreck, its fire and vexplode.wav come.</summary>
    private readonly List<(double At, CarNode Car, I76.Sim.Vehicles.Vehicle Vehicle)> _late = [];
    /// <summary>The cars dying now in the modern look's death (DeathVariety) and how many chunks each has thrown, for the share drawn.</summary>
    private readonly Dictionary<CarNode, int> _chunks = [];
    /// <summary>The modern look's fire on each burning wreck (WreckFire), until it has burnt out or the car is renewed.</summary>
    private readonly Dictionary<CarNode, WreckFire> _fires = [];

    /// <summary>The mission's light for the wrecks' smoke, as CombatView's: 1 by day, lower at night.</summary>
    private float SceneLight => Mathf.Clamp(Builder.Light.Intensity * 1.5f, 0.25f, 1f);

    /// <summary>A wreck starts burning (the modern look; hidden in 1997's): flames, smoke, the charred body's glow.</summary>
    private void Burn(CarNode car)
    {
        if (_fires.Remove(car, out var old)) { old.Detach(); old.QueueFree(); }
        if (!WreckFire.Enabled) return;
        var f = new WreckFire { Car = car, SceneLight = SceneLight, Name = $"Fire_{car.Name}" };
        AddChild(f);
        _fires[car] = f;
        GD.Print($"wreck fire: {car.Name} burning");
    }

    /// <summary>
    /// A car's death drawn: every zone goes to state 4 (the zones' nodes take their state-3 geometry, the burnt hull alone; the
    /// lamps go out with the lights and the brake lamp's hide flag), the fire, vexplode.wav (Sound event 7); a gun emplacement's
    /// zones' state 3 is the burnt pole, and its gun is hidden.
    /// </summary>
    private void Wreck(CarNode car, I76.Sim.Vehicles.Vehicle v)
    {
        car.SetWrecked(true);
        Rebuild(car);
        Burn(car);
        if (v.IsEmplacement) car.HideWeapon(0);
        Sound("vexplode.wav", v.Pos.ToVector3(), Ds3d.LoudMinDistance);
    }

    /// <summary>
    /// The modern look's small event at the moment of a car's death, before its blast (DeathVariety.Pre): the hood popping up
    /// with a puff under it, a burst at the failed wheel, a puff at the cab, a single crack; nothing for the other causes.
    /// </summary>
    private void PreEvent(CarNode car, I76.Sim.Vehicles.Vehicle v, DeathCause cause)
    {
        var pre = DeathVariety.PreFor(cause);
        int wheel = pre == DeathVariety.Pre.Wheel && v.Health is { } h ? DeathVariety.FailedWheel(h.Slots) : -1;
        GD.Print($"death-pre: {car.Name} cause {cause} pre {pre}{(wheel >= 0 ? $" wheel {wheel}" : "")} at {Damage.World.Time:0.000} s");
        if (pre == DeathVariety.Pre.None || Fx?.Invoke() is not { HasExplosions: true } fx) return;
        var b = car.BodyBounds;
        // a point on the body: fractions of its bounds, Godot axes (x right, y up, z back: the front is z 0)
        Vector3 On(float x, float y, float z) => car.GlobalTransform * (b.Position + b.Size * new Vector3(x, y, z));
        switch (pre)
        {
            case DeathVariety.Pre.Hood:
            {
                // the hood lifts off the engine bay, a dark puff under it; it falls back with the blast
                var at = On(0.5f, 0.85f, 0.15f);
                fx.Puff(at, DeathVariety.HoodPuff, new Color(0.22f, 0.2f, 0.19f), 1.2f);
                var basis = car.GlobalTransform.Basis.Orthonormalized();
                var rb = new RigidBody3D { LinearVelocity = basis.Y * DeathVariety.HoodLift + Coords.ToGodot(v.Vel), AngularVelocity = basis.X * -DeathVariety.HoodSpin };
                var size = new Vector3(b.Size.X * 0.8f, 0.04f, b.Size.Z * 0.28f);
                rb.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = _chunkMat });
                rb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                AddChild(rb);
                rb.GlobalTransform = new Transform3D(basis, On(0.5f, 0.95f, 0.17f));
                _debris.Add((rb, _t + 6));
                break;
            }
            case DeathVariety.Pre.Wheel:
            {
                // a small burst at the wheel that failed
                var at = (wheel >= 0 ? car.WheelPosition(wheel) : null) ?? On(0.5f, 0.2f, 0.2f);
                fx.Explode(at, DeathVariety.WheelBurst, 0.5, new Color(0.3f, 0.29f, 0.28f), 1f);
                fx.Strike(at, Vector3.Up, false, false);
                break;
            }
            case DeathVariety.Pre.Cab:
                // the driver: a grey puff at the cab, no fire
                fx.Puff(On(0.5f, 0.9f, 0.45f), DeathVariety.CabPuff, new Color(0.45f, 0.44f, 0.42f));
                break;
            case DeathVariety.Pre.Crack:
                // the ammunition: one crack, sparks and a flash on top of the body
                fx.Strike(On(0.5f, 1f, 0.5f), Vector3.Up, false, true);
                break;
        }
    }
    private BoxMesh _panel = null!;

    private double _t;
    private BoxMesh _chunk = null!;
    private StandardMaterial3D _chunkMat = null!;

    public override void _Ready()
    {
        _chunk = new BoxMesh { Size = new Vector3(0.35f, 0.2f, 0.5f) };
        _chunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.16f, 0.15f) };
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
        _late.RemoveAll(x => x.Car == car);
        _chunks.Remove(car);
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
        _late.RemoveAll(x => x.Car == car);   // a restored wreck is drawn from the sim's state, at once
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
    private void ThrowPiece(Node3D piece, System.Numerics.Vector3 vel)
    {
        var copy = (Node3D)piece.Duplicate();
        copy.Transform = Transform3D.Identity;
        copy.Visible = true;
        // a piece off a burning wreck is as charred and as hot as the car it left (the modern look's shader reads it)
        var burn = piece is MeshInstance3D pm ? pm.GetInstanceShaderParameter("wreck") : piece.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>().FirstOrDefault()?.GetInstanceShaderParameter("wreck");
        if (burn is { VariantType: Variant.Type.Vector2 } bv)
            foreach (var m in (copy is MeshInstance3D cm ? [cm] : copy.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>()))
                m.SetInstanceShaderParameter("wreck", bv.AsVector2() with { Y = bv.AsVector2().Y * 0.6f });
        // tumbling, its open side turns to the camera: the modern look draws it from both sides (1997's as the original, culled)
        if (Builder.Materials is { Mode1997: false } mats) mats.ShowBothSides(copy);
        var body = new RigidBody3D { LinearVelocity = Coords.ToGodot(vel), AngularVelocity = new Vector3(3, 5, 2) };
        body.AddChild(copy);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.4f, 0.4f, 0.4f) } });
        AddChild(body);
        body.GlobalTransform = piece.GlobalTransform;
        _debris.Add((body, _t + 6));
    }

    private static readonly int? HookLevel = int.TryParse(System.Environment.GetEnvironmentVariable("I76_CAR_DAMAGE"), out int l) ? System.Math.Clamp(l, 0, 3) : null;
    private readonly HashSet<CarNode> _hooked = [];

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
                    // the modern look's death (DeathVariety): a small event by cause now, the stock death a moment later, smaller
                    bool own = DeathVariety.Owns(k.Vehicle, car.Modern) && !IsHeli(car);
                    if (own) PreEvent(car, k.Vehicle, k.Cause);
                    if (k.DriverKilled)
                    {
                        // the .45's kill (the vehicle's .45-kill flag): no explosion and no wreck, the car as it was, its horn stuck on
                        if (I76.Sim.Vehicles.VehicleFactory.HornSound(Builder.Vfs, k.Vehicle.EngineNumber) is { } horn)
                            car.StartHorn(Audio?.Get(horn, loop: true));
                        break;
                    }
                    if (own)
                    {
                        _chunks[car] = 0;
                        _late.Add((_t + DeathVariety.Delay, car, k.Vehicle));
                        break;
                    }
                    Wreck(car, k.Vehicle);
                    break;
                }
                case VehicleWrecked w when Cars.TryGetValue(w.Vehicle, out var wc):
                    wc.StopHorn();
                    _chunks.Remove(wc);
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
                    foreach (var piece in pieces) ThrowPiece(piece, f.Velocity);
                    break;
                }
                case Debris d:
                {
                    // a dying car's chunks in the modern look's death: a share of them (DeathVariety.ShrapnelFactor)
                    var owner = _chunks.Keys.FirstOrDefault(c => c.Sim is { } ds && System.Numerics.Vector3.DistanceSquared(ds.Pos.ToVector3(), d.At) < 64);
                    if (owner is not null && !DeathVariety.KeepChunk(_chunks[owner]++)) break;
                    Throw(d.At, d.Velocity + new System.Numerics.Vector3((float)GD.RandRange(-4, 4), 0, (float)GD.RandRange(-4, 4)));
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
        for (int i = _late.Count - 1; i >= 0; i--)
            if (_t >= _late[i].At)
            {
                var (_, lc, lv) = _late[i];
                _late.RemoveAt(i);
                if (IsInstanceValid(lc)) Wreck(lc, lv);
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
