using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The base host form's options box (the port's layout): every rule's hotspot inside the box and none over another.</summary>
public class HostFormLayoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HotspotsSitInsideTheBoxApart(bool coop)
    {
        var hot = MeleeScreen.HostBoxHotspots(coop);
        var (x0, x1, y0, y1) = MeleeScreen.HostBoxInside;
        // a hotspot is hit strictly inside its rectangle: the pixels x0+1..x1-1, y0+1..y1-1
        foreach (var (id, r) in hot)
        {
            Assert.True(r.X0 + 1 >= x0 && r.X1 - 1 <= x1 && r.Y0 + 1 >= y0 && r.Y1 - 1 <= y1, $"{id} {r} outside the box");
            Assert.True(r.X1 - r.X0 >= 10 && r.Y1 - r.Y0 >= 10, $"{id} {r} too small to hit");
        }
        foreach (var (a, ra) in hot)
            foreach (var (b, rb) in hot)
                if (a < b)
                    Assert.False(ra.X0 + 1 < rb.X1 && rb.X0 + 1 < ra.X1 && ra.Y0 + 1 < rb.Y1 && rb.Y0 + 1 < ra.Y1, $"{a} {ra} overlaps {b} {rb}");
    }

    [Fact]
    public void TheCoopBoxKeepsItsOwnSwitches()
    {
        var melee = MeleeScreen.HostBoxHotspots(false);
        var coop = MeleeScreen.HostBoxHotspots(true);
        Assert.DoesNotContain(16, coop.Keys);   // TEAM PLAY
        Assert.DoesNotContain(18, coop.Keys);   // MAX SCORE
        Assert.DoesNotContain(29, coop.Keys);   // BOTS
        // INVITE ONLY moves to the first column's first row (TEAM PLAY's place)
        Assert.Equal(melee[16], coop[17]);
    }
}
