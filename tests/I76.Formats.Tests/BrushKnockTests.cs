using System.Numerics;
using I76.Formats.Vegetation;
using Xunit;

namespace I76.Formats.Tests;

/// <summary>A car knocking a plant down (docs/modern-vegetation.md, Brush impacts: Cars): the fall's time and curve, and the knock's sound.</summary>
public class BrushKnockTests
{
    private const float Mph = 0.44704f;

    [Fact]
    public void AtDrivingSpeedAPlantIsDownInATenthToAFifthOfASecondSoonerTheFaster()
    {
        Assert.InRange(BrushKnock.Seconds(30 * Mph), 0.12f, 0.2f);
        Assert.InRange(BrushKnock.Seconds(60 * Mph), 0.08f, 0.12f);
        Assert.InRange(BrushKnock.Seconds(100 * Mph), 0.06f, 0.1f);
        Assert.True(BrushKnock.Seconds(100 * Mph) < BrushKnock.Seconds(60 * Mph));
        // a nudge at walking pace bends it slowly, and only partway
        Assert.InRange(BrushKnock.Seconds(1.5f), 0.6f, BrushKnock.Slowest);
        Assert.InRange(BrushKnock.Seconds(10 * Mph), 0.25f, 0.4f);
        Assert.InRange(BrushKnock.Depth(1.5f), 0.5f, 0.6f);
        Assert.Equal(1f, BrushKnock.Depth(30 * Mph));
    }

    [Theory]
    [InlineData(BrushKnock.Shape.Smooth)]
    [InlineData(BrushKnock.Shape.Snap)]
    [InlineData(BrushKnock.Shape.Topple)]
    public void EveryCurveRunsFromStandingToDownAndNeverBack(BrushKnock.Shape shape)
    {
        Assert.Equal(0f, BrushKnock.Curve(0, shape), 4);
        Assert.Equal(1f, BrushKnock.Curve(1, shape), 4);
        float last = 0;
        for (int i = 1; i <= 50; i++) { float c = BrushKnock.Curve(i / 50f, shape); Assert.True(c >= last); last = c; }
    }

    [Fact]
    public void TheSnapIsMostOfTheWayDownInAThirdOfItsTime()
    {
        Assert.True(BrushKnock.Curve(1 / 3f, BrushKnock.Shape.Snap) > 0.7f);
        Assert.True(BrushKnock.Curve(1 / 3f, BrushKnock.Shape.Topple) < 0.15f);
    }

    [Fact]
    public void TheLaidValueIsTheInverseOfTheShadersBend()
    {
        for (int i = 0; i <= 20; i++)
        {
            float d = i / 20f;
            Assert.Equal(d, BrushKnock.Drawn(BrushKnock.LayFor(d)), 3);
        }
        Assert.Equal(0.6f, BrushKnock.LayFor(1), 3);
    }

    /// <summary>The field: a knocked cell goes down over the knock's time along the curve, not at once, and then rises as before.</summary>
    [Fact]
    public void AKnockedCellFallsOverItsTimeThenRisesAsBefore()
    {
        var f = new BrushField();
        f.Follow(0, 0);
        float secs = BrushKnock.Seconds(30 * Mph), dt = 1 / 60f;
        f.Stamp(0.3f, 0.3f, 0.75f, 1f, 0, 0, Vector2.UnitY, 0, secs);
        Assert.Equal(0f, f.Lay(0.3f, 0.3f));
        Assert.True(f.Falling(0.3f, 0.3f));
        float t = 0, half = -1;
        while (f.Falling(0.3f, 0.3f) && t < 2)
        {
            f.Decay(dt); t += dt;
            if (half < 0 && BrushKnock.Drawn(f.Lay(0.3f, 0.3f)) >= 0.5f) half = t;
        }
        Assert.InRange(t, secs - dt, secs + 2 * dt);
        Assert.InRange(half, 0, secs * 0.4f);   // the snap: half way down within 40% of the time
        Assert.Equal(1f, f.Lay(0.3f, 0.3f), 3);
        Assert.True(Vector2.Dot(f.Direction(0.3f, 0.3f), Vector2.UnitY) > 0.99f);
        for (int i = 0; i < 60; i++) f.Decay(dt);
        Assert.Equal(1f - 1f / BrushField.Recover, f.Lay(0.3f, 0.3f), 2);
    }

    [Fact]
    public void TheTrackStampingAgainEveryFrameDoesNotRestartTheFall()
    {
        var f = new BrushField();
        f.Follow(0, 0);
        float secs = 0.2f, dt = 1 / 60f, t = 0;
        for (; t < 1 && (t == 0 || f.Falling(0.3f, 0.3f)); t += dt)
        {
            f.Stamp(0.3f, 0.3f, 0.75f, 1f, 0, 0, Vector2.UnitY, 0, secs);
            f.Decay(dt);
        }
        Assert.InRange(t, secs - dt, secs + 2 * dt);
    }

    /// <summary>A nudge's depth is the bend drawn: the field holds the laid value that draws it, so the plant ends half bent, not flat.</summary>
    [Fact]
    public void ANudgeBendsThePlantPartwayOnly()
    {
        var f = new BrushField();
        f.Follow(0, 0);
        float depth = BrushKnock.Depth(1.5f);
        f.Stamp(0.3f, 0.3f, 0.75f, BrushKnock.LayFor(depth), 0, 0, Vector2.UnitY, 0, BrushKnock.Seconds(1.5f));
        for (int i = 0; i < 60 && f.Falling(0.3f, 0.3f); i++) f.Decay(1 / 60f);
        Assert.False(f.Falling(0.3f, 0.3f));
        Assert.InRange(BrushKnock.Drawn(f.Lay(0.3f, 0.3f)), 0.45f, depth + 0.01f);
    }

    [Fact]
    public void OnlyThePlantsTheFieldReachesCanBeKnocked()
    {
        var f = new BrushField();
        f.Follow(0, 0);
        Assert.True(f.Covers(90, -90));
        Assert.False(f.Covers(100, 0));
        // a stamp beyond it leaves nothing to fall (BrushImpacts skips those plants, else it would knock them every frame)
        f.Stamp(100, 0, 0.75f, 1, 0, 0, Vector2.UnitY, 0, 0.1f);
        Assert.False(f.Falling(100, 0));
        Assert.Equal(0, f.Active);
    }

    [Fact]
    public void ABlastStillLaysThemAtOnce()
    {
        var f = new BrushField();
        f.Follow(0, 0);
        f.Stamp(0.3f, 0.3f, 0.75f, 1f, 0, 0, Vector2.UnitY, 0, 0.2f);
        f.Stamp(0, 0, 6, 1f, 1f);
        Assert.Equal(1f, f.Lay(0.3f, 0.3f), 3);
        Assert.False(f.Falling(0.3f, 0.3f));
    }

    [Fact]
    public void TheKnockIsLouderTheFasterAndEachPlantSoundsALittleApart()
    {
        var slow = BrushKnock.Voice(3, 1, 10, 10).Db;
        var fast = BrushKnock.Voice(100 * Mph, 1, 10, 10).Db;
        Assert.True(fast - slow > 10, $"{slow} -> {fast}");
        Assert.InRange(fast, BrushKnock.DefaultDb - 1.6f, BrushKnock.DefaultDb + 1.6f);
        // a row of bushes 2 m apart: pitches spread over most of the range, none the same
        var pitches = Enumerable.Range(0, 12).Select(i => BrushKnock.Voice(20, 1, 100 + 2 * i, 50).Pitch).ToList();
        float shift = BrushKnock.Ratio(BrushKnock.DefaultSemitones);
        Assert.All(pitches, p => Assert.InRange(p, 0.75f * shift, 1.25f * shift));
        Assert.True(pitches.Max() - pitches.Min() > 0.12f);
        Assert.Equal(pitches.Count, pitches.Distinct().Count());
        // the same plant always sounds the same (each player's machine alike)
        Assert.Equal(BrushKnock.Voice(20, 1, 104, 50), BrushKnock.Voice(20, 1, 104, 50));
        // a big plant lower than a small one, on average
        var big = Enumerable.Range(0, 40).Average(i => BrushKnock.Voice(20, 3, i * 1.3f, 7).Pitch);
        var small = Enumerable.Range(0, 40).Average(i => BrushKnock.Voice(20, 0.5f, i * 1.3f, 7).Pitch);
        Assert.True(big < small);
    }

    [Fact]
    public void TheDefaultKnockIsHigherAndQuieterThanTheSignsAndStillLouderTheFaster()
    {
        Assert.Equal("vnvco3.wav", BrushKnock.DefaultSample);
        Assert.InRange(BrushKnock.DefaultSemitones, 4f, 12f);
        Assert.InRange(BrushKnock.DefaultDb, -12f, -4f);
        // the review's C: three semitones higher and 3 dB quieter than FL's 9 and -8, started 30 % in
        Assert.Equal(12f, BrushKnock.DefaultSemitones);
        Assert.Equal(-11f, BrushKnock.DefaultDb);
        Assert.Equal(30f, BrushKnock.DefaultStartPercent);
        Assert.Equal(0f, BrushKnock.DefaultLengthMs);
        var c = BrushKnock.VariantOf(BrushKnock.DefaultVariant.ToString())!;
        Assert.Equal((BrushKnock.DefaultSample, BrushKnock.DefaultSemitones, BrushKnock.DefaultDb, BrushKnock.DefaultStartPercent, BrushKnock.DefaultLengthMs),
            (c.Sample, c.Semitones, c.Db, c.StartPercent, c.LengthMs));
        Assert.Equal(1.4983f, BrushKnock.Ratio(7), 3);
        Assert.Equal(2f, BrushKnock.Ratio(12), 4);
        // the sign plays at its own level (0 dB); the bush's loudest is quieter, and 10 / 30 / 60 mph rise
        var levels = new[] { 10, 30, 60 }.Select(m => BrushKnock.Voice(m * Mph, 1, 10, 10).Db).ToList();
        Assert.True(levels[0] < levels[1] && levels[1] < levels[2], string.Join(", ", levels));
        Assert.True(BrushKnock.Voice(100 * Mph, 1, 10, 10).Db < -3f);
        // the pitch is above the sign's for every plant
        Assert.All(Enumerable.Range(0, 30), i => Assert.True(BrushKnock.Voice(20, 2, i * 1.7f, 3).Pitch > 1.1f));
    }

    private static void Restore(Action body)
    {
        var (s, st, db, sp, ln, si) = (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb, BrushKnock.StartPercent, BrushKnock.LengthMs, BrushKnock.Silent);
        try { body(); }
        finally { (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb, BrushKnock.StartPercent, BrushKnock.LengthMs, BrushKnock.Silent) = (s, st, db, sp, ln, si); }
    }

    [Fact]
    public void AStartPastTheAttackSkipsThatShareOfTheSoundAndALengthCutsIt()
    {
        // vnvco3: 4256 frames at 11025 Hz (386 ms); 30 % in is frame 1277 (116 ms), to its end
        Assert.Equal((1277, 4256), BrushKnock.Span(4256, 11025, 30, 0));
        Assert.Equal((0, 4256), BrushKnock.Span(4256, 11025, 0, 0));
        // 300 ms of a 1463 ms sound from 21 %: 3308 frames on
        Assert.Equal((3387, 3387 + 3308), BrushKnock.Span(16129, 11025, 21, 300));
        // a length past the end stops at the end; a start is held to 90 %; never an empty span
        Assert.Equal((1277, 4256), BrushKnock.Span(4256, 11025, 30, 5000));
        Assert.Equal((3830, 4256), BrushKnock.Span(4256, 11025, 100, 0));
        Assert.Equal((0, 4256), BrushKnock.Span(4256, 11025, -20, 0));
        Assert.Equal((0, 1), BrushKnock.Span(1, 11025, 50, 0));
        Assert.Equal((10, 11), BrushKnock.Span(100, 11025, 10, 0.001f));
        Assert.Equal((0, 0), BrushKnock.Span(0, 11025, 30, 0));
    }

    [Fact]
    public void ALateStartFadesInOverEightMsAndACutFadesOutOverTwenty()
    {
        const int rate = 11025, frames = 4256;
        int fin = (int)MathF.Round(BrushKnock.FadeInMs * rate / 1000), fout = (int)MathF.Round(BrushKnock.FadeOutMs * rate / 1000);
        Assert.Equal((88, 220), (fin, fout));
        // whole: untouched
        var whole = BrushKnock.Span(frames, rate, 0, 0);
        Assert.All(new[] { 0, 1, 100, frames - 1 }, i => Assert.Equal(1f, BrushKnock.Gain(i, whole, frames, rate)));
        // from 30 %: 0 at the cut, rising linearly, whole from 8 ms on; the natural end is not faded
        var late = BrushKnock.Span(frames, rate, 30, 0);
        Assert.Equal(0f, BrushKnock.Gain(0, late, frames, rate));
        Assert.Equal(0.5f, BrushKnock.Gain(44, late, frames, rate), 3);
        Assert.Equal(1f, BrushKnock.Gain(fin, late, frames, rate));
        Assert.Equal(1f, BrushKnock.Gain(late.End - late.Start - 1, late, frames, rate));
        // cut short: the last frame silent, the fade 20 ms long
        var cut = BrushKnock.Span(frames, rate, 0, 200);
        int n = cut.End - cut.Start;
        Assert.Equal(1f, BrushKnock.Gain(0, cut, frames, rate));
        Assert.Equal(1f, BrushKnock.Gain(n - fout - 1, cut, frames, rate));
        Assert.Equal(0f, BrushKnock.Gain(n - 1, cut, frames, rate));
        float last = 1f;
        for (int i = n - fout; i < n; i++) { float g = BrushKnock.Gain(i, cut, frames, rate); Assert.True(g <= last); last = g; }
    }

    [Fact]
    public void TrimCutsThePcmFramesAndFadesThemIn8And16Bit()
    {
        // 8-bit unsigned mono: a full-scale square, 1000 frames at 1000 Hz (fades of 8 and 20 frames)
        var data8 = Enumerable.Range(0, 1000).Select(i => (byte)(i % 2 == 0 ? 228 : 28)).ToArray();
        var pcm8 = new I76.Formats.Audio.Pcm(1, 1000, 8, data8);
        var t8 = BrushKnock.Trim(pcm8, 30, 0);
        Assert.Equal(700, t8.Data.Length);
        Assert.Equal((1, 1000, 8), (t8.Channels, t8.Rate, t8.Bits));
        Assert.Equal(128, t8.Data[0]);                       // silent at the cut (centre of 8-bit)
        Assert.Equal(128 + 50, t8.Data[4]);                  // half way up the fade: 100 * 0.5
        Assert.Equal(data8[300 + 8], t8.Data[8]);            // whole from the fade's end
        Assert.Equal(data8[999], t8.Data[^1]);
        // 16-bit stereo, cut to 100 ms from 50 %: the last frame silent on both channels
        var data16 = new byte[1000 * 4];
        for (int i = 0; i < 1000; i++) for (int c = 0; c < 2; c++) { short v = (short)(c == 0 ? 20000 : -20000); data16[i * 4 + c * 2] = (byte)v; data16[i * 4 + c * 2 + 1] = (byte)(v >> 8); }
        var t16 = BrushKnock.Trim(new I76.Formats.Audio.Pcm(2, 1000, 16, data16), 50, 100);
        Assert.Equal(100 * 4, t16.Data.Length);
        short S(int frame, int ch) => (short)(t16.Data[frame * 4 + ch * 2] | t16.Data[frame * 4 + ch * 2 + 1] << 8);
        Assert.Equal((0, 0), ((int)S(0, 0), (int)S(0, 1)));
        Assert.Equal((20000, -20000), ((int)S(50, 0), (int)S(50, 1)));
        Assert.Equal((0, 0), ((int)S(99, 0), (int)S(99, 1)));
        // played whole: the same PCM back
        Assert.Same(pcm8, BrushKnock.Trim(pcm8, 0, 0));
    }

    [Fact]
    public void TheVariantsAreTheReviewsLettersAndOffSilencesTheKnock() => Restore(() =>
    {
        Assert.Equal("abcdefg", new string(BrushKnock.Variants.Select(v => v.Letter).ToArray()));
        var a = BrushKnock.VariantOf("A")!;
        Assert.Equal(("vnvco3.wav", 9f, -8f, 0f, 0f), (a.Sample, a.Semitones, a.Db, a.StartPercent, a.LengthMs));   // section FL's knock
        var b = BrushKnock.VariantOf("b")!;
        Assert.Equal((a.Semitones + 3, a.Db - 3, 0f), (b.Semitones, b.Db, b.StartPercent));
        var d = BrushKnock.VariantOf("d")!;
        Assert.Equal((a.Semitones, b.Db, 30f), (d.Semitones, d.Db, d.StartPercent));
        Assert.All(BrushKnock.Variants.Skip(4), v => Assert.NotEqual("vnvco3.wav", v.Sample));
        Assert.Null(BrushKnock.VariantOf("off"));
        Assert.Null(BrushKnock.VariantOf("h"));
        Assert.Null(BrushKnock.VariantOf(""));
        Assert.Null(BrushKnock.VariantOf(null));

        BrushKnock.Use(BrushKnock.VariantOf("g"));
        Assert.False(BrushKnock.Silent);
        Assert.Equal(("cmap2.wav", 21f, 300f), (BrushKnock.Sample, BrushKnock.StartPercent, BrushKnock.LengthMs));
        Assert.True(BrushKnock.Trims);
        BrushKnock.Use(BrushKnock.VariantOf("a"));
        Assert.False(BrushKnock.Trims);
        Assert.Equal(0f, BrushKnock.StartSeconds(0.386));
        BrushKnock.Use(BrushKnock.VariantOf("c"));
        Assert.Equal(0.1158f, BrushKnock.StartSeconds(0.386), 4);
        BrushKnock.Use(null);
        Assert.True(BrushKnock.Silent);
    });

    [Fact]
    public void TheEnvHookReadsNameSemitonesDbStartAndLength() => Restore(() =>
    {
        BrushKnock.Use(BrushKnock.VariantOf("c"));
        Assert.False(BrushKnock.Apply("vland,1,2,x"));
        Assert.Equal((BrushKnock.DefaultSample, 30f), (BrushKnock.Sample, BrushKnock.StartPercent));
        // a value left out keeps the variant's
        Assert.True(BrushKnock.Apply("vland,4"));
        Assert.Equal(("vland.wav", 4f, -11f, 30f, 0f), (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb, BrushKnock.StartPercent, BrushKnock.LengthMs));
        Assert.True(BrushKnock.Apply("vcddirt,0,-12,40,250"));
        Assert.Equal((40f, 250f), (BrushKnock.StartPercent, BrushKnock.LengthMs));
        Assert.True(BrushKnock.Apply("vcddirt,0,-12,150,-5"));
        Assert.Equal((90f, 0f), (BrushKnock.StartPercent, BrushKnock.LengthMs));
        // the hook sounds even over bush_knock=off
        BrushKnock.Use(null);
        Assert.True(BrushKnock.Apply("vnvco3"));
        Assert.False(BrushKnock.Silent);
    });

    [Fact]
    public void TheEnvHookReadsNameSemitonesAndDb()
    {
        var (s, st, db) = (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb);
        try
        {
            Assert.False(BrushKnock.Apply(""));
            Assert.False(BrushKnock.Apply("vland,x"));
            Assert.Equal(BrushKnock.DefaultSample, BrushKnock.Sample);
            Assert.True(BrushKnock.Apply("vland,4,-10"));
            Assert.Equal(("vland.wav", 4f, -10f), (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb));
            Assert.True(BrushKnock.Apply("cgrowl"));
            Assert.Equal(("cgrowl.wav", 4f, -10f), (BrushKnock.Sample, BrushKnock.Semitones, BrushKnock.OffsetDb));
            Assert.True(BrushKnock.Apply("vnvco3.wav,100,-100"));
            Assert.Equal((24f, -40f), (BrushKnock.Semitones, BrushKnock.OffsetDb));
        }
        finally { BrushKnock.Sample = s; BrushKnock.Semitones = st; BrushKnock.OffsetDb = db; }
    }

    [Fact]
    public void ACarPloughingAHedgeSoundsAtMostOneKnockEveryGap()
    {
        var k = new KnockSounds();
        int played = 0;
        // 60 fps, a plant struck every frame for a second
        for (int f = 0; f < 60; f++) if (k.Try(1, f / 60.0)) played++;
        Assert.InRange(played, 9, (int)(1 / KnockSounds.Gap) + 1);
        // and all the cars together at most PerSecond a second
        var all = new KnockSounds();
        int total = 0;
        for (int f = 0; f < 60; f++) for (ulong car = 1; car <= 8; car++) if (all.Try(car, f / 60.0)) total++;
        Assert.Equal(KnockSounds.PerSecond, total);
        // after a pause a car is heard again at once
        Assert.True(k.Try(1, 5.0));
    }
}
