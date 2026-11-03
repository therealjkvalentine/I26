using I76.Game.World;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Map Edge's setting: Off (the original's walls as they are) or On (drawn down at their foot), and the four-value row's numbers read forward.</summary>
public class MapEdgeTests
{
    [Fact]
    public void SettingParsesToOffWhenAbsentOrUnknown()
    {
        Assert.Equal(MapEdge.Off, MapEdge.Parse(null));
        Assert.Equal(MapEdge.Off, MapEdge.Parse("off"));
        Assert.Equal(MapEdge.On, MapEdge.Parse("on"));
        Assert.Equal(MapEdge.Off, MapEdge.Parse("hide"));
        Assert.Equal(MapEdge.Off, MapEdge.Parse(7));
        Assert.Equal("on", MapEdge.Key(MapEdge.On));
        Assert.Equal("off", MapEdge.Key(MapEdge.Off));
        Assert.Equal(["Off", "On*"], MapEdge.Names);   // the port's value is marked (ShellSettings.PortMark)
    }

    /// <summary>The row had Plain (0), Haze (1), Fade Out (2) and Hide (3): Hide is On, the rest Off.</summary>
    [Fact]
    public void TheFourValueRowsNumbersMigrate()
    {
        Assert.Equal(MapEdge.Off, MapEdge.Parse(0));
        Assert.Equal(MapEdge.Off, MapEdge.Parse(1));
        Assert.Equal(MapEdge.Off, MapEdge.Parse(2));
        Assert.Equal(MapEdge.On, MapEdge.Parse(3));
    }
}
