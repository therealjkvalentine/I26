using I76.Formats;
using I76.Formats.Geo;
using I76.Formats.Imaging;
using I76.Formats.Vfs;
using I76.Formats.Tests;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The 3D dash's needles: needle.geo's faces are untextured, in one RGB. The original's 3D card draws that RGB, lit (a
/// dark crimson, no green, glancing down in the GOG game's m01); its software renderer draws a palette entry instead.
/// </summary>
public class CockpitNeedleTests
{
    [InstallFact]
    public void TheNeedleIsUntexturedInOnePaintedColour()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var geo = GeoMesh.Parse(vfs.Read("needle.geo"));
        Assert.Equal(4, geo.Faces.Length);
        foreach (var f in geo.Faces)
        {
            Assert.Equal("", f.Texture);                       // a colour face: no texture to resolve, no lighting to carry
            Assert.Equal((252, 0, 103), (f.R, f.G, f.B));      // the file's colour, not what reaches the screen
        }
    }

    /// <summary>
    /// Under the Software renderer the needle was drawn in the entry nearest needle.geo's own RGB, a hot pink. The original's
    /// software renderer draws another palette entry: measured in the original (retail 1.0, software renderer, 640x480, T01 by day, 3D cockpit, glancing down, n = 1 boot, 4 frames parked and 4
    /// at speed) every needle pixel is a shade of entry 160 (232, 48, 88) through T01's .lum: the speedometer's rows 22..31
    /// ((152, 16, 40), (136, 48, 48), (128, 16, 32), (115, 40, 40), (96, 40, 16), (64, 24, 16)), the tachometer's row 31.
    /// The entry nearest the RGB itself is 220 (250, 0, 50), none of whose shades showed.
    /// </summary>
    [InstallTheory]
    [InlineData("t01_16.act", 232, 48, 88)]
    [InlineData("t01.act", 232, 48, 88)]
    public void TheNeedleTakesThePalettesRed(string palette, int r, int g, int b)
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var f = GeoMesh.Parse(vfs.Read("needle.geo")).Faces[0];
        var pal = new Palette(vfs.Read(palette));
        Assert.Equal(((byte)r, (byte)g, (byte)b), SoftwarePalette.FlatColour(pal, f.R, f.G, f.B));
    }

    /// <summary>
    /// The needle turned about its middle, as long on the tail side as on the reading side. In the original the reading
    /// side is about 0.69 of it (I76.Formats.Assembly.CockpitNeedle): 74 mm of the 107 mm strip against 33.
    /// </summary>
    [InstallFact]
    public void TheNeedleTurnsWithTheReadingSideLonger()
    {
        var vfs = new AssetVfs(new GameInstall(TestInstall.Dir!));
        var geo = GeoMesh.Parse(vfs.Read("needle.geo"));
        float shift = I76.Formats.Assembly.CockpitNeedle.Shift(geo);
        float reading = geo.Vertices.Max(v => v.Z) + shift, tail = -(geo.Vertices.Min(v => v.Z) + shift);
        Assert.Equal(0.0204f, shift, 0.0005f);
        Assert.Equal(0.0741f, reading, 0.0005f);
        Assert.Equal(0.0333f, tail, 0.0005f);
        Assert.Equal(0.69f, reading / (reading + tail), 0.005f);
    }
}
