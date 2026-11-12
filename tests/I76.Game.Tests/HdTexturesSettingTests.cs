using I76.Game.Assets;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The texture pack's two keys ([port] world_textures and car_textures: Graphic Detail's World Textures and Car &amp; Dash Textures),
/// their words, and the migration from the keys of the HD Textures, 8x Dash and 8x Cars rows before them (hd_textures, hd_dash_size,
/// hd_car_size, and the single hd_texture_size before those two).
/// </summary>
public class HdTexturesSettingTests
{
    private static readonly (int, int) First = (4, 8);

    [Theory]
    [InlineData(0, "1997")]
    [InlineData(4, "4x")]
    [InlineData(8, "8x")]
    [InlineData(2, "2x")]
    public void ASizeIsWrittenAsAWordAndReadBack(int size, string word)
    {
        Assert.Equal(word, HdTextures.Word(size));
        Assert.Equal(size, HdTextures.ParseWord(word));
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData("8X", 8)]
    [InlineData("off", 0)]
    [InlineData("0", 0)]
    [InlineData("5x", null)]
    [InlineData("big", null)]
    [InlineData(null, null)]
    public void AWordByHandIsReadWhereItMeansASize(string? word, int? size) => Assert.Equal(size, HdTextures.ParseWord(word));

    /// <summary>The new keys win over everything the file had before them.</summary>
    [Fact]
    public void TheNewKeysWin()
    {
        Assert.Equal((0, 8), HdTextures.FromFile("1997", "8x", false, false, 4, 4, 4, First));
        Assert.Equal((4, 0), HdTextures.FromFile("4x", "1997", true, true, 8, 8, null, First));
        // a word that is no size: the first start's
        Assert.Equal((4, 8), HdTextures.FromFile("huge", "huge", null, true, null, null, null, First));
    }

    /// <summary>
    /// A file from before the keys: HD Textures Off is 1997 for both; on, the world at the cars' old size (an 8 is the 4x: an 8x pack
    /// holds no world texture) and the cars and the dash together at the dash's old size, which decided the memory.
    /// </summary>
    [Theory]
    [InlineData(false, true, 8, 8, null, 0, 0)]       // HD Textures Off
    [InlineData(true, true, 8, 8, null, 4, 8)]        // both 8x: the world 4x, the cars and the dash 8x
    [InlineData(true, true, 4, 8, null, 4, 8)]        // dash 4x and cars 8x on a machine whose first start takes the 8x: 8x
    [InlineData(true, true, 8, 4, null, 4, 8)]        // the dash's 8x chosen
    [InlineData(true, true, null, null, 8, 4, 8)]     // the single hd_texture_size before the two keys
    [InlineData(true, true, null, null, 3, 3, 3)]     // a 3x set by hand
    [InlineData(true, true, null, null, null, 4, 8)]  // on with no size: the first start's
    [InlineData(null, false, 8, 8, null, 0, 0)]       // no hd_textures and the menu art off: off, as before the key
    [InlineData(null, true, 4, 4, null, 4, 4)]        // no hd_textures, the menu art on: on
    public void TheOldKeysReadAsTheyMeant(bool? hd, bool menuArt, int? dash, int? car, int? single, int world, int carDash) =>
        Assert.Equal((world, carDash), HdTextures.FromFile(null, null, hd, menuArt, dash, car, single, First));

    /// <summary>The old dash's and cars' sizes as one: the dash's 8x is kept; the cars' 8x alone only where the dash's 8x fits (the first start's pick), else the 4x.</summary>
    [Theory]
    [InlineData(8, 4, 4, 8)]        // the dash's 8x chosen: kept
    [InlineData(4, 8, 8, 8)]        // the cars' 8x on a 6 GB card: 8x
    [InlineData(4, 8, 4, 4)]        // the 4 GB laptop's first start (dash 4x, cars 8x): 4x
    [InlineData(4, 4, 8, 4)]
    [InlineData(3, 2, 8, 3)]
    [InlineData(null, null, 8, 8)]
    public void TheOldPairBecomesOneSize(int? dash, int? car, int first, int size) => Assert.Equal(size, HdTextures.OldCarAndDash(dash, car, first));

    /// <summary>A machine whose first start is 1997 (no pack, or a small card) still reads an old file's explicit On as the 4x.</summary>
    [Fact]
    public void AnOldOnIsTheFourXEvenWhereTheFirstStartWouldBe1997() =>
        Assert.Equal((4, 4), HdTextures.FromFile(null, null, true, true, null, null, null, (0, 0)));
}

/// <summary>The first start's World Textures and Car &amp; Dash Textures (HdTextures.Pick, DefaultOn, PickCar).</summary>
public class HdTexturesFirstStartTests
{
    [Theory]
    [InlineData(true, 11264L, true)]      // the 1080 Ti
    [InlineData(true, 4096L, true)]       // a 4 GB laptop GTX 1650 Ti: the 4x pack fits
    [InlineData(true, 3840L, true)]
    [InlineData(true, 3839L, false)]
    [InlineData(true, 2048L, false)]
    [InlineData(true, null, true)]        // unknown memory (Linux) counts as enough
    [InlineData(false, 11264L, false)]    // no tex-world-4x
    public void FourXWhenItsFolderIsThereAndTheGpuHasRoom(bool has4x, long? mb, bool on) => Assert.Equal(on, HdTextures.DefaultOn(has4x, mb));

    /// <summary>
    /// HdTextures.PickCar: the cars' and the dash's 8x together (about 510 MB over the 4x, the dash's 482 of it) with tex-world-8x
    /// beside the 4x and a 6 GB card (the card's own memory) or a 16 GB Mac (the machine's, shared), else the 4x.
    /// </summary>
    [Theory]
    [InlineData(true, true, 11264L, false, 8)]    // the 1080 Ti
    [InlineData(true, true, 6144L, false, 8)]     // a 6 GB card
    [InlineData(true, true, 6143L, false, 8)]     // a driver that reports a MiB short
    [InlineData(true, true, 5120L, false, 4)]     // a 5 GB card
    [InlineData(true, true, 4096L, false, 4)]     // the laptop's 4 GB card
    [InlineData(true, true, 16384L, true, 8)]     // a 16 GB Mac
    [InlineData(true, true, 8192L, true, 4)]      // an 8 GB Mac
    [InlineData(true, true, 8192L, false, 8)]     // an 8 GB card
    [InlineData(true, false, 11264L, false, 4)]   // no tex-world-8x: the 4x
    [InlineData(false, true, 11264L, false, 4)]   // an 8x with no 4x under it
    [InlineData(true, true, null, false, 4)]      // unknown memory (Linux): never the 8x
    public void TheCarsAndDash8xWhereTheDashFits(bool has4x, bool has8x, long? mb, bool shared, int size) =>
        Assert.Equal(size, HdTextures.PickCar(has4x, has8x, mb, shared));

    [Theory]
    [InlineData(true, true, 11264L, false, 4, 8)]    // the owner's desktop: 4x and 8x
    [InlineData(true, true, 4096L, false, 4, 4)]     // the laptop: 4x and 4x
    [InlineData(true, false, 11264L, false, 4, 4)]   // no 8x folder
    [InlineData(true, true, 3072L, false, 0, 0)]     // a 3 GB card: 1997 both
    [InlineData(false, false, null, false, 0, 0)]    // no pack
    [InlineData(false, true, 11264L, false, 0, 8)]   // the 8x cockpit-and-cars pack alone: its 8x, the world 1997 (it has none)
    [InlineData(false, true, 4096L, false, 0, 8)]    // on a 4 GB card too: it takes less than the 4x would
    [InlineData(false, true, 3072L, false, 0, 0)]    // a 3 GB card: 1997, as for the 4x
    public void TheFirstStartsPair(bool has4x, bool has8x, long? mb, bool shared, int world, int car) =>
        Assert.Equal((world, car), HdTextures.Pick(has4x, has8x, mb, shared));
}

/// <summary>
/// [port] texture_packs and HdTextures.WithNewPacks: a 1997 saved with no pack there (nothing else to choose) gives way to the first
/// start's choice once a pack is installed; a 1997 chosen with a pack there stays.
/// </summary>
public class HdTexturesNewPackTests
{
    private static readonly (int, int) First = (4, 8);

    [Theory]
    [InlineData(new int[0], false, "none")]
    [InlineData(new[] { 8, 4 }, false, "8x 4x")]
    [InlineData(new[] { 4 }, true, "4x tex-world")]
    public void ThePacksAreWrittenAndReadBack(int[] sizes, bool plain, string word)
    {
        Assert.Equal(word, HdTextures.PacksWord(sizes, plain));
        Assert.Equal(sizes.Concat(plain ? [0] : Array.Empty<int>()), HdTextures.ParsePacks(word)!);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("4x big")]
    public void AWordItDidNotWriteIsNone(string? word) => Assert.Null(HdTextures.ParsePacks(word));

    /// <summary>The verification lane's case (e): a settings file saved before the pack was installed keeps 1997 no more.</summary>
    [Fact]
    public void A1997SavedWithNoPackTakesTheFirstStartsChoiceOnceAPackIsThere()
    {
        Assert.Equal((4, 8), HdTextures.WithNewPacks((0, 0), [], [8, 4], true, First));
        // a file from before the key, both rows 1997: saved with no pack (with one the rows start at 4x)
        Assert.Equal((4, 8), HdTextures.WithNewPacks((0, 0), null, [8, 4], true, First));
        // the first start of a small card is 1997: it stays
        Assert.Equal((0, 0), HdTextures.WithNewPacks((0, 0), [], [4], true, (0, 0)));
        // still no pack: nothing to change
        Assert.Equal((0, 0), HdTextures.WithNewPacks((0, 0), [], [], false, First));
    }

    /// <summary>A 1997 chosen with a pack there is the player's and survives a new pack.</summary>
    [Fact]
    public void A1997ChosenWithAPackThereStays()
    {
        Assert.Equal((0, 0), HdTextures.WithNewPacks((0, 0), [4], [8, 4], true, First));
        Assert.Equal((0, 8), HdTextures.WithNewPacks((0, 8), [8, 4], [8, 4], true, First));
        Assert.Equal((4, 0), HdTextures.WithNewPacks((4, 0), null, [8, 4], true, First));
    }

    /// <summary>Car &amp; Dash at 4x saved with only the 4x there: the 8x installed since is taken where the first start takes it.</summary>
    [Fact]
    public void ThePairSavedWithoutThe8xTakesItWhereTheFirstStartWould()
    {
        Assert.Equal((4, 8), HdTextures.WithNewPacks((4, 4), [4], [8, 4], true, First));
        Assert.Equal((4, 4), HdTextures.WithNewPacks((4, 4), [4], [8, 4], true, (4, 4)));    // a 4 GB card
        Assert.Equal((4, 4), HdTextures.WithNewPacks((4, 4), [8, 4], [8, 4], true, First));  // the 4x chosen with the 8x there
        Assert.Equal((4, 4), HdTextures.WithNewPacks((4, 4), null, [8, 4], true, First));    // before the key: cannot tell, kept
    }
}

/// <summary>Car &amp; Dash Textures' sizes: the 8x with the 4x, or the 8x alone when it is the only pack.</summary>
public class TextureRowSizesTests
{
    [Fact]
    public void TheCarRowOffersThe8xWithOrWithoutThe4x()
    {
        Assert.Equal([0, 4, 8], TextureRow.Sizes(GraphicsRow.CarTextures, [8, 4]));
        Assert.Equal([0, 8], TextureRow.Sizes(GraphicsRow.CarTextures, [8]));
        Assert.Equal([0, 4], TextureRow.Sizes(GraphicsRow.CarTextures, [4]));
        Assert.Equal([0, 4], TextureRow.Sizes(GraphicsRow.WorldTextures, [8, 4]));
        var s = new ShellSettings { CarTextures = 8 };
        Assert.Equal("8x" + ShellSettings.PortMark, TextureRow.Names(s, GraphicsRow.CarTextures, [8])[TextureRow.Index(s, GraphicsRow.CarTextures, [8])]);
    }
}

/// <summary>A fresh block, and so the rows' Reset, takes this machine's first-start choice (ShellSettings.HdDefault, set by Load from HdTextures.FirstStart).</summary>
[CollectionDefinition("HdDefault", DisableParallelization = true)]
public class HdDefaultCollection { }

[Collection("HdDefault")]
public class HdTexturesResetTests
{
    [Theory]
    [InlineData(4, 8)]
    [InlineData(4, 4)]
    [InlineData(0, 0)]
    public void TheRowsResetPutsBackTheFirstStartsChoice(int world, int car)
    {
        var was = ShellSettings.HdDefault;
        try
        {
            ShellSettings.HdDefault = (world, car);
            var s = new ShellSettings { WorldTextures = 2, CarTextures = 2 };
            GraphicsPages.Reset(s, GraphicsRow.WorldTextures);
            Assert.Equal((world, 2), (s.WorldTextures, s.CarTextures));
            GraphicsPages.Reset(s, GraphicsRow.CarTextures);
            Assert.Equal((world, car), (s.WorldTextures, s.CarTextures));
        }
        finally { ShellSettings.HdDefault = was; }
    }

    [Fact]
    public void BeforeLoadAFreshBlockIs4xAnd4x() => Assert.Equal((4, 4), ShellSettings.HdDefault);
}
