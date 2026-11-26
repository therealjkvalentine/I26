using Xunit;

namespace I76.Sim.Tests;

// The two sweeps over every car of the install, one class each: xUnit runs the tests of a class one after another and the
// classes side by side. The bodies stay in WreckSolidTests.

public class WreckSolidInstallSweepTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheSweepOverEveryCarOfTheInstall() => new WreckSolidTests(output).TheSweepOverEveryCarOfTheInstall();
}

public class WreckSolidInstallSolidSweepTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void TheSweepOverEveryCarOfTheInstallWithWrecksStayingSolid() => new WreckSolidTests(output).TheSweepOverEveryCarOfTheInstallWithWrecksStayingSolid();
}
