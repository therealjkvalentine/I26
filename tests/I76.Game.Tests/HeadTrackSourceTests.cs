using System.Buffers.Binary;
using I76.Game.Shell;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// Head tracking's second input, opentrack's FreeTrack output (FT_SharedMem): the memory's decode to the UDP output's units and signs,
/// which source the head follows, the settings keys, and the head-tracking rows' place on the Controls sheet.
/// </summary>
public class HeadTrackSourceTests
{
    /// <summary>The map's head as opentrack's proto-ft writes a pose given in its own units (degrees, cm): Yaw and Pitch negated, radians, mm.</summary>
    private static byte[] Heap(uint id, double yaw, double pitch, double roll, double x, double y, double z, int size = FreeTrack.Size)
    {
        const double rad = Math.PI / 180;
        var d = new byte[size];
        if (size >= 4) BinaryPrimitives.WriteUInt32LittleEndian(d, id);
        if (size >= 12) { BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(4), 100); BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(8), 250); }
        float[] v = [(float)(-yaw * rad), (float)(-pitch * rad), (float)(roll * rad), (float)(x * 10), (float)(y * 10), (float)(z * 10)];
        for (int i = 0; i < 6 && 12 + i * 4 + 4 <= size; i++) BinaryPrimitives.WriteSingleLittleEndian(d.AsSpan(12 + i * 4), v[i]);
        return d;
    }

    [Fact] public void TheLayoutIsFtDatasHeadDataIdTheCameraThenSixFloats()
    {
        Assert.Equal("FT_SharedMem", FreeTrack.MapName);
        Assert.Equal(4 + 4 + 4 + 6 * 4, FreeTrack.Size);
    }

    [Fact] public void APoseDecodesToTheUdpOutputsDegreesAndCentimetres()
    {
        var f = FreeTrack.Decode(Heap(77, yaw: 25, pitch: -10, roll: 4, x: 1.5, y: -2, z: 3.25));
        Assert.NotNull(f);
        var (id, p) = f!.Value;
        Assert.Equal(77u, id);
        // the same pose the UDP output would send: yaw right-positive, pitch up-positive (the signs proto-ft flips flipped back)
        Assert.Equal(25, p.Yaw, 3);
        Assert.Equal(-10, p.Pitch, 3);
        Assert.Equal(4, p.Roll, 3);
        Assert.Equal(1.5, p.X, 3);
        Assert.Equal(-2, p.Y, 3);
        Assert.Equal(3.25, p.Z, 3);
    }

    [Fact] public void ARightTurnScalesTheSameWayFromEitherSource()
    {
        var udp = new HeadPose(0, 0, 0, 25, 8, 0);
        var ft = FreeTrack.Decode(Heap(1, 25, 8, 0, 0, 0, 0))!.Value.Pose;
        var a = HeadLook.Scale(udp.Yaw, udp.Pitch, 3);
        var b = HeadLook.Scale(ft.Yaw, ft.Pitch, 3);
        Assert.Equal(a.Yaw, b.Yaw, 3);
        Assert.Equal(a.Pitch, b.Pitch, 3);
        Assert.True(b.Yaw > 0);   // right is right
    }

    [Fact] public void AShortOrBrokenMapIsNoPose()
    {
        Assert.Null(FreeTrack.Decode(Heap(1, 1, 2, 3, 4, 5, 6, size: FreeTrack.Size - 1)));
        var nan = Heap(1, 1, 2, 3, 4, 5, 6);
        BinaryPrimitives.WriteSingleLittleEndian(nan.AsSpan(16), float.NaN);
        Assert.Null(FreeTrack.Decode(nan));
        Assert.NotNull(FreeTrack.Decode(Heap(1, 1, 2, 3, 4, 5, 6, size: 120)));   // the whole FTHeap: the rest is not read
    }

    [Theory]
    // Auto: the one live; both live, the one in use stays (else UDP first); none live, none
    [InlineData(HeadTrackSource.Auto, true, false, null, HeadTrackSource.Udp)]
    [InlineData(HeadTrackSource.Auto, false, true, null, HeadTrackSource.FreeTrack)]
    [InlineData(HeadTrackSource.Auto, false, true, HeadTrackSource.Udp, HeadTrackSource.FreeTrack)]
    [InlineData(HeadTrackSource.Auto, true, true, null, HeadTrackSource.Udp)]
    [InlineData(HeadTrackSource.Auto, true, true, HeadTrackSource.FreeTrack, HeadTrackSource.FreeTrack)]
    [InlineData(HeadTrackSource.Auto, true, true, HeadTrackSource.Udp, HeadTrackSource.Udp)]
    [InlineData(HeadTrackSource.Auto, true, false, HeadTrackSource.FreeTrack, HeadTrackSource.Udp)]
    [InlineData(HeadTrackSource.Auto, false, false, HeadTrackSource.FreeTrack, null)]
    // forced: that one only
    [InlineData(HeadTrackSource.Udp, false, true, null, null)]
    [InlineData(HeadTrackSource.Udp, true, true, HeadTrackSource.FreeTrack, HeadTrackSource.Udp)]
    [InlineData(HeadTrackSource.FreeTrack, true, false, null, null)]
    [InlineData(HeadTrackSource.FreeTrack, true, true, HeadTrackSource.Udp, HeadTrackSource.FreeTrack)]
    public void TheHeadFollowsTheLiveSource(HeadTrackSource setting, bool udp, bool ft, HeadTrackSource? current, HeadTrackSource? want) =>
        Assert.Equal(want, HeadTrackSources.Choose(setting, udp, ft, current));

    [Fact] public void FreeTrackPosesCountAsTracking() =>
        Assert.Equal("Tracking", OpenTrack.Label(OpenTrack.StateOf(found: true, running: true, receiving: HeadTrackSources.Choose(HeadTrackSource.Auto, false, true, null) is not null)));

    [Fact] public void TheReaderPollsNothingWhereThereIsNoMap()
    {
        using var r = new FreeTrackReader();
        // elsewhere than Windows it never opens; on Windows with no opentrack FreeTrack output running the map is not there
        if (!FreeTrackReader.Supported) Assert.Null(r.Poll(1));
        else
        {
            var first = r.Poll(1);
            Assert.Null(first);   // the first read only marks DataID, a pose or not
        }
    }

    [Theory]
    [InlineData("auto", HeadTrackSource.Auto)]
    [InlineData("udp", HeadTrackSource.Udp)]
    [InlineData("FreeTrack", HeadTrackSource.FreeTrack)]
    [InlineData(" freetrack ", HeadTrackSource.FreeTrack)]
    [InlineData("shared", HeadTrackSource.Auto)]
    [InlineData("2", HeadTrackSource.FreeTrack)]
    [InlineData("9", HeadTrackSource.Auto)]
    [InlineData("", HeadTrackSource.Auto)]
    public void TheSourceKeyReadsAnythingElseAsAuto(string text, HeadTrackSource want) => Assert.Equal(want, HeadTrackSources.Parse(text));

    [Fact] public void TheHeadTrackingKeysRoundTripUnchanged()
    {
        var s = ShellSettings.Read(ShellFileCompatTests.Parse(
            "[port]\nhead_track=\"digital\"\nhead_track_sensitivity=2.5\nhead_track_port=5555\nhead_track_bind=\"0.0.0.0\"\n" +
            "head_track_position=true\nopentrack_auto=true\nopentrack_path=\"D:/ot\"\nhead_track_source=\"freetrack\"\n"));
        Assert.Equal(HeadTrackMode.Digital, s.HeadTrack);
        Assert.Equal(2.5f, s.HeadTrackSensitivity);
        Assert.Equal(5555, s.HeadTrackPort);
        Assert.Equal("0.0.0.0", s.HeadTrackBind);
        Assert.True(s.HeadTrackPosition);
        Assert.True(s.OpenTrackAuto);
        Assert.Equal("D:/ot", s.OpenTrackPath);
        Assert.Equal(HeadTrackSource.FreeTrack, s.HeadTrackSource);

        var f = s.Write(null);
        Assert.Equal("digital", (string)f.GetValue("port", "head_track"));
        Assert.Equal("freetrack", (string)f.GetValue("port", "head_track_source"));
        var back = ShellSettings.Read(f);
        Assert.Equal((s.HeadTrack, s.HeadTrackSensitivity, s.HeadTrackPort, s.HeadTrackBind, s.HeadTrackPosition, s.OpenTrackAuto, s.OpenTrackPath, s.HeadTrackSource),
            (back.HeadTrack, back.HeadTrackSensitivity, back.HeadTrackPort, back.HeadTrackBind, back.HeadTrackPosition, back.OpenTrackAuto, back.OpenTrackPath, back.HeadTrackSource));
    }

    [Fact] public void TheDefaultsWriteNoKeys()
    {
        var f = new ShellSettings().Write(null);
        foreach (var k in new[] { "head_track", "head_track_sensitivity", "head_track_port", "head_track_bind", "head_track_position", "opentrack_auto", "opentrack_path", "head_track_source" })
            Assert.False(f.HasSectionKey("port", k), k);
        Assert.Equal(HeadTrackSource.Auto, ShellSettings.Read(f).HeadTrackSource);
    }

    private static readonly string[] HeadRows = ["Head Tracking", "Head Track Sens.", "Opentrack", "Opentrack Auto", "Opentrack Profile"];

    [Fact] public void TheRowsAreOnTheControlsSheetAfterForceFeedbackNotInPlayOptions()
    {
        string?[] want = ["Force Feedback", "Rumble Strength", "Wheel Strength", .. HeadRows, null, "Mouse Options...", "Key Map..."];
        Assert.Equal<IEnumerable<string?>>(want, ControlsMenuScreen.Rows);
        foreach (var r in HeadRows) Assert.DoesNotContain(r, PlayOptionsScreen.ListRows(false));
    }

    [Fact] public void TheControlsSheetCarriesTheHeadTrackingRowsToTheEscMenu()
    {
        // the Esc menu hands back what the hosted sheet left (CopyControls), so a change there reaches the mission's settings
        var from = new ShellSettings { HeadTrack = HeadTrackMode.Analog, HeadTrackSensitivity = 4, OpenTrackAuto = true };
        var to = new ShellSettings();
        ControlsMenuScreen.CopyControls(from, to);
        Assert.Equal((HeadTrackMode.Analog, 4f, true), (to.HeadTrack, to.HeadTrackSensitivity, to.OpenTrackAuto));
    }

    [Fact] public void TheSensitivityStepsAndWraps()
    {
        Assert.Equal(4f, ControlsMenuScreen.NextSensitivity(3));
        Assert.Equal(1f, ControlsMenuScreen.NextSensitivity(5));
    }

    [Fact] public void TheProfileTipSaysTheDefaultFreeTrackProfileWorks() =>
        Assert.Contains("FreeTrack", MenuTips.Of("Opentrack Profile"));
}
