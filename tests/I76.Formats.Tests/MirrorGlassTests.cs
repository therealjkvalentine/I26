using System.Numerics;
using I76.Formats.Assembly;
using I76.Formats.Geo;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// The door mirrors' painted glass in the cars' interiors (MirrorGlass): which faces are the glass, which cars have it, and the
/// picture's coordinates over a pane as the driver sees it.
/// </summary>
public class MirrorGlassTests
{
    private static GeoFace Face(string tex, byte r, byte g, byte b) => new() { Texture = tex, R = r, G = g, B = b };

    [Fact]
    public void TheGlassIsTheSkyBlueOrTheMirrorsOwnPicture()
    {
        Assert.True(MirrorGlass.IsGlass(Face("", 73, 204, 255)));        // the Piranha's left glass
        Assert.True(MirrorGlass.IsGlass(Face("", 130, 196, 220)));       // the CC cars'
        Assert.True(MirrorGlass.IsGlass(Face("PP51MIRR.MAP", 0, 0, 0))); // the Piranha's right, painted
        Assert.False(MirrorGlass.IsGlass(Face("", 224, 224, 224)));      // a housing's grey
        Assert.False(MirrorGlass.IsGlass(Face("V1 BO DY.MAP", 255, 73, 0)));
        Assert.False(MirrorGlass.IsGlass(Face("ZMIRI101.MAP", 0, 0, 0))); // the rear mirror's art
        Assert.Equal(true, MirrorGlass.Side("PP51MIRL"));
        Assert.Equal(false, MirrorGlass.Side("PP51MIRR"));
        Assert.Null(MirrorGlass.Side("PP51MIRI"));
    }

    [Fact]
    public void APanesPictureRunsLeftToRightAndFootToTopAsTheDriverSeesIt()
    {
        // a 0.2 x 0.1 pane 1 m ahead-left of the eye, facing it
        var pane = new MirrorGlass.Pane(true, "XX51MIRL", [[new(-0.9f, 1.0f, 1f), new(-0.7f, 1.0f, 1f), new(-0.7f, 1.1f, 1f), new(-0.9f, 1.1f, 1f)]], -Vector3.UnitZ);
        var (uv, aspect) = MirrorGlass.Picture(pane, new Vector3(-0.8f, 1.05f, 0f));
        Assert.Equal(2f, aspect, 0.01f);
        Assert.Equal(new Vector2(0, 0), uv[0][0]);   // the left foot
        Assert.Equal(new Vector2(1, 0), uv[0][1]);   // the right foot
        Assert.Equal(new Vector2(1, 1), uv[0][2]);
        Assert.Equal(new Vector2(0, 1), uv[0][3]);
    }

    [InstallFact]
    public void ThePiranhaHasBothGlassesAndTheCcCarsTheLeft()
    {
        var p = MirrorGlass.Find(new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("vppirna1.vcf")!);
        Assert.Equal(2, p.Count);
        Assert.True(p[0].Left);
        Assert.Equal("PP51MIRL", p[0].Part, ignoreCase: true);
        Assert.Equal(4, p[0].Faces.Count);       // four flat blue faces
        Assert.False(p[1].Left);
        Assert.Single(p[1].Faces);               // one face, PP51MIRR's picture
        // on the doors, a hand's width across, facing back toward the driver
        Assert.InRange(p[0].Faces.SelectMany(f => f).Average(q => q.X), -0.95f, -0.8f);
        Assert.InRange(p[1].Faces.SelectMany(f => f).Average(q => q.X), 0.8f, 0.95f);
        var c = MirrorGlass.Find(new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("vccameo1.vcf")!);
        Assert.Single(c);
        Assert.True(c[0].Left);
        Assert.Single(c[0].Faces);
    }

    [InstallFact]
    public void OnlyThePiranhasAndTheCcCarsInteriorsHaveGlass()
    {
        var vfs = TestInstall.Vfs;
        var with = new List<string>();
        int cars = 0;
        foreach (var n in vfs.Names.Where(n => n.EndsWith(".vcf", StringComparison.OrdinalIgnoreCase)).Order())
        {
            AssembledModel? m;
            try { m = new ModelAssembler(vfs) { Detail = '5' }.Assemble(n); } catch (Exception) { continue; }
            if (m is null) continue;
            cars++;
            if (MirrorGlass.Find(m).Count > 0) with.Add(m.ObjectCode ?? n);
        }
        Assert.True(cars > 200, $"only {cars} cars");
        Assert.NotEmpty(with);
        Assert.All(with, code => Assert.Contains(code, new[] { "PP", "CC" }));
    }
}
