using I76.Game.Boot;
using I76.Game.Shell;
using I76.Game.Sim;
using I76.Sim;
using I76.Sim.Rng;
using I76.Sim.Terrain;
using I76.Sim.Weapons;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The settings without a row (docs/playing.md): their stored words, the keys that follow the look, the sky's default, Unlim.
/// Ordnance offline only; and the pads' rows that fold or mark the original's value (Force Feedback, Melee A.I.).
/// </summary>
[Collection("OriginalBehavior")]   // OriginalBehavior.Apply sets globals: not alongside the lamp ramp tests
public class SettingsWithoutARowTests
{
    private sealed class Flat : IGround
    {
        public (float Height, System.Numerics.Vector3 Normal) Query(double x, double y, double z) => (0f, System.Numerics.Vector3.UnitY);
        public int SurfaceId(double x, double z) => 0;
        public Surface Surface(int id) => new(2f, 0.03f, 0, 0, 0);
    }

    [Theory]
    [InlineData("unlimited", 1)]
    [InlineData("Original", 0)]
    [InlineData(" steady ", null)]      // another key's word
    [InlineData("nonsense", null)]
    [InlineData(null, null)]
    public void StoredWordsReadBack(string? word, int? index) => Assert.Equal(index, OriginalRecipe.ParseChoice(word, OriginalRecipe.OrdnanceNames));

    [Fact]
    public void EveryTableIsTheOriginalsWordFirst()
    {
        foreach (var names in new[] { OriginalRecipe.MirrorRangeNames, OriginalRecipe.BrakeLampNames, OriginalRecipe.MgTracerNames,
            OriginalRecipe.SkyDriftNames, OriginalRecipe.DeathOrbitNames, OriginalRecipe.MusicTrackNames, OriginalRecipe.OrdnanceNames,
            OriginalRecipe.SpecialsPanelNames, OriginalRecipe.HelicopterHearingNames, OriginalRecipe.WreckSolidNames })
            Assert.Equal("original", OriginalRecipe.Word(names[0]));
    }

    [Fact]
    public void AKeyThatFollowsTheLookIsTheOriginalsIn1997AndTheUpdateInTheModernLook()
    {
        var s = new ShellSettings();
        Assert.Null(s.MirrorRangeChoice);
        Assert.Null(s.BrakeLampChoice);
        Assert.Null(s.MgTracerChoice);
        Assert.Null(s.DrawDistanceChoice);
        OriginalBehavior.Apply(s);
        Assert.Equal(100f, OriginalBehavior.MirrorFar(true));
        Assert.Equal(MirrorCameraSettings.FullFarMetres, OriginalBehavior.MirrorFar(false));
        Assert.False(OriginalBehavior.BrakeHold(true));
        Assert.True(OriginalBehavior.BrakeHold(false));
        Assert.Equal(I76.Formats.Imaging.DrawDistance.Original, I76.Formats.Imaging.DrawDistance.Resolve(s.DrawDistanceChoice, true));
        Assert.Equal(I76.Formats.Imaging.DrawDistance.Far, I76.Formats.Imaging.DrawDistance.Resolve(s.DrawDistanceChoice, false));
        // a key set by hand holds in either look
        OriginalBehavior.Apply(new ShellSettings { MirrorRangeChoice = OriginalBehavior.Original });
        Assert.Equal(100f, OriginalBehavior.MirrorFar(false));
        OriginalBehavior.Apply(new ShellSettings());
    }

    /// <summary>[port] specials_panel: the modern look shows the 3D dash's specials art square unless the file says original; the 1997 look never does.</summary>
    [Fact]
    public void TheSpecialsPanelIsSquareInTheModernLookOnly()
    {
        var s = new ShellSettings();
        Assert.Null(s.SpecialsPanelChoice);
        OriginalBehavior.Apply(s);
        Assert.True(OriginalBehavior.SquareSpecials(false));
        Assert.False(OriginalBehavior.SquareSpecials(true));
        OriginalBehavior.Apply(new ShellSettings { SpecialsPanelChoice = OriginalRecipe.ParseChoice("original", OriginalRecipe.SpecialsPanelNames) });
        Assert.False(OriginalBehavior.SquareSpecials(false));
        OriginalBehavior.Apply(new ShellSettings { SpecialsPanelChoice = OriginalBehavior.Updated });
        Assert.False(OriginalBehavior.SquareSpecials(true));
        OriginalBehavior.Apply(new ShellSettings());
    }

    /// <summary>
    /// [port] helicopter_hearing: follows the look (the original's car-engine voice in the 1997 look, the far bands in the modern one)
    /// until the file says original or far; the key is written only when chosen, so a file from before it loads as it was.
    /// </summary>
    [Fact]
    public void TheHelicoptersHearingFollowsTheLookUntilTheFileChooses()
    {
        Assert.Equal("original", OriginalRecipe.Word(OriginalRecipe.HelicopterHearingNames[0]));
        Assert.Equal("far", OriginalRecipe.Word(OriginalRecipe.HelicopterHearingNames[1]));
        var s = new ShellSettings();
        Assert.Null(s.HelicopterHearingChoice);
        Assert.False(s.Write(null).HasSectionKey("port", "helicopter_hearing"));
        OriginalBehavior.Apply(s);
        Assert.Null(OriginalBehavior.RotorHearing(true));
        Assert.Same(OriginalBehavior.FarHearing, OriginalBehavior.RotorHearing(false));
        Assert.Equal(2000f, OriginalBehavior.RotorHearing(false)!.RangeMetres);
        foreach (var (word, look1997, far) in new[] { ("original", false, false), ("far", true, true), ("Far", false, true) })
        {
            var f = new ShellSettings().Write(null);
            f.SetValue("port", "helicopter_hearing", word);
            var read = ShellSettings.Read(f);
            OriginalBehavior.Apply(read);
            Assert.Equal(far, OriginalBehavior.RotorHearing(look1997) is not null);
            Assert.Equal(OriginalRecipe.Word(word), (string)read.Write(null).GetValue("port", "helicopter_hearing"));
        }
        OriginalBehavior.Apply(new ShellSettings());
    }

    /// <summary>[port] solid_cliffs: off (the original's) in both looks unless the file says on; written only when on.</summary>
    [Fact]
    public void SolidCliffsIsOffUnlessTheFileSaysOn()
    {
        var s = new ShellSettings();
        Assert.False(s.SolidCliffs);
        Assert.False(s.Write(null).HasSectionKey("port", "solid_cliffs"));
        s.Look1997 = false;
        Assert.False(ShellSettings.Read(s.Write(null)).SolidCliffs);
        foreach (var (word, on) in new[] { ("on", true), ("ON", true), ("off", false), ("yes", false) })
        {
            var f = new ShellSettings().Write(null);
            f.SetValue("port", "solid_cliffs", word);
            var read = ShellSettings.Read(f);
            Assert.Equal(on, read.SolidCliffs);
            Assert.Equal(on, read.Write(null).HasSectionKey("port", "solid_cliffs"));
        }
    }

    /// <summary>
    /// [port] wreck_solid: follows the look (the original's wreck timer in the 1997 look, solid while drawn in the modern one) until the
    /// file says original or drawn; written only when chosen. It is the session's rule offline and never touches a network game's.
    /// </summary>
    [Fact]
    public void WrecksStaySolidFollowsTheLookUntilTheFileChooses()
    {
        Assert.Equal("original", OriginalRecipe.Word(OriginalRecipe.WreckSolidNames[0]));
        Assert.Equal("drawn", OriginalRecipe.Word(OriginalRecipe.WreckSolidNames[1]));
        var s = new ShellSettings();
        Assert.Null(s.WreckSolidChoice);
        Assert.False(s.Write(null).HasSectionKey("port", "wreck_solid"));
        OriginalBehavior.Apply(s);
        Assert.False(OriginalBehavior.WrecksStaySolid(true));
        Assert.True(OriginalBehavior.WrecksStaySolid(false));
        Assert.Equal(!s.Look1997, s.WrecksStaySolid);
        foreach (var (word, look1997, solid) in new[] { ("original", false, false), ("drawn", true, true), ("Drawn", false, true), ("original", true, false) })
        {
            var f = new ShellSettings().Write(null);
            f.SetValue("port", "wreck_solid", word);
            var read = ShellSettings.Read(f);
            OriginalBehavior.Apply(read);
            Assert.Equal(solid, OriginalBehavior.WrecksStaySolid(look1997));
            read.Look1997 = look1997;
            Assert.Equal(solid, read.WrecksStaySolid);
            Assert.Equal(OriginalRecipe.Word(word), (string)read.Write(null).GetValue("port", "wreck_solid"));
        }
        // a word that is not one of the two is no choice: the look's again
        var odd = new ShellSettings().Write(null);
        odd.SetValue("port", "wreck_solid", "always");
        Assert.Null(ShellSettings.Read(odd).WreckSolidChoice);

        // the session's rule: the player's own game takes it, a network game's world is left to its host
        OriginalBehavior.Apply(new ShellSettings());
        var world = new I76.Sim.SimWorld(new Flat(), new I76.Sim.Rng.CrtRand(1));
        var combat = new I76.Sim.Weapons.Combat(world, _ => null);
        OriginalBehavior.ApplyWrecks(combat, look1997: false);
        Assert.True(world.WrecksStaySolid);
        OriginalBehavior.ApplyWrecks(combat, look1997: true);
        Assert.False(world.WrecksStaySolid);
        combat.NetSession = true;
        OriginalBehavior.ApplyWrecks(combat, look1997: false);
        Assert.False(world.WrecksStaySolid);
        combat.NetSession = false; combat.RemoteSim = true;
        OriginalBehavior.ApplyWrecks(combat, look1997: false);
        Assert.False(world.WrecksStaySolid);
        OriginalBehavior.Apply(new ShellSettings());
    }

    [Fact]
    public void The1997LookAlwaysDrawsTheGunsLine()
    {
        OriginalBehavior.Apply(new ShellSettings { MgTracerChoice = OriginalBehavior.Updated });
        Assert.True(OriginalBehavior.LineTracers(true));
        Assert.False(OriginalBehavior.LineTracers(false));
        OriginalBehavior.Apply(new ShellSettings());
    }

    /// <summary>The default: the clouds do not drift twice as fast with the mirror up; [port] sky_drift=original brings the original's second step back.</summary>
    [Fact]
    public void TheSkyStepsOnceATickUnlessTheFileSaysOriginal()
    {
        var s = new ShellSettings();
        Assert.Equal(1, s.SkyDrift);
        Assert.Equal("steady", OriginalRecipe.Word(OriginalRecipe.SkyDriftNames[s.SkyDrift]));
        OriginalBehavior.Apply(s);
        Assert.True(OriginalBehavior.SteadySky);
        OriginalBehavior.Apply(new ShellSettings { SkyDrift = OriginalRecipe.ParseChoice("original", OriginalRecipe.SkyDriftNames)!.Value });
        Assert.False(OriginalBehavior.SteadySky);
        OriginalBehavior.Apply(s);
    }

    /// <summary>[port] road_grain: off by default (upstream's roads), written only when on, applied through OriginalBehavior (MissionBuilder reads it).</summary>
    [Fact]
    public void TheRoadGrainIsOffUnlessTheFileSaysTrue()
    {
        Assert.False(new ShellSettings().RoadGrain);
        Assert.False(new ShellSettings().Write(null).HasSectionKey("port", "road_grain"));
        var f = new ShellSettings { RoadGrain = true }.Write(null);
        Assert.Equal(true, f.GetValue("port", "road_grain").Obj);
        var s = ShellSettings.Read(f);
        Assert.True(s.RoadGrain);
        OriginalBehavior.Apply(s);
        Assert.True(OriginalBehavior.RoadGrain);
        OriginalBehavior.Apply(new ShellSettings());
        Assert.False(OriginalBehavior.RoadGrain);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void UnlimOrdnanceReachesAnOfflineSessionOnly(bool net, bool expected)
    {
        var c = new Combat(new SimWorld(new Flat(), new CrtRand(1)), _ => null) { NetSession = net };
        PlayOptionsCombat.Apply(new ShellSettings { OrdnanceLimit = 1 }, c);
        Assert.Equal(expected, c.UnlimitedOrdnance);
        PlayOptionsCombat.Apply(new ShellSettings(), c);
        Assert.False(c.UnlimitedOrdnance);
    }

    /// <summary>Force Feedback's four values over the file's three keys: Off, Pad (the original's effects), Pad Enhanced (the port's pulses too), Wheel (the port's: the original drove a stick).</summary>
    [Fact]
    public void ForceFeedbackFoldsThePadsEventsAndTheWheelIntoOneRow()
    {
        Assert.Equal(["Off", "Pad", "Pad Enhanced*", "Wheel*"], ShellSettings.ForceFeedbackNames);
        var s = new ShellSettings();
        Assert.Equal(1, s.ForceFeedbackMode);                  // on, the pad, the game's own effects: the default
        s.ForceFeedbackMode = 2;
        Assert.True(s.ForceFeedback);
        Assert.False(s.ForceFeedbackWheel);
        Assert.Equal(I76.Formats.Input.RumbleMode.Enhanced, s.RumbleMode);
        s.ForceFeedbackMode = 3;
        Assert.True(s.ForceFeedbackWheel);
        Assert.Equal(I76.Formats.Input.RumbleMode.Original, s.RumbleMode);
        Assert.Equal(I76.Game.DirectInputForce.Supported ? 3 : 1, s.ForceFeedbackMode);   // without DirectInput the wheel reads as the pad
        s.ForceFeedbackMode = 0;
        Assert.False(s.ForceFeedback);
        Assert.False(s.ForceFeedbackWheel);
        s.ForceFeedbackMode = 1;
        Assert.True(s.ForceFeedback);
        Assert.Equal(I76.Formats.Input.RumbleMode.Original, s.RumbleMode);
    }

    /// <summary>A value shows what it is, and every value the original does not have is marked (docs/playing.md): the original's are the unmarked ones, and no row says "Original".</summary>
    [Fact]
    public void EveryRowMarksThePortsValuesAndLeavesTheOriginalsPlain()
    {
        Assert.Equal("*", ShellSettings.PortMark);
        foreach (var (names, originals) in new[]
        {
            (ShellSettings.ForceFeedbackNames, new[] { "Off", "Pad" }),
            (ShellSettings.MeleeAiNames, ["Normal"]),
            (I76.Sim.Views.ViewFov.Names, ["90"]),
            (I76.Sim.Views.PaintFinish.StepNames, ["Matte"]),
            (I76.Game.Boot.RenderScale.Names, ["100%"]),
            (I76.Sim.Views.CrashBlurMode.Names, ["On"]),
            (I76.Sim.Views.MouseLook.Names, ["Off"]),
            (I76.Game.Boot.RenderScale.AntiAliasing, ["Off", "2x", "4x"]),
            (AntiAliasingRow.Names, ["Off", "2x", "4x"]),
            (ShadowDetailRow.Names, ["Off", "Low", "High"]),
            (VisibilityRange.Names, ["Far"]),
            // World Textures and Car & Dash Textures: 1997, the install's own, is the original's
            (TextureRow.Names(new ShellSettings { WorldTextures = 4 }, GraphicsRow.WorldTextures, [8, 4]), ["1997"]),
            (TextureRow.Names(new ShellSettings { CarTextures = 8 }, GraphicsRow.CarTextures, [8, 4]), ["1997"]),
        })
        {
            Assert.Equal(originals, names.Where(n => !n.EndsWith(ShellSettings.PortMark, System.StringComparison.Ordinal)));
            Assert.DoesNotContain("Original", names);
        }
        Assert.Equal("Normal", ShellSettings.MeleeAiNames[(int)I76.Sim.Missions.MeleeAiSkill.Original]);
    }

    /// <summary>[port] lighting_calibration, on unless off; a file from before it keeps its look_colour_match=off.</summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("off", null, false)]
    [InlineData(" Off ", null, false)]
    [InlineData("on", null, true)]
    [InlineData(null, "off", false)]
    [InlineData("on", "off", true)]
    [InlineData("off", "on", false)]
    public void TheLightingCalibrationKeyReadsTheEarlierColourMatchKey(string? word, string? earlier, bool on)
    {
        Assert.Equal(on, ShellSettings.LightingCalibrationOn(word, earlier));
        Assert.True(new ShellSettings().LightingCalibration);
    }

    /// <summary>[port] lighting_calibration=filmic: the calibration as it was under Filmic; on (the card's curve) by default.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("on", false)]
    [InlineData("off", false)]
    [InlineData("filmic", true)]
    [InlineData(" Filmic ", true)]
    public void TheLightingCalibrationKeysFilmicWordKeepsTheFilmicCalibration(string? word, bool filmic)
    {
        Assert.Equal(filmic, ShellSettings.LightingFilmicOn(word));
        Assert.True(ShellSettings.LightingCalibrationOn(word, null) || word == "off");
        Assert.False(new ShellSettings().LightingFilmic);
    }
}
