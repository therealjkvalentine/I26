using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using I76.Formats.Input;
using I76.Game;
using Xunit;

namespace I76.Game.Tests;

/// <summary>Head tracking from opentrack's UDP output: the datagram, the scaling to the free look's reach, the override, the ease and digital's thresholds.</summary>
public class HeadTrackTests
{
    private static byte[] Datagram(double x, double y, double z, double yaw, double pitch, double roll, int size = HeadPose.Size)
    {
        var d = new byte[size];
        double[] v = [x, y, z, yaw, pitch, roll];
        for (int i = 0; i < 6 && i * 8 + 8 <= size; i++) BinaryPrimitives.WriteDoubleLittleEndian(d.AsSpan(i * 8), v[i]);
        return d;
    }

    private static HeadLook Tracker(HeadTrackMode mode, double yaw, double pitch, float sens = 1)
    {
        var h = new HeadLook { Mode = mode, Sensitivity = sens };
        h.Feed(new HeadPose(0, 0, 0, yaw, pitch, 0));
        return h;
    }

    [Fact] public void ADatagramIsSixLittleEndianDoubles()
    {
        var p = HeadPose.Decode(Datagram(1.5, -2, 3.25, 20, -7.5, 4));
        Assert.Equal(new HeadPose(1.5, -2, 3.25, 20, -7.5, 4), p);
    }

    [Fact] public void AShortOrBrokenDatagramIsIgnored()
    {
        Assert.Null(HeadPose.Decode(Datagram(0, 0, 0, 20, 0, 0, 47)));
        Assert.Null(HeadPose.Decode([]));
        Assert.Null(HeadPose.Decode(Datagram(0, 0, 0, double.NaN, 0, 0)));
        Assert.NotNull(HeadPose.Decode(Datagram(0, 0, 0, 20, 0, 0, 64)));   // a longer one: its first 48 bytes
    }

    [Fact] public void TheAnglesAreScaledAndHeldToTheFreeLooksReach()
    {
        Assert.Equal((30f, FreeLook.Rest), HeadLook.Scale(10, 0, 3));
        Assert.Equal((-80f, FreeLook.Rest), HeadLook.Scale(-40, 0, 3));                       // 80° each side
        Assert.Equal(FreeLook.Rest - 22, HeadLook.Scale(0, 30, 3).Pitch);                     // looking up: 22° over the forward −13°
        Assert.Equal(FreeLook.Rest + 20, HeadLook.Scale(0, -20 / 3.0, 3).Pitch, 4);           // down: 20° under it
        Assert.Equal(CockpitReach.MaxPitch(0), HeadLook.Scale(0, -30, 3).Pitch);              // and no lower than the wheel's hands allow
        Assert.Equal(CockpitReach.MaxPitch(75), HeadLook.Scale(25, -30, 3).Pitch);            // to the side: above the arms' cut-off ends (#165)
        Assert.Equal(FreeLook.Rest - 5, HeadLook.Scale(0, 2.5, 2).Pitch, 4);
    }

    [Fact] public void AnalogFollowsTheHeadOnce150MsHavePassed()
    {
        var h = Tracker(HeadTrackMode.Analog, 10, 0, 3);
        var o = h.Step(0.05, false);
        Assert.InRange(o.Weight, 0.01f, 0.5f);
        for (int i = 0; i < 3; i++) o = h.Step(0.05, false);
        Assert.Equal(1f, o.Weight);
        Assert.Equal(30f, o.Yaw);
        Assert.Equal(FreeLook.Rest, o.Pitch);
    }

    [Fact] public void AGlanceOrTheStickOverridesWhileHeld()
    {
        var h = Tracker(HeadTrackMode.Analog, 10, 0, 3);
        for (int i = 0; i < 10; i++) h.Step(0.05, false);
        float w = 1;
        for (int i = 0; i < 4; i++) { h.Feed(new HeadPose(0, 0, 0, 10, 0, 0)); w = h.Step(0.05, true).Weight; }
        Assert.Equal(0f, w);                                                                   // stood aside within 150 ms
        Assert.Equal((false, false, false), Tracker(HeadTrackMode.Digital, -40, 0, 3).Glances(true));
        h.Feed(new HeadPose(0, 0, 0, 10, 0, 0));
        Assert.True(h.Step(0.05, false).Weight > 0);                                          // let go: back in
    }

    [Fact] public void StoppedTrackingEasesBackToCentreOver150MsWithASmoothstep()
    {
        var h = Tracker(HeadTrackMode.Analog, 20, 0, 1);
        h.Step(0.2, false);
        Assert.Equal(1f, h.Step(0.29, false).Weight);                                          // 490 ms since the packet: still live
        var curve = new List<float>();
        for (int i = 0; i < 8; i++) curve.Add(h.Step(0.025, false).Weight);
        // past 500 ms the weight falls 1 → 0 in 150 ms: 1 − smoothstep(t / 150 ms), the last pose held meanwhile
        for (int i = 0; i < 6; i++) Assert.Equal(HeadLook.Smooth(1 - (i + 1) * 0.025f / 0.15f), curve[i], 3);
        Assert.Equal(0.5f, HeadLook.Smooth(1 - 3 * 0.025f / 0.15f), 3);                       // half way at 75 ms
        Assert.Equal(0f, curve[6]);
        Assert.Equal(0.5f, HeadLook.Smooth(0.5f), 5);
        Assert.Equal(0.15625f, HeadLook.Smooth(0.25f), 5);
        Assert.Equal(20f, h.Step(0.025, false).Yaw);
    }

    [Fact] public void TurningTheModeOffEasesBackToo()
    {
        var h = Tracker(HeadTrackMode.Analog, 20, 0, 1);
        h.Step(0.2, false);
        h.Mode = HeadTrackMode.Off;
        Assert.Equal(HeadLook.Smooth(1 - 0.05f / 0.15f), h.Step(0.05, false).Weight, 4);
        Assert.False(h.Live);
    }

    [Fact] public void DigitalSnapsToTheGlancesPastItsThresholds()
    {
        var h = Tracker(HeadTrackMode.Digital, 13, 0, 3);                                        // 39°: not yet
        Assert.Equal((false, false, false), h.Glances(false));
        h.Feed(new HeadPose(0, 0, 0, 14, 0, 0));                                                  // 42°: the right glance
        Assert.Equal((false, true, false), h.Glances(false));
        h.Feed(new HeadPose(0, 0, 0, 10.5, 0, 0));                                                // 31.5°: held (back under 30°)
        Assert.True(h.Glances(false).Right);
        h.Feed(new HeadPose(0, 0, 0, 9.9, 0, 0));
        Assert.False(h.Glances(false).Right);
        h.Feed(new HeadPose(0, 0, 0, -14, 0, 0));
        Assert.Equal((true, false, false), h.Glances(false));
        h.Feed(new HeadPose(0, 0, 0, 0, -5.1, 0));                                                // a nod down past 15°: the dash
        Assert.Equal((false, false, true), h.Glances(false));
        h.Feed(new HeadPose(0, 0, 0, 0, -3.5, 0));                                                // 10.5°: held
        Assert.True(h.Glances(false).Down);
        h.Feed(new HeadPose(0, 0, 0, 0, 10, 0));                                                  // up: nothing (the up glance is the look rear)
        Assert.Equal((false, false, false), h.Glances(false));
        Assert.Equal(0f, h.Step(0.2, false).Weight);                                              // digital moves no analog head
    }

    [Fact] public void TheReceiverTakesTheNewestDatagramWithoutBlocking()
    {
        using var rx = new HeadTrackReceiver(0);
        Assert.Null(rx.Poll());
        using var tx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        var to = new IPEndPoint(IPAddress.Loopback, rx.Port);
        tx.SendTo(Datagram(0, 0, 0, 5, 0, 0), to);
        tx.SendTo(Datagram(0, 0, 0, 6, 0, 0, 20), to);
        tx.SendTo(Datagram(0, 0, 0, 7, 0, 0), to);
        HeadPose? p = null;
        for (int i = 0; i < 50 && p is null; i++) { Thread.Sleep(10); p = rx.Poll(); }
        Assert.Equal(7, p?.Yaw);
    }
}
