using I76.Game.Shell;
using I76.Game.Ui;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The load's progress (the line under PLEASE STAND BY: StandByLineTests has its drawing rule): the model (its tables, only up, 1 at
/// the end), and the withdrawn wheel's key ([port] load_progress) read and dropped.
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
    public void AFileWithTheWithdrawnWheelsKeyStillReadsAndDropsIt()
    {
        // the wheel's [port] load_progress=on, as a build with it wrote it: the rest of the file reads, the key is not written again
        var file = ShellFileCompatTests.Parse(ShellFileCompatTests.ForkMainFile + "\nload_progress=\"on\"\n");
        var s = ShellSettings.Read(file);
        Assert.Equal(2, s.WorldTextures);
        Assert.False(s.Write(null).HasSectionKey("port", "load_progress"));
        Assert.DoesNotContain(DevSettings.All, e => e.Key == "load_progress");
    }
}
