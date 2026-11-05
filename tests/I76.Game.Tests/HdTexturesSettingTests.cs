using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The HD textures' switch ([port] hd_textures, no row): the key when the file has it, else the menu art's switch, which turned the
/// textures off too before they had a key of their own.
/// </summary>
public class HdTexturesSettingTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    public void TheKeyWinsWhenTheFileHasIt(bool key, bool menuArt, bool expected) => Assert.Equal(expected, HdTextures.FromFile(key, menuArt));

    [Fact]
    public void AFileWithoutTheKeyFollowsHdMenuArt()
    {
        Assert.True(HdTextures.FromFile(null, hdMenuArt: true));    // the default, and a player who never touched the menu art
        Assert.False(HdTextures.FromFile(null, hdMenuArt: false));  // menu art turned off: the textures stay off
    }
}

/// <summary>The first start's HD Textures (HdTextures.DefaultOn): 4x when tex-world-4x is in the user folder, off without it, and off on a GPU with under 4 GB.</summary>
public class HdTexturesFirstStartTests
{
    [Theory]
    [InlineData(true, 11264L, true)]      // the 1080 Ti
    [InlineData(true, 4096L, true)]       // a 4 GB laptop GTX 1650 Ti: the 4x pack fits
    [InlineData(true, 3072L, false)]      // a 3 GB card
    [InlineData(true, 2048L, false)]
    [InlineData(true, null, true)]        // unknown memory (Linux) counts as enough
    [InlineData(false, 11264L, false)]    // no tex-world-4x: off
    [InlineData(false, null, false)]
    public void FourXWhenItsFolderIsThereAndTheGpuHasRoom(bool has4x, long? mb, bool on) => Assert.Equal(on, HdTextures.DefaultOn(has4x, mb));

    /// <summary>The first start's size is the 4x even when a tex-world-8x is there: the 8x shows no difference at play (docs/perf/costs.md).</summary>
    [Fact]
    public void TheFirstStartIs4xWhateverTheCard() => Assert.Equal(4, HdTextures.FirstStartSize);

    [Fact]
    public void AFileWithoutTheKeyTakesTheFirstStartsDefaultUnlessTheMenuArtIsOff()
    {
        Assert.True(HdTextures.FromFile(null, hdMenuArt: true, firstStart: true));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: true, firstStart: false));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: false, firstStart: true));
        Assert.True(HdTextures.FromFile(true, hdMenuArt: false, firstStart: false));   // the key wins
    }
}
