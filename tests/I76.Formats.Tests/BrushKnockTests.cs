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
        Assert.InRange(BrushKnock.DefaultDb, -10f, -4f);
        Assert.Equal(1.4983f, BrushKnock.Ratio(7), 3);
        Assert.Equal(2f, BrushKnock.Ratio(12), 4);
        // the sign plays at its own level (0 dB); the bush's loudest is quieter, and 10 / 30 / 60 mph rise
        var levels = new[] { 10, 30, 60 }.Select(m => BrushKnock.Voice(m * Mph, 1, 10, 10).Db).ToList();
        Assert.True(levels[0] < levels[1] && levels[1] < levels[2], string.Join(", ", levels));
        Assert.True(BrushKnock.Voice(100 * Mph, 1, 10, 10).Db < -3f);
        // the pitch is above the sign's for every plant
        Assert.All(Enumerable.Range(0, 30), i => Assert.True(BrushKnock.Voice(20, 2, i * 1.7f, 3).Pitch > 1.1f));
    }

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
