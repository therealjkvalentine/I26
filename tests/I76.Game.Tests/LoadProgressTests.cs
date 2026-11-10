using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The loading card's wheel ([port] load_progress): the progress model (its tables, only up, 1 at the end), the pie's geometry, and
/// the key's words both ways.
/// </summary>
public class LoadProgressTests
{
    public static TheoryData<string> Tables => new() { "first", "restart" };
    private static LoadProgressModel.Stage[] Of(string name) => name == "first" ? LoadProgressModel.First : LoadProgressModel.Restart;

    [Theory]
    [MemberData(nameof(Tables))]
    public void TheSharesSumToOneAndEveryMilestoneMovesItOn(string name)
    {
        var stages = Of(name);
        var ends = LoadProgressModel.Ends(stages);
        Assert.Equal(1.0, ends[^1], 12);
        Assert.Equal(1.0, stages.Sum(s => s.Share) / stages.Sum(s => s.Share), 12);
        Assert.All(stages, s => Assert.True(s.Share > 0, s.Stamp));
        for (int i = 1; i < ends.Length; i++) Assert.True(ends[i] > ends[i - 1], stages[i].Stamp);
        Assert.Equal(stages.Length, stages.Select(s => s.Stamp).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void NoStageHoldsMostOfTheLoad(string name)
    {
        var stages = Of(name);
        double total = stages.Sum(s => s.Share);
        Assert.All(stages, s => Assert.True(s.Share / total < 0.25, $"{s.Stamp}: {s.Share / total:P0}"));   // (the first frame drawn, a restart's longest: 21 %)
    }

    [Fact]
    public void ALoadInOrderGoesUpToOne()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        Assert.Equal(0, m.Value);
        double last = 0;
        foreach (var s in LoadProgressModel.First)
        {
            m.Mark(s.Stamp);
            Assert.True(m.Value > last, s.Stamp);
            last = m.Value;
        }
        Assert.Equal(1.0, m.Value, 12);
    }

    [Fact]
    public void ItNeverGoesBack()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        m.Mark("look set");
        double at = m.Value;
        m.Mark("build start");            // a stamp out of order
        m.Mark("not a milestone");        // and one that is none (a car's, the 1997 look's)
        m.Part(LoadProgressModel.CarsMade, 0, 10);
        Assert.Equal(at, m.Value);
        m.Use(LoadProgressModel.Restart); // the restart's table, found at the build's start: never lower
        Assert.Equal(at, m.Value);
        m.Mark("spawn: combat view");
        Assert.True(m.Value > at);
    }

    [Fact]
    public void TheCarsMoveItACarAtATime()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        m.Mark("spawn: the player's car set up");
        double from = m.Value, last = from;
        for (int k = 1; k <= 16; k++)
        {
            m.Part(LoadProgressModel.CarsMade, k, 16);
            Assert.True(m.Value > last);
            last = m.Value;
        }
        double end = m.Value;
        m.Mark(LoadProgressModel.CarsMade);
        Assert.Equal(end, m.Value, 12);   // all of them is the stage's end
    }

    [Fact]
    public void AColdRoadBakeIsWeighedAgainAndMovesItAChunkAtATime()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        m.Mark("road bake");
        double at = m.Value;
        m.Weigh(LoadProgressModel.RoadBake, LoadProgressModel.ColdRoadBakeMs);   // the bake cache had none of the layers
        Assert.Equal(at, m.Value);
        double last = at;
        int moved = 0;
        for (int k = 1; k <= 60; k++)
        {
            m.Part(LoadProgressModel.RoadBake, k, 60);
            Assert.True(m.Value >= last);
            if (m.Value > last) moved++;
            last = m.Value;
        }
        Assert.True(moved > 50, $"{moved} of 60 chunks moved it");
        Assert.True(m.Value > 0.3, $"{m.Value:P0}");                         // the bake now most of the build's share
        m.Mark(LoadProgressModel.RoadBake);
        Assert.Equal(last, m.Value, 12);
        m.Finish();
        Assert.Equal(1.0, m.Value);
    }

    [Fact]
    public void ALoadThatSkipsMilestonesStillEndsAtOne()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        m.Mark("done");                   // a network game: no car stamps, no rehearsal
        m.Mark("look set");
        Assert.True(m.Value < 1);
        m.Finish();
        Assert.Equal(1.0, m.Value);
        m.Mark("build start");
        Assert.Equal(1.0, m.Value);
    }

    [Fact]
    public void TheWordIsTheStageUnderWay()
    {
        var m = new LoadProgressModel(LoadProgressModel.First);
        Assert.Equal(LoadProgressModel.Terrain, m.Word);
        m.Mark("done");
        Assert.Equal(LoadProgressModel.Scenery, m.Word);
        m.Mark("look set");
        Assert.Equal(LoadProgressModel.Vehicles, m.Word);
        m.Mark("spawn: combat view");
        Assert.Equal(LoadProgressModel.Cockpit, m.Word);
        m.Mark("vegetation prewarmed");
        Assert.Equal(LoadProgressModel.Effects, m.Word);
    }

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.249, 24)]
    [InlineData(0.25, 25)]
    [InlineData(0.999, 99)]
    [InlineData(1.0, 100)]
    [InlineData(1.5, 100)]
    [InlineData(-1.0, -1)]
    public void ThePerCentIsTheFractionsFloor(double f, int percent) => Assert.Equal(percent, LoadWheel.Percent(f));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 90)]
    [InlineData(50, 180)]
    [InlineData(75, 270)]
    [InlineData(100, 360)]
    public void ThePieTurnsAQuarterForEveryQuarter(int percent, double degrees) => Assert.Equal(degrees, LoadWheel.FillDegrees(percent), 9);

    [Theory]
    [InlineData(0, -1, 0)]       // 12 o'clock
    [InlineData(1, 0, 90)]       // 3 o'clock: clockwise, the screen's y down
    [InlineData(0, 1, 180)]
    [InlineData(-1, 0, 270)]
    public void AnglesGoClockwiseFromTwelve(double x, double y, double degrees) => Assert.Equal(degrees, LoadWheel.Angle(x, y), 9);

    [Fact]
    public void ThePieFillsClockwiseFromTwelve()
    {
        // a quarter: the top right quadrant only
        Assert.True(LoadWheel.Loaded(25, 0.5, -0.5));
        Assert.False(LoadWheel.Loaded(25, 0.5, 0.5));
        Assert.False(LoadWheel.Loaded(25, -0.5, -0.5));
        // a half: the right side
        Assert.True(LoadWheel.Loaded(50, 0.5, 0.5));
        Assert.False(LoadWheel.Loaded(50, -0.5, 0.5));
        // three quarters: all but the top left
        Assert.True(LoadWheel.Loaded(75, -0.5, 0.5));
        Assert.False(LoadWheel.Loaded(75, -0.5, -0.5));
        // nothing and everything
        Assert.False(LoadWheel.Loaded(0, 0.01, -1));
        Assert.True(LoadWheel.Loaded(100, -0.01, -1));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("on", true)]
    [InlineData("\"on\"", true)]
    [InlineData("off", false)]
    [InlineData("OFF", false)]
    [InlineData("false", false)]
    [InlineData("nonsense", true)]
    public void TheKeyReadsItsWords(string? word, bool on) => Assert.Equal(on, ShellSettings.LoadProgressWord(word));

    [Fact]
    public void TheKeyGoesRoundTrip()
    {
        foreach (bool on in new[] { true, false })
            Assert.Equal(on, ShellSettings.LoadProgressWord(ShellSettings.LoadProgressName(on)));
        Assert.False(new ShellSettings().LoadProgress);   // off by default since the loading follows upstream's (written only when on)
        // the Developer panel's row: off, on (the port's), stepping the setting both ways
        var row = DevSettings.All.Single(e => e.Key == "load_progress");
        Assert.Equal(["off", "on" + ShellSettings.PortMark], row.Values);
        var s = new ShellSettings();
        Assert.Equal(0, row.Get(s));
        row.Set(s, 1);
        Assert.True(s.LoadProgress);
        Assert.Equal(1, row.Get(s));
        row.Set(s, 0);
        Assert.False(s.LoadProgress);
    }
}
