using I76.Server;
using Xunit;

namespace I76.Net.Tests;

/// <summary>The host's Co-op Damage (i26-server --coop-damage, the host form's) reaches the mission the server builds.</summary>
public class CoopDamageNetTests
{
    [Fact]
    public void TheServersCoopDamageIsTheMissionsAndItsBuilds()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = dir, Arena = "t01", Coop = "t01", MaxPlayers = 2, Port = 0, CoopDamage = 0.5f });
        var mode = (CoopMode)server.Host.Mode;
        Assert.Equal(0.5f, mode.Setup.Damage.CoopDamageFactor);
        Assert.Equal(0.5f, mode.Build.Coop!.DamageFactor);
        // the default is the original's
        using var plain = new NetMeleeServer(new ServerOptions { Install = dir, Arena = "t01", Coop = "t01", MaxPlayers = 2, Port = 0 });
        Assert.Equal(1f, ((CoopMode)plain.Host.Mode).Setup.Damage.CoopDamageFactor);
    }
}
