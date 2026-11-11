using System.IO.Compression;
using I76.Formats.Shell;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>
/// A texture pack read from many threads at once, as a mission's build thread, its prefetch workers and the main thread may: a folder,
/// a partial 8x over the 4x (each file from the first folder that has it) and a zip (whose archive is not thread safe by itself) give
/// every file whole; a folder is listed once and a chain's count is the same from every thread. Synthetic files only.
/// </summary>
public sealed class HdPackThreadTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("i26-hdpack-threads").FullName;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    /// <summary>File i's bytes in a layer: its own length and pattern, so a mixed-up, torn or wrong-layer read shows.</summary>
    private static byte[] Content(int i, int layer)
    {
        var b = new byte[1000 + (i * 37 + layer * 101) % 5000];
        for (int k = 0; k < b.Length; k++) b[k] = (byte)(i * 31 + k * 7 + layer * 59);
        return b;
    }

    private static string Name(int i) => $"z{i:000}.dds";

    private string Folder(string name, IEnumerable<int> files, int layer)
    {
        var d = Path.Combine(_dir, name);
        Directory.CreateDirectory(d);
        foreach (int i in files) File.WriteAllBytes(Path.Combine(d, Name(i)), Content(i, layer));
        return d;
    }

    private static void Hammer(HdSource src, int files, Func<int, int> layerOf)
    {
        int bad = 0, reads = 0;
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            var rng = new Random(t);
            for (int n = 0; n < 1500; n++)
            {
                int i = rng.Next(files + 20);   // some names the pack has not got
                bool want = i < files;
                if (src.Exists(Name(i)) != want) Interlocked.Increment(ref bad);
                var b = src.Read(Name(i));
                Interlocked.Increment(ref reads);
                if (want ? b is null || !b.AsSpan().SequenceEqual(Content(i, layerOf(i))) : b is not null) Interlocked.Increment(ref bad);
            }
        });
        Assert.Equal(16 * 1500, reads);
        Assert.Equal(0, bad);
    }

    [Fact]
    public void AFolderReadFromManyThreadsGivesEveryFileWhole() =>
        Hammer(new HdDirSource(Folder("tex-world-4x", Enumerable.Range(0, 60), 4)), 60, _ => 4);

    [Fact]
    public void APartial8xOverThe4xReadFromManyThreadsTakesEachFileFromTheFirstThatHasIt()
    {
        // the 8x holds the first half (the cockpit's and the cars'), the 4x all of them: a file both have is the 8x's
        using var chain = new HdChainSource([new HdDirSource(Folder("tex-world-8x", Enumerable.Range(0, 30), 8)), new HdDirSource(Folder("tex-world-4x", Enumerable.Range(0, 60), 4))]);
        Hammer(chain, 60, i => i < 30 ? 8 : 4);
    }

    [Fact]
    public void AZipReadFromManyThreadsGivesEveryFileWhole()
    {
        var p = Path.Combine(_dir, "i76-hd-test.zip");
        using (var z = ZipFile.Open(p, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("pack.json").Open()) s.Write("""{"name": "test", "version": "1"}"""u8);
            foreach (int i in Enumerable.Range(0, 60))
                using (var s = z.CreateEntry("tex-world/" + Name(i)).Open()) s.Write(Content(i, 4));
        }
        using var zip = HdPackFinder.Newest([_dir], null, "i76-hd-*.zip", "tex-world/");
        Assert.NotNull(zip);
        Hammer(zip!, 60, _ => 4);
    }

    [Fact]
    public void AFolderIsListedOnceFromManyThreadsAndAFileAddedLaterIsNotSeen()
    {
        var d = Folder("listed", Enumerable.Range(0, 3), 4);
        var src = new HdDirSource(d);
        var counts = new int[16];
        // the first look from 16 threads at once: one listing, the same answer everywhere
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t => counts[t] = src.Exists(Name(0)) ? src.Count : -1);
        Assert.All(counts, c => Assert.Equal(3, c));
        File.WriteAllBytes(Path.Combine(d, Name(3)), Content(3, 4));
        // the index made at the first look answers every question after it (a mission asks for a thousand names)
        Assert.False(src.Exists(Name(3)));
        Assert.Null(src.Read(Name(3)));
        Assert.Equal(3, src.Count);
    }

    [Fact]
    public void AChainsCountIsTheSameFromEveryThread()
    {
        // each name once: 30 in the 8x, 60 in the 4x, 30 of them in both
        using var chain = new HdChainSource([new HdDirSource(Folder("c8", Enumerable.Range(0, 30), 8)), new HdDirSource(Folder("c4", Enumerable.Range(0, 60), 4))]);
        var counts = new int[64];
        Parallel.For(0, counts.Length, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t => counts[t] = chain.Count);
        Assert.All(counts, c => Assert.Equal(60, c));
        Assert.True(chain.ThreadSafe);
    }
}
