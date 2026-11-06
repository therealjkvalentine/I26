using I76.Game.Assets;
using I76.Game.Shell;
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

    /// <summary>
    /// HdTextures.Pick: 8x (cockpit &amp; cars) with tex-world-8x beside the 4x and a 6 GB card (Windows: the card's own memory) or a
    /// 16 GB Mac (the machine's memory, shared), else 4x with tex-world-4x and ~3.8 GB, else Off (0).
    /// </summary>
    [Theory]
    [InlineData(true, true, 11264L, false, 8)]    // the 1080 Ti
    [InlineData(true, true, 6144L, false, 8)]     // a 6 GB card
    [InlineData(true, true, 6143L, false, 8)]     // a driver that reports a MiB short
    [InlineData(true, true, 5120L, false, 4)]     // a 5 GB card: the 4x
    [InlineData(true, true, 4096L, false, 4)]     // the laptop's 4 GB GTX 1650 Ti: the 4x
    [InlineData(true, true, 3072L, false, 0)]     // a 3 GB card: off
    [InlineData(true, true, 16384L, true, 8)]     // a 16 GB Mac
    [InlineData(true, true, 24576L, true, 8)]     // a 24 GB Mac
    [InlineData(true, true, 8192L, true, 4)]      // an 8 GB Mac: the 4x, though 8 GB of video memory would be enough on a card
    [InlineData(true, true, 8192L, false, 8)]     // an 8 GB card
    [InlineData(true, false, 11264L, false, 4)]   // no tex-world-8x: the 4x
    [InlineData(true, false, 16384L, true, 4)]
    [InlineData(false, true, 11264L, false, 0)]   // an 8x with no 4x under it: off (the row offers the 8x only over the 4x)
    [InlineData(false, false, 11264L, false, 0)]  // no pack: off
    [InlineData(true, true, null, false, 4)]      // unknown memory (Linux): the 4x, as before, never the 8x
    [InlineData(false, false, null, false, 0)]
    public void EightXWhereItFitsElseFourXElseOff(bool has4x, bool has8x, long? mb, bool shared, int size) =>
        Assert.Equal(size, HdTextures.Pick(has4x, has8x, mb, shared));

    /// <summary>A saved shell.cfg size is kept as it is: a 4 stays the 4x on a machine whose first start would take the 8x, and an 8 stays the 8x on one that would not.</summary>
    [Theory]
    [InlineData(4, 8, 4)]      // saved 4x on a machine that would start on the 8x: kept
    [InlineData(8, 4, 8)]      // saved 8x on one that would start on the 4x: kept
    [InlineData(3, 8, 3)]
    [InlineData(5, 8, 4)]      // not a size: the default
    [InlineData(null, 8, 8)]   // a file from before the key (never chose a size): the first start's
    [InlineData(null, 4, 4)]
    public void ASavedSizeIsKept(int? saved, int firstStart, int size) => Assert.Equal(size, HdTextures.SizeFromFile(saved, firstStart));

    [Fact]
    public void AFileWithoutTheKeyTakesTheFirstStartsDefaultUnlessTheMenuArtIsOff()
    {
        Assert.True(HdTextures.FromFile(null, hdMenuArt: true, firstStart: true));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: true, firstStart: false));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: false, firstStart: true));
        Assert.True(HdTextures.FromFile(true, hdMenuArt: false, firstStart: false));   // the key wins
    }
}

/// <summary>A fresh block, and so the HD Textures row's Reset, takes this machine's first-start choice (ShellSettings.HdDefault, set by Load from HdTextures.FirstStartPick).</summary>
[CollectionDefinition("HdDefault", DisableParallelization = true)]
public class HdDefaultCollection { }

[Collection("HdDefault")]
public class HdTexturesResetTests
{
    [Theory]
    [InlineData(true, 8)]
    [InlineData(true, 4)]
    [InlineData(false, 4)]
    public void TheRowsResetPutsBackTheFirstStartsChoice(bool on, int size)
    {
        var was = ShellSettings.HdDefault;
        try
        {
            ShellSettings.HdDefault = (on, size);
            var s = new ShellSettings { HdTextures = !on, HdTextureSize = 2 };
            GraphicsPages.Reset(s, GraphicsRow.HdTextures);
            Assert.Equal((on, size), (s.HdTextures, s.HdTextureSize));
        }
        finally { ShellSettings.HdDefault = was; }
    }

    [Fact]
    public void BeforeLoadAFreshBlockIsOnAt4x() => Assert.Equal((true, 4), ShellSettings.HdDefault);
}
