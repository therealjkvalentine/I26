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
            OriginalRecipe.SkyDriftNames, OriginalRecipe.DeathOrbitNames, OriginalRecipe.MusicTrackNames, OriginalRecipe.OrdnanceNames })
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
