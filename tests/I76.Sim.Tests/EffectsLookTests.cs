using I76.Sim.Views;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>The config-only effects_size / effects_brightness / effects_opacity keys.</summary>
public class EffectsLookTests
{
    [Theory]
    [InlineData(null, 100)]
    [InlineData("", 100)]
    [InlineData("nonsense", 100)]
    [InlineData("80", 80)]
    [InlineData(" 70% ", 70)]
    [InlineData("55.4", 55)]
    [InlineData("10", 25)]      // clamped to 25..150
    [InlineData("0", 25)]
    [InlineData("-40", 25)]
    [InlineData("400", 150)]
    [InlineData("150", 150)]
    [InlineData("25", 25)]
    public void Parse(string? s, int expect) => Assert.Equal(expect, EffectsLook.Parse(s));

    [Fact]
    public void FactorIsThePercentageClamped()
    {
        Assert.Equal(1f, EffectsLook.Factor(100));
        Assert.Equal(0.7f, EffectsLook.Factor(70), 5);
        Assert.Equal(0.25f, EffectsLook.Factor(5));
        Assert.Equal(1.5f, EffectsLook.Factor(900));
    }
}
