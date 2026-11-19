using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Every row of the host forms is kept per game type in melee.cfg (HostRows; the play fork's, after #204 kept NO. DRIVERS): melee, race,
/// capture the flag and co-op each open on the rows they were last hosted with, after a game (the form made again) and after a restart
/// (the file read again). A fresh file keeps nothing (the form's defaults); a file from before has no type sections and keeps its older keys.
/// </summary>
public class HostRowsKeptTests
{
    private static MeleeSetup Again(MeleeSetup s) => MeleeSetup.Read(s.Write());

    [Fact]
    public void AFreshFileKeepsNoRowForAnyType()
    {
        var s = MeleeSetup.Read(new SettingsFile());
        Assert.Empty(s.Hosts);
        foreach (var t in Enum.GetValues<HostType>()) Assert.True(s.Host(t).IsEmpty);
        // and writes no type section
        Assert.DoesNotContain(new MeleeSetup().Write().Entries, e => e.Section.StartsWith("host_", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileFromBeforeStillLoadsItsKeys()
    {
        // upstream's and the fork's earlier melee.cfg: [melee] only
        var f = new SettingsFile();
        f.SetValue("melee", "host_drivers", 6);
        f.SetValue("melee", "bots", 3);
        f.SetValue("melee", "host_rate", 30);
        var s = MeleeSetup.Read(f);
        Assert.Equal((6, 3, 30), (s.HostDrivers, s.Bots, s.HostRate));
        Assert.Empty(s.Hosts);
    }

    [Fact]
    public void EveryRowGoesRoundTheFile()
    {
        var s = new MeleeSetup();
        s.Host(HostType.Melee).Merge(new HostRows
        {
            Drivers = 12, TeamPlay = true, InviteOnly = true, ScoreOn = true, MaxScore = 45000, Limit = "Kills", LimitValue = 30,
            Bots = 5, BotSkill = 2, BotCars = ["vppirna2.vcf", "", "vcgcour1.vcf"], Rate = 30, Turrets = true, Restrictions = "4:-1,nitro",
        });
        var r = Again(s).Host(HostType.Melee);
        Assert.Equal((12, true, true, true, 45000), (r.Drivers, r.TeamPlay, r.InviteOnly, r.ScoreOn, r.MaxScore));
        Assert.Equal(("Kills", 30, 5, 2, 30, true), (r.Limit, r.LimitValue, r.Bots, r.BotSkill, r.Rate, r.Turrets));
        Assert.Equal(["vppirna2.vcf", "", "vcgcour1.vcf"], r.BotCars);
        Assert.Equal("4:-1,nitro", r.Restrictions);
    }

    [Fact]
    public void EachTypeKeepsItsOwn()
    {
        var s = new MeleeSetup();
        s.Host(HostType.Melee).Merge(new HostRows { Drivers = 16, Limit = "Points", LimitValue = 50000, TeamPlay = false });
        s.Host(HostType.Race).Merge(new HostRows { Drivers = 6, Limit = "Laps", LimitValue = 10, TeamPlay = true });
        s.Host(HostType.Ctf).Merge(new HostRows { Drivers = 8, Limit = "Captures", LimitValue = 5 });
        s.Host(HostType.Coop).Merge(new HostRows { Drivers = 3, InviteOnly = true });
        var t = Again(s);
        Assert.Equal((16, "Points", 50000, false), (t.Host(HostType.Melee).Drivers, t.Host(HostType.Melee).Limit, t.Host(HostType.Melee).LimitValue, t.Host(HostType.Melee).TeamPlay));
        Assert.Equal((6, "Laps", 10, true), (t.Host(HostType.Race).Drivers, t.Host(HostType.Race).Limit, t.Host(HostType.Race).LimitValue, t.Host(HostType.Race).TeamPlay));
        Assert.Equal((8, "Captures", 5), (t.Host(HostType.Ctf).Drivers, t.Host(HostType.Ctf).Limit, t.Host(HostType.Ctf).LimitValue));
        Assert.Equal((3, true), (t.Host(HostType.Coop).Drivers, t.Host(HostType.Coop).InviteOnly));
        Assert.Null(t.Host(HostType.Coop).Limit);
    }

    [Fact]
    public void OneFormsRowsDoNotDropTheOthers()
    {
        // the base game's form keeps MAX SCORE, the Nitro Pack's LIMIT, for the same melee type
        var r = new HostRows { Limit = "Kills", LimitValue = 10 };
        r.Merge(new HostRows { ScoreOn = true, MaxScore = 30000, Drivers = 5 });
        Assert.Equal(("Kills", 10, true, 30000, 5), (r.Limit, r.LimitValue, r.ScoreOn, r.MaxScore, r.Drivers));
    }

    [Fact]
    public void TheOlderKeysStayTheLastGames()
    {
        // a build that reads only [melee] (upstream's) still finds the last game's count, bots and rate
        var s = new MeleeSetup { HostDrivers = 7, Bots = 2, HostRate = 30 };
        s.Host(HostType.Race).Merge(new HostRows { Drivers = 7, Bots = 2, Rate = 30 });
        var f = s.Write();
        Assert.Equal(7, (int)f.GetValue("melee", "host_drivers"));
        Assert.Equal(2, (int)f.GetValue("melee", "bots"));
        Assert.Equal(30, (int)f.GetValue("melee", "host_rate"));
    }

    [Fact]
    public void ValuesOutsideTheFormsRangeAreHeldToIt()
    {
        var f = new SettingsFile();
        string m = HostRows.Section(HostType.Melee);
        f.SetValue(m, "drivers", 500);
        f.SetValue(m, "max_score", 999999);
        f.SetValue(m, "bots", -4);
        f.SetValue(m, "bot_skill", 9);
        f.SetValue(m, "rate", 17);
        f.SetValue(m, "restrictions", "no such weapon");
        var r = MeleeSetup.Read(f).Host(HostType.Melee);
        Assert.Equal((MeleeScreen.MaxDrivers, HostRows.MaxScoreTop, 0, 2), (r.Drivers, r.MaxScore, r.Bots, r.BotSkill));
        Assert.Null(r.Rate);              // a rate the form does not offer: the form's own
        Assert.Equal("", r.Restrictions); // a list this build cannot read: nothing restricted
        Assert.Equal(1000, HostRows.ScoreFrom(0));
        Assert.Equal(21000, HostRows.ScoreFrom(20600));
    }

    [Theory]
    [InlineData("Kills", 30, 1, 3)]
    [InlineData("kills", 30, 1, 3)]
    [InlineData("Kills", 7, 1, 0)]      // a value the limit does not offer: its default
    [InlineData("Laps", 3, 0, 0)]       // a limit the type does not have: the form's default
    [InlineData(null, null, 0, 0)]
    [InlineData("No limit", 0, 3, 0)]
    public void ALimitIsFoundByNameAndValue(string? name, int? value, int limit, int at)
    {
        string[] names = ["Points", "Kills", "Minutes", "No limit"];
        int[][] values = [[20000, 5000, 10000, 50000, 100000], [20, 5, 10, 30, 50], [10, 5, 15, 30], [0]];
        Assert.Equal((limit, at), HostRows.LimitFrom(names, i => values[i], name, value));
    }

    [Theory]
    [InlineData(false, I76.Sim.Missions.MeleeKind.Combat, HostType.Melee)]
    [InlineData(false, I76.Sim.Missions.MeleeKind.Race, HostType.Race)]
    [InlineData(false, I76.Sim.Missions.MeleeKind.Ctf, HostType.Ctf)]
    [InlineData(true, I76.Sim.Missions.MeleeKind.Combat, HostType.Coop)]
    public void ATypeIsTheGamesKind(bool coop, I76.Sim.Missions.MeleeKind kind, HostType t) => Assert.Equal(t, HostRows.Of(coop, kind));

    [Fact]
    public void ThePasswordIsNeverAmongThem() =>
        Assert.DoesNotContain(typeof(HostRows).GetProperties(), p => p.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
}
