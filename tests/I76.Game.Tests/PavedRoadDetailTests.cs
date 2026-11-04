using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's paved roads: the road layer holds the original's 64-texel road textures at 6.4 texels a metre, so
/// near the camera the asphalt scan's aggregate goes over the paved part (not the dirt tracks), and textures are filtered
/// at the highest anisotropy, which the layer seen along a road at a grazing angle needs.
/// </summary>
public class PavedRoadDetailTests
{
    private static string Game(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, "game", .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, "game", .. path]));
    }

    [Fact]
    public void TheScanGoesOverThePavedRoadOnly()
    {
        var s = Game("Shaders", "terrain_modern.gdshaderinc");
        Assert.Matches(@"if \(has_asphalt && paved > 0\.0", s);
        // the colour is scaled by the scan's luminance against its mean (the layer's hue and markings stay), weighted by paved
        Assert.Matches(@"ground \*= mix\(1\.0, .*af \* paved\)", s);
        // and its relief is added after the ground's bumps are taken off the paved road
        int flat = s.IndexOf("grad *= far * (1.0 - paved);", StringComparison.Ordinal), add = s.IndexOf("grad += agrad;", StringComparison.Ordinal);
        Assert.True(flat > 0 && add > flat);
    }

    [Fact]
    public void TexturesAreFilteredAtSixteenTimesAnisotropy()
        => Assert.Matches(new Regex(@"^textures/default_filters/anisotropic_filtering_level=4\s*$", RegexOptions.Multiline), Game("project.godot"));
}
