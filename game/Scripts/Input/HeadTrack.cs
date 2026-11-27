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
/// Head Track Centre: the still zone round straight ahead and the softness of the curve out of it. Off: none, the head in proportion
/// (the port's first head tracking); Snap: the head look of the earlier opentrack layer over the original game (a 1.7° zone
/// subtracted, so no step leaving it, and a 1.7 power curve: small turns barely move the view, a full turn still reaches the reach).
/// </summary>
public enum HeadTrackCentre { Off, Light, Snap, Strong }

/// <summary>
/// The head tracker's part in the cockpit's head, apart from the engine so it can be tested. Analog: the tracked yaw through
/// <see cref="ViewYaw"/> (the centre's still zone, its curve, up to the reach: 180° by default, straight out of the rear window) and
/// the pitch in proportion, held to the free look's reach up and down (<see cref="I76.Formats.Input.FreeLook"/>: 22° above and 25°
/// below the forward −13°, which rises to the rear glance's −2° as the head turns round past 90°; less below to the sides:
/// <see cref="I76.Formats.Input.CockpitReach"/>), followed with a 16 ms lag, blended in over 150 ms (a smoothstep) when tracking
/// starts or a glance lets go, and out the same way when a glance key or the look stick takes over, packets stop for 500 ms or the
/// mode is turned off. Digital: the same yaw and pitch past a threshold hold the glance left, right, down (to the dash) or, turned
/// right round, the look rear, as those keys do; nodding up does nothing (the up glance is the look rear).
/// </summary>
public sealed class HeadLook
{
    public const int DefaultPort = 4242;
    /// <summary>Seconds without a packet before tracking counts as stopped.</summary>
    public const double Timeout = 0.5;
    /// <summary>Seconds of the blend in or out.</summary>
    public const double Ease = 0.15;
    /// <summary>The sensitivity's steps (the Controls sheet) and default. The full look (the reach) comes at a head turn of reach ÷ sensitivity past the still zone: 180 ÷ 7, about 26°, the earlier layer's full comfortable turn.</summary>
    public static readonly float[] Sensitivities = [1, 1.5f, 2, 2.5f, 3, 4, 5, 6, 7, 8, 10];
    public const float DefaultSensitivity = 7;
    /// <summary>Head Track Reach's steps (degrees of view each side at the full head turn) and default: 80 is the pad's free look, 160 the original's rear glance, 180 straight back.</summary>
    public static readonly int[] Reaches = [80, 120, 160, 180];
    public const int DefaultReach = 180;
    public const HeadTrackCentre DefaultCentre = HeadTrackCentre.Snap;
    /// <summary>Digital's thresholds on the mapped head (degrees of view): a side glance from 40° (back under 30°), the dash from 15° down (back under 10°), the look rear from 140° round (back under 120°).</summary>
    public const float SideEnter = 40, SideLeave = 30, DownEnter = 15, DownLeave = 10, RearEnter = 140, RearLeave = 120;
    /// <summary>The eye's reach with the position on (m, each axis).</summary>
    public const float MaxOffset = 0.1f;
    /// <summary>The lag the head is followed with (s; the earlier layer's 0.6 a 8 ms tick), and the step under which it lands exactly (degrees).</summary>
    public const double Lag = 0.016;
    public const float Land = 0.05f;

    public HeadTrackMode Mode { get; set; }
    public float Sensitivity { get; set; } = DefaultSensitivity;
    public float Reach { get; set; } = DefaultReach;
    public HeadTrackCentre Centre { get; set; } = DefaultCentre;
    /// <summary>The tracked x, y, z move the eye (off: only the angles are used).</summary>
    public bool Position { get; set; }

    private HeadPose _last;
    private double _since = double.PositiveInfinity;
    private float _w;
    private (float Yaw, float Pitch, float Roll) _target = (0, I76.Formats.Input.FreeLook.Rest, 0);
    private (float X, float Y, float Z) _offset;
    private bool _left, _right, _down, _rear, _following;

    /// <summary>A packet arrived.</summary>
    public void Feed(HeadPose p) { _last = p; _since = 0; }

    /// <summary>Packets are coming (the last one under <see cref="Timeout"/> ago) and the mode is on.</summary>
    public bool Live => Mode != HeadTrackMode.Off && _since < Timeout;

    /// <summary>A centre's still zone (degrees of head turn, subtracted) and its curve's power (1: in proportion).</summary>
    public static (float Zone, float Power) Shape(HeadTrackCentre c) => c switch
    {
        HeadTrackCentre.Light => (1f, 1.4f),
        HeadTrackCentre.Snap => (1.7f, 1.7f),
        HeadTrackCentre.Strong => (3f, 2.2f),
        _ => (0f, 1f),
    };

    /// <summary>The still zone for pitch (degrees of nod): the earlier layer's 0.7°, none with the centre off.</summary>
    public static float PitchZone(HeadTrackCentre c) => c == HeadTrackCentre.Off ? 0 : 0.7f;

    /// <summary>
    /// The view's yaw (degrees, right positive) for a tracked head yaw: the still zone subtracted (exactly 0 inside it, no step out
    /// of it), then reach × (turn ÷ full turn)^power, the full turn being reach ÷ sensitivity; held at the reach past it. Odd in
    /// the head's yaw. Centre Off and reach 80 is the port's first mapping, the yaw times the sensitivity held to 80°.
    /// </summary>
    public static float ViewYaw(double headYaw, float sensitivity, float reach, HeadTrackCentre centre)
    {
        var (zone, power) = Shape(centre);
        float a = MathF.Max(0, MathF.Abs((float)headYaw) - zone);
        float full = reach / MathF.Max(sensitivity, 0.01f);
        float n = MathF.Min(1, a / full);
        float v = reach * (power == 1 ? n : MathF.Pow(n, power));
        return headYaw < 0 ? -v : v;
    }

    /// <summary>The forward pitch the head rests at for a view yaw (game degrees, down positive): −13° ahead, rising to the rear glance's −2° from 90° to 160° round (a smoothstep).</summary>
    public static float RestPitch(float viewYaw) =>
        I76.Formats.Input.FreeLook.Rest + 11 * Smooth((MathF.Abs(viewYaw) - 90) / 70);

    /// <summary>The nod's gain: what the sensitivity gave at the free look's 80° reach, kept whatever the yaw's reach (80 × sensitivity ÷ reach).</summary>
    public static float PitchGain(float sensitivity, float reach) => sensitivity * I76.Formats.Input.FreeLook.MaxYaw / MathF.Max(reach, 1);

    /// <summary>The head's target from a tracked yaw and pitch (degrees; the game's pitch: negative looks up), at a sensitivity, reach and centre.</summary>
    public static (float Yaw, float Pitch) Scale(double yaw, double pitch, float sensitivity, float reach = I76.Formats.Input.FreeLook.MaxYaw, HeadTrackCentre centre = HeadTrackCentre.Off)
    {
        float y = ViewYaw(yaw, sensitivity, reach, centre);
        float rest = RestPitch(y), zone = PitchZone(centre);
        float nod = MathF.Max(0, MathF.Abs((float)pitch) - zone) * MathF.Sign((float)pitch);
        float p = Math.Clamp(rest - nod * PitchGain(sensitivity, reach), rest - I76.Formats.Input.FreeLook.MaxUp, rest + I76.Formats.Input.FreeLook.MaxDown);
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
            var (y, p) = Scale(_last.Yaw, _last.Pitch, Sensitivity, Reach, Centre);
            if (_following && _w > 0)
            {
                // followed with a short lag (rate-free), landing exactly once within a twentieth of a degree: dead centre in the still zone
                float k = (float)(1 - Math.Exp(-dt / Lag));
                y = Follow(_target.Yaw, y, k);
                p = Follow(_target.Pitch, p, k);
            }
            _following = true;
            _target = (y, p, Math.Clamp(y / 90f, -1f, 1f) * 2);   // a little roll into the turn, as the glances and the free look lean
            _offset = Position
                ? (Math.Clamp((float)_last.X / 100, -MaxOffset, MaxOffset), Math.Clamp((float)_last.Y / 100, -MaxOffset, MaxOffset), Math.Clamp(-(float)_last.Z / 100, -MaxOffset, MaxOffset))
                : (0, 0, 0);
        }
        else _following = false;   // the next start takes the head where it is (the blend in moves the view there)
        _w = Math.Clamp(_w + (float)((on ? 1 : -1) * dt / Ease), 0, 1);
        float s = Smooth(_w);
        return (s, _target.Yaw, _target.Pitch, _target.Roll, (_offset.X * s, _offset.Y * s, _offset.Z * s));
    }

    private static float Follow(float from, float to, float k) => MathF.Abs(to - from) < Land ? to : from + (to - from) * k;

    /// <summary>Digital, one frame: the glances the head holds (none while overridden, stopped, or in another mode).</summary>
    public (bool Left, bool Right, bool Down, bool Rear) Glances(bool overridden)
    {
        if (Mode != HeadTrackMode.Digital || !Live || overridden) { _left = _right = _down = _rear = false; return (false, false, false, false); }
        float yaw = ViewYaw(_last.Yaw, Sensitivity, Reach, Centre);
        float down = -MathF.Max(0, MathF.Abs((float)_last.Pitch) - PitchZone(Centre)) * MathF.Sign((float)_last.Pitch) * PitchGain(Sensitivity, Reach);
        _rear = MathF.Abs(yaw) >= (_rear ? RearLeave : RearEnter);
        _right = !_rear && yaw >= (_right ? SideLeave : SideEnter);
        _left = !_rear && -yaw >= (_left ? SideLeave : SideEnter);
        _down = !_rear && down >= (_down ? DownLeave : DownEnter);
        return (_left, _right, _down, _rear);
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
        Look.Reach = s.HeadTrackReach;
        Look.Centre = s.HeadTrackCentre;
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

    /// <summary>
    /// I76_HEAD_POSE=yaw,pitch[,roll] (opentrack's degrees: yaw right, pitch up): a tracker that sends that pose every frame, in place
    /// of the UDP and FreeTrack sources (a capture of the mapping without a head; Head Tracking must still be on in shell.cfg).
    /// </summary>
    internal static readonly HeadPose? PoseHook = ParsePose(System.Environment.GetEnvironmentVariable("I76_HEAD_POSE"));

    internal static HeadPose? ParsePose(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var c = System.Globalization.CultureInfo.InvariantCulture;
        var parts = v.Split(',');
        if (parts.Length is < 2 or > 3) return null;
        var a = new double[3];
        for (int i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, c, out a[i]) || !double.IsFinite(a[i])) return null;
        return new HeadPose(0, 0, 0, a[0], a[1], a[2]);
    }

    private static void Drain()
    {
        double now = Clock.Elapsed.TotalSeconds, dt = now - _lastDrain;
        _lastDrain = now;
        if (PoseHook is { } hook) { Active = HeadTrackSource.Udp; Look.Feed(hook); return; }
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
