using Godot;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Mirror AA (MirrorAaRow): the key, its default (the mirrors as before: none), and what each choice sets on a mirror's viewport. Without the engine.</summary>
public class MirrorAaTests
{
    [Fact]
    public void OffByDefaultAndReadsBack()
    {
        Assert.Equal(MirrorAaRow.Off, new ShellSettings().MirrorAa);
        Assert.Equal(["Off", "2x*", "4x*", "8x*", "SMAA*", "2x+SMAA*", "As View*"], MirrorAaRow.Names);
        Assert.Null(MirrorAaRow.Word(MirrorAaRow.Off));   // the default is not written
        for (int v = 0; v < MirrorAaRow.Names.Length; v++) Assert.Equal(v, MirrorAaRow.Parse(MirrorAaRow.Word(v)));
        Assert.Equal(MirrorAaRow.Off, MirrorAaRow.Parse(""));
        Assert.Equal(MirrorAaRow.Off, MirrorAaRow.Parse(null));
        Assert.Equal(MirrorAaRow.Off, MirrorAaRow.Parse("bogus"));
        Assert.Equal(MirrorAaRow.Smaa, MirrorAaRow.Parse("SMAA"));
        var s = new ShellSettings { MirrorAa = MirrorAaRow.AsView };
        MirrorAaRow.Step(s);
        Assert.Equal(MirrorAaRow.Off, s.MirrorAa);
    }

    [Fact]
    public void OffIsWhatTheMirrorsHadBefore()
    {
        // a SubViewport's own defaults: no MSAA, no screen-space AA (it takes nothing from the main view)
        Assert.Equal((Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(MirrorAaRow.Off, false, false, 3, true));
    }

    [Fact]
    public void EachChoiceSetsItsAa()
    {
        Assert.Equal((Viewport.Msaa.Msaa2X, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(1, false, false, 0, false));
        Assert.Equal((Viewport.Msaa.Msaa4X, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(2, false, false, 0, false));
        Assert.Equal((Viewport.Msaa.Msaa8X, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(3, false, false, 0, false));
        Assert.Equal((Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Smaa), MirrorAaRow.Of(MirrorAaRow.Smaa, false, false, 0, false));
        Assert.Equal((Viewport.Msaa.Msaa2X, Viewport.ScreenSpaceAAEnum.Smaa), MirrorAaRow.Of(MirrorAaRow.Msaa2Smaa, false, false, 0, false));
        // As View: the Anti-Aliasing row's MSAA byte and SMAA
        Assert.Equal((Viewport.Msaa.Msaa8X, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(MirrorAaRow.AsView, false, false, 3, false));
        Assert.Equal((Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Smaa), MirrorAaRow.Of(MirrorAaRow.AsView, false, false, 0, true));
    }

    [Fact]
    public void The1997LookAndCompatibilityRenderer()
    {
        for (int v = 0; v < MirrorAaRow.Names.Length; v++)
            Assert.Equal((Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(v, true, false, 3, true));   // the original's mirror
        Assert.Equal((Viewport.Msaa.Msaa4X, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(3, false, true, 0, false));     // no 8x, no SMAA
        Assert.Equal((Viewport.Msaa.Disabled, Viewport.ScreenSpaceAAEnum.Disabled), MirrorAaRow.Of(MirrorAaRow.Smaa, false, true, 0, false));
    }

    [Fact]
    public void CopiedWithItsPage()
    {
        var to = new ShellSettings();
        GraphicsPages.Copy(GraphicsRow.MirrorAa, new ShellSettings { MirrorAa = MirrorAaRow.Smaa }, to);
        Assert.Equal(MirrorAaRow.Smaa, to.MirrorAa);
        Assert.Contains(GraphicsRow.MirrorAa, GraphicsPages.Rows[GraphicsPages.CarCockpit]);
    }
}
