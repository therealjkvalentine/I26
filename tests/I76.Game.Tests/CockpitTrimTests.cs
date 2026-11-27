using Godot;
using I76.Formats.Assembly;
using I76.Formats.Tests;
using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's cockpit trim ([port] cockpit_trim, CockpitTrim): off by default and written only when on; the roof part's flat
/// faces looking down are the headliner, its others the pillars, header and rails; the side part's faces looking sideways the doors.
/// </summary>
public class CockpitTrimTests
{
    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteCockpitTrim((k, v) => keys[k] = v);
        return keys;
    }

    [Fact]
    public void TheKeyIsOffByDefaultAndRoundTrips()
    {
        Assert.Equal(CockpitTrim.Style.Off, new ShellSettings().CockpitTrim);
        Assert.Empty(Cfg(new ShellSettings()));
        foreach (var style in Enum.GetValues<CockpitTrim.Style>().Where(s => s != CockpitTrim.Style.Off))
        {
            var text = Cfg(new ShellSettings { CockpitTrim = style });
            Assert.Equal(new Dictionary<string, string> { ["cockpit_trim"] = CockpitTrim.Word(style) }, text);
            var back = new ShellSettings();
            back.ReadCockpitTrim(k => text.GetValueOrDefault(k));
            Assert.Equal(style, back.CockpitTrim);
        }
        Assert.Equal(Enum.GetValues<CockpitTrim.Style>().Length, CockpitTrim.Words.Length);
        Assert.Equal(CockpitTrim.Style.Charcoal, CockpitTrim.Parse(" Charcoal "));
        Assert.Equal(CockpitTrim.Style.Basket, CockpitTrim.Parse("\"basket\""));
        Assert.Equal(CockpitTrim.Style.Off, CockpitTrim.Parse(null));
        Assert.Equal(CockpitTrim.Style.Off, CockpitTrim.Parse("plaid"));
        var none = new ShellSettings { CockpitTrim = CockpitTrim.Style.Moulded };
        none.ReadCockpitTrim(_ => null);
        Assert.Equal(CockpitTrim.Style.Off, none.CockpitTrim);
    }

    [Fact]
    public void FacesAreToldApartByTheirFacing()
    {
        Vector3 N(float x, float y, float z) => new Vector3(x, y, z).Normalized();
        // the Piranha's: the headliner flat and its rear slope; the windscreen header, an A pillar's face, a roof rail
        Assert.Equal(CockpitTrim.Part.Roof, CockpitTrim.Classify("PP51BDYT", N(0, -1, 0.08f)));
        Assert.Equal(CockpitTrim.Part.Roof, CockpitTrim.Classify("PP51BDYT", N(0, -0.88f, -0.47f)));
        Assert.Equal(CockpitTrim.Part.Pillar, CockpitTrim.Classify("PP51BDYT", N(0, -0.64f, 0.76f)));
        Assert.Equal(CockpitTrim.Part.Pillar, CockpitTrim.Classify("PP51BDYT", N(0.62f, -0.68f, 0.39f)));
        Assert.Equal(CockpitTrim.Part.Pillar, CockpitTrim.Classify("PP51BDYT", N(-0.64f, -0.77f, -0.06f)));
        // the door's panel and its top lip; the dash's face, the floor and the rear shelf of the same part are left alone
        Assert.Equal(CockpitTrim.Part.Door, CockpitTrim.Classify("PP51BDYM", N(-1, -0.05f, 0)));
        Assert.Equal(CockpitTrim.Part.Door, CockpitTrim.Classify("PP51BDYM", N(-0.6f, -0.8f, 0.04f)));
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("PP51BDYM", N(0, -0.23f, 0.97f)));
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("PP51BDYM", N(0, 1, 0)));
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("PP51BDYM", N(-0.2f, 0.87f, 0)));
        // not the cockpit's parts: the outside model's roof, the dash top, a short name
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("PP11BDYT", N(0, -1, 0)));
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("PP51BDYF", N(0, -1, 0)));
        Assert.Equal(CockpitTrim.Part.None, CockpitTrim.Classify("BDYT", N(0, -1, 0)));
        // the trim grey: the cars' 54, 56 and 80; not the helicopters' light grey, yellow or near-black
        Assert.True(CockpitTrim.TrimGrey(54, 54, 54));
        Assert.True(CockpitTrim.TrimGrey(80, 80, 80));
        Assert.False(CockpitTrim.TrimGrey(188, 188, 188));
        Assert.False(CockpitTrim.TrimGrey(225, 225, 0));
        Assert.False(CockpitTrim.TrimGrey(17, 17, 17));
    }

    /// <summary>The Piranha's cockpit (the stock install): its 51 roof-part faces split into headliner and pillars, its door panels found, the headliner above the pillars' middle.</summary>
    [InstallFact]
    public void ThePiranhasCockpitSplitsIntoRoofPillarsAndDoors()
    {
        var m = new ModelAssembler(TestInstall.Vfs) { Detail = '5' }.Assemble("vppirna1.vcf")!;
        var count = new Dictionary<CockpitTrim.Part, int>();
        var heights = new Dictionary<CockpitTrim.Part, List<float>>();
        foreach (var p in m.Parts.Where(p => CockpitTrim.Zone(p.NodeName) is not null))
            foreach (var f in p.Mesh.Faces.Where(f => f.Vertices.Length >= 3 && f.Texture.Length == 0 && CockpitTrim.TrimGrey(f.R, f.G, f.B)))
            {
                var n = Coords.ToGodot(-p.ToModel.Rotate(new System.Numerics.Vector3(f.Plane.X, f.Plane.Y, f.Plane.Z))).Normalized();
                var part = CockpitTrim.Classify(p.NodeName, n);
                count[part] = count.GetValueOrDefault(part) + 1;
                float y = f.Vertices.Average(v => p.ToModel.Apply(p.Mesh.Vertices[v.Index]).Y);
                (heights.TryGetValue(part, out var l) ? l : heights[part] = []).Add(y);
            }
        Assert.Equal(51, count[CockpitTrim.Part.Roof] + count[CockpitTrim.Part.Pillar]);
        Assert.InRange(count[CockpitTrim.Part.Roof], 10, 30);
        Assert.InRange(count[CockpitTrim.Part.Pillar], 20, 42);
        Assert.InRange(count[CockpitTrim.Part.Door], 20, 40);
        Assert.True(count.GetValueOrDefault(CockpitTrim.Part.None) > 0, "the dash's face and the floor are left alone");
        Assert.True(heights[CockpitTrim.Part.Roof].Average() > heights[CockpitTrim.Part.Pillar].Average());
        Assert.True(heights[CockpitTrim.Part.Door].Average() < heights[CockpitTrim.Part.Pillar].Average());
    }
}
