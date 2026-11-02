using I76.Formats.Audio;
using Xunit;

namespace I76.Formats.Tests;

public class ExtraMusicTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "i26-xmusic-" + Guid.NewGuid().ToString("N"));
    public ExtraMusicTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static string[] Files(int n) => Enumerable.Range(1, n).Select(i => $"song{i}.mp3").ToArray();

    [Fact]
    public void One_start_in_three_is_an_extra_by_default()
    {
        var x = new ExtraMusic(Files(4), ExtraMusic.DefaultEvery, new Random(1));
        var picks = Enumerable.Range(1, 9).Select(_ => x.Start()).ToArray();
        Assert.All(new[] { 0, 1, 3, 4, 6, 7 }, i => Assert.Null(picks[i]));
        Assert.All(new[] { 2, 5, 8 }, i => Assert.NotNull(picks[i]));
    }

    [Fact]
    public void Every_file_plays_once_before_any_repeats_and_the_shuffle_follows_the_seed()
    {
        var a = new ExtraMusic(Files(5), 1, new Random(7));
        var b = new ExtraMusic(Files(5), 1, new Random(7));
        var first = Enumerable.Range(0, 5).Select(_ => a.Start()!).ToArray();
        Assert.Equal(Files(5).Order(), first.Order());
        Assert.Equal(first, Enumerable.Range(0, 5).Select(_ => b.Start()!).ToArray());
        var second = Enumerable.Range(0, 5).Select(_ => a.Start()!).ToArray();
        Assert.Equal(Files(5).Order(), second.Order());
    }

    [Fact]
    public void No_files_means_the_disc_every_time()
    {
        var x = new ExtraMusic([], 1, new Random(1));
        Assert.Null(x.Start());
        Assert.Null(x.Start());
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("x", 3)]
    [InlineData("1", 1)]
    [InlineData("0", 1)]
    [InlineData("50", 20)]
    public void The_interval_is_one_to_twenty(string? text, int every) => Assert.Equal(every, ExtraMusic.ParseEvery(text));

    [Fact]
    public void The_folder_is_the_user_folders_unless_named_or_turned_off()
    {
        Assert.Equal(Path.Combine("u", "music-extra"), ExtraMusic.FolderFor(null, "u"));
        Assert.Equal("D:/songs", ExtraMusic.FolderFor("D:/songs", "u"));
        Assert.Null(ExtraMusic.FolderFor("off", "u"));
        Assert.Null(ExtraMusic.FolderFor("0", "u"));
    }

    [Fact]
    public void Scan_finds_mp3_and_ogg_in_the_folder_and_its_direct_subfolders_only()
    {
        Directory.CreateDirectory(Path.Combine(_root, "album", "deep"));
        foreach (var f in new[] { "a.mp3", "b.OGG", "c.wav", "album/d.mp3", "album/deep/e.mp3" })
            File.WriteAllBytes(Path.Combine(_root, f), [0]);
        var names = ExtraMusic.Scan(_root).Select(p => Path.GetFileName(p)).ToArray();
        Assert.Equal(["a.mp3", "b.OGG", "d.mp3"], names);
        Assert.Empty(ExtraMusic.Scan(Path.Combine(_root, "nowhere")));
        Assert.Empty(ExtraMusic.Scan(null));
    }
}
