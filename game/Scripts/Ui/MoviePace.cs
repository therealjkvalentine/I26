namespace I76.Game.Ui;

/// <summary>
/// A movie's picture pace as the player sees it: when each of its frames first came up (a frame passed over counts as up when
/// the next one shown did). A frame is late when it came up more than <see cref="LateFrames"/> of the movie's frame times after
/// it was due; its due time runs from the steadiest frame of the last <see cref="WindowSeconds"/> (the picture is held to the
/// sound, which can itself stop and resume, so the schedule is the recent one, not the first frame's). Engine-free so the rule is
/// tested (MoviePaceTests); MoviePlayer feeds it and the load profile reports it.
/// </summary>
public sealed class MoviePace(double frameSeconds)
{
    public const double LateFrames = 1.5, WindowSeconds = 0.5;

    private readonly List<(double At, int Index)> _shown = [];
    private double _lastFrameAt = double.NaN;

    /// <summary>The longest wall-clock gap between two of the game's frames while the movie played (seconds): the worst hitch.</summary>
    public double WorstFrame { get; private set; }

    /// <summary>A frame of the game at <paramref name="at"/> seconds (any clock) showed the movie's frame <paramref name="index"/>, or none (-1).</summary>
    public void Frame(double at, int index)
    {
        if (!double.IsNaN(_lastFrameAt)) WorstFrame = Math.Max(WorstFrame, at - _lastFrameAt);
        _lastFrameAt = at;
        if (index >= 0 && (_shown.Count == 0 || index > _shown[^1].Index)) _shown.Add((at, index));
    }

    /// <summary>The movie's frames from the first shown to the last, each once.</summary>
    public int Frames => _shown.Count == 0 ? 0 : _shown[^1].Index - _shown[0].Index + 1;

    /// <summary>How many of them came up late, and the latest one's lateness (seconds).</summary>
    public (int Late, double Worst) Late()
    {
        int late = 0, from = 0;
        double worst = 0;
        for (int s = 0; s < _shown.Count; s++)
        {
            var (at, index) = _shown[s];
            while (from < s - 1 && _shown[from].At < at - WindowSeconds) from++;   // (the frame before always counts: a stall longer than the window)
            double anchor = double.MaxValue;
            for (int j = from; j <= s; j++) anchor = Math.Min(anchor, _shown[j].At - _shown[j].Index * frameSeconds);
            // this shown frame and those passed over since the one before it all came up now
            int first = s == 0 ? index : _shown[s - 1].Index + 1;
            for (int k = first; k <= index; k++)
            {
                double lateBy = at - (anchor + k * frameSeconds);
                worst = Math.Max(worst, lateBy);
                if (lateBy > LateFrames * frameSeconds) late++;
            }
        }
        return (late, worst);
    }
}
