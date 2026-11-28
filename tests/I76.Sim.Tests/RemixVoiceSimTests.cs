using I76.Formats.Remix;
using I76.Formats.Vfs;
using I76.Sim.Missions;
using I76.Sim.Session;
using Xunit;

namespace I76.Sim.Tests;

/// <summary>
/// The spoken lines in the simulation (docs/remix-missions.md "Spoken lines"): the radio queues a generated job's line by its sound
/// name and is busy for the table's length of it, whatever this machine could build; a cut of a clip keeps its whole name in the
/// queue.
/// </summary>
[Collection("remix")]   // GameInstall.UserMissionDir is one for the process: the remix test classes take turns
public class RemixVoiceSimTests
{
    [Fact]
    public void ACutsNameIsQueuedWholeAndOthersAtFifteenCharacters()
    {
        Assert.Equal("06sgc15@1625-2895.wav", CbRadio.Queued("06sgc15@1625-2895.wav"));
        Assert.Equal("abcdefghijklmno", CbRadio.Queued("abcdefghijklmnopq.wav"));
        Assert.Equal("rxvt108.wav", CbRadio.Queued("rxvt108.wav"));
    }

    /// <summary>A rolled tail's opening line (the police are coming) is on the air at the start, for the table's length of it.</summary>
    [Fact]
    public void AJobsLineIsOnTheAirForTheTablesLength()
    {
        if (SnapshotRig.Install is not { } inst) return;
        var dir = Path.Combine(Path.GetTempPath(), "i26-voice-tests-" + Environment.ProcessId);
        GameInstall.UserMissionDir = dir;
        var spec = RemixDirector.Roll(103, new RemixDirector.JobAsk { Id = "xvoice", Map = "t08", Template = "tail" });
        var start = spec.Voice.First(v => v.Moment == "police");
        Assert.NotNull(start.Line);
        RemixBuilder.Build(new GameInstall(inst), spec).WriteTo(dir);
        var l = new SnapshotRig.Loaded(inst);
        var (s, _) = SnapshotRig.Make(l, SnapshotRig.BuildOf(l.Dir, "xvoice", "vppirna1.vcf"), SimRate.R20, 1);
        s.Tick([new CarInput()]);
        var line = RemixVoice.ById(start.Line!)!;
        Assert.Equal(line.Sound + ".wav", s.Script!.Host.Cb.Current?.Name, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(RemixVoice.Seconds(line), s.Script.Host.Cb.Current!.Length);
    }
}
