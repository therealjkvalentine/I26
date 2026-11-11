using I76.Formats.Assembly;
using I76.Game.Assets;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A pack's .dds texels for CPU painting (<see cref="HeldDecode{T}"/>, TextureFactory's upscales): not kept, a prefetch's decode held
/// until it is taken once, and one decode of a picture at a time from many threads. Synthetic textures only.
/// </summary>
public class HeldDecodeTests
{
    /// <summary>A 64x32 texture's texels in a pattern of its own, as a .dds decompressed would give, counted per decode.</summary>
    private static (HeldDecode<RoadRaster.Tex> Cell, Func<int> Decodes) Picture(int seed, int delayMs = 0)
    {
        int decodes = 0, inside = 0;
        var cell = new HeldDecode<RoadRaster.Tex>(() =>
        {
            // two decodes of one picture at once would be seen here
            if (Interlocked.Increment(ref inside) != 1) throw new InvalidOperationException("decoded twice at once");
            Interlocked.Increment(ref decodes);
            if (delayMs > 0) Thread.Sleep(delayMs);
            var rgba = new byte[64 * 32 * 4];
            for (int i = 0; i < rgba.Length; i++) rgba[i] = (byte)(seed * 13 + i * 7);
            Interlocked.Decrement(ref inside);
            return new RoadRaster.Tex(64, 32, rgba);
        });
        return (cell, () => Volatile.Read(ref decodes));
    }

    private static bool Whole(RoadRaster.Tex t, int seed)
    {
        if (t.Width != 64 || t.Height != 32 || t.Rgba.Length != 64 * 32 * 4) return false;
        for (int i = 0; i < t.Rgba.Length; i++) if (t.Rgba[i] != (byte)(seed * 13 + i * 7)) return false;
        return true;
    }

    [Fact]
    public void TexelsAskedAreNotKept()
    {
        var (cell, decodes) = Picture(1);
        var a = cell.Take();
        var b = cell.Take();
        // each ask decodes: the caller keeps what it paints from, the factory holds nothing
        Assert.Equal(2, decodes());
        Assert.NotSame(a, b);
        Assert.False(cell.Holds);
        Assert.Null(cell.Held);
        Assert.True(Whole(a, 1) && Whole(b, 1));
    }

    [Fact]
    public void ADecodeAheadIsTakenOnceThenLetGo()
    {
        var (cell, decodes) = Picture(2);
        cell.Ahead();
        cell.Ahead();   // one held already: not decoded again
        Assert.Equal(1, decodes());
        Assert.True(cell.Holds);
        Assert.Equal(64 * 32 * 4, cell.Held!.Rgba.Length);
        var a = cell.Take();
        Assert.Equal(1, decodes());
        Assert.False(cell.Holds);
        Assert.True(Whole(a, 2));
        cell.Take();
        Assert.Equal(2, decodes());
    }

    [Fact]
    public void ManyThreadsAskingTheSamePictureNeverDecodeItTwiceAtOnce()
    {
        // 16 threads, each asking ahead and taking, on 8 pictures: every take whole, no decode overlapping another of its picture
        var pics = Enumerable.Range(0, 8).Select(s => Picture(s, delayMs: 1)).ToArray();
        int bad = 0, takes = 0;
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, t =>
        {
            var rng = new Random(t);
            for (int n = 0; n < 60; n++)
            {
                int s = rng.Next(pics.Length);
                if (rng.Next(3) == 0) pics[s].Cell.Ahead();
                else
                {
                    if (!Whole(pics[s].Cell.Take(), s)) Interlocked.Increment(ref bad);
                    Interlocked.Increment(ref takes);
                }
            }
        });
        Assert.Equal(0, bad);
        Assert.True(takes > 0);
        // every decode was taken, or is the one held now
        int decoded = pics.Sum(p => p.Decodes());
        int held = pics.Count(p => p.Cell.Holds);
        Assert.Equal(takes + held, decoded);
    }

    [Fact]
    public void ADecodeHeldIsTakenByOneThreadOnly()
    {
        // one decode ahead, 16 threads take at once: one gets it, the others decode their own (15 more), none twice the same object
        for (int round = 0; round < 20; round++)
        {
            var (cell, decodes) = Picture(round);
            cell.Ahead();
            var got = new RoadRaster.Tex[16];
            using var go = new ManualResetEventSlim();
            var threads = Enumerable.Range(0, 16).Select(i => new Thread(() => { go.Wait(); got[i] = cell.Take(); })).ToArray();
            foreach (var th in threads) th.Start();
            go.Set();
            foreach (var th in threads) th.Join();
            Assert.Equal(16, decodes());
            Assert.Equal(16, got.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.All(got, t => Assert.True(Whole(t, round)));
        }
    }
}
