using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Menu Width ([port] menu_width, MenuWidthRow): Wide unless the file says 4:3, offered only with the widescreen menu pack, on
/// Advanced beside the texture rows, never on the in-game pad; and the boot splash keeping a wide HD card's shape. Without the engine.
/// </summary>
public class MenuWidthRowTests
{
    [Fact]
    public void WideUnlessTheFileSaysFourThree()
    {
        Assert.True(new ShellSettings().MenuWide);
        Assert.True(MenuWidthRow.FromFile(null));
        Assert.True(MenuWidthRow.FromFile("wide"));
        Assert.True(MenuWidthRow.FromFile("anything"));
        Assert.False(MenuWidthRow.FromFile("4:3"));
        Assert.False(MenuWidthRow.FromFile(" 4:3 "));
        Assert.Equal("4:3", MenuWidthRow.Word(false));
        Assert.True(MenuWidthRow.FromFile(MenuWidthRow.Word(true)));
    }

    [Fact]
    public void TheRowStepsBetweenTheTwoAndShowsOnlyWithThePack()
    {
        var s = new ShellSettings();
        Assert.Equal(["4:3", "Wide" + ShellSettings.PortMark], MenuWidthRow.Names);
        Assert.Equal(1, MenuWidthRow.Index(s));
        MenuWidthRow.Step(s);
        Assert.False(s.MenuWide);
        Assert.Equal(0, MenuWidthRow.Index(s));
        MenuWidthRow.Put(s, 1);
        Assert.True(s.MenuWide);
        Assert.False(MenuWidthRow.Offered(false));
        Assert.True(MenuWidthRow.Offered(true));
    }

    [Fact]
    public void ItSitsOnAdvancedAfterTheTextureRowsAndResetPutsWideBack()
    {
        var adv = GraphicsPages.Rows[GraphicsPages.Advanced];
        Assert.Equal(Array.IndexOf(adv, GraphicsRow.CarTextures) + 1, Array.IndexOf(adv, GraphicsRow.MenuWidth));
        var s = new ShellSettings { MenuWide = false };
        GraphicsPages.Reset(s, GraphicsPages.Advanced);
        Assert.True(s.MenuWide);
        var t = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.MenuWidth, new ShellSettings { MenuWide = false }, t);
        Assert.False(t.MenuWide);
        Assert.NotEmpty(GraphicsHelp.Text(GraphicsRow.MenuWidth, false));
    }

    [Theory]
    [InlineData(2560, 1920, 1920)]   // the 4:3 pack's card
    [InlineData(640, 480, 1920)]
    [InlineData(3414, 1920, 2560)]   // a 16:9 card keeps 16:9 at 1440 tall
    [InlineData(4588, 1920, 3441)]
    [InlineData(1000, 1000, 1920)]   // narrower than 4:3: the 4:3 box, as before
    public void TheSplashKeepsAWideCardsShape(int w, int h, int expected) =>
        Assert.Equal(expected, I76.Game.Boot.BootSplash.WideWidth(w, h));
}
