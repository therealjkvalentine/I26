using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

public class HdPackFoldersTests
{
    private static Func<string, bool> Has(params string[] folders) => f => folders.Contains(f);

    [Fact]
    public void TheChosenSizeWinsWhenItsFolderExists() =>
        Assert.Equal("tex-world-3x", HdPackFolders.Choose(3, Has("tex-world", "tex-world-4x", "tex-world-3x")));

    [Fact]
    public void AMissingSizeFallsBackToThePlainFolder() =>
        Assert.Equal("tex-world", HdPackFolders.Choose(4, Has("tex-world", "tex-world-2x")));

    [Fact]
    public void WithoutPlainTheNearestSizePresentIsUsed()
    {
        Assert.Equal("tex-world-2x", HdPackFolders.Choose(4, Has("tex-world-2x")));
        Assert.Equal("tex-world-4x", HdPackFolders.Choose(3, Has("tex-world-4x", "tex-world-2x")));   // a tie takes the larger
    }

    [Fact]
    public void NoFolderIsNull() => Assert.Null(HdPackFolders.Choose(4, Has()));

    [Fact]
    public void OnlyExistingSizesAreListedLargestFirst() =>
        Assert.Equal([4, 2], HdPackFolders.Available(Has("tex-world-2x", "tex-world-4x", "tex-world")));

    [Fact]
    public void APartial8xChainsOverTheSmallerSizesLargestFirst()
    {
        Assert.Equal(["tex-world-8x", "tex-world-4x"], HdPackFolders.Chain(8, Has("tex-world-8x", "tex-world-4x", "tex-world")));
        Assert.Equal(["tex-world-8x", "tex-world-4x", "tex-world-2x"], HdPackFolders.Chain(8, Has("tex-world-2x", "tex-world-8x", "tex-world-4x")));
        // the 4x chosen: the 8x is not read, the smaller sizes follow
        Assert.Equal(["tex-world-4x", "tex-world-3x"], HdPackFolders.Chain(4, Has("tex-world-8x", "tex-world-4x", "tex-world-3x")));
        Assert.Equal(["tex-world-4x"], HdPackFolders.Chain(4, Has("tex-world-8x", "tex-world-4x")));
    }

    [Fact]
    public void AMissingChosenSizeChainsFromTheNearestPresent()
    {
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.Chain(8, Has("tex-world-4x", "tex-world-2x")));   // no 8x folder: the 4x
        Assert.Equal(["tex-world-4x"], HdPackFolders.Chain(3, Has("tex-world-8x", "tex-world-4x")));   // no 3x: the nearest, 4x, and nothing over it
    }

    [Fact]
    public void ThePlainFolderEndsTheChainAndNoneIsEmpty()
    {
        Assert.Equal(["tex-world"], HdPackFolders.Chain(8, Has("tex-world", "tex-world-4x")));
        Assert.Empty(HdPackFolders.Chain(4, Has()));
    }

    [Theory]
    [InlineData("tex-world-8x", 8)]
    [InlineData("tex-world-4x", 4)]
    [InlineData("tex-world", 0)]
    [InlineData(null, 0)]
    public void AFoldersSize(string? folder, int size) => Assert.Equal(size, HdPackFolders.SizeOf(folder));

    [Theory]
    [InlineData(8, 8)]
    [InlineData(4, 4)]
    [InlineData(2, 2)]
    [InlineData(5, 4)]
    [InlineData(0, 4)]
    public void ASizeOutsideTheSetIsTheDefault(int stored, int expected) => Assert.Equal(expected, HdPackFolders.Normalize(stored));

    /// <summary>The cockpit's art is every stem that starts with z (the dash, gauges, needles, panels, hands, mirror, radar); a car's or the world's is anything else.</summary>
    [Theory]
    [InlineData("zdash101.map", true)]
    [InlineData("zspeedo6.vqm", true)]
    [InlineData("ZNEEDLE1.MAP", true)]
    [InlineData("zswls01.map", true)]
    [InlineData("pp11bkb1.map", false)]    // a car's body
    [InlineData("cp11mdl1.vqm", false)]    // a police door
    [InlineData("wa11rd01.map", false)]    // the world
    [InlineData("tex/pp11ftf1", false)]
    [InlineData("", false)]
    public void TheCockpitsArtStartsWithZ(string member, bool cockpit) => Assert.Equal(cockpit, HdPackFolders.IsCockpit(member));

    /// <summary>Each kind reads from its own size: a car texture reads the 8x folder only when the cars' size is 8, the cockpit's only when the dash's is.</summary>
    [Fact]
    public void TheChainStartsAtTheKindsSize()
    {
        var has = Has("tex-world-8x", "tex-world-4x", "tex-world-2x");
        Assert.Equal(8, HdPackFolders.SizeFor("zdash101.map", 8, 4));
        Assert.Equal(4, HdPackFolders.SizeFor("pp11bkb1.map", 8, 4));
        Assert.Equal(["tex-world-8x", "tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor("zdash101.map", 8, 4, has));
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor("pp11bkb1.map", 8, 4, has));
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor("zdash101.map", 4, 8, has));
        Assert.Equal(["tex-world-8x", "tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor("pp11bkb1.map", 4, 8, has));
        // both at the 4x: the 8x folder is never read
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor("zdash101.map", 4, 4, has));
        // the dash's 8 with no 8x folder: the 4x (HdPackFolders.Choose), the same as the cars'
        Assert.Equal(["tex-world-4x"], HdPackFolders.ChainFor("zdash101.map", 8, 4, Has("tex-world-4x")));
        Assert.Empty(HdPackFolders.ChainFor("zdash101.map", 8, 8, Has()));
    }
}
