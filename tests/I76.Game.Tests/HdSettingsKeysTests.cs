using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The HD packs' switches ([port] hd_textures, Graphic Detail's HD Textures row; hd_menu_art, no row): on by default, a saved Off kept, files from before the
/// menu art's switch on; both on the Developer panel's Settings, applied live.
/// </summary>
public class HdSettingsKeysTests
{
    [Theory]
    [InlineData(null, 13, true)]     // absent: on
    [InlineData(true, 13, true)]
    [InlineData(false, 13, false)]   // the row's saved Off stays off
    [InlineData(false, 3, true)]     // before version 4 there was no switch
    public void MenuArtReadsBack(bool? key, int version, bool expected) => Assert.Equal(expected, HdAssets.FromFile(key, version));

    [Fact]
    public void BothAreOnByDefault()
    {
        var s = new ShellSettings();
        Assert.True(s.HdTextures);
        Assert.True(s.HdMenuArt);
    }

    [Theory]
    [InlineData("hd_textures")]
    [InlineData("hd_menu_art")]
    public void TheDeveloperPanelTogglesThem(string key)
    {
        var e = Assert.Single(DevSettings.All.Concat(DevSettings.WithRow), d => d.Key == key);
        var s = new ShellSettings();
        Assert.Equal(1, e.Get(s));
        e.Set(s, 0);
        Assert.Equal(0, e.Get(s));
        Assert.False(key == "hd_textures" ? s.HdTextures : s.HdMenuArt);
        Assert.Equal(["false", "true*"], e.Values);
    }
}
