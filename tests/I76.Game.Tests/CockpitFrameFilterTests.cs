using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The steering wheel's and the arms' frames (zswls, zhr45, zhl45, zsr45, zsl45) are swapped onto their faces as the wheel turns.
/// Point-sampled from the full-size level like the rest of the cockpit's art, a pack's 8x frames (2048 texels across a rim
/// drawn about 1200 pixels wide at 3440x1440) stepped and shimmered at every edge as the wheel turned: measured in T01, the
/// rim's edge changed by whole texels between frames. In the modern look a pack's frames are read filtered with their mipmaps
/// through albedo_frames (clamped, as the cockpit sampler is: the wheel quad's far row must not wrap into its rim); the
/// originals, and the 1997 look, stay point-sampled.
/// </summary>
public class CockpitFrameFilterTests
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
    public void TheFramesSamplerIsFilteredWithMipmapsAndClamped()
    {
        var line = Regex.Match(Object, @"^uniform sampler2D albedo_frames : .*$", RegexOptions.Multiline).Value;
        Assert.Contains("filter_linear_mipmap_anisotropic", line);
        Assert.Contains("repeat_disable", line);
    }

    [Fact]
    public void OnlyTheModernLookReadsTheFramesFiltered()
    {
        // after the prelit read, in the #else (modern) branch only: the 1997 look's point sampling is untouched
        var reads = Regex.Matches(Object, @"^\s*if \(frames_filtered\) c = texture\(albedo_frames, tuv\);\s*$", RegexOptions.Multiline);
        Assert.Single(reads);
        int at = reads[0].Index;
        int faithfulElse = Object.LastIndexOf("#else", at, StringComparison.Ordinal);
        int faithfulIf = Object.LastIndexOf("#ifdef FAITHFUL", at, StringComparison.Ordinal);
        Assert.True(faithfulElse > faithfulIf, "the filtered read sits in the modern branch");
    }

    [Fact]
    public void TheMaterialsFeedTheFramesSamplerOnlyWhenAsked()
    {
        var lib = Repo("game", "Scripts", "Assets", "MaterialLibrary.cs");
        Assert.Contains("bool? filtered = null", lib);
        Assert.Contains("SetShaderParameter(\"albedo_frames\", texture)", lib);
        Assert.Contains("SetShaderParameter(\"frames_filtered\", f)", lib);
    }

    [Fact]
    public void APacksFramesGetMipmapsAndAreFilteredTheOriginalsNot()
    {
        var pit = Repo("game", "Scripts", "Sim", "Cockpit.cs");
        // filtered only when a frame is wider than the original's (a pack served): a pack's .dds goes as stored with its own mipmaps
        // (block-compressed, not RGBA8), any other frame gets mipmaps made for it
        Assert.Contains("im.GetWidth() > baseW", pit);
        Assert.Contains("Textures.UpscaleImage(m) is { } up) { imgs[i] = up; stored[i] = true; }", pit);
        Assert.Contains("if (filtered) System.Threading.Tasks.Parallel.For(1, n + 1, i => { if (!stored[i]) imgs[i]?.GenerateMipmaps(); });", pit);
        var tf = Repo("game", "Scripts", "Assets", "TextureFactory.cs");
        Assert.Contains("HdOf(member) is { Gpu: { } g } && g.HasMipmaps() ? g : null", tf);
        Assert.Contains("filtered: _wheelFrames.Filtered", pit);
        Assert.Contains("filtered: a.Axis == 1 ? shot.Filtered : raise.Filtered", pit);
    }
}
