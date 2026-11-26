using I76.Game.Boot;
using I76.Game.Shell;
using I76.Sim.Save;
using Xunit;

namespace I76.Game.Tests;

/// <summary>The shell's side of the generated remix scenarios (docs/remix-missions.md): the scenario list's scroll hint and the checkpoint notice.</summary>
public class RemixShellTests
{
    /// <summary>Bug 15: the SCENARIO form's six rows say how many more lie above and below.</summary>
    [Fact]
    public void TheScenarioListSaysHowManyMoreRowsThereAre()
    {
        Assert.Equal((null, null), MeleeData.ScrollHint(0, 6, 6));
        Assert.Equal((null, "6 more below"), MeleeData.ScrollHint(0, 6, 12));
        Assert.Equal(("3 more above", "3 more below"), MeleeData.ScrollHint(3, 6, 12));
        Assert.Equal(("6 more above", null), MeleeData.ScrollHint(6, 6, 12));
        Assert.Equal((null, null), MeleeData.ScrollHint(0, 6, 3));
    }

    /// <summary>Bug 14: a remix's checkpoint refused because the remix was rebuilt since gets its own word; other refusals keep theirs.</summary>
    [Fact]
    public void ARebuiltRemixsCheckpointIsToldAsSuch()
    {
        var mismatch = new SnapshotMismatchException("the snapshot was made with a different install");
        Assert.Equal(Checkpoint.Problem.RemixChanged, Checkpoint.RestoreProblem(mismatch, remix: true));
        Assert.Equal(Checkpoint.Problem.Unreadable, Checkpoint.RestoreProblem(mismatch, remix: false));
        Assert.Equal(Checkpoint.Problem.Unreadable, Checkpoint.RestoreProblem(new InvalidDataException(), remix: true));
        Assert.Contains("rebuilt", Checkpoint.Notice(Checkpoint.Problem.RemixChanged));
        Assert.DoesNotContain("rebuilt", Checkpoint.Notice(Checkpoint.Problem.Unreadable));
    }
}
