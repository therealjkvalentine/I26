using I76.Game.Shell;
using I76.Game.Sim;
using I76.Game.Ui;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>[port] sky_speed and death_orbit_speed: drawn-only percentages, 50 by default (the orbit 25); 100 is the rate before the keys.</summary>
[Collection("OriginalBehavior")]   // OriginalBehavior.Apply sets a global: not alongside the other settings tests
public class PresentationSpeedTests
{
    /// <summary>shell.cfg's [port] text as the file holds it: key=value lines (the engine's ConfigFile is not loadable in the tests).</summary>
    private static string Cfg(ShellSettings s)
    {
        var lines = new List<string> { "[port]" };
        s.WriteSpeeds((k, v) => lines.Add($"{k}={v}"));
        return string.Join('|', lines);
    }

    private static string? Key(string cfg, string key) =>
        cfg.Split('|').Select(l => l.Split('=')).Where(p => p.Length == 2 && p[0] == key).Select(p => p[1]).FirstOrDefault();

    [Fact]
    public void TheKeysRoundTripThroughShellCfg()
    {
        string text = Cfg(new ShellSettings { SkySpeed = 100, DeathOrbitSpeed = 25 });
        Assert.Equal("100", Key(text, "sky_speed"));
        Assert.Equal("25", Key(text, "death_orbit_speed"));
        var r = new ShellSettings();
        r.ReadSpeeds(k => Key(text, k));
        Assert.Equal(100, r.SkySpeed);
        Assert.Equal(25, r.DeathOrbitSpeed);

        // the default is not written, and an absent key reads as the default
        string plain = Cfg(new ShellSettings());
        Assert.Null(Key(plain, "sky_speed"));
        Assert.Null(Key(plain, "death_orbit_speed"));
        var e = new ShellSettings { SkySpeed = 7, DeathOrbitSpeed = 7 };
        e.ReadSpeeds(k => Key(plain, k));
        Assert.Equal(50, e.SkySpeed);
        Assert.Equal(25, e.DeathOrbitSpeed);
    }

    [Fact]
    public void TheOrbitDefaultsToAQuarterAndTheSkyStaysAtHalf()
    {
        Assert.Equal(25, PresentationSpeed.DefaultOrbit);
        Assert.Equal(50, PresentationSpeed.Default);
        Assert.Equal(25, new ShellSettings().DeathOrbitSpeed);
        Assert.Equal(50, new ShellSettings().SkySpeed);
        OriginalBehavior.Apply(new ShellSettings());
        Assert.Equal(0.25f, OriginalBehavior.OrbitSpeed);
        Assert.Equal(0.5f, OriginalBehavior.SkySpeed);
    }

    [Fact]
    public void AFileThatNamesTheOrbitsValueKeepsItAndAnUnreadableOneGetsTheDefault()
    {
        foreach (int v in new[] { 50, 100, 10, 0 })
        {
            string text = Cfg(new ShellSettings { DeathOrbitSpeed = v });
            var r = new ShellSettings();
            r.ReadSpeeds(k => Key(text, k));
            Assert.Equal(v, r.DeathOrbitSpeed);
        }
        // 50 is no longer the default, so it is written; 25 is not
        Assert.Equal("50", Key(Cfg(new ShellSettings { DeathOrbitSpeed = 50 }), "death_orbit_speed"));
        Assert.Null(Key(Cfg(new ShellSettings { DeathOrbitSpeed = 25 }), "death_orbit_speed"));
        var f = new ShellSettings();
        f.ReadSpeeds(k => k == "death_orbit_speed" ? "fast" : null);
        Assert.Equal(25, f.DeathOrbitSpeed);
    }

    [Fact]
    public void TheOrbitRowOffersTheSlowSteps()
    {
        var e = Assert.Single(DevSettings.All, d => d.Key == "death_orbit_speed");
        Assert.Equal(new[] { 0, 10, 15, 25, 35, 50, 75, 100, 150, 200 }, DevSettings.OrbitSpeedSteps);
        Assert.Equal(DevSettings.OrbitSpeedSteps.Length, e.Values.Length);
        var s = new ShellSettings();
        Assert.Equal("25", e.Values[e.Get(s)]);
        foreach (int i in new[] { 1, 2, 4, 5, 7 }) { e.Set(s, i); Assert.Equal(DevSettings.OrbitSpeedSteps[i], s.DeathOrbitSpeed); }
        s.DeathOrbitSpeed = 12;   // set by hand: the nearest step shows
        Assert.Equal(DevSettings.OrbitSpeedSteps[1], DevSettings.OrbitSpeedSteps[e.Get(s)]);
        Assert.Contains("100 is the original", DevTips.Of("death_orbit_speed"));
    }

    [Theory]
    [InlineData("50", 50)]
    [InlineData("100%", 100)]
    [InlineData("0", 0)]
    [InlineData("999", 200)]
    [InlineData("-5", 0)]
    [InlineData("fast", 50)]
    [InlineData(null, 50)]
    public void ValuesClampToZeroTo200(string? text, int expected) => Assert.Equal(expected, PresentationSpeed.Parse(text));

    [Fact]
    public void TheSkyStepIsTodaysAt100AndHalfAt50()
    {
        float today = SimClock.SkyScrollPerSecond;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { SkySpeed = 100 });
            Assert.Equal(today, SimClock.SkyRate(today));
            Assert.Equal(SimClock.ModernSkyScrollPerSecond, SimClock.SkyRate(SimClock.ModernSkyScrollPerSecond));
            float perTick100 = SimClock.SkyRate(today) * SimClock.Tick;
            OriginalBehavior.Apply(new ShellSettings());
            Assert.Equal(perTick100 / 2, SimClock.SkyRate(today) * SimClock.Tick, 1e-6f);
            Assert.Equal(SimClock.ModernSkyScrollPerSecond / 2, SimClock.SkyRate(SimClock.ModernSkyScrollPerSecond), 1e-6f);
        }
        finally { OriginalBehavior.Apply(new ShellSettings()); }
    }

    [Theory]
    [InlineData(100, 160f)]   // 8 degrees a tick at 20 Hz
    [InlineData(50, 80f)]
    [InlineData(0, 0f)]
    public void TheOrbitTurnsDegreesPerSecond(int percent, float expected)
    {
        var o = new DeathOrbit { Speed = PresentationSpeed.Factor(percent) };
        o.Start(0);
        const float tick = 0.05f;
        for (int i = 0; i < 20; i++) o.Advance(tick, tick);   // one second of ticks
        Assert.Equal(expected, o.Yaw * 180f / MathF.PI, 0.01f);
        // the smooth turn scales the same way
        var m = new DeathOrbit { Speed = PresentationSpeed.Factor(percent) };
        m.Start(0);
        m.AdvanceSmooth(1f);
        Assert.Equal(20f * percent / 100f, m.Yaw * 180f / MathF.PI, 0.1f);
    }

    [Fact]
    public void TheDeveloperPanelsSkyRowIsAtFifty()
    {
        var e = Assert.Single(DevSettings.All, d => d.Key == "sky_speed");
        var s = new ShellSettings();
        Assert.Equal("50", e.Values[e.Get(s)]);
        e.Set(s, 4);
        Assert.Equal("100*", e.Values[e.Get(s)]);
    }
}
