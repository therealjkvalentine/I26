#!/usr/bin/env python3
"""Letter an HD texture pack's lettering again from a font (the modern look's "font redraws"), CPU only, in place.

The 1997 art paints its lettering in a few texels (the speedometer's figures, the damage panel's maker's plate, the Piranha's
licence plate and badge); upscaled by a model it comes out soft and blobby. This step takes such a texture as the pack holds it
(tools/upscale-textures.py's output: BC7 .dds or .png), takes the painted letters off (filled from round them, cut out of the
key, or a colour) and sets the same words again from a free font at the pack's resolution, then writes the texture back in the
pack's format, mipmaps made again. The game loads the pack as before: no runtime cost, and the 1997 look, which never draws the
pack, is untouched.

    python3 tools/text-redraws.py SRC_DIR [--pack DIR] [--only GLOB] [--list] [--restore] [--self-check]

SRC_DIR is the folder --export-textures wrote (the originals at their own size, and bottom-up.txt, the textures' stored row
order); --pack the pack folder (default: the game's tex-world-4x). One spec a texture family in tools/text-redraws/*.json
(below); each texture it lists is redrawn from its base: the pack's file as the model left it, copied the first time into
PACK/.text-base/ (a sub-folder the game does not look in), so a run can be repeated after a spec changes and --restore puts the
model's files back. pack.json gets a "text_redraws" list (texture, spec, fonts) and ATTRIBUTION.txt a line per font used.
Needs Python 3.10+, numpy, Pillow, and etcpak for .dds (pip install numpy pillow etcpak).

A spec (coordinates in the original's texels, upright, so the same spec serves a 2x, 3x or 4x pack):
    {"textures": ["zsy_"], "note": "...",
     "ops": [
       {"op": "erase", "rect": [x0, y0, x1, y1], "where": {"lum_below": 60}, "grow": 0.6, "fill": "inpaint"},
       {"op": "text", "text": "MR.DAMAGE", "font": "LeagueGothic", "cap": 7, "center": [203, 54.5], "width": 33,
        "colour": [18, 18, 14], "rotate": 0, "skew": 0, "tracking": 0, "stroke": {"width": 0.5, "colour": [0, 0, 0]},
        "shadow": {"dx": 0.3, "dy": 0.3, "colour": [0, 0, 0], "opacity": 0.5}, "gradient": [[255, 255, 255], [150, 150, 150]],
        "alpha": "keep"}]}
erase: the texels of rect (all, or those of the original matching "where": lum_below / lum_above on the original's brightness,
opaque / keyed on its alpha), grown by "grow" texels, with the pack's texels within "halo": {"within": texels, "lum_below" or
"lum_above"} of them that still look like ink (the upscale's soft edge), made opaque with "opaque": true, filled
("texture": [dx, dy] gives an inpainted hole the grain of the surface that far away: paper, paint): "inpaint" (smoothly from the texels round them), "key" (cut out of
the colour key: alpha 0, its colour "under" when given, else its neighbours'; a car panel shows a keyed texel's colour, the
cockpit's opaque faces draw the key black), or an [r, g, b].
text: the string at cap height "cap" (the font's H), its cap band's middle at "center", squeezed or stretched to "width" texels
(optional), turned "rotate" degrees anticlockwise, slanted "skew" (x shift per unit of height), letters spaced "tracking" caps
apart; the font is a name from tools/text-redraws/fonts/fonts.json (optional "variation": {"Weight": 700}). "alpha": "keep"
(the texture's own key untouched: lettering on an opaque face), "set" (opaque where the lettering covers half a texel or more:
lettering cut into the key, as dial figures over the world) or "cover" (opaque wherever it covers, over "under": a keyed face the
game draws in the key's colour, as the 3D dash's dials). "mirror": true reflects the lettering about its centre (the car's other side,
textured mirrored). "quad": [[x, y] x 4] sets the run's box (first letter to last, cap line to baseline) on those four
corners, top-left, top-right, bottom-right, bottom-left, in place of center/width/rotate/skew: a 4-point warp, for
lettering on a bent panel.
"""
import argparse, fnmatch, glob, json, math, os, shutil, struct, sys, time
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
SPECS = os.path.join(HERE, "text-redraws")
FONTS = os.path.join(SPECS, "fonts")
SS = 4          # supersampling of the lettering (each pack texel is SS x SS samples)
BASE = ".text-base"

def user_pack():
    if sys.platform == "win32": return os.path.join(os.environ.get("APPDATA", ""), "I26", "tex-world-4x")
    if sys.platform == "darwin": return os.path.expanduser("~/Library/Application Support/I26/tex-world-4x")
    return os.path.join(os.environ.get("XDG_DATA_HOME", os.path.expanduser("~/.local/share")), "I26", "tex-world-4x")

# ---- fonts ----

def font_table(fonts_dir=FONTS):
    with open(os.path.join(fonts_dir, "fonts.json"), encoding="utf-8") as f: return json.load(f)

def load_font(name, px, variation=None, fonts_dir=FONTS, table=None):
    t = (table or font_table(fonts_dir))[name]
    f = ImageFont.truetype(os.path.join(fonts_dir, t["file"]), max(1, int(round(px))))
    if variation:
        axes = f.get_variation_axes()
        names = [a["name"].decode() if isinstance(a["name"], bytes) else a["name"] for a in axes]
        f.set_variation_by_axes([float(variation.get(n, a["default"])) for n, a in zip(names, axes)])
    return f

def cap_ratio(name, variation=None, fonts_dir=FONTS, table=None):
    f = load_font(name, 400, variation, fonts_dir, table); b = f.getbbox("H", anchor="ls")
    return (b[3] - b[1]) / 400.0

# ---- the lettering's coverage ----

def text_layers(op, W, H, s, fonts_dir=FONTS, table=None):
    """The op's fill, stroke and shadow coverage (float 0..1, H x W at the pack's resolution, s pack pixels a texel) and the
    fill's vertical position in its cap band (0 at the cap line, 1 at the baseline) for a gradient."""
    cap = float(op["cap"]); var = op.get("variation"); name = op["font"]
    px = cap * s * SS / cap_ratio(name, var, fonts_dir, table)
    f = load_font(name, px, var, fonts_dir, table)
    capb = f.getbbox("H", anchor="ls"); cap_px = capb[3] - capb[1]
    text = op["text"]; track = float(op.get("tracking", 0)) * cap_px
    adv = [f.getlength(c) for c in text]
    run = sum(adv) + track * (len(text) - 1)
    stroke = op.get("stroke"); sw = int(round(float(stroke["width"]) * s * SS)) if stroke else 0
    pad = int(px) + sw + 4
    Wm, Hm = int(run) + 2 * pad, int(px * 2) + 2 * pad
    base_y = pad + int(px * 1.2)
    fill = Image.new("L", (Wm, Hm), 0); df = ImageDraw.Draw(fill)
    strk = Image.new("L", (Wm, Hm), 0) if sw else None; ds = ImageDraw.Draw(strk) if sw else None
    x = pad
    for c, a in zip(text, adv):
        df.text((x, base_y), c, font=f, fill=255, anchor="ls")
        if ds: ds.text((x, base_y), c, font=f, fill=255, anchor="ls", stroke_width=sw, stroke_fill=255)
        x += a + track
    # the mask's reference: the run's middle, the cap band's middle
    rx, ry = pad + run / 2.0, base_y - cap_px / 2.0
    fx = (float(op["width"]) * s * SS / run) if op.get("width") else 1.0
    ang = math.radians(float(op.get("rotate", 0))); skew = float(op.get("skew", 0))
    cx, cy = (float(v) * s for v in op.get("center", (0, 0)))
    # output sample (u, v) at SS x the pack's resolution -> mask point: undo the rotation, the skew and the squeeze
    ca, sa = math.cos(ang), math.sin(ang)
    k = 1.0 / SS
    # d = (u*k - cx, v*k - cy) in pack px; unrotate (anticlockwise on screen = clockwise in y-down coords)
    # x' = ca*dx - sa*dy ; y' = sa*dx + ca*dy   (inverse of a rotation by -ang in y-down space)
    a11, a12, a21, a22 = ca * k, -sa * k, sa * k, ca * k
    b1, b2 = -(ca * cx - sa * cy), -(sa * cx + ca * cy)
    # mask: mx = rx + (x' + skew*y') * SS / fx ; my = ry + y' * SS
    m = SS / fx
    A = (a11 * m + skew * a21 * m, a12 * m + skew * a22 * m, rx + (b1 + skew * b2) * m,
         a21 * SS, a22 * SS, ry + b2 * SS)
    if op.get("mirror"):
        # the lettering seen in a mirror (a texture laid on the car's other side mirrored): reflect about the centre's x
        X = 2 * cx * SS
        A = (-A[0], A[1], A[2] + A[0] * X, -A[3], A[4], A[5] + A[3] * X)
    Pc = tuple(A) + (0.0, 0.0)          # output sample -> mask point, as a projective map (affine so far)
    if op.get("quad"):
        # a 4-point warp: the run's box (cap line to baseline, first letter to last) onto the quad's corners (TL, TR, BR,
        # BL, the original's texels), for lettering on a bent or tilted panel
        src = [(pad, base_y - cap_px), (pad + run, base_y - cap_px), (pad + run, base_y), (pad, base_y)]
        dst = [(float(x) * s * SS, float(y) * s * SS) for x, y in op["quad"]]
        Pc = perspective(dst, src)
    def proj(P, ox=0.0, oy=0.0):
        """P with the output shifted by (ox, oy) pack px (u -> u - ox), normalised."""
        a, b, c, d, e, f, g, h = P; ox *= SS; oy *= SS
        n = 1 - g * ox - h * oy
        return tuple(v / n for v in (a, b, c - a * ox - b * oy, d, e, f - d * ox - e * oy, g, h))
    def warp(img, P):
        big = img.transform((W * SS, H * SS), Image.PERSPECTIVE, P, resample=Image.BILINEAR, fillcolor=0)
        return np.asarray(big.resize((W, H), Image.BOX), np.float32) / 255.0
    cov = warp(fill, Pc)
    st = warp(strk, Pc) if strk else None
    sh = None
    if op.get("shadow"):
        d = op["shadow"]
        sh = warp(strk or fill, proj(Pc, float(d.get("dx", 0)) * s, float(d.get("dy", 0)) * s))
    # the gradient's parameter: each pack pixel's mask y, 0 at the cap line, 1 at the baseline
    vv, uu = np.mgrid[0:H, 0:W].astype(np.float32)
    uu = (uu + 0.5) * SS; vv = (vv + 0.5) * SS
    my = (Pc[3] * uu + Pc[4] * vv + Pc[5]) / (Pc[6] * uu + Pc[7] * vv + 1)
    t = np.clip((my - (base_y - cap_px)) / cap_px, 0, 1)
    return cov, st, sh, t

def perspective(frm, to):
    """The 8 coefficients of the projective map taking the 4 points frm to the 4 points to (PIL's PERSPECTIVE order)."""
    M, r = [], []
    for (u, v), (x, y) in zip(frm, to):
        M.append([u, v, 1, 0, 0, 0, -u * x, -v * x]); r.append(x)
        M.append([0, 0, 0, u, v, 1, -u * y, -v * y]); r.append(y)
    return tuple(np.linalg.solve(np.array(M, np.float64), np.array(r, np.float64)))

# ---- erasing ----

def grow_mask(m, r):
    """Dilate a boolean mask by r pixels (a disc)."""
    if r <= 0: return m
    out = m.copy(); ri = int(math.ceil(r))
    for dy in range(-ri, ri + 1):
        for dx in range(-ri, ri + 1):
            if dx * dx + dy * dy > r * r + 1e-6: continue
            sh = np.zeros_like(m)
            ys = slice(max(0, dy), m.shape[0] + min(0, dy)); yd = slice(max(0, -dy), m.shape[0] + min(0, -dy))
            xs = slice(max(0, dx), m.shape[1] + min(0, dx)); xd = slice(max(0, -dx), m.shape[1] + min(0, -dx))
            sh[ys, xs] = m[yd, xd]; out |= sh
    return out

def inpaint(rgb, hole, iters=None):
    """Fill the hole's texels smoothly from round it (repeated averaging of the known 4-neighbours, then relaxed)."""
    rgb = rgb.astype(np.float32).copy(); known = ~hole
    if not hole.any() or not known.any(): return rgb
    # onion peel: each pass fills the hole's rim from its known neighbours
    k = known.copy()
    while not k.all():
        pad = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge"); kp = np.pad(k, 1, mode="edge")
        acc = np.zeros_like(rgb); cnt = np.zeros(k.shape, np.float32)
        for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1)):
            s = pad[1 + dy:1 + dy + k.shape[0], 1 + dx:1 + dx + k.shape[1]]; sm = kp[1 + dy:1 + dy + k.shape[0], 1 + dx:1 + dx + k.shape[1]]
            acc += s * sm[..., None]; cnt += sm
        rim = (~k) & (cnt > 0)
        if not rim.any(): break
        rgb[rim] = acc[rim] / cnt[rim][..., None]; k |= rim
    # relax inside the hole (a few Jacobi passes) so the peel's streaks smooth out
    for _ in range(iters if iters is not None else 24):
        pad = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge")
        avg = (pad[:-2, 1:-1] + pad[2:, 1:-1] + pad[1:-1, :-2] + pad[1:-1, 2:]) / 4
        rgb[hole] = avg[hole]
    return rgb

def where_mask(orig, rect, where):
    """The original's texels in rect matching `where` (all of rect for none), as a boolean mask at the original's size."""
    h, w = orig.shape[:2]
    m = np.zeros((h, w), bool)
    x0, y0, x1, y1 = (int(round(v)) for v in rect)
    x0, y0, x1, y1 = max(0, x0), max(0, y0), min(w, x1), min(h, y1)
    sub = np.ones((y1 - y0, x1 - x0), bool)
    if where:
        o = orig[y0:y1, x0:x1].astype(np.float32); lum = o[..., :3].mean(axis=2); a = o[..., 3]
        if "lum_below" in where: sub &= lum < float(where["lum_below"])
        if "lum_above" in where: sub &= lum > float(where["lum_above"])
        if where.get("opaque"): sub &= a > 0
        if where.get("keyed"): sub &= a == 0
    m[y0:y1, x0:x1] = sub
    return m

def apply_ops(px, orig, ops, fonts_dir=FONTS, table=None):
    """px: the pack's texture (H x W x 4 uint8, upright); orig: the original (h x w x 4). Returns the redrawn RGBA."""
    H, W = px.shape[:2]; s = W / orig.shape[1]
    rgb = px[..., :3].astype(np.float32); alpha = px[..., 3].copy()
    grain = []          # (hole, [dx, dy]) of the run's erasures that take a surface's grain
    pending = None      # consecutive inpaint erasures fill as one hole, so none fills from another's letters
    rebleed = np.zeros(alpha.shape, bool)   # texels newly cut into the key with no colour given: coloured from round them at the end
    for i, op in enumerate(ops):
        kind = op["op"]
        if kind == "erase":
            m = where_mask(orig, op["rect"], op.get("where"))
            big = np.repeat(np.repeat(m, int(round(s)), axis=0), int(round(s)), axis=1)[:H, :W]
            x0, y0, x1, y1 = (v * s for v in op["rect"])
            clip = np.zeros_like(big); clip[int(y0):int(math.ceil(y1)), int(x0):int(math.ceil(x1))] = True
            hole = grow_mask(big, float(op.get("grow", 0)) * s) & (clip if op.get("clip", True) else True)
            if op.get("halo"):
                # the upscale's soft edge round the painted letters: the pack's texels near the hole that still look like ink
                hl = op["halo"]; near = grow_mask(hole, float(hl.get("within", 1)) * s) & ~hole
                lum = rgb.mean(axis=2)
                ink = np.ones_like(near)
                if "lum_below" in hl: ink &= lum < float(hl["lum_below"])
                if "lum_above" in hl: ink &= lum > float(hl["lum_above"])
                hole = hole | (near & ink)
            if op.get("opaque"): alpha[hole] = 255           # keyed texels in the hole become part of the face
            fill = op.get("fill", "inpaint")
            if fill == "inpaint":
                pending = hole if pending is None else pending | hole
                if op.get("texture"): grain.append((hole, op["texture"]))
                nxt = ops[i + 1] if i + 1 < len(ops) else None
                if not (nxt and nxt["op"] == "erase" and nxt.get("fill", "inpaint") == "inpaint"):
                    rgb = inpaint(rgb, pending); pending = None
                    # the inpaint is smooth: give each hole the grain of the surface beside it (the pack's own detail,
                    # its high frequencies, from the texels "texture": [dx, dy] texels away)
                    if grain:
                        blur = np.asarray(Image.fromarray(np.clip(rgb + 0.5, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.4 * s)), np.float32)
                        hp = rgb - blur
                        for h, (gx, gy) in grain:
                            dx, dy = int(round(float(gx) * s)), int(round(float(gy) * s))
                            sh = np.roll(np.roll(hp, -dy, axis=0), -dx, axis=1)
                            rgb[h] += sh[h]
                        grain = []
            elif fill == "key":
                alpha[hole] = 0
                if "under" in op: rgb[hole] = op["under"]; rebleed &= ~hole
                else: rebleed |= hole
            else:
                rgb[hole] = fill
        elif kind == "text":
            cov, st, sh, t = text_layers(op, W, H, s, fonts_dir, table)
            mode = op.get("alpha", "keep")
            allc = np.maximum(cov, st) if st is not None else cov
            if sh is not None: allc = np.maximum(allc, sh * float(op["shadow"].get("opacity", 0.6)))
            rebleed &= ~(allc > 0.02)          # the lettering's own edge colour stays
            if mode == "cover":
                under = np.array(op.get("under", [0, 0, 0]), np.float32)
                newly = (alpha == 0) & (allc > 0.02)
                rgb[newly] = under; alpha[newly] = 255
            def comp(c, colour, opacity=1.0):
                nonlocal rgb
                a = (c * opacity)[..., None]
                rgb = rgb * (1 - a) + np.asarray(colour, np.float32) * a
            if op.get("ink"):
                # ink on paper: fill and stroke as one coverage, feathered, multiplied into the surface at the ink's opacity
                ik = op["ink"]; a = np.maximum(cov, st) if st is not None else cov
                fe = float(ik.get("feather", 0))
                if fe > 0: a = np.asarray(Image.fromarray(np.clip(a * 255 + 0.5, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(fe / 2.0)), np.float32) / 255.0
                a = (a * float(ik.get("opacity", 1.0)))[..., None]
                rgb = rgb * (1 - a * (1 - np.asarray(op.get("colour", [0, 0, 0]), np.float32) / 255.0))
            else:
                if sh is not None: comp(sh, op["shadow"].get("colour", [0, 0, 0]), float(op["shadow"].get("opacity", 0.6)))
                if st is not None: comp(st, op["stroke"].get("colour", [0, 0, 0]))
            if op.get("ink"): pass
            elif op.get("gradient"):
                g0, g1 = (np.asarray(c, np.float32) for c in op["gradient"])
                col = g0 * (1 - t[..., None]) + g1 * t[..., None]
                a = cov[..., None]; rgb = rgb * (1 - a) + col * a
            else:
                comp(cov, op.get("colour", [0, 0, 0]))
            if mode == "set":
                on = allc >= 0.5
                alpha[on] = 255
                # a texel the lettering half covers stays in the key: its colour is the lettering's, for the filter
            elif mode not in ("keep", "cover"):
                raise ValueError(f"alpha {mode!r}: keep, set or cover")
        else:
            raise ValueError(f"op {kind!r}: erase or text")
    # the key's colour stays as the pack has it (a car's panel, which does not honour the key, shows it); only texels newly
    # cut into the key without a colour take their neighbours' (the mipmaps and filtering see no seam)
    if rebleed.any(): rgb = bleed(rgb, (~rebleed).astype(np.uint8))
    return np.dstack([np.clip(rgb + 0.5, 0, 255).astype(np.uint8), alpha.astype(np.uint8)])

def bleed(rgb, alpha, passes=64):
    """The keyed texels take their opaque neighbours' colour (as tools/upscale-textures.py leaves them), for the mipmaps."""
    rgb = rgb.astype(np.float32); m = alpha > 0
    for _ in range(passes):
        if m.all() or not m.any(): break
        pad = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge"); mp = np.pad(m, 1, mode="edge")
        acc = np.zeros_like(rgb); cnt = np.zeros(m.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                sl = pad[1 + dy:1 + dy + rgb.shape[0], 1 + dx:1 + dx + rgb.shape[1]]
                sm = mp[1 + dy:1 + dy + m.shape[0], 1 + dx:1 + dx + m.shape[1]]
                acc += sl * sm[..., None]; cnt += sm
        fill = (~m) & (cnt > 0)
        rgb[fill] = acc[fill] / cnt[fill][..., None]; m = m | fill
    return rgb

# ---- pack files ----

def write_dds(path, rgba, etcpak):
    """BC7 (sRGB, DX10 header) with its full mip chain, box-filtered: tools/upscale-textures.py's format."""
    im = Image.fromarray(rgba, "RGBA"); levels = [im]
    while im.size != (1, 1):
        im = im.resize((max(1, im.size[0] // 2), max(1, im.size[1] // 2)), Image.BOX); levels.append(im)
    def bc7(l):
        p = np.array(l); h, w = p.shape[:2]; pw, ph = (w + 3) // 4 * 4, (h + 3) // 4 * 4
        if (pw, ph) != (w, h): p = np.pad(p, ((0, ph - h), (0, pw - w), (0, 0)), mode="edge")
        return etcpak.compress_bc7(np.ascontiguousarray(p).tobytes(), pw, ph)
    blocks = [bc7(l) for l in levels]
    w, h = rgba.shape[1], rgba.shape[0]
    hdr = struct.pack("<4sIIIIIII44x", b"DDS ", 124, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000, h, w, len(blocks[0]), 0, len(levels))
    hdr += struct.pack("<II4s20x", 32, 0x4, b"DX10")
    hdr += struct.pack("<IIII4x", 0x1000 | 0x400000 | 0x8, 0, 0, 0)
    dx10 = struct.pack("<IIIII", 99, 3, 0, 1, 0)
    with open(path, "wb") as f: f.write(hdr + dx10 + b"".join(blocks))

def read_pack_file(path):
    im = Image.open(path); im.load()
    return np.array(im.convert("RGBA"))

def pack_file(pack, stem):
    for e in (".dds", ".png"):
        p = os.path.join(pack, stem + e)
        if os.path.exists(p): return p
    return None

def load_specs(specs_dir=SPECS, only="*"):
    out = []
    for p in sorted(glob.glob(os.path.join(specs_dir, "*.json"))):
        with open(p, encoding="utf-8") as f: spec = json.load(f)
        for t in spec["textures"]:
            if fnmatch.fnmatch(t.lower(), only.lower()): out.append((os.path.basename(p), t.lower(), spec))
    return out

def redraw(src, pack, name, stem, spec, etcpak, table, fonts_dir=FONTS, bottom_up=frozenset()):
    cur = pack_file(pack, stem)
    if cur is None: return f"{stem}: not in the pack, skipped"
    base_dir = os.path.join(pack, BASE); os.makedirs(base_dir, exist_ok=True)
    base = os.path.join(base_dir, os.path.basename(cur))
    if not os.path.exists(base): shutil.copy2(cur, base)          # the model's file, kept the first time
    px = read_pack_file(base)
    flip = stem in bottom_up and base.endswith(".dds")
    if flip: px = px[::-1]
    orig = np.array(Image.open(os.path.join(src, stem + ".png")).convert("RGBA"))
    out = apply_ops(px, orig, spec["ops"], fonts_dir, table)
    if base.endswith(".dds") and etcpak is not None and out.shape[0] % 4 == 0 and out.shape[1] % 4 == 0:
        write_dds(cur + ".part", out[::-1] if flip else out, etcpak); os.replace(cur + ".part", cur)
    else:
        Image.fromarray(out, "RGBA").save(cur + ".part.png"); os.replace(cur + ".part.png", os.path.splitext(cur)[0] + ".png")
    return f"{stem}: {name}"

def fonts_of(spec):
    return sorted({op["font"] for op in spec["ops"] if op["op"] == "text"})

def note_pack(pack, done, table):
    """pack.json's text_redraws and fonts, ATTRIBUTION.txt's font lines (earlier runs' lines replaced)."""
    pj = os.path.join(pack, "pack.json")
    info = {}
    if os.path.exists(pj):
        with open(pj, encoding="utf-8") as f: info = json.load(f)
    prev = {r["texture"]: r for r in info.get("text_redraws", [])}
    for stem, name, spec in done: prev[stem] = {"texture": stem, "spec": "tools/text-redraws/" + name, "fonts": fonts_of(spec)}
    info["text_redraws"] = [prev[k] for k in sorted(prev)]
    used = sorted({f for r in info["text_redraws"] for f in r["fonts"]})
    info["text_fonts"] = [{"font": table[f].get("name", f), "by": table[f]["by"], "license": table[f]["license"], "source": table[f]["source"]} for f in used]
    lines = [f"lettering redrawn in {table[f].get('name', f)} by {table[f]['by']}, {table[f]['license']}, {table[f]['source']}" for f in used]
    if lines:
        info["attribution"] = " | ".join([a for a in info.get("attribution", "").split(" | ") if a and not a.startswith("lettering redrawn in ")] + lines)
    with open(pj + ".part", "w", encoding="utf-8") as f: json.dump(info, f, indent=2)
    os.replace(pj + ".part", pj)
    at = os.path.join(pack, "ATTRIBUTION.txt")
    old = open(at, encoding="utf-8").read().splitlines() if os.path.exists(at) else []
    with open(at, "w", encoding="utf-8") as f: f.write("\n".join([l for l in old if l and not l.startswith("lettering redrawn in ")] + lines) + "\n")

def restore(pack, only):
    base_dir = os.path.join(pack, BASE); n = 0
    if not os.path.isdir(base_dir): return 0
    for fn in os.listdir(base_dir):
        if fnmatch.fnmatch(os.path.splitext(fn)[0], only.lower()):
            shutil.copy2(os.path.join(base_dir, fn), os.path.join(pack, fn)); os.remove(os.path.join(base_dir, fn)); n += 1
    pj = os.path.join(pack, "pack.json")
    if os.path.exists(pj):
        with open(pj, encoding="utf-8") as f: info = json.load(f)
        info["text_redraws"] = [r for r in info.get("text_redraws", []) if not fnmatch.fnmatch(r["texture"], only.lower())]
        with open(pj, "w", encoding="utf-8") as f: json.dump(info, f, indent=2)
    return n

def menu_maps(src, pack, out_dir, specs):
    """The map screen draws its map from the HD menu art (maps/<stem>), not from the world pack: each map's upscale (the
    world pack's file, as the model left it) redrawn and written there as a PNG, upright."""
    bu = set()
    p = os.path.join(src, "bottom-up.txt")
    if os.path.exists(p):
        with open(p) as f: bu = {l.strip().lower() for l in f if l.strip()}
    os.makedirs(os.path.join(out_dir, "maps"), exist_ok=True); table = font_table()
    for name, stem, spec in specs:
        base = os.path.join(pack, BASE, stem + ".dds")
        cur = base if os.path.exists(base) else pack_file(pack, stem)
        if cur is None: print(f"{stem}: not in the pack, skipped"); continue
        px = read_pack_file(cur)
        if stem in bu and cur.endswith(".dds"): px = px[::-1]
        orig = np.array(Image.open(os.path.join(src, stem + ".png")).convert("RGBA"))
        out = apply_ops(px, orig, spec["ops"], table=table)
        dst = os.path.join(out_dir, "maps", stem + ".png")
        Image.fromarray(out, "RGBA").save(dst + ".part.png"); os.replace(dst + ".part.png", dst)
        print(f"{stem}: {dst}")

# ---- self-check ----

def self_check(fonts_dir=FONTS):
    """A made-up plate: grey with a dark 'X' painted at 1 texel strokes; erase it, letter 'AB' in each font, check that the
    old ink went, the new ink landed in the box, and the key modes behave."""
    table = font_table(fonts_dir); ok = True
    orig = np.full((16, 32, 4), 180, np.uint8); orig[..., 3] = 255
    for i in range(8): orig[4 + i, 4 + i, :3] = 20; orig[4 + i, 11 - i, :3] = 20
    s = 4; px = np.repeat(np.repeat(orig, s, 0), s, 1)
    for name in table:
        op = [{"op": "erase", "rect": [2, 2, 14, 14], "where": {"lum_below": 60}, "grow": 0.5},
              {"op": "text", "text": "AB", "font": name, "cap": 6, "center": [22, 8], "colour": [10, 10, 10]}]
        out = apply_ops(px, orig, op, fonts_dir, table).astype(int)
        left = out[8:56, 8:56, :3].mean(axis=2)
        right = out[20:44, 64:116, :3].mean(axis=2)
        e1 = left.min() > 150                        # the painted X is gone
        e2 = (right < 60).mean() > 0.08              # the new letters are there
        e3 = (out[:, :60, :3].mean(axis=2) < 60).sum() == 0   # and nowhere else
        print(f"  {name}: erased {e1}, lettered {e2} ({(right < 60).mean():.2f}), contained {e3}")
        ok &= e1 and e2 and e3
    # keyed lettering: a key face with a white '1', redrawn as cover over black
    key = np.zeros((16, 16, 4), np.uint8); key[3:12, 7, :] = 255
    px = np.repeat(np.repeat(key, s, 0), s, 1)
    out = apply_ops(px, key, [{"op": "erase", "rect": [0, 0, 16, 16], "where": {"opaque": True}, "grow": 0.5, "fill": "key"},
                              {"op": "text", "text": "7", "font": sorted(table)[0], "cap": 8, "center": [8, 8], "colour": [255, 255, 255], "alpha": "cover"}], fonts_dir, table)
    c1 = out[..., 3].max() == 255 and (out[..., 3] == 0).mean() > 0.5
    lit = out[out[..., 3] == 255][:, :3].mean(axis=1)
    c2 = lit.max() > 200 and lit.min() < 40
    print(f"  cover: key kept {c1}, anti-aliased over black {c2}")
    ok &= c1 and c2
    # rotation keeps the centre
    plain = np.full((32, 32, 4), 200, np.uint8); plain[..., 3] = 255
    out = apply_ops(np.repeat(np.repeat(plain, s, 0), s, 1), plain, [{"op": "text", "text": "I", "font": "Anton", "cap": 10, "center": [16, 16], "rotate": 90, "colour": [0, 0, 0]}], fonts_dir, table)
    ys, xs = np.where(out[..., :3].mean(axis=2) < 100)
    c3 = abs(xs.mean() / s - 16) < 0.7 and abs(ys.mean() / s - 16) < 0.7 and (xs.max() - xs.min()) > (ys.max() - ys.min())
    print(f"  rotate 90: centred and lying {c3}")
    ok &= c3
    print("self-check:", "PASS" if ok else "FAIL")
    return ok

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", nargs="?", help="the folder --export-textures wrote (the originals and bottom-up.txt)")
    ap.add_argument("--pack", default=None, help="the pack folder (default: the game's tex-world-4x)")
    ap.add_argument("--specs", default=SPECS, help="the folder of spec .json files (default tools/text-redraws)")
    ap.add_argument("--only", default="*", help="a glob of texture names to redraw")
    ap.add_argument("--list", action="store_true", help="list the specs' textures and quit")
    ap.add_argument("--restore", action="store_true", help="put the model's files back (from PACK/.text-base) and quit")
    ap.add_argument("--self-check", action="store_true", help="redraw made-up textures and check the result")
    ap.add_argument("--menu-maps", metavar="DIR", help="the mission maps instead (specs in tools/text-redraws/maps/): the pack's upscale of each"
                    " map redrawn and written as DIR/maps/<map>.png, where the map screen (M) looks for an HD map (DIR: the HD menu art's"
                    " folder, user://tex-menu, or I76_TEX_DIR); the world pack is not changed")
    args = ap.parse_args()
    if args.self_check: sys.exit(0 if self_check() else 1)
    pack = args.pack or user_pack()
    if args.restore: print(f"{restore(pack, args.only)} textures restored in {pack}"); return
    if args.menu_maps:
        if not args.src: ap.error("the folder --export-textures wrote is needed")
        menu_maps(args.src, pack, args.menu_maps, load_specs(os.path.join(SPECS, "maps"), args.only)); return
    specs = load_specs(args.specs, args.only)
    if args.list:
        for name, stem, spec in specs: print(f"{stem}\t{name}\t{', '.join(fonts_of(spec))}")
        return
    if not args.src: ap.error("the folder --export-textures wrote is needed")
    try: import etcpak
    except ImportError: etcpak = None; print("etcpak not installed (pip install etcpak): .dds textures are written as .png")
    bu = set()
    p = os.path.join(args.src, "bottom-up.txt")
    if os.path.exists(p):
        with open(p) as f: bu = {l.strip().lower() for l in f if l.strip()}
    table = font_table(); done = []; t0 = time.time()
    for name, stem, spec in specs:
        msg = redraw(args.src, pack, name, stem, spec, etcpak, table, bottom_up=bu)
        print(msg)
        if not msg.endswith("skipped"): done.append((stem, name, spec))
    note_pack(pack, done, table)
    print(f"{len(done)} redrawn in {time.time() - t0:.1f} s, in {pack}")

if __name__ == "__main__":
    main()
