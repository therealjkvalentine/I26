using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Geo;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The shape the cockpit's faces give their art's texels (PanelAspect): every car's specials panel stretches its art 1.63 times
/// in height on the model, the weapons and damage panels show theirs square, and the modern look's fit makes the specials square.
/// </summary>
public class PanelAspectTests
{
    /// <summary>A quad w x h (model units) at z = 0 whose UVs run u 0..u1 and v v0..1, as two triangles the way the .geo stores them.</summary>
    private static (ModelPart Part, GeoFace Face) Quad(float w, float h, float u1, float v0)
    {
        var mesh = new GeoMesh
        {
            Vertices = [new(w / 2, h / 2, 0), new(-w / 2, h / 2, 0), new(w / 2, -h / 2, 0), new(-w / 2, -h / 2, 0)],
            Faces =
            [
                new GeoFace { Texture = "ZBKS_.MAP", Vertices = [new(1, 0, 0, 1), new(3, 0, 0, v0), new(2, 0, u1, v0)] },
                new GeoFace { Texture = "ZBKS_.MAP", Vertices = [new(0, 0, u1, 1), new(1, 0, 0, 1), new(2, 0, u1, v0)] },
            ],
        };
        return (new ModelPart("XX51SPC6", mesh, Affine.Identity), mesh.Faces[0]);
    }

    [Fact]
    public void AQuadWhoseTexelsAreSquareMeasuresOne()
    {
        // 181.3 x 76 texels of a 256 x 128 map over 266 x 111.6 mm: square
        var (p, f) = Quad(0.2657f, 0.2657f * 76 / 181.33f, 0.7083f, 0.4066f);
        Assert.Equal(1f, PanelAspect.TexelAspect(p, f, 256, 128)!.Value, 0.01f);
    }

    [Fact]
    public void TheSpecialsQuadsShapeStretchesItsArt163Times()
    {
        // the SPC6 quad: 266 x 181 mm for the same texels
        var (p, f) = Quad(0.2657f, 0.1812f, 0.7083f, 0.4066f);
        Assert.Equal(1.63f, PanelAspect.TexelAspect(p, f, 256, 128)!.Value, 0.01f);
    }

    [Fact]
    public void TheSquareFitKeepsTheTopRowAndShowsAspectTimesTheRows()
    {
        var (p, f) = Quad(0.2657f, 0.1812f, 0.7083f, 0.4066f);
        float aspect = PanelAspect.TexelAspect(p, f, 256, 128)!.Value;
        var fit = PanelAspect.SquareFit(aspect);
        float V(float v) => v * fit.Y + fit.W;
        Assert.Equal(1f, V(1f), 1e-6f);                                  // the art's first row stays at the top edge
        Assert.Equal(1f, fit.X); Assert.Equal(0f, fit.Z);                 // the width untouched
        Assert.Equal((1 - 0.4066f) * 128 * aspect, (1 - V(0.4066f)) * 128, 0.01f);   // the face now spans aspect times its rows
        // the face with the fitted UVs is square
        var g = new GeoFace { Vertices = [.. f.Vertices.Select(v => v with { V = V(v.V) })] };
        Assert.Equal(1f, PanelAspect.TexelAspect(p, g, 256, 128)!.Value, 0.001f);
    }

    /// <summary>Measured on the install: every car's specials face (the high-resolution SPC6, ZBKS_.MAP) at 1.63, its weapons and damage panels square.</summary>
    [InstallFact]
    public void EveryCarsSpecialsPanelIsStretchedAndItsOtherPanelsAreNot()
    {
        var vfs = TestInstall.Vfs;
        var resolver = new TextureResolver(vfs);
        var sizes = new Dictionary<string, (int W, int H)>();
        (int W, int H) Size(string stem) => sizes.TryGetValue(stem, out var s) ? s
            : sizes[stem] = resolver.Load(resolver.FindByStem(stem)!) is { } t ? (t.Width, t.Height) : (0, 0);
        int cars = 0;
        foreach (var vcf in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order())
        {
            AssembledModel? interior;
            try { interior = new ModelAssembler(vfs) { Detail = '5' }.Assemble(vcf); } catch (InvalidDataException) { continue; }
            if (interior is null) continue;
            static bool Hi(ModelPart p) => p.NodeName.Length == 8 && p.NodeName[7] == '6';
            if (PanelAspect.Of(interior, "zbks_", Size("zbks_").W, Size("zbks_").H, Hi) is not { } found) continue;
            float spc = found.Aspect;
            // the turrets', the howitzer's and the helicopter's interiors (XT, XW, UH: nobody drives them) are other models, 1.14
            if (interior.Parts.FirstOrDefault(p => p.NodeName.EndsWith("SPC6", StringComparison.OrdinalIgnoreCase)) is { } sp && sp.NodeName[..2] is "XT" or "XW" or "UH")
            {
                Assert.Equal(1.136f, spc, 0.002f);
                continue;
            }
            Assert.Equal(1.629f, spc, 0.002f);
            Assert.Equal(52f / 128, found.MinV, 0.001f);   // the art's 76 rows, the rest of the map below them
            cars++;
            if (PanelAspect.Of(interior, "zbk_", Size("zbk_").W, Size("zbk_").H, Hi) is { } wep) Assert.InRange(wep.Aspect, 0.95f, 1.05f);
            if (PanelAspect.Of(interior, "zsy_", Size("zsy_").W, Size("zsy_").H, Hi) is { } sys) Assert.InRange(sys.Aspect, 0.95f, 1.05f);
        }
        Assert.True(cars > 20, $"{cars} cars with a specials panel");
        var piranha = new ModelAssembler(vfs) { Detail = '5' }.Assemble("vppirna4.vcf")!;
        Assert.Equal(1.629f, PanelAspect.Of(piranha, "zbks_", 256, 128, p => p.NodeName.EndsWith('6'))!.Value.Aspect, 0.002f);
    }
}
