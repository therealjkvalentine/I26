using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The cockpit's own art (the z* textures: dash, gauges, panels, wheel) is read at its full-size level and point-sampled
/// in every look, as the original's point sampler drew it: measured on the speedometer's face at 640x480, the original's
/// lettering is sharp and its face dark, where a filtered or mipmapped read greyed both. Clamped at the edges, so the
/// steering wheel's rim does not wrap.
/// </summary>
public class CockpitSamplingTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    private static string Object => Repo("game", "Shaders", "object.gdshaderinc");

    [Fact]
    public void TheCockpitSamplerIsPointSampledAndClamped()
    {
        var line = Regex.Match(Object, @"^uniform sampler2D albedo_cockpit : .*$", RegexOptions.Multiline).Value;
        Assert.Contains("filter_nearest", line);
        Assert.DoesNotContain("filter_linear", line);
        Assert.Contains("repeat_disable", line);
    }

    [Fact]
    public void EveryLookReadsTheCockpitArtAtItsFullSizeLevel()
    {
        var lines = Regex.Matches(Object, @"^\s*vec4 c = use_texture \? .*$", RegexOptions.Multiline).Select(m => m.Value).ToList();
        Assert.Equal(2, lines.Count);
        // the 1997 look (3D Card and Software alike) and the modern look
        Assert.All(lines, l => Assert.Contains("prelit ? textureLod(albedo_cockpit, tuv, 0.0)", l));
    }

    [Fact]
    public void TheMaterialsFeedTheCockpitSampler()
    {
        var lib = Repo("game", "Scripts", "Assets", "MaterialLibrary.cs");
        Assert.Contains("SetShaderParameter(\"albedo_cockpit\", t.Texture)", lib);
        Assert.DoesNotContain("albedo_nearest", lib);
        Assert.DoesNotContain("albedo_nearest", Object);
    }
}
