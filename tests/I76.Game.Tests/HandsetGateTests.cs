using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

public class HandsetGateTests
{
    [Fact]
    public void ALineStartedInTheCockpitRaisesTheHandset()
    {
        var g = new HandsetGate();
        var line = new object();
        Assert.True(g.Raised(line, binoculars: false));
        Assert.False(g.Raised(null, binoculars: false));
    }

    [Fact]
    public void ALineStartedUnderTheBinocularsNeverRaisesIt()
    {
        var g = new HandsetGate();
        var line = new object();
        Assert.False(g.Raised(line, binoculars: true));
        // lowering them mid-line does not bring it up: only a line's start raises it (0x423861)
        Assert.False(g.Raised(line, binoculars: false));
    }

    [Fact]
    public void RaisingTheBinocularsMidLineLeavesItUp()
    {
        var g = new HandsetGate();
        var line = new object();
        Assert.True(g.Raised(line, binoculars: false));
        Assert.True(g.Raised(line, binoculars: true));
    }

    [Fact]
    public void EachQueuedLineIsDecidedAtItsOwnStart()
    {
        var g = new HandsetGate();
        object a = new(), b = new();
        Assert.False(g.Raised(a, binoculars: true));
        Assert.True(g.Raised(b, binoculars: false));   // the next line (0x4239c8) with the mask off
        Assert.False(g.Raised(HandsetGate.Microphone, binoculars: true));
    }
}
