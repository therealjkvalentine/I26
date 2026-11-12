using I76.Game.Shell;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// user://build-rules.cfg (<see cref="BuildRules"/>): written once with each key's facts in comments above it, never
/// rewritten, a missing key's block appended; 1997 picks the 1997 CD build's rule, anything else the 1998 build's.
/// </summary>
public class BuildRulesTests
{
    [Fact]
    public void ANewFileHasEveryKeyAtThe1998ValueUnderItsComment()
    {
        var text = BuildRules.Complete(null);
        Assert.Equal(BuildRules.Default, BuildRules.Parse(text));
        var lines = text.Replace("\r", "").Split('\n');
        foreach (var (key, _) in BuildRules.Keys)
        {
            int at = Array.IndexOf(lines, key + "=1998");
            Assert.True(at > 0, key);
            // the comment block directly above the key names both builds' values
            var block = string.Join("\n", lines[..at].Reverse().TakeWhile(l => l.StartsWith(';')));
            Assert.Contains("1998 (default)", block);
            Assert.Contains("1997:", block);
        }
        Assert.DoesNotContain("_NUMBERS", text);
    }

    [Fact]
    public void APlayersFileIsKeptAndOnlyAMissingKeyAppended()
    {
        const string mine = "; my notes\n[rules]\nturret_slew = 1997\n";
        var full = BuildRules.Complete(mine);
        Assert.StartsWith(mine.TrimEnd(), full);
        Assert.Contains("ai_tactics=1998", full);
        // turret_slew is Play Options' Turrets now (shell.cfg): the line stays in the player's file, not a rule here (ShellSettings.Read takes it once)
        Assert.Equal(new BuildRules(Tactics1997: false), BuildRules.Parse(full));
        Assert.DoesNotContain("turret_slew=1998", full);
        Assert.Equal(full, BuildRules.Complete(full));
    }

    [Theory]
    [InlineData("ai_tactics=1997", true)]
    [InlineData("ai_tactics=\"1997\"\nturret_slew=1997", true)]
    [InlineData("turret_slew=1997\nai_tactics=gold", false)]
    [InlineData("; ai_tactics=1997", false)]
    public void OnlyThe1997ValueTurnsARuleOn(string text, bool tactics) =>
        Assert.Equal(new BuildRules(tactics), BuildRules.Parse(text));

    [Fact]
    public void ANetworkGameKeepsThe1998Rules()
    {
        var world = new I76.Sim.SimWorld(null!, new I76.Sim.Rng.CrtRand(1));
        // the turrets' slew is no longer the file's: Apply leaves it to the settings (ShellSettings.ApplyTo) and the host
        var c = new Combat(world, _ => null) { NetSession = true };
        new BuildRules(true).Apply(c, null);
        Assert.False(c.TurretSlew1997);
        var offline = new Combat(world, _ => null);
        new BuildRules(true).Apply(offline, null);
        Assert.False(offline.TurretSlew1997);
    }
}
