using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The host form's NO. DRIVERS is kept in melee.cfg (host_drivers, #204): the form opens on the count last hosted with, after a game
/// (the form made again) and after a restart (the file read again), apart from the instant melee's own driver count; 4 until then.
/// </summary>
public class HostDriversKeptTests
{
    private static MeleeSetup Again(MeleeSetup s) => MeleeSetup.Read(s.Write());

    [Fact]
    public void ANewFileOpensTheFormOnFour()
    {
        Assert.Equal(4, new MeleeSetup().HostDrivers);
        Assert.Equal(4, MeleeSetup.Read(new SettingsFile()).HostDrivers);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(MeleeScreen.MaxDrivers)]
    public void TheCountHostedWithIsWhatTheFormShowsNextTime(int drivers)
    {
        var s = Again(new MeleeSetup { HostDrivers = drivers });
        Assert.Equal(drivers, s.HostDrivers);
    }

    [Fact]
    public void TheHostCountAndTheInstantMeleeCountAreApart()
    {
        // the instant melee's opponents (drivers, 2 by default) and the host form's seats are two settings
        var s = Again(new MeleeSetup { Drivers = 3, HostDrivers = 6 });
        Assert.Equal((3, 6), (s.Drivers, s.HostDrivers));
        Assert.Equal(2, Again(new MeleeSetup { HostDrivers = 6 }).Drivers);
        Assert.Equal(6, Again(new MeleeSetup { Drivers = 5, HostDrivers = 6 }).HostDrivers);
    }

    [Fact]
    public void ACountOutsideTheFormsRangeIsHeldToIt()
    {
        var f = new SettingsFile();
        f.SetValue("melee", "host_drivers", 500);
        Assert.Equal(MeleeScreen.MaxDrivers, MeleeSetup.Read(f).HostDrivers);
        f.SetValue("melee", "host_drivers", -3);
        Assert.Equal(1, MeleeSetup.Read(f).HostDrivers);
    }

    [Fact]
    public void EveryOtherKeyStillGoesRoundTheFile()
    {
        var s = new MeleeSetup
        {
            Area = 3, Model = 2, Variant = 4, Drivers = 5, Game = "Pit", Driver = "Taurus", NitroKind = 1, NitroArena = "r01.rac", CoopArena = "t05",
            CoopHost = true, Bots = 2, BotSkill = 2, BotCars = ["a.vcf", "", "b.vcf"], HostRate = 30, HostTurrets = true, NitroDrivers = 12, HostDrivers = 6,
        };
        var t = Again(s);
        Assert.Equal((3, 2, 4, 5, "Pit", "Taurus"), (t.Area, t.Model, t.Variant, t.Drivers, t.Game, t.Driver));
        Assert.Equal((1, "r01.rac", "t05", true), (t.NitroKind, t.NitroArena, t.CoopArena, t.CoopHost));
        Assert.Equal((2, 2, 30, true, 12, 6), (t.Bots, t.BotSkill, t.HostRate, t.HostTurrets, t.NitroDrivers, t.HostDrivers));
        Assert.Equal(["a.vcf", "", "b.vcf"], t.BotCars);
    }
}
