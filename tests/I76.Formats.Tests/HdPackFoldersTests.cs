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

    /// <summary>Three kinds: the cockpit's art (z*) is the dash's whatever asks; a car's texture is a car's only when the model builder marked it; the rest is the world's.</summary>
    [Fact]
    public void AKindIsTheDashsACarsOrTheWorlds()
    {
        Assert.Equal(TextureKind.Dash, HdPackFolders.KindOf("zdash101.map", car: false));
        Assert.Equal(TextureKind.Dash, HdPackFolders.KindOf("zdash101.map", car: true));
        Assert.Equal(TextureKind.Car, HdPackFolders.KindOf("pp11bkb1.map", car: true));
        Assert.Equal(TextureKind.World, HdPackFolders.KindOf("wa11rd01.map", car: false));
    }

    /// <summary>The world reads from its size; the cars and the dash from theirs, one size; a kind at 0 (1997) reads no folder.</summary>
    [Fact]
    public void TheChainStartsAtTheKindsSize()
    {
        var has = Has("tex-world-8x", "tex-world-4x", "tex-world-2x");
        Assert.Equal(4, HdPackFolders.SizeFor(TextureKind.World, 4, 8));
        Assert.Equal(8, HdPackFolders.SizeFor(TextureKind.Car, 4, 8));
        Assert.Equal(8, HdPackFolders.SizeFor(TextureKind.Dash, 4, 8));
        Assert.Equal(["tex-world-8x", "tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor(TextureKind.Dash, 4, 8, has));
        Assert.Equal(["tex-world-8x", "tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor(TextureKind.Car, 4, 8, has));
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor(TextureKind.World, 4, 8, has));
        // the cars and the dash at the 4x: the 8x folder is never read
        Assert.Equal(["tex-world-4x", "tex-world-2x"], HdPackFolders.ChainFor(TextureKind.Car, 4, 4, has));
        // 1997: none, the install's own
        Assert.Empty(HdPackFolders.ChainFor(TextureKind.World, 0, 8, has));
        Assert.Empty(HdPackFolders.ChainFor(TextureKind.Dash, 4, 0, has));
        // an 8 with no 8x folder: the 4x (HdPackFolders.Choose)
        Assert.Equal(["tex-world-4x"], HdPackFolders.ChainFor(TextureKind.Dash, 4, 8, Has("tex-world-4x")));
        Assert.Empty(HdPackFolders.ChainFor(TextureKind.Car, 8, 8, Has()));
    }
}
