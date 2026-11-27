using Godot;
using I76.Formats.Input;
using I76.Formats.Remix;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// ROGUETRIP (the play fork's; docs/remix-missions.md "Roguetrip"), once ROAD TRIP on the SCENARIO form: its own entry in the main
/// menu's MELEE row after AUTO MELEE, the steps back from its screens, and runs saved under the old name resuming.
/// </summary>
public class RoguetripMenuTests
{
    /// <summary>With the original's sprites (MULTI MELEE 100 wide at 345, AUTO MELEE 90 at 445) ROGUETRIP abuts AUTO MELEE and nothing moves.</summary>
    [Fact]
    public void RoguetripFollowsAutoMeleeInTheMeleeRow()
    {
        var (mx, ax, rx) = MainMenuScreen.MeleeRowPlace(100, 90, 76);
        Assert.Equal(MainMenuScreen.MultiX, mx);
        Assert.Equal(MainMenuScreen.AutoX, ax);
        Assert.Equal(ax + 90, rx);
        Assert.True(rx + 76 <= MainMenuScreen.RowRightEdge);
    }

    /// <summary>A box too wide for the room left moves the row left together, in order, keeping the original's gap, ending at the edge.</summary>
    [Theory]
    [InlineData(100, 90, 130)]
    [InlineData(96, 94, 160)]
    public void AWideRoguetripMovesTheRowLeftTogether(int multi, int auto, int box)
    {
        var (mx, ax, rx) = MainMenuScreen.MeleeRowPlace(multi, auto, box);
        int gap = System.Math.Max(0, MainMenuScreen.AutoX - (MainMenuScreen.MultiX + multi));
        Assert.Equal(mx + multi + gap, ax);
        Assert.Equal(ax + auto, rx);
        Assert.Equal(MainMenuScreen.RowRightEdge, rx + box);
        Assert.True(mx < MainMenuScreen.MultiX);
    }

    [Fact]
    public void RoguetripIsOnTheBaseGamesMenuOnly()
    {
        Assert.True(MainMenuScreen.ShowsRoguetrip(nitro: false));
        Assert.False(MainMenuScreen.ShowsRoguetrip(nitro: true));
        Assert.Equal("ROGUETRIP", RemixScenarios.Roguetrip);
        Assert.DoesNotContain("ROAD TRIP", MainMenuScreen.RoguetripBanner);
    }

    /// <summary>The main menu answers the keyboard as the pad: the arrows move the focus, Enter clicks it.</summary>
    [Fact]
    public void TheMainMenusKeysAreThePadsSteps()
    {
        Assert.Equal(new PadRepeater.Step(NavDir.Right, PadRepeater.Btn.None), MenuKeys.Step([Key.Right]));
        Assert.Equal(new PadRepeater.Step(NavDir.Down, PadRepeater.Btn.None), MenuKeys.Step([Key.Down]));
        Assert.Equal(new PadRepeater.Step(NavDir.None, PadRepeater.Btn.Accept), MenuKeys.Step([Key.Enter]));
        Assert.Equal(new PadRepeater.Step(NavDir.None, PadRepeater.Btn.Accept), MenuKeys.Step([Key.KpEnter]));
        Assert.False(MenuKeys.Step([Key.A]).Any);
    }

    /// <summary>Esc on the choice: back to the car pick for a run just begun there; a run under way stays and the menu comes back.</summary>
    [Fact]
    public void EscOnTheChoiceGoesBackOneStep()
    {
        var fresh = RemixRun.Start(4242, "vppirna1.vcf", "Unnamed", "Piranha", 0);
        Assert.True(RoguetripScreen.BackToCarPick(fromCarPick: true, fresh));
        Assert.False(RoguetripScreen.BackToCarPick(fromCarPick: false, fresh));
        var going = RemixRun.Parse(RoundFiveRun);
        Assert.False(RoguetripScreen.BackToCarPick(fromCarPick: true, going));
    }

    /// <summary>A run file as round five wrote it (the mode called ROAD TRIP), a job under way when the game closed.</summary>
    private const string RoundFiveRun = """
        # a road trip in progress (docs/remix-missions.md "Road trip"): the run's number, the jobs played, the car's damage
        number: 4242
        builder: 5
        phase: playing
        busts: 0
        map: t15
        won: 1
        kills: 3
        bonus: 0
        car: vppirna1.vcf
        driver: Unnamed
        model: Piranha
        model_index: 0
        legs:
        - map: t15
          name: the mesa
          template: ambush
          seed: 2776940
          challenge: 0.8
          won: true
          kills: 3
          outcome: won
          why: ''
        log:
        - 'remix: RUN 004242 JOB 1/4 #2776940 builder v5 map t15 (the mesa) template ambush challenge 0.8 car vppirna1.vcf commit c74b1aea (i76 remix roll --run 4242 --job 1 --picks 0)'
        current:
          map: t16
          name: Fort Davis
          template: escort
          seed: 462184530
          challenge: 1
        picks:
        - 0
        - 1
        """;

    /// <summary>
    /// The rename keeps the run's file: a run saved by round five (run.yaml in the generated missions' folder) loads, comes back to
    /// its choice, and is written back under the same name; the jobs it offers are titled with the new name.
    /// </summary>
    [Fact]
    public void ARunSavedAsARoadTripResumesAsARoguetrip()
    {
        Assert.Equal("run.yaml", RemixRun.FileName);
        Assert.Equal("xrun", RemixRun.Stem);
        string dir = Path.Combine(Path.GetTempPath(), "i26-roguetrip-" + System.Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "run.yaml"), RoundFiveRun);
            var run = RemixRun.Load(dir);
            Assert.NotNull(run);
            Assert.Equal(4242, run!.Number);
            Assert.Single(run.Legs);
            Assert.Equal("playing", run.Phase);
            run.Resume();
            Assert.Equal("choose", run.Phase);
            run.Save(dir);
            Assert.Equal(["run.yaml"], Directory.GetFiles(dir).Select(Path.GetFileName));
            var again = RemixRun.Load(dir)!;
            Assert.Equal(4242, again.Number);
            Assert.Equal("t15", again.Map);
            Assert.Equal(run.Log, again.Log);
            Assert.StartsWith("# a roguetrip in progress", File.ReadAllText(Path.Combine(dir, "run.yaml")));
            var offer = again.Offers(RemixNodes.Builtin())[0];
            var spec = again.Spec(offer);
            Assert.Equal("ROGUETRIP 2/4", spec.Title);
            Assert.StartsWith("Roguetrip 4242, job 2:", spec.Text.Objectives[0]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
