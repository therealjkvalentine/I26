using Godot;

namespace I76.Game.World;

/// <summary>A column of hot air: its foot, its radius there, the flames' top over the foot, the column's height, and how strong (0..1).</summary>
public readonly record struct HeatSource(Vector3 Base, float Radius, float FlameTop, float Height, float Strength);

/// <summary>
/// The fires Heat Shimmer bends the air over: the burning scenery wrecks (StaticFire) and cars (WreckFire) register themselves with a
/// function saying where and how hot they are now (null: not burning, or hidden); the flame streams add their points frame by frame.
/// Nothing is kept or asked for while the setting is off (<see cref="Wanted"/>).
/// </summary>
public static class HeatSources
{
    private static readonly List<(Node3D Node, Func<HeatSource?> Now)> Kept = [];
    private static readonly List<HeatSource> Points = [];

    /// <summary>Heat Shimmer draws fires: the streams' points are worth adding.</summary>
    public static bool Wanted => HeatShimmer.FiresOn;

    public static void Add(Node3D node, Func<HeatSource?> now) => Kept.Add((node, now));

    /// <summary>A source for this frame only (a flame stream's point).</summary>
    public static void Point(HeatSource s) { if (Wanted && Points.Count < 64) Points.Add(s); }

    public static void DropPoints() => Points.Clear();

    /// <summary>This frame's sources (the kept ones burning, the frame's points), the frame's points then dropped.</summary>
    public static void Collect(List<HeatSource> into)
    {
        into.Clear();
        for (int i = Kept.Count - 1; i >= 0; i--)
        {
            var (n, now) = Kept[i];
            if (!GodotObject.IsInstanceValid(n)) { Kept.RemoveAt(i); continue; }
            if (!n.IsInsideTree() || !n.IsVisibleInTree()) continue;
            if (now() is { Strength: > 0.01f } s) into.Add(s);
        }
        into.AddRange(Points);
        Points.Clear();
    }
}

/// <summary>
/// Graphic Detail's Heat Shimmer ([port] heat_shimmer, HeatShimmerRow), the modern look's, not the original's: the air over a fire bends
/// what is behind it, and on a hot day (HotDay, Fires + Road) the air low over the far ground does too, with the road's mirage. One compute
/// pass after the scene's transparent pass (a CompositorEffect on the main camera, beside the smoke layer's), over the screen's rectangle
/// that holds something to bend: the colour there copied aside, then each pixel read back from a place nudged by scrolling noise.
/// <list type="bullet">
/// <item>Fires: each source a column rising from its flames; the ray's nearest point to the column's axis says how deep in it the pixel
/// looks. The nudge is an angle (the same look at any resolution or render scale), fading out from 40 to 180 m, and only where the
/// scene lies behind the column: the cockpit, a car in front, anything nearer is never bent, nor pulled into the bend.</item>
/// <item>Road: by a hot day only, ground seen at a grazing angle (its normal from the depth) and level, from 150 m out, gets a fine
/// vertical ripple; under about a degree and from 300 m, the mirage: the sky (or whatever lies farther) at the mirrored angle shows
/// through, wavering.</item>
/// </list>
/// The 3D view's own pass: the 2D dash and HUD are drawn after it, the mirrors and the weapon camera are other cameras and stay
/// unbent. Forward+ only (Compatibility: none). Off, nothing is gathered and the effect is not in the frame.
/// I76_HEAT_STATS=1 prints the sources and the rectangle every 2 s.
/// </summary>
public partial class HeatShimmer : Node3D
{
    /// <summary>The setting (HeatShimmerRow: 0 Off, 1 Fires, 2 Fires + Road).</summary>
    public static int Setting { get; private set; }
    public static bool Look1997 { get; set; }
    /// <summary>The mission is a hot day (HotDay).</summary>
    public static bool HotDay { get; private set; }
    public static bool FiresOn => Setting >= 1 && !Look1997 && _supported;
    public static bool RoadOn => Setting >= 2 && !Look1997 && HotDay && _supported;

    /// <summary>Road shimmer and mirage strengths (I76_HEAT_ROAD, I76_HEAT_MIRAGE, 0..2; 1 the shipped).</summary>
    private static readonly float RoadGain = Env("I76_HEAT_ROAD", 1f), MirageGain = Env("I76_HEAT_MIRAGE", 1f), FireGain = Env("I76_HEAT_FIRE", 1f);
    private static readonly bool Stats = System.Environment.GetEnvironmentVariable("I76_HEAT_STATS") == "1";
    /// <summary>I76_HEAT_DEBUG=1: the bend painted (red: how far a pixel is nudged, 4 px full; green: the mirage), to tune and check the masks.</summary>
    private static readonly bool Debug = System.Environment.GetEnvironmentVariable("I76_HEAT_DEBUG") is "1" or "2";
    private static readonly bool Debug2 = System.Environment.GetEnvironmentVariable("I76_HEAT_DEBUG") == "2";
    /// <summary>I76_HEAT_HOT=1|0 forces the hot day on or off (captures).</summary>
    private static readonly string? ForceHot = System.Environment.GetEnvironmentVariable("I76_HEAT_HOT");

    private static float Env(string name, float d) =>
        float.TryParse(System.Environment.GetEnvironmentVariable(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? Math.Clamp(v, 0f, 4f) : d;

    public static void Apply(int setting, bool look1997) { Setting = setting; Look1997 = look1997; }

    public static void SetMission(I76.Formats.Mission.LightPreset light, I76.Formats.Imaging.Palette? palette)
    {
        HotDay = ForceHot is "1" ? true : ForceHot is "0" ? false : World.HotDay.Is(light, palette);
        GD.Print($"heat shimmer: {(HotDay ? "a hot day" : "not a hot day")} (sun {I76.Game.Sim.SmokeLight.Elevation(light):0} deg, {(light.IsDay ? "day" : "night")}, palette warmth {(palette is null ? "none" : World.HotDay.Warmth(palette.Rgb).ToString("0.000"))})");
    }

    private const int MaxSources = 16, UboFloats = 68 + MaxSources * 8;
    private const float FireFar = 180f, RoadNear = 150f;
    private static bool _supported = true;
    private readonly ShimmerEffect _effect = new();
    private readonly List<HeatSource> _all = [];
    private double _time;

    // handed to the render thread under the lock
    private static readonly object Lock = new();
    private static readonly float[] Src = new float[MaxSources * 8];
    private static int _count;
    private static Rect2 _rect;   // normalised to the view
    private static float _road, _mirage, _clock;
    private static Vector3 _right;

    public override void _Ready()
    {
        if (RenderingServer.GetCurrentRenderingMethod() != "forward_plus" || RenderingServer.GetRenderingDevice() is null) _supported = false;
    }

    public override void _ExitTree()
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam?.Compositor is { } c && c.CompositorEffects.Contains(_effect))
        {
            var list = c.CompositorEffects; list.Remove(_effect); c.CompositorEffects = list;
        }
    }

    public override void _Process(double delta)
    {
        bool fires = FiresOn, road = RoadOn;
        if (!fires && !road) { if (_effect.Enabled) _effect.Enabled = false; HeatSources.DropPoints(); return; }
        _time += delta;
        var cam = GetViewport().GetCamera3D();
        if (cam is null) { _effect.Enabled = false; return; }
        Attach(cam);
        var view = GetViewport().GetVisibleRect().Size;
        var eye = cam.GlobalPosition;
        HeatSources.Collect(_all);
        // the nearest in front within reach
        _all.RemoveAll(s => s.Base.DistanceTo(eye) > FireFar + s.Height || cam.IsPositionBehind(s.Base + Vector3.Up * s.Height * 0.5f) && s.Base.DistanceTo(eye) > s.Height + s.Radius * 3f);
        _all.Sort((a, b) => a.Base.DistanceSquaredTo(eye).CompareTo(b.Base.DistanceSquaredTo(eye)));
        int n = Math.Min(_all.Count, MaxSources);
        Rect2? rect = null;
        for (int i = 0; i < n; i++) rect = Union(rect, ColumnRect(cam, view, _all[i]));
        if (road) rect = Union(rect, BandRect(cam, view));
        if (rect is not { } r || r.Size.X <= 0 || r.Size.Y <= 0) { _effect.Enabled = false; return; }
        lock (Lock)
        {
            for (int i = 0; i < n; i++)
            {
                var s = _all[i];
                int o = i * 4;
                Src[o] = s.Base.X; Src[o + 1] = s.Base.Y; Src[o + 2] = s.Base.Z; Src[o + 3] = s.Radius;
                o = MaxSources * 4 + i * 4;
                Src[o] = s.FlameTop; Src[o + 1] = s.Height; Src[o + 2] = Math.Clamp(s.Strength, 0f, 1.5f) * FireGain; Src[o + 3] = (i * 0.618034f) % 1f;
            }
            _count = n;
            _rect = r;
            _road = road ? RoadGain : 0f;
            _mirage = road ? MirageGain : 0f;
            _clock = (float)(_time % 4096.0);
            _right = cam.GlobalTransform.Basis.X;
        }
        _effect.Enabled = true;
        if (Stats) Report(n, r);
    }

    private ulong _statFrom;
    private void Report(int n, Rect2 r)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _statFrom < 2000) return;
        _statFrom = now;
        GD.Print($"heat shimmer: {n} sources (of {_all.Count}), road {(RoadOn ? "on" : "off")}, rect {r.Position.X:0.00},{r.Position.Y:0.00} {r.Size.X:0.00}x{r.Size.Y:0.00}");
    }

    /// <summary>The effect in the camera's compositor, beside what is there (the smoke layer's); a camera without one gets one.</summary>
    private void Attach(Camera3D cam)
    {
        // as last frame: no array copied out of the engine, but looked at again each second (another effect may have set the list)
        if (cam == _attachedCam && cam.Compositor == _attachedTo && _attachedTo is not null && ++_attachAge % 60 != 0) return;
        if (cam.Compositor is null) cam.Compositor = new Compositor { CompositorEffects = [_effect] };
        else
        {
            var list = cam.Compositor.CompositorEffects;
            if (!list.Contains(_effect)) { list.Add(_effect); cam.Compositor.CompositorEffects = list; }
        }
        (_attachedCam, _attachedTo) = (cam, cam.Compositor);
    }

    private Camera3D? _attachedCam;
    private Compositor? _attachedTo;
    private int _attachAge;

    private static Rect2? Union(Rect2? a, Rect2? b) => a is null ? b : b is null ? a : a.Value.Merge(b.Value);

    /// <summary>The column's box on the view (normalised), the whole view when the eye is in or by it, none off the view.</summary>
    private static Rect2? ColumnRect(Camera3D cam, Vector2 view, HeatSource s)
    {
        float r = s.Radius * 1.9f;
        Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
        for (int k = 0; k < 8; k++)
        {
            var p = s.Base + new Vector3((k & 1) == 0 ? -r : r, (k & 2) == 0 ? 0 : s.Height, (k & 4) == 0 ? -r : r);
            if (cam.IsPositionBehind(p)) return new Rect2(0, 0, 1, 1);
            var q = cam.UnprojectPosition(p) / view;
            lo = lo.Min(q); hi = hi.Max(q);
        }
        lo = lo.Clamp(Vector2.Zero, Vector2.One); hi = hi.Clamp(Vector2.Zero, Vector2.One);
        return hi.X > lo.X && hi.Y > lo.Y ? new Rect2(lo, hi - lo) : null;
    }

    /// <summary>The band round the horizon the road shimmer and the mirage (and its mirrored sky) take: 2 degrees either side, across the view.</summary>
    private static Rect2? BandRect(Camera3D cam, Vector2 view)
    {
        var fwd = -cam.GlobalTransform.Basis.Z;
        float yaw = Mathf.Atan2(fwd.X, fwd.Z);
        float lo = float.MaxValue, hi = float.MinValue;
        for (int a = -8; a <= 8; a++)
            foreach (float el in (float[])[-2f, 2f])
            {
                float az = yaw + a * 0.2f, e = Mathf.DegToRad(el);
                var d = new Vector3(Mathf.Sin(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(az) * Mathf.Cos(e));
                var p = cam.GlobalPosition + d * 1000f;
                if (cam.IsPositionBehind(p)) continue;
                var q = cam.UnprojectPosition(p) / view;
                if (q.X < -1 || q.X > 2) continue;
                lo = Math.Min(lo, q.Y); hi = Math.Max(hi, q.Y);
            }
        lo = Math.Clamp(lo, 0f, 1f); hi = Math.Clamp(hi, 0f, 1f);
        return hi > lo ? new Rect2(0, lo, 1, hi - lo) : null;
    }

    // ---- render thread ----

    private sealed partial class ShimmerEffect : CompositorEffect
    {
        public ShimmerEffect()
        {
            EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
            AccessResolvedColor = true;
            AccessResolvedDepth = true;
            Enabled = false;
        }

        public override void _RenderCallback(int effectCallbackType, RenderData renderData)
        {
            if (_failed || effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent) return;
            try { Render(renderData); }
            catch (Exception e) { _failed = true; GD.PrintErr($"heat shimmer: {e}"); }
        }
    }

    private static bool _failed;
    private static Rid _copyShader, _copyPipeline, _copySet;
    private static Rid _shader, _pipeline, _linear, _nearest, _ubo, _tmp, _set, _setColor, _setDepth;
    private static Vector2I _tmpSize;
    private static long _tmpFormat = -1;
    private static readonly float[] Ubo = new float[UboFloats];
    private static readonly byte[] UboBytes = new byte[UboFloats * 4];
    private static Godot.Collections.Array<RDUniform>? _uniforms;

    private static void Render(RenderData renderData)
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd is null) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers || renderData.GetRenderSceneData() is not RenderSceneDataRD scene) return;
        if (buffers.GetViewCount() != 1) return;   // one view (no XR)
        var size = buffers.GetInternalSize();
        if (size.X <= 0 || size.Y <= 0) return;
        if (!_pipeline.IsValid && !Setup(rd)) return;
        var color = buffers.GetColorLayer(0, false);
        var depth = buffers.GetDepthLayer(0, false);
        if (!color.IsValid || !depth.IsValid) return;
        var fmt = rd.TextureGetFormat(color);
        if (fmt is null) return;
        if (fmt.Format != RenderingDevice.DataFormat.R16G16B16A16Sfloat) { _failed = true; GD.PrintErr($"heat shimmer: the scene's colour is {fmt.Format}, not RGBA16F; off"); return; }
        if (!_tmp.IsValid || _tmpSize != size || (long)fmt.Format != _tmpFormat) MakeTmp(rd, size, fmt.Format);

        Rect2 rn; int count; float road, mirage, clock; Vector3 right;
        lock (Lock)
        {
            rn = _rect; count = _count; road = _road; mirage = _mirage; clock = _clock; right = _right;
            Array.Copy(Src, 0, Ubo, 68, MaxSources * 8);
        }
        var camT = scene.GetCamTransform();
        var proj = scene.GetViewProjection(0);
        var view = new Projection(camT.AffineInverse());
        Write(proj.Inverse(), Ubo.AsSpan(0, 16));
        Write(new Projection(camT), Ubo.AsSpan(16, 16));
        Write(proj * view, Ubo.AsSpan(32, 16));
        // the pixels to bend, and round them the copy (as far as a nudge reaches)
        float focal = MathF.Abs(proj.Y.Y) * 0.5f * size.Y;
        float most = Math.Max(2f, size.Y * 0.012f);
        int margin = (int)MathF.Ceiling(most) + 3;
        int x0 = Math.Clamp((int)MathF.Floor(rn.Position.X * size.X), 0, size.X), y0 = Math.Clamp((int)MathF.Floor(rn.Position.Y * size.Y), 0, size.Y);
        int x1 = Math.Clamp((int)MathF.Ceiling(rn.End.X * size.X), 0, size.X), y1 = Math.Clamp((int)MathF.Ceiling(rn.End.Y * size.Y), 0, size.Y);
        if (x1 <= x0 || y1 <= y0) return;
        Ubo[48] = x0; Ubo[49] = y0; Ubo[50] = x1; Ubo[51] = y1;
        Ubo[52] = size.X; Ubo[53] = size.Y; Ubo[54] = 1f / size.X; Ubo[55] = 1f / size.Y;
        Ubo[56] = clock; Ubo[57] = count; Ubo[58] = road; Ubo[59] = mirage;
        Ubo[60] = SmokeDepthReversed(proj) ? 1f : -1f; Ubo[61] = focal; Ubo[62] = RoadNear; Ubo[63] = most;
        Ubo[64] = right.X; Ubo[65] = right.Y; Ubo[66] = right.Z; Ubo[67] = Debug2 ? 2f : Debug ? 1f : 0f;
        System.Buffer.BlockCopy(Ubo, 0, UboBytes, 0, UboFloats * 4);
        rd.BufferUpdate(_ubo, 0, UboFloats * 4, UboBytes);

        int cx0 = Math.Max(0, x0 - margin), cy0 = Math.Max(0, y0 - margin), cx1 = Math.Min(size.X, x1 + margin), cy1 = Math.Min(size.Y, y1 + margin);

        if (!_set.IsValid || !rd.UniformSetIsValid(_set) || !_copySet.IsValid || !rd.UniformSetIsValid(_copySet) || color != _setColor || depth != _setDepth)
        {
            if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
            if (_copySet.IsValid && rd.UniformSetIsValid(_copySet)) rd.FreeRid(_copySet);
            _setColor = color; _setDepth = depth;
            // the scene's colour cannot be copied from (no copy-from usage): a kernel copies the rectangle aside
            var cin = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 }; cin.AddId(color);
            var cout = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 }; cout.AddId(_tmp);
            _copySet = rd.UniformSetCreate([cin, cout], _copyShader, 0);
            var img = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 }; img.AddId(color);
            var src = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 1 }; src.AddId(_linear); src.AddId(_tmp);
            var dep = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 2 }; dep.AddId(_nearest); dep.AddId(depth);
            var ubo = new RDUniform { UniformType = RenderingDevice.UniformType.UniformBuffer, Binding = 3 }; ubo.AddId(_ubo);
            _uniforms = [img, src, dep, ubo];
            _set = rd.UniformSetCreate(_uniforms, _shader, 0);
        }
        if (!_set.IsValid || !_copySet.IsValid) { _failed = true; GD.PrintErr("heat shimmer: no uniform set (the scene's colour cannot be written by a kernel?); off"); return; }
        long cl = rd.ComputeListBegin();
        rd.ComputeListBindComputePipeline(cl, _copyPipeline);
        rd.ComputeListBindUniformSet(cl, _copySet, 0);
        Span<byte> rect = stackalloc byte[16];
        System.BitConverter.TryWriteBytes(rect, cx0); System.BitConverter.TryWriteBytes(rect[4..], cy0);
        System.BitConverter.TryWriteBytes(rect[8..], cx1); System.BitConverter.TryWriteBytes(rect[12..], cy1);
        rd.ComputeListSetPushConstant(cl, rect, 16);
        rd.ComputeListDispatch(cl, (uint)((cx1 - cx0 + 7) / 8), (uint)((cy1 - cy0 + 7) / 8), 1);
        rd.ComputeListAddBarrier(cl);
        rd.ComputeListBindComputePipeline(cl, _pipeline);
        rd.ComputeListBindUniformSet(cl, _set, 0);
        rd.ComputeListDispatch(cl, (uint)((x1 - x0 + 7) / 8), (uint)((y1 - y0 + 7) / 8), 1);
        rd.ComputeListEnd();
    }

    /// <summary>Whether a larger depth is nearer the eye (Godot's reversed z), as the smoke layer reads it.</summary>
    private static bool SmokeDepthReversed(Projection proj) => I76.Game.Sim.SmokeDepth.NearerIsLarger(proj);

    private static void MakeTmp(RenderingDevice rd, Vector2I size, RenderingDevice.DataFormat format)
    {
        if (_set.IsValid && rd.UniformSetIsValid(_set)) rd.FreeRid(_set);
        if (_copySet.IsValid && rd.UniformSetIsValid(_copySet)) rd.FreeRid(_copySet);
        _set = default; _copySet = default;
        if (_tmp.IsValid) rd.FreeRid(_tmp);
        _tmp = rd.TextureCreate(new RDTextureFormat
        {
            Width = (uint)size.X, Height = (uint)size.Y, Format = format,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.StorageBit,
        }, new RDTextureView());
        _tmpSize = size; _tmpFormat = (long)format;
    }

    private static bool Setup(RenderingDevice rd)
    {
        var spirv = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = Kernel });
        if (spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } err) { _failed = true; GD.PrintErr($"heat shimmer: the kernel did not compile: {err}"); return false; }
        _shader = rd.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) { _failed = true; GD.PrintErr("heat shimmer: the backend refused the kernel"); return false; }
        _pipeline = rd.ComputePipelineCreate(_shader);
        var copy = rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = CopyKernel });
        if (copy.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } cerr) { _failed = true; GD.PrintErr($"heat shimmer: the copy kernel did not compile: {cerr}"); return false; }
        _copyShader = rd.ShaderCreateFromSpirV(copy);
        if (!_copyShader.IsValid) { _failed = true; return false; }
        _copyPipeline = rd.ComputePipelineCreate(_copyShader);
        _linear = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Linear, MagFilter = RenderingDevice.SamplerFilter.Linear, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        _nearest = rd.SamplerCreate(new RDSamplerState { MinFilter = RenderingDevice.SamplerFilter.Nearest, MagFilter = RenderingDevice.SamplerFilter.Nearest, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge, RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge });
        _ubo = rd.UniformBufferCreate(UboFloats * 4);
        GD.Print("heat shimmer: kernel ready");
        return _pipeline.IsValid;
    }

    private static void Write(Projection p, Span<float> to)
    {
        to[0] = p.X.X; to[1] = p.X.Y; to[2] = p.X.Z; to[3] = p.X.W;
        to[4] = p.Y.X; to[5] = p.Y.Y; to[6] = p.Y.Z; to[7] = p.Y.W;
        to[8] = p.Z.X; to[9] = p.Z.Y; to[10] = p.Z.Z; to[11] = p.Z.W;
        to[12] = p.W.X; to[13] = p.W.Y; to[14] = p.W.Z; to[15] = p.W.W;
    }

    private const string CopyKernel = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(rgba16f, set = 0, binding = 0) uniform restrict readonly image2D color_in;
        layout(rgba16f, set = 0, binding = 1) uniform restrict writeonly image2D color_copy;
        layout(push_constant, std430) uniform P { ivec4 rect; } pc;
        void main() {
            ivec2 p = ivec2(gl_GlobalInvocationID.xy) + pc.rect.xy;
            if (p.x >= pc.rect.z || p.y >= pc.rect.w) return;
            imageStore(color_copy, p, imageLoad(color_in, p));
        }
        """;

    private const string Kernel = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(rgba16f, set = 0, binding = 0) uniform restrict writeonly image2D color_out;
        layout(set = 0, binding = 1) uniform sampler2D src;
        layout(set = 0, binding = 2) uniform sampler2D depth_tex;
        layout(set = 0, binding = 3, std140) uniform U {
            mat4 invproj; mat4 invview; mat4 viewproj;
            vec4 rect;    // the pixels to bend: x0, y0, x1, y1
            vec4 size;    // w, h, 1/w, 1/h
            vec4 misc;    // clock (s), sources, road, mirage
            vec4 misc2;   // reversed z (+1), focal length (px), the road's near edge (m), the most a nudge moves (px)
            vec4 cam_right;
            vec4 src_pos[16];   // foot xyz, radius
            vec4 src_par[16];   // flames' top, column height, strength, seed
        } u;

        float hash3(ivec3 p) {
            uvec3 v = uvec3(p) * 1664525u + 1013904223u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            v ^= v >> 16u;
            v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
            return float(v.x & 0xffffffu) * (1.0 / 16777215.0);
        }
        float vnoise(vec3 p) {
            ivec3 i = ivec3(floor(p)); vec3 f = fract(p); f = f * f * (3.0 - 2.0 * f);
            float a = mix(hash3(i), hash3(i + ivec3(1, 0, 0)), f.x), b = mix(hash3(i + ivec3(0, 1, 0)), hash3(i + ivec3(1, 1, 0)), f.x);
            float c = mix(hash3(i + ivec3(0, 0, 1)), hash3(i + ivec3(1, 0, 1)), f.x), d = mix(hash3(i + ivec3(0, 1, 1)), hash3(i + ivec3(1, 1, 1)), f.x);
            return mix(mix(a, b, f.y), mix(c, d, f.y), f.z);
        }
        vec3 view_at(vec2 uv, float d) {
            vec4 v = u.invproj * vec4(uv * 2.0 - 1.0, d, 1.0);
            return v.xyz / v.w;
        }
        bool is_sky(float d) { return u.misc2.x > 0.0 ? d <= 0.0 : d >= 1.0; }
        float dist_at(ivec2 p) {
            p = clamp(p, ivec2(0), ivec2(u.size.xy) - 1);
            float d = texelFetch(depth_tex, p, 0).r;
            return is_sky(d) ? 1e7 : length(view_at((vec2(p) + 0.5) * u.size.zw, d));
        }

        void main() {
            ivec2 p = ivec2(gl_GlobalInvocationID.xy) + ivec2(u.rect.xy);
            if (p.x >= int(u.rect.z) || p.y >= int(u.rect.w)) return;
            vec2 uv = (vec2(p) + 0.5) * u.size.zw;
            float dz = texelFetch(depth_tex, p, 0).r;
            bool sky = is_sky(dz);
            vec3 vpos = view_at(uv, dz);
            float dist = sky ? 1e7 : length(vpos);
            vec3 dirW = normalize(mat3(u.invview) * normalize(view_at(uv, 0.5)));
            vec3 cam = u.invview[3].xyz;
            float clock = u.misc.x;
            vec2 off = vec2(0.0);   // radians: x along the view's right, y up
            float t_near = 1e9;

            // fires: the columns this pixel looks through in front of the scene
            int n = int(u.misc.y);
            for (int i = 0; i < n; i++) {
                vec4 a = u.src_pos[i], b = u.src_par[i];
                vec2 o = cam.xz - a.xz, d2 = dirW.xz;
                float dd = dot(d2, d2);
                float t = dd > 1e-8 ? max(-dot(o, d2) / dd, 0.0) : 0.0;
                if (t >= dist || t < 1.0) continue;
                vec3 q = cam + dirW * t;
                float y = q.y - a.y, H = b.y, top = b.x;
                if (y <= 0.0 || y >= H) continue;
                float rad = a.w * (1.0 + 0.8 * y / H);
                float m = 1.0 - smoothstep(0.3 * rad, rad, length(q.xz - a.xz));
                m *= smoothstep(top * 0.35, top * 1.05, y) * (1.0 - smoothstep(H * 0.4, H, y));
                m *= b.z * (1.0 - smoothstep(40.0, 180.0, t)) * smoothstep(1.5, 4.0, t);
                if (m <= 0.002) continue;
                float s = dot(q - a.xyz, u.cam_right.xyz);
                vec3 nc = vec3(s * 2.6, (y - clock * 2.4) * 2.0, clock * 1.1 + b.w * 37.0);
                vec2 w = vec2(vnoise(nc), vnoise(nc + vec3(19.7, 7.3, 3.1))) * 2.0 - 1.0;
                w += 0.5 * (vec2(vnoise(nc * 2.3 + 5.0), vnoise(nc * 2.3 + 11.0)) * 2.0 - 1.0);
                off += w * m * 0.0024;
                t_near = min(t_near, t);
            }

            // a hot day: the air low over the ground bends what is seen through it from 150 m out (the far ground, the foot of a
            // mesa, the horizon's edge: rays within a degree of level), and level ground seen at a grazing angle shows the mirage
            float road = u.misc.z, mir = u.misc.w, mirage = 0.0;
            vec3 mcol = vec3(0.0);
            if (road > 0.0 || mir > 0.0) {
                float elev = abs(dirW.y);   // the sine of the ray's angle to level
                float far = smoothstep(u.misc2.z, u.misc2.z * 2.6, dist) * (1.0 + 0.6 * smoothstep(400.0, 2000.0, dist));
                float band = (1.0 - smoothstep(0.003, 0.016, elev)) * far;
                if (u.cam_right.w > 1.5) band = 0.0;
                if (band > 0.0 && road > 0.0) {
                    // fine wavelets, wide and flat, climbing: their size an angle, but never under two pixels
                    float kv = min(950.0, u.misc2.y / 2.0), kh = kv * 0.25;
                    vec3 a = vec3(dirW.x * kh + clock * 0.35, dirW.y * kv - clock * 4.0, dirW.z * kh);
                    float r1 = vnoise(a) * 2.0 - 1.0;
                    float r2 = vnoise(a * vec3(2.1, 2.3, 2.1) + vec3(3.7, -clock * 3.0, 1.9)) * 2.0 - 1.0;
                    float amp = max(0.0005, 0.9 / u.misc2.y);
                    off += vec2(0.35 * r2, r1 + 0.5 * r2) * band * road * amp;
                    t_near = min(t_near, u.misc2.z * 0.8);
                }
                if (mir > 0.0 && !sky && dist > u.misc2.z) {
                    // level ground at a grazing angle (its normal from the next texels' depths)
                    ivec2 px = min(p + ivec2(1, 0), ivec2(u.size.xy) - 1), py = min(p + ivec2(0, 1), ivec2(u.size.xy) - 1);
                    vec3 vx = view_at((vec2(px) + 0.5) * u.size.zw, texelFetch(depth_tex, px, 0).r);
                    vec3 vy = view_at((vec2(py) + 0.5) * u.size.zw, texelFetch(depth_tex, py, 0).r);
                    vec3 nW = normalize(mat3(u.invview) * cross(vx - vpos, vy - vpos));
                    float sin_inc = abs(dot(nW, dirW));
                    float level = smoothstep(0.95, 0.995, abs(nW.y));
                    float mw = (1.0 - smoothstep(0.006, 0.02, sin_inc)) * level * smoothstep(u.misc2.z, u.misc2.z * 2.0, dist);
                    if (u.cam_right.w > 1.5) { imageStore(color_out, p, vec4(1.0 - smoothstep(0.006, 0.02, sin_inc), level, smoothstep(u.misc2.z, u.misc2.z * 2.0, dist), 1.0)); return; }
                    if (mw > 0.0) {
                        // the ray bent back up off the hot layer: what lies at the mirrored angle, if it is farther than the ground
                        vec4 c = u.viewproj * vec4(dirW.x, -dirW.y, dirW.z, 0.0);
                        if (c.w > 0.0) {
                            vec2 muv = c.xy / c.w * 0.5 + 0.5;
                            if (all(greaterThan(muv, vec2(0.0))) && all(lessThan(muv, vec2(1.0))) && dist_at(ivec2(muv * u.size.xy)) > dist) {
                                float kv = min(700.0, u.misc2.y / 3.0);
                                float fl = vnoise(vec3(dirW.x * kv * 0.3 + clock * 0.3, dirW.y * kv - clock * 2.5, dirW.z * kv * 0.3 + 9.1));
                                mcol = texture(src, muv).rgb;
                                mirage = clamp(mw * mir * (0.4 + 0.4 * fl), 0.0, 1.0);
                            }
                        }
                    }
                }
            }
            if (off == vec2(0.0) && mirage <= 0.0) return;
            vec2 opx = vec2(off.x, -off.y) * u.misc2.y;
            float len = length(opx);
            if (len > u.misc2.w) opx *= u.misc2.w / len;
            vec2 suv = uv + opx * u.size.zw;
            // never pull in what is nearer than the heat bending it (the cockpit, a car in front)
            if (dist_at(ivec2(suv * u.size.xy)) < t_near) suv = uv;
            vec4 col = texture(src, suv);
            col.rgb = mix(col.rgb, mcol, mirage);
            if (u.cam_right.w > 0.0) col.rgb = mix(col.rgb, vec3(clamp(len / 4.0, 0.0, 1.0), mirage, 0.0), clamp(len * 2.0 + mirage * 4.0, 0.0, 0.85));
            imageStore(color_out, p, col);
        }
        """;
}
