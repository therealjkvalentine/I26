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

    [Theory]
    [InlineData(4, 4)]
    [InlineData(2, 2)]
    [InlineData(5, 4)]
    [InlineData(0, 4)]
    public void ASizeOutsideTheSetIsTheDefault(int stored, int expected) => Assert.Equal(expected, HdPackFolders.Normalize(stored));
}
