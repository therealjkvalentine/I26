using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

public class RestartCacheTests
{
    private sealed class Kit(long bytes) { public long Bytes = bytes; }

    private static RestartCache<Kit> Cache(long ceiling = 1000, System.Func<bool>? shortOf = null) => new(ceiling, k => k.Bytes, shortOf ?? (() => false));

    [Fact]
    public void TheSameKeyTakesWhatWasKept()
    {
        var c = Cache();
        var kit = new Kit(10);
        Assert.Null(c.Take("t05|a"));
        c.Keep("t05|a", kit);
        Assert.Same(kit, c.Take("t05|a"));
        Assert.Same(kit, c.Take("t05|a"));   // a run of restarts
        Assert.Null(c.Dropped);
    }

    [Fact]
    public void AnotherKeyDropsIt()
    {
        var c = Cache();
        c.Keep("t05|a", new Kit(10));
        Assert.Null(c.Take("t06|a"));
        Assert.False(c.Holds);
        Assert.Equal("another mission", c.Dropped);
        // and the first mission again finds nothing: only the last mission's kit is ever held
        Assert.Null(c.Take("t05|a"));
    }

    [Fact]
    public void ASettingInTheKeyDropsItToo()
    {
        var c = Cache();
        c.Keep("t05|8x", new Kit(10));
        Assert.Null(c.Take("t05|4x"));
        Assert.Equal("another mission", c.Dropped);
    }

    [Fact]
    public void OverTheCeilingItIsDropped()
    {
        var c = Cache(ceiling: 100);
        var kit = new Kit(50);
        c.Keep("t05", kit);
        Assert.False(c.Trim());
        kit.Bytes = 101;   // it grew over the mission
        Assert.True(c.Trim());
        Assert.False(c.Holds);
        Assert.Equal("over the ceiling", c.Dropped);
        c.Keep("t05", new Kit(101));
        Assert.Null(c.Take("t05"));
    }

    [Fact]
    public void ShortOfMemoryItIsDropped()
    {
        bool tight = false;
        var c = Cache(shortOf: () => tight);
        c.Keep("t05", new Kit(10));
        Assert.NotNull(c.Take("t05"));
        tight = true;
        Assert.Null(c.Take("t05"));
        Assert.Equal("memory short", c.Dropped);
    }

    [Fact]
    public void KeepingReplacesAndClearsTheReason()
    {
        var c = Cache();
        c.Keep("t05", new Kit(1));
        c.Drop("another mission");
        var kit = new Kit(2);
        c.Keep("t06", kit);
        Assert.Null(c.Dropped);
        Assert.Equal("t06", c.Key);
        Assert.Same(kit, c.Take("t06"));
    }
}

public class RestartCacheKeyTests
{
    private static readonly object?[] Parts = [@"C:\install", null, @"C:\install\missions\t05.msn", 12345L, 638000000000000000L, "AB12", "-", false, true, 4, 8, "HdFolders"];

    [Fact]
    public void TheSamePartsGiveTheSameKey() => Assert.Equal(RestartCache.KeyOf(Parts), RestartCache.KeyOf((object?[])Parts.Clone()));

    [Fact]
    public void EveryPartChangesTheKey()
    {
        var keys = new List<string> { RestartCache.KeyOf(Parts) };
        object?[] others = ["D:/install", "maps", "t06.msn", 12346L, 638000000000000001L, "AB13", "CD", true, false, 0, 4, "HdZip"];
        for (int i = 0; i < Parts.Length; i++)
        {
            var p = (object?[])Parts.Clone();
            p[i] = others[i];
            keys.Add(RestartCache.KeyOf(p));
        }
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void SeparatorsInAPartDoNotAlias()
    {
        Assert.NotEqual(RestartCache.KeyOf("a|b", "c"), RestartCache.KeyOf("a", "b|c"));
        Assert.NotEqual(RestartCache.KeyOf("-", "x"), RestartCache.KeyOf(null, "x"));
        Assert.NotEqual(RestartCache.KeyOf("%7C"), RestartCache.KeyOf("|"));
    }
}
