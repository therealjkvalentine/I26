using I76.Net;
using I76.Server;
using I76.Sim.Math;
using I76.Sim.Session;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Net.Tests;

/// <summary>
/// #168: the sounds of the own rounds striking, and of being struck, in a network game of two predicted clients over a
/// link with latency (in-process: each client's inputs reach the host, and the host's snapshots and events reach it, a
/// fixed time late, on a test clock).
/// </summary>
public class HitSoundTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>A sound heard by a client: on its tick, the sound, and whether its prediction played it.</summary>
    internal readonly record struct Heard(uint Tick, string Sound, bool Predicted);

    private sealed class Peer
    {
        public required Seat Seat;
        public required ClientWorld Mirror;
        public required MeleeClient Client;
        public required OwnCarPredictor Predictor;
        public readonly Queue<(double Due, InputMessage M)> Up = new();
        public readonly Queue<(double Due, Snapshot S, EventBatch? E)> Down = new();
        public readonly List<Heard> Heard = [];
        public double Now;
    }

    /// <summary>
    /// Two players on m01 face each other 25 m apart, each car still. A fires a gun for five ticks (three rounds) at <paramref name="aFires"/>,
    /// B at <paramref name="bFires"/>; every tick each client sends its input (it reaches the host <paramref name="latencyMs"/>
    /// later), predicts its car, and takes what the host sent <paramref name="latencyMs"/> before. Returns what each client
    /// heard of the impacts and the host's ticks of them.
    /// </summary>
    internal static (List<Heard> A, List<Heard> B, List<uint> Host, string Gun)? Game(int latencyMs, uint aFires = 60, uint bFires = 140, uint ticks = 220)
    {
        if (HostTests.InstallDir is not { } install) return null;
        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m01", Seed = 7 });
        var sa = host.Join("a", "vppirna1.vcf", 0);
        var sb = host.Join("b", "vppirna1.vcf", 0);
        // b 25 m ahead of a, turned to face it
        var ca = sa.Player.Car;
        var cb = sb.Player.Car;
        var f = ca.Rot.Forward;
        double bx = ca.Pos.X + f.X * 25, bz = ca.Pos.Z + f.Z * 25;
        cb.Pos = new Double3(bx, host.Setup.Ground.BareHeight((float)bx, (float)bz) + (ca.Pos.Y - host.Setup.Ground.BareHeight((float)ca.Pos.X, (float)ca.Pos.Z)), bz);
        cb.Rot = Rows.Concat(ca.Rot, Rows.AboutY(MathF.PI));
        // the first bullet gun on each (the same car: the same hardpoint)
        var arm = ca.Armory!;
        int hp = Enumerable.Range(0, Math.Min(5, arm.Weapons.Count)).First(i => arm.Weapons[i].Def.Kind is 1 or 6 && arm.Weapons[i].Ammo != 0);
        string gun = $"hardpoint {hp + 1} ({arm.Weapons[hp].Def.File}, kind {arm.Weapons[hp].Def.Kind}, {arm.Weapons[hp].Def.Rate}/s)";
        double dt = host.Session.TickSeconds, late = latencyMs / 1000.0;
        Peer Make(Seat s)
        {
            var client = new MeleeClient();
            var p = new Peer { Seat = s, Client = client, Mirror = new ClientWorld(HostTests.Mirror("m01"), host.AcceptFor(s)), Predictor = null! };
            p.Predictor = new OwnCarPredictor(p.Mirror, client);
            client.TestClock = () => p.Now;
            return p;
        }
        var peers = new[] { Make(sa), Make(sb) };
        var hostImpacts = new List<uint>();
        for (uint t = 1; t <= ticks; t++)
        {
            double now = t * dt;
            for (int i = 0; i < 2; i++)
            {
                var p = peers[i];
                p.Now = now;
                uint fires = i == 0 ? aFires : bFires;
                var input = new CarInput();
                if (t >= fires && t < fires + 5) input.Buttons |= (CarButtons)((uint)CarButtons.Hardpoint1 << hp);
                p.Up.Enqueue((now + late, new InputMessage(t, [input])));
                p.Predictor.Step(t, input);
                while (p.Up.Count > 0 && p.Up.Peek().Due <= now + 1e-9) p.Seat.Inputs.Add(p.Up.Dequeue().M);
            }
            host.Step();
            var events = host.TakeEvents();
            foreach (var e in events) if (e is ImpactEvent) hostImpacts.Add(host.Tick);
            foreach (var p in peers)
            {
                p.Down.Enqueue((now + late, PredictionTests.Through(host, p.Seat), events.Count > 0 ? new EventBatch(host.Tick, events) : null));
                while (p.Down.Count > 0 && p.Down.Peek().Due <= now + 1e-9)
                {
                    var (_, s, e) = p.Down.Dequeue();
                    if (e is not null) p.Client.Events.Enqueue(e);
                    p.Client.Inject(s);
                }
                p.Mirror.Advance(dt, p.Client);
                p.Predictor.Reconcile();
                foreach (var e in p.Mirror.Setup.Combat.Events)
                    if (e is Impact { Sound.Length: > 0 } im) p.Heard.Add(new Heard(t, im.Sound, false));
                    else if (e is ImpactSound s) p.Heard.Add(new Heard(t, s.Sound, true));
                p.Mirror.Setup.Combat.Events.Clear();
            }
        }
        return (peers[0].Heard, peers[1].Heard, hostImpacts, gun);
    }

    private static string Show(IEnumerable<Heard> h) => string.Join(", ", h.Select(x => $"{x.Sound} on {x.Tick}{(x.Predicted ? " (predicted)" : "")}"));

    /// <summary>
    /// Each client hears its own rounds strike on the tick they strike in its prediction, once each (the host's copy is
    /// not played again), and the other's rounds striking its car when the view reaches the host's tick of them.
    /// </summary>
    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void OwnHitsAreHeardAtOnceAndBeingHitWithTheView(int latencyMs)
    {
        if (Game(latencyMs) is not var (a, b, hostTicks, gun)) return;
        const uint aFires = 60, bFires = 140;
        var aOwn = a.Where(h => h.Tick >= aFires && h.Tick < bFires).ToList();
        var aHit = a.Where(h => h.Tick >= bFires).ToList();
        var bHit = b.Where(h => h.Tick >= aFires && h.Tick < bFires).ToList();
        var bOwn = b.Where(h => h.Tick >= bFires).ToList();
        var hostA = hostTicks.Where(t => t >= aFires && t < bFires).ToList();
        var hostB = hostTicks.Where(t => t >= bFires).ToList();
        output.WriteLine($"{latencyMs} ms each way, {gun}; host impacts of a's rounds on {string.Join(", ", hostA)}, of b's on {string.Join(", ", hostB)}");
        output.WriteLine($"a fires on {aFires}: a hears its hits: {Show(aOwn)}; b hears being hit: {Show(bHit)}");
        output.WriteLine($"b fires on {bFires}: b hears its hits: {Show(bOwn)}; a hears being hit: {Show(aHit)}");
        Assert.NotEmpty(hostA);
        Assert.NotEmpty(hostB);
        // once each: as many heard as the host made, by each side
        Assert.Equal(hostA.Count, aOwn.Count);
        Assert.Equal(hostA.Count, bHit.Count);
        Assert.Equal(hostB.Count, bOwn.Count);
        Assert.Equal(hostB.Count, aHit.Count);
        // the own: from the prediction, before the host has even made them (an input reaches it a one-way trip later)
        Assert.All(aOwn.Concat(bOwn), h => Assert.True(h.Predicted));
        for (int k = 0; k < hostA.Count; k++) Assert.True(aOwn[k].Tick < hostA[k], $"a's hit {k} heard on {aOwn[k].Tick}, the host's on {hostA[k]}");
        for (int k = 0; k < hostB.Count; k++) Assert.True(bOwn[k].Tick < hostB[k], $"b's hit {k} heard on {bOwn[k].Tick}, the host's on {hostB[k]}");
        // being hit: the server's, when the view reaches its tick (a one-way trip and the view's delay after the host)
        Assert.All(aHit.Concat(bHit), h => Assert.False(h.Predicted));
        for (int k = 0; k < hostA.Count; k++) Assert.True(bHit[k].Tick > hostA[k]);
    }

    /// <summary>
    /// The match: a server impact of the same sound and kind, near the predicted one's place, flight and tick, is taken
    /// once; another sound, kind, place or flight is not (it plays); a predicted one the server never sent is let go after
    /// the window.
    /// </summary>
    [Fact]
    public void AServerImpactIsTakenOnlyForAPredictedOne()
    {
        if (HostTests.InstallDir is not { } install) return;
        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m01", Seed = 7 });
        var seat = host.Join("a", "vppirna1.vcf", 0);
        var predictor = new OwnCarPredictor(new ClientWorld(HostTests.Mirror("m01"), host.AcceptFor(seat)), new MeleeClient());
        var at = new System.Numerics.Vector3(100, 5, 200);
        var fwd = System.Numerics.Vector3.UnitZ;
        predictor.ExpectImpact(new ImpactSound("wmgr1.wav", at, 1, fwd), 100);
        predictor.ExpectImpact(new ImpactSound("wmgr1.wav", at, 1, fwd), 102);
        predictor.ExpectImpact(new ImpactSound("vimpb1.wav", at, 2, fwd), 104);
        ImpactEvent Server(string sound, byte kind, System.Numerics.Vector3 where, System.Numerics.Vector3 dir) => new("xbulc1.xdf", sound, where, 1, kind, where, System.Numerics.Vector3.UnitY, dir);
        // not the same sound, kind, flight or place: played
        Assert.False(predictor.TakeImpact(Server("wmgr2.wav", 1, at, fwd), 101, 7));
        Assert.False(predictor.TakeImpact(Server("wmgr1.wav", 6, at, fwd), 101, 7));
        Assert.False(predictor.TakeImpact(Server("wmgr1.wav", 1, at, -fwd), 101, 7));
        Assert.False(predictor.TakeImpact(Server("wmgr1.wav", 1, at + new System.Numerics.Vector3(OwnCarPredictor.ImpactMetres + 1, 0, 0), fwd), 101, 7));
        // the two predicted, a few metres off and a tick late (the target moved for the server): each taken once
        Assert.True(predictor.TakeImpact(Server("wmgr1.wav", 1, at + new System.Numerics.Vector3(4, 0, 0), fwd), 101, 7));
        Assert.True(predictor.TakeImpact(Server("wmgr1.wav", 1, at, fwd), 103, 7));
        Assert.False(predictor.TakeImpact(Server("wmgr1.wav", 1, at, fwd), 103, 7));
        // the rocket's, never sent: gone after the window
        Assert.False(predictor.TakeImpact(Server("vimpb1.wav", 2, at, fwd), 104 + 8, 7));
    }
}
