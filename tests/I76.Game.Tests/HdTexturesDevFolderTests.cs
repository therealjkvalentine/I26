using I76.Formats.Shell;
using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// I76_TEX_DIR's world/ folder (a dev hook) beside an installed tex-world-Nx pack: it is read first and the pack after it. It used to
/// serve nothing at all when a pack was there (both chains left empty: "HD textures served: none").
/// </summary>
public class HdTexturesDevFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "i26-devtex-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private HdDirSource Folder(string name, params (string File, string Text)[] files)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        foreach (var (f, t) in files) File.WriteAllText(Path.Combine(d, f), t);
        return new HdDirSource(d);
    }

    private static string? ReadFirst(HdSource[] chain, string rel) =>
        chain.FirstOrDefault(s => s.Exists(rel))?.Read(rel) is { } b ? System.Text.Encoding.ASCII.GetString(b) : null;

    [Fact]
    public void ADevFolderBesideAPackIsReadFirstThenThePack()
    {
        var dev = Folder("dev", ("zradf000.png", "dev"));
        var x8 = Folder("tex-world-8x", ("zradf000.dds", "8x"), ("zradmask.dds", "8x"));
        var x4 = Folder("tex-world-4x", ("zradf000.dds", "4x"), ("pp11ftf1.dds", "4x"));
        var (source, world, car) = HdTextures.Compose(dev, [x8, x4], [x4], [x8, x4], 4, 8);
        Assert.NotNull(source);
        Assert.Equal([dev, x4], world);
        Assert.Equal([dev, x8, x4], car);
        Assert.Equal("dev", ReadFirst(car, "zradf000.png"));          // the redrawn frame from the dev folder
        Assert.Equal("8x", ReadFirst(car, "zradmask.dds"));          // the rest from the pack
        Assert.Equal("4x", ReadFirst(world, "pp11ftf1.dds"));
        Assert.True(source!.Exists("zradf000.png") && source.Exists("pp11ftf1.dds"));
    }

    [Fact]
    public void WithoutAPackTheDevFolderAloneAndAKindAt1997ReadsNothing()
    {
        var dev = Folder("dev", ("zradf000.png", "dev"));
        var (source, world, car) = HdTextures.Compose(dev, [], [], [], 4, 0);
        Assert.Same(dev, source);
        Assert.Equal([dev], world);
        Assert.Empty(car);
    }

    [Fact]
    public void WithoutADevFolderThePacksAsBefore()
    {
        var x8 = Folder("tex-world-8x", ("a.dds", "8x"));
        var x4 = Folder("tex-world-4x", ("a.dds", "4x"));
        var (source, world, car) = HdTextures.Compose(null, [x8, x4], [x4], [x8, x4], 4, 8);
        Assert.Equal([x4], world);
        Assert.Equal([x8, x4], car);
        Assert.Equal($"{x8.Origin} over {x4.Origin}", source!.Origin);
        var none = HdTextures.Compose(null, [], [], [], 4, 8);
        Assert.Null(none.Source);
        Assert.Empty(none.World);
    }
}
