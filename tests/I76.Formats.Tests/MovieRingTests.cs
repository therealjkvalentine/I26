using I76.Formats.Video;
using Xunit;

namespace I76.Formats.Tests;

public class MovieRingTests
{
    private static MovieRing<string> Ring(params int[] indices)
    {
        var r = new MovieRing<string>();
        foreach (int i in indices) r.Add(i, $"f{i}");
        return r;
    }

    [Fact]
    public void NothingIsTakenBeforeItsTurn()
    {
        var r = Ring(3, 4);
        Assert.False(r.TakeDue(2, out _, out _, out int passed));
        Assert.Equal(0, passed);
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void TheLatestDueFrameIsTakenAndTheOnesBeforeItArePassedOverAndReleased()
    {
        var released = new List<string>();
        var r = Ring(0, 1, 2, 3, 4);
        Assert.True(r.TakeDue(2, out int index, out var frame, out int passed, released.Add));
        Assert.Equal(2, index);
        Assert.Equal("f2", frame);
        Assert.Equal(2, passed);
        Assert.Equal(["f0", "f1"], released);
        Assert.Equal(2, r.Count);   // 3 and 4 wait for their turn
    }

    [Fact]
    public void TheDecoderDropsWhatALongStepPassedByButKeepsTheFrameToShowNext()
    {
        var released = new List<string>();
        var r = Ring(10, 11, 12, 13, 14, 15);
        // the clock ran on to 13 while the main thread was held: 10-12 will never go up, 13 is the next to show
        Assert.Equal(3, r.DropPassed(13, released.Add));
        Assert.Equal(["f10", "f11", "f12"], released);
        Assert.True(r.TakeDue(13, out int index, out _, out int passed));
        Assert.Equal(13, index);
        Assert.Equal(0, passed);
    }

    [Fact]
    public void NothingIsDroppedWhileTheClockIsBeforeTheSecondFrame()
    {
        var r = Ring(10, 11, 12);
        Assert.Equal(0, r.DropPassed(10));
        Assert.Equal(0, r.DropPassed(5));
        Assert.Equal(3, r.Count);
        var one = Ring(7);
        Assert.Equal(0, one.DropPassed(100));   // a lone frame stays: it is the one to show
        Assert.Equal(1, one.Count);
    }

    [Fact]
    public void ClearReleasesEverything()
    {
        var released = new List<string>();
        var r = Ring(1, 2, 3);
        r.Clear(released.Add);
        Assert.True(r.IsEmpty);
        Assert.Equal(3, released.Count);
        // with nothing to release them to (the movie over): still emptied, not a loop that never ends (a press during a briefing hung on this)
        var full = Ring(1, 2, 3);
        Assert.True(Task.Run(() => full.Clear()).Wait(2000));
        Assert.True(full.IsEmpty);
        var stale = Ring(1, 2, 3);
        Assert.True(Task.Run(() => stale.DropPassed(5)).Wait(2000));
        Assert.Equal(1, stale.Count);
    }

    [Fact]
    public void ADecoderAndAMainThreadTogetherShowEveryFrameInOrderOnce()
    {
        var r = new MovieRing<int>();
        const int n = 2000;
        var shown = new List<int>();
        var decoder = Task.Run(() =>
        {
            for (int i = 0; i < n; i++)
            {
                while (r.Count >= 8) Thread.Yield();
                r.Add(i, i);
            }
        });
        int want = 0, releasedCount = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while ((shown.Count == 0 || shown[^1] < n - 1) && sw.Elapsed.TotalSeconds < 20)
        {
            if (r.TakeDue(want, out int index, out int frame, out int passed, _ => releasedCount++))
            {
                Assert.Equal(index, frame);
                shown.Add(index);
            }
            if (want < n - 1) want++;
        }
        decoder.Wait();
        Assert.Equal(n - 1, shown[^1]);
        Assert.True(shown.SequenceEqual(shown.Order().Distinct()));
        Assert.Equal(n, shown.Count + releasedCount);
    }
}

public class MovieAheadTests
{
    private const double Fps15 = 3333 / 50000.0;   // the upscaled briefings' rate (50000/3333)

    [Fact]
    public void AtTheUpscaledBriefingsSizeTwoSecondsAheadFitsTheBudget()
    {
        long frame = 1920L * 1440 * 3 / 2;   // packed 4:2:0
        int n = MovieAhead.Frames(MovieAhead.LoadingSeconds, Fps15, frame);
        Assert.Equal(31, n);   // 2 s at 15.0015 fps, rounded up
        Assert.InRange(MovieAhead.Bytes(n, frame) / 1048576.0, 120, 130);
        Assert.Equal(12, MovieAhead.Frames(MovieAhead.PlainSeconds, Fps15, frame));
    }

    [Fact]
    public void TheMemoryBudgetCapsALargerMovie()
    {
        long frame = 3840L * 2880 * 3 / 2;   // 16.6 MB a frame
        int n = MovieAhead.Frames(2.0, 1 / 30.0, frame);
        Assert.Equal((int)(MovieAhead.BudgetBytes / frame), n);
        Assert.True(MovieAhead.Bytes(n, frame) <= MovieAhead.BudgetBytes);
        Assert.Equal(3, MovieAhead.Frames(2.0, 1 / 30.0, MovieAhead.BudgetBytes));   // never under three
    }

    [Fact]
    public void TheDueFrameRunsOnFromTheMainThreadsLastWord()
    {
        Assert.Equal(-1, MovieAhead.Due(false, 5, 1, Fps15));   // nothing up yet: every frame is wanted
        Assert.Equal(15, MovieAhead.Due(true, 1.0, 0, Fps15));
        // the main thread held 0.5 s in a step after saying 1.0 s: the sound played on, so the clock is at 1.5 s
        Assert.Equal(22, MovieAhead.Due(true, 1.0, 0.5, Fps15));
        Assert.Equal(15, MovieAhead.Due(true, 1.0, -3, Fps15));
    }

    [Theory]
    [InlineData(20, 20, 0, true)]    // the due frame itself
    [InlineData(25, 20, 0, true)]    // ahead
    [InlineData(19, 20, 0, false)]   // its turn gone: decoded only
    [InlineData(5, 20, 14, false)]
    [InlineData(5, 20, 15, true)]    // fifteen passed by in a row: one copied anyway, so a slow decoder still moves the picture
    [InlineData(0, -1, 0, true)]     // before the first frame is up
    public void OnlyFramesThatCanStillGoUpAreCopiedOut(long index, int due, int run, bool copy) =>
        Assert.Equal(copy, MovieAhead.ShouldCopy(index, due, run));
}

public class TheoraHeaderTests
{
    /// <summary>A first Ogg page holding a Theora identification header (one 42-byte packet).</summary>
    public static byte[] Page(int w, int h, int num, int den)
    {
        var page = new byte[27 + 1 + 42];
        "OggS"u8.CopyTo(page);
        page[5] = 2;   // beginning of stream
        page[26] = 1;  // one segment
        page[27] = 42;
        var p = page.AsSpan(28);
        p[0] = 0x80; "theora"u8.CopyTo(p[1..]);
        p[7] = 3; p[8] = 2; p[9] = 1;
        p[10] = (byte)((w + 15) / 16 >> 8); p[11] = (byte)((w + 15) / 16);
        p[12] = (byte)((h + 15) / 16 >> 8); p[13] = (byte)((h + 15) / 16);
        p[14] = (byte)(w >> 16); p[15] = (byte)(w >> 8); p[16] = (byte)w;
        p[17] = (byte)(h >> 16); p[18] = (byte)(h >> 8); p[19] = (byte)h;
        p[22] = (byte)(num >> 24); p[23] = (byte)(num >> 16); p[24] = (byte)(num >> 8); p[25] = (byte)num;
        p[26] = (byte)(den >> 24); p[27] = (byte)(den >> 16); p[28] = (byte)(den >> 8); p[29] = (byte)den;
        return page;
    }

    [Fact]
    public void TheSizeAndFrameRateComeFromTheFirstPage()
    {
        var info = TheoraHeader.Parse(Page(1920, 1440, 50000, 3333));
        Assert.NotNull(info);
        Assert.Equal((1920, 1440, 50000, 3333), (info.Value.Width, info.Value.Height, info.Value.FpsNum, info.Value.FpsDen));
        Assert.Equal(0.06666, info.Value.FrameDuration, 5);
    }

    [Fact]
    public void AnythingElseIsNotATheoraHeader()
    {
        Assert.Null(TheoraHeader.Parse([]));
        var vorbis = Page(1920, 1440, 30, 1);
        "vorbis"u8.CopyTo(vorbis.AsSpan(29));
        Assert.Null(TheoraHeader.Parse(vorbis));
        Assert.Null(TheoraHeader.Parse(Page(1920, 1440, 0, 1)));
        Assert.Null(TheoraHeader.Read(Path.Combine(Path.GetTempPath(), "no-such-movie.ogv")));
    }
}

[Collection("ogv library")]
public class OgvLibraryTests
{
    [Fact]
    public void AMissingLibraryIsNoErrorAndOpensNothing()
    {
        OgvLibrary.ResetForTests();
        try
        {
            string nowhere = Path.Combine(Path.GetTempPath(), "i26-no-such-dir", OgvLibrary.FileName);
            Assert.False(OgvLibrary.Load([null, "", nowhere]));
            Assert.False(OgvLibrary.Available);
            Assert.Contains(nowhere, OgvLibrary.Missing);
            Assert.Null(OgvDecoder.Open("anything.ogv"));
            // the first look decides: a later one does not look again
            Assert.False(OgvLibrary.Load([nowhere]));
        }
        finally { OgvLibrary.ResetForTests(); }
    }

    [Fact]
    public void AFileThatIsNotTheLibraryIsNotLoaded()
    {
        OgvLibrary.ResetForTests();
        string junk = Path.Combine(Path.GetTempPath(), $"i26-not-a-library-{Environment.ProcessId}.dll");
        File.WriteAllBytes(junk, [1, 2, 3, 4]);
        try { Assert.False(OgvLibrary.Load([junk])); }
        finally { File.Delete(junk); OgvLibrary.ResetForTests(); }
    }

    [Fact]
    public void ThePackedPictureHoldsTheLumaThenTheChromaSideBySide()
    {
        Assert.Equal((1920, 2160), OgvDecoder.PackedSize(1920, 1440));
        Assert.Equal((642, 362), OgvDecoder.PackedSize(641, 241));   // odd sizes: the chroma rounds up
    }
}
