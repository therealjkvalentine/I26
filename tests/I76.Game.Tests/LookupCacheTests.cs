using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The shell lists' names read from the install (<see cref="LookupCache"/>: the roster's arena titles, the popup's car names).</summary>
public class LookupCacheTests
{
    [Fact]
    public void AMissIsKeptForAWhileAndAFoundNameForGood()
    {
        long now = 1000;
        var cache = new LookupCache(TimeSpan.FromSeconds(5), () => now);
        int looks = 0;
        string have = "";
        string Look() { looks++; return have; }

        // a listed game whose map this install lacks: the roster draws again and again, the folders are probed once
        for (int i = 0; i < 50; i++) Assert.Equal("", cache.Get("c:/i76|nr380", Look));
        Assert.Equal(1, looks);
        // the map arrives; the miss stands until it has passed, then the name is found and kept
        have = "Nr 380";
        now += 4999;
        Assert.Equal("", cache.Get("C:/I76|NR380", Look));
        Assert.Equal(1, looks);
        now += 1;
        Assert.Equal("Nr 380", cache.Get("c:/i76|nr380", Look));
        Assert.Equal(2, looks);
        have = "";
        now += 3_600_000;
        Assert.Equal("Nr 380", cache.Get("c:/i76|nr380", Look));
        Assert.Equal(2, looks);
    }

    [Fact]
    public void ForgettingTheMissesLooksAgainAtOnce()
    {
        var cache = new LookupCache(TimeSpan.FromMinutes(1), () => 0);
        string have = "";
        Assert.Equal("", cache.Get("car.vcf", () => have));
        have = "Picard Piranha";
        Assert.Equal("", cache.Get("car.vcf", () => have));
        cache.ForgetMisses();
        Assert.Equal("Picard Piranha", cache.Get("car.vcf", () => have));
    }
}
