using I76.Server;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// The solid-cliff rule in a network game (docs/multiplayer.md "Solid cliffs"): the host's for the whole game, in a melee and in co-op;
/// the accept tells each fork client, whose prediction of its own car then steps as the server's does.
/// </summary>
public class CliffRuleNetTests(ITestOutputHelper output)
{
    [Fact]
    public void AMeleeHostsRuleIsTheServersWorlds()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using (var on = new NetMeleeServer(new ServerOptions { Install = dir, Arena = "m01", Port = 0, SolidCliffs = true }))
            Assert.True(on.Host.Setup.World.SolidCliffs);
        using var off = new NetMeleeServer(new ServerOptions { Install = dir, Arena = "m01", Port = 0 });
        Assert.False(off.Host.Setup.World.SolidCliffs);
    }

    [Fact]
    public void TheCoopHostsRuleReachesTheJoinersAccept()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var g = new CoopFlowTests.Game(dir, output, tweak: o => o with { SolidCliffs = true });
        var ann = g.Join("Ann");
        g.Start();
        Assert.True(g.Wait(() => ann.Accept is not null), "no accept");
        Assert.True(ann.Accept!.SolidCliffs);
        Assert.True((ann.Accept.Features & PeerFeatures.SolidCliffs) != 0);
        Assert.True(g.Server.Invoke(h => h.Setup.World.SolidCliffs));
    }

    /// <summary>A host migration's successor reads the options from the savestate's extras (JSON), and its restore adopts the snapshot's rule.</summary>
    [Fact]
    public void AHostMigrationKeepsTheRule()
    {
        var json = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var o = new ServerOptions { Install = "", Arena = "m01", SolidCliffs = true };
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<ServerOptions>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(o, json), json)!.SolidCliffs);
        Assert.False(new ServerOptions { Install = "" }.SolidCliffs);
    }
}
