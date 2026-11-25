using I76.Game.Shell;
using I76.Sim.Trip;
using Xunit;

namespace I76.Game.Tests;

/// <summary>trip.cfg's Trip+ part (the play fork's): the finished flag and the [trip_plus] section, read back as written; an older file reads as the original's trip.</summary>
public class TripPlusSaveTests
{
    /// <summary>The file's values as the engine's ConfigFile hands them back: whole numbers as long, flags as bool.</summary>
    private static Func<string, string, object?> Stored(TripPlusSave s)
    {
        var d = s.Entries().ToDictionary(e => (e.Section, e.Key), e => e.Value is int i ? (object)(long)i : e.Value);
        return (sec, key) => d.TryGetValue((sec, key), out var v) ? v : null;
    }

    [Fact]
    public void AFileWithoutTheSectionIsAnUnfinishedTripOnLapOne()
    {
        var s = TripPlusSave.Read((_, _) => null);
        Assert.False(s.Finished);
        Assert.Equal(1, s.Lap);
        Assert.Equal(0, s.LapsDone);
        Assert.Null(s.Rules);
        Assert.Null(s.Active);
        // and the original's trip writes nothing of its own
        Assert.Empty(new TripPlusSave().Entries());
    }

    [Fact]
    public void TheSectionRoundTrips()
    {
        var s = new TripPlusSave(true, 3, 2, new TripPlusRules(2, -2, 8));
        var back = TripPlusSave.Read(Stored(s));
        Assert.Equal(s, back);
        Assert.Equal(new TripPlusRules(2, -2, 8), back.Active);
        var keys = s.Entries().Select(e => $"{e.Section}.{e.Key}").ToList();
        Assert.Equal(["trip.finished", "trip_plus.lap", "trip_plus.laps_done", "trip_plus.copies", "trip_plus.skill", "trip_plus.salvage"], keys);
    }

    [Fact]
    public void AT17WinFinishesTheTripAndItsLap()
    {
        var first = new TripPlusSave().Won17();
        Assert.True(first.Finished);
        Assert.Equal(1, first.LapsDone);
        Assert.Equal(first, TripPlusSave.Read(Stored(first)));   // the finished trip's laps done are kept with it
        var second = new TripPlusSave(true, 2, 1, new TripPlusRules(1, -1, 4)).Won17();
        Assert.Equal(2, second.LapsDone);
        Assert.Equal(2, second.Lap);
        // an earlier lap won again does not lower the count
        Assert.Equal(3, new TripPlusSave(true, 1, 3).Won17().LapsDone);
    }

    [Fact]
    public void ValuesOutOfRangeAreHeld()
    {
        var d = new Dictionary<(string, string), object>
        {
            [("trip_plus", "lap")] = 2L, [("trip_plus", "copies")] = 9L, [("trip_plus", "skill")] = -9L, [("trip_plus", "salvage")] = -3L,
        };
        var s = TripPlusSave.Read((sec, key) => d.TryGetValue((sec, key), out var v) ? v : null);
        Assert.Equal(new TripPlusRules(3, -4, 0), s.Rules);
    }
}
