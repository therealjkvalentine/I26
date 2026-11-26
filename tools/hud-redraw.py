#!/usr/bin/env python3
"""Redraw the cockpit's damage panel (Mr. Damage) and weapon counters' housings for an HD pack, CPU only.

    python tools/hud-redraw.py measure EXPORT_DIR [wires.json]
    python tools/hud-redraw.py render  EXPORT_DIR UPSCALE_DIR OUT_DIR [--scale 8] [--distress 0|light|medium]
    python tools/hud-redraw.py pack    EXPORT_DIR PACK_DIR [--out DIR] [--base DIR] [--distress 0|light|medium]
    python tools/hud-redraw.py --self-check

EXPORT_DIR is the folder `i76 export-textures` wrote (the originals as PNGs, rows as the game shows them, and
bottom-up.txt); UPSCALE_DIR a pack's upscale of the same textures (PNG or .dds, any whole multiple of the original's
size). The textures: zsy_ (the damage panel), zsye (its lights: every part's five states), zwpe (the counters' five
housings), zbk_ and zbks_ (the weapons' and specials' rails the housings hang on). Nothing of the game's art is in the
repository: the spec (tools/hud-redraw/hud.json) holds coordinates and numbers only, everything else is measured from
the export each run.

- The wires: the red and yellow wires at the damage panel's sides are found in the original by colour, each colour's
  pieces between the connectors fitted as centrelines (radar-redraw.py's measure_wire: ordered end to end, a smoothing
  spline), drawn again as tubes of their measured widths, shaded from the original's own colours regressed along them
  (radar-redraw.py's Band), the one on top where two cross as the original has it. The upscale's own wire texels (red or
  yellow by hue) round them are taken out first.
- The wear (tools/panel_distress.py): a layer of grime, chipped paint along the art's edges, a few fine scratches and a
  slightly uneven print on the lettering, made from a fixed seed, at the spec's level (0 = none). It is made once per unit
  and laid the same on every state: the damage panel's layer is made over the panel as the game first shows it (every part
  stamped unlit) and each light's five states get the part of it under their place, so a light changing state changes
  nothing else and the stamp's edge does not show; the housings' layer is the same on all five (their screws' angles are
  the only difference).
- What the game stamps is left where it is: the panel's texels under every part's stamp are the pack's (never seen: the
  cells are opaque), and on a housing the diode, the label plate and the counter windows are the pack's texels exactly.

`pack` is the HD recipes' step (tools/hd-recipes/world-4x.json, world-8x.json), after the lettering and the radar: it
redraws the pack's own files in their format (.dds: BC7 with mips, rows stored bottom up as bottom-up.txt says), keeps the
files it started from in PACK_DIR/.hud-base/ (a rerun starts from those, so it writes the same files) and names the redraw
in pack.json (FILES.sha256 made again when there is one). --out DIR leaves the pack as it is. A pack whose pack.json names a
hud redraw and that has no .hud-base/ is refused; --base DIR gives the upscale's own files. Needs numpy, scipy, Pillow
(and etcpak for .dds).
"""
import argparse, importlib.util, json, math, os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = os.path.join(HERE, "hud-redraw", "hud.json")
BASE = ".hud-base"
sys.path.insert(0, HERE)
import panel_distress as pd   # noqa: E402


def _radar():
    sp = importlib.util.spec_from_file_location("radar_redraw", os.path.join(HERE, "radar-redraw.py"))
    m = importlib.util.module_from_spec(sp); sp.loader.exec_module(m); return m


rr = _radar()


def load_spec(path=SPEC):
    with open(path, encoding="utf-8") as f: return json.load(f)


def stems(spec):
    d, w = spec["damage"], spec["weapons"]
    return [d["panel"], d["sheet"], w["housing"]] + list(w["rails"])


def level_of(spec, name):
    lv = spec["distress"]["levels"]
    if name not in lv: raise SystemExit(f"no distress level {name!r}: " + ", ".join(lv))
    return lv[name]


# ------------------------------------------------------------------------------------------------------------- the wires

def is_col(a, c): return (a[..., 0] == c[0]) & (a[..., 1] == c[1]) & (a[..., 2] == c[2])


def wire_pixels(orig, wz):
    """{colour: mask} of the wires' pixels in the zones."""
    a = orig.astype(int); op = a[..., 3] > 0
    zone = np.zeros(op.shape, bool)
    for x0, y0, x1, y1 in wz["zones"]: zone[y0:y1, x0:x1] = True
    out = {}
    for name, cols in wz["colours"].items():
        m = np.zeros(op.shape, bool)
        for c in cols: m |= op & is_col(a, c)
        out[name] = m & zone
    return out


def measure_wires(orig, wz):
    """The wires' pieces: colour, zone, centreline points, width, length and pixels (8-connected pieces of one colour)."""
    pieces = []
    for name, m in wire_pixels(orig, wz).items():
        lab, n = ndimage.label(m, np.ones((3, 3)))
        for k in range(1, n + 1):
            pm = lab == k
            if pm.sum() < wz["min_pixels"]: continue
            w = rr.measure_wire(pm, wz["bin"], wz["smooth"])
            if w is None: continue
            ys, xs = np.nonzero(pm)
            zi = next(i for i, (x0, y0, x1, y1) in enumerate(wz["zones"]) if x0 <= xs.mean() < x1 and y0 <= ys.mean() < y1)
            pieces.append({"colour": name, "zone": zi, "mask": pm, **w})
    return order_pieces(pieces, orig, wz)


def order_pieces(pieces, orig, wz):
    """Drawing order: where two pieces of different colours overlap, the one whose colour the original shows there last."""
    H, W = orig.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    cols = wire_pixels(orig, wz)
    reach = []
    for p in pieces:
        cu = rr.curve_open(p["points"])
        t, d = rr.curve_coords(cu, xx, yy)
        reach.append((np.abs(d) <= p["width"] / 2 + 0.6) & (t > -0.5) & (t < cu["length"] + 0.5))
    score = [0] * len(pieces)
    for i in range(len(pieces)):
        for j in range(i + 1, len(pieces)):
            if pieces[i]["colour"] == pieces[j]["colour"]: continue
            ov = reach[i] & reach[j]
            a = int((ov & cols[pieces[i]["colour"]]).sum()); b = int((ov & cols[pieces[j]["colour"]]).sum())
            if a > b: score[i] += 1; score[j] -= 1
            elif b > a: score[j] += 1; score[i] -= 1
    order = sorted(range(len(pieces)), key=lambda k: (score[k], k))
    return [pieces[k] for k in order]


def up_px(mask, s):
    return np.repeat(np.repeat(mask, s, 0), s, 1)


def redraw_wires(up, orig, wz, s, pieces=None):
    """The panel's upscale (RGBA uint8 at s, rows as shown) with its wires drawn again; pieces from measure_wires."""
    if pieces is None: pieces = measure_wires(orig, wz)
    H, W = orig.shape[:2]
    out = up.astype(float)
    wires = np.zeros((H, W), bool)
    for m in wire_pixels(orig, wz).values(): wires |= m
    clear_px = (orig[..., 3] == 0) | wires          # where the original shows a wire or nothing
    for zi, (x0, y0, x1, y1) in enumerate(wz["zones"]):
        sl = (slice(y0 * s, y1 * s), slice(x0 * s, x1 * s))
        reg = out[sl]
        # the upscale's wire texels round the original's wires: red or yellow by hue
        wt = up_px(wires[y0:y1, x0:x1], s)
        near = ndimage.distance_transform_edt(~wt) < wz["clear_reach"] * s
        r, g, b = reg[..., 0], reg[..., 1], reg[..., 2]
        hue = np.maximum(r - g, np.minimum(r, g) - b)
        k = pd.smoothstep(wz["hue_min"], wz["hue_full"], hue) * near * (reg[..., 3] > 0)
        cl = up_px(clear_px[y0:y1, x0:x1], s)
        # on the body or a connector: its own colour from the texels round it that are not wire
        keep = (k < 0.05) & (reg[..., 3] > 127)
        wgt = keep.astype(float)
        fill = np.stack([ndimage.gaussian_filter(reg[..., c] * wgt, 0.6 * s) for c in range(3)], -1) / np.maximum(ndimage.gaussian_filter(wgt, 0.6 * s), 1e-6)[..., None]
        kb = (k * ~cl)[..., None]
        reg[..., :3] = reg[..., :3] * (1 - kb) + fill * kb
        reg[..., 3] = reg[..., 3] * (1 - k * cl)
        # the tubes, bottom first, held to where the original shows a wire or nothing (a wire ends at its connector)
        cv = rr.Canvas(W, H, s, (x0, y0, x1, y1))
        allowed = np.clip((ndimage.gaussian_filter(cl.astype(float), 0.3 * s) - 0.5) * 2.5 + 0.5, 0, 1)
        for p in pieces:
            if p["zone"] != zi: continue
            cu = rr.curve_open(p["points"])
            cov, t, d = rr.tube_coverage(cu, cv, p["width"], wz["extend"])
            cov = cov * allowed
            band = rr.Band(cu, orig, (-p["width"] / 2 - 0.5, p["width"] / 2 + 0.5), wz["sigma_t"], wz["sigma_d"], outlier=1e9, mask=p["mask"], iters=0)
            v = band.at(t, d); col = v[..., :3] / np.maximum(v[..., 3:4] / 255, 1e-3)
            a0 = reg[..., 3:4] / 255; c = cov[..., None]
            na = a0 * (1 - c) + c
            reg[..., :3] = (np.clip(col, 0, 255) * c + reg[..., :3] * a0 * (1 - c)) / np.maximum(na, 1e-6)
            reg[..., 3] = na[..., 0] * 255
        out[sl] = reg
    return np.clip(np.round(out), 0, 255).astype(np.uint8)


# ------------------------------------------------------------------------------------------------------------- the damage panel

def footprints(spec):
    """[(anchor, sprite, (x, y, w, h) on the panel, (u = rows up))] as the game stamps them."""
    d = spec["damage"]; out = []
    for anchor, (ax, ay), sprite in d["parts"]:
        sp = d["sprites"][sprite]; w, h = sp["size"]
        u = 1 if sp["cells"][0][1] > 0 else 0
        out.append((anchor, sprite, (ax, ay - u, w, h), u))
    return out


def cell_rect(spec, sprite, state):
    """A sprite's cell on the sheet in one state (index), as drawn: (x, y, w, h) original px."""
    sp = spec["damage"]["sprites"][sprite]; x, y = sp["cells"][state]; w, h = sp["size"]
    u = 1 if sp["cells"][0][1] > 0 else 0
    return (x, y - u, w, h)


def composite(panel, sheet, spec, s, state=0):
    """The panel as the game first draws it: every part stamped in one state (0: unlit) over it."""
    out = panel.copy()
    for anchor, sprite, (x, y, w, h), u in footprints(spec):
        cx, cy, _, _ = cell_rect(spec, sprite, state)
        cell = sheet[cy * s:(cy + h) * s, cx * s:(cx + w) * s]
        dst = out[y * s:(y + h) * s, x * s:(x + w) * s]
        keyed = cell[..., 3] >= 128
        dst[keyed] = cell[keyed]
    return out


def damage_layer(panel, sheet, spec, s, level, wire_pieces=None):
    """The damage unit's wear layer over the panel (the composite's size), shared sprites' places made alike; None at 0."""
    if not level: return None, None
    d = spec["damage"]; ds = spec["distress"]
    comp = composite(panel, sheet, spec, s)
    H, W = comp.shape[:2]
    ink = np.zeros((H, W)); zone = np.zeros((H, W))
    def look(box):
        x0, y0, x1, y1 = [int(round(v * s)) for v in box]
        zone[y0:y1, x0:x1] = 1
        return pd.ink_mask(comp, s, box)
    for box in d["lettering"]: ink = np.maximum(ink, look(box))
    for anchor, sprite, (x, y, w, h), u in footprints(spec):
        k = d.get("plate_frame", 2.0)       # the lens inside its frame (a frame darker than a lit lens is not print)
        if d["sprites"][sprite].get("lettered"): ink = np.maximum(ink, look((x + k, y + k, x + w - k, y + h - k)))
    # no scratches across the wires
    sm = (comp[..., 3] > 127).astype(float)
    if wire_pieces:
        wm = np.zeros((H // s, W // s), bool)
        for p in wire_pieces: wm |= p["mask"]
        wm = ndimage.binary_dilation(wm, iterations=1)
        sm *= 1 - up_px(wm, s)
    # chips and scratches on the panel's paint only: not on the lights' cells (their lenses, lit or dark) nor on the maker's
    # plate's lettering
    wm = np.ones((H, W))
    boxes = [tuple(b) for b in d["lettering"]]
    for x0, y0, x1, y1 in boxes:
        k = d.get("plate_inset", 1.2)
        wm[int((y0 + k) * s):int((y1 - k) * s), int((x0 + k) * s):int((x1 - k) * s)] = 0
    for _, sprite, (x, y, w, h), _ in footprints(spec):
        wm[y * s:(y + h) * s, x * s:(x + w) * s] = 0
        sm[y * s:(y + h) * s, x * s:(x + w) * s] = 0
    L = pd.make_layer(comp, s, level, ds["seed"], "damage", ink=ink, scratch_mask=sm, wear_mask=wm, ink_zone=zone)
    # a sprite stamped at several places carries one layer: its first place's; round the others the panel's layer is blended
    # into that one over shared_blend px, so no edge shows
    M = d["shared_blend"]
    first = {}
    for anchor, sprite, (x, y, w, h), u in footprints(spec):
        if sprite not in first: first[sprite] = (x, y); continue
        fx, fy = first[sprite]
        m = int(math.ceil(M))
        bx0, by0 = max(x - m, 0), max(y - m, 0); bx1, by1 = min(x + w + m, W // s), min(y + h + m, H // s)
        sx0, sy0 = bx0 + fx - x, by0 + fy - y
        if sx0 < 0 or sy0 < 0 or sx0 + (bx1 - bx0) > W // s or sy0 + (by1 - by0) > H // s: continue
        X = (np.arange(bx0 * s, bx1 * s) + 0.5)[None, :] / s; Y = (np.arange(by0 * s, by1 * s) + 0.5)[:, None] / s
        dist = np.hypot(np.maximum(np.maximum(x - X, X - (x + w)), 0), np.maximum(np.maximum(y - Y, Y - (y + h)), 0))
        wgt = pd.smoothstep(0, M, dist)
        for k in L:
            src = L[k][sy0 * s:(sy0 + by1 - by0) * s, sx0 * s:(sx0 + bx1 - bx0) * s]
            L[k][by0 * s:by1 * s, bx0 * s:bx1 * s] = src * (1 - wgt) + L[k][by0 * s:by1 * s, bx0 * s:bx1 * s] * wgt
    return L, comp


def redraw_damage(orig_panel, up_panel, up_sheet, spec, s, level):
    """(panel, sheet) redrawn at s: the wires, then the wear (the panel outside the stamps' places, every state of every
    cell its place's)."""
    d = spec["damage"]; ds = spec["distress"]
    pieces = measure_wires(orig_panel, d["wires"])
    panel = redraw_wires(up_panel, orig_panel, d["wires"], s, pieces)
    sheet = up_sheet.copy()
    L, comp = damage_layer(panel, up_sheet, spec, s, level, pieces)
    if L is None: return panel, sheet
    # the panel: everywhere but under the stamps (the game covers those with opaque cells; left as the pack has them)
    under = np.zeros(panel.shape[:2], bool)
    for anchor, sprite, (x, y, w, h), u in footprints(spec): under[y * s:(y + h) * s, x * s:(x + w) * s] = True
    Lp = {k: v * ~under for k, v in L.items()}
    panel = pd.apply_layer(panel, Lp, ds["colours"], s=s)
    # the sheet: each sprite's five states the layer at its first place
    done = set()
    for anchor, sprite, (x, y, w, h), u in footprints(spec):
        if sprite in done: continue
        done.add(sprite)
        reg = (x * s, y * s, (x + w) * s, (y + h) * s)
        for st in range(len(d["states"])):
            cx, cy, _, _ = cell_rect(spec, sprite, st)
            cell = sheet[cy * s:(cy + h) * s, cx * s:(cx + w) * s]
            sheet[cy * s:(cy + h) * s, cx * s:(cx + w) * s] = pd.apply_layer(cell, L, ds["colours"], region=reg, s=s)
    return panel, sheet


# ------------------------------------------------------------------------------------------------------------- the housings

def housing_mask(spec, s):
    """Where a housing may change (1) and where the game stamps over it (0: the pack's texels kept), at s."""
    w, h = spec["weapons"]["size"]
    m = np.ones((h * s, w * s))
    for x0, y0, x1, y1 in spec["weapons"]["stamped"]: m[y0 * s:y1 * s, x0 * s:x1 * s] = 0
    return m


def redraw_housings(up, spec, s, level):
    """zwpe at s with one wear layer laid on all five housings, the stamped places kept."""
    wp = spec["weapons"]; ds = spec["distress"]
    if not level: return up.copy()
    w, h = wp["size"]
    crops = [up[y * s:(y + h) * s, x * s:(x + w) * s] for x, y in wp["variants"]]
    # the structure the layer follows: the five housings' median (their screws' slots blur away, the rest is the same)
    struct = np.median(np.stack(crops).astype(float), 0).astype(np.uint8)
    m = housing_mask(spec, s) * (struct[..., 3] > 127)
    L = pd.make_layer(struct, s, level, ds["seed"], "housing", mask=m)
    out = up.copy()
    for (x, y), c in zip(wp["variants"], crops):
        out[y * s:(y + h) * s, x * s:(x + w) * s] = pd.apply_layer(c, L, ds["colours"], s=s)
    return out


def redraw_rails(up, name, spec, s, level):
    if not level: return up.copy()
    L = pd.make_layer(up, s, level, spec["distress"]["seed"], name)
    return pd.apply_layer(up, L, spec["distress"]["colours"], s=s)


# ------------------------------------------------------------------------------------------------------------- files

def read_bottom_up(export_dir): return rr.read_bottom_up(export_dir)


def load_upright(path, stem, bu, size, s):
    """A pack's texture in the export's rows (a .dds named in bottom-up.txt turned upright) at s texels a pixel."""
    a = rr.load_rgba(path)
    if path.endswith(".dds") and stem in bu: a = np.ascontiguousarray(a[::-1])
    return rr.resize_to(a, size[0] * s, size[1] * s)


def redraw_all(export_dir, find, spec, s, level_name, with_input=False):
    """{stem: RGBA (export rows)} of the five textures redrawn (and the upscale's, with_input); find(stem) gives the
    upscale's file."""
    level = level_of(spec, level_name)
    bu = read_bottom_up(export_dir)
    orig = {st: rr.load_rgba(os.path.join(export_dir, st + ".png")) for st in stems(spec)}
    up = {st: load_upright(find(st), st, bu, (orig[st].shape[1], orig[st].shape[0]), s) for st in stems(spec)}
    d, wp = spec["damage"], spec["weapons"]
    out = {}
    out[d["panel"]], out[d["sheet"]] = redraw_damage(orig[d["panel"]], up[d["panel"]], up[d["sheet"]], spec, s, level)
    out[wp["housing"]] = redraw_housings(up[wp["housing"]], spec, s, level)
    for r in wp["rails"]: out[r] = redraw_rails(up[r], r, spec, s, level)
    return (out, up) if with_input else out


def scale_of(path, orig_w):
    with Image.open(path) as im: w = im.size[0]
    if w % orig_w: raise SystemExit(f"{path}: {w} texels wide, not a whole multiple of {orig_w}")
    return w // orig_w


def render_all(export_dir, upscale_dir, out_dir, spec, s, level_name):
    os.makedirs(out_dir, exist_ok=True)
    out = redraw_all(export_dir, lambda st: rr.find_texture(upscale_dir, st) or _missing(st, upscale_dir), spec, s, level_name)
    for st, a in out.items(): Image.fromarray(a).save(os.path.join(out_dir, st + ".png"))
    return out


def _missing(stem, where): raise SystemExit(f"{stem}: neither .png nor .dds in {where}")


def pack_redrawn(pack):
    pj = os.path.join(pack, "pack.json")
    if not os.path.exists(pj): return False
    with open(pj, encoding="utf-8") as f:
        try: return "hud_redraw" in json.load(f)
        except ValueError: return False


def note_in_pack(src, dst, spec, spec_path, level_name, s):
    """src's pack.json with hud_redraw written into dst; FILES.sha256 made again when dst is the pack and has one."""
    pj = os.path.join(src, "pack.json")
    if os.path.exists(pj):
        with open(pj, encoding="utf-8", newline="") as f: raw = f.read()
        meta = json.loads(raw); nl = "\r\n" if "\r\n" in raw else "\n"
        meta["hud_redraw"] = {"tool": "tools/hud-redraw.py", "tool_sha256": rr.sha256_of(os.path.abspath(__file__)),
                              "distress_sha256": rr.sha256_of(os.path.join(HERE, "panel_distress.py")),
                              "spec": "tools/hud-redraw/hud.json", "spec_sha256": rr.sha256_of(spec_path),
                              "distress": level_name, "seed": spec["distress"]["seed"], "scale": s,
                              "textures": ", ".join(stems(spec))}
        with open(os.path.join(dst, "pack.json.part"), "w", encoding="utf-8", newline="") as f:
            f.write(json.dumps(meta, indent=2, ensure_ascii=False).replace("\n", nl) + nl)
        os.replace(os.path.join(dst, "pack.json.part"), os.path.join(dst, "pack.json"))
    if os.path.abspath(src) == os.path.abspath(dst) and os.path.exists(os.path.join(dst, "FILES.sha256")):
        sp = importlib.util.spec_from_file_location("hd_pack", os.path.join(HERE, "hd-pack.py"))
        hp = importlib.util.module_from_spec(sp); sp.loader.exec_module(hp); hp.write_sums(dst)


def pack_step(export_dir, pack, out, spec, spec_path, level_name, base=None):
    """The five textures of a pack redrawn in its own format, in place (out None; the files it starts from kept in
    pack/.hud-base/) or into out. Returns the files written and the scale."""
    bu = read_bottom_up(export_dir)
    base_dir = os.path.join(pack, BASE); dst = out or pack
    if base is None and not os.path.isdir(base_dir) and pack_redrawn(pack):
        raise SystemExit(f"{pack}: its damage panel and housings are a redraw already (pack.json has hud_redraw) and there "
                         f"is no {BASE}; give the upscale's own files with --base DIR")
    src = {}
    for st in stems(spec):
        p = (rr.find_texture(base, st) if base else None) or rr.find_texture(base_dir, st) or (None if base else rr.find_texture(pack, st))
        if p is None: raise SystemExit(f"{st}: not in {base or pack}")
        src[st] = p
    if out is None:
        os.makedirs(base_dir, exist_ok=True)
        for st, p in src.items():
            if os.path.dirname(os.path.abspath(p)) != os.path.abspath(base_dir):
                b = os.path.join(base_dir, os.path.basename(p))
                try: os.link(p, b)                  # the pack's file is replaced below, never written into
                except OSError:
                    import shutil; shutil.copy2(p, b)
                src[st] = b
    with Image.open(os.path.join(export_dir, spec["damage"]["panel"] + ".png")) as im: ow = im.size[0]
    s = scale_of(src[spec["damage"]["panel"]], ow)
    fmt = os.path.splitext(src[spec["damage"]["panel"]])[1]
    if fmt == ".dds":
        import etcpak, dds_mips
    os.makedirs(dst, exist_ok=True)
    written = []
    res, ins = redraw_all(export_dir, lambda st: src[st], spec, s, level_name, with_input=True)
    for st, a in res.items():
        path = os.path.join(dst, st + fmt)
        if np.array_equal(a, ins[st]):
            # nothing drawn on it (wear 0: only the panel's wires change): the file it started from, not encoded again
            if os.path.abspath(src[st]) != os.path.abspath(path):
                import shutil; shutil.copy2(src[st], path + ".part"); os.replace(path + ".part", path)
            written.append(os.path.basename(path)); continue
        if fmt == ".dds": dds_mips.write_dds(path + ".part", np.ascontiguousarray(a[::-1] if st in bu else a), etcpak)
        else: Image.fromarray(a).save(path + ".part", format="PNG")
        os.replace(path + ".part", path)
        other = os.path.join(dst, st + (".png" if fmt == ".dds" else ".dds"))
        if out is None and os.path.exists(other): os.remove(other)
        written.append(os.path.basename(path))
    note_in_pack(pack, dst, spec, spec_path, level_name, s)
    return written, s


# ------------------------------------------------------------------------------------------------------------- self-check

def synthetic(spec, s=4):
    """A made-up panel (olive with a frame and two crossing wires at the left), sheet and housing strip: no game art."""
    W, H = spec["damage"]["size"]
    p = np.zeros((H, W, 4), np.uint8)
    p[2:122, 14:238] = (66, 72, 32, 255); p[2:4, 14:238] = (101, 101, 101, 255); p[120:122, 14:238] = (33, 33, 33, 255)
    red, yel = spec["damage"]["wires"]["colours"]["red"], spec["damage"]["wires"]["colours"]["yellow"]
    for y in range(4, 40):
        x = 4 + int(3 * math.sin(y / 6)); p[y, x] = (*red[0], 255); p[y, x + 1] = (*red[1], 255)
        p[y, x + 3] = (*yel[0], 255); p[y, x + 4] = (*yel[1], 255)
    sh = np.zeros((130, 305, 4), np.uint8); sh[..., :3] = 200; sh[..., 3] = 255
    sh[2:9, 10:50] = (20, 20, 20, 255)
    hz = np.zeros((128, 256, 4), np.uint8)
    for k in range(5): hz[k * 24:k * 24 + 24, 0:182] = (67, 67, 67, 255); hz[k * 24 + 2:k * 24 + 21, 18:165] = (66, 72, 32, 255)
    return p, sh, hz


def self_check():
    spec = load_spec(); s = 4; ok = True
    p, sh, hz = synthetic(spec, s)
    up = rr.resize_to(p, 256 * s, 128 * s)
    upsh = rr.resize_to(sh, 305 * s, 130 * s)
    for lv in ("0", "light", "medium"):
        a, b = redraw_damage(p, up, upsh, spec, s, level_of(spec, lv))
        a2, b2 = redraw_damage(p, up, upsh, spec, s, level_of(spec, lv))
        same = np.array_equal(a, a2) and np.array_equal(b, b2)
        under = all(np.array_equal(a[y * s:(y + h) * s, x * s:(x + w) * s], redraw_wires(up, p, spec["damage"]["wires"], s)[y * s:(y + h) * s, x * s:(x + w) * s])
                    for _, _, (x, y, w, h), _ in footprints(spec))
        print(f"damage {lv}: reproducible {same}, stamps' places untouched {under}")
        ok &= same and under
    uh = rr.resize_to(hz, 256 * s, 128 * s)
    hh = redraw_housings(uh, spec, s, level_of(spec, "medium"))
    m = housing_mask(spec, s) == 0
    keep = all(np.array_equal(hh[y * s:(y + 24) * s, x * s:(x + 182) * s][m], uh[y * s:(y + 24) * s, x * s:(x + 182) * s][m]) for x, y in spec["weapons"]["variants"])
    print(f"housings: stamped places untouched {keep}"); ok &= keep
    return ok


# ------------------------------------------------------------------------------------------------------------- main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", nargs="?", choices=["measure", "render", "pack"])
    ap.add_argument("export", nargs="?"); ap.add_argument("upscale", nargs="?"); ap.add_argument("out", nargs="?")
    ap.add_argument("--spec", default=SPEC)
    ap.add_argument("--scale", type=int, default=None, help="render: texels a pixel (8)")
    ap.add_argument("--distress", default=None, help="the wear's level: 0, light or medium (the spec's default)")
    ap.add_argument("--out", dest="out_dir", help="pack: write the files here and leave the pack as it is")
    ap.add_argument("--base", help="pack: the upscale's own files (a backup), when the pack's are a redraw already")
    ap.add_argument("--self-check", action="store_true")
    a = ap.parse_args(argv)
    if a.self_check: return 0 if self_check() else 1
    spec = load_spec(a.spec)
    lv = a.distress or spec["distress"]["default"]
    if a.cmd == "measure":
        o = rr.load_rgba(os.path.join(a.export, spec["damage"]["panel"] + ".png"))
        res = [{k: v for k, v in p.items() if k != "mask"} for p in measure_wires(o, spec["damage"]["wires"])]
        txt = json.dumps(res, indent=1)
        if a.upscale: open(a.upscale, "w").write(txt)
        else: print(txt)
        return 0
    if a.cmd == "render":
        render_all(a.export, a.upscale, a.out, spec, a.scale or 8, lv)
        return 0
    if a.cmd == "pack":
        if not a.export or not a.upscale or a.out: ap.error("pack takes EXPORT_DIR and PACK_DIR (and --out DIR)")
        files, s = pack_step(a.export, a.upscale, a.out_dir, spec, a.spec, lv, a.base)
        print(f"damage panel and housings redrawn at {s}x, wear {lv}: {len(files)} files into {a.out_dir or a.upscale}")
        return 0
    ap.print_help(); return 2


if __name__ == "__main__":
    sys.exit(main())
