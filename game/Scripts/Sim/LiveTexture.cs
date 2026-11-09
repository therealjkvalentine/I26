using Godot;

namespace I76.Game.Sim;

/// <summary>
/// A texture the cockpit redraws in parts (its panels, the radar screen): RGBA8 texels kept on the CPU by the caller, uploaded
/// whole when it is made or changes size, otherwise only the rectangles that changed. On a RenderingDevice (Forward+, Mobile) it
/// is a texture of its own the GPU can copy into: each rectangle goes up through a small staging texture and is copied into place
/// on the GPU, byte for byte. The Compatibility renderer has no such copy: there every change uploads the whole picture, as an
/// ImageTexture. Rows are the picture's as stored (bottom-up, the maps' order); a rectangle's Y counts stored rows.
/// The texture carries its mip levels, so the modern look's materials can filter it where the screen shows it smaller than its
/// texels (the 8x art at 3440x1440): each level is a 2x2 box of the one above, made on the GPU by a small compute kernel under
/// the rectangles that changed (a counter's window, the sweep's wedge), level by level; the CPU never holds the chain.
/// </summary>
public sealed class LiveTexture
{
    private readonly RenderingDevice? _rd = RenderingServer.GetRenderingDevice();
    private readonly Texture2Drd? _rdTex;
    private readonly ImageTexture? _imgTex;
    private Image? _img;
    private Rid _rid;
    private readonly Dictionary<(int W, int H, int N), Rid> _staging = [];
    private readonly Dictionary<(int W, int H), int> _used = [];
    private byte[] _pack = [];
    /// <summary>The levels the GPU texture has (1 without the kernel).</summary>
    private int _levels = 1;
    /// <summary>A view of each level, and the kernel's uniform set for each level after the first (the level above read, it written).</summary>
    private Rid[] _views = [], _sets = [];
    /// <summary>A staging texture of the whole picture (one level) for a whole upload at the same size.</summary>
    private Rid _whole;
    /// <summary>Every level packed in order, as TextureCreate takes a texture's data; one buffer for all live textures.</summary>
    private static byte[] s_all = [];

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The texture the materials and the 2D dash hold: the same object for the picture's life, whatever its size.</summary>
    public Texture2D Texture => (Texture2D?)_rdTex ?? _imgTex!;

    public LiveTexture(byte[] rgba, int w, int h)
    {
        if (_rd is not null) { _rdTex = new Texture2Drd(); Set(rgba, w, h); }
        else { _img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba); _img.GenerateMipmaps(); _imgTex = ImageTexture.CreateFromImage(_img); (Width, Height) = (w, h); DashStats.Upload(rgba.Length); }
    }

    /// <summary>The whole picture, of any size (a size change makes the GPU texture again under the same Texture).</summary>
    public void Set(byte[] rgba, int w, int h)
    {
        DashStats.Upload((long)w * h * 4);
        if (_rd is null)
        {
            // the engine's own mip chain: the whole picture goes up anyway here
            _img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
            _img.GenerateMipmaps();
            _imgTex!.SetImage(_img);
            (Width, Height) = (w, h);
            return;
        }
        if (_rid.IsValid && w == Width && h == Height)
        {
            // the picture through a staging texture of its size (kept: the radar's frames come whole), then its levels
            if (!_whole.IsValid)
            {
                var sf = new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)w, Height = (uint)h,
                    UsageBits = RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
                };
                _whole = _rd.TextureCreate(sf, new RDTextureView());
            }
            _rd.TextureUpdate(_whole, 0, rgba.AsSpan(0, w * h * 4));
            _rd.TextureCopy(_whole, _rid, Vector3.Zero, Vector3.Zero, new Vector3(w, h, 1), 0, 0, 0, 0);
            Levels(0, 0, w, h);
            return;
        }
        var old = _rid;
        FreeLevels();
        if (_whole.IsValid) { _rd.FreeRid(_whole); _whole = default; }
        int levels = Kernel(_rd) ? LevelCount(w, h) : 1;
        var f = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)w, Height = (uint)h, Mipmaps = (uint)levels,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
                | RenderingDevice.TextureUsageBits.CanCopyToBit | RenderingDevice.TextureUsageBits.CanCopyFromBit
                | (levels > 1 ? RenderingDevice.TextureUsageBits.StorageBit : 0),
        };
        // the sRGB view beside the linear one, as an ImageTexture's: a material's source_color sampler reads that
        f.AddShareableFormat(RenderingDevice.DataFormat.R8G8B8A8Unorm);
        f.AddShareableFormat(RenderingDevice.DataFormat.R8G8B8A8Srgb);
        // the data carries every level; the levels under the picture are made by the kernel once it exists
        int total = Packed(w, h, levels);
        if (s_all.Length < total) s_all = new byte[total];
        System.Buffer.BlockCopy(rgba, 0, s_all, 0, w * h * 4);
        _rid = _rd.TextureCreate(f, new RDTextureView(), [s_all.Length == total ? s_all : s_all[..total]]);
        (Width, Height, _levels) = (w, h, levels);
        _rdTex!.TextureRdRid = _rid;
        if (old.IsValid) _rd.FreeRid(old);
        if (levels > 1)
        {
            _views = new Rid[levels];
            _sets = new Rid[levels - 1];
            for (int k = 0; k < levels; k++) _views[k] = _rd.TextureCreateSharedFromSlice(new RDTextureView(), _rid, 0, (uint)k, 1, RenderingDevice.TextureSliceType.Slice2D);
            for (int k = 1; k < levels; k++)
            {
                var above = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
                above.AddId(_views[k - 1]);
                var level = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 1 };
                level.AddId(_views[k]);
                _sets[k - 1] = _rd.UniformSetCreate([above, level], s_shader, 0);
            }
            Levels(0, 0, w, h);
        }
    }

    /// <summary>
    /// The rectangles <paramref name="rects"/> of <paramref name="rgba"/> (the whole picture, at this texture's size) uploaded
    /// into place and the levels under each made again; the rest of the texture is left as it was.
    /// </summary>
    public void Update(byte[] rgba, IReadOnlyList<Rect2I> rects)
    {
        if (_rd is null || !_rid.IsValid) { Set(rgba, Width, Height); return; }
        // as much as the whole picture through the staging textures: the whole picture in one upload instead
        long staged = 0;
        foreach (var r in rects) staged += (long)Bucket(System.Math.Max(1, r.Size.X)) * Bucket(System.Math.Max(1, r.Size.Y));
        if (staged * 4 >= (long)Width * Height * 3) { Set(rgba, Width, Height); return; }
        _used.Clear();
        foreach (var r in rects)
        {
            int x0 = System.Math.Max(0, r.Position.X), y0 = System.Math.Max(0, r.Position.Y);
            int x1 = System.Math.Min(Width, r.End.X), y1 = System.Math.Min(Height, r.End.Y);
            if (x1 <= x0 || y1 <= y0) continue;
            int w = x1 - x0, h = y1 - y0;
            // staging textures in power-of-two sizes, one per rectangle of a size in a call (each copy reads its own), kept
            int bw = Bucket(w), bh = Bucket(h);
            int n = _used.GetValueOrDefault((bw, bh));
            _used[(bw, bh)] = n + 1;
            if (!_staging.TryGetValue((bw, bh, n), out var st))
            {
                var f = new RDTextureFormat
                {
                    Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)bw, Height = (uint)bh,
                    UsageBits = RenderingDevice.TextureUsageBits.CanUpdateBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
                };
                _staging[(bw, bh, n)] = st = _rd.TextureCreate(f, new RDTextureView());
            }
            int bytes = bw * bh * 4;
            if (_pack.Length < bytes) _pack = new byte[System.Math.Max(bytes, _pack.Length * 2)];
            for (int y = 0; y < h; y++) System.Buffer.BlockCopy(rgba, ((y0 + y) * Width + x0) * 4, _pack, y * bw * 4, w * 4);
            _rd.TextureUpdate(st, 0, _pack.AsSpan(0, bytes));
            _rd.TextureCopy(st, _rid, Vector3.Zero, new Vector3(x0, y0, 0), new Vector3(w, h, 1), 0, 0, 0, 0);
            DashStats.Upload(bytes);
            Levels(x0, y0, x1, y1);
        }
    }

    /// <summary>The levels under the full-size rectangle [x0, x1) x [y0, y1) made again, each from the one above, by the kernel.</summary>
    private void Levels(int x0, int y0, int x1, int y1)
    {
        if (_levels <= 1) return;
        Span<byte> rect = stackalloc byte[16];
        long cl = _rd!.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(cl, s_pipeline);
        for (int k = 1; k < _levels; k++)
        {
            (x0, y0, x1, y1) = Under(x0, y0, x1, y1, LevelSize(Width, k), LevelSize(Height, k));
            if (x1 <= x0 || y1 <= y0) break;
            if (k > 1) _rd.ComputeListAddBarrier(cl);
            _rd.ComputeListBindUniformSet(cl, _sets[k - 1], 0);
            System.BitConverter.TryWriteBytes(rect, x0); System.BitConverter.TryWriteBytes(rect[4..], y0);
            System.BitConverter.TryWriteBytes(rect[8..], x1 - x0); System.BitConverter.TryWriteBytes(rect[12..], y1 - y0);
            _rd.ComputeListSetPushConstant(cl, rect, 16);
            _rd.ComputeListDispatch(cl, (uint)((x1 - x0 + 7) / 8), (uint)((y1 - y0 + 7) / 8), 1);
        }
        _rd.ComputeListEnd();
    }

    private static Rid s_shader, s_pipeline;
    private static bool s_kernelTried;

    /// <summary>The box kernel, compiled once from its source: false where it does not compile (the texture then has one level).</summary>
    private static bool Kernel(RenderingDevice rd)
    {
        if (s_kernelTried) return s_pipeline.IsValid;
        s_kernelTried = true;
        var src = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = Box };
        var spirv = rd.ShaderCompileSpirVFromSource(src);
        if (spirv.GetStageCompileError(RenderingDevice.ShaderStage.Compute) is { Length: > 0 } err) { GD.PushWarning($"live texture: the mip kernel did not compile: {err}"); return false; }
        s_shader = rd.ShaderCreateFromSpirV(spirv);
        if (!s_shader.IsValid) return false;
        s_pipeline = rd.ComputePipelineCreate(s_shader);
        return s_pipeline.IsValid;
    }

    /// <summary>
    /// A level's texels in the rectangle (the push constant: origin and size, in that level's texels) from the level above:
    /// the same rule as <see cref="Downsample"/>, so the GPU's chain is what the CPU's reference gives (verify checks it).
    /// </summary>
    private const string Box = """
        #version 450
        layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
        layout(set = 0, binding = 0, rgba8) uniform restrict readonly image2D above;
        layout(set = 0, binding = 1, rgba8) uniform restrict writeonly image2D level;
        layout(push_constant, std430) uniform Rect { ivec2 origin; ivec2 size; } rect;
        void main() {
        	ivec2 c = ivec2(gl_GlobalInvocationID.xy);
        	if (c.x >= rect.size.x || c.y >= rect.size.y) return;
        	c += rect.origin;
        	ivec2 last = imageSize(above) - 1;
        	ivec2 s0 = min(c * 2, last), s1 = min(c * 2 + 1, last);
        	ivec4 t0 = ivec4(round(imageLoad(above, ivec2(s0.x, s0.y)) * 255.0));
        	ivec4 t1 = ivec4(round(imageLoad(above, ivec2(s1.x, s0.y)) * 255.0));
        	ivec4 t2 = ivec4(round(imageLoad(above, ivec2(s0.x, s1.y)) * 255.0));
        	ivec4 t3 = ivec4(round(imageLoad(above, ivec2(s1.x, s1.y)) * 255.0));
        	ivec3 rgb = ivec3(0);
        	int n = 0;
        	if (t0.a >= 128) { rgb += t0.rgb; n++; }
        	if (t1.a >= 128) { rgb += t1.rgb; n++; }
        	if (t2.a >= 128) { rgb += t2.rgb; n++; }
        	if (t3.a >= 128) { rgb += t3.rgb; n++; }
        	if (n == 0) { rgb = t0.rgb + t1.rgb + t2.rgb + t3.rgb; n = 4; }
        	ivec3 o = (rgb + n / 2) / n;
        	int a = (t0.a + t1.a + t2.a + t3.a + 2) >> 2;
        	imageStore(level, c, vec4(vec3(o), float(a)) / 255.0);
        }
        """;

    /// <summary>A staging texture's side for <paramref name="n"/> texels: the power of two at or above it (16 at least) up to 256, then the multiple of 256.</summary>
    public static int Bucket(int n)
    {
        if (n > 256) return (n + 255) / 256 * 256;
        int b = 16;
        while (b < n) b <<= 1;
        return b;
    }

    /// <summary>The mip levels a w x h texture has down to 1x1, the full-size level counted.</summary>
    public static int Levels(int w, int h)
    {
        int n = 1;
        for (int m = System.Math.Max(w, h); m > 1; m >>= 1) n++;
        return n;
    }

    /// <summary>A side of <paramref name="n"/> texels at mip level <paramref name="k"/> (1 at least).</summary>
    public static int LevelSize(int n, int k) => System.Math.Max(1, n >> k);

    /// <summary>
    /// The texels of the next level (lw x lh) under a level's rectangle [x0, x1) x [y0, y1): those whose 2x2 reaches into it,
    /// a texel at the rectangle's odd edge included.
    /// </summary>
    public static (int X0, int Y0, int X1, int Y1) Under(int x0, int y0, int x1, int y1, int lw, int lh) =>
        (x0 >> 1, y0 >> 1, System.Math.Min(lw, (x1 + 1) >> 1), System.Math.Min(lh, (y1 + 1) >> 1));

    /// <summary>
    /// The most levels a live texture carries: down to a sixteenth of its size, which serves the screen minifying it 16:1, far
    /// past what a dash in view comes to; every level after a change is a dispatch, so the chain stops there.
    /// </summary>
    public const int MaxLevels = 5;

    /// <summary>The levels a w x h live texture carries: its chain, <see cref="MaxLevels"/> at most.</summary>
    public static int LevelCount(int w, int h) => System.Math.Min(MaxLevels, Levels(w, h));

    /// <summary>The bytes of the first <paramref name="levels"/> levels of a w x h RGBA8 texture packed in order.</summary>
    public static int Packed(int w, int h, int levels)
    {
        int total = 0;
        for (int k = 0; k < levels; k++) total += LevelSize(w, k) * LevelSize(h, k) * 4;
        return total;
    }

    /// <summary>
    /// The texels [x0, x1) x [y0, y1) of a mip level (<paramref name="dst"/>, half the size of <paramref name="src"/>, a side of 1 at
    /// least) made from the level above (sw x sh): each the mean of its 2x2 texels, the last column or row read twice where the
    /// level above is odd. A keyed texel (alpha under half) lends only its alpha, so the colour at a cut edge stays the painted
    /// side's; where all four are keyed, their mean. The kernel's rule, on the CPU: the reference the GPU's chain is checked against.
    /// </summary>
    public static void Downsample(byte[] src, int sw, int sh, byte[] dst, int x0, int y0, int x1, int y1)
    {
        int dw = LevelSize(sw, 1);
        var s = src.AsSpan();
        for (int y = y0; y < y1; y++)
        {
            int row0 = System.Math.Min(sh - 1, y * 2) * sw, row1 = System.Math.Min(sh - 1, y * 2 + 1) * sw;
            for (int x = x0; x < x1; x++)
            {
                int sx0 = System.Math.Min(sw - 1, x * 2), sx1 = System.Math.Min(sw - 1, x * 2 + 1);
                int o0 = (row0 + sx0) * 4, o1 = (row0 + sx1) * 4, o2 = (row1 + sx0) * 4, o3 = (row1 + sx1) * 4;
                int a0 = s[o0 + 3], a1 = s[o1 + 3], a2 = s[o2 + 3], a3 = s[o3 + 3];
                int r = 0, g = 0, b = 0, n = 0;
                if (a0 >= 128) { r += s[o0]; g += s[o0 + 1]; b += s[o0 + 2]; n++; }
                if (a1 >= 128) { r += s[o1]; g += s[o1 + 1]; b += s[o1 + 2]; n++; }
                if (a2 >= 128) { r += s[o2]; g += s[o2 + 1]; b += s[o2 + 2]; n++; }
                if (a3 >= 128) { r += s[o3]; g += s[o3 + 1]; b += s[o3 + 2]; n++; }
                if (n == 0)
                {
                    n = 4;
                    r = s[o0] + s[o1] + s[o2] + s[o3];
                    g = s[o0 + 1] + s[o1 + 1] + s[o2 + 1] + s[o3 + 1];
                    b = s[o0 + 2] + s[o1 + 2] + s[o2 + 2] + s[o3 + 2];
                }
                int d = (y * dw + x) * 4;
                dst[d] = (byte)((r + n / 2) / n); dst[d + 1] = (byte)((g + n / 2) / n); dst[d + 2] = (byte)((b + n / 2) / n); dst[d + 3] = (byte)((a0 + a1 + a2 + a3 + 2) >> 2);
            }
        }
    }

    /// <summary>
    /// The GPU's copy of the picture with its mip levels packed after it (rows as stored), to check the uploads against the
    /// CPU's (I76_DASH_STATS=verify); null without a RenderingDevice.
    /// </summary>
    public byte[]? ReadBack() => _rd is not null && _rid.IsValid ? _rd.TextureGetData(_rid, 0) : null;

    /// <summary>What the GPU should hold for the picture <paramref name="rgba"/>: it and its levels made whole on the CPU, packed as <see cref="ReadBack"/> returns them.</summary>
    public byte[] Expected(byte[] rgba)
    {
        var all = new byte[Packed(Width, Height, _levels)];
        System.Buffer.BlockCopy(rgba, 0, all, 0, Width * Height * 4);
        byte[] above = rgba;
        for (int k = 1, o = Width * Height * 4; k < _levels; k++)
        {
            var level = new byte[LevelSize(Width, k) * LevelSize(Height, k) * 4];
            Downsample(above, LevelSize(Width, k - 1), LevelSize(Height, k - 1), level, 0, 0, LevelSize(Width, k), LevelSize(Height, k));
            System.Buffer.BlockCopy(level, 0, all, o, level.Length);
            o += level.Length;
            above = level;
        }
        return all;
    }

    private void FreeLevels()
    {
        foreach (var s in _sets) if (s.IsValid) _rd!.FreeRid(s);
        foreach (var v in _views) if (v.IsValid) _rd!.FreeRid(v);
        _sets = []; _views = [];
    }

    /// <summary>Lets the GPU textures go (the cockpit leaving the tree): the Texture shows nothing after.</summary>
    public void Free()
    {
        if (_rd is null) return;
        FreeLevels();
        if (_whole.IsValid) { _rd.FreeRid(_whole); _whole = default; }
        if (_rid.IsValid) { _rdTex!.TextureRdRid = default; _rd.FreeRid(_rid); _rid = default; }
        foreach (var st in _staging.Values) _rd.FreeRid(st);
        _staging.Clear();
    }
}
