using Godot;
using I76.Game.Boot;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Graphic Detail's Render Scale and the Anti-Aliasing row's 8x step.</summary>
public class RenderScaleTests
{
    [Fact] public void ScalesRunFrom50To200PercentWith100TheDefault()
    {
        Assert.Equal([50, 67, 75, 85, 100, 150, 200], RenderScale.Percents);
        Assert.Equal(RenderScale.Percents.Length, RenderScale.Names.Length);
        Assert.Equal(1f, RenderScale.Scale(RenderScale.Default));
        Assert.Equal(0.5f, RenderScale.Scale(0));
        Assert.Equal(0.75f, RenderScale.Scale(2));
        Assert.Equal(2f, RenderScale.Scale(6));
        Assert.Equal(2f, RenderScale.Scale(9));   // clamped
        Assert.Equal(0.5f, RenderScale.Scale(-1));
        Assert.Equal(RenderScale.Default, new I76.Game.Shell.ShellSettings().RenderScale);
    }

    [Fact] public void ParseKeepsTheDefaultForAnythingNotOffered()
    {
        Assert.Equal(RenderScale.Default, RenderScale.Parse(null));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(100));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(175));
        Assert.Equal(RenderScale.Default, RenderScale.Parse(0));
        Assert.Equal(0, RenderScale.Parse(50));
        Assert.Equal(2, RenderScale.Parse(75));
        Assert.Equal(5, RenderScale.Parse(150));
        Assert.Equal(6, RenderScale.Parse(200));
    }

    [Fact] public void TheAntiAliasingRowHasEightX()
    {
        Assert.Equal(["Off", "2x", "4x", "8x"], RenderScale.AntiAliasing);
        Assert.Equal(Viewport.Msaa.Disabled, RenderScale.Msaa(0, false));
        Assert.Equal(Viewport.Msaa.Msaa2X, RenderScale.Msaa(1, false));
        Assert.Equal(Viewport.Msaa.Msaa4X, RenderScale.Msaa(2, false));
        Assert.Equal(Viewport.Msaa.Msaa8X, RenderScale.Msaa(3, false));
    }

    [Fact] public void EightXIsFourXWhereTheRendererHasNoEightX() => Assert.Equal(Viewport.Msaa.Msaa4X, RenderScale.Msaa(3, true));
}
