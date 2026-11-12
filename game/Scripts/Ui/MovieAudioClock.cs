namespace I76.Game.Ui;

/// <summary>
/// A Smacker movie's picture clock held to its sound (#157). The sound is pushed from the main thread into the audio
/// generator's buffer, which plays on by itself through a hitch as long as the buffer lasts; a hitch longer than that
/// leaves the sound silent and then resuming where it stopped. The picture's clock (the frames' deltas) agrees with
/// neither: the engine's delta does not carry a long stall whole, so the picture fell behind sound that played on, and
/// had it carried it, it would have run ahead of sound that stopped. The picture is kept within <see cref="Lead"/> of
/// the sound heard so far either way, so after a hitch both go on together rather than apart for the rest of the movie.
/// Once the sound has all been heard (or the movie has none) the picture runs by its own clock.
/// </summary>
public sealed class MovieAudioClock(int mixRate, int capacity)
{
    /// <summary>How far the picture may run ahead of the sound heard (seconds): the mixer takes the buffer in chunks.</summary>
    public const double Lead = 0.1;

    private long _pushed;

    /// <summary>Frames of the movie's sound pushed into the generator so far.</summary>
    public long PushedFrames => _pushed;

    public void Pushed(int frames) => _pushed += frames;

    /// <summary>The frames still in the generator's buffer, from its free room.</summary>
    public long Buffered(int available) => Math.Max(0, capacity - available);

    /// <summary>The movie's sound taken by the mixer so far (seconds), from the generator's free room.</summary>
    public double Heard(int available) => Math.Max(0, _pushed - Buffered(available)) / (double)mixRate;

    /// <summary>
    /// The picture's clock after a frame of <paramref name="delta"/> seconds: moved on, and held within <see cref="Lead"/> of
    /// the sound heard while there is sound still to come (<paramref name="soundToCome"/>: decoded and not yet pushed, or
    /// still in the buffer), and brought up to within <see cref="Lead"/> behind it when it has fallen further back (the
    /// engine's frame delta does not carry the whole of a long stall: measured, a 6 s stall of the main thread moved the
    /// picture's clock by under 40 ms while the buffered sound played on). It never goes back: a picture ahead of the
    /// sound waits for it.
    /// </summary>
    public double Step(double clock, double delta, int available, bool soundToCome)
    {
        if (!soundToCome && Buffered(available) == 0) return clock + delta;
        double heard = Heard(available);
        return Math.Max(clock, Math.Clamp(clock + delta, heard - Lead, heard + Lead));
    }
}
