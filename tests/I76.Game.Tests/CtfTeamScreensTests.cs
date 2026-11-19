using I76.Game.Shell;
using I76.Sim.Missions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Capture the flag's team screens. The Nitro Pack's front end asks a CTF game's team (hosting with BROADCAST and joining alike) with
/// a popup of the game's own colours, one of three by the team count (2, 3 or 4 rows: RED DAWN, GANG GREEN, PURPLE REIGN, BLACK
/// PLAGUE), and asks team play's eight teams only for a team combat game; STANDINGS heads a CTF game's teams with the same colours.
/// Before, every team game asked with the eight combat teams, and a CTF game's standings named its teams Grey Hounds and Puce Panthers.
/// </summary>
public class CtfTeamScreensTests
{
    [Theory]
    [InlineData(2, 46)]
    [InlineData(3, 45)]
    [InlineData(4, 44)]
    public void ACaptureTheFlagGameAsksWithItsOwnPopup(int teams, int sprite)
    {
        var l = TeamPopup.LayoutOf(teams);
        Assert.Equal(sprite, l.NativeSprite);
        Assert.Equal(teams, l.Names.Count);
        // the 231x208 popup centred on the 640x480 screen as the front end places it, DONE and CANCEL in its lower rows
        Assert.Equal((205, 136, 231, 208), (l.X, l.Y, l.W, l.H));
        Assert.Equal((58, 129, 180, 196), l.Done);
        Assert.Equal((137, 208, 180, 196), l.Cancel);
        Assert.Equal(36, l.MarkX);
    }

    [Fact]
    public void TeamCombatKeepsTheEightTeamPopup()
    {
        // the base game asks a CTF count of 0 (MeleeScreen.ChooseTeam): no game of its own has the CTF popup
        foreach (var l in new[] { TeamPopup.LayoutOf(0) })
        {
            Assert.Null(l.NativeSprite);
            Assert.Equal(8, l.Names.Count);
            Assert.Equal((191, 83, 259, 314), (l.X, l.Y, l.W, l.H));
            Assert.Equal((90, 162, 286, 303), l.Done);
            Assert.Equal(228, l.X + l.MarkX);
        }
    }

    [Fact]
    public void CaptureTheFlagStandingsNameTheGamesOwnTeams()
    {
        Assert.Equal(["Red Dawn", "Gang Green", "Purple Reign", "Black Plague"], Enumerable.Range(1, 4).Select(t => StandingsScreen.TeamHeader(MeleeKind.Ctf, t)));
        Assert.Equal("", StandingsScreen.TeamHeader(MeleeKind.Ctf, 5));
        Assert.Equal(["Grey Hounds", "Puce Panthers", "Men In Black"], new[] { 1, 2, 8 }.Select(t => StandingsScreen.TeamHeader(MeleeKind.Combat, t)));
        Assert.Equal("", StandingsScreen.TeamHeader(MeleeKind.Combat, 0));
    }
}
