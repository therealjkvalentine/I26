using Godot;

namespace I76.Game.Sim;

/// <summary>
/// A texture the cockpit redraws in parts (its panels, the radar screen): RGBA8 texels kept on the CPU by the caller, uploaded
/// whole when it is made or changes size, otherwise only the rectangles that changed. On a RenderingDevice (Forward+, Mobile) it
/// is a texture of its own the GPU can copy into: each rectangle goes up through a small staging texture and is copied into place
/// on the GPU, byte for byte. The Compatibility renderer has no such copy: there every change uploads the whole picture, as an
/// ImageTexture. Rows are the picture's as stored (bottom-up, the maps' order); a rectangle's Y counts stored rows.
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

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>The texture the materials and the 2D dash hold: the same object for the picture's life, whatever its size.</summary>
    public Texture2D Texture => (Texture2D?)_rdTex ?? _imgTex!;

    public LiveTexture(byte[] rgba, int w, int h)
    {
        if (_rd is not null) { _rdTex = new Texture2Drd(); Set(rgba, w, h); }
        else { _img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba); _imgTex = ImageTexture.CreateFromImage(_img); (Width, Height) = (w, h); DashStats.Upload(rgba.Length); }
    }

    /// <summary>The whole picture, of any size (a size change makes the GPU texture again under the same Texture).</summary>
    public void Set(byte[] rgba, int w, int h)
    {
        DashStats.Upload((long)w * h * 4);
        if (_rd is null)
        {
            if (w == Width && h == Height) { _img!.SetData(w, h, false, Image.Format.Rgba8, rgba); _imgTex!.Update(_img); }
            else { _img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba); _imgTex!.SetImage(_img); }
            (Width, Height) = (w, h);
            return;
        }
        if (_rid.IsValid && w == Width && h == Height) { _rd.TextureUpdate(_rid, 0, rgba.AsSpan(0, w * h * 4)); return; }
        var old = _rid;
        var f = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R8G8B8A8Unorm, Width = (uint)w, Height = (uint)h,
            UsageBits = RenderingDevice.TextureUsageBits.SamplingBit | RenderingDevice.TextureUsageBits.CanUpdateBit
                | RenderingDevice.TextureUsageBits.CanCopyToBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
        };
        // the sRGB view beside the linear one, as an ImageTexture's: a material's source_color sampler reads that
        f.AddShareableFormat(RenderingDevice.DataFormat.R8G8B8A8Unorm);
        f.AddShareableFormat(RenderingDevice.DataFormat.R8G8B8A8Srgb);
        _rid = _rd.TextureCreate(f, new RDTextureView(), [rgba.Length == w * h * 4 ? rgba : rgba[..(w * h * 4)]]);
        (Width, Height) = (w, h);
        _rdTex!.TextureRdRid = _rid;
        if (old.IsValid) _rd.FreeRid(old);
    }

    /// <summary>
    /// The rectangles <paramref name="rects"/> of <paramref name="rgba"/> (the whole picture, at this texture's size) uploaded
    /// into place; the rest of the texture is left as it was.
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
        }
    }

    /// <summary>A staging texture's side for <paramref name="n"/> texels: the power of two at or above it (16 at least) up to 256, then the multiple of 256.</summary>
    public static int Bucket(int n)
    {
        if (n > 256) return (n + 255) / 256 * 256;
        int b = 16;
        while (b < n) b <<= 1;
        return b;
    }

    /// <summary>The GPU's copy of the picture (rows as stored), to check the uploads against the CPU's (I76_DASH_STATS=verify); null without a RenderingDevice.</summary>
    public byte[]? ReadBack() => _rd is not null && _rid.IsValid ? _rd.TextureGetData(_rid, 0) : null;

    /// <summary>Lets the GPU textures go (the cockpit leaving the tree): the Texture shows nothing after.</summary>
    public void Free()
    {
        if (_rd is null) return;
        if (_rid.IsValid) { _rdTex!.TextureRdRid = default; _rd.FreeRid(_rid); _rid = default; }
        foreach (var st in _staging.Values) _rd.FreeRid(st);
        _staging.Clear();
    }
}
