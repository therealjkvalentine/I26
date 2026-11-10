using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace I76.Game;

/// <summary>The port's head tracking (Play Options' Head Tracking): off, the head in proportion, or the glance poses past a threshold.</summary>
public enum HeadTrackMode { Off, Analog, Digital }

/// <summary>One opentrack sample: positions in cm, angles in degrees (yaw right-positive, pitch up-positive), as its "UDP over network" output sends them.</summary>
public readonly record struct HeadPose(double X, double Y, double Z, double Yaw, double Pitch, double Roll)
{
    /// <summary>The datagram's size: six little-endian doubles (x, y, z, yaw, pitch, roll).</summary>
    public const int Size = 48;

    /// <summary>The datagram decoded; a short one, or one with a value that is not a finite number, is not a pose.</summary>
    public static HeadPose? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < Size) return null;
        Span<double> v = stackalloc double[6];
        for (int i = 0; i < 6; i++)
        {
            v[i] = BinaryPrimitives.ReadDoubleLittleEndian(d.Slice(i * 8, 8));
            if (!double.IsFinite(v[i])) return null;
        }
        return new HeadPose(v[0], v[1], v[2], v[3], v[4], v[5]);
    }
}

/// <summary>
/// The head tracker's part in the cockpit's head, apart from the engine so it can be tested. Analog: the tracked yaw and pitch
/// times the sensitivity, held to the pad's free look reach (<see cref="I76.Formats.Input.FreeLook"/>: 80° each side, 22° above
/// and 25° below the forward −13°), blended in over 150 ms (a smoothstep) when tracking starts or a glance lets go, and out the
/// same way when a glance key or the look stick takes over, packets stop for 500 ms or the mode is turned off. Digital: the
/// head past a threshold holds the glance left, right or down (to the dash), as those keys do; nodding up does nothing (the
/// up glance is the look rear).
/// </summary>
public sealed class HeadLook
{
    public const int DefaultPort = 4242;
    /// <summary>Seconds without a packet before tracking counts as stopped.</summary>
    public const double Timeout = 0.5;
    /// <summary>Seconds of the blend in or out.</summary>
    public const double Ease = 0.15;
    /// <summary>The sensitivity's steps (Play Options) and default: a comfortable head turn of about 25° looks 75° round.</summary>
    public static readonly float[] Sensitivities = [1, 1.5f, 2, 2.5f, 3, 4, 5];
    public const float DefaultSensitivity = 3;
    /// <summary>Digital's thresholds on the scaled head (degrees): a side glance from 40° (back under 30°), the dash from 15° down (back under 10°).</summary>
    public const float SideEnter = 40, SideLeave = 30, DownEnter = 15, DownLeave = 10;
    /// <summary>The eye's reach with the position on (m, each axis).</summary>
    public const float MaxOffset = 0.1f;

    public HeadTrackMode Mode { get; set; }
    public float Sensitivity { get; set; } = DefaultSensitivity;
    /// <summary>The tracked x, y, z move the eye (off: only the angles are used).</summary>
    public bool Position { get; set; }

    private HeadPose _last;
    private double _since = double.PositiveInfinity;
    private float _w;
    private (float Yaw, float Pitch, float Roll) _target = (0, I76.Formats.Input.FreeLook.Rest, 0);
    private (float X, float Y, float Z) _offset;
    private bool _left, _right, _down;

    /// <summary>A packet arrived.</summary>
    public void Feed(HeadPose p) { _last = p; _since = 0; }

    /// <summary>Packets are coming (the last one under <see cref="Timeout"/> ago) and the mode is on.</summary>
    public bool Live => Mode != HeadTrackMode.Off && _since < Timeout;

    /// <summary>The head's target from a tracked yaw and pitch (degrees; the game's pitch: negative looks up), at a sensitivity.</summary>
    public static (float Yaw, float Pitch) Scale(double yaw, double pitch, float sensitivity)
    {
        const float rest = I76.Formats.Input.FreeLook.Rest;
        float y = Math.Clamp((float)yaw * sensitivity, -I76.Formats.Input.FreeLook.MaxYaw, I76.Formats.Input.FreeLook.MaxYaw);
        float p = Math.Clamp(rest - (float)pitch * sensitivity, rest - I76.Formats.Input.FreeLook.MaxUp, rest + I76.Formats.Input.FreeLook.MaxDown);
        return (y, p);
    }

    /// <summary>The blend's curve: 0..1 in, smoothstep out.</summary>
    public static float Smooth(float t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// One frame (dt in seconds); <paramref name="overridden"/>: a glance key or the look stick is held. Returns the analog head
    /// (yaw, pitch, roll in degrees) and its weight over the cockpit's own head (0 none, 1 the tracker's alone), and the eye's offset
    /// (m, the game's axes: x right, y up, z forward) at that weight.
    /// </summary>
    public (float Weight, float Yaw, float Pitch, float Roll, (float X, float Y, float Z) Offset) Step(double dt, bool overridden)
    {
        _since += dt;
        bool on = Mode == HeadTrackMode.Analog && Live && !overridden;
        if (on)
        {
            var (y, p) = Scale(_last.Yaw, _last.Pitch, Sensitivity);
            _target = (y, p, Math.Clamp(y / 90f, -1f, 1f) * 2);   // a little roll into the turn, as the glances and the free look lean
            _offset = Position
                ? (Math.Clamp((float)_last.X / 100, -MaxOffset, MaxOffset), Math.Clamp((float)_last.Y / 100, -MaxOffset, MaxOffset), Math.Clamp(-(float)_last.Z / 100, -MaxOffset, MaxOffset))
                : (0, 0, 0);
        }
        _w = Math.Clamp(_w + (float)((on ? 1 : -1) * dt / Ease), 0, 1);
        float s = Smooth(_w);
        return (s, _target.Yaw, _target.Pitch, _target.Roll, (_offset.X * s, _offset.Y * s, _offset.Z * s));
    }

    /// <summary>Digital, one frame: the glances the head holds (none while overridden, stopped, or in another mode).</summary>
    public (bool Left, bool Right, bool Down) Glances(bool overridden)
    {
        if (Mode != HeadTrackMode.Digital || !Live || overridden) { _left = _right = _down = false; return (false, false, false); }
        float yaw = (float)_last.Yaw * Sensitivity, down = -(float)_last.Pitch * Sensitivity;
        _right = yaw >= (_right ? SideLeave : SideEnter);
        _left = -yaw >= (_left ? SideLeave : SideEnter);
        _down = down >= (_down ? DownLeave : DownEnter);
        return (_left, _right, _down);
    }
}

/// <summary>
/// opentrack's "UDP over network" output, read without blocking: a socket bound to the port (loopback unless told otherwise), drained
/// once a frame; the newest whole datagram wins.
/// </summary>
public sealed class HeadTrackReceiver : IDisposable
{
    private readonly Socket _socket;
    private readonly byte[] _buf = new byte[512];

    public int Port { get; }

    public HeadTrackReceiver(int port, string bind = "127.0.0.1")
    {
        var address = IPAddress.TryParse(bind, out var a) ? a : IPAddress.Loopback;
        _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
        _socket.Bind(new IPEndPoint(address, port));
        Port = ((IPEndPoint)_socket.LocalEndPoint!).Port;
    }

    /// <summary>The newest pose since the last poll, or null.</summary>
    public HeadPose? Poll()
    {
        HeadPose? newest = null;
        try
        {
            while (_socket.Available > 0)
            {
                int n = _socket.Receive(_buf);
                if (HeadPose.Decode(_buf.AsSpan(0, n)) is { } p) newest = p;
            }
        }
        catch (SocketException) { }   // a reset from an earlier send to a closed port, or nothing to read after all
        return newest;
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>The one head tracker of the process: the settings applied (the socket opened, moved or closed with them) and polled once a frame.</summary>
public static class HeadTracker
{
    public static HeadLook Look { get; } = new();
    private static HeadTrackReceiver? _rx;
    private static (int Port, string Bind) _bound;

    /// <summary>Play Options' Head Tracking and the shell.cfg keys under it: off closes the socket.</summary>
    public static void Apply(Shell.ShellSettings s)
    {
        Look.Mode = s.HeadTrack;
        Look.Sensitivity = s.HeadTrackSensitivity;
        Look.Position = s.HeadTrackPosition;
        var want = (s.HeadTrackPort, s.HeadTrackBind);
        if (s.HeadTrack == HeadTrackMode.Off || _bound != want) { _rx?.Dispose(); _rx = null; _bound = default; }
        if (s.HeadTrack == HeadTrackMode.Off || _rx is not null) return;
        try { _rx = new HeadTrackReceiver(want.HeadTrackPort, want.HeadTrackBind); _bound = want; }
        catch (System.Net.Sockets.SocketException e) { Godot.GD.PrintErr($"head tracking: cannot listen on {want.HeadTrackBind}:{want.HeadTrackPort}: {e.Message}"); }
    }

    /// <summary>The socket drained (the newest pose fed), then one frame of the head: see <see cref="HeadLook.Step"/>.</summary>
    public static (float Weight, float Yaw, float Pitch, float Roll, (float X, float Y, float Z) Offset) Step(double dt, bool overridden)
    {
        Drain();
        return Look.Step(dt, overridden);
    }

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastPacket = double.NegativeInfinity;

    private static void Drain()
    {
        if (_rx?.Poll() is not { } p) return;
        Look.Feed(p);
        _lastPacket = Clock.Elapsed.TotalSeconds;
    }

    /// <summary>The socket is open and a pose came within <see cref="HeadLook.Timeout"/> (the menus' Opentrack row; drains the socket, out of a mission too).</summary>
    public static bool Receiving
    {
        get
        {
            Drain();
            return _rx is not null && Clock.Elapsed.TotalSeconds - _lastPacket < HeadLook.Timeout;
        }
    }
}
