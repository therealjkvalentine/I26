using System.Text.RegularExpressions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A sign's two back-to-back cut-out faces (MeshBuilder.BackToBack) are drawn from their front only, and in the modern look
/// keyed as every other cut-out face is: by the smooth outline through alpha-to-coverage. They ran the opaque back-culled
/// shader, which cut at half the upscale's own alpha with a hard discard, so the road signs' outlines were the upscale's
/// lumps and stair steps, with the colour bled round them showing as a light fringe.
/// </summary>
public class SignEdgeTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    [Fact]
    public void TheBackToBackCutOutShaderIsFrontOnlyAndKeyedByCoverage()
    {
        var s = Repo("game", "Shaders", "object_back_key.gdshader");
        var mode = Regex.Match(s, @"^render_mode .*$", RegexOptions.Multiline).Value;
        Assert.Contains("cull_back", mode);
        Assert.Contains("alpha_to_coverage", mode);
        Assert.Contains("#define KEY_COVERAGE", s);
        Assert.DoesNotContain("#define FAITHFUL", s);
    }

    [Fact]
    public void BackToBackCutOutFacesTakeItAndTheSmoothOutline()
    {
        var lib = Repo("game", "Scripts", "Assets", "MaterialLibrary.cs");
        Assert.Contains("res://Shaders/object_back_key.gdshader", lib);
        Assert.Matches(@"oneSided \? \(keyed \? BackKey : Back\)", lib);
        Assert.Matches(@"modern == Both \|\| modern == BackKey\)\)", lib);
        // the 1997 look keeps the original's front-only keyed face (Back97), and swaps back to BackKey, not the opaque Back
        Assert.Contains("_oneSided1997[mat] = (BackKey, Back97)", lib);
    }
}
