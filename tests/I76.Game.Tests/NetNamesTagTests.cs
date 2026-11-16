using I76.Game.Net;
using I76.Sim.Missions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The name tags over the cars of a network game: a Co-op Scaling copy reads as the car it copies (no "EXTRA").</summary>
public class NetNamesTagTests
{
    [Fact]
    public void ACopyIsTaggedAsTheCarItCopies()
    {
        // the mission's own car carries no tag in co-op, so neither does any copy of it
        Assert.Null(NetNames.Tag(isPlayer: false, owner: -1, "t09dl01"));
        for (int k = 1; k <= CoopSetup.MaxExtraEnemies; k++)
            Assert.Null(NetNames.Tag(isPlayer: false, owner: -1, ScriptHost.ExtraName("t09dl01", k)));
        // players keep their names; a melee's computer player its driver's
        Assert.Equal("Jade", NetNames.Tag(isPlayer: true, owner: 1, "Jade"));
        Assert.Equal("player 3", NetNames.Tag(isPlayer: true, owner: 2, ""));
        Assert.Equal(NetNames.Tag(true, -1, "t09dl01"), NetNames.Tag(true, -1, ScriptHost.ExtraName("t09dl01", 2)));
    }

    [Fact]
    public void TheOriginalNameDropsTheCopyNumber()
    {
        Assert.Equal("t09dl01", ScriptHost.OriginalName(ScriptHost.ExtraName("t09dl01", 3)));
        Assert.Equal("t09dl01", ScriptHost.OriginalName("t09dl01"));
        // a name with a plus of its own that is no copy number stays whole
        Assert.Equal("a+b", ScriptHost.OriginalName("a+b"));
    }
}
