using I76.Game.Shell;
using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// [port] gun_smoke (GunSmoke): the modern look's smoke from firing guns. Light by default; a machine gun's wisps at most a few a second a
/// gun, a cannon's puff a shot; whiter and thinner than damage smoke, short-lived, fading to nothing (SmokeFade) and never handed to Long's
/// haze; nothing for weapons that are not guns. Without the engine; the sources are checked to use the rules.
/// </summary>
[Collection("SmokeStatics")]
public class GunSmokeTests
{
    private static string Src(params string[] p)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine([dir, "game", "Scripts", .. p]))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine([dir!, "game", "Scripts", .. p]));
    }

    [Fact]
    public void LightIsTheDefaultAndTheKeyRoundTrips()
    {
        Assert.Equal(GunSmoke.Level.Light, GunSmoke.Default);
        var s = new ShellSettings();
        Assert.Equal(GunSmoke.Level.Light, s.GunSmoke);
        Assert.False(s.Write(null).HasSectionKey("port", "gun_smoke"));
        foreach (var lv in new[] { GunSmoke.Level.Off, GunSmoke.Level.Heavy })
        {
            var f = new ShellSettings { GunSmoke = lv }.Write(null);
            Assert.Equal(GunSmoke.Words[(int)lv], f.GetValue("port", "gun_smoke").ToString());
            Assert.Equal(lv, ShellSettings.Read(f).GunSmoke);
        }
        Assert.Null(GunSmoke.Parse("thick", null));
        Assert.Contains(I76.Game.Ui.DevSettings.All, e => e.Key == "gun_smoke" && e.When == I76.Game.Ui.DevWhen.Live);
        Assert.False(string.IsNullOrEmpty(I76.Game.Ui.DevTips.Of("gun_smoke")));
    }

    [Fact]
    public void EachGunHasItsAmountAndNothingElseSmokes()
    {
        foreach (var lv in new[] { GunSmoke.Level.Light, GunSmoke.Level.Heavy })
        {
            var mg = GunSmoke.For(1, lv)!.Value; var cannon = GunSmoke.For(6, lv)!.Value; var hand = GunSmoke.For(0x12, lv)!.Value;
            // a machine gun: wisps, a few a second at most whatever its rate; a cannon: a puff a shot (it fires slower than this)
            Assert.InRange(mg.Every, 0.08f, 0.25f);
            Assert.True(cannon.Every <= 0.05f);
            // a cannon's puff larger and denser than a machine gun's wisp, the handgun's least
            Assert.True(cannon.Final > mg.Final && cannon.Dens > mg.Dens && hand.Final <= mg.Final);
            // short-lived, and thinner than a damage puff (white band 0.3 x 1.4 at its thinnest)
            foreach (var p in new[] { mg, cannon, hand })
            {
                Assert.InRange(p.Life, 1f, 2.5f);
                Assert.True(p.Dens < 0.5f && p.Start < p.Final);
            }
            // rockets, mortars, missiles, droppers, flame: none (their trails are their smoke)
            foreach (uint k in new uint[] { 2, 3, 4, 5, 7, 8, 0x14, 0x16, 9, 10, 0 })
                Assert.Null(GunSmoke.For(k, lv));
        }
        Assert.Null(GunSmoke.For(1, GunSmoke.Level.Off));
        // heavy is more of it
        Assert.True(GunSmoke.For(1, GunSmoke.Level.Heavy)!.Value.Dens > GunSmoke.For(1, GunSmoke.Level.Light)!.Value.Dens);
        Assert.True(GunSmoke.For(1, GunSmoke.Level.Heavy)!.Value.Every < GunSmoke.For(1, GunSmoke.Level.Light)!.Value.Every);
    }

    [Fact]
    public void APuffGrowsAndFadesToNothingAndIsLeftBehind()
    {
        var mg = GunSmoke.For(1, GunSmoke.Level.Light)!.Value;
        float end = mg.Final / SmokeFade.Grow, prev = 0f;
        for (int i = 0; i <= 100; i++)
        {
            float lf = i / 100f, size = SmokeFade.Size(mg.Start, end, lf);
            Assert.True(size >= prev - 1e-5f);
            prev = size;
        }
        Assert.Equal(mg.Final, prev, 3);
        Assert.Equal(0f, mg.Dens * SmokeFade.Out(1f));
        // half the car's speed and a push down the barrel; the air takes the push off (most of it within half a second)
        var v = GunSmoke.Start(new System.Numerics.Vector3(0, 0, 30), new System.Numerics.Vector3(0, 0, 2));
        Assert.Equal(15f + GunSmoke.Push, v.Z, 3);
        Assert.True(MathF.Exp(-GunSmoke.Drag * 0.5f) < 0.3f);
        // the sources: a gun's puff fades by Out under either Smoke Linger and ends with its life under Long too
        string dmg = Src("Sim", "DamageSmoke.cs"), cv = Src("Sim", "CombatView.cs");
        Assert.Contains("float lf = p.Age / p.Span, size = SmokeFade.Size(p.Start, p.End, lf);", dmg);
        Assert.Contains("return SmokeHaze.On && !p.Brief ? (lf, size, p.Dens) : (lf, size, p.Dens * SmokeFade.Out(lf));", dmg);
        Assert.Contains("p.Span = p.Life;   // its own life is its curve's", dmg);
        Assert.Contains("if (!hazeOn || p.Brief) { p.Live = false; continue; }", dmg);
        // and so never reaches the hand-over to the haze, which follows it
        Assert.True(dmg.IndexOf("if (!hazeOn || p.Brief) { p.Live = false; continue; }", StringComparison.Ordinal) < dmg.IndexOf("if (!p.Haze) HandOver(ref p);", StringComparison.Ordinal));
        Assert.Contains("GunSmoke.For(s.Weapon.Def.Kind, GunSmoke.Now) is { } gp", cv);
    }
}
