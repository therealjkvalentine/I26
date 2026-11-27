using I76.Formats.Input;
using I76.Game;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The head tracker's yaw mapping (<see cref="HeadLook.ViewYaw"/>): the centre's still zone, no step out of it, the full look at
/// reach ÷ sensitivity, symmetry, and the settings that choose it (Head Track Reach and Centre) with their forward migration.
/// </summary>
public class HeadTrackReachTests
{
    private const float Sens = HeadLook.DefaultSensitivity, Reach = HeadLook.DefaultReach;

    [Fact] public void InsideTheSnapZoneTheViewIsDeadCentre()
    {
        foreach (double h in new[] { 0, 0.3, -0.9, 1.2, -1.69, 1.7 })
            Assert.Equal(0f, HeadLook.ViewYaw(h, Sens, Reach, HeadTrackCentre.Snap));
    }

    [Fact] public void LeavingTheZoneHasNoStep()
    {
        // 0.01° of head past the zone's edge moves the view by a hair, and no 0.01° step anywhere jumps
        Assert.InRange(HeadLook.ViewYaw(1.71, Sens, Reach, HeadTrackCentre.Snap), 0f, 0.01f);
        foreach (var c in Enum.GetValues<HeadTrackCentre>())
        {
            float prev = 0, maxStep = 0;
            for (int i = 1; i <= 4500; i++)
            {
                float v = HeadLook.ViewYaw(i * 0.01, Sens, Reach, c);
                Assert.True(v >= prev, $"{c} not monotonic at {i * 0.01}");
                maxStep = MathF.Max(maxStep, v - prev);
                prev = v;
            }
            // the steepest the curve gets: power × reach ÷ full turn per degree of head
            var (zone, power) = HeadLook.Shape(c);
            Assert.True(maxStep <= power * Sens * 0.01f + 1e-3f, $"{c}: a step of {maxStep}");
        }
    }

    [Fact] public void AComfortableTurnReachesStraightBack()
    {
        var (zone, _) = HeadLook.Shape(HeadTrackCentre.Snap);
        float full = zone + Reach / Sens;   // about 27.4° of head
        Assert.InRange(full, 25f, 28f);
        Assert.Equal(180f, HeadLook.ViewYaw(full, Sens, Reach, HeadTrackCentre.Snap), 3);
        Assert.Equal(-180f, HeadLook.ViewYaw(-full, Sens, Reach, HeadTrackCentre.Snap), 3);
        Assert.Equal(180f, HeadLook.ViewYaw(45, Sens, Reach, HeadTrackCentre.Snap));              // held past it
        // small turns barely move it: 5° of head looks under 6° round, half the full turn under a third of the reach
        Assert.InRange(HeadLook.ViewYaw(5, Sens, Reach, HeadTrackCentre.Snap), 0.1f, 6f);
        Assert.InRange(HeadLook.ViewYaw(zone + Reach / Sens / 2, Sens, Reach, HeadTrackCentre.Snap), 50f, 60f);
    }

    [Fact] public void TheMappingIsSymmetric()
    {
        foreach (var c in Enum.GetValues<HeadTrackCentre>())
            foreach (float r in HeadLook.Reaches)
                for (double h = 0; h <= 60; h += 0.37)
                    Assert.Equal(-HeadLook.ViewYaw(h, Sens, r, c), HeadLook.ViewYaw(-h, Sens, r, c));
    }

    [Fact] public void CentreOffAtReach80IsTheFirstMapping()
    {
        for (double h = -40; h <= 40; h += 0.5)
            Assert.Equal(Math.Clamp((float)h * 3, -80f, 80f), HeadLook.ViewYaw(h, 3, 80, HeadTrackCentre.Off), 4);
        Assert.Equal(HeadLook.Scale(10, 2, 3), HeadLook.Scale(10, 2, 3, 80, HeadTrackCentre.Off));
    }

    [Fact] public void LookingRoundTheHeadRisesToTheRearGlancesPitch()
    {
        Assert.Equal(FreeLook.Rest, HeadLook.RestPitch(0));
        Assert.Equal(FreeLook.Rest, HeadLook.RestPitch(-90));
        Assert.Equal(-2f, HeadLook.RestPitch(160), 4);                                          // the original's rear glance: (160, −2, 2)
        Assert.Equal(-2f, HeadLook.RestPitch(-180), 4);
        Assert.Equal((180f, -2f), HeadLook.Scale(40, 0, Sens, Reach, HeadTrackCentre.Snap));
        // the nod's gain is the first mapping's at the free look's reach: 7 at 180 nods like 3.1 did
        Assert.Equal(7 * 80 / 180f, HeadLook.PitchGain(7, 180), 4);
        Assert.Equal(FreeLook.Rest, HeadLook.Scale(0, 0.6, Sens, Reach, HeadTrackCentre.Snap).Pitch);   // a nod inside 0.7°: still
    }

    private static (float Yaw, float Pitch) Hold(HeadLook h, double yaw, double pitch, int frames, int fps = 60)
    {
        (float, float) o = default;
        for (int i = 0; i < frames; i++)
        {
            h.Feed(new HeadPose(0, 0, 0, yaw, pitch, 0));   // a pose every frame, as a tracker sends
            var s = h.Step(1.0 / fps, false);
            o = (s.Yaw, s.Pitch);
        }
        return o;
    }

    [Fact] public void AnalogSitsDeadCentreInTheZoneAndFollowsOutside()
    {
        var h = new HeadLook { Mode = HeadTrackMode.Analog };
        Hold(h, 20, 0, 20);
        var o = Hold(h, 1.2, 0.3, 20);                                                              // back inside the zone
        Assert.Equal(0f, o.Yaw);                                                                   // landed exactly
        Assert.Equal(FreeLook.Rest, o.Pitch);
        o = Hold(h, 27.4, 0, 30);
        Assert.Equal(HeadLook.ViewYaw(27.4, Sens, Reach, HeadTrackCentre.Snap), o.Yaw);
        Assert.InRange(o.Yaw, 179f, 180f);
    }

    [Fact] public void TheLagIsTheSameAtAnyFrameRate()
    {
        float At(int fps)
        {
            var h = new HeadLook { Mode = HeadTrackMode.Analog };
            Hold(h, 10, 0, fps, fps);
            return Hold(h, 20, 0, fps / 60, fps).Yaw;   // a 60th of a second later
        }
        Assert.InRange(MathF.Abs(At(60) - At(240)), 0f, 0.3f);
    }
    [Fact] public void DigitalLooksRearWhenTurnedRightRound()
    {
        var h = new HeadLook { Mode = HeadTrackMode.Digital };
        h.Feed(new HeadPose(0, 0, 0, 12.5, 0, 0));
        Assert.Equal((false, true, false, false), h.Glances(false));                               // about 42° of view: the right glance
        h.Feed(new HeadPose(0, 0, 0, -24, 0, 0));
        Assert.Equal((false, false, false, true), h.Glances(false));                              // 150° round: the look rear, not a side
        h.Feed(new HeadPose(0, 0, 0, -22.5, 0, 0));
        Assert.True(h.Glances(false).Rear);                                                        // held over 120°
        h.Feed(new HeadPose(0, 0, 0, -18, 0, 0));
        Assert.Equal((true, false, false, false), h.Glances(false));
    }

    [Fact] public void TheDefaultsAreTheEarlierLayersAndAFileWithoutTheKeysGetsThem()
    {
        var s = ShellSettings.Read(ShellFileCompatTests.Parse("[port]\nhead_track=\"analog\"\nopentrack_auto=true\n"));
        Assert.Equal((7f, 180, HeadTrackCentre.Snap), (s.HeadTrackSensitivity, s.HeadTrackReach, s.HeadTrackCentre));
    }

    [Fact] public void ASensitivitySavedBeforeTheReachKeepsTheFirstMapping()
    {
        var s = ShellSettings.Read(ShellFileCompatTests.Parse("[port]\nhead_track=\"analog\"\nhead_track_sensitivity=4.0\n"));
        Assert.Equal((4f, 80, HeadTrackCentre.Off), (s.HeadTrackSensitivity, s.HeadTrackReach, s.HeadTrackCentre));
        var back = ShellSettings.Read(s.Write(null));
        Assert.Equal((4f, 80, HeadTrackCentre.Off), (back.HeadTrackSensitivity, back.HeadTrackReach, back.HeadTrackCentre));
    }

    [Fact] public void ASensitivitySavedNowKeepsItsReach()
    {
        // written with the reach beside it, so the next read does not take it for an old file
        var s = new ShellSettings { HeadTrackSensitivity = 3 };
        var f = s.Write(null);
        Assert.True(f.HasSectionKey("port", "head_track_reach"));
        var back = ShellSettings.Read(f);
        Assert.Equal((3f, 180, HeadTrackCentre.Snap), (back.HeadTrackSensitivity, back.HeadTrackReach, back.HeadTrackCentre));
    }

    [Fact] public void TheReachAndCentreRoundTrip()
    {
        var s = ShellSettings.Read(ShellFileCompatTests.Parse("[port]\nhead_track_reach=160\nhead_track_centre=\"strong\"\n"));
        Assert.Equal((7f, 160, HeadTrackCentre.Strong), (s.HeadTrackSensitivity, s.HeadTrackReach, s.HeadTrackCentre));
        var f = s.Write(null);
        Assert.Equal("strong", (string)f.GetValue("port", "head_track_centre"));
        Assert.False(f.HasSectionKey("port", "head_track_sensitivity"));
        Assert.Equal(HeadTrackCentre.Snap, ShellSettings.Read(ShellFileCompatTests.Parse("[port]\nhead_track_centre=\"wobbly\"\n")).HeadTrackCentre);
    }

    [Fact] public void TheReachStepsAndWraps()
    {
        Assert.Equal(120, ControlsMenuScreen.NextReach(80));
        Assert.Equal(180, ControlsMenuScreen.NextReach(160));
        Assert.Equal(80, ControlsMenuScreen.NextReach(180));
        Assert.Equal(120, ControlsMenuScreen.NextReach(100));
    }

    [Fact] public void ThePoseHookReadsOpentracksDegrees()
    {
        Assert.Equal(new HeadPose(0, 0, 0, 12.5, -3, 0), HeadTracker.ParsePose("12.5,-3"));
        Assert.Equal(new HeadPose(0, 0, 0, -20, 1, 2), HeadTracker.ParsePose(" -20, 1 ,2"));
        Assert.Null(HeadTracker.ParsePose("12"));
        Assert.Null(HeadTracker.ParsePose("a,b"));
        Assert.Null(HeadTracker.ParsePose(null));
    }
}
