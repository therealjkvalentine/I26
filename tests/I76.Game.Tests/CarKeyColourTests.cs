using System.Text.RegularExpressions;
using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A car's opaque faces draw their texture's keyed texels (palette index 255) in that index's colour, as the original draws any texel
/// of an opaque face: the Jefferson Dreadnought limo's roof and the school bus's black bands and lettering are index 255, black. The
/// texture the game uploads has its keyed texels coloured from their neighbours (so filtering does not pull the key colour into a
/// cut-out's edge); drawn as they were, those bled colours took the bus's lettering and bands to orange and greyed the limo's roof.
/// </summary>
public class CarKeyColourTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path]));
    }

    [Theory]
    [InlineData(MaterialLibrary.FaceMode.Opaque, true, true)]
    [InlineData(MaterialLibrary.FaceMode.Opaque, false, false)]
    [InlineData(MaterialLibrary.FaceMode.Cutout, true, false)]
    [InlineData(MaterialLibrary.FaceMode.Translucent, true, false)]
    public void OnlyAnOpaqueFaceOfAKeyedTextureDrawsTheKeyColour(MaterialLibrary.FaceMode mode, bool hasKey, bool expected)
        => Assert.Equal(expected, MaterialLibrary.DrawsKeyColour(mode, hasKey));

    [Fact]
    public void TheCarFacesSetTheKeyColourAndTheShaderDrawsItUnderTheKey()
    {
        var lib = Repo("game", "Scripts", "Assets", "MaterialLibrary.cs");
        var car = Regex.Match(lib, @"else if \(car is \{ \} paint && t is not null\)\s*\{(.*?)if \(CarMaps\)", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("DrawsKeyColour(mode, t.HasKey)", car);
        Assert.Contains("\"key_under\", true", car);
        Assert.Contains("\"key_fill_color\", t.KeyColour", car);
        var shader = Repo("game", "Shaders", "object.gdshaderinc");
        Assert.Matches(@"(?m)^uniform bool key_under = false;", shader);
        // after both looks' texture reads (the 1997 look's and the modern look's), before the respray and the key's discard
        int read = shader.LastIndexOf("vec4 c = use_texture ?", StringComparison.Ordinal);
        int under = shader.IndexOf("if (key_under) c = vec4(mix(key_fill_color.rgb, c.rgb, c.a), 1.0);", StringComparison.Ordinal);
        int paint = shader.IndexOf("if (has_paint_mask", StringComparison.Ordinal);
        Assert.True(read >= 0 && under > read && paint > under);
    }
}
