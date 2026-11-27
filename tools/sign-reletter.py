#!/usr/bin/env python3
"""Re-letter the military restricted-area signs of the HD world pack in Arial, CPU only.

    python tools/sign-reletter.py measure EXPORT_DIR [STEM]
    python tools/sign-reletter.py render  EXPORT_DIR UPSCALE_DIR OUT_DIR [--option D] [--only STEMS]
    python tools/sign-reletter.py pack    EXPORT_DIR PACK_DIR [--out DIR] [--base DIR] [--option D] [--only STEMS]
    python tools/sign-reletter.py --self-check

The signs: sw1mtr_1 (WHITE SANDS MISSILE TEST RANGE, Training A01) and sw1afb_1 (CHENNAULT AIR FORCE BASE, the Nitro Pack's
P18), one layout: a red band with the place's name over RESTRICTED AREA, then on white NO TRESPASSING BEYOND THIS POINT, a
black or red line, a WARNING box at the right with a bold line and two paragraphs of small print, and USE OF DEADLY FORCE
AUTHORIZED along the bottom. The original paints its large lines in a few texels a stroke and the box's paragraphs a texel
and a half high; the upscale makes the large lines soft and misshapen and the paragraphs a smear.

Every line is taken off the pack's picture (the texels the original paints in the line's ink, the upscale's soft edge round
them; filled from the band or the board round them, so the band's streaks, the board, the border and the bolt stay the
upscale's) and set again in Arial (bold or regular, squeezed to the painted line's ink box read off the original), in the
original's ink colour, the WARNING heading's rule drawn as a straight line. The box's small print is set from the layout's
wording option (A to D: tools/sign-reletter/military.json "options"; the recipe names one): wrapped in Arial at the option's
size and line pitch inside the original's box. A light print wear (patches a few per cent fainter, a few pin-holes, from a
fixed seed; tools/panel_distress.py's noise) keeps the new lettering from reading as a vector overlay.

Nothing of the game's art or text tables is in the repository: the layout holds the measured boxes and colours (numbers, from
`measure`) and the signs' words typed as plain sign wording; the board is the player's own pack, the ink masks the player's
own export. Arial is the machine's (Windows' Fonts folder, or I26_ARIAL_DIR: arial.ttf, arialbd.ttf), as the HD menu art's.

`pack` is the HD recipes' step (tools/hd-recipes/world-4x.json and world-8x.json, after the lettering): it redraws the
pack's own files in their format (.dds: BC7 with mips, rows stored bottom up as bottom-up.txt says), keeps the files it
started from in PACK_DIR/.sign-base/ (a rerun starts from those, so it writes the same files), names the redraw in
pack.json (FILES.sha256 made again when there is one), and skips a sign the pack does not hold (the 8x holds no world
texture). --out DIR leaves the pack as it is. Needs numpy, scipy, Pillow (and etcpak for .dds).
"""
import argparse, importlib.util, json, math, os, shutil, sys
import numpy as np
from PIL import Image, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
LAYOUT = os.path.join(HERE, "sign-reletter", "military.json")
BASE = ".sign-base"
sys.path.insert(0, HERE)
import panel_distress as pd   # noqa: E402


def _load(name, file):
    sp = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    m = importlib.util.module_from_spec(sp); sp.loader.exec_module(m); return m


tr = _load("text_redraws", "text-redraws.py")


def load_layout(path=LAYOUT):
    with open(path, encoding="utf-8") as f: return json.load(f)


def option_of(layout, name=None):
    name = (name or layout["default_option"]).upper()
    if name not in layout["options"]: raise SystemExit(f"option {name!r}: one of {', '.join(layout['options'])}")
    return name, layout["options"][name]


# ---------------------------------------------------------------------------------------------------------- the ink masks

def coverage(rgb, ground, ink):
    """Each texel's share of ink between the ground's colour and the ink's (its colour projected on the line between them)."""
    g = np.asarray(ground, np.float32); d = np.asarray(ink, np.float32) - g
    return np.clip(((rgb.astype(np.float32) - g) * d).sum(-1) / max(float((d * d).sum()), 1.0), 0, 1)


def rect_slices(rect, s=1):
    x0, y0, x1, y1 = rect
    return (slice(max(0, int(math.floor(y0 * s))), max(0, int(math.ceil(y1 * s)))),
            slice(max(0, int(math.floor(x0 * s))), max(0, int(math.ceil(x1 * s)))))


def ink_box(cov, rect, level=0.5):
    """The painted ink's box in the rect, to a fraction of a texel: the first and last rows and columns whose ink reaches
    `level`, widened by the share the texel outside them holds."""
    ys, xs = rect_slices(rect); c = cov[ys, xs]
    col, row = c.max(0), c.max(1)
    def ends(p, off):
        on = np.nonzero(p >= level)[0]
        if not len(on): return None
        a, b = on[0], on[-1]
        lo = a - (float(p[a - 1]) if a > 0 else 0.0); hi = b + 1 + (float(p[b + 1]) if b + 1 < len(p) else 0.0)
        return off + lo, off + hi
    ex, ey = ends(col, xs.start), ends(row, ys.start)
    if ex is None or ey is None: return None
    return [round(ex[0], 2), round(ey[0], 2), round(ex[1], 2), round(ey[1], 2)]


def measure_line(orig, line, grounds):
    """A line's ink colour (the median of its most inked texels) and its painted ink box, from the original."""
    rgb = orig[..., :3].astype(np.float32); ys, xs = rect_slices(line["rect"])
    g = np.asarray(grounds[line["ground"]], np.float32)
    sub = rgb[ys, xs].reshape(-1, 3); dist = np.sqrt(((sub - g) ** 2).sum(-1))
    far = sub[dist >= np.percentile(dist, 97)]
    ink = np.median(far, 0)
    cov = coverage(rgb, g, ink)
    return [int(round(v)) for v in ink], ink_box(cov, line["rect"])


def measure_grounds(orig, sign):
    rgb = orig[..., :3].astype(np.float32)
    return {k: [int(round(v)) for v in np.median(np.concatenate([rgb[rect_slices(r)[0], rect_slices(r)[1]].reshape(-1, 3) for r in rs]), 0)]
            for k, rs in sign["ground_samples"].items()}


def measure(export_dir, layout, stems=None):
    out = {}
    for stem, sign in layout["signs"].items():
        if stems and stem not in stems: continue
        orig = load_original(export_dir, stem)
        grounds = measure_grounds(orig, sign)
        lines = {}
        for ln in sign["lines"]:
            ink, box = measure_line(orig, ln, grounds)
            lines[ln["id"]] = {"ink": ink, "box": box}
        out[stem] = {"grounds": grounds, "lines": lines}
    return out


# ------------------------------------------------------------------------------------------------------------- the block

def font_file(name):
    t = tr.font_table()[name]; p = tr.font_path(t)
    if p is None: raise SystemExit(f"{name}: {t['file']} is not installed (Arial is the machine's own: Windows' Fonts folder, "
                                   "or I26_ARIAL_DIR naming a folder with arial.ttf and arialbd.ttf)")
    return p


_FONTS = {}
def measure_font(name, size=400):
    k = (name, size)
    if k not in _FONTS: _FONTS[k] = ImageFont.truetype(font_file(name), size)
    return _FONTS[k]


def text_width(text, name, em):
    """The run's advance in texels at em texels to the em."""
    return measure_font(name).getlength(text) * em / 400.0


def wrap(text, name, em, width):
    """Greedy word wrap at the face's own advances."""
    lines, cur = [], ""
    for w in text.split(" "):
        t = (cur + " " + w).strip()
        if not cur or text_width(t, name, em) <= width: cur = t
        else: lines.append(cur); cur = w
    if cur: lines.append(cur)
    return lines


def block_lines(sign, opt):
    """The box's small print as (text, face, em, x0, cap top) lines: the bold line, then each paragraph wrapped at the box's
    width, at the option's size and pitch, a paragraph's gap between them."""
    b = sign["block"]; x0, width = b["x0"], b["width"]
    words = {"installation": sign["installation"]}
    out = []
    y = opt.get("top", b["top"])
    em, pitch = opt["em"], opt["pitch"]
    capr = tr.cap_ratio(b["font"])
    for i, para in enumerate(opt["paragraphs"]):
        if i: y += opt.get("gap", 1.0)
        for ln in wrap(para.format(**words), b["font"], em, width):
            out.append((ln, b["font"], em, x0, y)); y += pitch
    return out, capr


def block_ops(sign, opt, colour):
    lines, capr = block_lines(sign, opt)
    ops = []
    for text, face, em, x0, top in lines:
        cap = em * capr; w = text_width(text, face, em)
        ops.append({"op": "text", "text": text, "font": face, "cap": round(cap, 3), "center": [x0 + w / 2.0, top + cap / 2.0],
                    "colour": colour})
    return ops


# ------------------------------------------------------------------------------------------------------------- the redraw

def paint(paints, item, ink):
    """An item's colour: its own "colour", else its "paint" (the layout's paints), else the original's measured ink."""
    if "colour" in item: return item["colour"]
    if "paint" in item: return paints[item["paint"]]
    return ink


def text_ops(sign, meas, opt, paints=None):
    """The lettering's ops (tools/text-redraws.py's text and lines) for one sign and wording option."""
    ops = []; paints = paints or {}
    for ln in sign["lines"]:
        m = meas["lines"][ln["id"]]
        if ln.get("role") == "erase": continue
        if ln.get("role") == "rule":
            x0, y0, x1, y1 = m["box"]; y = (y0 + y1) / 2.0
            ops.append({"op": "lines", "segments": [[x0, y, x1, y]], "width": ln.get("width", y1 - y0), "colour": paint(paints, ln, m["ink"]),
                        "cap": "butt"})
            continue
        if ln.get("role") == "block": continue
        op = {"op": "text", "text": ln["text"], "font": ln["font"], "cap": round(m["box"][3] - m["box"][1], 2),
              "box": m["box"], "colour": paint(paints, ln, m["ink"])}
        if ln.get("embolden"): op["embolden"] = ln["embolden"]
        ops.append(op)
    sub = sign["block"]["sub"]
    if opt.get("sub", True):
        sm = meas["lines"][sub["id"]]
        text = opt.get("sub_text", sign["installation"])
        box = list(sm["box"])
        # the bold line's ink starts where the painted one does; its length is Arial's at the painted cap height
        cap = box[3] - box[1]
        w = text_width(text, sub["font"], cap / tr.cap_ratio(sub["font"]))
        if sub.get("fit") == "width": w = box[2] - box[0]
        box[2] = box[0] + min(w, sign["block"]["x0"] + sign["block"]["width"] - box[0])
        ops.append({"op": "text", "text": text, "font": sub["font"], "cap": round(cap, 2), "box": [round(v, 2) for v in box],
                    "colour": paint(paints, sub, sm["ink"])})
    ops += block_ops(sign, opt, paint(paints, sign["block"], meas["lines"][sign["block"]["id"]]["ink"]))
    return ops


def erase(px, orig, sign, meas, s, seed=0, stem=""):
    """The pack's picture with every line's painted letters taken off: each line's rect (the painted letters and the
    upscale's soft, ringing edge round them, on a flat band or board) filled smoothly from the texels round it, then given
    a grain as fine and as strong as the pack's own on that ground (measured in the ground samples, a fixed seed), so the
    fill does not read as a flat patch. The band's streaks and the board's border, outside the rects, stay the upscale's."""
    H, W = px.shape[:2]
    rgb = px[..., :3].astype(np.float32)
    hole = np.zeros((H, W), bool); ground_of = np.zeros((H, W), np.int8)
    names = list(sign["ground_samples"])
    for ln in sign["lines"]:
        ys, xs = rect_slices(ln.get("erase", ln["rect"]), s)
        hole[ys, xs] = True; ground_of[ys, xs] = names.index(ln["ground"])
    # each ground's holes filled only from the texels round them that are that ground (the pack's colour there within
    # "tolerance" of its colour in the ground samples): a rule, the band's edge or a letter's halo beside a rect does not
    # bleed into the fill
    out = rgb.copy(); tol = float(sign.get("tolerance", 24))
    samples = []
    for g in names:
        m = np.zeros((H, W), bool)
        for r in sign["ground_samples"][g]:
            ys, xs = rect_slices(r, s); m[ys, xs] = True
        samples.append(m)
    for i, g in enumerate(names):
        sel = hole & (ground_of == i)
        if not sel.any(): continue
        pg = np.median(rgb[samples[i]], 0)
        like = np.sqrt(((rgb - pg) ** 2).sum(-1)) <= tol
        # worked in the holes' box and a margin (what lies farther does not reach the fill)
        ys, xs = np.nonzero(sel); m = 3 * s
        y0, y1, x0, x1 = max(0, ys.min() - m), min(H, ys.max() + 1 + m), max(0, xs.min() - m), min(W, xs.max() + 1 + m)
        sub, ss = sel[y0:y1, x0:x1], (slice(y0, y1), slice(x0, x1))
        out[ss][sub] = tr.inpaint(rgb[ss], sub | ~like[ss], tr.relax_passes(s))[sub]
    # the pack's own grain on each ground: its texels less their blur, in the ground samples
    hp = rgb - tr.gauss2(rgb, 0.6 * s)
    rng = pd.rng_for(seed, stem + ":grain")
    h, w = H // s, W // s
    n = np.stack([pd.noise(rng, h, w, 0.12, s)[:H, :W] for _ in range(3)], -1)
    n = 0.25 * n + 0.75 * n.mean(-1, keepdims=True)     # mostly a light/dark grain, a little colour in it
    for i, g in enumerate(names):
        m = samples[i]
        # robust (the median's spread): a stray letter's edge in a sample does not count
        sd = 1.4826 * np.median(np.abs(hp[m] - np.median(hp[m], 0)), 0) if m.any() else np.zeros(3)
        sel = hole & (ground_of == i)
        out[sel] += (n[sel] * sd)
    return out, hole


def wear(stem, lettered, erased, s, level, seed):
    """A light print wear: the new lettering a few per cent fainter in soft patches, with a few pin-holes, toward the board
    under it (the erased picture). Fixed seed; one pattern at any scale (panel_distress's noise grid)."""
    if not level: return lettered
    H, W = lettered.shape[:2]; h, w = int(round(H / s)), int(round(W / s))
    rng = pd.rng_for(seed, stem)
    n = pd.noise(rng, h, w, level["patch_px"], s)[:H, :W]
    fade = np.clip(level["fade"] * (0.5 + 0.5 * n), 0, 1)
    pin = pd.noise(rng, h, w, level["pin_px"], s)[:H, :W]
    fade = np.maximum(fade, level["pin"] * pd.smoothstep(level["pin_cut"], level["pin_cut"] + 0.4, pin))
    f = fade[..., None]
    return lettered * (1 - f) + erased * f


def redraw(orig, up, stem, layout, meas, option=None):
    """The sign's pack picture (upright RGBA, any whole multiple of the original) re-lettered with the wording option."""
    sign = layout["signs"][stem]; _, opt = option_of(layout, option)
    s = up.shape[1] // orig.shape[1]
    erased, hole = erase(up, orig, sign, meas, s, layout["print"]["seed"], stem)
    base = np.dstack([np.clip(erased + 0.5, 0, 255).astype(np.uint8), up[..., 3]])
    ops = text_ops(sign, meas, opt, layout.get("paints"))
    lettered = tr.apply_ops(base, orig, ops).astype(np.float32)
    # the lettering soft by a fraction of a texel, as the board's own print
    soft = layout["print"].get("soften", 0)
    if soft:
        diff = lettered[..., :3] - erased
        diff = tr.gauss2(diff, soft * s)
        rgb = erased + diff
    else:
        rgb = lettered[..., :3]
    rgb = wear(stem, rgb, erased, s, layout["print"]["wear"], layout["print"]["seed"])
    out = np.dstack([np.clip(rgb + 0.5, 0, 255).astype(np.uint8), up[..., 3]])
    return out


# -------------------------------------------------------------------------------------------------------------- the files

def read_bottom_up(export_dir):
    p = os.path.join(export_dir, "bottom-up.txt")
    if not os.path.exists(p): return frozenset()
    with open(p) as f: return frozenset(l.strip().lower() for l in f if l.strip())


def load_original(export_dir, stem):
    p = os.path.join(export_dir, stem + ".png")
    if not os.path.exists(p): raise SystemExit(f"{stem}: not in {export_dir}")
    return np.array(Image.open(p).convert("RGBA"))


def find_texture(folder, stem):
    for ext in (".png", ".dds"):
        p = os.path.join(folder, stem + ext)
        if os.path.exists(p): return p
    return None


def load_upright(path, stem, bu):
    im = Image.open(path); im.load(); a = np.array(im.convert("RGBA"))
    return np.ascontiguousarray(a[::-1]) if path.endswith(".dds") and stem in bu else a


def stems_of(layout, only=None):
    st = list(layout["signs"])
    if only: st = [s for s in st if s in {o.strip().lower() for o in only.split(",")}]
    return st


def redraw_file(export_dir, path, stem, layout, option, bu, meas=None):
    orig = load_original(export_dir, stem)
    up = load_upright(path, stem, bu)
    if up.shape[1] % orig.shape[1] or up.shape[0] * orig.shape[1] != up.shape[1] * orig.shape[0]:
        raise SystemExit(f"{path}: {up.shape[1]}x{up.shape[0]}, not a whole multiple of the original's {orig.shape[1]}x{orig.shape[0]}")
    meas = meas or layout["signs"][stem]["measured"]
    return redraw(orig, up, stem, layout, meas, option), up


def render_all(export_dir, upscale_dir, out_dir, layout, option, only=None):
    os.makedirs(out_dir, exist_ok=True); bu = read_bottom_up(export_dir); done = []
    for stem in stems_of(layout, only):
        p = find_texture(upscale_dir, stem)
        if p is None: print(f"{stem}: not in {upscale_dir}, skipped"); continue
        out, _ = redraw_file(export_dir, p, stem, layout, option, bu)
        Image.fromarray(out).save(os.path.join(out_dir, stem + ".png")); done.append(stem)
    return done


def sha256_of(path):
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def note_in_pack(src, dst, layout_path, option, done):
    pj = os.path.join(src, "pack.json")
    if os.path.exists(pj):
        with open(pj, encoding="utf-8", newline="") as f: raw = f.read()
        meta = json.loads(raw); nl = "\r\n" if "\r\n" in raw else "\n"
        meta["sign_reletter"] = {"tool": "tools/sign-reletter.py", "tool_sha256": sha256_of(os.path.abspath(__file__)),
                                 "layout": "tools/sign-reletter/military.json", "layout_sha256": sha256_of(layout_path),
                                 "option": option, "font": "Arial (the machine's own, not shipped)", "textures": ", ".join(done)}
        with open(os.path.join(dst, "pack.json.part"), "w", encoding="utf-8", newline="") as f:
            f.write(json.dumps(meta, indent=2, ensure_ascii=False).replace("\n", nl) + nl)
        os.replace(os.path.join(dst, "pack.json.part"), os.path.join(dst, "pack.json"))
    if os.path.abspath(src) == os.path.abspath(dst) and os.path.exists(os.path.join(dst, "FILES.sha256")):
        _load("hd_pack", "hd-pack.py").write_sums(dst)


def pack_redrawn(pack):
    pj = os.path.join(pack, "pack.json")
    if not os.path.exists(pj): return False
    with open(pj, encoding="utf-8") as f:
        try: return "sign_reletter" in json.load(f)
        except ValueError: return False


def pack_step(export_dir, pack, out, layout, layout_path, option, base=None, only=None):
    """The signs of a pack re-lettered in its own format, in place (out None; the files they start from kept in
    pack/.sign-base/) or into out. A sign the pack does not hold is skipped. Returns the files written."""
    bu = read_bottom_up(export_dir); base_dir = os.path.join(pack, BASE); dst = out or pack
    name, _ = option_of(layout, option)
    src = {}
    for st in stems_of(layout, only):
        p = (find_texture(base, st) if base else None) or find_texture(base_dir, st) or (None if base else find_texture(pack, st))
        if p is None: continue
        if not base and os.path.dirname(os.path.abspath(p)) != os.path.abspath(base_dir) and pack_redrawn(pack):
            raise SystemExit(f"{pack}: its signs are re-lettered already (pack.json has sign_reletter) and there is no "
                             f"{BASE}/{os.path.basename(p)}; give the upscale's own files with --base DIR")
        src[st] = p
    if out is None and src:
        os.makedirs(base_dir, exist_ok=True)
        for st, p in src.items():
            if os.path.dirname(os.path.abspath(p)) != os.path.abspath(base_dir):
                b = os.path.join(base_dir, os.path.basename(p))
                if not os.path.exists(b):
                    try: os.link(p, b)               # the pack's file is replaced below, never written into
                    except OSError: shutil.copy2(p, b)
                src[st] = b
    os.makedirs(dst, exist_ok=True); written = []
    for st, p in src.items():
        a, _ = redraw_file(export_dir, p, st, layout, name, bu)
        fmt = os.path.splitext(p)[1]; path = os.path.join(dst, st + fmt)
        if fmt == ".dds":
            import etcpak, dds_mips
            dds_mips.write_dds(path + ".part", np.ascontiguousarray(a[::-1] if st in bu else a), etcpak)
        else:
            Image.fromarray(a).save(path + ".part", format="PNG")
        os.replace(path + ".part", path); written.append(os.path.basename(path))
    if written: note_in_pack(pack, dst, layout_path, name, [os.path.splitext(w)[0] for w in written])
    return written


# ------------------------------------------------------------------------------------------------------------- self-check

def synthetic(layout, stem):
    """A made-up sign in the layout's places: a red band, a white board, every line's box filled with blocky 'letters' in its
    measured ink (vertical bars), the box's paragraphs as grey dashes. No game art."""
    sign = layout["signs"][stem]; meas = sign["measured"]
    a = np.zeros((64, 128, 4), np.uint8); a[..., 3] = 255
    a[..., :3] = meas["grounds"]["board"]; a[:17, :, :3] = meas["grounds"]["band"]
    for ln in sign["lines"]:
        m = meas["lines"][ln["id"]]; x0, y0, x1, y1 = (int(round(v)) for v in m["box"])
        for x in range(x0, x1, 2 if ln.get("role") != "rule" else 1): a[y0:max(y1, y0 + 1), x, :3] = m["ink"]
    return a


def self_check():
    """Each option on a made-up sign: the same output twice, and nothing changed farther than a texel outside the lines'
    erase rects (the board, the band's streaks and the border stay the pack's)."""
    layout = load_layout(); ok = True
    for stem in layout["signs"]:
        o = synthetic(layout, stem); s = 2
        up = np.repeat(np.repeat(o, s, 0), s, 1)
        meas = layout["signs"][stem]["measured"]; sign = layout["signs"][stem]
        inside = np.zeros(up.shape[:2], bool)
        for ln in sign["lines"]:
            x0, y0, x1, y1 = ln.get("erase", ln["rect"])
            ys, xs = rect_slices([x0 - 1, y0 - 1, x1 + 1, y1 + 1], s); inside[ys, xs] = True
        for opt in layout["options"]:
            a = redraw(o, up, stem, layout, meas, opt); b = redraw(o, up, stem, layout, meas, opt)
            same = np.array_equal(a, b); untouched = np.array_equal(a[~inside], up[~inside])
            print(f"{stem} {opt}: reproducible {same}, nothing outside the lines' rects changed {untouched}")
            ok &= same and untouched
    return ok


# ------------------------------------------------------------------------------------------------------------------ main

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", nargs="?", choices=["measure", "render", "pack"])
    ap.add_argument("export", nargs="?"); ap.add_argument("upscale", nargs="?"); ap.add_argument("out", nargs="?")
    ap.add_argument("--layout", default=LAYOUT)
    ap.add_argument("--option", default=None, help="the warning box's wording: A, B, C or D (the layout's default_option)")
    ap.add_argument("--only", default=None, help="the signs to do, comma separated stems")
    ap.add_argument("--out", dest="out_dir", help="pack: write the files here and leave the pack as it is")
    ap.add_argument("--base", help="pack: the upscale's own files, when the pack's are re-lettered already")
    ap.add_argument("--self-check", action="store_true")
    a = ap.parse_args(argv)
    if a.self_check: return 0 if self_check() else 1
    layout = load_layout(a.layout)
    if a.cmd == "measure":
        if not a.export: ap.error("measure takes EXPORT_DIR")
        print(json.dumps(measure(a.export, layout, [a.upscale] if a.upscale else None), indent=1)); return 0
    if a.cmd == "render":
        if not (a.export and a.upscale and a.out): ap.error("render takes EXPORT_DIR UPSCALE_DIR OUT_DIR")
        done = render_all(a.export, a.upscale, a.out, layout, a.option, a.only)
        print(f"{len(done)} signs re-lettered (option {option_of(layout, a.option)[0]}) into {a.out}"); return 0
    if a.cmd == "pack":
        if not a.export or not a.upscale or a.out: ap.error("pack takes EXPORT_DIR and PACK_DIR (and --out DIR)")
        files = pack_step(a.export, a.upscale, a.out_dir, layout, a.layout, a.option, a.base, a.only)
        print(f"signs re-lettered (option {option_of(layout, a.option)[0]}): {len(files)} files into {a.out_dir or a.upscale}"
              + ("" if files else " (the pack holds none of them)"))
        return 0
    ap.print_help(); return 2


if __name__ == "__main__":
    sys.exit(main())
