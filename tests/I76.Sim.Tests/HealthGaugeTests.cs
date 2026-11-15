using I76.Sim.Damage;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The target box's bar: the health percentage by object type.</summary>
public class HealthGaugeTests
{
    [Fact]
    public void AHelicoptersBarFollowsItsPool()
    {
        // the AI's helicopter: no car damage state at all; it read whole while it took hits (#38)
        var v = new Vehicle { Heli = new HeliState() };
        v.Heli.SetPool(6000);
        Assert.Equal(1f, HealthGauge.Fraction(v));
        v.Heli.Hp = 1584;
        Assert.Equal(1584f / 6000f, HealthGauge.Fraction(v), 4);
    }

    [Fact]
    public void ThePlayersHelicopterReadsItsPoolNotItsSpecialsOnlyDamageState()
    {
        var v = new Vehicle { Heli = new HeliState(), Health = new VehicleDamage(new Vehicle(), true) };
        v.Heli.SetPool(3000);
        v.Heli.Hp = 1500;
        Assert.Equal(0.5f, HealthGauge.Fraction(v), 4);
    }

    [Fact]
    public void AnythingWithoutAStateReadsWhole() => Assert.Equal(1f, HealthGauge.Fraction(new Vehicle()));

    /// <summary>A network game's remote car: the original's step from its owner's reading, 100 above 66.67, 66.67 above 33.33, else 33.33.</summary>
    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(0.6668f, 1f)]
    [InlineData(0.6667f, 0.6667f)]
    [InlineData(0.34f, 0.6667f)]
    [InlineData(0.3333f, 0.3333f)]
    [InlineData(0.006f, 0.3333f)]   // brakes scratched: the unscaled branch
    [InlineData(0f, 0.3333f)]
    public void ARemoteCarReadsTheNetworkStep(float full, float shown) => Assert.Equal(shown, HealthGauge.Remote(full));

    [Fact]
    public void OnlyARemoteCarIsStepped()
    {
        var v = new Vehicle();
        v.Health = new VehicleDamage(v, false);
        v.Health.Slots[9] = new BrakePart(v.P, 100) { Hp = 60 };
        Assert.Equal(0.006f, HealthGauge.Bar(v, remote: false), 4);
        Assert.Equal(0.3333f, HealthGauge.Bar(v, remote: true));
        var heli = new Vehicle { Heli = new HeliState() };
        heli.Heli.SetPool(1000);
        heli.Heli.Hp = 100;
        Assert.Equal(0.1f, HealthGauge.Bar(heli, remote: true), 4);
    }
}
