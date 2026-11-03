using System.IO.Compression;
using System.Text;
using I76.Formats.Imaging;
using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>A pack's upscale of a world texture: found by stem, fitted at a power-of-two scale, flipped to the stored order, keyed as the original.</summary>
public class HdTextureTests
{
    /// <summary>A 4x2 original in stored (bottom-up) order: row 0 red with its last texel keyed, row 1 blue.</summary>
    private static byte[] Original()
    {
        var o = new byte[4 * 2 * 4];
        for (int x = 0; x < 4; x++) { o[x * 4] = 255; o[x * 4 + 3] = 255; o[(4 + x) * 4 + 2] = 255; o[(4 + x) * 4 + 3] = 255; }
        o[3 * 4 + 3] = 0;
        return o;
    }

    /// <summary>Its 2x upscale as a picture decodes (top-down): blue rows on top, red below, with no alpha at all.</summary>
    private static byte[] Upright2x()
    {
        var p = new byte[8 * 4 * 4];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 8; x++) { int i = (y * 8 + x) * 4; p[i + (y < 2 ? 2 : 0)] = 255; p[i + 3] = 255; }
        return p;
    }

    [Fact]
    public void ScaleIsAPowerOfTwoTheSameBothWays()
    {
        Assert.Equal(2, HdTexture.ScaleOf(8, 4, 4, 2));
        Assert.Equal(4, HdTexture.ScaleOf(512, 256, 128, 64));
        Assert.Equal(1, HdTexture.ScaleOf(4, 2, 4, 2));
        Assert.Equal(0, HdTexture.ScaleOf(12, 6, 4, 2));
        Assert.Equal(0, HdTexture.ScaleOf(8, 2, 4, 2));
        Assert.Equal(0, HdTexture.ScaleOf(9, 4, 4, 2));
    }

    [Fact]
    public void FitFlipsToTheStoredOrderAndTakesTheOriginalsKey()
    {
        var f = HdTexture.Fit(Upright2x(), 8, 4, 2, Original(), 4, 2, bottomUp: true);
        // stored row 0 (the bottom of the picture) is red, its 2x2 block over the original's keyed texel alpha 0
        Assert.Equal(255, f[0]); Assert.Equal(0, f[2]); Assert.Equal(255, f[3]);
        Assert.Equal(0, f[(0 * 8 + 6) * 4 + 3]); Assert.Equal(0, f[(1 * 8 + 7) * 4 + 3]); Assert.Equal(255, f[(0 * 8 + 5) * 4 + 3]);
        // stored row 3 (the top of the picture) is blue, opaque
        Assert.Equal(255, f[(3 * 8 + 7) * 4 + 2]); Assert.Equal(255, f[(3 * 8 + 7) * 4 + 3]);
        Assert.Equal(255, f[(2 * 8 + 7) * 4 + 3]); Assert.Equal(255, f[(2 * 8 + 7) * 4 + 2]);
        // a .vqm keeps the picture's order
        var v = HdTexture.Fit(Upright2x(), 8, 4, 2, Original(), 4, 2, bottomUp: false);
        Assert.Equal(255, v[2]);
    }

    [Fact]
    public void APicturesOwnAlphaIsTheKeyAtOneHalf()
    {
        var p = Upright2x();
        p[3] = 127; p[7] = 128;
        var f = HdTexture.Fit(p, 8, 4, 2, Original(), 4, 2, bottomUp: false);
        Assert.Equal(0, f[3]); Assert.Equal(255, f[7]);
        // the original's key is not used then
        Assert.Equal(255, f[(0 * 8 + 6) * 4 + 3]);
    }

    [Fact]
    public void AKeyedTexelTakesTheBledOriginalsColourWhateverThePackPutThere()
    {
        // the pack's picture: opaque blue, but cyan with alpha 0 over the left half of the top row (a key colour the model smeared)
        var p = Upright2x();
        for (int x = 0; x < 4; x++) { int i = x * 4; p[i] = 0; p[i + 1] = 255; p[i + 2] = 255; p[i + 3] = 0; }
        var orig = Original();
        orig[3 * 4] = 10; orig[3 * 4 + 1] = 20; orig[3 * 4 + 2] = 30;   // the colour bled under the original's keyed texel
        // the picture's own alpha: its keyed texels take the original's colour under them (stored row 3 is the picture's top row, original row 1: blue)
        var f = HdTexture.Fit(p, 8, 4, 2, orig, 4, 2, bottomUp: true);
        for (int x = 0; x < 4; x++) { int i = (3 * 8 + x) * 4; Assert.Equal(new byte[] { 0, 0, 255, 0 }, f[i..(i + 4)]); }
        // an opaque picture keyed by the original: the keyed block carries the bled colour, not the picture's
        var g = HdTexture.Fit(Upright2x(), 8, 4, 2, orig, 4, 2, bottomUp: true);
        Assert.Equal(new byte[] { 10, 20, 30, 0 }, g[((0 * 8 + 6) * 4)..((0 * 8 + 6) * 4 + 4)]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, g[((0 * 8 + 5) * 4)..((0 * 8 + 5) * 4 + 4)]);
    }

    [Fact]
    public void FoundByStemInAFolderOrAPacksWorldRoot()
    {
        Assert.Equal(["pp11ftf1.dds", "pp11ftf1.png", "pp11ftf1.webp", "pp11ftf1.jpg"], HdTexture.Candidates("PP11FTF1.M16"));
        var dir = Directory.CreateTempSubdirectory("hdtex").FullName;
        File.WriteAllText(Path.Combine(dir, "zsy_.png"), "px");
        using var folder = new HdDirSource(dir);
        Assert.Equal("zsy_.png", HdTexture.Candidates("zsy_.map").First(folder.Exists));
        var zip = Path.Combine(dir, "i76-hd-world.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = z.CreateEntry("pack.json").Open()) w.Write("{\"name\":\"T\",\"version\":\"1.0\"}"u8);
            using (var w = z.CreateEntry("tex-world/PP11FTF1.png").Open()) w.Write(Encoding.UTF8.GetBytes("hd"));
            using (var w = z.CreateEntry("tex-menu/shellbg1.webp").Open()) w.Write(Encoding.UTF8.GetBytes("menu"));
        }
        using var world = HdPackFinder.Newest([dir], null, "i76-hd-*.zip", "tex-world/");
        Assert.Equal(1, world!.Count);
        Assert.Equal("hd", Encoding.UTF8.GetString(world.Read("pp11ftf1.png")!));
        Assert.Null(HdPackFinder.Newest([dir]));
    }
}
