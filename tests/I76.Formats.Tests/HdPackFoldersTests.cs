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
}
