using I76.Game.Shell;
using I76.Sim.Missions;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The co-op host form's defaults and words (round 4): Rolling Start on with no row, Extra Enemies at the cap and said as "+1/player",
/// and the help line (HostFormHelp) for every setting of the options box, melee and co-op.
/// </summary>
public class CoopHostFormTests
{
    [Fact]
    public void RollingStartIsOnByDefaultAndHasNoRow()
    {
        Assert.True(new ShellSettings().CoopRollingStart);
        Assert.DoesNotContain(HostFormLayout.Fields(new HostFormLayout.State { Coop = true }), f => f.Label.Contains("ROLLING"));
        Assert.Null(HostFormHelp.Text(42));
    }

    [Fact]
    public void ExtraEnemiesDefaultsToTheCapOnePerPlayer()
    {
        Assert.Equal(3, CoopSetup.MaxExtraEnemies);
        Assert.Equal(CoopSetup.MaxExtraEnemies, new ShellSettings().CoopScale);
        Assert.Equal("+1/player", HostFormLayout.CoopScaleWord(new ShellSettings().CoopScale));
    }

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "Up to +1")]
    [InlineData(2, "Up to +2")]
    [InlineData(3, "+1/player")]
    public void TheCapIsSaidInAPlayersWords(int cap, string word) => Assert.Equal(word, HostFormLayout.CoopScaleWord(cap));

    /// <summary>The stepped lines' parts (BOTS' label, ⊟ ⊞, the skill, A.I. CARS; MAX SCORE's box and ⊟ ⊞) and the game type button have help too.</summary>
    private static readonly int[] MeleeParts = [HostFormHelp.BotsLabel, 29, 30, 31, 34, 18, 20, 21, 32];

    [Fact]
    public void EverySettingOfTheBoxHasAHelpLine()
    {
        foreach (bool coop in new[] { false, true })
            foreach (var f in HostFormLayout.Fields(new HostFormLayout.State { Coop = coop }))
                Assert.False(string.IsNullOrWhiteSpace(HostFormHelp.Text(f.Id)), $"{f.Label} ({f.Id}) has no help");
        foreach (int id in MeleeParts) Assert.False(string.IsNullOrWhiteSpace(HostFormHelp.Text(id)), $"{id} has no help");
        // the art's own controls say nothing (the original's form had no help text)
        foreach (int id in new[] { 3, 4, 5, 6, 8, 9, 11, 12, 13, 14, 15, 22 }) Assert.Null(HostFormHelp.Text(id));
    }

    [Fact]
    public void TheTooltipNamesTheSettingInAPlayersWords()
    {
        foreach (bool coop in new[] { false, true })
            foreach (var f in HostFormLayout.Fields(new HostFormLayout.State { Coop = coop }))
            {
                var t = HostFormHelp.Text(f.Id)!;
                // the tooltip starts with the setting's name, in a player's words, and stays a short sentence or two
                Assert.StartsWith(f.Label.Split(' ')[0], t.ToUpperInvariant());
                Assert.InRange(t.Length, 10, 200);
                Assert.All(t, ch => Assert.InRange(ch, ' ', '~'));
                Assert.DoesNotContain("=", t);   // no config key shown to the player
            }
        Assert.Contains("per extra player", HostFormHelp.Text(40));
        Assert.Contains("x1 the original's", HostFormHelp.Text(41));
    }
}
