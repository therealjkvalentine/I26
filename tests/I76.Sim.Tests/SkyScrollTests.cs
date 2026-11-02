using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

public class SkyScrollTests
{
    [Theory]
    [InlineData(true, true, 2, 2)]    // cockpit view, mirror full: the sky is drawn again for the mirror
    [InlineData(true, true, 1, 1)]    // objects only: no sky in the mirror
    [InlineData(true, true, 0, 1)]    // mirror off
    [InlineData(false, true, 2, 1)]   // an outside view: no mirror pass
    [InlineData(true, false, 2, 1)]   // no mirror in this cockpit
    public void StepsPerTick(bool cockpit, bool mirror, int mode, int expected) =>
        Assert.Equal(expected, SkyScroll.StepsPerTick(cockpit, mirror, mode));
}
