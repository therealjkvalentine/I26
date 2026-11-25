using I76.Game.Boot;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The Nitro Pack's campaign under the base menu's TRIP (Boot/NitroVisit.cs): when the window goes back to the base game, what a
/// won mission marks, which install is offered, and where the new entries sit on the menu. The hand-over itself (statics, installs,
/// music) is the switch guard's (Boot/SwitchProbe.cs, I76_SWITCH_TEST).
/// </summary>
public class NitroVisitTests
{
    [Theory]
    [InlineData(MissionResult.Won, true)]
    [InlineData(MissionResult.Failed, true)]
    [InlineData(MissionResult.Quit, true)]
    [InlineData(MissionResult.Reconfigure, false)]   // the escape menu's Reconfigure: back to the scenario form, still on Nitro
    [InlineData(MissionResult.FirstEntry, false)]
    public void AMissionsEndHandsBack(MissionResult result, bool back) =>
        Assert.Equal(back, NitroVisit.HandBackAfter(result, fromLaunch: true, loadError: false));

    [Fact]
    public void NoLaunchOrALoadFailureStaysOnNitro()
    {
        // the visit's own first entry (no launch yet): Nitro's character choice
        Assert.False(NitroVisit.HandBackAfter(MissionResult.FirstEntry, fromLaunch: false, loadError: false));
        Assert.False(NitroVisit.HandBackAfter(MissionResult.Quit, fromLaunch: false, loadError: false));
        // a mission that could not be built: its form says why
        Assert.False(NitroVisit.HandBackAfter(MissionResult.Quit, fromLaunch: true, loadError: true));
    }

    [Fact]
    public void OnlyAWonCampaignMissionIsMarkedDone()
    {
        Assert.True(NitroVisit.MarksDone(MissionResult.Won, GameKind.Scenario));
        Assert.False(NitroVisit.MarksDone(MissionResult.Quit, GameKind.Scenario));
        Assert.False(NitroVisit.MarksDone(MissionResult.Failed, GameKind.Scenario));
        Assert.False(NitroVisit.MarksDone(MissionResult.Won, GameKind.Trip));      // TRAINING (b01) is launched as a trip
    }

    [Fact]
    public void TheOfferedInstallIsAnotherNitroInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), "i26-nitro-visit-" + Guid.NewGuid().ToString("N"));
        var base76 = Directory.CreateDirectory(Path.Combine(root, "i76")).FullName;
        var nitro = Directory.CreateDirectory(Path.Combine(root, "nitro")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
        try
        {
            bool IsNitro(string d) => Path.GetFullPath(d).TrimEnd('/', '\\') == nitro;
            Assert.Equal(nitro, NitroVisit.PickNitro([null, "", base76, other, nitro], base76, IsNitro));
            // the running install itself is never offered, however it is spelled
            Assert.Null(NitroVisit.PickNitro([nitro + Path.DirectorySeparatorChar], nitro, IsNitro));
            // only the base game: no entry
            Assert.Null(NitroVisit.PickNitro([base76, other], base76, IsNitro));
            // a folder that cannot be read is passed over
            Assert.Equal(nitro, NitroVisit.PickNitro([other, nitro], base76, d => d == other ? throw new UnauthorizedAccessException() : IsNitro(d)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(70, 50, 100)]    // about the sprites' widths: the row passes the frame's edge, all three move left
    [InlineData(70, 50, 30)]     // narrow enough: SCENARIO stays at its own x
    [InlineData(60, 90, 120)]
    public void NitroSitsBetweenScenarioAndInstantMelee(int scenario, int nitroBox, int instant)
    {
        var (sx, nx, ix) = MainMenuScreen.AutoRowPlace(scenario, nitroBox, instant);
        int gap = System.Math.Max(4, MainMenuScreen.InstantX - (MainMenuScreen.ScenarioX + scenario));
        // in order, at the row's own gap between the lettering (NITRO's box has a 4 margin each side), none overlapping
        Assert.Equal(sx + scenario + gap, nx + 4);
        Assert.Equal(nx + nitroBox - 4 + gap, ix);
        Assert.True(sx + scenario <= nx + 4 && nx + nitroBox - 4 <= ix);
        // inside the frame, and SCENARIO never right of its original place
        Assert.True(ix + instant <= MainMenuScreen.RowRightEdge, $"INSTANT MELEE ends at {ix + instant}");
        Assert.True(sx <= MainMenuScreen.ScenarioX && sx > 0);
        if (scenario + gap + nitroBox - 8 + gap + instant <= MainMenuScreen.RowRightEdge - MainMenuScreen.ScenarioX) Assert.Equal(MainMenuScreen.ScenarioX, sx);
        // the row stays above the MAP EDITOR entry and the banners
        var (_, ey, _, _) = MainMenuScreen.EditorEntry;
        Assert.True(MainMenuScreen.AutoRowY + 24 <= ey && MainMenuScreen.AutoRowY + 24 <= MainMenuScreen.BannerTop);
    }
}
