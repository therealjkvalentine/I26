using System.Diagnostics;
using System.Runtime.InteropServices;
using Godot;
using I76.Formats.Input;

namespace I76.Game;

/// <summary>
/// A force-feedback wheel or stick through DirectInput 8 (Windows only; Godot has no force-feedback API for one): the
/// sink of the original's effects as its DLL had them on the device (<see cref="WheelFrame"/>). The original's DLL opened
/// the first force-feedback joystick once at start, exclusive and background, with the auto-centre off; so does this,
/// but it tries every force-feedback device in turn (one held by another program, or a virtual one that takes no
/// effects, does not stop the next) and reacquires one that was lost instead of giving up.
///
/// Everything DirectInput is done on the sink's own thread: the game's frame only posts the newest slots (a
/// <see cref="WheelMailbox{TFrame,TControl}"/>) and never waits on a driver call, which can block on a wheel's USB
/// reports. <see cref="WheelCoalescer"/> keeps the calls to what changed. An effect type the device lacks (custom curves
/// on most wheels, ramps on some) is played by rewriting one constant force from the effect's own shape every 10 ms.
/// The two ways the original's DLL could crash are closed: an effect whose creation failed is never touched (its slot
/// holds no object), and nothing is called on the device after it was released (only this thread holds the pointers).
/// No device, or any failure while opening, leaves <see cref="Status"/> Failed and the game on the pad's rumble.
/// </summary>
public sealed class DirectInputForce
{
    public enum State { Opening, Ready, Failed, Closed }

    public static bool Supported => OperatingSystem.IsWindows();

    private static DirectInputForce? _shared;
    /// <summary>The open device, if one was asked for (one per process).</summary>
    public static DirectInputForce? Shared => _shared;

    /// <summary>Starts opening the device (or returns the one already open). <paramref name="window"/> is the game window's handle; <paramref name="wanted"/> part of a device's name, or empty for the first.</summary>
    public static DirectInputForce? Open(long window, string wanted, int strength, SceneTree? tree)
    {
        if (!Supported) return null;
        if (_shared is { Status: State.Opening or State.Ready } s && s._wanted == wanted) { s.SetStrength(strength); return s; }
        Close();
        _shared = new DirectInputForce(window, wanted, strength);
        if (tree?.Root is { } root && !GodotObject.IsInstanceValid(_keeper))
        {
            _keeper = new WheelForceKeeper { Name = "WheelForceKeeper" };
            root.CallDeferred(Node.MethodName.AddChild, _keeper);
        }
        return _shared;
    }

    /// <summary>Stops every effect and releases the device (the setting left the wheel, the game ends). Waits a moment for the thread, never for good.</summary>
    public static void Close()
    {
        var s = _shared;
        _shared = null;
        if (s is null) return;
        s._box.Send((Ctl.Quit, 0));
        if (!s._thread.Join(1500)) GD.Print("force feedback: the wheel's thread did not end within 1.5 s (a driver call blocking); left behind");
    }

    private static WheelForceKeeper? _keeper;

    private enum Ctl { Quit, StopAll, Strength }

    private readonly WheelMailbox<IReadOnlyDictionary<string, WheelSlot>, (Ctl, int)> _box = new();
    private readonly Thread _thread;
    private readonly long _window;
    private readonly string _wanted;
    private int _strength;
    private volatile State _state = State.Opening;
    private volatile int _axes = 1;

    public State Status => _state;
    /// <summary>The device's force-feedback axes: 1 a wheel, 2 a stick.</summary>
    public int Axes => _axes;
    public string? DeviceName { get; private set; }

    private DirectInputForce(long window, string wanted, int strength)
    {
        _window = window;
        _wanted = wanted ?? "";
        _strength = Math.Clamp(strength, 0, 100);
        _thread = new Thread(Run) { IsBackground = true, Name = "wheel force feedback" };
        _thread.Start();
    }

    /// <summary>The effects as they should be now (copied: the model reuses its frame).</summary>
    public void Post(WheelFrame f)
    {
        if (_state is State.Ready) _box.Post(new Dictionary<string, WheelSlot>(f.Slots));
    }

    /// <summary>Everything off (the pause, a menu, a video): the effects are stopped and released, the device kept.</summary>
    public void StopAll() => _box.Send((Ctl.StopAll, 0));

    /// <summary>The device's overall gain, 0..100 %, applied at once (DIPROP_FFGAIN may change while acquired).</summary>
    public void SetStrength(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent == _strength) return;
        _strength = percent;
        _box.Send((Ctl.Strength, percent));
    }

    // ---- the thread: all of DirectInput below runs here only

    private sealed class Live
    {
        public IntPtr Effect;
        public required WheelEffect Shape;
        public bool Streamed;
        public double Started;
    }

    private IntPtr _di, _dev, _format, _scratch, _stream;
    private readonly Dictionary<string, Live> _live = [];
    private readonly HashSet<WheelKind> _native = [];
    private readonly HashSet<string> _told = [];
    private readonly WheelCoalescer _co = new();
    private bool _lost;
    private int _streamLevel, _errors;
    private const int ScratchBytes = 32 * 1024;

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            if (!OpenDevice()) { _state = State.Failed; Release(); return; }
            _state = State.Ready;
            double lastTry = 0;
            while (true)
            {
                var (ctls, frame) = _box.Take(_live.Values.Any(l => l.Streamed) ? 10 : 200);
                double now = clock.Elapsed.TotalSeconds;
                bool quit = false;
                foreach (var (c, v) in ctls)
                    switch (c)
                    {
                        case Ctl.Quit: quit = true; break;
                        case Ctl.StopAll: Apply(_co.Clear(), now); break;
                        case Ctl.Strength: SetGain(v); break;
                    }
                if (quit) break;
                try
                {
                    if (_lost && now - lastTry >= 1) { lastTry = now; Reacquire(); }
                    if (frame is not null) Apply(_co.Diff(frame, now), now);
                    Stream(now);
                }
                catch (Exception e)
                {
                    if (++_errors <= 3) GD.Print($"force feedback: wheel error: {e.Message}");
                    if (_errors > 50) { GD.Print("force feedback: too many wheel errors; the wheel is released"); break; }
                }
            }
        }
        catch (Exception e)
        {
            GD.Print($"force feedback: the wheel could not be used: {e.Message}");
            if (_state == State.Opening) _state = State.Failed;
        }
        Release();
        if (_state != State.Failed) _state = State.Closed;
    }

    private bool OpenDevice()
    {
        var iid = IID_IDirectInput8W;
        int hr = DirectInput8Create(GetModuleHandleW(IntPtr.Zero), 0x0800, ref iid, out _di, IntPtr.Zero);
        if (hr < 0 || _di == IntPtr.Zero) { GD.Print($"force feedback: DirectInput is not available ({hr:x8}); the pad's rumble is used"); return false; }
        var found = new List<(Guid Instance, string Name, string Product)>();
        EnumCb cb = (inst, _) =>
        {
            found.Add((Marshal.PtrToStructure<Guid>(inst + 4), Marshal.PtrToStringUni(inst + 40) ?? "", Marshal.PtrToStringUni(inst + 560) ?? ""));
            return 1;
        };
        hr = Call<EnumDevicesFn>(_di, 4)(_di, 4 /* DI8DEVCLASS_GAMECTRL */, Marshal.GetFunctionPointerForDelegate(cb), IntPtr.Zero, 0x101 /* ATTACHEDONLY | FORCEFEEDBACK */);
        GC.KeepAlive(cb);
        GD.Print($"force feedback: {found.Count} force-feedback device(s){(found.Count > 0 ? ": " + string.Join(", ", found.Select(f => f.Name)) : "")}");
        if (hr < 0 || found.Count == 0) return false;
        // the one named in the settings first (part of its name), then the rest in DirectInput's order
        var order = found.OrderBy(f => _wanted.Length > 0 && (f.Name.Contains(_wanted, StringComparison.OrdinalIgnoreCase) || f.Product.Contains(_wanted, StringComparison.OrdinalIgnoreCase)) ? 0 : 1).ToList();
        _scratch = Marshal.AllocHGlobal(ScratchBytes);
        _format = Format();
        foreach (var f in order)
        {
            if (TryDevice(f.Instance, f.Name)) { DeviceName = f.Name; return true; }
            if (_dev != IntPtr.Zero) { Call<ReleaseFn>(_dev, 2)(_dev); _dev = IntPtr.Zero; }
        }
        GD.Print("force feedback: no force-feedback device could be opened (is its control panel or another game holding it?); the pad's rumble is used");
        return false;
    }

    private bool TryDevice(Guid instance, string name)
    {
        int hr = Call<CreateDeviceFn>(_di, 3)(_di, ref instance, out _dev, IntPtr.Zero);
        if (hr < 0) { GD.Print($"force feedback: {name}: CreateDevice {hr:x8}"); return false; }
        if ((hr = Call<PtrFn>(_dev, 11)(_dev, _format)) < 0) { GD.Print($"force feedback: {name}: SetDataFormat {hr:x8}"); return false; }
        if ((hr = Call<CoopFn>(_dev, 13)(_dev, (IntPtr)_window, 0x9 /* EXCLUSIVE | BACKGROUND */)) < 0) { GD.Print($"force feedback: {name}: SetCooperativeLevel {hr:x8}"); return false; }
        SetDword(9 /* DIPROP_AUTOCENTER */, 0);     // set unacquired; the device's own spring would fight the effects
        if ((hr = Call<NoArgFn>(_dev, 7)(_dev)) < 0) { GD.Print($"force feedback: {name}: Acquire {hr:x8} (held by another program?)"); return false; }
        int axes = 0;
        EnumCb count = (_, _) => { axes++; return 1; };
        Call<EnumObjectsFn>(_dev, 4)(_dev, Marshal.GetFunctionPointerForDelegate(count), IntPtr.Zero, 0x01000003 /* DIDFT_AXIS | DIDFT_FFACTUATOR */);
        GC.KeepAlive(count);
        _axes = Math.Clamp(axes, 1, 2);
        foreach (var (k, g) in Guids)
        {
            var guid = g;
            Marshal.WriteInt32(_scratch, 552);
            if (Call<EffectInfoFn>(_dev, 20)(_dev, _scratch, ref guid) >= 0) _native.Add(k);
        }
        if (!_native.Contains(WheelKind.Constant) && !_native.Contains(WheelKind.Sine) && !_native.Contains(WheelKind.Square))
        {
            GD.Print($"force feedback: {name}: takes none of the effects used");
            Call<NoArgFn>(_dev, 8)(_dev);
            return false;
        }
        Call<UintFn>(_dev, 22)(_dev, 0x1 /* DISFFC_RESET */);
        Call<UintFn>(_dev, 22)(_dev, 0x10 /* DISFFC_SETACTUATORSON */);
        SetGain(_strength);
        GD.Print($"force feedback: wheel {name}: {axes} force axis(es); effects {string.Join(" ", _native.OrderBy(k => k))}; strength {_strength}%");
        return true;
    }

    private void SetGain(int percent) => SetDword(7 /* DIPROP_FFGAIN */, (uint)Math.Clamp(percent, 0, 100) * 100);

    private void SetDword(int prop, uint value)
    {
        if (_dev == IntPtr.Zero) return;
        // DIPROPDWORD: the header (size, header size, object 0, DIPH_DEVICE) and the value
        Marshal.WriteInt32(_scratch, 0, 20);
        Marshal.WriteInt32(_scratch, 4, 16);
        Marshal.WriteInt32(_scratch, 8, 0);
        Marshal.WriteInt32(_scratch, 12, 0);
        Marshal.WriteInt32(_scratch, 16, (int)value);
        Call<SetPropertyFn>(_dev, 6)(_dev, (IntPtr)prop, _scratch);
    }

    /// <summary>A data format of the X and Y axes (both optional): Acquire needs one; the effects name their axes by these offsets.</summary>
    private static IntPtr Format()
    {
        int objSize = Marshal.SizeOf<DIOBJECTDATAFORMAT>(), fmtSize = Marshal.SizeOf<DIDATAFORMAT>();
        var mem = Marshal.AllocHGlobal(fmtSize + 2 * objSize + 32);
        IntPtr objs = mem + fmtSize, guids = objs + 2 * objSize;
        Marshal.StructureToPtr(GUID_XAxis, guids, false);
        Marshal.StructureToPtr(GUID_YAxis, guids + 16, false);
        for (int i = 0; i < 2; i++)
            Marshal.StructureToPtr(new DIOBJECTDATAFORMAT { pguid = guids + 16 * i, dwOfs = (uint)(4 * i), dwType = 0x80FFFF03 /* AXIS | ANYINSTANCE | OPTIONAL */ }, objs + i * objSize, false);
        Marshal.StructureToPtr(new DIDATAFORMAT { dwSize = (uint)fmtSize, dwObjSize = (uint)objSize, dwFlags = 1 /* DIDF_ABSAXIS */, dwDataSize = 8, dwNumObjs = 2, rgodf = objs }, mem, false);
        return mem;
    }

    private void Apply(List<WheelCommand> cmds, double now)
    {
        foreach (var c in cmds)
        {
            switch (c.Op)
            {
                case WheelOp.Create:
                {
                    var l = new Live { Shape = c.Effect!, Started = now };
                    _live[c.Slot] = l;
                    if (_native.Contains(l.Shape.Kind)) l.Effect = Create(l.Shape);
                    else if (l.Shape.Kind != WheelKind.Spring && _native.Contains(WheelKind.Constant)) l.Streamed = true;
                    else Tell($"{l.Shape.Kind}", $"force feedback: the wheel has no {l.Shape.Kind} effect; not played");
                    break;
                }
                case WheelOp.Restart:
                    if (!_live.TryGetValue(c.Slot, out var r)) break;
                    r.Shape = c.Effect!;
                    r.Started = now;
                    if (r.Effect != IntPtr.Zero) Set(r.Effect, r.Shape, WheelParams.All, start: true);
                    break;
                case WheelOp.Update:
                    if (!_live.TryGetValue(c.Slot, out var u)) break;
                    u.Shape = c.Effect!;
                    if (u.Effect != IntPtr.Zero) Set(u.Effect, u.Shape, c.Params, start: false);
                    break;
                case WheelOp.Remove:
                    if (!_live.Remove(c.Slot, out var d) || d.Effect == IntPtr.Zero) break;
                    Call<NoArgFn>(d.Effect, 8)(d.Effect);
                    Call<ReleaseFn>(d.Effect, 2)(d.Effect);
                    break;
            }
        }
    }

    /// <summary>Creates and starts an effect; zero (never used) if the device refuses it.</summary>
    private IntPtr Create(WheelEffect e)
    {
        var guid = Guids.First(g => g.Kind == e.Kind).Guid;
        IntPtr eff = IntPtr.Zero;
        int hr = Retry(() => Call<CreateEffectFn>(_dev, 18)(_dev, ref guid, Build(e), out eff, IntPtr.Zero));
        if (hr < 0 || eff == IntPtr.Zero) { Tell($"create {e.Kind}", $"force feedback: the wheel refused a {e.Kind} effect ({hr:x8})"); return IntPtr.Zero; }
        hr = Retry(() => Call<StartFn>(eff, 7)(eff, 1, 0));
        if (hr < 0) Tell($"start {e.Kind}", $"force feedback: a {e.Kind} effect did not start ({hr:x8})");
        return eff;
    }

    private void Set(IntPtr eff, WheelEffect e, WheelParams p, bool start)
    {
        uint flags = (uint)p | (start ? 0x20000000u /* DIEP_START */ : 0);
        int hr = Retry(() => Call<SetParamsFn>(eff, 6)(eff, Build(e), flags));
        if (hr < 0) Tell($"set {e.Kind}", $"force feedback: a {e.Kind} effect's update failed ({hr:x8})");
    }

    /// <summary>A call that failed because the device was not acquired (lost, or taken for a moment) is tried again once after an Acquire.</summary>
    private int Retry(Func<int> call)
    {
        int hr = call();
        if (!Unacquired(hr)) return hr;
        if (Call<NoArgFn>(_dev, 7)(_dev) < 0) { if (!_lost) GD.Print($"force feedback: the wheel was lost ({hr:x8}); trying to get it back"); _lost = true; return hr; }
        return call();
    }

    private static bool Unacquired(int hr) => (uint)hr is 0x8007000C /* NOTACQUIRED */ or 0x8007001E /* INPUTLOST */ or 0x80040205 /* NOTEXCLUSIVEACQUIRED */;

    /// <summary>Acquires a lost device again and starts its effects over (a lost device forgets what was playing).</summary>
    private void Reacquire()
    {
        if (Call<NoArgFn>(_dev, 7)(_dev) < 0) return;
        _lost = false;
        GD.Print("force feedback: the wheel is back");
        foreach (var l in _live.Values)
            if (l.Effect != IntPtr.Zero && l.Shape.DurationUs == WheelEffect.Infinite) Call<StartFn>(l.Effect, 7)(l.Effect, 1, 0);
        if (_stream != IntPtr.Zero) Call<StartFn>(_stream, 7)(_stream, 1, 0);
    }

    /// <summary>The effects the device lacks, as one constant force rewritten from their shapes (only when it moved by a step).</summary>
    private void Stream(double now)
    {
        double sum = 0;
        bool any = false;
        foreach (var l in _live.Values)
            if (l.Streamed) { any = true; sum += l.Shape.At((now - l.Started) * 1e6); }
        int level = (int)Math.Round(Math.Clamp(sum, -10000, 10000) / 100) * 100;
        if (level == _streamLevel) return;
        if (_stream == IntPtr.Zero)
        {
            if (!any) return;
            _stream = Create(new WheelEffect { Kind = WheelKind.Constant, Magnitude = level });
            if (_stream == IntPtr.Zero) { foreach (var l in _live.Values) l.Streamed = false; return; }
        }
        else Set(_stream, new WheelEffect { Kind = WheelKind.Constant, Magnitude = level }, WheelParams.TypeSpecific, start: false);
        _streamLevel = level;
    }

    /// <summary>Lays out the DIEFFECT for <paramref name="e"/> in the scratch block.</summary>
    private IntPtr Build(WheelEffect e)
    {
        int effSize = Marshal.SizeOf<DIEFFECT>();
        IntPtr axes = _scratch + 128, dirs = axes + 16, env = dirs + 16, spec = env + 32, curve = spec + 64;
        int n = e.Kind == WheelKind.Spring ? _axes : Math.Min(_axes, 2);
        Marshal.WriteInt32(axes, 0, 0);          // DIJOFS_X
        Marshal.WriteInt32(axes, 4, 4);          // DIJOFS_Y
        Marshal.WriteInt32(dirs, 0, n == 1 ? 0 : e.Direction);
        Marshal.WriteInt32(dirs, 4, 0);
        Marshal.StructureToPtr(new DIENVELOPE { dwSize = 20, dwAttackLevel = (uint)Math.Clamp(e.AttackLevel, 0, 10000), dwAttackTime = e.AttackUs, dwFadeLevel = (uint)Math.Clamp(e.FadeLevel, 0, 10000), dwFadeTime = e.FadeUs }, env, false);
        int size;
        switch (e.Kind)
        {
            case WheelKind.Constant:
                Marshal.WriteInt32(spec, e.Magnitude); size = 4; break;
            case WheelKind.Ramp:
                Marshal.WriteInt32(spec, 0, e.Magnitude); Marshal.WriteInt32(spec, 4, e.End); size = 8; break;
            case WheelKind.Spring:
                // one condition per axis: centred, the same both ways
                for (int i = 0; i < n; i++)
                    Marshal.StructureToPtr(new DICONDITION { lOffset = e.Offset, lPositiveCoefficient = e.Magnitude, lNegativeCoefficient = e.Magnitude, dwPositiveSaturation = 10000, dwNegativeSaturation = 10000 }, spec + 24 * i, false);
                size = 24 * n; break;
            case WheelKind.Custom:
            {
                var s = e.Samples ?? [];
                int count = Math.Min(s.Length, (ScratchBytes - 512) / 4);
                Marshal.Copy(s, 0, curve, count);
                Marshal.StructureToPtr(new DICUSTOMFORCE { cChannels = 1, dwSamplePeriod = e.SamplePeriodUs, cSamples = (uint)count, rglForceData = curve }, spec, false);
                size = Marshal.SizeOf<DICUSTOMFORCE>(); break;
            }
            default:
                Marshal.StructureToPtr(new DIPERIODIC { dwMagnitude = (uint)Math.Clamp(e.Magnitude, 0, 10000), lOffset = e.Offset, dwPhase = (uint)e.Phase, dwPeriod = e.PeriodUs }, spec, false);
                size = 16; break;
        }
        Marshal.StructureToPtr(new DIEFFECT
        {
            dwSize = (uint)effSize,
            dwFlags = 0x2 /* OBJECTOFFSETS */ | (n == 1 ? 0x10u /* CARTESIAN */ : 0x20u /* POLAR */),
            dwDuration = e.DurationUs,
            dwSamplePeriod = e.Kind == WheelKind.Custom ? e.SamplePeriodUs : 0,
            dwGain = (uint)Math.Clamp(e.Gain, 0, 10000),
            dwTriggerButton = 0xFFFFFFFF,             // DIEB_NOTRIGGER
            cAxes = (uint)n,
            rgdwAxes = axes,
            rglDirection = dirs,
            lpEnvelope = e.HasEnvelope && e.Kind != WheelKind.Spring ? env : IntPtr.Zero,
            cbTypeSpecificParams = (uint)size,
            lpvTypeSpecificParams = spec,
            dwStartDelay = e.StartDelayUs,
        }, _scratch, false);
        return _scratch;
    }

    private void Tell(string key, string line)
    {
        if (_told.Add(key)) GD.Print(line);
    }

    private void Release()
    {
        try
        {
            foreach (var l in _live.Values)
                if (l.Effect != IntPtr.Zero) { Call<NoArgFn>(l.Effect, 8)(l.Effect); Call<ReleaseFn>(l.Effect, 2)(l.Effect); }
            _live.Clear();
            if (_stream != IntPtr.Zero) { Call<NoArgFn>(_stream, 8)(_stream); Call<ReleaseFn>(_stream, 2)(_stream); _stream = IntPtr.Zero; }
            if (_dev != IntPtr.Zero)
            {
                Call<UintFn>(_dev, 22)(_dev, 0x2 /* DISFFC_STOPALL */);
                Call<NoArgFn>(_dev, 8)(_dev);    // Unacquire
                Call<ReleaseFn>(_dev, 2)(_dev);
                _dev = IntPtr.Zero;
            }
            if (_di != IntPtr.Zero) { Call<ReleaseFn>(_di, 2)(_di); _di = IntPtr.Zero; }
        }
        catch (Exception e) { GD.Print($"force feedback: releasing the wheel: {e.Message}"); }
        if (_scratch != IntPtr.Zero) { Marshal.FreeHGlobal(_scratch); _scratch = IntPtr.Zero; }
        if (_format != IntPtr.Zero) { Marshal.FreeHGlobal(_format); _format = IntPtr.Zero; }
    }

    // ---- COM by vtable slot (IDirectInput8W, IDirectInputDevice8W, IDirectInputEffect)

    private readonly Dictionary<(IntPtr, int, Type), Delegate> _fns = [];

    private T Call<T>(IntPtr obj, int slot) where T : Delegate
    {
        IntPtr fn = Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size);
        if (!_fns.TryGetValue((fn, slot, typeof(T)), out var d)) _fns[(fn, slot, typeof(T))] = d = Marshal.GetDelegateForFunctionPointer<T>(fn);
        return (T)d;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ReleaseFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int UintFn(IntPtr self, uint a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int PtrFn(IntPtr self, IntPtr a);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CoopFn(IntPtr self, IntPtr hwnd, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPropertyFn(IntPtr self, IntPtr prop, IntPtr header);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateDeviceFn(IntPtr self, ref Guid guid, out IntPtr dev, IntPtr outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumDevicesFn(IntPtr self, uint type, IntPtr cb, IntPtr ctx, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumObjectsFn(IntPtr self, IntPtr cb, IntPtr ctx, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateEffectFn(IntPtr self, ref Guid guid, IntPtr eff, out IntPtr effect, IntPtr outer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EffectInfoFn(IntPtr self, IntPtr info, ref Guid guid);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetParamsFn(IntPtr self, IntPtr eff, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int StartFn(IntPtr self, uint iterations, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumCb(IntPtr instance, IntPtr ctx);

    [DllImport("dinput8.dll")] private static extern int DirectInput8Create(IntPtr hinst, uint version, ref Guid iid, out IntPtr di, IntPtr outer);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandleW(IntPtr name);

    private static readonly Guid IID_IDirectInput8W = new("BF798031-483A-4DA2-AA99-5D64ED369700");
    private static readonly Guid GUID_XAxis = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
    private static readonly Guid GUID_YAxis = new("A36D02E1-C9F3-11CF-BFC7-444553540000");

    private static readonly (WheelKind Kind, Guid Guid)[] Guids =
    [
        (WheelKind.Constant, new("13541C20-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Ramp, new("13541C21-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Square, new("13541C22-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Sine, new("13541C23-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Triangle, new("13541C24-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.SawtoothUp, new("13541C25-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.SawtoothDown, new("13541C26-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Spring, new("13541C27-8E33-11D0-9AD0-00A0C9A06E35")),
        (WheelKind.Custom, new("13541C2B-8E33-11D0-9AD0-00A0C9A06E35")),
    ];

    [StructLayout(LayoutKind.Sequential)]
    private struct DIEFFECT
    {
        public uint dwSize, dwFlags, dwDuration, dwSamplePeriod, dwGain, dwTriggerButton, dwTriggerRepeatInterval, cAxes;
        public IntPtr rgdwAxes, rglDirection, lpEnvelope;
        public uint cbTypeSpecificParams;
        public IntPtr lpvTypeSpecificParams;
        public uint dwStartDelay;
    }

    [StructLayout(LayoutKind.Sequential)] private struct DIENVELOPE { public uint dwSize, dwAttackLevel, dwAttackTime, dwFadeLevel, dwFadeTime; }
    [StructLayout(LayoutKind.Sequential)] private struct DIPERIODIC { public uint dwMagnitude; public int lOffset; public uint dwPhase, dwPeriod; }
    [StructLayout(LayoutKind.Sequential)] private struct DICONDITION { public int lOffset, lPositiveCoefficient, lNegativeCoefficient; public uint dwPositiveSaturation, dwNegativeSaturation; public int lDeadBand; }
    [StructLayout(LayoutKind.Sequential)] private struct DICUSTOMFORCE { public uint cChannels, dwSamplePeriod, cSamples; public IntPtr rglForceData; }
    [StructLayout(LayoutKind.Sequential)] private struct DIOBJECTDATAFORMAT { public IntPtr pguid; public uint dwOfs, dwType, dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct DIDATAFORMAT { public uint dwSize, dwObjSize, dwFlags, dwDataSize, dwNumObjs; public IntPtr rgodf; }
}
