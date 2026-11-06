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
    /// HdTextures.PickDash: the dash's 8x (the cockpit's art, drawn near full screen: about half a gigabyte, docs/perf/costs.md) with
    /// tex-world-8x beside the 4x and a 6 GB card (Windows: the card's own memory) or a 16 GB Mac (the machine's memory, shared), else
    /// the 4x. Off is the switch's business (DefaultOn), not a size's.
    /// </summary>
    [Theory]
    [InlineData(true, true, 11264L, false, 8)]    // the 1080 Ti
    [InlineData(true, true, 6144L, false, 8)]     // a 6 GB card
    [InlineData(true, true, 6143L, false, 8)]     // a driver that reports a MiB short
    [InlineData(true, true, 5120L, false, 4)]     // a 5 GB card: the 4x
    [InlineData(true, true, 4096L, false, 4)]     // the laptop's 4 GB GTX 1650 Ti: the 4x
    [InlineData(true, true, 3072L, false, 4)]     // a 3 GB card: the 4x (and the switch off)
    [InlineData(true, true, 16384L, true, 8)]     // a 16 GB Mac
    [InlineData(true, true, 24576L, true, 8)]     // a 24 GB Mac
    [InlineData(true, true, 8192L, true, 4)]      // an 8 GB Mac: the 4x, though 8 GB of video memory would be enough on a card
    [InlineData(true, true, 8192L, false, 8)]     // an 8 GB card
    [InlineData(true, false, 11264L, false, 4)]   // no tex-world-8x: the 4x
    [InlineData(true, false, 16384L, true, 4)]
    [InlineData(false, true, 11264L, false, 4)]   // an 8x with no 4x under it: the rows offer the 8x only over the 4x
    [InlineData(false, false, 11264L, false, 4)]  // no pack
    [InlineData(true, true, null, false, 4)]      // unknown memory (Linux): the 4x, never the 8x
    public void TheDash8xWhereItFitsElseThe4x(bool has4x, bool has8x, long? mb, bool shared, int size) =>
        Assert.Equal(size, HdTextures.PickDash(has4x, has8x, mb, shared));

    /// <summary>HdTextures.PickCar: a car's textures are small and only the scene's cars are loaded (tens of megabytes, docs/perf/costs.md), so the cars' 8x fits wherever the 4x does: tex-world-8x beside the 4x and ~3.8 GB, a Mac of any size.</summary>
    [Theory]
    [InlineData(true, true, 11264L, false, 8)]    // the 1080 Ti
    [InlineData(true, true, 6144L, false, 8)]
    [InlineData(true, true, 4096L, false, 8)]     // the laptop's 4 GB card: the cars' 8x, the dash's 4x
    [InlineData(true, true, 3840L, false, 8)]     // the 4x's own threshold
    [InlineData(true, true, 3072L, false, 4)]     // a 3 GB card (the switch is off there anyway)
    [InlineData(true, true, 8192L, true, 8)]      // an 8 GB Mac: the cars' 8x, the dash's 4x
    [InlineData(true, true, 16384L, true, 8)]
    [InlineData(true, false, 11264L, false, 4)]   // no tex-world-8x
    [InlineData(false, true, 11264L, false, 4)]   // an 8x with no 4x under it
    [InlineData(true, true, null, false, 4)]      // unknown memory (Linux): never the 8x
    public void TheCars8xWhereThe4xFits(bool has4x, bool has8x, long? mb, bool shared, int size) =>
        Assert.Equal(size, HdTextures.PickCar(has4x, has8x, mb, shared));

    /// <summary>The switch: on with tex-world-4x and ~3.8 GB (or unknown memory), whatever the 8x would be.</summary>
    [Theory]
    [InlineData(true, 6144L, true)]
    [InlineData(true, 3840L, true)]
    [InlineData(true, 3839L, false)]
    [InlineData(true, null, true)]
    [InlineData(false, 11264L, false)]
    public void TheSwitchIsOnWhereThe4xFits(bool has4x, long? mb, bool on) => Assert.Equal(on, HdTextures.DefaultOn(has4x, mb));

    /// <summary>A saved shell.cfg size is kept as it is: a 4 stays the 4x on a machine whose first start would take the 8x, and an 8 stays the 8x on one that would not.</summary>
    [Theory]
    [InlineData(4, 8, 4)]      // saved 4x on a machine that would start on the 8x: kept
    [InlineData(8, 4, 8)]      // saved 8x on one that would start on the 4x: kept
    [InlineData(3, 8, 3)]
    [InlineData(5, 8, 4)]      // not a size: the default
    [InlineData(null, 8, 8)]   // a file from before the key (never chose a size): the first start's
    [InlineData(null, 4, 4)]
    public void ASavedSizeIsKept(int? saved, int firstStart, int size) => Assert.Equal(size, HdTextures.SizeFromFile(saved, firstStart));

    /// <summary>
    /// The two keys (hd_dash_size, hd_car_size) and the migration from the one before them (hd_texture_size): a file with only the old
    /// key sets both from it (an 8 both to 8, a 4 both to 4); a new key beats the old; neither, the first start's for each kind.
    /// </summary>
    [Theory]
    [InlineData(null, null, 8, 4, 8, 8, 8)]      // an old file's 8: both 8, though this machine would start the dash on the 4x
    [InlineData(null, null, 4, 8, 8, 4, 4)]      // an old file's 4: both 4, though this machine would start both on the 8x
    [InlineData(8, 4, 8, 4, 4, 8, 4)]            // the two keys win over the old one
    [InlineData(8, null, 4, 4, 4, 8, 4)]         // one new key and the old one: the old fills the other
    [InlineData(null, 8, null, 4, 4, 4, 8)]      // one new key alone: the first start fills the other
    [InlineData(null, null, null, 4, 8, 4, 8)]   // no key: the first start's, apart (the 4 GB laptop: dash 4x, cars 8x)
    [InlineData(null, null, null, 4, 4, 4, 4)]
    [InlineData(5, 7, 8, 4, 4, 4, 4)]            // not sizes: the default
    public void TheOldSingleSizeSetsBothUntilTheNewKeysAreWritten(int? dash, int? car, int? old, int firstDash, int firstCar, int expectDash, int expectCar) =>
        Assert.Equal((expectDash, expectCar), HdTextures.SizesFromFile(dash, car, old, firstDash, firstCar));

    [Fact]
    public void AFileWithoutTheKeyTakesTheFirstStartsDefaultUnlessTheMenuArtIsOff()
    {
        Assert.True(HdTextures.FromFile(null, hdMenuArt: true, firstStart: true));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: true, firstStart: false));
        Assert.False(HdTextures.FromFile(null, hdMenuArt: false, firstStart: true));
        Assert.True(HdTextures.FromFile(true, hdMenuArt: false, firstStart: false));   // the key wins
    }
}

/// <summary>A fresh block, and so the HD Textures, 8x Dash and 8x Cars rows' Reset, takes this machine's first-start choice (ShellSettings.HdDefault, set by Load from HdTextures.FirstStart).</summary>
[CollectionDefinition("HdDefault", DisableParallelization = true)]
public class HdDefaultCollection { }

[Collection("HdDefault")]
public class HdTexturesResetTests
{
    [Theory]
    [InlineData(true, 8, 8)]
    [InlineData(true, 4, 8)]     // the 4 GB card: the dash's 4x, the cars' 8x
    [InlineData(true, 4, 4)]
    [InlineData(false, 4, 4)]
    public void TheRowsResetPutsBackTheFirstStartsChoice(bool on, int dash, int car)
    {
        var was = ShellSettings.HdDefault;
        try
        {
            ShellSettings.HdDefault = (on, dash, car);
            var s = new ShellSettings { HdTextures = !on, HdDashSize = 2, HdCarSize = 2 };
            GraphicsPages.Reset(s, GraphicsRow.HdTextures);
            Assert.Equal((on, 2, 2), (s.HdTextures, s.HdDashSize, s.HdCarSize));   // the switch's row alone
            GraphicsPages.Reset(s, GraphicsRow.HdDash);
            Assert.Equal((dash, 2), (s.HdDashSize, s.HdCarSize));
            GraphicsPages.Reset(s, GraphicsRow.HdCars);
            Assert.Equal((on, dash, car), (s.HdTextures, s.HdDashSize, s.HdCarSize));
        }
        finally { ShellSettings.HdDefault = was; }
    }

    [Fact]
    public void BeforeLoadAFreshBlockIsOnAt4x() => Assert.Equal((true, 4, 4), ShellSettings.HdDefault);
}
