using System.Text.RegularExpressions;
using I76.Formats;
using I76.Formats.Assembly;
using I76.Formats.Tests;
using I76.Formats.Vfs;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The cockpit's own art (the z* textures: dash, gauges, panels, wheel). The 1997 look reads it through i76_cockpit: the
/// original's low-resolution art at its full-size level and point-sampled, as the original's point sampler drew it (measured on
/// the speedometer's face at 640x480, the original's lettering is sharp and its face dark, where a filtered or mipmapped read
/// greyed both); art larger than any of the original's (a high-resolution replacement) through the sampler with its mipmaps.
/// The modern look reads the same art through a filtered sampler (i76_cockpit_soft): linear through the mip levels with
/// anisotropy where the screen minifies the art (a pack's 8x at 3440x1440), and where it magnifies it each texel flat with only
/// its edge blended, so the painted pixels keep that sharpness without their edges crawling as the dash sways. Both clamped at
/// the edges, so the steering wheel's rim does not wrap.
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
        Assert.Contains("prelit ? i76_cockpit(tuv)", lines[0]);
        Assert.Contains("prelit ? i76_cockpit_soft(tuv)", lines[1]);
        // the 1997 read: the full-size level, point-sampled, unless the art is larger than the original's
        var fn = Regex.Match(Object, @"vec4 i76_cockpit\(vec2 uv\) \{.*?\n\}", RegexOptions.Singleline).Value;
        Assert.Contains("> 256) return texture(albedo_cockpit, uv)", fn);
        Assert.Contains("texelFetch(albedo_cockpit,", fn);
        Assert.Contains(", 0);", fn);
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

    /// <summary>
    /// The rule's line: no face of the game's 3D cockpits (every player car's interior, detail 5) carries cockpit art over 256
    /// texels a side, the 3D card's largest texture. (The larger z* pictures are the 2D dash's and the map's, never on a face.)
    /// </summary>
    [InstallFact]
    public void NoCockpitFaceCarriesArtOver256()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var resolver = new TextureResolver(vfs);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var car in vfs.Names.Where(n => n.StartsWith("vp", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)))
        {
            if (new ModelAssembler(vfs) { Detail = '5' }.Assemble(car) is not { } interior) continue;
            foreach (var f in interior.Parts.SelectMany(p => p.Mesh.Faces))
                if (f.Texture.Length > 0 && resolver.ResolveFace(f.Texture, interior.ObjectCode, interior.TmtNames, vtfMaps: interior.VtfMaps) is { } member
                    && member[(member.LastIndexOfAny(['/', '\\', ':']) + 1)..] is ['z' or 'Z', ..] && seen.Add(member) && resolver.Load(member) is { } t)
                    Assert.True(t.Width <= 256 && t.Height <= 256, $"{car}: {member} {t.Width}x{t.Height}");
        }
        Assert.True(seen.Count > 10, $"only {seen.Count} cockpit textures on faces");
    }
}
