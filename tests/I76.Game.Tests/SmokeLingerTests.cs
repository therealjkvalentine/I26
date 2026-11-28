using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// [port] smoke_linger (SmokeLinger): how long the modern look's smoke hangs in the air, in percent of each smoke's own life, 400 by
/// default (150, then 200 with SmokeFade's fade, before the smoke was made to linger and grow slowly), 100 to 300; the pools that hold the puffs grow with it; visual only.
/// </summary>
[Collection("SmokeStatics")]
public class SmokeLingerTests
{
    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteSmokeLinger((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheDefaultIsFourTimesAsLongAndIsNotWritten()
    {
        Assert.Equal(400, SmokeLinger.Default);
        Assert.Equal(400, new ShellSettings().SmokeLinger);
        Assert.Empty(Cfg(new ShellSettings()));
        foreach (int p in new[] { 100, 125, 150, 200, 300, 600 })
        {
            var text = Cfg(new ShellSettings { SmokeLinger = p });
            Assert.Equal(new Dictionary<string, string> { ["smoke_linger"] = p.ToString(System.Globalization.CultureInfo.InvariantCulture) }, text);
            var back = new ShellSettings();
            back.ReadSmokeLinger(k => text.GetValueOrDefault(k));
            Assert.Equal(p, back.SmokeLinger);
        }
    }

    [Theory]
    [InlineData(null, 400)]
    [InlineData("", 400)]
    [InlineData("long", 400)]
    [InlineData("100", 100)]
    [InlineData("150", 150)]
    [InlineData("50", 100)]     // never shorter than the lives as they were
    [InlineData("999", 600)]
    public void TheFilesWordIsClamped(string? word, int percent) => Assert.Equal(percent, SmokeLinger.Parse(word));

    [Fact]
    public void ThePoolsGrowWithTheLinger()
    {
        int was = SmokeLinger.Percent;
        try
        {
            SmokeLinger.Percent = 100;
            Assert.Equal(1f, SmokeLinger.Factor);
            Assert.Equal(3072, SmokeLinger.Pool(3072));
            SmokeLinger.Percent = 150;
            Assert.Equal(1.5f, SmokeLinger.Factor);
            Assert.Equal(4608, SmokeLinger.Pool(3072));
            SmokeLinger.Percent = 600;
            Assert.Equal(SmokeLinger.PoolMax(2400), SmokeLinger.Pool(2400));
            // the pools are made for the longest linger: the share in use never passes what was made
            foreach (int p in SmokeLinger.Steps)
            {
                SmokeLinger.Percent = p;
                Assert.True(SmokeLinger.Pool(3072) <= SmokeLinger.PoolMax(3072));
            }
        }
        finally { SmokeLinger.Percent = was; }
    }

    [Fact]
    public void UnderLongAPuffsOwnLifeKeepsTheEarlierLives()
    {
        int was = SmokeLinger.Percent; bool wasLong = SmokeHaze.Long;
        try
        {
            foreach (int p in SmokeLinger.Steps)
            {
                SmokeLinger.Percent = p;
                SmokeHaze.Long = false;
                Assert.Equal(p / 100f, SmokeLinger.LifeFactor);
                SmokeHaze.Long = true;
                Assert.Equal(MathF.Min(p / 100f, SmokeLinger.LongYoung), SmokeLinger.LifeFactor);
            }
            // the default: Normal's puffs four times their own life, Long's twice (the lives before smoke_linger 400) and then the haze
            SmokeLinger.Percent = SmokeLinger.Default;
            SmokeHaze.Long = false; Assert.Equal(4f, SmokeLinger.LifeFactor);
            SmokeHaze.Long = true; Assert.Equal(2f, SmokeLinger.LifeFactor);
        }
        finally { SmokeLinger.Percent = was; SmokeHaze.Long = wasLong; }
    }

    [Fact]
    public void TheSettingsReachTheSmoke()
    {
        int was = SmokeLinger.Percent;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { SmokeLinger = 150 });
            Assert.Equal(150, SmokeLinger.Percent);
            OriginalBehavior.Apply(new ShellSettings());
            Assert.Equal(400, SmokeLinger.Percent);
        }
        finally { SmokeLinger.Percent = was; }
        var e = Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "smoke_linger");
        Assert.Equal(DevWhenLive, e.When);
        // the original's lives unmarked, the port's longer ones marked
        Assert.Equal(["100", "125*", "150*", "200*", "300*", "400*", "600*"], e.Values);
        Assert.Equal("400*", e.Values[e.Get(new ShellSettings())]);
        Assert.Equal("200*", e.Values[e.Get(new ShellSettings { SmokeLinger = 180 })]);
    }

    private const I76.Game.Ui.DevWhen DevWhenLive = I76.Game.Ui.DevWhen.Live;

    /// <summary>Every modern smoke but the chemical cloud takes the factor; the 1997 look's puffs never do; nothing of it is in the simulation.</summary>
    [Fact]
    public void TheModernSmokesTakeItAndThe1997LookAndTheSimulationDoNot()
    {
        static string Src(params string[] p) => File.ReadAllText(Path.Combine([Repo(), .. p]));
        Assert.Contains("p.Life = Math.Min(p.Life, 4f) * SmokeLinger.LifeFactor;", Src("game", "Scripts", "Sim", "DamageSmoke.cs"));
        Assert.Contains("Life = Life * (0.85f + 0.3f * r3) * linger", Src("game", "Scripts", "Sim", "RocketSmoke.cs"));
        Assert.Contains("(3.5 + GD.Randf()) * SmokeLinger.LifeFactor", Src("game", "Scripts", "Sim", "ModernFx.cs"));
        Assert.Contains("8.0 * linger", Src("game", "Scripts", "Sim", "WreckFire.cs"));
        Assert.DoesNotContain("SmokeLinger", Src("game", "Scripts", "Sim", "ChemClouds.cs"));
        Assert.DoesNotContain("SmokeLinger", Src("game", "Scripts", "Sim", "Contrails.cs"));
        // the 1997 look's damage puffs keep the original's 20 ticks
        Assert.Contains("float life = modern ? (float)ModernLife : Life1997;", Src("game", "Scripts", "Sim", "DamageSmoke.cs"));
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Repo(), "src"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotContain("SmokeLinger", File.ReadAllText(f));
    }

    private static string Repo()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "I76.sln"))) d = d.Parent;
        return d!.FullName;
    }
}
