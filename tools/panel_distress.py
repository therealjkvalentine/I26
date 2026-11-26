"""A light, procedural wear layer for the cockpit's HD panel art (tools/hud-redraw.py; tools/radar-redraw.py --distress).

A layer is a set of maps over one unit's picture, in its original pixels at s texels a pixel, made once from a fixed seed
and applied the same way to every frame or state of that unit (a damage light lit or dark, a counter housing's five screw
angles, the radar's 62 frames), so nothing in it changes from frame to frame:

- grime: a darkening toward a dark olive-brown, strongest where edges meet (corners, round the plates and screws), mottled
  by a smooth noise;
- wear: small chips of the paint lightened toward bare metal, only along the art's own strong edges;
- scratches: a few thin, short, slightly bent hairlines, mostly near horizontal, lighter than the paint with a fainter
  dark edge below;
- ink: printed lettering a little uneven (patches a few per cent fainter, a few pin-holes), never moved.

Every random number comes from numpy's PCG64 seeded with the spec's seed and the unit's name (crc32), drawn on a fixed grid
of 8 texels a pixel and averaged down to the pack's scale, so a 4x and an 8x pack get the same pattern. The strengths are
the spec's levels ("0" is no layer at all: the picture is returned unchanged). Needs numpy and scipy.
"""
import math, zlib
import numpy as np
from scipy import ndimage

GRID = 8          # the random fields' texels a pixel, whatever the pack's scale


def rng_for(seed, name):
    return np.random.default_rng([int(seed), zlib.crc32(name.encode("utf-8"))])


def lum(rgb):
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114


def to_scale(a, s):
    """A field on the 8-a-pixel grid brought to s texels a pixel (block means for 1, 2, 4; zoom otherwise)."""
    if s == GRID: return a
    if GRID % s == 0:
        k = GRID // s; h, w = a.shape[0] // k, a.shape[1] // k
        return a[:h * k, :w * k].reshape(h, k, w, k).mean((1, 3))
    return ndimage.zoom(a, s / GRID, order=1)


def noise(rng, h, w, sigma_px, s):
    """A smooth noise field (zero mean, unit spread) of h x w pixels at s texels a pixel, its features sigma_px pixels."""
    n = rng.standard_normal((h * GRID, w * GRID))
    n = ndimage.gaussian_filter(n, sigma_px * GRID, mode="wrap")
    n = to_scale(n, s)
    sd = n.std()
    return (n - n.mean()) / (sd if sd > 0 else 1.0)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def edges(rgba, s, blur_px=0.5):
    """The structure the layer follows: the luminance gradient (x, y; per original pixel) of the art, opaque texels only."""
    a = rgba[..., 3].astype(float) / 255
    L = lum(rgba[..., :3].astype(float)) * a
    Lb = ndimage.gaussian_filter(L, blur_px * s)
    gy, gx = np.gradient(Lb)
    return gx * s, gy * s


def scratch_maps(rng, h, w, s, p):
    """Scratches as two coverages (light hairline, the dark edge below it) on h x w pixels at s."""
    light = np.zeros((h * s, w * s)); dark = np.zeros_like(light)
    n = int(round(p["scratches"] * h * w / 1000.0))
    for _ in range(n):
        x0, y0 = rng.uniform(0, w), rng.uniform(0, h)
        ang = rng.normal(0, p.get("scratch_spread_deg", 22)) if rng.random() < 0.7 else rng.uniform(-90, 90)
        L = float(np.clip(rng.lognormal(math.log(p.get("scratch_len", 4.0)), 0.5), 1.2, 3 * p.get("scratch_len", 4.0)))
        bend = rng.normal(0, 0.12) * L
        a = rng.uniform(0.45, 1.0)
        th = math.radians(ang); u = np.array([math.cos(th), math.sin(th)]); nrm = np.array([-u[1], u[0]])
        k = max(6, int(L * 4))
        t = np.linspace(0, 1, k)
        P = np.array([x0, y0]) + np.outer(t * L, u) + np.outer(4 * t * (1 - t) * bend, nrm)
        wid = p.get("scratch_width", 0.2)
        bx0 = int(max(0, math.floor((P[:, 0].min() - 1) * s))); bx1 = int(min(w * s, math.ceil((P[:, 0].max() + 1) * s)))
        by0 = int(max(0, math.floor((P[:, 1].min() - 1) * s))); by1 = int(min(h * s, math.ceil((P[:, 1].max() + 1.5) * s)))
        if bx1 <= bx0 or by1 <= by0: continue
        X = (np.arange(bx0, bx1) + 0.5)[None, :, None] / s; Y = (np.arange(by0, by1) + 0.5)[:, None, None] / s
        # distance to the polyline's points (dense enough at 4 a pixel), the intensity tapered at both ends
        d = np.hypot(X - P[None, None, :, 0], Y - P[None, None, :, 1])
        j = d.argmin(-1); dm = d.min(-1)
        taper = np.sin(np.pi * np.clip(t[j], 0, 1)) ** 0.6
        cov = np.clip(0.5 - (dm - wid / 2) * s, 0, 1) * taper * a
        light[by0:by1, bx0:bx1] = np.maximum(light[by0:by1, bx0:bx1], cov)
        # the dark edge: the same line a quarter pixel lower (down the picture), fainter
        d2 = np.hypot(X - P[None, None, :, 0], Y - (P[None, None, :, 1] + 0.25))
        j2 = d2.argmin(-1)
        cov2 = np.clip(0.5 - (d2.min(-1) - wid / 2) * s, 0, 1) * np.sin(np.pi * np.clip(t[j2], 0, 1)) ** 0.6 * a
        dark[by0:by1, bx0:bx1] = np.maximum(dark[by0:by1, bx0:bx1], cov2 * (1 - cov))
    return light, dark


def make_layer(struct, s, level, seed, name, mask=None, ink=None, scratch_mask=None, wear_mask=None, ink_zone=None):
    """The layer's maps for a unit whose art (RGBA uint8 at s texels a pixel) is struct: {grime, wear, scr_l, scr_d, ink}
    (floats, struct's size), or None for level None (no layer). mask: where the layer may act at all (0..1; default the
    opaque texels); ink: the printed lettering's coverage (0..1); scratch_mask: where scratches may lie (default mask);
    wear_mask: where paint may chip (default everywhere the mask allows); ink_zone: where the lettering's paper is read from
    (default where ink was looked for: everywhere)."""
    if not level: return None
    p = level; H, W = struct.shape[:2]; h, w = H // s, W // s
    rng = rng_for(seed, name)
    m = (struct[..., 3] > 127).astype(float) if mask is None else mask
    if ink is not None and np.any(ink > 0):      # the lettering is print, not relief: the edges are found without it
        rgb = struct[..., :3].astype(float); k = np.clip(ink * 1.5, 0, 1)[..., None]
        flat = struct.copy(); flat[..., :3] = np.clip(rgb * (1 - k) + paper_of(rgb, (ink > 0.05).astype(float), s) * k, 0, 255).astype(np.uint8)
        gx, gy = edges(flat, s)
    else:
        gx, gy = edges(struct, s)
    lo, hi = p.get("edge_lo", 18.0), p.get("edge_hi", 40.0)
    strong = smoothstep(lo, hi, np.hypot(gx, gy))
    # grime: in corners (edges across and along meeting), a little along the edges, mottled; a faint mottle all over
    r = p.get("grime_reach", 1.6) * s
    cx = ndimage.gaussian_filter(smoothstep(lo, hi, np.abs(gx)), r); cy = ndimage.gaussian_filter(smoothstep(lo, hi, np.abs(gy)), r)
    corner = np.sqrt(cx * cy); corner = np.clip(corner / max(np.percentile(corner, 99.5), 1e-6), 0, 1)
    along = ndimage.gaussian_filter(strong, r); along = np.clip(along / max(np.percentile(along, 99), 1e-6), 0, 1)
    N = 0.5 + 0.5 * np.tanh(noise(rng, h, w, p.get("mottle_px", 3.0), s) * 1.1)
    N2 = 0.5 + 0.5 * np.tanh(noise(rng, h, w, 1.4, s))
    G = p["grime"] * (0.8 * corner ** 1.1 * (0.3 + 0.7 * N) * (0.85 + 0.15 * N2) + 0.3 * along * N * N + p.get("mottle", 0.12) * N)
    # wear: small chips along strong edges (not printed lettering, wear_mask)
    P_ = ndimage.gaussian_filter(strong, p.get("chip_reach", 0.5) * s)
    P_ = np.clip(P_ / max(P_.max(), 1e-6), 0, 1)
    nf = noise(rng, h, w, p.get("chip_px", 0.32), s); nm = noise(rng, h, w, 2.5, s)
    score = (nf + 0.7 * nm) / math.sqrt(1.49)
    chip = smoothstep(p["chip_threshold"], p["chip_threshold"] + 0.3, score) * smoothstep(0.3, 0.7, P_)
    Wv = chip * p["wear"] * (1.0 if wear_mask is None else wear_mask)
    # scratches
    sl, sd = scratch_maps(rng, h, w, s, p)
    sm = m if scratch_mask is None else scratch_mask
    out = {"grime": G * m, "wear": Wv * m, "scr_l": sl * p["scratch"] * sm, "scr_d": sd * p["scratch"] * p.get("scratch_dark", 0.5) * sm}
    # the lettering: fainter patches and pin-holes, the edges a little rough
    if ink is not None:
        ni = 0.5 + 0.5 * np.tanh(noise(rng, h, w, p.get("ink_patch_px", 1.3), s) * 1.2 - 0.3)
        pin = smoothstep(p.get("pin_threshold", 2.6), p.get("pin_threshold", 2.6) + 0.3, noise(rng, h, w, 0.22, s))
        rough = smoothstep(1.2, 2.0, noise(rng, h, w, 0.3, s)) * (ink < 0.9) * (ink > 0.05)
        F = np.clip(p["ink"] * ni + p.get("ink_pins", 0.7) * pin + p.get("ink_rough", 0.6) * rough, 0, 1)
        out["ink"] = F * ink * m; out["ink_cov"] = ink * m
        out["ink_zone"] = np.ones((H, W)) if ink_zone is None else ink_zone.astype(float)
    else:
        out["ink"] = np.zeros((H, W)); out["ink_cov"] = out["ink"]; out["ink_zone"] = out["ink"]
    return out


def paper_of(rgb, ink, s, sigma_px=1.5, zone=None):
    """The colour round printed letters: the non-ink texels' colour (inside zone, the plate the letters are on) spread over
    the ink (normalised convolution)."""
    wgt = np.clip(1 - ink * 4, 0, 1) * (1 if zone is None else zone)
    num = np.stack([ndimage.gaussian_filter(rgb[..., k] * wgt, sigma_px * s) for k in range(3)], -1)
    den = ndimage.gaussian_filter(wgt, sigma_px * s)[..., None]
    return num / np.maximum(den, 1e-6)


def apply_float(img, layer, colours, sl=(slice(None), slice(None)), paper=None, s=1):
    """img (h, w, 3 float, the layer's size or the slice sl of it) with the layer applied, in place."""
    g = lambda k: layer[k][sl]
    ink = g("ink")
    if np.any(ink > 0):
        pp = paper if paper is not None else paper_of(img, (g("ink_cov") > 0.05).astype(float), s, zone=g("ink_zone"))
        img *= 1 - ink[..., None]; img += pp * ink[..., None]
    for key, col in (("grime", "grime"), ("wear", "metal"), ("scr_d", "scratch_dark"), ("scr_l", "scratch")):
        a = g(key)[..., None]
        img *= 1 - a; img += np.asarray(colours[col], float) * a
    return img


def apply_layer(rgba, layer, colours, region=None, paper=None, s=1):
    """rgba (uint8, the layer's size, or the region (x0, y0, x1, y1 in texels) of the layer that rgba covers) with the
    layer applied; alpha is left as it is. colours: grime, metal, scratch, scratch_dark (RGB)."""
    if layer is None: return rgba
    sl = (slice(None), slice(None)) if region is None else (slice(region[1], region[3]), slice(region[0], region[2]))
    img = apply_float(rgba[..., :3].astype(float), layer, colours, sl, paper, s)
    out = rgba.copy()
    out[..., :3] = np.clip(np.round(img), 0, 255).astype(np.uint8)
    return out


def ink_mask(rgba, s, box_px, contrast=(0.5, 0.7), dark=(80, 50), sigma_px=1.6):
    """Printed letters' coverage inside box_px (x0, y0, x1, y1 original pixels): texels much darker than the paper round
    them and dark in themselves (a lens's own shading is not print)."""
    H, W = rgba.shape[:2]
    out = np.zeros((H, W))
    x0, y0, x1, y1 = [int(round(v * s)) for v in box_px]
    x0, y0 = max(x0, 0), max(y0, 0); x1, y1 = min(x1, W), min(y1, H)
    L = lum(rgba[y0:y1, x0:x1, :3].astype(float))
    paper = ndimage.maximum_filter(ndimage.gaussian_filter(L, 0.3 * s), size=int(2 * sigma_px * s) | 1)
    paper = ndimage.gaussian_filter(paper, sigma_px * s)
    rel = (paper - L) / np.maximum(paper, 1.0)
    out[y0:y1, x0:x1] = smoothstep(contrast[0], contrast[1], rel) * (paper > 60) * smoothstep(dark[0], dark[1], L)
    return out
