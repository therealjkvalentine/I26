using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// [port] headlamp_throw (HeadlampThrow): how far down the road the modern look's headlamps reach. 75 is the beam before the key
/// (110 m range, 0.6 fall-off, energy 9); 100, the default, reaches about a third further by a longer range and a flatter
/// fall-off, its energy set so the near road keeps its light; LampStrength.HeadlampRange is the one range every car's lamps
/// and anything that follows their reach read.
/// </summary>
[Collection("OriginalBehavior")]   // OriginalBehavior.Apply sets a global: not alongside the other settings tests
public class HeadlampThrowTests
{
    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteHeadlampThrow((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheKeyRoundTripsThroughShellCfgAndOnlyWhenNotTheDefault()
    {
        Assert.Equal(100, HeadlampThrow.Default);
        Assert.Equal(100, new ShellSettings().HeadlampThrow);
        Assert.Empty(Cfg(new ShellSettings()));
        var text = Cfg(new ShellSettings { HeadlampThrow = HeadlampThrow.TodayPercent });
        Assert.Equal(new Dictionary<string, string> { ["headlamp_throw"] = "75" }, text);
        var back = new ShellSettings();
        back.ReadHeadlampThrow(k => text.GetValueOrDefault(k));
        Assert.Equal(75, back.HeadlampThrow);
        // absent or not a number: the default; out of range: the end
        Assert.Equal(100, HeadlampThrow.Parse(null));
        Assert.Equal(100, HeadlampThrow.Parse("far"));
        Assert.Equal(HeadlampThrow.Max, HeadlampThrow.Parse("999"));
        Assert.Equal(HeadlampThrow.Min, HeadlampThrow.Parse("0"));
        int was = HeadlampThrow.Percent;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { HeadlampThrow = 75 });
            Assert.Equal(75, HeadlampThrow.Percent);
            OriginalBehavior.Apply(new ShellSettings());
            Assert.Equal(100, HeadlampThrow.Percent);
        }
        finally { HeadlampThrow.Percent = was; }
        Assert.Contains(I76.Game.Ui.DevSettings.All, e => e.Key == "headlamp_throw" && e.When == I76.Game.Ui.DevWhen.Live);
        Assert.Contains(HeadlampThrow.TodayPercent, I76.Game.Ui.DevSettings.HeadlampThrowSteps);
        Assert.Contains(HeadlampThrow.Default, I76.Game.Ui.DevSettings.HeadlampThrowSteps);
    }

    [Fact]
    public void TodaysPercentGivesTheBeamBeforeTheKey()
    {
        Assert.Equal(110f, HeadlampThrow.Range(HeadlampThrow.TodayPercent), 4);
        Assert.Equal(0.6f, HeadlampThrow.Attenuation(HeadlampThrow.TodayPercent), 5);
        Assert.Equal(1f, HeadlampThrow.EnergyScale(HeadlampThrow.TodayPercent), 5);
        Assert.Equal(110f, LampStrength.HeadlampRange(75), 4);
    }

    [Fact]
    public void TheRangeFollowsTheThrowAndIsTheOneAccessor()
    {
        // in proportion: the default's range a third over today's
        Assert.Equal(110f * 100 / 75, LampStrength.HeadlampRange(100), 3);
        Assert.Equal(110f * 150 / 75, LampStrength.HeadlampRange(150), 3);
        // clamped as the file's word is
        Assert.Equal(LampStrength.HeadlampRange(HeadlampThrow.Max), LampStrength.HeadlampRange(999), 4);
        // the setting in force when none is named
        int was = HeadlampThrow.Percent;
        try
        {
            HeadlampThrow.Percent = 90;
            if (HeadlampThrow.Hook is null) Assert.Equal(HeadlampThrow.Range(90), LampStrength.HeadlampRange(), 4);
        }
        finally { HeadlampThrow.Percent = was; }
        // a longer throw falls off flatter, never brighter at the near reference
        for (int p = HeadlampThrow.Min; p < HeadlampThrow.Max; p += 5)
        {
            Assert.True(HeadlampThrow.Attenuation(p + 5) <= HeadlampThrow.Attenuation(p));
            Assert.True(HeadlampThrow.Range(p + 5) > HeadlampThrow.Range(p));
        }
    }

    /// <summary>The distance down a flat road where one lamp's light falls to <paramref name="share"/> of its light 8 m ahead.</summary>
    private static float Reach(int percent, float share)
    {
        float at8 = HeadlampThrow.RoadLight(HeadlampThrow.TodayPercent, 8f) * share, x = 8f;
        while (HeadlampThrow.RoadLight(percent, x) > at8 && x < 400f) x += 0.1f;
        return x;
    }

    [Theory]
    [InlineData(90, 1.20f)]
    [InlineData(100, 1.35f)]
    [InlineData(110, 1.50f)]
    public void TheReachDownTheRoadGrowsWithTheThrowWhateverTheThreshold(int percent, float factor)
    {
        // the road's lit length (to a twentieth, a fiftieth, a hundredth or a two-hundredth of the light 8 m ahead) grows by the
        // throw's share over today's, to a few percent: a longer, flatter fall-off, not a brighter beam
        foreach (float share in new[] { 0.05f, 0.02f, 0.01f, 0.005f })
            Assert.InRange(Reach(percent, share) / Reach(HeadlampThrow.TodayPercent, share), factor - 0.04f, factor + 0.04f);
    }

    [Fact]
    public void TheNearRoadAndTheHoodDoNotBrighten()
    {
        // the near road (8 m) takes the same light at every throw, the road nearer the car no more than before
        foreach (int p in new[] { 75, 90, 100, 110, 150 })
        {
            Assert.Equal(HeadlampThrow.RoadLight(75, 8f), HeadlampThrow.RoadLight(p, 8f), 0.01f * HeadlampThrow.RoadLight(75, 8f));
            for (float x = 0.5f; x < 8f; x += 0.5f)
                Assert.True(HeadlampThrow.RoadLight(p, x) <= HeadlampThrow.RoadLight(75, x) * 1.001f, $"{p} at {x} m");
        }
        // and the energy is never raised to reach further
        Assert.True(HeadlampThrow.EnergyScale(100) < 1f);
    }

    [Fact]
    public void EveryCarsLampsTakeIt()
    {
        string car = File.ReadAllText(Path.Combine(Repo(), "game", "Scripts", "Sim", "CarNode.cs"));
        // made with the range and fall-off in force, re-applied live on a change, for the player's lamps and every other car's alike
        Assert.Contains("SpotRange = LampStrength.HeadlampRange(),", car);
        Assert.Contains("l.SpotRange = LampStrength.HeadlampRange(throwNow);", car);
        Assert.Contains("HeadlampThrow.EnergyScale(throwNow)", car);
        Assert.DoesNotContain("SpotAttenuation = 0.6f,", car.Substring(car.IndexOf("public void AddHeadlights", StringComparison.Ordinal), 1200));
    }

    private static string Repo()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "game", "Scripts", "Sim", "CarNode.cs"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}
