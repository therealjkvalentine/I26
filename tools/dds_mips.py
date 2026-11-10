"""The HD packs' .dds files (BC7 sRGB, DX10 header, full mip chain): the mip chain made from the top level, the writer, a reader of
every level, and the check that a file's levels are its top level filtered down (tools/dds-mips.py is the command line).

The chain is a box filter in linear light, colour not weighted by alpha: a keyed texel's colour (the colour bled under the key) is
averaged in as it is, so an opaque face, which draws the colour under the key, shows the same picture at every distance. Pillow's
RGBA resize weights colour by alpha (premultiplied): every keyed texel was black in the smaller levels, so whatever draws the colour
under the key (a keyed face's smooth outline, an opaque face before the key colour was drawn there) saw another picture from afar.

Needs numpy, Pillow (reads BC7), and etcpak to write (pip install numpy pillow etcpak)."""
import io, os, struct
import numpy as np
from PIL import Image

DXGI_BC7_SRGB = 99
_BLOCK_BYTES = {70: 8, 71: 8, 72: 8, 73: 16, 74: 16, 75: 16, 76: 16, 77: 16, 78: 16, 97: 16, 98: 16, 99: 16}

def _to_linear(c):
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def _to_srgb8(l):
    l = np.clip(l, 0.0, 1.0)
    c = np.where(l <= 0.0031308, l * 12.92, 1.055 * np.power(l, 1 / 2.4) - 0.055)
    return np.clip(c * 255.0 + 0.5, 0, 255).astype(np.uint8)

def _half(a):
    """A float (h, w, c) array box-filtered to half size (odd sizes by area, as Pillow's BOX resize does), at least 1x1."""
    h, w = a.shape[:2]; nh, nw = max(1, h // 2), max(1, w // 2)
    if h % 2 == 0 and w % 2 == 0:
        return a.reshape(nh, 2, nw, 2, a.shape[2]).mean(axis=(1, 3))
    return np.dstack([np.array(Image.fromarray(np.ascontiguousarray(a[..., k], np.float32), "F").resize((nw, nh), Image.BOX))
                      for k in range(a.shape[2])])

def mip_chain(rgba):
    """The levels of an RGBA8 picture down to 1x1: level 0 the picture itself, each next one the last box-filtered to half, colour
    in linear light and not weighted by alpha, alpha linear."""
    levels = [np.ascontiguousarray(rgba, np.uint8)]
    lin = np.dstack([_to_linear(rgba[..., :3].astype(np.float64)), rgba[..., 3:4].astype(np.float64) / 255.0])
    while lin.shape[0] > 1 or lin.shape[1] > 1:
        lin = _half(lin)
        levels.append(np.dstack([_to_srgb8(lin[..., :3]), np.clip(lin[..., 3:4] * 255.0 + 0.5, 0, 255).astype(np.uint8)]))
    return levels

def _bc7(level, etcpak):
    h, w = level.shape[:2]; pw, ph = (w + 3) // 4 * 4, (h + 3) // 4 * 4
    if (pw, ph) != (w, h): level = np.pad(level, ((0, ph - h), (0, pw - w), (0, 0)), mode="edge")   # the last blocks' unused texels
    return etcpak.compress_bc7(np.ascontiguousarray(level).tobytes(), pw, ph)

def dds_header(w, h, mips, top_bytes):
    hdr = struct.pack("<4sIIIIIII44x", b"DDS ", 124, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000, h, w, top_bytes, 0, mips)
    hdr += struct.pack("<II4s20x", 32, 0x4, b"DX10")                      # pixel format: a DX10 header follows
    hdr += struct.pack("<IIII4x", 0x1000 | 0x400000 | 0x8, 0, 0, 0)       # caps: texture, mipmap, complex
    return hdr + struct.pack("<IIIII", DXGI_BC7_SRGB, 3, 0, 1, 0)          # DXGI_FORMAT_BC7_UNORM_SRGB, a 2D texture

def encode_dds(rgba, etcpak):
    """The bytes of a BC7 (sRGB, DX10 header) .dds of an RGBA8 picture (stored row order) with its full mip chain (mip_chain)."""
    blocks = [_bc7(l, etcpak) for l in mip_chain(rgba)]
    return dds_header(rgba.shape[1], rgba.shape[0], len(blocks), len(blocks[0])) + b"".join(blocks)

def write_dds(path, rgba, etcpak):
    with open(path, "wb") as f: f.write(encode_dds(rgba, etcpak))

def read_header(data):
    h, w, _pitch, _depth, mips = struct.unpack_from("<IIIII", data, 12)
    fourcc = data[84:88]; off = 128; dxgi = None
    if fourcc == b"DX10": dxgi = struct.unpack_from("<I", data, 128)[0]; off = 148
    return dict(w=w, h=h, mips=max(1, mips), fourcc=fourcc, dxgi=dxgi, offset=off)

def read_levels(path, upto=None):
    """Every level of a block-compressed .dds (or levels 0..upto) as RGBA8 arrays in the stored row order, and its header."""
    with open(path, "rb") as f: data = f.read()
    hd = read_header(data)
    bpb = _BLOCK_BYTES.get(hd["dxgi"], 16) if hd["dxgi"] is not None else (8 if hd["fourcc"] == b"DXT1" else 16)
    head = data[:hd["offset"]]; off = hd["offset"]; w, h = hd["w"], hd["h"]; out = []
    for i in range(hd["mips"]):
        n = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * bpb
        if upto is None or i <= upto:
            one = bytearray(head); struct.pack_into("<III", one, 12, h, w, n); struct.pack_into("<I", one, 28, 1)
            im = Image.open(io.BytesIO(bytes(one) + data[off:off + n])); im.load()
            out.append(np.array(im.convert("RGBA")))
        off += n; w, h = max(1, w // 2), max(1, h // 2)
    hd["trailing"] = len(data) - off
    return out, hd

def level1_error(levels):
    """Per texel of level 1: how far its colour is (0-255, its largest channel; not alpha, where BC7 alone can miss by 60 at a
    key's edge) from the top level box-filtered, the nearer of a
    linear-light and a plain sRGB average (both fair filters; a stale or alpha-weighted level is far from both). None without a level 1."""
    if len(levels) < 2: return None
    top, l1 = levels[0], levels[1].astype(np.float32)
    h, w = l1.shape[:2]
    plain = np.dstack([np.array(Image.fromarray(np.ascontiguousarray(top[..., k], np.float32), "F").resize((w, h), Image.BOX)) for k in range(4)])
    lin = mip_chain(top)[1].astype(np.float32)
    return np.minimum(np.abs(plain - l1)[..., :3].max(axis=2), np.abs(lin - l1)[..., :3].max(axis=2))

def check(path, bad_level=48.0, bad_share=0.002):
    """(stale, share of level-1 texels whose colour is off by more than bad_level, share of level-1 keyed texels made black,
    header) for a .dds. Stale when the first share is over bad_share (a level made from its top by BC7 is off by that much on
    under 0.1 % of its texels: fine detail in one block), or when any keyed texel of level 1 is black where the top level's
    colour under it is not (the alpha-weighted resize's mark). A file bigger than 1x1 with no level 1 counts as stale. A small,
    high-contrast texture can pass bad_share by BC7's error alone: over it with no black keyed texel, a file is stale only if its
    level 1 is also off from the filtered top encoded by BC7 here (with etcpak installed), by the same measure."""
    levels, hd = read_levels(path, upto=1)
    if len(levels) < 2: return (hd["w"] > 1 or hd["h"] > 1, 0.0, 0.0, hd)
    e = level1_error(levels)
    share = float((e > bad_level).mean())
    l1 = levels[1]; want = mip_chain(levels[0])[1]
    black = float(((l1[..., 3] < 8) & (l1[..., :3].max(axis=2) < 8) & (want[..., :3].max(axis=2) > 64)).mean())
    stale = share > bad_share or black > 0
    if stale and black == 0 and hd["dxgi"] == DXGI_BC7_SRGB:
        try:
            import etcpak
        except ImportError:
            etcpak = None
        if etcpak is not None:
            h, w = want.shape[:2]
            one = dds_header(w, h, 1, 0) + _bc7(want, etcpak)
            im = Image.open(io.BytesIO(one)); im.load(); enc = np.array(im.convert("RGBA"))[:h, :w].astype(np.int32)
            stale = float((np.abs(enc - l1.astype(np.int32))[..., :3].max(axis=2) > bad_level).mean()) > bad_share
    return (stale, share, black, hd)

def regenerate(path, etcpak, out=None):
    """The file's mip chain made again from its top level (the top level's blocks kept byte for byte); written to `out` or in
    place, through a .part file and a replace (a hard link to the old file keeps the old file)."""
    with open(path, "rb") as f: data = f.read()
    hd = read_header(data)
    if hd["dxgi"] != DXGI_BC7_SRGB: raise ValueError(f"{path}: not BC7 sRGB (DXGI {hd['dxgi']})")
    top = read_levels(path, upto=0)[0][0]
    n0 = max(1, (hd["w"] + 3) // 4) * max(1, (hd["h"] + 3) // 4) * 16
    chain = mip_chain(top)
    body = data[hd["offset"]:hd["offset"] + n0] + b"".join(_bc7(l, etcpak) for l in chain[1:])
    dst = out or path
    with open(dst + ".part", "wb") as f: f.write(dds_header(hd["w"], hd["h"], len(chain), n0) + body)
    os.replace(dst + ".part", dst)
