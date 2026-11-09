using I76.Game.Sim;
using Xunit;

namespace I76.Game.Tests;

/// <summary>
/// The radar's frames kept as BC7 and decoded when shown (RadarFrames, Bc7): the block layouts the specification gives, the hand-laid
/// modes against the general decode, and the decoded buffers giving every frame's texels whatever order they are asked in.
/// </summary>
public class RadarFramesTests
{
    /// <summary>Fields written from the block's lowest bit, as the specification lays them.</summary>
    private sealed class Writer
    {
        private System.Numerics.BigInteger _v;
        private int _n;
        public Writer Put(int value, int bits) { _v |= (System.Numerics.BigInteger)value << _n; _n += bits; return this; }
        public byte[] Bytes()
        {
            Assert.Equal(128, _n);
            var b = _v.ToByteArray(isUnsigned: true, isBigEndian: false);
            var o = new byte[16];
            b.AsSpan(0, System.Math.Min(16, b.Length)).CopyTo(o);
            return o;
        }
    }

    private static readonly int[] W4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    private static uint[] Decode(byte[] block, bool general = false)
    {
        var px = new uint[16];
        if (general) Bc7.General(block, px, 4);
        else Bc7.Block(block, px, 4);
        return px;
    }

    private static uint Rgba(int r, int g, int b, int a) => (uint)(r | g << 8 | b << 16 | a << 24);

    [Fact]
    public void Mode6IsSevenBitEndpointsWithAPBitEachAndFourBitIndices()
    {
        // endpoint 0 (p 1): R 127, G 0, B 64, A 127; endpoint 1 (p 0): R 0, G 127, B 64, A 127; texel i at index i (texel 0's 3 bits: 0)
        var w = new Writer().Put(0x40, 7).Put(127, 7).Put(0, 7).Put(0, 7).Put(127, 7).Put(64, 7).Put(64, 7).Put(127, 7).Put(127, 7).Put(1, 1).Put(0, 1);
        w.Put(0, 3);
        for (int i = 1; i < 16; i++) w.Put(i, 4);
        var px = Decode(w.Bytes());
        static int L(int a, int b, int t) => ((64 - t) * a + t * b + 32) >> 6;
        for (int i = 0; i < 16; i++)
            Assert.Equal(Rgba(L(255, 0, W4[i]), L(1, 254, W4[i]), L(129, 128, W4[i]), L(255, 254, W4[i])), px[i]);
    }

    [Fact]
    public void Mode1SplitsTheBlockByItsPartition()
    {
        // partition 13: the top two rows subset 0 (black), the bottom two subset 1 (white: 63 and the subset's p-bit 1); indices 0
        var w = new Writer().Put(0b10, 2).Put(13, 6);
        for (int c = 0; c < 3; c++) w.Put(0, 6).Put(0, 6).Put(63, 6).Put(63, 6);
        w.Put(0, 1).Put(1, 1).Put(0, 46);
        var px = Decode(w.Bytes());
        for (int i = 0; i < 16; i++) Assert.Equal(i < 8 ? Rgba(0, 0, 0, 255) : Rgba(255, 255, 255, 255), px[i]);
    }

    [Fact]
    public void NoModeBitIsTransparentBlack()
    {
        Assert.All(Decode(new byte[16]), p => Assert.Equal(0u, p));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    public void TheHandLaidModesDecodeAsTheGeneralOne(int mode)
    {
        var rng = new System.Random(mode);
        var block = new byte[16];
        for (int n = 0; n < 20000; n++)
        {
            rng.NextBytes(block);
            block[0] = (byte)((block[0] & ~((2 << mode) - 1)) | 1 << mode);
            Assert.Equal(Decode(block, general: true), Decode(block));
        }
    }

    /// <summary>A w x h picture of random mode-6 and mode-1 blocks.</summary>
    private static byte[] Blocks(int w, int h, int seed)
    {
        var rng = new System.Random(seed);
        var b = new byte[w * h];
        rng.NextBytes(b);
        for (int i = 0; i < b.Length; i += 16) b[i] = (byte)(rng.Next(2) == 0 ? (b[i] & 0x80) | 0x40 : (b[i] & 0xfc) | 2);
        return b;
    }

    private static byte[] Expected(RadarFrames.Frame f, byte[] blocks)
    {
        var px = new byte[f.W * f.H * 4];
        Bc7.Decode(blocks, f.W, f.H, px, 0, f.H / 4);
        return px;
    }

    [Fact]
    public void EveryFrameDecodesTheSameWhateverTheOrderAskedAndAhead()
    {
        var r = new RadarFrames();
        var blocks = new byte[31][];
        for (int i = 0; i < 31; i++) r.Sweep[i] = RadarFrames.Compressed(16, 8, blocks[i] = Blocks(16, 8, i));
        var want = Enumerable.Range(0, 31).Select(i => Expected(r.Sweep[i]!, blocks[i])).ToArray();
        // the sweep: each step's frame, the next decoded ahead; then jumps back and forth (a target held, the screen off)
        for (int lap = 0; lap < 2; lap++)
            for (int i = 0; i < 30; i++)
            {
                Assert.Equal(want[i], r.Bytes(r.Sweep[i]!));
                r.Ahead(r.Sweep[(i + 1) % 30]);
            }
        foreach (int i in new[] { 30, 3, 17, 4, 2, 30, 29, 0 })
        {
            r.Ahead(r.Sweep[(i + 1) % 31]);
            r.Ahead(r.Sweep[(i + 30) % 31]);
            Assert.Equal(want[i], r.Bytes(r.Sweep[i]!));
        }
        // the frames as stored and four decoded buffers, no more
        Assert.Equal(31L * 16 * 8 + 4L * 16 * 8 * 4, r.Kept);
    }

    [Fact]
    public void AFrameIsReadyOnceDecoded()
    {
        var r = new RadarFrames();
        r.Sweep[0] = RadarFrames.Compressed(16, 8, Blocks(16, 8, 5));
        r.Sweep[1] = RadarFrames.Compressed(16, 8, Blocks(16, 8, 6));
        Assert.False(r.Ready(r.Sweep[0]!));
        r.Bytes(r.Sweep[0]!);
        Assert.True(r.Ready(r.Sweep[0]!));
        r.Ahead(r.Sweep[1]);
        r.Bytes(r.Sweep[1]!);   // waits for the worker's decode
        Assert.True(r.Ready(r.Sweep[1]!));
    }

    [Fact]
    public void AFrameKeptWholeIsGivenAsItIs()
    {
        var px = new byte[16 * 8 * 4];
        new System.Random(3).NextBytes(px);
        var r = new RadarFrames();
        r.Sweep[0] = RadarFrames.Whole(16, 8, px);
        r.Ahead(r.Sweep[0]);
        Assert.Same(px, r.Bytes(r.Sweep[0]!));
        Assert.Equal(px.Length, r.Kept);
    }
}
