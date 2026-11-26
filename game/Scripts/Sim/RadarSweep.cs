using System.Globalization;
using System.Text.Json;
using Godot;

namespace I76.Game.Sim;

/// <summary>
/// [port] radar_sweep, Graphic Detail's Radar Sweep (Car &amp; Cockpit; the modern look with a pack that has the sweep's layers):
/// Stepped, the default, the original's 30 painted frames swapped every 0.2 s (the beam 12 degrees on each step, the lock cone
/// on the target's 12-degree frame); Smooth, the same sweep drawn every rendered frame at a continuous angle (RadarSweepLaw) and
/// the lock cone on the target's true bearing, from the pack's zrads_face, zrads_grid and zrads.json (tools/radar-redraw.py).
/// </summary>
public static class RadarSweepRow
{
    public static readonly string[] Names = ["Stepped", "Smooth*"];
    public const int Stepped = 0, Smooth = 1, Default = Stepped;
    public static int Clamp(int v) => Math.Clamp(v, 0, Names.Length - 1);
    /// <summary>The key's word; null for Stepped, the default, which is not written.</summary>
    public static string? Word(int v) => Clamp(v) == Smooth ? "smooth" : null;
    public static int Parse(string? word) => word?.Trim().ToLowerInvariant() == "smooth" ? Smooth : Stepped;
    public static void Step(I76.Game.Shell.ShellSettings s) => s.RadarSweep = (Clamp(s.RadarSweep) + 1) % Names.Length;

    /// <summary>I76_RADAR_SWEEP=stepped|smooth: the mode instead of the setting's (captures, A/B runs).</summary>
    public static readonly int? Hook = System.Environment.GetEnvironmentVariable("I76_RADAR_SWEEP") is { Length: > 0 } h ? Parse(h) : null;
}

/// <summary>
/// When the sweep's beam points where: the cockpit steps the sweep's frame every 0.2 s of its clock (the process time, which
/// stops with the game paused), 30 frames a turn, 60 degrees a second clockwise; frame f's beam lies at bearing 12 f minus the
/// art's offset (the fitted 1.078 degrees, zrads.json). The smooth beam is the same clock read between the steps: at each step's
/// instant it is exactly the stepped frame's angle, and it gets to the next frame's angle as the next step is shown.
/// </summary>
public static class RadarSweepLaw
{
    /// <summary>The sweep's step: the original's 0.2 s (Cockpit: _radarNext).</summary>
    public const double StepSeconds = 0.2;
    public const double StepDegrees = 360.0 / I76.Sim.Weapons.RadarScope.SweepFrames;

    /// <summary>
    /// The sweep's continuous phase in frames: the frame shown (<paramref name="cur"/>) plus the share of its step gone at the
    /// clock <paramref name="t"/>, the next step due at <paramref name="next"/>. In [cur, cur + 1); cur itself at the step's instant.
    /// </summary>
    public static double Phase(int cur, double t, double next) => cur + Math.Clamp((t - (next - StepSeconds)) / StepSeconds, 0, 0.999999);

    /// <summary>The beam's bearing at a phase (degrees clockwise from the car's front): the art's, 12 f minus its offset.</summary>
    public static double Bearing(double phase, double offsetDeg) => StepDegrees * phase - offsetDeg;

    /// <summary>The beam's direction in the frames' file axes (atan2 of y down the stored rows, x right): 90 - 12 f + offset.</summary>
    public static double FileAngle(double phase, double offsetDeg) => 90.0 - StepDegrees * phase + offsetDeg;

    /// <summary>The lock cone's direction in the file axes for a bearing (the stepped cone frames lie on 12 f exactly).</summary>
    public static double ConeFileAngle(double bearing) => 90.0 - bearing;

    /// <summary>The target's bearing, degrees clockwise from the car's front in [0, 360): the angle RadarScope.LockedFrame truncates to a frame.</summary>
    public static double TargetBearing(float x, float z)
    {
        double a = MathF.Abs(x) < 1e-5f && MathF.Abs(z) < 1e-5f ? 0 : Math.Atan2(x, z) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    /// <summary>How long the cone takes to settle on a bearing (a time constant): a new target, or the target's bearing moving.</summary>
    public const double ConeEaseSeconds = 0.06;

    /// <summary>The cone's bearing eased toward the target's by the shorter way round, over a frame of <paramref name="dt"/> seconds.</summary>
    public static double Ease(double from, double to, double dt)
    {
        double d = ((to - from) % 360 + 540) % 360 - 180;
        double k = dt <= 0 ? 0 : 1 - Math.Exp(-dt / ConeEaseSeconds);
        double e = from + d * k;
        if (Math.Abs(d) < 0.01) e = to;
        return (e % 360 + 360) % 360;
    }
}

/// <summary>zrads.json: the fitted numbers of the pack's sweep layers (tools/radar-redraw.py sweep_params; docs/hd-textures.md).</summary>
public sealed record RadarSweepParams
{
    public const int Format = 1;
    public int Scale { get; init; }
    public int W { get; init; }
    public int H { get; init; }
    public string FaceLayer { get; init; } = "zrads_face";
    public string GridLayer { get; init; } = "zrads_grid";
    /// <summary>The scope's window in texels (x0, y0, x1, y1), the grid layer's extent: the part the shader draws.</summary>
    public Rect2I Box { get; init; }
    public Vector2 FaceCentre { get; init; }
    public float FaceRadius { get; init; }
    public Vector3 FaceColour { get; init; }
    public Vector2 Pivot { get; init; }
    public float OffsetDeg { get; init; }
    public float StepDeg { get; init; } = 12;
    public float StepSeconds { get; init; } = 0.2f;
    public float BeamWidth { get; init; }
    public float CoreWidth { get; init; }
    public Vector3 EdgeColour { get; init; }
    public Vector3 CoreColour { get; init; }
    public float GlowWidth { get; init; }
    public Vector3 GlowColour { get; init; }
    public float GlowAlpha { get; init; } = 0.5f;
    public float Length { get; init; }
    public float GlowSpan { get; init; }
    public float GlowPeak { get; init; }
    public float GlowDecay { get; init; } = 1;
    public Vector3 GlowTrailColour { get; init; }
    public float ConeHalf { get; init; }
    public float ConeBack { get; init; }
    public Vector3 ConeFill { get; init; }
    public float ConeSoft { get; init; }
    public float ConeRim { get; init; }
    public Vector3? ConeRimColour { get; init; }
    public Vector3? ConeAxisColour { get; init; }
    public float ConeAxisAlpha { get; init; } = 0.5f;

    /// <summary>The numbers from zrads.json, or null for a file this build cannot draw (another format, a missing number).</summary>
    public static RadarSweepParams? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.GetProperty("format").GetInt32() != Format) return null;
            static float F(JsonElement e, string k, float def = float.NaN) => e.TryGetProperty(k, out var v) ? v.GetSingle() : def;
            static Vector2 V2(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());
            static Vector3 V3(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
            static Vector3? O3(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? V3(v) : null;
            var size = r.GetProperty("size"); var layers = r.GetProperty("layers"); var box = layers.GetProperty("grid_box");
            var face = r.GetProperty("face"); var sweep = r.GetProperty("sweep"); var beam = r.GetProperty("beam");
            var glow = r.GetProperty("afterglow"); var cone = r.GetProperty("cone");
            int x0 = box[0].GetInt32(), y0 = box[1].GetInt32(), x1 = box[2].GetInt32(), y1 = box[3].GetInt32();
            var p = new RadarSweepParams
            {
                Scale = r.GetProperty("scale").GetInt32(), W = size[0].GetInt32(), H = size[1].GetInt32(),
                FaceLayer = layers.GetProperty("face").GetString()!, GridLayer = layers.GetProperty("grid").GetString()!,
                Box = new Rect2I(x0, y0, x1 - x0, y1 - y0),
                FaceCentre = V2(face.GetProperty("centre")), FaceRadius = F(face, "radius"), FaceColour = V3(face.GetProperty("colour")),
                Pivot = V2(r.GetProperty("pivot")),
                OffsetDeg = F(sweep, "offset_deg"), StepDeg = F(sweep, "step_deg", 12), StepSeconds = F(sweep, "step_s", 0.2f),
                BeamWidth = F(beam, "width"), CoreWidth = F(beam, "core_width"), EdgeColour = V3(beam.GetProperty("edge_colour")),
                CoreColour = V3(beam.GetProperty("core_colour")), Length = F(beam, "length"),
                GlowWidth = F(beam, "glow_width", 0), GlowColour = O3(beam, "glow_colour") ?? Vector3.Zero, GlowAlpha = F(beam, "glow_alpha", 0.5f),
                GlowSpan = F(glow, "span_deg", 0), GlowPeak = F(glow, "peak", 0), GlowDecay = F(glow, "decay_deg", 1),
                GlowTrailColour = V3(glow.GetProperty("colour")),
                ConeHalf = F(cone, "half_angle"), ConeBack = F(cone, "apex_behind_pivot"), ConeFill = V3(cone.GetProperty("fill")),
                ConeSoft = F(cone, "soft", 0), ConeRim = F(cone, "rim", 0), ConeRimColour = O3(cone, "rim_colour"),
                ConeAxisColour = O3(cone, "axis_colour"), ConeAxisAlpha = F(cone, "axis_alpha", 0.5f),
            };
            // the frames' law: 30 steps of 12 degrees in 0.2 s each (what the stepped frames were drawn to and the cockpit runs)
            if (p.Scale < 1 || p.W != 256 * p.Scale || p.H != 128 * p.Scale || x1 <= x0 || y1 <= y0 || x0 < 0 || y0 < 0 || x1 > p.W || y1 > p.H) return null;
            if (MathF.Abs(p.StepDeg - (float)RadarSweepLaw.StepDegrees) > 1e-4f || MathF.Abs(p.StepSeconds - (float)RadarSweepLaw.StepSeconds) > 1e-4f) return null;
            foreach (float v in new[] { p.FaceRadius, p.OffsetDeg, p.BeamWidth, p.CoreWidth, p.Length, p.ConeHalf, p.ConeBack })
                if (float.IsNaN(v)) return null;
            return p;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// The compute kernel that draws the scope's window into the radar's texture (level 0), its numbers as constants. Each texel:
    /// where the screen's picture (<c>under</c>: the face with the game's blips, lamp and plate drawn on it) differs from the face, that
    /// picture (the game's marks lie over everything, as on a stepped frame); else the face, over its circle the afterglow and the beam
    /// at <c>angle</c> (mode 0) or the lock cone along it (mode 1), then the grid layer over. tools/radar-redraw.py compose_smooth is
    /// the same drawing in numpy; the numbers are the stepped frames' (style B), so a step's frame and the kernel at its angle agree.
    /// </summary>
    public string Kernel()
    {
        var c = CultureInfo.InvariantCulture;
        string f(float v) => v.ToString("0.0######", c);
        string v2(Vector2 v) => $"vec2({f(v.X)}, {f(v.Y)})";
        string v3(Vector3 v) => $"vec3({f(v.X)}, {f(v.Y)}, {f(v.Z)})";
        var glow = GlowWidth > 0 ? $"img = over(img, seg(P, PV, e, {f(GlowWidth)}), {v3(GlowColour)}, {f(GlowAlpha)});" : "";
        var trail = GlowSpan > 0 ? $$"""
                float behind = mod(ang - pc.angle + 180.0, 360.0) - 180.0;
                float inten = (behind > 0.0 && behind < {{f(GlowSpan)}}) ? {{f(GlowPeak)}} * exp(-behind / {{f(GlowDecay)}}) : 0.0;
                img = over(img, inten * clamp((r - 1.0) * 0.5, 0.0, 1.0), {{v3(GlowTrailColour)}}, 1.0);
            """ : "";
        string soft(string d) => ConeSoft > 0 ? $"smooth3(clamp(0.5 - ({d}) / {f(ConeSoft)}, 0.0, 1.0))" : $"aa({d})";
        string softInner(string d) => ConeSoft > 0 ? $"clamp(0.5 - ({d}) / {f(ConeSoft)}, 0.0, 1.0)" : $"aa({d})";
        var fill = ConeRimColour is { } rim
            ? $"img = over(img, cov, {v3(rim)}, 1.0); img = over(img, {softInner($"d + {f(ConeRim)}")}, {v3(ConeFill)}, 1.0);"
            : $"img = over(img, cov, {v3(ConeFill)}, 1.0);";
        var axis = ConeAxisColour is { } ax
            ? $"float rel = clamp(1.0 - abs(sv) / max((t + {f(ConeBack)}) * tan(th), 1e-3), 0.0, 1.0) * (t > 0.0 ? 1.0 : 0.0); img = over(img, rel * rel, {v3(ax)}, {f(ConeAxisAlpha)});"
            : "";
        return $$"""
            #version 450
            layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
            layout(set = 0, binding = 0, rgba8) uniform restrict writeonly image2D screen;
            layout(set = 0, binding = 1, rgba8) uniform restrict readonly image2D under;
            layout(set = 0, binding = 2, rgba8) uniform restrict readonly image2D face;
            layout(set = 0, binding = 3, rgba8) uniform restrict readonly image2D grid;
            layout(push_constant, std430) uniform Params { ivec2 origin; ivec2 size; float angle; int mode; float pad0; float pad1; } pc;
            const float S = {{f(Scale)}};
            const vec2 PV = {{v2(Pivot)}};
            const vec2 FC = {{v2(FaceCentre)}};
            const float FR = {{f(FaceRadius)}};
            const vec3 G = {{v3(FaceColour)}};
            float aa(float d) { return clamp(0.5 - d * S, 0.0, 1.0); }
            float smooth3(float x) { return x * x * (3.0 - 2.0 * x); }
            vec3 over(vec3 img, float cov, vec3 col, float alpha) { float a = clamp(cov, 0.0, 1.0) * alpha; return img * (1.0 - a) + a * col; }
            float seg(vec2 P, vec2 p0, vec2 p1, float w) {
                vec2 d = p1 - p0; float L = length(d); vec2 u = d / max(L, 1e-9);
                float t = dot(P - p0, u); float n = -(P.x - p0.x) * u.y + (P.y - p0.y) * u.x;
                return aa(length(vec2(t - clamp(t, 0.0, L), n)) - w * 0.5);
            }
            void main() {
                ivec2 c = ivec2(gl_GlobalInvocationID.xy);
                if (c.x >= pc.size.x || c.y >= pc.size.y) return;
                ivec2 at = c + pc.origin;
                vec4 u4 = imageLoad(under, c);
                vec4 f4 = imageLoad(face, c);
                if (any(greaterThan(abs(u4 - f4), vec4(0.5 / 255.0)))) { imageStore(screen, at, u4); return; }
                vec2 P = (vec2(at) + 0.5) / S;
                float inside = aa(length(P - FC) - FR);
                vec3 img = G;
                if (inside > 0.0) {
                    vec2 X = P - PV;
                    float ar = radians(pc.angle);
                    vec2 u = vec2(cos(ar), sin(ar));
                    if (pc.mode == 0) {
                        float r = length(X);
                        float ang = degrees(atan(X.y, X.x));
            {{trail}}
                        vec2 e = PV + u * {{f(Length)}};
                        {{glow}}
                        img = over(img, seg(P, PV, e, {{f(BeamWidth)}}), {{v3(EdgeColour)}}, 1.0);
                        img = over(img, seg(P, PV, e, {{f(CoreWidth)}}), {{v3(CoreColour)}}, 1.0);
                    } else {
                        vec2 n = vec2(-u.y, u.x);
                        float t = dot(X, u), sv = dot(X, n);
                        float th = radians({{f(ConeHalf)}});
                        float d = max(abs(sv) * cos(th) - (t + {{f(ConeBack)}}) * sin(th), -t);
                        float cov = {{soft("d")}};
                        {{fill}}
                        {{axis}}
                    }
                }
                vec3 rgb = f4.rgb * 255.0 + inside * (img - G);
                vec4 g4 = imageLoad(grid, c);
                rgb = rgb * (1.0 - g4.a) + g4.rgb * 255.0 * g4.a;
                imageStore(screen, at, vec4(clamp(rgb, 0.0, 255.0) / 255.0, f4.a));
            }
            """;
    }
}

/// <summary>The pack's sweep layers as the cockpit keeps them: the face (a radar frame of its own, RGBA), the grid's window and the numbers.</summary>
public sealed class RadarSweepArt
{
    public required RadarSweepParams Params { get; init; }
    public required RadarFrames.Frame Face { get; init; }
    /// <summary>The grid layer's RGBA8 over <see cref="RadarSweepParams.Box"/> (rows as the frames').</summary>
    public required byte[] Grid { get; init; }

    /// <summary>
    /// The layers from the cockpit's chain of the packs (HdTextures: the dash's), or null when a file is missing or does not fit the
    /// frames served (<paramref name="frameW"/> x <paramref name="frameH"/>: the same scale, so the layers are the frames' pack's).
    /// </summary>
    public static RadarSweepArt? Load(int frameW, int frameH)
    {
        if (I76.Game.Assets.HdTextures.ReadDash("zrads.json") is not { } js || RadarSweepParams.Parse(System.Text.Encoding.UTF8.GetString(js)) is not { } p) return null;
        if (p.W != frameW || p.H != frameH) return null;
        static byte[]? Rgba(string stem, int w, int h)
        {
            if (I76.Game.Assets.HdTextures.Read(stem) is not { } file || I76.Game.Assets.HdTextures.Decode(file.Name, file.Bytes) is not { } img) return null;
            img.ClearMipmaps();
            if (img.IsCompressed()) img.Decompress();
            img.Convert(Image.Format.Rgba8);
            return img.GetWidth() == w && img.GetHeight() == h ? img.GetData() : null;
        }
        var face = Rgba(p.FaceLayer, p.W, p.H);
        var grid = Rgba(p.GridLayer, p.Box.Size.X, p.Box.Size.Y);
        if (face is null || grid is null) return null;
        return new RadarSweepArt { Params = p, Face = RadarFrames.Whole(p.W, p.H, face), Grid = grid };
    }

    /// <summary>The face's texels over the box (rows as stored).</summary>
    public byte[] FaceBox()
    {
        var b = Params.Box;
        var o = new byte[b.Size.X * b.Size.Y * 4];
        var src = Face.Rgba!;
        for (int y = 0; y < b.Size.Y; y++) System.Buffer.BlockCopy(src, ((b.Position.Y + y) * Face.W + b.Position.X) * 4, o, y * b.Size.X * 4, b.Size.X * 4);
        return o;
    }
}

/// <summary>
/// The smooth sweep on the GPU: the scope's window of the radar's live texture drawn by the kernel each frame, from a copy of the
/// window as the cockpit last sent it (the face with its blips, lamp and plate), the face and the grid; then the window's mip levels.
/// Nothing is uploaded a frame: the cockpit's picture changes (a blip, the lamp) go up as ever and are copied into the window's copy
/// on the GPU. Needs a RenderingDevice and the live texture's levels (a storage view); without them the cockpit stays stepped.
/// </summary>
public sealed class RadarSweepGpu : IDisposable
{
    private readonly RenderingDevice _rd;
    private readonly RadarSweepArt _art;
    private Rid _shader, _pipeline, _under, _face, _grid, _set, _setFor;
    private readonly Rect2I _box;

    private RadarSweepGpu(RenderingDevice rd, RadarSweepArt art) { _rd = rd; _art = art; _box = art.Params.Box; }

    public RadarSweepArt Art => _art;

    /// <summary>The kernel compiled and the window's textures made, or null where it cannot run (no device, a compile error).</summary>
    public static RadarSweepGpu? Make(RadarSweepArt art)
    {
        if (!LiveTexture.UsesDevice(RenderingServer.GetCurrentRenderingMethod(), RenderingServer.GetRenderingDevice() is not null)) return null;
        var g = new RadarSweepGpu(RenderingServer.GetRenderingDevice(), art);
        if (g.Build()) return g;
        g.Dispose();
        return null;
    }

    private bool Build()
    {
        var spirv = _rd.ShaderCompileSpirVFromSource(new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = _art.Params.Kernel() });
        if (spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } err) { GD.PushWarning($"radar sweep: the kernel did not compile: {err}"); return false; }
        _shader = _rd.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) return false;
        _pipeline = _rd.ComputePipelineCreate(_shader);
        if (!_pipeline.IsValid) return false;
        Rid Tex(byte[]? data, int w, int h, RenderingDevice.TextureUsageBits extra)
        {
            var f = new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)w, Height = (uint)h,
                UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.CanUpdateBit | extra,
            };
            return data is null ? _rd.TextureCreate(f, new RDTextureView()) : _rd.TextureCreate(f, new RDTextureView(), [data]);
        }
        int w = _box.Size.X, h = _box.Size.Y;
        _under = Tex(null, w, h, RenderingDevice.TextureUsageBits.CanCopyToBit);
        _face = Tex(_art.FaceBox(), w, h, 0);
        _grid = Tex(_art.Grid, w, h, 0);
        { DashStats.Upload(2L * w * h * 4); I76.Game.Boot.FrameSections.Upload(2L * w * h * 4); }
        return _under.IsValid && _face.IsValid && _grid.IsValid;
    }

    /// <summary>
    /// The window's copy brought up to date where the cockpit just sent its picture (<paramref name="rects"/>, raw rows; null: all of
    /// it), copied on the GPU from the live texture, which holds the sent picture there until <see cref="Draw"/> writes over it.
    /// </summary>
    public void Took(LiveTexture live, IReadOnlyList<Rect2I>? rects)
    {
        if (!live.DeviceRid.IsValid) return;
        if (rects is null) { Copy(live, _box); return; }
        foreach (var r in rects) Copy(live, r.Intersection(_box));
    }

    private void Copy(LiveTexture live, Rect2I r)
    {
        if (r.Size.X <= 0 || r.Size.Y <= 0) return;
        _rd.TextureCopy(live.DeviceRid, _under, new Vector3(r.Position.X, r.Position.Y, 0), new Vector3(r.Position.X - _box.Position.X, r.Position.Y - _box.Position.Y, 0),
            new Vector3(r.Size.X, r.Size.Y, 1), 0, 0, 0, 0);
    }

    /// <summary>The window drawn into the live texture's full-size level with the beam (or, <paramref name="cone"/>, the lock cone) at file angle <paramref name="angle"/>, then its levels.</summary>
    public bool Draw(LiveTexture live, double angle, bool cone)
    {
        var view = live.StorageLevel0;
        if (!view.IsValid || live.Width != _art.Params.W || live.Height != _art.Params.H) return false;
        if (_setFor != view || !_set.IsValid || !_rd.UniformSetIsValid(_set))
        {
            if (_set.IsValid && _rd.UniformSetIsValid(_set)) _rd.FreeRid(_set);
            RDUniform U(int binding, Rid tex) { var u = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = binding }; u.AddId(tex); return u; }
            _set = _rd.UniformSetCreate([U(0, view), U(1, _under), U(2, _face), U(3, _grid)], _shader, 0);
            _setFor = view;
            if (!_set.IsValid) return false;
        }
        Span<byte> pc = stackalloc byte[32];
        pc.Clear();
        System.BitConverter.TryWriteBytes(pc, _box.Position.X); System.BitConverter.TryWriteBytes(pc[4..], _box.Position.Y);
        System.BitConverter.TryWriteBytes(pc[8..], _box.Size.X); System.BitConverter.TryWriteBytes(pc[12..], _box.Size.Y);
        System.BitConverter.TryWriteBytes(pc[16..], (float)angle); System.BitConverter.TryWriteBytes(pc[20..], cone ? 1 : 0);
        long cl = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(cl, _pipeline);
        _rd.ComputeListBindUniformSet(cl, _set, 0);
        _rd.ComputeListSetPushConstant(cl, pc, 32);
        _rd.ComputeListDispatch(cl, (uint)((_box.Size.X + 7) / 8), (uint)((_box.Size.Y + 7) / 8), 1);
        _rd.ComputeListEnd();
        live.LevelsUnder(_box);
        return true;
    }

    public void Dispose()
    {
        if (_set.IsValid && _rd.UniformSetIsValid(_set)) _rd.FreeRid(_set);
        foreach (var r in new[] { _under, _face, _grid, _pipeline, _shader }) if (r.IsValid) _rd.FreeRid(r);
        _set = _under = _face = _grid = _pipeline = _shader = default;
    }
}
