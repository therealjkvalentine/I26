using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// A movie's picture held to its sound through a hitch of the main thread that feeds the sound (MovieAudioClock, #157):
/// a generator of the given buffer, filled each frame from the main thread with all it has room for, played by the mixer
/// in real time; one frame of the main thread takes <c>hitch</c> seconds, of which the engine's delta carries at most
/// <c>deltaCap</c>.
/// </summary>
public class MovieAudioClockTests
{
    private const int Rate = 22050;

    /// <summary>Plays 10 s of a movie at 60 frames a second with one hitch at 3 s; the picture clock and the sound heard at 6 s, the sound's gap.</summary>
    private static (double Picture, double Heard, double Gap) Play(double buffer, double hitch, bool held, double deltaCap = double.MaxValue, double decodedAhead = 4)
    {
        int capacity = (int)(buffer * Rate);
        var clock = new MovieAudioClock(Rate, capacity);
        long buffered = 0, pushed = 0, total = 10 * Rate;
        double picture = 0, wall = 0, gap = 0;
        int Room() => (int)(capacity - buffered);
        void Push()
        {
            // what the decoder has made so far: the sound up to decodedAhead seconds past the picture
            long made = Math.Min(total, (long)((picture + decodedAhead) * Rate));
            long n = Math.Min(Room(), made - pushed);
            if (n <= 0) return;
            buffered += n; pushed += n; clock.Pushed((int)n);
        }
        void Mix(double dt)
        {
            long want = (long)(dt * Rate), took = Math.Min(want, buffered);
            buffered -= took;
            gap += (want - took) / (double)Rate * (pushed < total ? 1 : 0);
        }
        Push();
        while (wall < 6)
        {
            double dt = wall < 3 && wall + 1 / 60.0 >= 3 ? hitch : 1 / 60.0;
            wall += dt;
            Mix(dt);
            double delta = Math.Min(dt, deltaCap);
            picture = held ? clock.Step(picture, delta, Room(), pushed < total) : picture + delta;
            Push();
        }
        return (picture, clock.Heard(Room()), gap);
    }

    [Fact]
    public void AHitchLongerThanTheBufferLeavesThePictureWithItsSound()
    {
        // 0.5 s of buffer and a 1.5 s hitch the delta carries whole: the sound stops for about a second, the picture runs on
        var (before, beforeHeard, beforeGap) = Play(0.5, 1.5, held: false);
        var (after, afterHeard, afterGap) = Play(0.5, 1.5, held: true);
        Assert.True(beforeGap > 0.9, $"gap {beforeGap}");
        Assert.True(before - beforeHeard > 0.9, $"{before} vs {beforeHeard}");
        Assert.InRange(after - afterHeard, -MovieAudioClock.Lead, MovieAudioClock.Lead);
        Assert.True(afterGap > 0.9);
    }

    [Fact]
    public void AStallTheDeltaDoesNotCarryLeavesThePictureWithItsSound()
    {
        // a 6 s stall of which the delta carries 40 ms (as measured in the game), 4.5 s of buffer: the sound plays 4.5 s on
        var (before, beforeHeard, _) = Play(4.5, 6, held: false, deltaCap: 0.04);
        var (after, afterHeard, _) = Play(4.5, 6, held: true, deltaCap: 0.04);
        Assert.True(beforeHeard - before > 3.5, $"{before} vs {beforeHeard}");
        Assert.InRange(after - afterHeard, -MovieAudioClock.Lead, MovieAudioClock.Lead);
    }

    [Fact]
    public void ABufferOfFourAndAHalfSecondsCarriesTheSoundThroughTheHitch()
    {
        var (picture, heard, gap) = Play(4.5, 1.5, held: true);
        Assert.Equal(0, gap, 3);
        Assert.InRange(picture - heard, -MovieAudioClock.Lead, MovieAudioClock.Lead);
        Assert.True(picture > 5.9, $"picture at {picture}");
    }

    [Fact]
    public void ThePictureNeverGoesBackAndRunsOnAfterTheSound()
    {
        var c = new MovieAudioClock(Rate, Rate);
        // nothing heard yet: the picture waits within the lead
        double p = c.Step(0, 0.5, Rate, soundToCome: true);
        Assert.Equal(MovieAudioClock.Lead, p, 6);
        Assert.Equal(p, c.Step(p, 0.5, Rate, soundToCome: true));
        // the sound all heard (none to come, the buffer empty): the picture runs by its clock
        c.Pushed(Rate / 2);
        Assert.Equal(p + 1, c.Step(p, 1, Rate, soundToCome: false), 6);
    }
}
