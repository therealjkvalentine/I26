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

    /// <summary>The first start's size (HdTextures.FirstStartSize): 8x only with its folder and a GPU of 6 GB or more (HdTextures.High8xVideoMemoryMb), else 4x.</summary>
    [Theory]
    [InlineData(true, 11264L, 8)]     // the 1080 Ti 11 GB
    [InlineData(true, 8192L, 8)]      // an 8 GB card
    [InlineData(true, 6144L, 8)]      // a 6 GB card: the 8x's ~3 GB fits with half the card spare
    [InlineData(true, 5120L, 4)]      // under 6 GB: 4x
    [InlineData(true, 4096L, 4)]      // the laptop GTX 1650 Ti 4 GB
    [InlineData(true, null, 4)]       // unknown memory: the 4x
    [InlineData(false, 11264L, 4)]    // no tex-world-8x: the 4x
    public void EightXOnlyWithItsFolderAndAGpuWithRoom(bool has8x, long? mb, int size) => Assert.Equal(size, HdTextures.FirstStartSize(has8x, mb));

    /// <summary>Apple silicon: the figure is the machine's memory, which the game, the OS and the GPU share.</summary>
    [Theory]
    [InlineData(16384L, 8)]   // a 16 GB Mac
    [InlineData(8192L, 4)]    // an 8 GB Mac: the 4x
    [InlineData(24576L, 8)]
    public void AMacCountsItsSharedMemory(long mb, int size) => Assert.Equal(size, HdTextures.FirstStartSize(true, mb, unified: true));

    [Fact]
    public void AFileWithoutTheKeyTakesTheFirstStartsDefaultUnlessTheMenuArtIsOff()
    {
        Assert.True(HdTextures.FromFile(null, hdMenuArt: true, firstStart: true));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: true, firstStart: false));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: false, firstStart: true));
        Assert.True(HdTextures.FromFile(true, hdMenuArt: false, firstStart: false));   // the key wins
    }
}
