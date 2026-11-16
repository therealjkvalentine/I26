using I76.Server;
using Xunit;
using Xunit.Abstractions;

namespace I76.Net.Tests;

/// <summary>
/// The port's wreck rule in a network game (docs/multiplayer.md "Wrecks stay solid"): the host's choice in a co-op mission, in
/// force only while every player in the game has it, so the server's contacts and each client's prediction agree.
/// </summary>
public class WreckRuleNetTests(ITestOutputHelper output)
{
    private static bool ServerRule(CoopFlowTests.Game g) => g.Server.Invoke(h => h.Setup.World.WrecksStaySolid);

    [Fact]
    public void TheHostsRuleHoldsBetweenForkPlayers()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var g = new CoopFlowTests.Game(dir, output, wrecksSolid: true);
        var ann = g.Join("Ann");
        var cy = g.Join("Cy");
        g.Start();
        Assert.True(g.Wait(() => ServerRule(g) && ann.WrecksSolid && cy.WrecksSolid), $"server {ServerRule(g)}, Ann {ann.WrecksSolid}, Cy {cy.WrecksSolid}");
        Assert.True(ann.Accept!.WrecksSolid);
    }

    [Fact]
    public void AHostThatDoesNotAskLeavesTheOriginals()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var g = new CoopFlowTests.Game(dir, output);
        var ann = g.Join("Ann");
        g.Start();
        g.Pump(300);
        Assert.False(ServerRule(g));
        Assert.False(ann.WrecksSolid);
        Assert.False(ann.Accept!.WrecksSolid);
    }

    [Fact]
    public void APlayerWithoutTheRuleTurnsItOffWhileTheyPlay()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var g = new CoopFlowTests.Game(dir, output, wrecksSolid: true);
        var ann = g.Join("Ann");
        var bob = g.Join("Bob", upstream: true);
        g.Start();
        Assert.True(g.Wait(() => !ServerRule(g) && !ann.WrecksSolid), $"server {ServerRule(g)}, Ann {ann.WrecksSolid}");
        g.Pump(300);
        Assert.False(ServerRule(g));
        Assert.False(ann.WrecksSolid);
        Assert.False(bob.WrecksSolid);
        // Bob leaves: the rule is the host's again, and Ann's game is told
        g.Players.RemoveAll(p => p.Client == bob);
        bob.Dispose();
        Assert.True(g.Wait(() => ServerRule(g) && ann.WrecksSolid), $"after Bob left: server {ServerRule(g)}, Ann {ann.WrecksSolid}");
    }

    [Fact]
    public void AMeleeNeverTakesTheRule()
    {
        if (HostTests.InstallDir is not { } dir) return;
        using var server = new NetMeleeServer(new ServerOptions { Install = dir, Arena = "m01", Port = 0, WrecksStaySolid = true });
        Assert.False(server.Host.Setup.World.WrecksStaySolid);
        Assert.False(server.Host.WrecksSolidAsked);
    }

    /// <summary>A host migration's successor reads the options from the savestate's extras (JSON): the rule goes across.</summary>
    [Fact]
    public void AHostMigrationKeepsTheRule()
    {
        var json = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var o = new ServerOptions { Install = "", Coop = "t01", WrecksStaySolid = true };
        Assert.True(System.Text.Json.JsonSerializer.Deserialize<ServerOptions>(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(o, json), json)!.WrecksStaySolid);
        Assert.False(new ServerOptions { Install = "" }.WrecksStaySolid);
    }
}
