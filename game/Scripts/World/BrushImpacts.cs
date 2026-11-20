using Godot;
using I76.Formats.Vegetation;
using I76.Game.Sim;

namespace I76.Game.World;

/// <summary>
/// The modern look's brush impacts (docs/modern-vegetation.md, Brush impacts; a deviation: the original's few plants are
/// solid props): the scattered plants answer the fighting. A round striking the ground in a plant's crown throws leaves and
/// twigs and shakes it; a car driving through lays the plants down away from it, and they rise again over 15 s; a blast
/// lays them down outward and shakes them; fire (a flame thrower's stream, a fireball, a fire drop, a burning wreck) can
/// set a leafy plant smoking and now and then burning for a few seconds, leaving it scorched. The plants are bent in the
/// vegetation shader by a field of cells round the camera (<see cref="BrushField"/>, one 128 x 128 texture, the global
/// i76_veg_field); the leaves, smoke and flames are short-lived particles, capped. Visual only: nothing the simulation reads.
/// Hidden with the plants in the 1997 look; off with shell.cfg [port] brush_impacts=off.
/// </summary>
public partial class BrushImpacts : Node3D
{
    /// <summary>shell.cfg [port] brush_impacts (no row; on by default).</summary>
    public static bool Enabled { get; set; } = true;
    private static BrushImpacts? _current;

    /// <summary>I76_BUSH_KNOCK=name,semitones,dB,start,length,fade: any sound, pitch, level, start %, length ms and fade-in ms, on top of [port] bush_knock, for trying others without a rebuild.</summary>
    private static readonly string? KnockHook = System.Environment.GetEnvironmentVariable("I76_BUSH_KNOCK");
    private static bool _hookSaid;

    /// <summary>shell.cfg [port] bush_knock (ShellSettings.BushKnock: "off" or a variant's letter), live; I76_BUSH_KNOCK on top of it.</summary>
    public static void ApplyKnock(string? choice)
    {
        BrushKnock.Use(BrushKnock.VariantOf(choice));
        if (BrushKnock.Apply(KnockHook) && !_hookSaid)
        {
            _hookSaid = true;
            GD.Print(System.FormattableString.Invariant($"bush knock: {BrushKnock.Sample} {BrushKnock.Semitones:0.#} semitones {BrushKnock.OffsetDb:0.#} dB start {BrushKnock.StartPercent:0.#} % length {BrushKnock.LengthMs:0} ms fade-in {BrushKnock.FadeInMs:0.#} ms"));
        }
    }

    public required VegetationNode Veg { get; init; }
    /// <summary>The mission's tint on the plants (the vegetation material's), for the leaves.</summary>
    public Color Tint { get; set; } = Colors.White;

    private const int MaxBursts = 40, BurstsPerFrame = 4, MaxFires = 6, MaxFlaming = 3;
    private readonly BrushField _field = new();
    private readonly byte[] _bytes = new byte[BrushField.Size * BrushField.Size * 4];
    private Image _img = null!;
    private ImageTexture _tex = null!;
    private readonly List<PlantSpot> _near = [];
    private bool _on;
    private double _t;
    private float _dt;
    private Vector3 _o;
    /// <summary>I76_BRUSH_DEBUG=1: a line twice a second of what the field holds.</summary>
    private static readonly bool Debug = System.Environment.GetEnvironmentVariable("I76_BRUSH_DEBUG") == "1";
    private int _frame, _rounds, _blasts;
    private long _ticks;
    private int _burstsThisFrame;
    private readonly List<(GpuParticles3D P, double Until)> _bursts = [];
    private readonly Dictionary<ulong, (Vector3 P, double Leaves)> _cars = [];
    private readonly RandomNumberGenerator _rng = new();

    private ParticleProcessMaterial _leafProc = null!, _strawProc = null!, _twigProc = null!, _smokeProc = null!, _flameProc = null!;
    private QuadMesh _leafMesh = null!, _smokeMesh = null!, _flameMesh = null!;
    private BoxMesh _twigMesh = null!;
    private ShaderMaterial _leafMat = null!;
    private float _meshSize = -1;

    private sealed class Fire
    {
        public Vector3 At;
        public float Radius, Height;
        public double Start, Life, FlameFrom, FlameTo;
        public GpuParticles3D Smoke = null!;
        public GpuParticles3D? Flames;
        public OmniLight3D? Light;
        public bool Ending;
    }
    private readonly List<Fire> _fires = [];

    public override void _Ready()
    {
        _img = Image.CreateEmpty(BrushField.Size, BrushField.Size, false, Image.Format.Rgba8);
        _tex = ImageTexture.CreateFromImage(_img);
        RenderingServer.GlobalShaderParameterSet("i76_veg_field", _tex);
        RenderingServer.GlobalShaderParameterSet("i76_veg_field_rect", Vector4.Zero);
        _current = this;
        MakeLook();
        // I76_BRUSH_FALL=shape[,base,per-speed]: the knock's curve (smooth, snap, topple) and time, for comparing them in captures
        if (System.Environment.GetEnvironmentVariable("I76_BRUSH_FALL") is { Length: > 0 } fall)
        {
            var parts = fall.Split(',');
            if (System.Enum.TryParse<BrushKnock.Shape>(parts[0], true, out var shape)) BrushKnock.Fall = shape;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (parts.Length > 2 && float.TryParse(parts[1], inv, out float b0) && float.TryParse(parts[2], inv, out float b1)) { BrushKnock.Base = b0; BrushKnock.PerSpeed = b1; }
        }
    }

    public override void _ExitTree()
    {
        if (_current == this) _current = null;
        RenderingServer.GlobalShaderParameterSet("i76_veg_field_rect", Vector4.Zero);
    }

    // ---- what the fighting calls (Godot's frame, global) ----

    /// <summary>A round struck the ground at <paramref name="hit"/> travelling along <paramref name="dir"/>.</summary>
    public static void Round(Vector3 hit, Vector3 dir, bool heavy) => _current?.OnRound(hit, dir, heavy);
    /// <summary>A blast of the given drawn size (ModernFx's), with fire in it or not.</summary>
    public static void Blast(Vector3 at, float size, float fire) => _current?.OnBlast(at, size, fire);
    /// <summary>A fire burning within <paramref name="radius"/> of <paramref name="at"/> this frame: it sets a plant there alight at about <paramref name="perSecond"/> a second.</summary>
    public static void Heat(Vector3 at, float radius, float perSecond) => _current?.OnHeat(at, radius, perSecond);

    private void OnRound(Vector3 hit, Vector3 dir, bool heavy)
    {
        if (!_on) return;
        Veg.PlantsNear(hit, 0.5f, _near, 16);
        PlantSpot? best = null;
        float bd = float.MaxValue;
        foreach (var s in _near)
        {
            var r = G(s.Root);
            if (hit.Y > r.Y + s.Height + 0.4f || hit.Y < r.Y - 1.5f) continue;
            float d = new Vector2(hit.X - r.X, hit.Z - r.Z).Length() - s.Radius;
            if (d < bd) { bd = d; best = s; }
        }
        if (best is not { } p) return;
        _rounds++;
        var root = G(p.Root);
        if (Debug) { var l = Veg.ToLocal(root); GD.Print($"brush: round into a plant at {l.X:0.0},{l.Y:0.0},{-l.Z:0.0} (game)"); }
        var way = new System.Numerics.Vector2(dir.X, dir.Z);
        System.Numerics.Vector2? along = way.LengthSquared() > 1e-4f ? System.Numerics.Vector2.Normalize(way) : null;
        _field.Stamp(root.X - _o.X, root.Z - _o.Z, MathF.Max(p.Radius, 0.8f), 0, heavy ? 1f : 0.75f, 0, along);
        var up = (-dir.Normalized() * 0.6f + Vector3.Up).Normalized();
        var at = new Vector3(hit.X, MathF.Max(hit.Y, root.Y + 0.1f) + 0.1f, hit.Z);
        Leaves(at, up, p.Ground, heavy ? 18 : 12, 0.3f, heavy ? 4 : 2);
    }

    private void OnBlast(Vector3 at, float size, float fire)
    {
        if (!_on) return;
        _blasts++;
        float radius = Mathf.Clamp(size * 2.2f, 4f, 16f);
        _field.Stamp(at.X - _o.X, at.Z - _o.Z, radius, 0.75f, 1f, fire > 0.5f ? 0.5f : 0.25f);
        Veg.PlantsNear(at, radius, _near, 48);
        _near.Sort((a, b) => G(a.Root).DistanceSquaredTo(at).CompareTo(G(b.Root).DistanceSquaredTo(at)));
        int n = 0;
        foreach (var s in _near)
        {
            if (s.Ground && s.Height < 0.3f) continue;
            var r = G(s.Root);
            var outward = new Vector3(r.X - at.X, 0, r.Z - at.Z);
            outward = outward.LengthSquared() > 1e-4f ? outward.Normalized() : Vector3.Zero;
            Leaves(r + Vector3.Up * s.Height * 0.6f, (outward * 1.2f + Vector3.Up).Normalized(), s.Ground, 12, MathF.Min(s.Radius * 0.6f, 0.8f), 3, force: true);
            if (++n == 5) break;
        }
        if (fire > 0.5f)
            for (int k = 0; k < 2; k++) if (_rng.Randf() < 0.5f) Ignite(at, radius * 0.5f, 0.6f);
    }

    private void OnHeat(Vector3 at, float radius, float perSecond)
    {
        if (!_on || _rng.Randf() >= perSecond * _dt) return;
        Ignite(at, radius, 0.35f);
    }

    // ---- the frame ----

    private static readonly StringName FieldRectName = "i76_veg_field_rect";   // set every frame: made once

    public override void _Process(double delta)
    {
        using var __fs = I76.Game.Boot.FrameSections.Time(I76.Game.Boot.FrameSections.Sec.Brush);
        _dt = (float)delta;
        _t += delta;
        _burstsThisFrame = 0;
        bool on = Enabled && Veg.Visible && Veg.IsVisibleInTree() && GetViewport()?.GetCamera3D() is not null;
        if (!on)
        {
            if (_on) { _field.Clear(); RenderingServer.GlobalShaderParameterSet("i76_veg_field_rect", Vector4.Zero); foreach (var f in _fires) End(f, now: true); _fires.Clear(); _cars.Clear(); }
            _on = false;
            Expire();
            return;
        }
        _on = true;
        long t0 = Debug ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        // the field lies in the world root's frame (the map moves under a rebase of the origin; the field with it)
        _o = Veg.GlobalPosition;
        var cam = GetViewport().GetCamera3D().GlobalPosition;
        _field.Follow(cam.X - _o.X, cam.Z - _o.Z);
        Cars();
        Fires();
        _field.Decay(_dt);
        if (_field.Dirty)
        {
            _field.Encode(_bytes);
            _img.SetData(BrushField.Size, BrushField.Size, false, Image.Format.Rgba8, _bytes);
            _tex.Update(_img);
        }
        RenderingServer.GlobalShaderParameterSet(FieldRectName, new Vector4(_field.MinX + _o.X, _field.MinZ + _o.Z, 1f / (BrushField.Size * BrushField.Cell), 1f));
        Expire();
        if (Debug) _ticks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        if (Debug && ++_frame % 30 == 0)
            GD.Print($"brush: scorch bytes {_bytes.Where((b, k) => k % 4 == 2 && b > 0).Count()}, lay bytes {_bytes.Where((b, k) => k % 4 == 0 && b > 0).Count()}, car {(CabClip.Count > 0 ? CabClip.Positions[0] : default)} way {(CabClip.Count > 0 ? _field.Direction(CabClip.Positions[0].X - _o.X, CabClip.Positions[0].Z - _o.Z) : default)}; " + $"{_ticks * 1e6 / System.Diagnostics.Stopwatch.Frequency / _frame:0} us a frame, {_field.Active} cells disturbed, {_bursts.Count} bursts, {_fires.Count} fires ({_fires.Count(f => f.Flames is { Emitting: true })} burning), {_rounds} rounds into plants, {_blasts} blasts");
    }

    private void Expire()
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
            if (_t >= _bursts[i].Until) { _bursts[i].P.QueueFree(); _bursts.RemoveAt(i); }
    }

    /// <summary>
    /// The cars (CabClip's, this frame) knock the plants down: every plant whose crown the car's footprint reaches starts to
    /// fall at once, along the car's way and out to the side it was struck on, over <see cref="BrushKnock.Seconds"/> (a tenth
    /// to a fifth of a second at speed, slower and less far for a nudge), with a knock at the plant (<see cref="Knock"/>)
    /// and, for a shrub at speed, its leaves thrown; the track behind lays the rest down the same way.
    /// </summary>
    private void Cars()
    {
        for (int i = 0; i < CabClip.Count; i++)
        {
            var p = CabClip.Positions[i];
            ulong id = CabClip.Ids[i];
            if (!_cars.TryGetValue(id, out var prev)) { _cars[id] = (p, 0); continue; }
            var move = p - prev.P;
            move.Y = 0;
            float len = move.Length();
            _cars[id] = (p, prev.Leaves);
            if (_dt <= 0 || len < 0.5f * _dt || len > 60f * _dt) continue;
            // on the ground: a car in the air passes over
            if (Veg.GroundY(p) is not { } gy || p.Y - gy > 2.2f) continue;
            var fwd = new System.Numerics.Vector2(move.X, move.Z) / len;
            var side = new System.Numerics.Vector2(-fwd.Y, fwd.X);
            float speed = len / _dt;
            float secs = BrushKnock.Seconds(speed), depth = BrushKnock.Depth(speed);
            // the depth is the bend drawn; the field holds the laid value the shader draws it from
            float lay = depth >= 1 ? 1f : BrushKnock.LayFor(depth);
            var half = CabClip.Halves[i];
            float hw = MathF.Max(half.X, 0.6f), hl = MathF.Max(half.Y, 1.2f);
            // the plants the footprint reaches this frame (the box swept from where it was)
            Veg.PlantsNear(p - move * 0.5f, hl + len * 0.5f + 0.5f, _near, 48);
            foreach (var s in _near)
            {
                var r = G(s.Root);
                float fx = r.X - _o.X, fz = r.Z - _o.Z;
                var d = new System.Numerics.Vector2(r.X - p.X, r.Z - p.Z);
                float a = System.Numerics.Vector2.Dot(d, fwd), b = System.Numerics.Vector2.Dot(d, side);
                float ox = MathF.Max(0, a - hl) + MathF.Max(0, -hl - len - a), oz = MathF.Max(0, MathF.Abs(b) - hw);
                float reach = s.Radius * 0.75f;
                if (ox * ox + oz * oz > reach * reach) continue;
                // beyond the field the plants are not bent: nothing to knock (a far car's knock would never end)
                if (r.Y > p.Y + 1.5f || r.Y + s.Height < p.Y - 1.5f || !_field.Covers(fx, fz)) continue;
                bool standing = !_field.Falling(fx, fz) && _field.Lay(fx, fz) < 0.3f;
                // along the way, pushed out to the side it was struck on (the plants under the middle mostly straight on)
                float out_ = Math.Clamp(b / hw, -1f, 1f) * 0.8f;
                var way = System.Numerics.Vector2.Normalize(fwd + side * out_);
                _field.Stamp(fx, fz, 0.75f, lay, 0.4f, 0, way, 0, secs);
                if (!standing) continue;
                if (s.Height >= 0.35f) Knock(id, r, s, speed);
                if (speed > 4f && s.Height >= 0.5f && _t - _cars[id].Leaves > 0.2)
                {
                    var dir = new Vector3(fwd.X, 0, fwd.Y);
                    Leaves(r + Vector3.Up * s.Height * 0.5f, (dir * 0.8f + Vector3.Up).Normalized(), s.Ground, 10, MathF.Min(s.Radius * 0.5f, 0.6f), 2);
                    _cars[id] = (p, _t);
                }
            }
            if (Trace && i == 0 && Engine.GetFramesDrawn() % 2 == 0)
                GD.Print(System.FormattableString.Invariant($"brush-trace: t {_t:0.000} speed {speed:0.0} car {p.X:0.00},{p.Z:0.00} plants {_near.Count}"));
            int steps = Math.Max(1, (int)MathF.Ceiling(len / 0.75f));
            for (int k = 1; k <= steps; k++)
            {
                var q = prev.P + move * (k / (float)steps);
                _field.Stamp(q.X - _o.X, q.Z - _o.Z, 1.9f, lay, 0.4f, 0, fwd, 0.8f, secs);
            }
        }
        if (_cars.Count > CabClip.Max * 2) _cars.Clear();
    }

    // ---- the knock's sound ----

    /// <summary>The game's sounds (Main sets it with the mission's bank): the knock is the original's own for a car running a small prop down.</summary>
    public static Assets.AudioBank? Audio { get; set; }
    /// <summary>
    /// The original's sound for a car knocking a sign or post off (class-4 objects: the short knock vnvco3, once per object):
    /// its nearest to a car going through a bush, pitched up, quieter and started past its first hit so it does not sound as the signs
    /// (BrushKnock.DefaultSample; [port] bush_knock picks another of BrushKnock.Variants).
    /// Its own voice name, so it never takes the signs' voice.
    /// </summary>
    private const string KnockVoice = "brush-knock";
    private const int KnockVoices = 4;
    private readonly KnockSounds _knocks = new();
    /// <summary>I76_BRUSH_TRACE=1: a line for each knock (the plant, the car's speed, the fall's time) and the car every other frame.</summary>
    private static readonly bool Trace = System.Environment.GetEnvironmentVariable("I76_BRUSH_TRACE") == "1";

    /// <summary>A knock at the plant's root (positional, so it passes by), pitched and levelled for the plant and the car's speed, rate-limited.</summary>
    private void Knock(ulong car, Vector3 root, PlantSpot s, float speed)
    {
        if (Trace) GD.Print(System.FormattableString.Invariant($"brush-trace: t {_t:0.000} frame {Engine.GetFramesDrawn()} knock at {root.X:0.00},{root.Z:0.00} height {s.Height:0.00} radius {s.Radius:0.00} speed {speed:0.0} fall {BrushKnock.Seconds(speed):0.000} s depth {BrushKnock.Depth(speed):0.00}"));
        if (Audio is null || BrushKnock.Silent || !_knocks.Try(car, _t) || KnockStream(Audio, out float from) is not { } stream) return;
        var (pitch, db) = BrushKnock.Voice(speed, s.Height, root.X - _o.X, root.Z - _o.Z);
        var v = Voice3D.OneShot(stream, KnockVoice, (50, KnockVoices), I76.Sim.Sound.Ds3d.MinDistance, db);
        v.PitchScale = pitch;
        // placed before it enters the tree: the voice's range check runs as it does
        v.Position = ToLocal(root + Vector3.Up * MathF.Min(s.Height * 0.5f, 0.8f));
        AddChild(v);
        v.Start(from);
        if (v.Playing) Voice3D.Trace(System.FormattableString.Invariant($"start: brush knock {BrushKnock.Sample} pitch {pitch:0.00} {db:0.0} dB from {BrushKnock.StartPercent:0} % at {root.X:0.0},{root.Y:0.0},{root.Z:0.0}, speed {speed:0.0} m/s"));
    }

    /// <summary>The knock's sounds as played, by sound, start, length and fade-in: the bank's stream cut to <see cref="BrushKnock.Span"/> and faded.</summary>
    private static readonly Dictionary<string, AudioStream?> Trimmed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The knock's stream (<see cref="BrushKnock.Sample"/> from the bank, HiFi Audio's when it has one), started
    /// <see cref="BrushKnock.StartPercent"/> in (at the nearest zero crossing), faded in over <see cref="BrushKnock.FadeInMs"/> and cut
    /// at <see cref="BrushKnock.LengthMs"/> (<see cref="BrushKnock.Trim"/>): the original's PCM, or a pack's compressed sound decoded
    /// once (<see cref="Decode"/>). Only a pack's sound that cannot be decoded is played from the same share of its length
    /// (<paramref name="from"/>), uncut and without the fade.
    /// </summary>
    private static AudioStream? KnockStream(Assets.AudioBank bank, out float from)
    {
        from = 0;
        var whole = bank.Get(BrushKnock.Sample);
        if (whole is null || !BrushKnock.Trims) return whole;
        var key = System.FormattableString.Invariant($"{BrushKnock.Sample}#{BrushKnock.StartPercent}#{BrushKnock.LengthMs}#{BrushKnock.FadeInMs}#{whole.GetInstanceId()}");
        if (!Trimmed.TryGetValue(key, out var s))
        {
            if (Trimmed.Count > 32) Trimmed.Clear();
            var pcm = whole is AudioStreamWav ? I76.Formats.Audio.SoundFiles.Read(bank.Vfs, BrushKnock.Sample) : Decode(whole);
            s = Trimmed[key] = pcm is null ? null : Assets.AudioBank.ToStream(BrushKnock.Trim(pcm));
        }
        if (s is not null) return s;
        from = BrushKnock.StartSeconds(whole.GetLength());
        return whole;
    }

    /// <summary>
    /// A short compressed stream (a pack's Ogg, at most 10 s) decoded to 16-bit stereo PCM at the mix rate, through its own playback
    /// (AudioStreamPlayback.MixAudio, nothing heard), so the knock's trim and fades apply to it as to the original's; null if it gives nothing.
    /// </summary>
    internal static I76.Formats.Audio.Pcm? Decode(AudioStream stream)
    {
        int rate = (int)AudioServer.GetMixRate();
        double seconds = stream.GetLength();
        if (rate <= 0 || seconds <= 0 || seconds > 10 || stream.InstantiatePlayback() is not { } playback) return null;
        int want = (int)Math.Ceiling(seconds * rate);
        var data = new byte[want * 4];
        int got = 0;
        playback.Start(0);
        while (got < want)
        {
            var mixed = playback.MixAudio(1f, Math.Min(4096, want - got));
            if (mixed.Length == 0) break;
            foreach (var f in mixed)
            {
                if (got >= want) break;
                short l = (short)Math.Clamp((int)MathF.Round(f.X * 32767f), short.MinValue, short.MaxValue);
                short r = (short)Math.Clamp((int)MathF.Round(f.Y * 32767f), short.MinValue, short.MaxValue);
                data[got * 4] = (byte)l; data[got * 4 + 1] = (byte)(l >> 8); data[got * 4 + 2] = (byte)r; data[got * 4 + 3] = (byte)(r >> 8);
                got++;
            }
        }
        playback.Stop();
        return got == 0 ? null : new I76.Formats.Audio.Pcm(2, rate, 16, got == want ? data : data[..(got * 4)]);
    }

    // ---- fire ----

    private void Ignite(Vector3 at, float radius, float flameChance)
    {
        if (_fires.Count >= MaxFires) return;
        Veg.PlantsNear(at, radius, _near, 24);
        PlantSpot? pick = null;
        float bd = float.MaxValue;
        foreach (var s in _near)
        {
            if (!s.Burns || s.Height < 0.25f) continue;
            var r = G(s.Root);
            if (_field.Scorch(r.X - _o.X, r.Z - _o.Z) > 0.6f || _fires.Any(f => f.At.DistanceSquaredTo(r) < 1f)) continue;
            float d = r.DistanceSquaredTo(at);
            if (d < bd) { bd = d; pick = s; }
        }
        if (pick is not { } p) return;
        var f = new Fire { At = G(p.Root), Radius = MathF.Min(p.Radius, 1.5f), Height = MathF.Min(p.Height, 2.5f), Start = _t, Life = _rng.RandfRange(7f, 13f) };
        bool flames = _fires.Count(x => x.Flames is not null) < MaxFlaming && _rng.Randf() < flameChance;
        if (flames) { f.FlameFrom = _rng.RandfRange(0.3f, 1.2f); f.FlameTo = f.FlameFrom + _rng.RandfRange(3f, 6f); }
        f.Smoke = Emitter(_smokeProc, _smokeMesh, 26, 3.2, f.At + Vector3.Up * f.Height * 0.5f, f.Radius * 0.6f);
        if (flames)
        {
            f.Flames = Emitter(_flameProc, _flameMesh, 48, 0.7, f.At + Vector3.Up * f.Height * 0.35f, f.Radius * 0.7f);
            f.Flames.Emitting = false;
            f.Light = new OmniLight3D { LightColor = new Color(1f, 0.55f, 0.22f), LightEnergy = 0, OmniRange = 7f, ShadowEnabled = false };
            AddChild(f.Light);
            f.Light.GlobalPosition = f.At + Vector3.Up * (f.Height * 0.6f + 0.3f);
        }
        _fires.Add(f);
    }

    private GpuParticles3D Emitter(ParticleProcessMaterial proc, Mesh mesh, int amount, double life, Vector3 at, float radius)
    {
        // a fire's own copy of the process: its plant's reach
        var own = (ParticleProcessMaterial)proc.Duplicate();
        own.EmissionSphereRadius = MathF.Max(radius, 0.3f);
        var p = new GpuParticles3D
        {
            ProcessMaterial = own, DrawPass1 = mesh, Amount = amount, Lifetime = life, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-6, -2, -6), new Vector3(12, 14, 12)), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.GlobalPosition = at;
        p.Emitting = true;
        return p;
    }

    private void Fires()
    {
        for (int i = _fires.Count - 1; i >= 0; i--)
        {
            var f = _fires[i];
            f.At = f.Smoke.GlobalPosition - Vector3.Up * f.Height * 0.5f;   // after a rebase too
            double age = _t - f.Start;
            if (age >= f.Life + 3.5) { End(f, now: true); _fires.RemoveAt(i); continue; }
            if (age >= f.Life && !f.Ending) End(f, now: false);
            bool burning = f.Flames is not null && age >= f.FlameFrom && age < f.FlameTo;
            if (f.Flames is not null && f.Flames.Emitting != burning) f.Flames.Emitting = burning;
            if (f.Light is not null)
            {
                float fl = burning ? Mathf.Clamp((float)(age - f.FlameFrom) * 2f, 0, 1) * Mathf.Clamp((float)(f.FlameTo - age), 0, 1) : 0;
                f.Light.LightEnergy = fl * 1.8f * ModernFx.FxBright * (0.75f + 0.25f * MathF.Sin((float)_t * 23f + i) * MathF.Sin((float)_t * 7f));
            }
            // the plant scorches as it smoulders, burnt black and slumped where it burns
            float scorch = burning ? Mathf.Clamp((float)(age - f.FlameFrom) / 2.5f, 0.3f, 1f) : Mathf.Clamp((float)age / 6f, 0f, 0.35f);
            _field.Stamp(f.At.X - _o.X, f.At.Z - _o.Z, f.Radius + 0.6f, burning ? 0.35f : 0, 0, scorch);
            if (burning) Ignite(f.At, 2.5f, 0f);   // a smoulder may catch beside it (no flames: capped)
        }
    }

    private void End(Fire f, bool now)
    {
        f.Ending = true;
        if (now)
        {
            f.Smoke.QueueFree(); f.Flames?.QueueFree(); f.Light?.QueueFree();
            return;
        }
        f.Smoke.Emitting = false;
        if (f.Flames is not null) f.Flames.Emitting = false;
        if (f.Light is not null) f.Light.LightEnergy = 0;
    }

    // ---- leaves ----

    /// <summary>A burst of leaves (or straw off grass) and a few twigs sprayed along <paramref name="up"/>.</summary>
    private void Leaves(Vector3 at, Vector3 up, bool grass, int count, float spread, int twigs, bool force = false)
    {
        if (!force && _burstsThisFrame >= BurstsPerFrame) return;
        if (_bursts.Count >= MaxBursts) return;
        _burstsThisFrame++;
        if (_meshSize != ModernFx.FxSize)
        {
            _meshSize = ModernFx.FxSize;
            _leafMesh.Size = new Vector2(0.14f, 0.09f) * _meshSize;
            _twigMesh.Size = new Vector3(0.016f, 0.2f, 0.016f) * _meshSize;
        }
        Burst(grass ? _strawProc : _leafProc, _leafMesh, count, 1.5, at + up * spread * 0.3f, up);
        if (twigs > 0 && !grass) Burst(_twigProc, _twigMesh, twigs, 1.2, at, up);
    }

    private void Burst(ParticleProcessMaterial proc, Mesh mesh, int amount, double life, Vector3 at, Vector3 up)
    {
        var p = new GpuParticles3D
        {
            ProcessMaterial = proc, DrawPass1 = mesh, Amount = amount, Lifetime = life, OneShot = true, Explosiveness = 0.9f,
            LocalCoords = false, VisibilityAabb = new Aabb(new Vector3(-6, -6, -6), new Vector3(12, 12, 12)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(p);
        p.GlobalTransform = new Transform3D(Facing(up), at);
        p.Emitting = true;
        _bursts.Add((p, _t + life + 0.2));
    }

    private static Vector3 G(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static Basis Facing(Vector3 up)
    {
        var y = up.LengthSquared() > 1e-6f ? up.Normalized() : Vector3.Up;
        var x = Mathf.Abs(y.Dot(Vector3.Forward)) < 0.9f ? y.Cross(Vector3.Forward).Normalized() : y.Cross(Vector3.Right).Normalized();
        return new Basis(x, y, x.Cross(y));
    }

    private static GradientTexture1D Ramp(params (float At, Color C)[] stops) =>
        new() { Gradient = new Gradient { Offsets = [.. stops.Select(s => s.At)], Colors = [.. stops.Select(s => s.C)] } };

    private void MakeLook()
    {
        // the leaves: small lit cards, both sides, coloured per leaf from the plants' palette and the mission's tint; they thin out by a dither as they die
        _leafMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode cull_disabled;
                    global uniform float i76_fx_bright;
                    global uniform float i76_fx_alpha;
                    uniform vec3 tint = vec3(1.0);
                    void fragment() {
                        vec2 d = UV * 2.0 - 1.0;
                        if (d.x * d.x + d.y * d.y * 0.55 > 1.0) discard;
                        float n = fract(sin(dot(FRAGCOORD.xy, vec2(12.9898, 78.233))) * 43758.5453);
                        if (COLOR.a * min(i76_fx_alpha, 1.0) < n) discard;
                        if (!FRONT_FACING) NORMAL = -NORMAL;
                        ALBEDO = COLOR.rgb * tint * mix(1.0, i76_fx_bright, 0.5);
                        ROUGHNESS = 0.8;
                        BACKLIGHT = vec3(0.2);
                    }
                    """,
            },
        };
        _leafMat.SetShaderParameter("tint", new Vector3(Tint.R, Tint.G, Tint.B));
        _leafMesh = new QuadMesh { Size = new Vector2(0.14f, 0.09f), Material = _leafMat };
        _twigMesh = new BoxMesh { Size = new Vector3(0.016f, 0.2f, 0.016f), Material = _leafMat };
        var fade = Ramp((0, Colors.White), (0.6f, Colors.White), (1, new Color(1, 1, 1, 0)));
        ParticleProcessMaterial Bits(float vmin, float vmax, float gravity, (float, Color)[] palette, bool align)
        {
            var m = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 60, InitialVelocityMin = vmin, InitialVelocityMax = vmax,
                Gravity = new Vector3(0, -gravity, 0), DampingMin = 1.5f, DampingMax = 3.5f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.35f,
                AngleMin = -180, AngleMax = 180, AngularVelocityMin = -540, AngularVelocityMax = 540,
                ColorInitialRamp = Ramp(palette), ColorRamp = fade,
                ScaleMin = 0.7f, ScaleMax = 1.4f,
            };
            if (align) m.SetParticleFlag(ParticleProcessMaterial.ParticleFlags.AlignYToVelocity, true);
            else m.SetParticleFlag(ParticleProcessMaterial.ParticleFlags.RotateY, true);
            return m;
        }
        _leafProc = Bits(1.5f, 4.5f, 4f, [(0, new Color(0.30f, 0.34f, 0.16f)), (0.35f, new Color(0.42f, 0.46f, 0.30f)), (0.7f, new Color(0.56f, 0.50f, 0.30f)), (1, new Color(0.40f, 0.30f, 0.18f))], false);
        _strawProc = Bits(1.2f, 3.5f, 4.5f, [(0, new Color(0.72f, 0.64f, 0.42f)), (0.5f, new Color(0.58f, 0.52f, 0.34f)), (1, new Color(0.46f, 0.47f, 0.32f))], false);
        _twigProc = Bits(2f, 5f, 9f, [(0, new Color(0.30f, 0.22f, 0.15f)), (1, new Color(0.45f, 0.38f, 0.30f))], true);

        // smoke off a smouldering plant: soft grey puffs rising and spreading (as ModernFx's dust, darker)
        var soft = new GradientTexture2D
        {
            Gradient = new Gradient { Offsets = [0f, 0.55f, 1f], Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.45f), new Color(1, 1, 1, 0)] },
            Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 64, Height = 64,
        };
        var smokeMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;
                    global uniform float i76_fx_alpha;
                    uniform sampler2D soft : filter_linear;
                    void vertex() {
                        mat4 m = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
                        float s = length(MODEL_MATRIX[0].xyz);
                        MODELVIEW_MATRIX = VIEW_MATRIX * m * mat4(vec4(s, 0, 0, 0), vec4(0, s, 0, 0), vec4(0, 0, s, 0), vec4(0, 0, 0, 1));
                    }
                    void fragment() {
                        float a = texture(soft, UV).a * COLOR.a * min(i76_fx_alpha, 1.0);
                        ALBEDO = COLOR.rgb;
                        ALPHA = a;
                    }
                    """,
            },
        };
        smokeMat.SetShaderParameter("soft", soft);
        _smokeMesh = new QuadMesh { Size = Vector2.One, Material = smokeMat };
        _smokeProc = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 12, InitialVelocityMin = 0.8f, InitialVelocityMax = 1.6f, Gravity = new Vector3(0.25f, 0.35f, 0),
            DampingMin = 0.2f, DampingMax = 0.5f, EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1f,
            ScaleMin = 0.9f, ScaleMax = 1.5f, ScaleCurve = new CurveTexture { Curve = Curve((0, 0.5f), (1, 2.6f)) },
            ColorRamp = Ramp((0, new Color(0.26f, 0.25f, 0.24f, 0)), (0.12f, new Color(0.27f, 0.26f, 0.25f, 0.45f)), (0.6f, new Color(0.36f, 0.35f, 0.34f, 0.25f)), (1, new Color(0.42f, 0.42f, 0.42f, 0))),
        };
        // flames: additive soft cards licking up, yellow to red
        var flameMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type spatial;
                    render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;
                    global uniform float i76_fx_alpha;
                    global uniform float i76_fx_bright;
                    uniform sampler2D soft : filter_linear;
                    void vertex() {
                        mat4 m = mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
                        float s = length(MODEL_MATRIX[0].xyz);
                        MODELVIEW_MATRIX = VIEW_MATRIX * m * mat4(vec4(s, 0, 0, 0), vec4(0, s * 1.6, 0, 0), vec4(0, 0, s, 0), vec4(0, 0, 0, 1));
                    }
                    void fragment() {
                        float a = texture(soft, UV).a;
                        ALBEDO = COLOR.rgb * a * a * COLOR.a * 2.2 * i76_fx_bright * min(i76_fx_alpha, 1.0);
                    }
                    """,
            },
        };
        flameMat.SetShaderParameter("soft", soft);
        _flameMesh = new QuadMesh { Size = Vector2.One * 0.45f, Material = flameMat };
        _flameProc = new ParticleProcessMaterial
        {
            Direction = Vector3.Up, Spread = 10, InitialVelocityMin = 1.2f, InitialVelocityMax = 2.6f, Gravity = new Vector3(0, 1.5f, 0),
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1f,
            ScaleMin = 0.8f, ScaleMax = 1.4f, ScaleCurve = new CurveTexture { Curve = Curve((0, 1f), (1, 0.2f)) },
            ColorRamp = Ramp((0, new Color(1f, 0.85f, 0.45f, 1)), (0.4f, new Color(1f, 0.5f, 0.15f, 0.9f)), (1, new Color(0.6f, 0.15f, 0.05f, 0))),
        };
    }

    private static Curve Curve(params (float X, float Y)[] pts)
    {
        var c = new Curve { MaxValue = 4f };
        foreach (var (x, y) in pts) c.AddPoint(new Vector2(x, y));
        return c;
    }
}
