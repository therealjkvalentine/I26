using System.Numerics;
using I76.Formats.Assembly;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The HD pack's seam audit finds where two textures meet on a model (i76 tex-seams, tools/upscale-textures.py
/// --neighbours): on a made-up box, a hood meeting a fender at a fold, a door meeting the fender in the same plane, one
/// texture's two UV islands meeting, a texture continuous across a quad split, and a double-sided panel.
/// </summary>
public class TextureSeamsTests
{
    private static TextureSeams.Face Quad(string part, string tex, Vector3[] p, Vector2[] uv) => new(part, p, uv, tex);

    private static readonly Vector3 A = new(0, 1, 0), B = new(1, 1, 0), C = new(1, 1, 1), D = new(0, 1, 1);   // the hood's corners
    private static readonly Vector3 E = new(1, 0, 0), F = new(1, 0, 1);                                         // the fender's foot

    [Fact]
    public void TwoTexturesAtAFoldAreOneSeamWithBothSidesUvs()
    {
        var hood = Quad("HOOD", "pp11ftt1.map", [A, B, C, D], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        var fender = Quad("FNDR", "pp11ftr1.map", [B, E, F, C], [new(0, 0.5f), new(0, 0), new(1, 0), new(1, 0.5f)]);
        var s = Assert.Single(TextureSeams.Find([hood, fender]));
        Assert.Equal("pp11ftt1.map", s.A.Texture);
        // the shared edge B-C: the hood's u = 1 from v 0 to 1, the fender's v = 0.5 from u 0 to 1, in the same order
        Assert.Equal((new Vector2(1, 0), new Vector2(1, 1)), (s.A.Uv0, s.A.Uv1));
        Assert.Equal((new Vector2(0, 0.5f), new Vector2(1, 0.5f)), (s.B.Uv0, s.B.Uv1));
        Assert.Equal(90f, s.Angle, 1);
        Assert.Equal(1f, s.Length, 4);
        Assert.Equal(new Vector2(0.5f, 0.5f), s.A.Centre);
    }

    [Fact]
    public void OneTextureContinuousAcrossAnEdgeIsNoSeamButTwoIslandsAre()
    {
        // the hood split in two triangles sharing A-C with the same UVs: continuous
        var t1 = Quad("HOOD", "pp11ftt1.map", [A, B, C], [new(0, 0), new(1, 0), new(1, 1)]);
        var t2 = Quad("HOOD", "pp11ftt1.map", [A, C, D], [new(0, 0), new(1, 1), new(0, 1)]);
        Assert.Empty(TextureSeams.Find([t1, t2]));
        // the same texture's other island on the far side of B-C: a seam of the texture with itself
        var island = Quad("FNDR", "pp11ftt1.map", [B, E, F, C], [new(0.5f, 0.5f), new(0.5f, 0), new(0.75f, 0), new(0.75f, 0.5f)]);
        var s = Assert.Single(TextureSeams.Find([t1, t2, island]));
        Assert.Equal(s.A.Texture, s.B.Texture);
    }

    [Fact]
    public void ABackToBackPanelIsNotASeamAndPointsWeldWithinTolerance()
    {
        var front = Quad("SIGN", "sign_a.map", [A, B, C, D], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        var back = Quad("SIGN", "sign_b.map", [D, C, B, A], [new(0, 1), new(1, 1), new(1, 0), new(0, 0)]);
        Assert.Empty(TextureSeams.Find([front, back]));
        // a neighbour a hair off (under the weld) still shares the edge
        var nudge = new Vector3(0, 0, 2e-5f);
        var next = Quad("SIGN", "sign_c.map", [B + nudge, E, F, C + nudge], [new(0, 0), new(0, 1), new(1, 1), new(1, 0)]);
        Assert.Single(TextureSeams.Find([front, next], weld: 1e-4f));
    }

    [Fact]
    public void FacesThatRepeatTheirTexture()
    {
        Assert.Equal((false, false), TextureSeams.Tiles(Quad("W", "w.map", [A, B, C, D], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)])));
        Assert.Equal((true, false), TextureSeams.Tiles(Quad("W", "w.map", [A, B, C, D], [new(0, 0), new(3, 0), new(3, 1), new(0, 1)])));
        Assert.Equal((false, true), TextureSeams.Tiles(Quad("W", "w.map", [A, B, C, D], [new(0, 0.5f), new(1, 0.5f), new(1, 1.5f), new(0, 1.5f)])));
    }
}
