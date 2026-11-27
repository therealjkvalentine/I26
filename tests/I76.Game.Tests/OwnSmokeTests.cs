using I76.Game.Shell;
using I76.Game.Sim;
using I76.Sim.Damage;
using I76.Sim.Vehicles;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// [port] own_smoke (OwnSmoke): the player's own damage smoke in the modern look's cockpit view starts in the engine bay only while
/// the engine is damaged with <c>engine</c>, behind the car otherwise; <c>any</c>, the default, the engine bay as before. When a car
/// smokes stays the original's: a health gauge below 0.75, which armour alone brings down while the engine, suspension and brake are
/// whole, and any damage to one of those three takes to black at once.
/// </summary>
public class OwnSmokeTests
{
    private static VehicleDamage Car(int frontArmour, int engineHp)
    {
        var v = new Vehicle();
        var d = new VehicleDamage(v, true);
        v.Health = d;
        for (int z = 0; z < 4; z++) { d.Armour[z] = d.ArmourMax[z] = 100; d.Chassis[z] = d.ChassisMax[z] = 100; }
        d.Armour[0] = frontArmour;
        d.Slots[7] = new EnginePart(v.Engine, 100, 0.4f) { Hp = engineHp };
        return d;
    }

    [Fact]
    public void AnyIsTheDefaultAndTheKeyRoundTrips()
    {
        var s = new ShellSettings();
        Assert.False(s.OwnSmokeEngine);
        Assert.False(s.Write(null).HasSectionKey("port", "own_smoke"));
        Assert.True(OwnSmoke.Parse("engine") && OwnSmoke.Parse(" Engine "));
        Assert.False(OwnSmoke.Parse(null) || OwnSmoke.Parse("") || OwnSmoke.Parse("any") || OwnSmoke.Parse("original"));
        var f = new SettingsFile();
        f.SetValue("port", "own_smoke", "engine");
        var r = ShellSettings.Read(f);
        Assert.True(r.OwnSmokeEngine);
        var w = r.Write(null);
        Assert.Equal("engine", w.GetValue("port", "own_smoke", "").ToString());
        Assert.True(ShellSettings.Read(w).OwnSmokeEngine);
    }

    [Fact]
    public void TheDevPanelOffersBothAndAppliesThemLive()
    {
        var row = Assert.Single(I76.Game.Ui.DevSettings.All, e => e.Key == "own_smoke");
        Assert.Equal(I76.Game.Ui.DevWhen.Live, row.When);
        var s = new ShellSettings();
        Assert.Equal(0, row.Get(s));
        try
        {
            row.Set(s, 1); row.Apply!(s);
            Assert.True(s.OwnSmokeEngine && OwnSmoke.EngineOnly);
            row.Set(s, 0); row.Apply!(s);
            Assert.False(s.OwnSmokeEngine || OwnSmoke.EngineOnly);
        }
        finally { OwnSmoke.EngineOnly = false; }
    }

    [Fact]
    public void ArmourAloneSmokesAsTheOriginalButFromBehindWithEngine()
    {
        // armour alone: the gauge reads the weakest zone (0.3 x 0.72 + 0.28 = 0.496), grey smoke, the engine whole
        var armour = Car(30, 100);
        Assert.Equal(1, armour.SmokeStage());
        Assert.False(OwnSmoke.EngineDamaged(armour));
        Assert.True(OwnSmoke.FromRear(true, own: true, cockpit: true, OwnSmoke.EngineDamaged(armour)));
        Assert.False(OwnSmoke.FromRear(false, own: true, cockpit: true, OwnSmoke.EngineDamaged(armour)));   // any: the engine bay

        // the engine scratched: the gauge is its ratio taken as a percentage (the original's quirk), so it smokes black at once,
        // from the engine bay with either value
        var engine = Car(100, 90);
        Assert.Equal(2, engine.SmokeStage());
        Assert.True(OwnSmoke.EngineDamaged(engine));
        Assert.False(OwnSmoke.FromRear(true, own: true, cockpit: true, OwnSmoke.EngineDamaged(engine)));

        // armour lightly scratched (0.8: a gauge of 0.856): no smoke at all
        Assert.Equal(-1, Car(80, 100).SmokeStage());
        Assert.False(OwnSmoke.EngineDamaged(null));
    }

    [Fact]
    public void OnlyThePlayersOwnCarInTheCockpitMoves()
    {
        Assert.False(OwnSmoke.FromRear(true, own: false, cockpit: true, engineDamaged: false));   // other cars: the engine bay
        Assert.False(OwnSmoke.FromRear(true, own: true, cockpit: false, engineDamaged: false));   // the chase view: the engine bay
        Assert.True(OwnSmoke.FromRear(true, own: true, cockpit: true, engineDamaged: false));
    }
}
