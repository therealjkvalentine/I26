using System.Diagnostics;
using I76.Net;
using I76.Server;
using I76.Sim.Damage;
using Xunit;

namespace I76.Net.Tests;

public partial class CoopNetTests
{
    /// <summary>
    /// Co-op Scaling's copies on the clients (upstream #194): a car the server made during the mission is built on every client
    /// with a damage state, so the health the server's snapshots carry reaches it. A copy damaged on the server reads damaged
    /// on both clients' target bars (it read whole, the bar white, until it died), and its paint is the server's.
    /// </summary>
    [Fact]
    public void ACopyDamagedOnTheServerReadsDamagedOnEveryClient()
    {
        if (HostTests.InstallDir is not { } install) return;
        var server = new NetMeleeServer(new ServerOptions
        {
            Install = install, Arena = "t01", Coop = "t01", MaxPlayers = 2, Port = 0, Seed = 7, Name = "co-op", ListenHost = true, CoopScale = 3,
        });
        server.Log += m => output.WriteLine($"[server] {m}");
        server.Start();
        using var g = new Game(server, install, output);
        // both in before anyone's first input starts the mission: the copies are made for the humans present at the first orders
        var players = new[] { new Player("Hal", install), new Player("Ann", install) };
        foreach (var p in players)
        {
            p.Client.Log += m => output.WriteLine($"[{p.Name}] {m}");
            p.Client.Connect("127.0.0.1", server.Port, p.Name, Protocol.MissionCar);
            g.Players.Add(p);
        }
        var clock = Stopwatch.StartNew();
        double last = 0;
        while (!players.All(p => p.Client.State == ClientState.Joined && p.Mirror is not null) && clock.ElapsedMilliseconds < 20000)
        {
            double now = clock.Elapsed.TotalSeconds;
            foreach (var p in players) p.Frame(now - last);
            last = now;
            Thread.Sleep(5);
        }
        Assert.All(players, p => Assert.Equal(ClientState.Joined, p.Client.State));

        // the mission starts; T01's enemy cars are ordered from its first ticks, one copy each for the second human
        int serial = 0;
        Assert.True(g.Wait(() => (serial = server.Invoke(h => h.Setup.Script!.Host.Entities.FirstOrDefault(e => e.ExtraOf is not null && !e.Dead)?.Vehicle is { } v ? v.NetId & 0xffffff : 0)) != 0),
            "the server made no copy");
        Assert.True(g.Wait(() => players.All(p => p.Mirror!.Car(serial) is not null)), "a client never had the copy");
        foreach (var p in players)
        {
            var v = p.Mirror!.Car(serial)!;
            Assert.NotNull(v.Health);                                         // was null: the snapshot's damage had nowhere to go
            Assert.Equal(1f, HealthGauge.Fraction(v), 3);
            Assert.Equal(server.Invoke(h => h.Setup.World.Vehicles.Single(x => (x.NetId & 0xffffff) == serial).Paint), v.Paint);
        }

        // the server's copy loses its front armour: 28 % on the gauge (the red third)
        float want = server.Invoke(h =>
        {
            var v = h.Setup.World.Vehicles.Single(x => (x.NetId & 0xffffff) == serial);
            v.Health!.Armour[0] = 0;
            return HealthGauge.Fraction(v);
        });
        output.WriteLine($"copy {serial}: the server's gauge {want:0.00}");
        Assert.InRange(want, 0.2f, 0.34f);
        Assert.True(g.Wait(() => players.All(p => MathF.Abs(HealthGauge.Fraction(p.Mirror!.Car(serial)!) - want) < 0.02f), 5000),
            string.Join(", ", players.Select(p => $"{p.Name} reads {HealthGauge.Fraction(p.Mirror!.Car(serial)!):0.00}")));
    }
}
