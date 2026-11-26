using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace I76.Game;

/// <summary>The port's head tracking (the Controls sheet's Head Tracking): off, the head in proportion, or the glance poses past a threshold.</summary>
public enum HeadTrackMode { Off, Analog, Digital }

/// <summary>
/// Where the poses come from ([port] head_track_source): whichever is live (Auto), opentrack's "UDP over network" output only, or
/// its "freetrack 2.0 Enhanced" output (the FT_SharedMem memory, Windows only) only.
/// </summary>
public enum HeadTrackSource { Auto, Udp, FreeTrack }

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
/// opentrack's FreeTrack output, the FT_SharedMem memory map, as opentrack's freetrackclient/fttypes.h lays it out (struct FTData, the
/// head of FTHeap; all fields 4 bytes, little-endian): uint32 DataID, int32 CamWidth, int32 CamHeight, then float Yaw, Pitch, Roll,
/// X, Y, Z (then the raw pose and the points, not read). opentrack's proto-ft (freetrack::pose) writes Yaw = -yaw, Pitch = -pitch and
/// Roll = roll in radians and X, Y, Z = the position times 10 (mm), then adds 1 to DataID (it sets it to 0 when a game's id changes):
/// so DataID changing means a new pose, and the decode undoes the signs and units to the UDP output's (degrees, cm).
/// </summary>
public static class FreeTrack
{
    /// <summary>The memory map's name (FREETRACK_HEAP).</summary>
    public const string MapName = "FT_SharedMem";
    /// <summary>The bytes read: DataID, the camera's size and the six pose floats.</summary>
    public const int Size = 36;

    /// <summary>The DataID and the pose (in the UDP output's units and signs); null when short or a value is not a finite number.</summary>
    public static (uint DataId, HeadPose Pose)? Decode(ReadOnlySpan<byte> d)
    {
        if (d.Length < Size) return null;
        uint id = BinaryPrimitives.ReadUInt32LittleEndian(d);
        Span<float> v = stackalloc float[6];   // yaw, pitch, roll, x, y, z
        for (int i = 0; i < 6; i++)
        {
            v[i] = BinaryPrimitives.ReadSingleLittleEndian(d.Slice(12 + i * 4, 4));
            if (!float.IsFinite(v[i])) return null;
        }
        const double deg = 180 / Math.PI;
        return (id, new HeadPose(v[3] / 10.0, v[4] / 10.0, v[5] / 10.0, -v[0] * deg, -v[1] * deg, v[2] * deg));
    }
}

/// <summary>
/// Which source feeds the head (apart from the engine so it can be tested). Auto: the one in use while it stays live, else whichever is
/// live, UDP first; Udp or FreeTrack: that one only. Null: none is live.
/// </summary>
public static class HeadTrackSources
{
    public static HeadTrackSource? Choose(HeadTrackSource setting, bool udpLive, bool freeTrackLive, HeadTrackSource? current) => setting switch
    {
        HeadTrackSource.Udp => udpLive ? HeadTrackSource.Udp : null,
        HeadTrackSource.FreeTrack => freeTrackLive ? HeadTrackSource.FreeTrack : null,
        _ when current == HeadTrackSource.FreeTrack && freeTrackLive => HeadTrackSource.FreeTrack,
        _ => udpLive ? HeadTrackSource.Udp : freeTrackLive ? HeadTrackSource.FreeTrack : null,
    };

    /// <summary>The settings key's text (auto, udp, freetrack) read; anything else is Auto.</summary>
    public static HeadTrackSource Parse(string? text) =>
        Enum.TryParse<HeadTrackSource>(text?.Trim(), true, out var v) && Enum.IsDefined(v) ? v : HeadTrackSource.Auto;

    public static string Key(HeadTrackSource s) => s.ToString().ToLowerInvariant();
}

/// <summary>
/// The head tracker's part in the cockpit's head, apart from the engine so it can be tested. Analog: the tracked yaw and pitch
/// times the sensitivity, held to the pad's free look reach (<see cref="I76.Formats.Input.FreeLook"/>: 80° each side, 22° above
/// and 25° below the forward −13°, less below to the sides: <see cref="I76.Formats.Input.CockpitReach"/>), blended in over 150 ms (a smoothstep) when tracking starts or a glance lets go, and out the
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
        return (y, Math.Min(p, I76.Formats.Input.CockpitReach.MaxPitch(y)));   // never low enough to the side to see the arms' cut-off ends (#165)
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

/// <summary>
/// opentrack's FreeTrack output read without blocking (Windows only): the FT_SharedMem map opened read-only when it exists (opentrack
/// makes it when tracking starts with that output; never made here), looked for again once a second while it is missing; a pose is
/// new when DataID changes. Elsewhere it never opens and polls nothing.
/// </summary>
public sealed class FreeTrackReader : IDisposable
{
    private System.IO.MemoryMappedFiles.MemoryMappedFile? _map;
    private System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? _view;
    private readonly byte[] _buf = new byte[FreeTrack.Size];
    private uint? _lastId;
    private double _retry;

    public static bool Supported => OperatingSystem.IsWindows();

    /// <summary>The newest pose when DataID moved since the last poll (dt: seconds since then, for the retry), or null.</summary>
    public HeadPose? Poll(double dt)
    {
        if (!Supported) return null;
        if (_view is null)
        {
            if ((_retry -= dt) > 0) return null;
            _retry = 1;
            if (!Open()) return null;
        }
        try
        {
            // opentrack stores each field on its own (no lock a reader can take): read twice, keep a read DataID did not move under
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _view!.ReadArray(0, _buf, 0, _buf.Length);
                if (_view.ReadUInt32(0) != System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(_buf)) continue;
                if (FreeTrack.Decode(_buf) is not { } f) return null;
                bool fresh = _lastId is { } last && last != f.DataId;
                _lastId = f.DataId;   // the first read only sets the mark: the map outlives a stopped opentrack's last pose
                return fresh ? f.Pose : null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException) { Close(); }
        return null;
    }

    private bool Open()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            _map = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(FreeTrack.MapName, System.IO.MemoryMappedFiles.MemoryMappedFileRights.Read);
            _view = _map.CreateViewAccessor(0, FreeTrack.Size, System.IO.MemoryMappedFiles.MemoryMappedFileAccess.Read);
            _lastId = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Close();   // FileNotFoundException (an IOException): no opentrack with that output running
            return false;
        }
    }

    private void Close()
    {
        _view?.Dispose(); _view = null;
        _map?.Dispose(); _map = null;
        _lastId = null;
    }

    public void Dispose() => Close();
}

/// <summary>
/// The one head tracker of the process: the settings applied (the UDP socket and the FreeTrack map opened, moved or closed with
/// them) and polled once a frame; the source the poses come from chosen by <see cref="HeadTrackSources.Choose"/>.
/// </summary>
public static class HeadTracker
{
    public static HeadLook Look { get; } = new();
    private static HeadTrackReceiver? _rx;
    private static FreeTrackReader? _ft;
    private static (int Port, string Bind) _bound;
    private static HeadTrackSource _source;

    /// <summary>The source the head follows now, or null (none live).</summary>
    public static HeadTrackSource? Active { get; private set; }

    /// <summary>The Controls sheet's Head Tracking and the shell.cfg keys under it: off closes the socket and the map.</summary>
    public static void Apply(Shell.ShellSettings s)
    {
        Look.Mode = s.HeadTrack;
        Look.Sensitivity = s.HeadTrackSensitivity;
        Look.Position = s.HeadTrackPosition;
        _source = s.HeadTrackSource;
        bool on = s.HeadTrack != HeadTrackMode.Off && !I76.Game.Boot.QuietWindow.DevicesLeftAlone;   // a quiet run leaves the person's tracker port alone
        bool udp = on && _source != HeadTrackSource.FreeTrack;
        bool ft = on && _source != HeadTrackSource.Udp && FreeTrackReader.Supported;
        if (!ft) { _ft?.Dispose(); _ft = null; }
        else _ft ??= new FreeTrackReader();
        var want = (s.HeadTrackPort, s.HeadTrackBind);
        if (!udp || _bound != want) { _rx?.Dispose(); _rx = null; _bound = default; }
        if (!udp || _rx is not null) return;
        try { _rx = new HeadTrackReceiver(want.HeadTrackPort, want.HeadTrackBind); _bound = want; }
        catch (System.Net.Sockets.SocketException e) { Godot.GD.PrintErr($"head tracking: cannot listen on {want.HeadTrackBind}:{want.HeadTrackPort}: {e.Message}"); }
    }

    /// <summary>The sources drained (the chosen one's newest pose fed), then one frame of the head: see <see cref="HeadLook.Step"/>.</summary>
    public static (float Weight, float Yaw, float Pitch, float Roll, (float X, float Y, float Z) Offset) Step(double dt, bool overridden)
    {
        Drain();
        return Look.Step(dt, overridden);
    }

    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static double _lastUdp = double.NegativeInfinity, _lastFt = double.NegativeInfinity, _lastDrain;

    private static void Drain()
    {
        double now = Clock.Elapsed.TotalSeconds, dt = now - _lastDrain;
        _lastDrain = now;
        var u = _rx?.Poll();
        var f = _ft?.Poll(dt);
        if (u is not null) _lastUdp = now;
        if (f is not null) _lastFt = now;
        Active = HeadTrackSources.Choose(_source, _rx is not null && now - _lastUdp < HeadLook.Timeout,
            _ft is not null && now - _lastFt < HeadLook.Timeout, Active);
        if ((Active == HeadTrackSource.Udp ? u : Active == HeadTrackSource.FreeTrack ? f : null) is { } p) Look.Feed(p);
    }

    /// <summary>A source is open and a pose came from it within <see cref="HeadLook.Timeout"/> (the menus' Opentrack row; drains, out of a mission too).</summary>
    public static bool Receiving
    {
        get
        {
            Drain();
            return Active is not null;
        }
    }
}
