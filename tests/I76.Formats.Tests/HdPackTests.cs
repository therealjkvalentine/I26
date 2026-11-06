using System.IO.Compression;
using System.Text;
using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

public class HdPackTests
{
    private static string Make(string dir, string name, string version, params (string Path, string Text)[] files)
    {
        var path = Path.Combine(dir, name);
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string n, string t) { using var w = z.CreateEntry(n).Open(); w.Write(Encoding.UTF8.GetBytes(t)); }
        Put("pack.json", $"{{\"name\":\"Test Pack\",\"version\":\"{version}\",\"date\":\"2026-01-01\",\"min_engine\":\"1.1\"}}");
        foreach (var (p, t) in files) Put(p, t);
        return path;
    }

    private static string Temp() => Directory.CreateTempSubdirectory("hdpack").FullName;

    [Fact]
    public void ReadsFilesAndManifestFromZip()
    {
        var dir = Temp();
        var zip = Make(dir, "i76-hd-menus.zip", "1.0.0", ("tex-menu/shellbg.json", "{\"6\":{\"file\":\"x.webp\",\"affine\":[1,2,3,4]}}"), ("tex-menu/nitro/load/Loadgame.webp", "px"), ("other/ignored.txt", "no"));
        using var s = new HdZipSource(zip);
        Assert.Equal("Test Pack 1.0.0", s.Info!.ToString());
        Assert.Equal(2, s.Count);
        Assert.True(s.Exists("nitro/load/loadgame.webp"));
        Assert.False(s.Exists("ignored.txt"));
        Assert.Equal("px", Encoding.UTF8.GetString(s.Read("nitro\\load\\loadgame.webp")!));
        Assert.Null(s.Read("missing.webp"));
        Assert.Contains("\"affine\"", Encoding.UTF8.GetString(s.Read("shellbg.json")!));
    }

    [Fact]
    public void NewestPackWinsAndBrokenOnesAreSkipped()
    {
        var dir = Temp();
        Make(dir, "i76-hd-menus-1.2.0.zip", "1.2.0", ("tex-menu/a.webp", "a"));
        Make(dir, "i76-hd-menus-1.10.0.zip", "1.10.0", ("tex-menu/a.webp", "b"));
        File.WriteAllText(Path.Combine(dir, "i76-hd-menus-bad.zip"), "not a zip");
        var warnings = new List<string>();
        using var s = HdPackFinder.Newest([dir, Path.Combine(dir, "nope")], warnings.Add);
        Assert.Equal("1.10.0", s!.Info!.Version);
        Assert.Single(warnings);
    }

    [Fact]
    public void FolderSourceReadsTheSameLayout()
    {
        var dir = Temp();
        Directory.CreateDirectory(Path.Combine(dir, "nitro"));
        File.WriteAllText(Path.Combine(dir, "nitro", "shellbg.json"), "{}");
        using var s = new HdDirSource(dir);
        Assert.True(s.Exists("nitro/shellbg.json"));
        Assert.Null(s.Info);
        Assert.Null(HdPackInfo.Parse("nope"u8.ToArray()));
    }

    /// <summary>pack.json's partial and covers: a pack of some textures, and the HD Textures row's words for it.</summary>
    [Fact]
    public void APartialPackSaysWhatItCovers()
    {
        var i = HdPackInfo.Parse("""{"name": "Upscaled", "scale": 8, "partial": true, "covers": "cockpit & cars"}"""u8.ToArray())!;
        Assert.True(i.Partial);
        Assert.Equal("cockpit & cars", i.Covers);
        var full = HdPackInfo.Parse("""{"name": "Upscaled", "scale": 4}"""u8.ToArray())!;
        Assert.False(full.Partial);
        Assert.Equal("", full.Covers);
    }

    /// <summary>A partial 8x over the 4x, per texture: the 8x's file where it has one, the 4x's for the rest; the 8x's pack.json is the chain's.</summary>
    [Fact]
    public void AChainServesEachFileFromTheFirstFolderThatHasIt()
    {
        string x8 = Temp(), x4 = Temp();
        File.WriteAllText(Path.Combine(x8, "zdash101.dds"), "8x");
        File.WriteAllText(Path.Combine(x8, "pack.json"), """{"name": "8x", "partial": true, "covers": "cockpit & cars"}""");
        File.WriteAllText(Path.Combine(x4, "zdash101.dds"), "4x");
        File.WriteAllText(Path.Combine(x4, "tpgrass1.dds"), "4x");
        using var c = new HdChainSource([new HdDirSource(x8), new HdDirSource(x4)]);
        Assert.Equal("8x", System.Text.Encoding.ASCII.GetString(c.Read("zdash101.dds")!));
        Assert.Equal("4x", System.Text.Encoding.ASCII.GetString(c.Read("tpgrass1.dds")!));
        Assert.Null(c.Read("nothing.dds"));
        Assert.True(c.Exists("tpgrass1.dds"));
        Assert.Equal(x4, c.Owner("tpgrass1.dds")!.Origin);
        Assert.True(c.Info!.Partial);
        Assert.True(c.ThreadSafe);
    }

    [Fact]
    public void AChainCountsAFileBothFoldersHaveOnce()
    {
        string x8 = Temp(), x4 = Temp();
        File.WriteAllText(Path.Combine(x8, "zdash101.dds"), "8x");
        File.WriteAllText(Path.Combine(x8, "TPGRASS1.DDS"), "8x");   // a folder's names as written: the same file whatever the case
        File.WriteAllText(Path.Combine(x4, "zdash101.dds"), "4x");
        File.WriteAllText(Path.Combine(x4, "tpgrass1.dds"), "4x");
        File.WriteAllText(Path.Combine(x4, "tpdirt01.dds"), "4x");
        using var c = new HdChainSource([new HdDirSource(x8), new HdDirSource(x4)]);
        // the start-up log's count: zdash101, tpgrass1, tpdirt01 (the sum of the two folders was 5)
        Assert.Equal(3, c.Count);
        Assert.Equal(3, c.Names.Count());
    }
}
