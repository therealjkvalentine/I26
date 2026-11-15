using System.Numerics;
using I76.Net;
using I76.Server;
using I76.Sim.Damage;
using I76.Sim.Session;
using I76.Sim.Vehicles;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// The target box's health bar over the wire. The original's health percentage of a remote car in a network game is the step
/// its owner sends (100, 66.67 or 33.33), so the bar never shrinks below a third; read in full, a car whose brakes, engine or
/// suspension is scratched is below 1 % (the original's unscaled branch, kept offline) and its bar is a pixel or two wide.
/// </summary>
public class TargetHealthBarNetTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class Client
    {
        public required Seat Seat;
        public required ClientWorld Mirror;
        public readonly MeleeClient Net = new();
        public required OwnCarPredictor Predictor;
        public Vehicle Car => Mirror.Own!.Vehicle;
    }

    private static int Id(Vehicle? v) => v is null ? 0 : v.NetId & 0xffffff;

    /// <summary>
    /// Two clients, snapshots two ticks late: a targets b 25 m ahead; b's front armour is shot away, then the hits reach its
    /// brakes. a's bar follows the original's steps (whole, 66.67, 33.33) and stays a third wide with the brakes hit, where
    /// the full reading is 0.6 %. After b's death and respawn a's T takes it again, whole. a's own car reads in full.
    /// </summary>
    [Fact]
    public void ARemoteCarsBarKeepsTheOriginalsNetworkStepsWhenItsBrakesAreHit()
    {
        if (HostTests.InstallDir is not { } install) return;
        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m01" });
        var a = host.Join("a", "vppirna1.vcf", 0);
        var b = host.Join("b", "vcgcour1.vcf", 0);
        var clients = new List<Client>();
        Client Add(Seat s)
        {
            var m = new ClientWorld(HostTests.Mirror(host.Arena), host.AcceptFor(s));
            var c = new Client { Seat = s, Mirror = m, Predictor = null! };
            c.Predictor = new OwnCarPredictor(m, c.Net, predictFire: true);
            clients.Add(c);
            return c;
        }
        var ca = Add(a);
        var cb = Add(b);
        var wire = new Queue<(List<(Client, Snapshot)>, EventBatch?)>();
        uint T = 0;
        CarCommands aCmd = 0, bCmd = 0;
        void Tick()
        {
            T++;
            foreach (var c in clients)
            {
                var i = new CarInput { Commands = c == ca ? aCmd : bCmd };
                c.Seat.Inputs.Add(new InputMessage(T, [i]));
                c.Predictor.Step(T, i);
            }
            aCmd = bCmd = 0;
            host.Step();
            var ev = host.TakeEvents();
            wire.Enqueue((clients.Select(c => (c, PredictionTests.Through(host, c.Seat))).ToList(), ev.Count > 0 ? new EventBatch(host.Tick, ev) : null));
            while (wire.Count > 2)
            {
                var (snaps, batch) = wire.Dequeue();
                foreach (var (c, s) in snaps) { if (batch is not null) c.Net.Events.Enqueue(batch); c.Net.Inject(s); }
            }
            foreach (var c in clients)
            {
                c.Mirror.Advance(host.Session.TickSeconds, c.Net);
                c.Predictor.Reconcile();
                c.Mirror.Setup.Combat.Events.Clear();
                c.Mirror.Setup.Damage.Events.Clear();
            }
        }
        void Run(float s) { for (int k = 0; k < s / host.Session.TickSeconds; k++) Tick(); }
        var ra = a.Player.Car;
        var rb = b.Player.Car;
        void Ahead() { rb.Pos = ra.Pos + ra.Rot.Forward * 25; rb.Rot = ra.Rot; rb.Vel = default; rb.NetGeneration++; }
        void Hit(int amount)
        {
            var p = new DamagePacket { Attacker = ra, Mask = 2, Kind = DamageKind.Round };
            p.Ch[1] = amount;
            host.Setup.Damage.Apply(rb, p, new Vector3(0, 0.5f, 2));
        }
        // what a's target box reads: its target on a's client, the bar's value there, and the full reading
        (Vehicle? T, float Bar, float Full) Box()
        {
            var t = ca.Mirror.Setup.Combat.RadarTarget.GetValueOrDefault(ca.Car);
            return t is null ? (null, 0, 0) : (t, HealthGauge.Bar(t, ca.Mirror.Setup.Combat.RemoteSim && t != ca.Car), HealthGauge.Fraction(t));
        }
        void Log(string what)
        {
            var (t, bar, full) = Box();
            output.WriteLine($"{what}: a's target {Id(t)}, bar {bar:0.0000} (full reading {full:0.0000}, the server's {HealthGauge.Fraction(rb):0.0000}); width {(int)(30 * System.Math.Sqrt(System.Math.Clamp(bar * 100, 0.1, 100) * 0.01) + 0.5)} of 30 frame pixels");
        }

        Run(0.5f);
        Ahead();
        Run(0.5f);
        aCmd = CarCommands.TargetNearest;
        Run(1);
        Log("targeted");
        var whole = Box();
        for (int k = 0; k < 6; k++) { Hit(60); Run(0.5f); }
        Log("front armour hit");
        var armour = Box();
        for (int k = 0; k < 40 && rb.Health!.Brake!.Hp >= rb.Health.Brake.Max && rb.Health.Engine!.Hp >= rb.Health.Engine.Max && rb.Health.Suspension!.Hp >= rb.Health.Suspension.Max; k++) { Hit(60); Tick(); }
        Run(1);
        Log("brakes, engine or suspension hit");
        var parts = Box();
        float server = HealthGauge.Fraction(rb);
        bool alive = !Combat.IsDead(rb);
        // the death drops the target (the original's radar); the respawned car is targeted again, whole
        for (int k = 0; k < 200 && !Combat.IsDead(rb); k++) Hit(5000);
        Run(11);
        var dead = Box();
        bCmd = CarCommands.RaiseDead;
        Run(1);
        Ahead();
        Run(0.5f);
        aCmd = CarCommands.TargetNearest;
        Run(1);
        Log("respawned, T again");
        var back = Box();

        Assert.Equal(b.CarSerial, Id(whole.T));
        Assert.Equal(1f, whole.Bar);
        Assert.Equal(b.CarSerial, Id(armour.T));
        Assert.Equal(0.6667f, armour.Bar);
        Assert.True(alive);
        Assert.Equal(b.CarSerial, Id(parts.T));
        Assert.True(parts.Full < 0.01f, $"the full reading {parts.Full}: the unscaled branch");
        Assert.Equal(0.3333f, parts.Bar);
        Assert.Equal(HealthGauge.Remote(server), parts.Bar);
        // a's own car is not a remote car: its value reads in full
        Assert.Equal(HealthGauge.Fraction(ca.Car), HealthGauge.Bar(ca.Car, remote: false));
        Assert.Null(dead.T);
        Assert.Equal(b.CarSerial, Id(back.T));
        Assert.Equal(1f, back.Bar);
    }
}
