using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Graphic Detail's HD Textures ([port] hd_textures): the key when the file has it, else the HD Menu Art switch, which turned the
/// textures off too before they had a row; the row marks Off as the original's (it draws the textures it ships).
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

    [Fact]
    public void OffIsMarkedAsTheOriginals() => Assert.Equal(["Off*", "On"], HdTextures.Names);
}
