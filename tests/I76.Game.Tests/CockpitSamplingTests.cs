using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The cockpit's own art (the z* textures: dash, gauges, panels, wheel). The 1997 look reads it at its full-size level and
/// point-sampled, as the original's point sampler drew it: measured on the speedometer's face at 640x480, the original's
/// lettering is sharp and its face dark, where a filtered or mipmapped read greyed both. The modern look reads the same art
/// through a filtered sampler (i76_cockpit_soft): linear through the mip levels with anisotropy where the screen minifies the
/// art (a pack's 8x at 3440x1440), and where it magnifies it each texel flat with only its edge blended, so the painted
/// pixels keep that sharpness without their edges crawling as the dash sways. Both clamped at the edges, so the steering
/// wheel's rim does not wrap.
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
    public void TheModernLooksSamplerIsFilteredThroughItsLevelsAndClamped()
    {
        var line = Regex.Match(Object, @"^uniform sampler2D albedo_cockpit_soft : .*$", RegexOptions.Multiline).Value;
        Assert.Contains("filter_linear_mipmap_anisotropic", line);
        Assert.Contains("repeat_disable", line);
    }

    [Fact]
    public void The1997LookReadsTheCockpitArtAtItsFullSizeLevelAndTheModernLookThroughTheSoftSampler()
    {
        var lines = Regex.Matches(Object, @"^\s*vec4 c = use_texture \? .*$", RegexOptions.Multiline).Select(m => m.Value).ToList();
        Assert.Equal(2, lines.Count);
        // the 1997 look (3D Card and Software alike), under FAITHFUL, then the modern look
        Assert.Contains("prelit ? textureLod(albedo_cockpit, tuv, 0.0)", lines[0]);
        Assert.Contains("prelit ? i76_cockpit_soft(tuv)", lines[1]);
        // the soft read: the level from the true footprint, the footprint held to a texel at most, so a magnified texel stays flat
        var soft = Regex.Match(Object, @"vec4 i76_cockpit_soft\(vec2 uv\) \{.*?\n\}", RegexOptions.Singleline).Value;
        Assert.Contains("textureGrad(albedo_cockpit_soft", soft);
        Assert.Contains("clamp(fwidth(p), vec2(1e-4), vec2(1.0))", soft);
    }

    [Fact]
    public void TheMaterialsFeedBothCockpitSamplers()
    {
        var lib = Repo("game", "Scripts", "Assets", "MaterialLibrary.cs");
        Assert.Contains("SetCockpitArt(mat, t.Texture)", lib);
        var helper = Regex.Match(lib, @"public static void SetCockpitArt\(ShaderMaterial m, Texture2D t\)\s*\{.*?\}", RegexOptions.Singleline).Value;
        Assert.Contains("SetShaderParameter(\"albedo_cockpit\", t)", helper);
        Assert.Contains("SetShaderParameter(\"albedo_cockpit_soft\", t)", helper);
        // no material sets the one sampler without the other
        Assert.DoesNotContain("SetShaderParameter(\"albedo_cockpit\", t.Texture)", lib);
        Assert.DoesNotContain("albedo_nearest", lib);
        Assert.DoesNotContain("albedo_nearest", Object);
    }
}
