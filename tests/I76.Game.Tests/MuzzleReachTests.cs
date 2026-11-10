using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The modern look's muzzle light (MuzzleReach): a shot lights the world round the gun at night, the road and ground and the
/// cars about it, by the weapon (a cannon furthest), a trace by day. [port] muzzle_reach in percent: 100 the table, 0 the light
/// before the key (4 m, 0.8, on the firing car alone), and the 1997 look untouched.
/// </summary>
[Collection("OriginalBehavior")]   // OriginalBehavior.Apply sets a global: not alongside the other settings tests
public class MuzzleReachTests
{
    private static string Repo(params string[] path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, .. path]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, .. path])).ReplaceLineEndings("\n");
    }

    private static Dictionary<string, string> Cfg(ShellSettings s)
    {
        var keys = new Dictionary<string, string>();
        s.WriteMuzzleReach((k, v) => keys[k] = v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return keys;
    }

    [Fact]
    public void TheKeyRoundTripsThroughShellCfgAndOnlyWhenNotTheDefault()
    {
        Assert.Equal(100, MuzzleReach.Default);
        Assert.Equal(100, new ShellSettings().MuzzleReach);
        Assert.Empty(Cfg(new ShellSettings()));
        foreach (int p in new[] { 0, 50, 150, 200 })
        {
            var text = Cfg(new ShellSettings { MuzzleReach = p });
            Assert.Equal(new Dictionary<string, string> { ["muzzle_reach"] = p.ToString(System.Globalization.CultureInfo.InvariantCulture) }, text);
            var back = new ShellSettings();
            back.ReadMuzzleReach(k => text.GetValueOrDefault(k));
            Assert.Equal(p, back.MuzzleReach);
        }
        // absent or not a number: the default; out of range: the end
        Assert.Equal(100, MuzzleReach.Parse(null));
        Assert.Equal(100, MuzzleReach.Parse("far"));
        Assert.Equal(200, MuzzleReach.Parse("999"));
        Assert.Equal(0, MuzzleReach.Parse("-3"));
        int was = MuzzleReach.Percent;
        try
        {
            OriginalBehavior.Apply(new ShellSettings { MuzzleReach = 0 });
            Assert.Equal(0, MuzzleReach.Percent);
            OriginalBehavior.Apply(new ShellSettings());
            Assert.Equal(100, MuzzleReach.Percent);
        }
        finally { MuzzleReach.Percent = was; }
        Assert.Contains(I76.Game.Ui.DevSettings.All, e => e.Key == "muzzle_reach" && e.When == I76.Game.Ui.DevWhen.Live);
    }

    /// <summary>Every stock gun's ordnance kind (GDFC +0x5a, the install's .gdf files) to its row; the droppers and flamers have no light.</summary>
    [Theory]
    [InlineData(0x12u, MuzzleReach.Gun.Handgun)]       // gh45
    [InlineData(1u, MuzzleReach.Gun.MachineGun)]       // gmlight, gmmedium, gmheavy and their turrets
    [InlineData(6u, MuzzleReach.Gun.Cannon)]           // gclight, gcmedium, gcheavy, gchades, the tank guns and the turrets
    [InlineData(2u, MuzzleReach.Gun.Rocket)]           // gdumb, tdumb
    [InlineData(3u, MuzzleReach.Gun.Rocket)]           // gsheat
    [InlineData(8u, MuzzleReach.Gun.Rocket)]           // gsradar, gscherub
    [InlineData(4u, MuzzleReach.Gun.Mortar)]           // ggrenade, gezkill, tthowitz
    [InlineData(5u, MuzzleReach.Gun.Mortar)]           // gcluster
    [InlineData(7u, MuzzleReach.Gun.Mortar)]           // gwhiteph
    [InlineData(0x16u, MuzzleReach.Gun.Mortar)]        // the Nitro chemical mortar
    [InlineData(0x15u, MuzzleReach.Gun.Other)]         // the Ball O'Flame: its own fire light
    public void EachGunHasItsRow(uint kind, MuzzleReach.Gun gun) => Assert.Equal(gun, MuzzleReach.Of(kind));

    [Fact]
    public void TheTableReachesFurtherByTheGunAndWellPastTheLightBeforeTheKey()
    {
        var guns = new[] { MuzzleReach.Gun.Handgun, MuzzleReach.Gun.MachineGun, MuzzleReach.Gun.Cannon, MuzzleReach.Gun.Rocket, MuzzleReach.Gun.Mortar };
        var b = MuzzleReach.Before;
        Assert.Equal(new MuzzleReach.Flash(4f, 0.8f, 1f, 1.2f, 0.05f), b);
        foreach (var g in guns)
        {
            var n = MuzzleReach.For(g, 0f, 100);
            Assert.Equal(MuzzleReach.Night(g), n);
            // falling off slower than the inverse distance, sitting ahead of the muzzle, a flash's few frames
            Assert.InRange(n.Decay, 0.5f, 1f);
            Assert.InRange(n.Ahead, 0.5f, 4f);
            Assert.InRange(n.Life, 0.04f, 0.12f);
            if (g != MuzzleReach.Gun.Handgun) Assert.True(n.Range >= 3 * b.Range, $"{g}: {n.Range} m");
        }
        float R(MuzzleReach.Gun g) => MuzzleReach.Night(g).Range;
        float E(MuzzleReach.Gun g) => MuzzleReach.Night(g).Energy;
        Assert.True(R(MuzzleReach.Gun.Cannon) > R(MuzzleReach.Gun.Rocket) && R(MuzzleReach.Gun.Rocket) > R(MuzzleReach.Gun.MachineGun));
        Assert.True(R(MuzzleReach.Gun.MachineGun) > R(MuzzleReach.Gun.Handgun) && R(MuzzleReach.Gun.Mortar) > R(MuzzleReach.Gun.Handgun));
        Assert.True(E(MuzzleReach.Gun.Cannon) > E(MuzzleReach.Gun.MachineGun) && E(MuzzleReach.Gun.MachineGun) > E(MuzzleReach.Gun.Handgun));
        Assert.Equal(b, MuzzleReach.Night(MuzzleReach.Gun.Other));
    }

    [Fact]
    public void ZeroIsTheLightBeforeTheKeyDayAndNight()
    {
        foreach (var g in Enum.GetValues<MuzzleReach.Gun>())
            foreach (float day in new[] { 0f, 0.625f, 1f })
                Assert.Equal(MuzzleReach.Before, MuzzleReach.For(g, day, 0));
    }

    [Fact]
    public void FullAtNightATraceInTheSunAndTheStepsBetween()
    {
        var g = MuzzleReach.Gun.Cannon;
        var n = MuzzleReach.Night(g);
        var b = MuzzleReach.Before;
        // full sun: the table's reach with a trace of its energy (less than the light before the key gave by day)
        var sun = MuzzleReach.For(g, 1f, 100);
        Assert.Equal(n.Range, sun.Range);
        Assert.Equal(n.Energy * MuzzleReach.DayFloor, sun.Energy, 5);
        Assert.True(sun.Energy < b.Energy);
        // dusk: between
        Assert.InRange(MuzzleReach.For(g, 0.625f, 100).Energy, sun.Energy, n.Energy);
        // half: halfway along each figure; 200: as far again
        var half = MuzzleReach.For(g, 0f, 50);
        Assert.Equal((b.Range + n.Range) / 2, half.Range, 4);
        Assert.Equal((b.Energy + n.Energy) / 2, half.Energy, 4);
        Assert.Equal((b.Decay + n.Decay) / 2, half.Decay, 4);
        var twice = MuzzleReach.For(g, 0f, 200);
        Assert.Equal(2 * n.Range - b.Range, twice.Range, 4);
        Assert.Equal(twice, MuzzleReach.For(g, 0f, 999));
        // the reach grows with the setting
        float last = 0;
        foreach (int p in new[] { 0, 25, 50, 75, 100, 125, 150, 200 }) { float r = MuzzleReach.For(g, 0f, p).Range; Assert.True(r > last); last = r; }
    }

    [Fact]
    public void TheCombatViewLightsEachShotFromTheTableInTheModernLookOnly()
    {
        var cv = Repo("game", "Scripts", "Sim", "CombatView.cs");
        // one call, behind the modern look's test, and the light it sets is the table's
        Assert.Matches(@"if \(IsModern && !IsDropped\(s\.Weapon\.Def\.Kind\) && s\.Weapon\.Def\.Class != 5 && [^\n]*\)\n\s*MuzzleLight\(s\);", cv);
        Assert.Contains("MuzzleReach.Current(MuzzleReach.Of(s.Weapon.Def.Kind))", cv);
        foreach (var f in new[] { "l.OmniRange = f.Range;", "l.OmniAttenuation = f.Decay;", "ShadowEnabled = false" }) Assert.Contains(f, cv);
        // hidden at the end of its life, and at once in the 1997 look
        Assert.Contains("(_t >= until || !IsModern)) light.Visible = false;", cv);
        Assert.Contains("MuzzleReach.Percent = s.MuzzleReach;", Repo("game", "Scripts", "Sim", "OriginalBehavior.cs"));
    }
}
