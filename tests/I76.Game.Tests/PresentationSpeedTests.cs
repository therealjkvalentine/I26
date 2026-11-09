using I76.Game.Shell;
using I76.Game.Sim;
using I76.Game.Ui;
using I76.Sim.Views;
using Xunit;

namespace I76.Game.Tests;

/// <summary>[port] sky_speed and death_orbit_speed: drawn-only percentages, 50 by default; 100 is the rate before the keys.</summary>
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
        Assert.Equal(50, e.DeathOrbitSpeed);
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
    public void TheDeveloperPanelHasBothRowsAtFifty()
    {
        foreach (var key in new[] { "sky_speed", "death_orbit_speed" })
        {
            var e = Assert.Single(DevSettings.All, d => d.Key == key);
            var s = new ShellSettings();
            Assert.Equal("50", e.Values[e.Get(s)]);
            e.Set(s, 4);
            Assert.Equal("100*", e.Values[e.Get(s)]);
        }
    }
}
