using System.Diagnostics;
using System.Numerics;
using I76.Net;
using I76.Server;
using I76.Sim.Damage;
using I76.Sim.Missions;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Net.Tests;

public partial class CoopNetTests
{
    /// <summary>
    /// Co-op's escorts with Extra Enemies on (docs/multiplayer.md "Escorted cars"): the host's sim alone scales what T01's Taurus
    /// takes, its successor derives the same factor from the savestate's build (no wire change), and every client reads the
    /// host's health on the Taurus (a client's mirror damages nothing itself, so a client of any build agrees).
    /// </summary>
    [Fact]
    public void TheHostsEscortHealthReachesEveryClient()
    {
        if (HostTests.InstallDir is not { } install) return;
        var server = new NetMeleeServer(new ServerOptions
        {
            Install = install, Arena = "t01", Coop = "t01", MaxPlayers = 2, Port = 0, Seed = 7, Name = "co-op", ListenHost = true, CoopScale = 1,
        });
        server.Log += m => output.WriteLine($"[server] {m}");
        var mode = (CoopMode)server.Host.Mode;
        Assert.Equal(1f / CoopSetup.EscortHealth, mode.Setup.Damage.EscortFactor);
        // a successor builds the mission from the savestate's build: the same factor and the same escorts
        var gi = new I76.Formats.Vfs.GameInstall(install);
        var vfs = new I76.Formats.Vfs.AssetVfs(gi);
        var asm = new I76.Formats.Assembly.ModelAssembler(vfs);
        var successor = CoopMission.Build(vfs, new I76.Formats.Assembly.MissionWorld(vfs, asm, gi.Resolve("miss16", "t01.msn")!), asm,
            mode.Build.Coop!, new I76.Sim.Rng.CrtRand(7), 7);
        Assert.Equal(mode.Setup.Damage.EscortFactor, successor.Damage.EscortFactor);
        Assert.Equal(mode.Setup.Script!.Host.Escorts(mode.Setup.Script.Vm!).Select(e => e.Name).Order(),
            successor.Script!.Host.Escorts(successor.Script.Vm!).Select(e => e.Name).Order());
        // Extra Enemies off: the original's
        using (var plain = new NetMeleeServer(new ServerOptions { Install = install, Arena = "t01", Coop = "t01", MaxPlayers = 2, Port = 0 }))
            Assert.Equal(1f, ((CoopMode)plain.Host.Mode).Setup.Damage.EscortFactor);

        server.Start();
        using var g = new Game(server, install, output);
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

        // the Taurus takes 60 rounds of 20 points on the server: two thirds of each reaches it
        int serial = server.Invoke(h => h.Setup.Script!.Vm!.Actors.First(a => a.Label.Equals("taurus", StringComparison.OrdinalIgnoreCase)).Handle is Entity { Vehicle: { } v } ? v.NetId & 0xffffff : 0);
        Assert.NotEqual(0, serial);
        float want = server.Invoke(h =>
        {
            var v = h.Setup.World.Vehicles.Single(x => (x.NetId & 0xffffff) == serial);
            for (int i = 0; i < 60; i++)
            {
                var pk = new DamagePacket { Mask = 1, Kind = DamageKind.Round };
                pk.Ch[0] = 20;
                h.Setup.Damage.Apply(v, pk, new Vector3(0, 0.5f, 2f));
            }
            return HealthGauge.Fraction(v);
        });
        output.WriteLine($"taurus {serial}: the server's gauge {want:0.00}");
        Assert.InRange(want, 0.05f, 0.95f);
        Assert.True(g.Wait(() => players.All(p => p.Mirror!.Car(serial) is { } c && MathF.Abs(HealthGauge.Fraction(c) - want) < 0.02f), 5000),
            string.Join(", ", players.Select(p => $"{p.Name} reads {(p.Mirror!.Car(serial) is { } c ? HealthGauge.Fraction(c) : -1):0.00}")));
        // the clients' mirrors have no escort rule of their own: the host's sim decides
        Assert.All(players, p => Assert.Equal(1f, p.Mirror!.Setup.Damage.EscortFactor));
    }
}
