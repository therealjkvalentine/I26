using I76.Net;
using I76.Net.Hud;
using I76.Server;
using I76.Sim.Session;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Net.Tests;

/// <summary>The network Esc menu's Self Destruct (new car): Ctrl+Del's command, refused while dead or decided, a no-killer death on the server.</summary>
public class EscapeMenuSelfDestructTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void TheMenuSendsSelfDestructOnlyWhileAliveAndUndecided(bool dead, bool decided, bool sends)
    {
        Assert.Equal(sends, NetMenu.CanSelfDestruct(dead, decided));
        Assert.Equal(sends ? CarCommands.SelfDestruct : 0, NetMenu.SelfDestruct(dead, decided));
    }

    /// <summary>The menu's command on the wire destroys the car with no killer: a death, no kill for anyone, the score floored at 0.</summary>
    [Fact]
    public void TheMenusCommandIsANoKillerDeathOnTheServer()
    {
        if (HostTests.InstallDir is not { } install) return;
        var host = new MeleeHost(new ServerOptions { Install = install, Arena = "m01", Seed = 7 });
        var a = host.Join("a", "vppirna1.vcf", 0);
        var b = host.Join("b", "vppirna1.vcf", 1);
        var kills = new List<KillEvent>();
        uint t = 1;
        void Step(CarInput i)
        {
            a.Inputs.Add(new InputMessage(t, [i]));
            b.Inputs.Add(new InputMessage(t, [default]));
            t++;
            host.Step();
            kills.AddRange(host.TakeEvents().OfType<KillEvent>());
        }
        for (int k = 0; k < 10; k++) Step(default);
        Assert.Equal(0, (int)(a.Player.Car.Flags & VehicleFlags.Dead));
        Step(new CarInput { Commands = NetMenu.SelfDestruct(dead: false, decided: false) });
        for (int k = 0; k < 5; k++) Step(default);
        Assert.NotEqual(0, (int)(a.Player.Car.Flags & VehicleFlags.Dead));
        Assert.Equal(1, a.Player.Deaths);
        Assert.Equal(0, a.Player.Score);
        Assert.Equal(0, b.Player.Kills);
        Assert.Contains(kills, k => k.Attacker == 0);
    }
}
