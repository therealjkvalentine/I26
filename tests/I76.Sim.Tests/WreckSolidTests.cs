using System.Globalization;
using System.Numerics;
using I76.Formats.Bwd2;
using I76.Formats.Vfs;
using I76.Sim.Damage;
using I76.Sim.Math;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// When a destroyed car stops the player's car and when it does not. The original (static reading of the vehicle's state
/// update at 0x465370, the 1998 build): a computer car's death starts a 10 s timer; while it runs the car is still in the
/// movers' collision list, so it is hit, pushed and rammed as a live car is. At the timer's end, outside a melee, the
/// routine at 0x434870 takes its collider out of that list (0x46555c): from then on nothing collides with the wreck, cars
/// and rounds alike, though it is still drawn. The timer is the car's shared one: an oil slick met while dying resets it to
/// 2 s with flag 0x400, the end then is not the wreck's (0x8c08 != 0x8000, 0x465494) and the collider is never unlinked.
/// The sweeps drive the player's car at wrecks of every age, from every side, at several speeds, and count the contacts.
///
/// The port's rule (<see cref="SimWorld.WrecksStaySolid"/>, docs/deviations.md "Wrecks stay solid"; off, everything above):
/// after its timer a computer car's wreck goes on stopping the humans' cars, standing where it is as scenery does, and
/// stops rounds; a computer car still passes through it. The same sweeps with the rule on, a human's car and a computer's.
/// </summary>
public class WreckSolidTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const float Dt = 102f / 2048f;

    private static string? InstallDir =>
        Environment.GetEnvironmentVariable("I76_INSTALL") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    /// <summary>Set to a folder to have the sweeps write their rows there (wreck-sweep-*.csv).</summary>
    private static string? OutDir =>
        Environment.GetEnvironmentVariable("I76_WRECK_SWEEP_OUT") is { Length: > 0 } d && Directory.Exists(d) ? d : null;

    private sealed class Flat : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    private sealed class Raised : IGround
    {
        public (float Height, Vector3 Normal) Query(double x, double y, double z) => (z > 15 ? 0.3f : 0f, Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    /// <summary>How the car died.</summary>
    public enum Death
    {
        /// <summary>The script's destroy, the 35 m fall, the rollover: the death routine with no packet.</summary>
        Destroyed,
        /// <summary>The same at 25 m/s: the wreck rolls on, braking.</summary>
        Moving,
        /// <summary>The .45's kill (0x4020): no explosion, no kick, the same 10 s.</summary>
        Driver,
        /// <summary>Destroyed, then an oil slick under it 1 s later: the shared timer restarts at 2 s and its end is not the wreck's.</summary>
        Oiled,
        /// <summary>Rounds until the kill roll succeeds (cars built from the install only).</summary>
        Shot,
    }

    /// <param name="Solid">The port's rule is on.</param>
    /// <param name="Human">The car driven at the wreck is the player's; else a computer's (the player's stands by).</param>
    public sealed record Case(string Body, Death Death, float LaunchAge, int Heading, float Speed, bool Solid = false, bool Human = true);

    /// <param name="Result">"contact", "through" (crossed the wreck's place untouched), "missed" (the wreck was not there:
    /// in the air or rolled away), "short" (never got there) or, with the rule on, "pushed-through" (met the wreck, then got
    /// inside its box while driven at it for 4 s more).</param>
    public sealed record Outcome(Case Case, string Result, bool Unlinked, float Age, float WreckHeight, float Apart, float UpY);

    private static Vehicle Synthetic(float halfWidth, float halfLength, float height, float mass)
    {
        var v = new Vehicle();
        foreach (var (w, x, wz) in new[] { (v.FrontRight, 1f, 0.65f), (v.FrontLeft, -1f, 0.65f), (v.RearRight, 1f, -0.62f), (v.RearLeft, -1f, -0.62f) })
        {
            w.Grip = 0.5f; w.Rolling = 1f; w.Offset = new Vector3(x * (halfWidth - 0.1f), 0.33f, wz * halfLength);
            w.InitSuspension();
        }
        v.P.Wheelbase = halfLength * 1.3f; v.P.Track = (halfWidth - 0.1f) * 2; v.P.Mass = mass;
        v.BoxX = [-halfWidth, -halfWidth * 0.55f, halfWidth * 0.55f, halfWidth];
        v.BoxY = [0, 0.3f, height * 0.7f, height];
        v.BoxZ = [-halfLength, -halfLength * 0.5f, halfLength * 0.5f, halfLength];
        return v;
    }

    private static readonly (string Name, Func<Vehicle> Make)[] SyntheticBodies =
    [
        ("car", () => Synthetic(0.9f, 2.1f, 1.4f, 1500)),
        ("small", () => Synthetic(0.75f, 1.6f, 1.2f, 900)),
        ("bus", () => Synthetic(1.3f, 5.2f, 3f, 6000)),
    ];

    private sealed class Rig
    {
        public required SimWorld World;
        public required Combat Combat;
        public required DamageModel Damage;
        public required Vehicle Player, Victim;
        public int Contacts;
        public float Age;
        public bool Killed;

        public void Frame()
        {
            World.Frame(Dt);
            Combat.Frame(Dt);
            if (Killed) Age += Dt;
            Damage.Events.Clear();
            Combat.Events.Clear();
            World.Step.CarHits.Clear(); World.Step.Impacts.Clear(); World.Step.ObjectHits.Clear(); World.Step.Landings.Clear();
        }
    }

    private static Rig Build(Vehicle player, Vehicle victim, int seed, bool solid = false, bool human = true)
    {
        var world = new SimWorld(new Flat(), new CrtRand((uint)seed)) { WrecksStaySolid = solid };
        player.Pos = new Double3(150, 0, 0);
        victim.Pos = new Double3(0, 0, 0);
        world.Vehicles.Add(player); world.Vehicles.Add(victim);
        if (human) world.Player = player;
        else
        {
            // the car driven is a computer's: the player's own stands by, near enough to keep both on the near path
            var own = Synthetic(0.9f, 2.1f, 1.4f, 1500);
            own.Pos = new Double3(150, 0, 60);
            world.Vehicles.Add(own);
            world.Player = own;
        }
        var combat = new Combat(world, _ => null);
        var damage = new DamageModel(world, combat);
        var rig = new Rig { World = world, Combat = combat, Damage = damage, Player = player, Victim = victim };
        var inner = world.CarContact;
        world.CarContact = (a, b, rec) => { if (a == player && b == victim) rig.Contacts++; inner?.Invoke(a, b, rec); };
        var wreck = world.WreckContact;
        world.WreckContact = (a, b, rec) => { if (a == player && b == victim) rig.Contacts++; wreck?.Invoke(a, b, rec); };
        return rig;
    }

    /// <summary>The car's centre is inside the wreck's box, 0.3 m in from its sides (the wreck in the car's way, not over it).</summary>
    private static bool Inside(Vehicle car, Vehicle wreck)
    {
        if (Lowest(wreck) >= (float)car.Pos.Y + (car.BoxY[1] + car.BoxY[3]) / 2) return false;
        var l = wreck.Rot.ToLocal(car.Pos.ToVector3() - wreck.Pos.ToVector3());
        return l.X > wreck.BoxX[0] + 0.3f && l.X < wreck.BoxX[3] - 0.3f && l.Z > wreck.BoxZ[0] + 0.3f && l.Z < wreck.BoxZ[3] - 0.3f
            && l.Y > wreck.BoxY[1] - 1f && l.Y < wreck.BoxY[3];
    }

    private static void Kill(Rig rig, Death how)
    {
        var v = rig.Victim;
        switch (how)
        {
            case Death.Moving:
                v.Vel = v.Rot.Forward * 25f; v.Speed = 25f;
                rig.Damage.Destroy(v);
                break;
            case Death.Driver:
                v.Flags |= (VehicleFlags)0x4020;
                rig.Damage.VehicleSlot(v, null, Vector3.Zero);
                break;
            case Death.Shot:
                foreach (var part in v.Health!.Slots) if (part is { Hp: > 1 }) part.Hp = 1;
                for (int z = 0; z < 4; z++) v.Health.Armour[z] = v.Health.Chassis[z] = v.Health.ArmourMirror[z] = v.Health.ChassisMirror[z] = 0;
                for (int i = 0; i < 2000 && !DamageModel.IsDead(v); i++)
                {
                    var round = new DamagePacket { Attacker = rig.Player, Mask = 1, Kind = DamageKind.Round };
                    round.Ch[0] = 10;
                    rig.Damage.VehicleSlot(v, round, new Vector3(1.5f, 0.5f, 0));
                }
                break;
            default:
                rig.Damage.Destroy(v);
                break;
        }
        Assert.True(DamageModel.IsDead(v), $"{how}: the car did not die");
        rig.Killed = true;
    }

    private static float Reach(Vehicle v) => new Vector2(MathF.Max(-v.BoxX[0], v.BoxX[3]), MathF.Max(-v.BoxZ[0], v.BoxZ[3])).Length();

    /// <summary>The height of the lowest corner of the car's collision box (x0..x3, y1..y3, z0..z3).</summary>
    private static float Lowest(Vehicle v)
    {
        float low = float.MaxValue;
        foreach (float x in new[] { v.BoxX[0], v.BoxX[3] })
            foreach (float y in new[] { v.BoxY[1], v.BoxY[3] })
                foreach (float z in new[] { v.BoxZ[0], v.BoxZ[3] })
                    low = MathF.Min(low, (float)v.Pos.Y + v.Rot.ToWorld(new Vector3(x, y, z)).Y);
        return low;
    }

    /// <summary>The wreck made and aged, then the player's car sent at where it lies; what happened when it got there.</summary>
    private static Outcome Run(Case c, Vehicle player, Vehicle victim, Action<Rig>? done = null)
    {
        var rig = Build(player, victim, seed: 1 + c.Heading + (int)(c.LaunchAge * 10), c.Solid, c.Human);
        for (int i = 0; i < 20; i++) rig.Frame();     // both settle on their wheels
        Kill(rig, c.Death);
        bool oiled = false;
        while (rig.Age < c.LaunchAge)
        {
            rig.Frame();
            if (c.Death == Death.Oiled && !oiled && rig.Age >= 1f) { Combat.Oil(victim); oiled = true; }
        }

        // the player's car on the ground, pointed at the wreck from the case's side, already at speed
        float a = c.Heading * MathF.PI / 4;
        var dir = new Vector3(MathF.Sin(a), 0, MathF.Cos(a));
        var target = victim.Pos.ToVector3();
        float gap = Reach(player) + Reach(victim) + 6f;
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, dir));
        player.Rot = new Rows(right, Vector3.UnitY, Vector3.Cross(right, Vector3.UnitY));
        var start = new Vector3(target.X, 0, target.Z) - dir * gap;
        player.Pos = new Double3(start.X, player.Pos.Y, start.Z);
        player.Vel = dir * c.Speed; player.Speed = c.Speed; player.AngVel = Vector3.Zero;
        player.Throttle = 0; player.Steer = 0;
        player.Rec.Clear();
        rig.Contacts = 0;

        int limit = (int)((gap + 12f) / (c.Speed * Dt)) + 40;
        string result = "short";
        for (int i = 0; i < limit; i++)
        {
            bool unlinked = victim.Wreck;
            float height = (float)victim.Pos.Y;
            rig.Frame();
            var rel = player.Pos.ToVector3() - victim.Pos.ToVector3();
            float apart = new Vector2(rel.X, rel.Z).Length();
            if (rig.Contacts > 0)
            {
                string met = "contact";
                if (c.Solid && c.Human && unlinked)
                {
                    // the rule on: driven at the wreck for 4 s more, at 8 m/s whenever it has been thrown back, the car never gets inside it
                    for (int k = 0; k < 80 && met == "contact"; k++)
                    {
                        var to = victim.Pos.ToVector3() - player.Pos.ToVector3();
                        to.Y = 0;
                        if (to.LengthSquared() > 1e-4f && Vector3.Dot(player.Vel, Vector3.Normalize(to)) < 3f) { player.Vel = Vector3.Normalize(to) * 8f; player.Speed = 8f; }
                        rig.Frame();
                        if (Inside(player, victim)) met = "pushed-through";
                    }
                }
                done?.Invoke(rig);
                return new Outcome(c, met, unlinked, rig.Age, height, apart, victim.Rot.Up.Y);
            }
            float along = Vector3.Dot(player.Pos.ToVector3() - new Vector3(target.X, 0, target.Z), dir);
            if (along < 0) continue;
            // level with where the wreck was when it set off: was the wreck still there, and on the ground?
            var moved = victim.Pos.ToVector3() - target;
            // (its box's lowest corner below the height the car's box is sampled at: not over the car, thrown up by the death's kick)
            bool there = new Vector2(moved.X, moved.Z).Length() < 0.5f && Lowest(victim) < (float)player.Pos.Y + (player.BoxY[1] + player.BoxY[3]) / 2;
            result = there ? "through" : "missed";
            done?.Invoke(rig);
            return new Outcome(c, result, unlinked, rig.Age, height, apart, victim.Rot.Up.Y);
        }
        return new Outcome(c, result, victim.Wreck, rig.Age, (float)victim.Pos.Y, 0, victim.Rot.Up.Y);
    }

    private void Report(string name, IReadOnlyList<Outcome> rows)
    {
        string F(float f) => f.ToString("0.00", CultureInfo.InvariantCulture);
        foreach (var g in rows.GroupBy(r => (r.Unlinked, r.Result)).OrderBy(g => g.Key.Unlinked).ThenBy(g => g.Key.Result))
            output.WriteLine($"{name}: collider {(g.Key.Unlinked ? "unlinked" : "linked")}, {g.Key.Result}: {g.Count()} of {rows.Count}");
        foreach (var g in rows.GroupBy(r => r.Case.Death).OrderBy(g => g.Key))
            output.WriteLine($"{name}: {g.Key}: {g.Count(r => r.Result == "contact")} contact, {g.Count(r => r.Result == "through")} through, {g.Count(r => r.Result == "missed")} missed, {g.Count(r => r.Result == "short")} short; unlinked {g.Count(r => r.Unlinked)}");
        if (OutDir is not { } dir) return;
        bool solid = rows.Any(r => r.Case.Solid);
        foreach (var g in rows.Where(r => r.Case.Solid).GroupBy(r => (r.Case.Human, r.Unlinked, r.Result)).OrderBy(g => g.Key))
            output.WriteLine($"{name}: rule on, {(g.Key.Human ? "a human's car" : "a computer's car")}, collider {(g.Key.Unlinked ? "unlinked" : "linked")}, {g.Key.Result}: {g.Count()}");
        var lines = new List<string> { "body,death,launch_age,heading_deg,speed,result,collider_unlinked,age,wreck_height,apart,wreck_up_y" + (solid ? ",rule,driver" : "") };
        lines.AddRange(rows.Select(r => string.Join(',', r.Case.Body, r.Case.Death, F(r.Case.LaunchAge), r.Case.Heading * 45, F(r.Case.Speed), r.Result, r.Unlinked ? 1 : 0, F(r.Age), F(r.WreckHeight), F(r.Apart), F(r.UpY))
            + (solid ? $",{(r.Case.Solid ? "drawn" : "original")},{(r.Case.Human ? "human" : "computer")}" : "")));
        File.WriteAllLines(Path.Combine(dir, $"wreck-sweep-{name}.csv"), lines);
    }

    /// <summary>The rule, on every row of a sweep.</summary>
    private static void AssertTheOriginalsRule(IReadOnlyList<Outcome> rows)
    {
        // a wreck whose collider is still linked and that is where the car drives stops it, every time
        var leaks = rows.Where(r => !r.Unlinked && r.Result == "through").ToList();
        Assert.True(leaks.Count == 0, $"{leaks.Count} drives went through a wreck whose collider was linked, e.g. {leaks.FirstOrDefault()}");
        // and once the collider is unlinked nothing touches it
        var hits = rows.Where(r => r.Unlinked && r.Result == "contact").ToList();
        Assert.True(hits.Count == 0, $"{hits.Count} drives hit a wreck whose collider was unlinked, e.g. {hits.FirstOrDefault()}");
        Assert.DoesNotContain(rows, r => r.Result == "short");
        // the collider is unlinked exactly at the wreck timer's end, 10 s after the death, unless the oil slick took the timer
        Assert.All(rows.Where(r => r.Case.Death != Death.Oiled && MathF.Abs(r.Age - 10f) > 0.2f), r => Assert.Equal(r.Age > 10f, r.Unlinked));
        Assert.All(rows.Where(r => r.Case.Death == Death.Oiled), r => Assert.False(r.Unlinked));
    }

    /// <summary>The port's rule, on every row of a sweep run with it on.</summary>
    private static void AssertWrecksStaySolid(IReadOnlyList<Outcome> rows)
    {
        Assert.All(rows, r => Assert.True(r.Case.Solid));
        Assert.DoesNotContain(rows, r => r.Result == "short");
        // a human's car is stopped by a wreck in its way whatever the wreck's age, and never gets into it
        var leaks = rows.Where(r => r.Case.Human && r.Result is "through" or "pushed-through").ToList();
        Assert.True(leaks.Count == 0, $"{leaks.Count} drives of a human's car went through a wreck with the rule on, e.g. {leaks.FirstOrDefault()}");
        // a computer's car passes through a wreck whose collider is unlinked, as in the original, and meets a linked one as ever
        var hits = rows.Where(r => !r.Case.Human && r.Unlinked && r.Result == "contact").ToList();
        Assert.True(hits.Count == 0, $"{hits.Count} drives of a computer's car hit an unlinked wreck, e.g. {hits.FirstOrDefault()}");
        var blocked = rows.Where(r => !r.Case.Human && !r.Unlinked && r.Result == "through").ToList();
        Assert.True(blocked.Count == 0, $"{blocked.Count} drives of a computer's car went through a linked wreck, e.g. {blocked.FirstOrDefault()}");
        Assert.Contains(rows, r => r.Case.Human && r.Unlinked && r.Result == "contact");
        Assert.Contains(rows, r => !r.Case.Human && r.Unlinked && r.Result == "through");
    }

    private static readonly float[] LaunchAges = [0.2f, 2f, 5f, 7.5f, 11f, 15f, 40f];
    private static readonly float[] Speeds = [5f, 15f, 30f, 60f];

    [Fact]
    public void TheSweepOverBuiltBodies()
    {
        var rows = new List<Outcome>();
        foreach (var (name, make) in SyntheticBodies)
            // the driver's death alone: it leaves the car on its wheels. A body built here has no ride offset, so thrown onto
            // its roof by the explosion's kick its box lies under the ground (the install's cars, below, take every death)
            foreach (var death in new[] { Death.Driver })
                foreach (float age in LaunchAges)
                    for (int h = 0; h < 8; h++)
                        foreach (float s in Speeds)
                        {
                            var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
                            var victim = make();
                            victim.Health = new VehicleDamage(victim, false);
                            rows.Add(Run(new Case(name, death, age, h, s), player, victim));
                        }
        Report("built", rows);
        AssertTheOriginalsRule(rows);
        Assert.Contains(rows, r => r.Unlinked && r.Result == "through");
        Assert.Contains(rows, r => !r.Unlinked && r.Result == "contact");
    }

    [Fact]
    public void TheSweepOverBuiltBodiesWithWrecksStayingSolid()
    {
        var rows = new List<Outcome>();
        foreach (var (name, make) in SyntheticBodies)
            foreach (bool human in new[] { true, false })
                foreach (float age in new[] { 5f, 11f, 15f, 40f })
                    for (int h = 0; h < 8; h++)
                        foreach (float s in Speeds)
                        {
                            var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
                            var victim = make();
                            victim.Health = new VehicleDamage(victim, false);
                            rows.Add(Run(new Case(name, Death.Driver, age, h, s, Solid: true, Human: human), player, victim));
                        }
        Report("built-solid", rows);
        AssertWrecksStaySolid(rows);
        // on its wheels where it died, every wreck is in the way: a human's car meets each one
        Assert.All(rows.Where(r => r.Case.Human), r => Assert.Equal("contact", r.Result));
    }

    /// <summary>Every car file of the install as the wreck, the rule on: the Piranha as the player's car and as a computer's.</summary>
    [Fact]
    public void TheSweepOverEveryCarOfTheInstallWithWrecksStayingSolid()
    {
        if (InstallDir is null) return;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        var rows = new List<Outcome>();
        foreach (var vcf in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            Vehicle probe;
            try { probe = VehicleFactory.Create(vfs, vcf); }
            catch (Exception) { continue; }
            if (probe.IsHeli || probe.IsEmplacement || probe.ObjectClass != 1 || probe.Build is null) continue;
            foreach (var death in new[] { Death.Destroyed, Death.Driver, Death.Shot })
                foreach (bool human in new[] { true, false })
                    foreach (float age in new[] { 7.5f, 11f, 30f })
                        for (int h = 0; h < 8; h += 2)
                            foreach (float s in new[] { 10f, 40f })
                            {
                                var player = VehicleFactory.Create(vfs, "vppirna1.vcf");
                                var victim = VehicleFactory.Create(vfs, vcf);
                                victim.Health = VehicleDamage.Create(victim, victim.Build!, false, new CrtRand(7));
                                rows.Add(Run(new Case(vcf, death, age, h + (death == Death.Driver ? 1 : 0), s, Solid: true, Human: human), player, victim));
                            }
        }
        output.WriteLine($"{rows.Select(r => r.Case.Body).Distinct().Count()} car files");
        Report("install-solid", rows);
        AssertWrecksStaySolid(rows);
    }

    /// <summary>Every car file of the install as the wreck, the player's car the Piranha.</summary>
    [Fact]
    public void TheSweepOverEveryCarOfTheInstall()
    {
        if (InstallDir is null) return;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        var rows = new List<Outcome>();
        var skipped = new List<string>();
        foreach (var vcf in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            Vehicle Make()
            {
                var v = VehicleFactory.Create(vfs, vcf);
                return v;
            }
            Vehicle probe;
            try { probe = Make(); }
            catch (Exception e) { skipped.Add($"{vcf} ({e.GetType().Name})"); continue; }
            if (probe.IsHeli || probe.IsEmplacement || probe.ObjectClass != 1 || probe.Build is null) { skipped.Add($"{vcf} (class {probe.ObjectClass})"); continue; }
            foreach (var death in new[] { Death.Destroyed, Death.Driver, Death.Shot, Death.Moving, Death.Oiled })
                foreach (float age in new[] { 3f, 7.5f, 11f, 30f })
                    for (int h = 0; h < 8; h += 2)
                        foreach (float s in new[] { 10f, 40f })
                        {
                            var player = VehicleFactory.Create(vfs, "vppirna1.vcf");
                            var victim = Make();
                            victim.Health = VehicleDamage.Create(victim, victim.Build!, false, new CrtRand(7));
                            rows.Add(Run(new Case(vcf, death, age, h + (death == Death.Driver ? 1 : 0), s), player, victim));
                        }
        }
        output.WriteLine($"{rows.Select(r => r.Case.Body).Distinct().Count()} car files; not cars: {string.Join(", ", skipped)}");
        Report("install", rows);
        AssertTheOriginalsRule(rows);
    }

    [Fact]
    public void AWreckStopsTheCarWhileItsTimerRuns()
    {
        var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        var victim = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        victim.Health = new VehicleDamage(victim, false);
        var o = Run(new Case("car", Death.Destroyed, 5f, 0, 30f), player, victim);
        output.WriteLine(o.ToString());
        Assert.Equal("contact", o.Result);
        Assert.False(victim.Wreck);
        Assert.True(o.Age < 10f);
        // thrown back off it: the car does not carry on at its speed
        Assert.True(Vector3.Dot(player.Vel, Vector3.UnitZ) < 15f, $"the car kept {player.Vel}");
    }

    [Fact]
    public void AfterItsTimerAWreckIsOutOfTheCollisionsAsInTheOriginal()
    {
        var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        var victim = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        victim.Health = new VehicleDamage(victim, false);
        var o = Run(new Case("car", Death.Destroyed, 11f, 0, 30f), player, victim);
        output.WriteLine(o.ToString());
        Assert.True(victim.Wreck);
        Assert.Equal("through", o.Result);
        Assert.True(Vector3.Dot(player.Vel, Vector3.UnitZ) > 25f, $"the car lost its speed: {player.Vel}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithTheRuleOnAnOldWreckStopsAHumansCarAndNotAComputers(bool human)
    {
        var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        var victim = Synthetic(0.9f, 2.1f, 1.4f, 1500);
        victim.Health = new VehicleDamage(victim, false);
        var o = Run(new Case("car", Death.Driver, 11f, 0, 30f, Solid: true, Human: human), player, victim);
        output.WriteLine(o.ToString());
        Assert.True(victim.Wreck);
        Assert.Equal(human ? "contact" : "through", o.Result);
        if (human) Assert.True(player.Pos.Z < victim.Pos.Z, $"the car is past the wreck: {player.Pos.Z} against {victim.Pos.Z}");
        else Assert.True(Vector3.Dot(player.Vel, Vector3.UnitZ) > 25f, $"the computer's car lost its speed: {player.Vel}");
        // the wreck stands: it is scenery now
        Assert.Equal(Vector3.Zero, victim.Vel);
    }

    [Fact]
    public void TheCarIsDamagedByTheWreckAsByScenery()
    {
        if (InstallDir is null) return;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        var player = VehicleFactory.Create(vfs, "vppirna1.vcf");
        player.Health = VehicleDamage.Create(player, player.Build!, true, new CrtRand(3));
        var victim = VehicleFactory.Create(vfs, "vppirna1.vcf");
        victim.Health = VehicleDamage.Create(victim, victim.Build!, false, new CrtRand(7));
        int before = player.Health.Armour.Sum() + player.Health.Chassis.Sum();
        var o = Run(new Case("vppirna1.vcf", Death.Driver, 11f, 0, 30f, Solid: true), player, victim);
        Assert.Equal("contact", o.Result);
        Assert.True(player.Health.Armour.Sum() + player.Health.Chassis.Sum() < before, "the car took nothing from the wreck");
        Assert.False(DamageModel.IsDead(player));
    }

    /// <summary>The rule changes nothing it does not name: the same drive twice gives the same state, bit for bit, and the rand stream stands where it stood.</summary>
    [Fact]
    public void TheRuleIsDeterministic()
    {
        (Double3 Pos, Vector3 Vel, Rows Rot, int Rand)[] ends = new (Double3, Vector3, Rows, int)[2];
        for (int k = 0; k < 2; k++)
        {
            var player = Synthetic(0.9f, 2.1f, 1.4f, 1500);
            var victim = Synthetic(0.9f, 2.1f, 1.4f, 1500);
            victim.Health = new VehicleDamage(victim, false);
            int kk = k;
            Run(new Case("car", Death.Driver, 11f, 1, 30f, Solid: true), player, victim, rig => ends[kk] = (player.Pos, player.Vel, player.Rot, rig.World.Rand.Next()));
        }
        Assert.Equal(ends[0].Pos, ends[1].Pos);
        Assert.Equal(ends[0].Vel, ends[1].Vel);
        Assert.Equal(ends[0].Rot.Forward, ends[1].Rot.Forward);
        Assert.Equal(ends[0].Rand, ends[1].Rand);
    }

    /// <summary>Rounds: through an old wreck in the original, stopped by it with the rule on, as they are while its timer runs.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundsAreStoppedByAWreckThatStaysSolid(bool solid)
    {
        if (InstallDir is null) return;
        var vfs = new AssetVfs(new GameInstall(InstallDir));
        // (the ground a step higher under the wreck, as DamageTests has it: the Piranha's guns fire level, over a car on its own ground)
        var world = new SimWorld(new Raised(), new CrtRand(1)) { WrecksStaySolid = solid };
        var shooter = VehicleFactory.Create(vfs, "vppirna1.vcf");
        var target = VehicleFactory.Create(vfs, "vppirna1.vcf");
        target.Pos = new Double3(0, 0.3, 30);
        world.Vehicles.Add(shooter); world.Vehicles.Add(target);
        world.Player = shooter;
        foreach (var v in world.Vehicles) v.Health = VehicleDamage.Create(v, v.Build!, v == world.Player, world.Rand);
        var combat = new Combat(world, n => vfs.TryRead(n) is { } d ? EffectDef.Read(n, d) : null);
        var damage = new DamageModel(world, combat);
        target.Flags |= (VehicleFlags)0x4020;     // the driver's death: the car stays where it is
        damage.VehicleSlot(target, null, Vector3.Zero);
        for (int k = 0; k < 230; k++) { world.Frame(Dt); combat.Frame(Dt); }
        Assert.True(target.Wreck);
        int hits = 0;
        damage.Damaging += (v, p) => { if (v == target) hits++; };
        var a = shooter.Armory!;
        var keys = new bool[5];
        for (int i = 0; i < a.Weapons.Count; i++) keys[i] = a.Weapons[i].MountKind == 1 && a.Weapons[i].Def.Class == 2;
        for (int k = 0; k < 60; k++)
        {
            a.PlayerTriggers(false, keys);
            world.Frame(Dt);
            combat.Frame(Dt);
        }
        output.WriteLine($"rule {(solid ? "on" : "off")}: {hits} rounds met the wreck; {a.Weapons.Count} weapons, {keys.Count(k => k)} fired, wreck at {target.Pos}, up {target.Rot.Up}, events {string.Join(", ", combat.Events.GroupBy(e => e.GetType().Name).Select(g => $"{g.Key} {g.Count()}"))}");
        if (solid) Assert.True(hits > 0, "no round met the wreck");
        else Assert.Equal(0, hits);
    }
}
