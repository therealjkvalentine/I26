namespace I76.Game.Sim;

/// <summary>
/// BC7 (BPTC) blocks to RGBA8, as the format's specification decodes them (the engine's decompression gives the same bytes). The
/// radar keeps a pack's frames as the pack stores them, a byte a texel, and decodes a frame into RGBA when it is about to be shown.
/// </summary>
public static class Bc7
{
    // per mode: subsets, partition bits, rotation bits, index-selection bits, colour bits, alpha bits, a p-bit per endpoint,
    // a p-bit per subset, index bits, second index bits
    private static readonly byte[] Modes =
    [
        3, 4, 0, 0, 4, 0, 1, 0, 3, 0,
        2, 6, 0, 0, 6, 0, 0, 1, 3, 0,
        3, 6, 0, 0, 5, 0, 0, 0, 2, 0,
        2, 6, 0, 0, 7, 0, 1, 0, 2, 0,
        1, 0, 2, 1, 5, 6, 0, 0, 2, 3,
        1, 0, 2, 0, 7, 8, 0, 0, 2, 2,
        1, 0, 0, 0, 7, 7, 1, 0, 4, 0,
        2, 6, 0, 0, 5, 5, 1, 0, 2, 0,
    ];

    /// <summary>The two-subset partitions: bit i set, texel i (row-major) in subset 1.</summary>
    private static readonly ushort[] P2 =
    [
        0xcccc, 0x8888, 0xeeee, 0xecc8, 0xc880, 0xfeec, 0xfec8, 0xec80,
        0xc800, 0xffec, 0xfe80, 0xe800, 0xffe8, 0xff00, 0xfff0, 0xf000,
        0xf710, 0x008e, 0x7100, 0x08ce, 0x008c, 0x7310, 0x3100, 0x8cce,
        0x088c, 0x3110, 0x6666, 0x366c, 0x17e8, 0x0ff0, 0x718e, 0x399c,
        0xaaaa, 0xf0f0, 0x5a5a, 0x33cc, 0x3c3c, 0x55aa, 0x9696, 0xa55a,
        0x73ce, 0x13c8, 0x324c, 0x3bdc, 0x6996, 0xc33c, 0x9966, 0x0660,
        0x0272, 0x04e4, 0x4e40, 0x2720, 0xc936, 0x936c, 0x39c6, 0x639c,
        0x9336, 0x9cc6, 0x817e, 0xe718, 0xccf0, 0x0fcc, 0x7744, 0xee22,
    ];

    /// <summary>The three-subset partitions: two bits a texel, its subset.</summary>
    private static readonly uint[] P3 =
    [
        0xaa685050, 0x6a5a5040, 0x5a5a4200, 0x5450a0a8, 0xa5a50000, 0xa0a05050, 0x5555a0a0, 0x5a5a5050,
        0xaa550000, 0xaa555500, 0xaaaa5500, 0x90909090, 0x94949494, 0xa4a4a4a4, 0xa9a59450, 0x2a0a4250,
        0xa5945040, 0x0a425054, 0xa5a5a500, 0x55a0a0a0, 0xa8a85454, 0x6a6a4040, 0xa4a45000, 0x1a1a0500,
        0x0050a4a4, 0xaaa59090, 0x14696914, 0x69691400, 0xa08585a0, 0xaa821414, 0x50a4a450, 0x6a5a0200,
        0xa9a58000, 0x5090a0a8, 0xa8a09050, 0x24242424, 0x00aa5500, 0x24924924, 0x24499224, 0x50a50a50,
        0x500aa550, 0xaaaa4444, 0x66660000, 0xa5a0a5a0, 0x50a050a0, 0x69286928, 0x44aaaa44, 0x66666600,
        0xaa444444, 0x54a854a8, 0x95809580, 0x96969600, 0xa85454a8, 0x80959580, 0xaa141414, 0x96960000,
        0xaaaa1414, 0xa05050a0, 0xa0a5a5a0, 0x96000000, 0x40804080, 0xa9a8a9a8, 0xaaaaaa44, 0x2a4a5254,
    ];

    /// <summary>The texel whose index has one bit fewer: subset 1 of two; subsets 1 and 2 of three (subset 0's is texel 0).</summary>
    private static readonly byte[] Anchor2 =
    [
        15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15,
        15, 2, 8, 2, 2, 8, 8, 15, 2, 8, 2, 2, 8, 8, 2, 2,
        15, 15, 6, 8, 2, 8, 15, 15, 2, 8, 2, 2, 2, 15, 15, 6,
        6, 2, 6, 8, 15, 15, 2, 2, 15, 15, 15, 15, 15, 2, 2, 15,
    ];
    private static readonly byte[] Anchor3a =
    [
        3, 3, 15, 15, 8, 3, 15, 15, 8, 8, 6, 6, 6, 5, 3, 3,
        3, 3, 8, 15, 3, 3, 6, 10, 5, 8, 8, 6, 8, 5, 15, 15,
        8, 15, 3, 5, 6, 10, 8, 15, 15, 3, 15, 5, 15, 15, 15, 15,
        3, 15, 5, 5, 5, 8, 5, 10, 5, 10, 8, 13, 15, 12, 3, 3,
    ];
    private static readonly byte[] Anchor3b =
    [
        15, 8, 8, 3, 15, 15, 3, 8, 15, 15, 15, 15, 15, 15, 15, 8,
        15, 8, 15, 3, 15, 8, 15, 8, 3, 15, 6, 10, 15, 15, 10, 8,
        15, 3, 15, 10, 10, 8, 9, 10, 6, 15, 8, 15, 3, 6, 6, 8,
        15, 3, 15, 15, 15, 15, 15, 15, 15, 15, 15, 15, 3, 15, 15, 8,
    ];

    private static readonly byte[] W2 = [0, 21, 43, 64], W3 = [0, 9, 18, 27, 37, 46, 55, 64],
        W4 = [0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64];

    private static byte[] Weights(int bits) => bits == 2 ? W2 : bits == 3 ? W3 : W4;

    /// <summary>The block rows [<paramref name="by0"/>, <paramref name="by1"/>) of a w x h picture from its blocks into <paramref name="rgba"/> (w x h RGBA8).</summary>
    public static void Decode(ReadOnlySpan<byte> blocks, int w, int h, Span<byte> rgba, int by0, int by1)
    {
        int bw = w / 4;
        var px = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(rgba);
        for (int by = by0; by < by1; by++)
            for (int bx = 0; bx < bw; bx++)
                Block(blocks.Slice((by * bw + bx) * 16, 16), px.Slice(by * 4 * w + bx * 4), w);
    }

    /// <summary>One block's 4 x 4 texels (RGBA8 as little-endian words) into <paramref name="dst"/>, its rows <paramref name="stride"/> texels apart.</summary>
    public static void Block(ReadOnlySpan<byte> b, Span<uint> dst, int stride)
    {
        ulong lo = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(b), hi = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(8));
        if ((lo & 0x7f) == 0x40) Mode6(lo, hi, dst, stride);
        else if ((lo & 3) == 2) Mode1(lo, hi, dst, stride);
        else General(b, dst, stride);
    }

    /// <summary>The specification's decode of a block of any mode (<see cref="Block"/> lays modes 1 and 6 out by hand, to the same texels).</summary>
    [System.Runtime.CompilerServices.SkipLocalsInit]
    public static void General(ReadOnlySpan<byte> b, Span<uint> dst, int stride)
    {
        var r = new Bits(System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(b), System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(8)));
        int mode = b[0] == 0 ? 8 : System.Numerics.BitOperations.TrailingZeroCount(b[0]);
        if (mode == 8)
        {
            // no mode bit: every texel zero
            for (int y = 0; y < 4; y++) dst.Slice(y * stride, 4).Clear();
            return;
        }
        r.Read(mode + 1);
        var m = Modes.AsSpan(mode * 10, 10);
        int ns = m[0], cb = m[4], ab = m[5], ib = m[8], ib2 = m[9];
        int partition = r.Read(m[1]), rotation = r.Read(m[2]), sel = r.Read(m[3]);
        int ne = ns * 2;
        Span<int> ep = stackalloc int[24];   // endpoint e's channel c at e * 4 + c
        for (int c = 0; c < 3; c++)
            for (int e = 0; e < ne; e++) ep[e * 4 + c] = r.Read(cb);
        for (int e = 0; e < ne; e++) ep[e * 4 + 3] = ab > 0 ? r.Read(ab) : 255;
        int cbits = cb, abits = ab;
        if (m[6] != 0)
        {
            for (int e = 0; e < ne; e++)
            {
                int p = r.Read(1);
                for (int c = 0; c < (ab > 0 ? 4 : 3); c++) ep[e * 4 + c] = ep[e * 4 + c] << 1 | p;
            }
            cbits++;
            if (ab > 0) abits++;
        }
        else if (m[7] != 0)
        {
            for (int s = 0; s < ns; s++)
            {
                int p = r.Read(1);
                for (int e = s * 2; e < s * 2 + 2; e++)
                    for (int c = 0; c < 3; c++) ep[e * 4 + c] = ep[e * 4 + c] << 1 | p;
            }
            cbits++;
        }
        for (int e = 0; e < ne; e++)
        {
            for (int c = 0; c < 3; c++) ep[e * 4 + c] = Expand(ep[e * 4 + c], cbits);
            if (ab > 0) ep[e * 4 + 3] = Expand(ep[e * 4 + 3], abits);
        }
        // the colour's and the alpha's index widths: one set of indices, or two (mode 4's selection bit swaps them)
        int cIb = ib, aIb = ib2 > 0 ? ib2 : ib;
        if (sel != 0) (cIb, aIb) = (aIb, cIb);
        // each subset's palette: the colour at every index (the alpha too, with one set of indices), and with two sets the alpha's
        // own; a rotation swaps the alpha with a channel, so the channels go to their bytes in the word here
        int rs = rotation == 0 ? 24 : (rotation - 1) * 8;   // where the alpha's value goes; the swapped channel goes to byte 3
        Span<uint> pal = stackalloc uint[48];
        Span<uint> apal = stackalloc uint[8];
        var cw = Weights(cIb);
        for (int s = 0; s < ns; s++)
        {
            int e0 = s * 8, e1 = e0 + 4;
            for (int j = 0; j < cw.Length; j++)
            {
                int w = cw[j];
                uint R = (uint)Lerp(ep[e0], ep[e1], w), G = (uint)Lerp(ep[e0 + 1], ep[e1 + 1], w), B = (uint)Lerp(ep[e0 + 2], ep[e1 + 2], w);
                uint col = rotation switch { 1 => R << 24 | G << 8 | B << 16, 2 => R | G << 24 | B << 16, 3 => R | G << 8 | B << 24, _ => R | G << 8 | B << 16 };
                if (ib2 == 0) col |= (uint)(ab > 0 ? Lerp(ep[e0 + 3], ep[e1 + 3], w) : 255) << 24;
                pal[s * 16 + j] = col;
            }
        }
        if (ib2 > 0)
        {
            var aw = Weights(aIb);
            for (int j = 0; j < aw.Length; j++) apal[j] = (uint)Lerp(ep[3], ep[7], aw[j]) << rs;
        }
        int a1 = ns == 2 ? Anchor2[partition] : ns == 3 ? Anchor3a[partition] : 0, a2 = ns == 3 ? Anchor3b[partition] : 0;
        uint p2 = ns == 2 ? P2[partition] : 0u, p3 = ns == 3 ? P3[partition] : 0u;
        Span<uint> outp = stackalloc uint[16];
        if (ib2 == 0)
        {
            for (int i = 0; i < 16; i++)
            {
                bool anchor = i == 0 || (ns > 1 && i == a1) || (ns > 2 && i == a2);
                int s = ns == 1 ? 0 : ns == 2 ? (int)(p2 >> i & 1) : (int)(p3 >> (2 * i) & 3);
                outp[i] = pal[s * 16 + r.Read(anchor ? ib - 1 : ib)];
            }
        }
        else
        {
            // one subset, two sets of indices: the first the colour's and the second the alpha's, unless the selection bit swapped them
            Span<int> first = stackalloc int[16];
            for (int i = 0; i < 16; i++) first[i] = r.Read(i == 0 ? ib - 1 : ib);
            for (int i = 0; i < 16; i++)
            {
                int second = r.Read(i == 0 ? ib2 - 1 : ib2);
                outp[i] = sel == 0 ? pal[first[i]] | apal[second] : pal[second] | apal[first[i]];
            }
        }
        for (int y = 0; y < 4; y++) outp.Slice(y * 4, 4).CopyTo(dst.Slice(y * stride, 4));
    }

    /// <summary>
    /// Mode 6, most of a pack's blocks (70 % of the radar's): one subset, 7-bit RGBA endpoints with a p-bit each, 4-bit indices (bits 65-127). The same as
    /// the general decode, laid out by hand.
    /// </summary>
    private static void Mode6(ulong lo, ulong hi, Span<uint> dst, int stride)
    {
        int p0 = (int)(lo >> 63), p1 = (int)(hi & 1);
        Span<uint> pal = stackalloc uint[16];
        int r0 = E6(lo, 7, p0), r1 = E6(lo, 14, p1), g0 = E6(lo, 21, p0), g1 = E6(lo, 28, p1), b0 = E6(lo, 35, p0), b1 = E6(lo, 42, p1), a0 = E6(lo, 49, p0), a1 = E6(lo, 56, p1);
        for (int j = 0; j < 16; j++)
        {
            int w = W4[j];
            pal[j] = (uint)Lerp(r0, r1, w) | (uint)Lerp(g0, g1, w) << 8 | (uint)Lerp(b0, b1, w) << 16 | (uint)Lerp(a0, a1, w) << 24;
        }
        for (int y = 0; y < 4; y++)
        {
            var row = dst.Slice(y * stride, 4);
            for (int x = 0; x < 4; x++)
            {
                int i = y * 4 + x;
                row[x] = pal[i == 0 ? (int)(hi >> 1 & 7) : (int)(hi >> (4 * i) & 15)];
            }
        }
    }

    /// <summary>
    /// Mode 1, most of the rest (27 % of the radar's): two subsets, 6-bit RGB endpoints with a p-bit a subset (bits 80, 81), 3-bit indices from bit 82, a
    /// bit fewer at texel 0 and the second subset's anchor. The same as the general decode, laid out by hand.
    /// </summary>
    private static void Mode1(ulong lo, ulong hi, Span<uint> dst, int stride)
    {
        int part = (int)(lo >> 2 & 63);
        Span<uint> pal = stackalloc uint[16];
        for (int s = 0; s < 2; s++)
        {
            int p = (int)(hi >> (16 + s) & 1);
            // channel c of endpoint e at bit 8 + 24 c + 6 e
            int r0 = E1(lo, hi, 8 + 12 * s, p), r1 = E1(lo, hi, 14 + 12 * s, p), g0 = E1(lo, hi, 32 + 12 * s, p), g1 = E1(lo, hi, 38 + 12 * s, p),
                b0 = E1(lo, hi, 56 + 12 * s, p), b1 = E1(lo, hi, 62 + 12 * s, p);
            for (int j = 0; j < 8; j++)
            {
                int w = W3[j];
                pal[s * 8 + j] = (uint)Lerp(r0, r1, w) | (uint)Lerp(g0, g1, w) << 8 | (uint)Lerp(b0, b1, w) << 16 | 0xff000000u;
            }
        }
        int anchor = Anchor2[part], at = 82;
        uint sub = P2[part];
        for (int i = 0; i < 16; i++)
        {
            int n = i == 0 || i == anchor ? 2 : 3;
            dst[(i >> 2) * stride + (i & 3)] = pal[(int)(sub >> i & 1) * 8 + Get(lo, hi, at, n)];
            at += n;
        }
    }

    /// <summary>A mode-1 endpoint channel: 6 bits at <paramref name="at"/> and the subset's p-bit, to 8 bits.</summary>
    private static int E1(ulong lo, ulong hi, int at, int p)
    {
        int v = Get(lo, hi, at, 6) << 1 | p;
        return v << 1 | v >> 6;
    }

    /// <summary><paramref name="n"/> bits of the block's 128 from bit <paramref name="at"/>.</summary>
    private static int Get(ulong lo, ulong hi, int at, int n) =>
        (int)((at >= 64 ? hi >> (at - 64) : at + n <= 64 ? lo >> at : lo >> at | hi << (64 - at)) & ((1UL << n) - 1));

    /// <summary>A mode-6 endpoint channel: 7 bits at <paramref name="at"/> and the p-bit, to 8 bits.</summary>
    private static int E6(ulong lo, int at, int p) => (int)(lo >> at & 0x7f) << 1 | p;

    private static int Expand(int v, int bits) => v << (8 - bits) | v >> (2 * bits - 8);

    private static int Lerp(int a, int b, int w) => ((64 - w) * a + w * b + 32) >> 6;

    /// <summary>The block's 128 bits, read from the lowest.</summary>
    private ref struct Bits(ulong lo, ulong hi)
    {
        private ulong _lo = lo, _hi = hi;

        public int Read(int n)
        {
            if (n == 0) return 0;
            int v = (int)(_lo & ((1UL << n) - 1));
            _lo = _lo >> n | _hi << (64 - n);
            _hi >>= n;
            return v;
        }
    }
}
