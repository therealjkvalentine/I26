using I76.Game.Editor;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// #146: the map editor's tool column (terrain, roads, objects, undo) is about 1100 px tall at 1080p, so it ran under the status bar
/// and off the window below FLAG. It scrolls now, in a box no taller than the window leaves it.
/// </summary>
public class EditorToolColumnTests
{
    [Theory]
    [InlineData(1080f, 1f)]
    [InlineData(1440f, 1440f / 1080)]
    [InlineData(864f, 0.8f)]
    [InlineData(720f, 0.8f)]
    public void ATallColumnStopsAboveTheStatusBar(float window, float s)
    {
        float h = EditorStyle.ColumnHeight(1100 * s, window, s);
        Assert.True(h < 1100 * s);
        // the panel (the box and its 14 + 14 margins) from 70 down ends at least 54 above the window's bottom
        Assert.True(70 * s + h + 28 * s <= window - 54 * s + 0.01f);
    }

    [Fact]
    public void AColumnThatFitsIsShownWhole() => Assert.Equal(600f, EditorStyle.ColumnHeight(600, 1080, 1));

    [Fact]
    public void ATinyWindowStillShowsSomeOfIt() => Assert.Equal(80f, EditorStyle.ColumnHeight(1100, 200, 0.8f), 3);
}
