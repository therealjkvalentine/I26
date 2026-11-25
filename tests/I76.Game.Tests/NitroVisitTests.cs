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

    [Fact]
    public void TheNewEntriesSitUnderTheTripRowClearOfTheRest()
    {
        // the trip row's sprites are at y 340, about 22 high; the MELEE side's sub-rows (AUTO MELEE's, MULTI MELEE's) start at x 343
        Assert.True(MainMenuScreen.PortRowY >= 340 + 24, $"row {MainMenuScreen.PortRowY}");
        Assert.True(MainMenuScreen.PortRowX >= 74 && MainMenuScreen.PortRowX < 343);
        // above the MAP EDITOR entry and the banners
        var (_, ey, _, _) = MainMenuScreen.EditorEntry;
        Assert.True(MainMenuScreen.PortRowY + 24 <= ey, $"row bottom against the editor entry at {ey}");
        Assert.True(MainMenuScreen.PortRowY + 24 <= MainMenuScreen.BannerTop);
        Assert.Equal(3, new[] { MainMenuScreen.ScenariosEntry, MainMenuScreen.NitroEntry, MainMenuScreen.BackEntry }.Distinct().Count());
    }
}
