using I76.Sim.Damage;

namespace I76.Game.Sim;

/// <summary>
/// The modern look's car death by what killed it ([port] death_variety, on by default; docs/deviations.md "Car deaths"). The
/// original's death is one explosion whatever the cause, and the 1997 look keeps it. Here it comes in two stages: the
/// component's own event at the component (stage 1), then, 200 to 500 ms later, the tank's single blast (stage 2), with a
/// throw of the drawn body by cause that settles back onto the sim's wreck. Every number is a row of <see cref="Table"/>.
/// The plan is drawn from the death's cause and seed (<see cref="VehicleKilled"/>), so every machine of a network game draws
/// the same one. Engine-free: the views read the plan and draw it.
/// </summary>
public static class DeathVariety
{
    /// <summary>[port] death_variety: off draws every death as the plain one (upstream's single full blast, every thrown wheel glowing).</summary>
    public static bool Enabled = true;

    /// <summary>A blast on the car, after <see cref="Delay"/> s: where, as fractions of the body's half width, height and half length (game axes: x right, z forward), and its radius in metres.</summary>
    public readonly record struct Blast(float Delay, float X, float Y, float Z, float Size, BlastKind Kind = BlastKind.Fire, float Smoke = 0.5f);

    /// <summary>Fire: a fireball with smoke; Dust: thrown out low, no fire; Pop: a short burst and sparks; Sparks: a shower of sparks and a small flare, no smoke.</summary>
    public enum BlastKind { Fire, Dust, Pop, Sparks }

    /// <summary>How the drawn body is thrown: not at all (the sim's kick shows), sliding on the ground, down and back, dropped onto its frame, slammed down, or launched.</summary>
    public enum ThrowKind { None, Slide, DownBack, Drop, Slam, Launch }

    /// <summary>
    /// The drawn throw, in the car's frame at death (Right, Up, Back in m/s; Yaw, Roll, Pitch the turns in radians over the
    /// throw), from stage 1 or stage 2 (<see cref="AtTank"/>); <see cref="Friction"/> slows a slide (m/s²); <see cref="Dip"/>
    /// drops the body that far for a moment (m).
    /// </summary>
    public readonly record struct Throw(ThrowKind Kind, bool AtTank = false, float Up = 0, float Right = 0, float Back = 0, float Yaw = 0, float Roll = 0, float Pitch = 0, float Friction = 5f, float Dip = 0);

    /// <summary>
    /// One death's look. <see cref="First"/>: stage 1's blasts; <see cref="TankAt"/> s later the tank's one blast
    /// (<see cref="TankSize"/> m, <see cref="TankSmoke"/> its smoke amount, 0 none). <see cref="FlamingWheels"/>: how many of
    /// the wheels the wreck throws burn as they fly (the first thrown). The fire: where on the length it burns (−1 rear … 1
    /// front), its size, the seconds before it catches, its smoke rate (<see cref="Smoke"/>, 1 the old rate), how far its
    /// flames spread (<see cref="Spread"/>) and how long they stay full (<see cref="FlamesFull"/> s). <see cref="Sag"/> drops
    /// the burnt body that far (m) and tilts it. <see cref="Panels"/> body panels thrown from <see cref="PanelAt"/> (the last
    /// <see cref="TankPanels"/> of them at stage 2, the rest at stage 1), burning when <see cref="PanelsBurn"/>. <see cref="Extra"/>: the weapon's cook-off pops. <see cref="Cue"/>: stage 1's sound.
    /// </summary>
    public sealed record Plan(int FlamingWheels, float FireAt, float FireSize, float FireDelay, float Sag, float Pitch, float Roll, Blast[] Blasts, int Panels, float PanelAt,
        bool Calm = false, Blast[]? First = null, float TankAt = 0, float TankSize = 0, float TankSmoke = 0, float TankY = 0.5f, Throw Throw = default,
        float Smoke = 1f, float Spread = 1f, float FlamesFull = 10f, float FlameLevel = 1f, bool PanelsBurn = false, string? Cue = null, RollBlast Roll2 = RollBlast.Blast, float Debris = 1f, int TankPanels = 0)
    {
        public static readonly Plan Plain = new(int.MaxValue, 0, 1, 0, 0, 0, 0, [], 0, 0);
        /// <summary>The catastrophic award.</summary>
        public bool Award => !Calm && this != Plain;
    }

    /// <summary>How the sim's effect-roll explosions (x1_cars1, about 25 a death) are drawn: a 3 m blast (off, as before), a flare, a cook-off pop, or the award's small blast.</summary>
    public enum RollBlast { Blast, Flare, Pop, Small }

    /// <summary>A number in [0, 1) for draw <paramref name="k"/> of a death's seed.</summary>
    public static float U(ushort seed, int k)
    {
        uint x = seed * 0x9e3779b1u + (uint)k * 0x85ebca77u;
        x ^= x >> 15; x *= 0x2c1b3c6du; x ^= x >> 12; x *= 0x297a2d39u; x ^= x >> 15;
        return (x >> 8) / 16777216f;
    }

    /// <summary>One cause's numbers (design: _scratch/r3-death-2/design.md). Positions are fractions of the body's half sizes, game axes.</summary>
    public sealed record CauseTuning(Blast[] First, float Delay, float Tank, float TankY, Throw Throw, float FireAt, float FireSize, float Smoke,
        float Sag = 0, float SagPitch = 0, float SagRoll = 0, int Panels = 0, float PanelAt = 0, bool PanelsBurn = false, int FlamingWheels = 0, string? Cue = null, float Spread = 1f);

    /// <summary>
    /// Every cause's death, one row each: stage 1, the delay to the tank, its size and height, the drawn throw, the fire and
    /// its smoke, the sag, the panels, the burning wheels, stage 1's sound. "side" and "end" in the throw and blasts are
    /// drawn per death (a corner); <see cref="Build"/> applies them.
    /// </summary>
    public static readonly IReadOnlyDictionary<DeathCause, CauseTuning> Table = new Dictionary<DeathCause, CauseTuning>
    {
        // a fireball out of the hood, the hood thrown burning; the tank 250 ms later; the body slammed down and back
        [DeathCause.Engine] = new([new(0, 0, 0.7f, 0.85f, 2.2f, BlastKind.Fire, 0.3f)], 0.25f, 3.4f, 0.5f,
            new(ThrowKind.DownBack, AtTank: true, Back: 4f, Pitch: 0.2f, Friction: 5f, Dip: 0.3f), 0.55f, 1.0f, 0.5f, Panels: 1, PanelAt: 0.7f, PanelsBurn: true, Cue: "vtcoll.wav"),
        // a burst low at the failed corner; the wreck slides to that side, yawing; its first thrown wheel burns
        [DeathCause.Wheel] = new([new(0, 0.95f, 0.15f, 0.75f, 1.4f, BlastKind.Pop)], 0.35f, 3.0f, 0.5f,
            new(ThrowKind.Slide, Right: 4.5f, Yaw: 0.9f, Friction: 4f), 0, 0.9f, 0.5f, Sag: 0.15f, SagPitch: -0.08f, SagRoll: 0.1f, FlamingWheels: 1, Cue: "tblow.wav"),
        // sparks and a small pop at a corner; the wreck slides on and spins out
        [DeathCause.Brakes] = new([new(0, 0.95f, 0.1f, 0.75f, 0.9f, BlastKind.Sparks)], 0.45f, 3.0f, 0.5f,
            new(ThrowKind.Slide, Back: -4f, Yaw: 1.4f, Friction: 5f), 0, 0.9f, 0.5f, Sag: 0.2f, SagPitch: -0.12f, SagRoll: 0.12f, Cue: "tskid1.wav"),
        // a crunch under the body (dust, sparks); the tank low; the body drops onto its frame
        [DeathCause.Suspension] = new([new(0, 0, 0, 0, 2.5f, BlastKind.Dust), new(0.02f, 0, 0.05f, 0, 0.8f, BlastKind.Sparks)], 0.5f, 2.8f, 0.3f,
            new(ThrowKind.Drop, Dip: 0.35f), 0, 0.85f, 0.5f, Sag: 0.35f, SagRoll: 0.16f, Cue: "vland.wav"),
        // the first cook-off pop at a weapon; the tank; the wreck launched a little; more pops after (Build)
        [DeathCause.Weapon] = new([new(0, 0.5f, 0.9f, 0.3f, 1.0f, BlastKind.Pop)], 0.4f, 3.2f, 0.5f,
            new(ThrowKind.Launch, AtTank: true, Up: 6f, Right: 1.5f, Roll: 6.2832f, Friction: 6f), 0, 1, 0.55f),
        // a blast at the rear; the special's death is always the award
        [DeathCause.Special] = new([new(0, 0, 0.5f, -0.8f, 2.6f, BlastKind.Fire, 0.3f)], 0.2f, 3.4f, 0.5f,
            new(ThrowKind.Launch, AtTank: true, Up: 10f, Back: 2f, Roll: 6.2832f, Yaw: 1.2f), -0.45f, 1.2f, 0.6f, Panels: 2, PanelAt: -0.6f, PanelsBurn: true),
        // the original's: no blast, the horn stuck on; a small fire takes the engine bay some seconds later, thin smoke
        [DeathCause.Driver] = new([], 0, 0, 0, default, 0.5f, 0.6f, 0.25f),
        // the landing: dust thrown out low and wide; the tank; the body slammed down, a panel off
        [DeathCause.Fall] = new([new(0, 0, 0, 0, 5f, BlastKind.Dust)], 0.3f, 3.2f, 0.5f,
            new(ThrowKind.Slam, Dip: 0.35f), 0, 1.1f, 0.5f, Sag: 0.15f, Panels: 1, Cue: "vland.wav"),
        // on its roof: a burst from the underside; the tank; the sim's tumble; a wide fire, its flames spilling out
        [DeathCause.Rollover] = new([new(0, 0, 0, 0, 1.6f, BlastKind.Fire, 0.2f)], 0.4f, 3.2f, 0.5f,
            default, 0, 1.6f, 0.6f, Spread: 2f),
        // the self-destruct key, a script's destroy, a departing player: one blast at the moment of death, the sim's kick
        [DeathCause.SelfDestruct] = new([], 0, 3.6f, 0.5f, default, 0, 1, 0.5f),
        [DeathCause.Destroyed] = new([], 0, 3.6f, 0.5f, default, 0, 1, 0.5f),
    };

    /// <summary>
    /// The catastrophic award's numbers: the tank's blast and smoke, the launch (m/s up and back, a full roll and a sixth of a
    /// turn of yaw, so it lands the way up it left), the panels thrown burning, every thrown wheel burning (as many as the wreck throws), the fire's size, its
    /// full-flame time and smoke, the delay taken off stage 2 and its floor.
    /// </summary>
    public static class Award
    {
        public const float Tank = 7.5f, TankSmoke = 1f, Up = 10f, Back = 2f, Roll = 6.2832f, Yaw = 1.2f, FireSize = 1.4f, FlamesFull = 20f, Smoke = 1f;
        public const float Sooner = 0.1f, MinDelay = 0.15f;
        public const int Panels = 3, Wheels = 6;
        /// <summary>One death in this many of the ordinary causes (drawn from the seed); every special kill.</summary>
        public const float Odds = 0.125f;
    }

    /// <summary>The effect roll's blasts: a flare's radius, a pop's, the award's small blast and its smoke; the share of the roll's chunks drawn in an ordinary death and their speed.</summary>
    public const float FlareSize = 1.2f, PopSize = 0.9f, SmallSize = 2f, SmallSmoke = 0.33f, DebrisShare = 0.5f, DebrisSpeed = 0.6f;
    /// <summary>The wreck fire's flames at stage 2 (a fraction of full) and the seconds to full; the smoke's start after stage 2.</summary>
    public const float FlameStart = 0.35f, FlameRamp = 3f, SmokeAfter = 0.6f;
    /// <summary>The driver's fire: its flames' strength, the delay range (s).</summary>
    public const float DriverFlames = 0.6f, DriverDelayMin = 3f, DriverDelayRange = 4f;

    /// <summary>The plan for a death; <see cref="Plan.Plain"/> when the variety is off.</summary>
    public static Plan For(DeathCause cause, ushort seed, bool enabled = true)
    {
        if (!enabled) return Plan.Plain;
        return Build(cause, seed, Catastrophic(cause, seed));
    }

    /// <summary>Whether the modern look draws this death's opening blast and smoke for this car: the vehicle's own, not a gun emplacement or a helicopter.</summary>
    public static bool Owns(I76.Sim.Vehicles.Vehicle v, bool modernCar) => Enabled && modernCar && !v.IsEmplacement && !v.IsHeli;

    /// <summary>The catastrophic award: every special kill and one death in eight of the others (not the driver's: no blast), drawn from the death's seed.</summary>
    public static bool Catastrophic(DeathCause cause, ushort seed) => cause == DeathCause.Special || cause != DeathCause.Driver && U(seed, 50) < Award.Odds;

    /// <summary>How many blasts a plan draws as the tank's, its own (stage 1 and the cook-off apart): one, two in none.</summary>
    public static int TankBlasts(Plan p) => p.TankSize > 0 ? 1 : 0;

    private static Plan Build(DeathCause cause, ushort seed, bool award)
    {
        var t = Table.TryGetValue(cause, out var row) ? row : Table[DeathCause.Destroyed];
        float side = U(seed, 1) < 0.5f ? -1 : 1, end = U(seed, 2) < 0.5f ? -1 : 1;
        // the corner: stage 1's blasts at the drawn side and end (a wheel's, a brake's), the slide toward that side
        var first = t.First.Select(b => b with { X = b.X * side, Z = cause is DeathCause.Wheel or DeathCause.Brakes ? b.Z * end : b.Z }).ToArray();
        var th = t.Throw with { Right = t.Throw.Right * side, Yaw = t.Throw.Yaw * side, Roll = t.Throw.Roll * side };
        float delay = t.Delay;
        var extra = System.Array.Empty<Blast>();
        if (cause == DeathCause.Weapon)
        {
            // the ammunition cooks off: three to five more pops over a few seconds round the body
            int n = 3 + (int)(U(seed, 5) * 3);
            extra = new Blast[n];
            for (int i = 0; i < n; i++) extra[i] = new(1.2f + i * 0.75f + U(seed, 10 + i) * 0.4f, (U(seed, 20 + i) * 2 - 1) * 0.8f, 0.9f, (U(seed, 30 + i) * 2 - 1) * 0.8f, 0.8f + U(seed, 40 + i) * 0.7f, BlastKind.Pop);
        }
        float fireDelay = cause == DeathCause.Driver ? DriverDelayMin + U(seed, 7) * DriverDelayRange : 0;
        var plan = new Plan(t.FlamingWheels, t.FireAt, t.FireSize, fireDelay, t.Sag, t.SagPitch * end, t.SagRoll * side, extra, t.Panels, t.PanelAt,
            Calm: true, First: first, TankAt: delay, TankSize: t.Tank, TankSmoke: 0.5f, TankY: t.TankY, Throw: th, Smoke: t.Smoke, Spread: t.Spread,
            FlameLevel: cause == DeathCause.Driver ? DriverFlames : 1f, PanelsBurn: t.PanelsBurn, Cue: t.Cue,
            Roll2: cause == DeathCause.Weapon ? RollBlast.Pop : RollBlast.Flare, Debris: DebrisShare);
        if (!award) return plan;
        // the award: a big tank sooner, the body launched and tumbling, panels and wheels thrown burning, a bigger, longer fire
        return plan with
        {
            Calm = false, TankAt = System.MathF.Max(Award.MinDelay, delay - Award.Sooner), TankSize = Award.Tank, TankSmoke = Award.TankSmoke,
            Throw = new(ThrowKind.Launch, AtTank: true, Up: Award.Up, Back: Award.Back, Roll: Award.Roll * side, Yaw: Award.Yaw * end, Friction: 6f),
            Panels = System.Math.Max(t.Panels, Award.Panels), TankPanels = System.Math.Max(0, Award.Panels - t.Panels), PanelAt = t.Panels > 0 ? t.PanelAt : 0, PanelsBurn = true, FlamingWheels = Award.Wheels,
            FireSize = System.MathF.Max(t.FireSize, Award.FireSize), FlamesFull = Award.FlamesFull, Smoke = Award.Smoke, Roll2 = cause == DeathCause.Weapon ? RollBlast.Pop : RollBlast.Small, Debris = 1f,
        };
    }
}
