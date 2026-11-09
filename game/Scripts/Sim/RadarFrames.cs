using Godot;

namespace I76.Game.Sim;

/// <summary>
/// The radar screen's frames: the sweep's ZRADF000-030 (030 the screen off) and the locked ZRADB000-030. A pack's frames are kept as
/// the pack stores them, BC7, a byte a texel (mip 0 only: the screen is drawn from the full-size frame), and decoded into one of a few
/// kept RGBA buffers when one is about to be shown: the sweep's next frame and the locked frame's neighbours on a worker thread, ahead
/// of their step. The originals, and a pack's pictures in any other form, are kept as their RGBA. Each frame decoded is the same bytes
/// as the engine's decompression gives (I76_DASH_STATS=verify checks every frame at the load).
/// </summary>
public sealed class RadarFrames
{
    /// <summary>The frames' stems, the sweep's then the locked ones.</summary>
    public static IEnumerable<string> Stems =>
        Enumerable.Range(0, 31).Select(i => "zradf" + i.ToString("000")).Concat(Enumerable.Range(0, 31).Select(i => "zradb" + i.ToString("000")));

    /// <summary>One frame: its size and what is kept of it.</summary>
    public sealed class Frame
    {
        public int W { get; init; }
        public int H { get; init; }
        /// <summary>RGBA8 kept whole (an original, a picture not BC7), or null.</summary>
        internal byte[]? Rgba;
        /// <summary>BC7 blocks of the full-size level, or null.</summary>
        internal byte[]? Blocks;
    }

    /// <summary>A frame kept as BC7 blocks (w and h whole multiples of 4).</summary>
    public static Frame Compressed(int w, int h, byte[] blocks) => new() { W = w, H = h, Blocks = blocks };

    /// <summary>A frame kept as RGBA8.</summary>
    public static Frame Whole(int w, int h, byte[] rgba) => new() { W = w, H = h, Rgba = rgba };

    private sealed class Slot
    {
        public Frame? Frame;
        public byte[] Bytes = [];
        public System.Threading.Tasks.Task? Pending;
        public long Used;
    }

    /// <summary>Decoded frames kept: the one shown, the one before (the changed rectangles are found between them) and two ahead.</summary>
    private const int Slots = 4;
    private readonly Slot[] _slots = Enumerable.Range(0, Slots).Select(_ => new Slot()).ToArray();
    private long _use;

    public Frame?[] Sweep { get; } = new Frame?[31];
    public Frame?[] Locked { get; } = new Frame?[31];

    /// <summary>The bytes kept: the frames as stored and the decoded buffers.</summary>
    public long Kept
    {
        get
        {
            long n = 0;
            foreach (var f in Sweep) n += (f?.Rgba?.Length ?? 0) + (f?.Blocks?.Length ?? 0);
            foreach (var f in Locked) n += (f?.Rgba?.Length ?? 0) + (f?.Blocks?.Length ?? 0);
            foreach (var s in _slots) n += s.Bytes.Length;
            return n;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<I76.Game.Assets.TextureFactory, RadarFrames> Made = new();
    private static readonly object Checking = new();

    /// <summary>The frames a cockpit took, by factory: a factory the restart cache keeps (World.RestartCache) serves the same frames to the next.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<I76.Game.Assets.TextureFactory, RadarFrames> Taken = new();

    /// <summary>Makes the frames for what the factory serves now, for <see cref="Take"/> (the mission's build, on its thread), unless a cockpit took them already.</summary>
    public static void Prefetch(I76.Game.Assets.TextureFactory textures, I76.Formats.Assembly.TextureResolver resolver)
    {
        if (Taken.TryGetValue(textures, out var t) && t._hd == textures.Hd) return;
        Made.AddOrUpdate(textures, Make(textures, resolver));
    }

    /// <summary>The frames for what the factory serves now: those the build made, those a cockpit took before (with buffers of its own), or made here.</summary>
    public static RadarFrames Take(I76.Game.Assets.TextureFactory textures, I76.Formats.Assembly.TextureResolver resolver)
    {
        RadarFrames r;
        if (Made.TryGetValue(textures, out var m) && m._hd == textures.Hd) { Made.Remove(textures); r = m; }
        else if (Taken.TryGetValue(textures, out var t) && t._hd == textures.Hd) r = t.Again();
        else r = Make(textures, resolver);
        Taken.AddOrUpdate(textures, r);
        return r;
    }

    /// <summary>The same frames (read only) with decode buffers of their own.</summary>
    private RadarFrames Again()
    {
        var r = new RadarFrames { _hd = _hd };
        Sweep.CopyTo(r.Sweep, 0);
        Locked.CopyTo(r.Locked, 0);
        return r;
    }

    private bool _hd;

    private static RadarFrames Make(I76.Game.Assets.TextureFactory textures, I76.Formats.Assembly.TextureResolver resolver)
    {
        var r = new RadarFrames { _hd = textures.Hd };
        var members = Stems.Select(resolver.FindByStem).ToArray();
        var pics = textures.Pictures(members);
        var frames = new Frame?[members.Length];
        int checkedFrames = 0;
        System.Threading.Tasks.Parallel.For(0, members.Length, i =>
        {
            if (pics[i] is not { } p) return;
            frames[i] = FromPicture(p);
            if (DashStats.Verify && frames[i]!.Blocks is { } b)
            {
                // the decode against the engine's own decompression of the same picture
                var d = (Image)p.Duplicate();
                d.ClearMipmaps(); d.Decompress(); d.Convert(Image.Format.Rgba8);
                var mine = new byte[frames[i]!.W * frames[i]!.H * 4];
                Bc7.Decode(b, frames[i]!.W, frames[i]!.H, mine, 0, frames[i]!.H / 4);
                lock (Checking) { DashStats.Check("radar frame " + members[i], mine, d.GetData()); checkedFrames++; }
            }
            p.Dispose();
        });
        if (DashStats.Verify) GD.Print($"dash-verify: radar frames decoded against the engine's: {checkedFrames} (a frame that differs has its own line)");
        for (int i = 0; i < members.Length; i++)
        {
            // the originals (the 1997 look, the HD Textures row off, a frame the pack has not): the factory's RGBA, kept by it already
            if (frames[i] is null && members[i] is { } m && textures.GetRgba(m) is { } t) frames[i] = Whole(t.Width, t.Height, t.Rgba);
            if (i < 31) r.Sweep[i] = frames[i];
            else r.Locked[i - 31] = frames[i];
        }
        return r;
    }

    /// <summary>A pack's picture as kept: BC7 as stored (its full-size level), anything else as RGBA8.</summary>
    private static Frame FromPicture(Image p)
    {
        int w = p.GetWidth(), h = p.GetHeight();
        p.ClearMipmaps();
        if (p.GetFormat() == Image.Format.BptcRgba && w % 4 == 0 && h % 4 == 0) return Compressed(w, h, p.GetData());
        if (p.IsCompressed()) p.Decompress();
        p.Convert(Image.Format.Rgba8);
        return Whole(w, h, p.GetData());
    }

    /// <summary>The frame's RGBA8 (rows as stored), decoded now if no buffer holds it yet (a decode under way is waited for).</summary>
    public byte[] Bytes(Frame f)
    {
        if (f.Rgba is { } rgba) return rgba;
        var s = Find(f);
        if (s is null)
        {
            s = Free(f);
            var (blocks, bytes, bh) = (f.Blocks!, s.Bytes, f.H / 4);
            // in bands on the pool's threads: the frame is wanted this frame
            System.Threading.Tasks.Parallel.For(0, 8, k => Bc7.Decode(blocks, f.W, f.H, bytes, bh * k / 8, bh * (k + 1) / 8));
        }
        else s.Pending?.Wait();
        s.Pending = null;
        s.Used = ++_use;
        return s.Bytes;
    }

    /// <summary>Starts decoding <paramref name="f"/> on a worker thread, unless a buffer holds it.</summary>
    public void Ahead(Frame? f)
    {
        if (f?.Blocks is not { } blocks || Find(f) is not null) return;
        var s = Free(f);
        var bytes = s.Bytes;
        s.Pending = System.Threading.Tasks.Task.Run(() =>
        {
            // below the main thread's priority: on a busy processor the frame being drawn comes first
            var t = System.Threading.Thread.CurrentThread;
            var was = t.Priority;
            t.Priority = System.Threading.ThreadPriority.BelowNormal;
            try { Bc7.Decode(blocks, f.W, f.H, bytes, 0, f.H / 4); }
            finally { t.Priority = was; }
        });
    }

    /// <summary>Whether <paramref name="f"/>'s RGBA is there without waiting: kept whole, or decoded into a buffer.</summary>
    public bool Ready(Frame f) => f.Rgba is not null || Find(f) is { } s && (s.Pending is null || s.Pending.IsCompleted);

    private Slot? Find(Frame f)
    {
        foreach (var s in _slots) if (s.Frame == f) return s;
        return null;
    }

    /// <summary>The least recently used buffer not being decoded into, given to <paramref name="f"/> (sized for it).</summary>
    private Slot Free(Frame f)
    {
        Slot? best = null;
        foreach (var s in _slots)
            if (s.Pending is null || s.Pending.IsCompleted)
                if (best is null || s.Used < best.Used) best = s;
        // every buffer busy: the oldest, once its decode is done
        if (best is null) { best = _slots.MinBy(s => s.Used)!; best.Pending!.Wait(); }
        best.Pending = null;
        best.Frame = f;
        best.Used = ++_use;
        if (best.Bytes.Length != f.W * f.H * 4) best.Bytes = new byte[f.W * f.H * 4];
        return best;
    }
}
