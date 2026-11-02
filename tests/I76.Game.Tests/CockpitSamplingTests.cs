using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The cockpit's own art (the z* textures: dash, gauges, panels, wheel) goes through the same object path as every
/// other object: bilinear on the 3D card (the Glide renderer asks for bilinear on every polygon with the 16-bit art),
/// point-sampled by the Software renderer. It was point-sampled in every look, so the dash magnified on a large window
/// showed its texels as slanted blocks when the head looked down at it.
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
    public void TheCockpitSamplerIsBilinearAndClamped()
    {
        var line = Regex.Match(Object, @"^uniform sampler2D albedo_cockpit : .*$", RegexOptions.Multiline).Value;
        Assert.Contains("filter_linear", line);
        Assert.DoesNotContain("filter_nearest", line);
        Assert.Contains("repeat_disable", line);
    }

    [Fact]
    public void The3DCardFiltersTheCockpitAndTheSoftwareRendererPointSamplesIt()
    {
        var lines = Regex.Matches(Object, @"^\s*vec4 c = use_texture \? .*$", RegexOptions.Multiline).Select(m => m.Value).ToList();
        Assert.Equal(2, lines.Count);
        // the 1997 look
        Assert.Contains("prelit ? (i76_software ? i76_point_clamped(albedo_cockpit, tuv) : texture(albedo_cockpit, tuv))", lines[0]);
        // the modern look
        Assert.Contains("prelit ? texture(albedo_cockpit, tuv)", lines[1]);
        Assert.Contains("#define i76_point_clamped(", Repo("game", "Shaders", "software.gdshaderinc"));
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
