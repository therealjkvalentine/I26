"""The HD menu art's lettering and clean-up passes, as ops of tools/text-redraws.py's spec format (used by tools/menu-pack.py).

Each op works on a page (or a sprite) of the pack at 4x and reads the original from the export; coordinates are the original's
texels, as every text-redraws spec's. They reproduce the scripts the released pack (2026.11.23) was made with:

  retype      per run {"text", "rect"} the original's ink inside rect (connected pieces wholly inside it) gives the words' box;
              the pack's letters there are taken out (the paper under them made from the original's, its ink inpainted, 4x,
              levelled to the pack's paper round them) and the words set in Arial (bold unless "weight"), stretched onto the
              box at 4x, in the ink's colour (the original's commonest strong ink texel, or "colour"). Options per run or op:
              threshold (38), grow (15), colour, weight (bold, regular, italic), embolden (0.012 of the size for the regular
              and italic), rotate (90, -90), darken, fill "flat" (a small button's face colour), edge (a word joining a leader
              line at the rect's left), box (the word's ink box given, [x0, y0, x1, y1]: a letter joined to a leader line).
  reset       per run the line is fitted to the original's print (Arial regular, regular emboldened, bold; size, tracking and
              place searched against the 1x ink by correlation; or "fix": [face, px, tracking] placed at "left"), the pack's
              letters inpainted out and the fit set at 4x in "colour" (or the least-squares ink of the original).
  small_text  (a step, not a spec op) the small print through bicubic 4x and an unsharp mask instead of the model's.
  key-marks   the printed "(C)", "(V)", "(R)" key marks replaced by the circled marks the game draws for them (font record
              69's braces: the install's Lee face's O as a ring and the letter inside, emboldened, as ShellText.Ringed draws
              them), at "marks": [[x0, y0, x1, y1, letter], ...]; "erase": "red" (the red ink in the box) or "dark" (ink
              darker than the paper by "threshold").
  title       a page's title replaced by the same title as another pack file prints it ("from", "from_rect", "rect").
  lineart     (a step) a blueprint sprite's dark 1-texel lines traced into smooth strokes.
Fonts: Arial from the system's font folder (Windows' own copy, as the release used); I26_ARIAL_DIR names a folder holding
arial.ttf, arialbd.ttf and ariali.ttf instead. Without them the step stops and says so.
"""
import os, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 4


def cv():
    try:
        import cv2
        return cv2
    except ImportError:
        sys.exit("the menu lettering needs OpenCV: pip install opencv-python-headless")


# ---- fonts ----

ARIAL = {"regular": "arial.ttf", "bold": "arialbd.ttf", "italic": "ariali.ttf"}


def font_dirs():
    d = []
    if os.environ.get("I26_ARIAL_DIR"): d.append(os.environ["I26_ARIAL_DIR"])
    win = os.environ.get("WINDIR") or os.environ.get("SystemRoot")
    if win: d.append(os.path.join(win, "Fonts"))
    if os.environ.get("LOCALAPPDATA"): d.append(os.path.join(os.environ["LOCALAPPDATA"], "Microsoft", "Windows", "Fonts"))
    d += ["/usr/share/fonts/truetype/msttcorefonts", "/Library/Fonts", "/System/Library/Fonts/Supplemental", os.path.expanduser("~/.fonts")]
    return d


def arial(weight):
    """The path of Arial's weight (regular, bold, italic), or a clear stop."""
    want = ARIAL[weight]; alts = {want.lower(), {"regular": "arial.ttf", "bold": "arial bold.ttf", "italic": "arial italic.ttf"}[weight],
                                  {"regular": "arial.ttf", "bold": "arial_bold.ttf", "italic": "arial_italic.ttf"}[weight]}
    for d in font_dirs():
        if not os.path.isdir(d): continue
        for f in os.listdir(d):
            if f.lower() in alts: return os.path.join(d, f)
    sys.exit(f"Arial ({want}) not found in the system's font folder ({', '.join(x for x in font_dirs() if x)}): the menu lettering "
             "is set in Windows' Arial, as the released pack was. Run the build on Windows, or set I26_ARIAL_DIR to a folder holding "
             "arial.ttf, arialbd.ttf and ariali.ttf from a Windows copy you own.")


# ---- retype (r5-menu-art retype.py; r9-modpack retype2.py's "edge") ----

def ink_mask(rgb, t):
    c = cv(); g = c.cvtColor(rgb, c.COLOR_RGB2GRAY).astype(np.int16)
    med = c.medianBlur(g.astype(np.uint8), 21).astype(np.int16)
    return (med - g) > t


def render(text, path, px=400, variation=None, embolden=0.0):
    f = ImageFont.truetype(path, px)
    sw = int(round(embolden * px))
    if variation:
        try: f.set_variation_by_axes(variation)
        except Exception: pass
    l, t, r, b = f.getbbox(text, stroke_width=sw)
    im = Image.new("L", (r - l + 40, b - t + 40), 0)
    ImageDraw.Draw(im).text((20 - l, 20 - t), text, font=f, fill=255, stroke_width=sw, stroke_fill=255)
    a = np.array(im); ys, xs = np.nonzero(a > 8)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def retype(pack, orig, op, ctx=None):
    """pack: the page at 4x (H x W x 3 uint8), orig: the original (h x w x 3). Returns the page lettered again."""
    c = cv()
    t = op.get("threshold", 38); ink = ink_mask(orig, t)
    out = pack.copy(); erase = np.zeros(pack.shape[:2], np.uint8); jobs = []; lift = np.zeros_like(ink)
    g = c.cvtColor(orig, c.COLOR_RGB2GRAY).astype(np.int16); med = c.medianBlur(g.astype(np.uint8), 21).astype(np.int16)
    for run in op["runs"]:
        x0, y0, x1, y1 = run["rect"]
        src = ink if "threshold" not in run else ink_mask(orig, run["threshold"])
        m = np.zeros_like(ink); m[y0:y1, x0:x1] = src[y0:y1, x0:x1]
        # only pieces wholly inside the rect (a button's or a box's edge is left alone)
        n, lab, st, _ = c.connectedComponentsWithStats(m.astype(np.uint8), connectivity=8)
        keep = np.zeros_like(ink); edge = run.get("edge")
        for i in range(1, n):
            x, y, w, h, ar = st[i]
            if h <= 2 and w >= 6: continue                      # a ruled line (the catalogue's leaders), not a letter
            if w > 20 and h > 8 and ar < 0.3 * w * h: continue  # a button's or a box's outline
            if edge and ar < 3: continue                         # a leader's last texel left in the rect
            if (x >= x0 if edge else x > x0) and y > y0 and x + w < x1 and y + h < y1: keep |= lab == i
        if "box" in run:
            # the word's ink box given: every ink texel inside it, a letter joined to a leader line too (the line outside the box stays)
            bx = run["box"]; keep = np.zeros_like(ink); keep[bx[1]:bx[3], bx[0]:bx[2]] = src[bx[1]:bx[3], bx[0]:bx[2]]
        ys, xs = np.nonzero(keep)
        if len(ys) == 0: print("  no ink:", run["text"]); continue
        bx0, by0, bx1, by1 = xs.min(), ys.min(), xs.max() + 1, ys.max() + 1
        big = c.resize(keep.astype(np.uint8) * 255, (pack.shape[1], pack.shape[0]), interpolation=c.INTER_LINEAR) > 60
        big = c.dilate(big.astype(np.uint8), np.ones((run.get("grow", op.get("grow", 15)),) * 2, np.uint8))
        # the ink's colour: the commonest of the original's strongest ink texels (at least half the darkest's depth)
        d = (med - g)[keep]; px = orig[keep].reshape(-1, 3)[d >= 0.5 * d.max()]
        vals, cnt = np.unique(px, axis=0, return_counts=True)
        col = np.array(run.get("colour", op.get("colour")), np.float32) if "colour" in run or "colour" in op else vals[cnt.argmax()].astype(np.float32)
        # every texel the letters touch, faint edge steps too: within the box grown by a texel, darker than the paper by > 10
        soft = np.zeros_like(ink); sy0, sx0 = max(by0 - 1, 0), max(bx0 - 1, 0)
        soft[sy0:by1 + 1, sx0:bx1 + 1] = ((med - g) > 10)[sy0:by1 + 1, sx0:bx1 + 1]
        soft &= c.dilate(keep.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0
        lift |= soft
        big = np.maximum(big, c.dilate((c.resize(soft.astype(np.uint8) * 255, (pack.shape[1], pack.shape[0]), interpolation=c.INTER_LINEAR) > 40).astype(np.uint8), np.ones((9, 9), np.uint8)))
        erase |= big
        flat = None
        if run.get("fill") == "flat":
            reg = orig[y0:y1, x0:x1].reshape(-1, 3).astype(np.float32); rl = reg.mean(1)
            flat = np.median(reg[rl > np.percentile(rl, 50)], 0)
        jobs.append((run, (bx0, by0, bx1, by1), col, big, flat))
    # the paper behind the letters: the original's, its ink inpainted at its own size, then 4x; laid in with a feathered edge
    allink = c.dilate((ink | lift).astype(np.uint8), np.ones((3, 3), np.uint8))
    paper = c.inpaint(orig, allink, 3, c.INPAINT_TELEA)
    paper = c.resize(paper, (pack.shape[1], pack.shape[0]), interpolation=c.INTER_CUBIC).astype(np.float32)
    for _, _, _, big, flat in jobs:
        if flat is not None: continue
        ring = (c.dilate(big, np.ones((9, 9), np.uint8)) > 0) & (erase == 0)
        ring &= out.astype(np.float32).mean(2) > paper.mean(2) - 30
        if ring.sum() < 30: continue
        paper[big > 0] += np.median(out[ring].astype(np.float32), 0) - np.median(paper[ring], 0)
    for run, _, _, big, flat in jobs:
        if flat is not None: paper[big > 0] = flat
    fe = c.GaussianBlur(erase.astype(np.float32), (0, 0), 2.0)[..., None]
    acc = out.astype(np.float32) * (1 - fe) + paper * fe
    for run, (bx0, by0, bx1, by1), col, _, _ in jobs:
        w = run.get("weight", "bold")
        emb = run.get("embolden", op.get("embolden", 0.012) if w in ("regular", "italic") else 0.0)
        cov = render(run["text"], arial(w), variation=run.get("variation"), embolden=emb)
        if run.get("rotate") == 90: cov = np.rot90(cov, 1)
        if run.get("rotate") == -90: cov = np.rot90(cov, -1)
        W, H = (bx1 - bx0) * S, (by1 - by0) * S
        cc = c.resize(cov.astype(np.float32) / 255, (W, H), interpolation=c.INTER_AREA)
        cc = np.clip(cc * run.get("darken", 1.0), 0, 1)[..., None]
        reg = acc[by0 * S:by0 * S + H, bx0 * S:bx0 * S + W]
        acc[by0 * S:by0 * S + H, bx0 * S:bx0 * S + W] = reg * (1 - cc) + col[None, None, :] * cc
    return np.clip(acc + 0.5, 0, 255).astype(np.uint8)


# ---- reset (r14-retouch reset.py) ----

FACES = {"regular": ("regular", 0), "regular+": ("regular", 1), "regular++": ("regular", 2), "bold": ("bold", 0)}
_cache = {}


def render4(text, face, px4, track4):
    """coverage (0..1) of text at 4x: rendered at 8x glyph by glyph (no kerning, as the original's print), reduced by 2"""
    key = (text, face, round(px4, 2), round(track4, 2))
    if key in _cache: return _cache[key]
    w, sw = FACES[face]; f = ImageFont.truetype(arial(w), round(px4 * 2))
    asc, desc = f.getmetrics(); x = 8.0; W = int(f.getlength(text) + len(text) * (abs(track4) * 2 + 2) + 40)
    im = Image.new("L", (W, asc + desc + 16), 0); d = ImageDraw.Draw(im)
    for ch in text:
        d.text((x, 8), ch, font=f, fill=255, stroke_width=sw, stroke_fill=255); x += f.getlength(ch) + track4 * 2
    a = np.asarray(im, np.float32) / 255
    a = a[: a.shape[0] // 2 * 2, : a.shape[1] // 2 * 2].reshape(a.shape[0] // 2, 2, a.shape[1] // 2, 2).mean((1, 3))
    ys, xs = np.nonzero(a > 0.02); a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    _cache[key] = a; return a


def down(c4, ox, oy, H, W):
    can = np.zeros((H * 4, W * 4), np.float32); h, w = c4.shape
    x0, y0 = max(ox, 0), max(oy, 0); x1, y1 = min(ox + w, W * 4), min(oy + h, H * 4)
    if x1 <= x0 or y1 <= y0: return can.reshape(H, 4, W, 4).mean((1, 3)), can
    can[y0:y1, x0:x1] = c4[y0 - oy:y1 - oy, x0 - ox:x1 - ox]
    return can.reshape(H, 4, W, 4).mean((1, 3)), can


def ncc(a, b):
    a = a - a.mean(); b = b - b.mean(); return float((a * b).sum() / (np.sqrt((a * a).sum() * (b * b).sum()) + 1e-9))


def fit(text, D, box, faces):
    H, W = D.shape; bx0, by0, bx1, by1 = box; best = None
    for face in faces:
        c = render4(text, face, 40, 0); px0 = 40 * (by1 - by0) * 4 / c.shape[0]
        for px4 in px0 * np.array([0.94, 0.97, 1.0, 1.03, 1.06]):
            c = render4(text, face, px4, 0); n = max(len(text) - 1, 1)
            tr0 = ((bx1 - bx0) * 4 - c.shape[1]) / n
            for track4 in tr0 + np.array([-0.4, -0.2, 0, 0.2, 0.4]):
                c = render4(text, face, px4, track4)
                cx = bx0 * 4 + ((bx1 - bx0) * 4 - c.shape[1]) // 2; cy = by0 * 4 + ((by1 - by0) * 4 - c.shape[0]) // 2
                for dy in range(-4, 5):
                    for dx in range(-4, 5):
                        s = ncc(down(c, cx + dx, cy + dy, H, W)[0], D)
                        if best is None or s > best[0]: best = (s, face, px4, track4, cx + dx, cy + dy)
    return best


def reset(pack, orig, op, ctx=None):
    c = cv(); pack = pack.copy()
    K = pack.shape[1] // orig.shape[1]; assert K == 4 and pack.shape[0] == orig.shape[0] * 4
    g = c.cvtColor(orig, c.COLOR_RGB2GRAY).astype(np.float32); med = c.medianBlur(g.astype(np.uint8), 21).astype(np.float32)
    medc = np.stack([c.medianBlur(np.ascontiguousarray(orig[..., i]), 21) for i in range(3)], -1).astype(np.float32)
    dark = med - g; fitted = []; erase = np.zeros(pack.shape[:2], np.uint8); jobs = []
    pg = c.cvtColor(pack, c.COLOR_RGB2GRAY).astype(np.float32); pmed = c.medianBlur(pg.astype(np.uint8), 41).astype(np.float32)
    for run in op["runs"]:
        x0, y0, x1, y1 = run["rect"]; t = run.get("threshold", op.get("threshold", 30))
        m = (dark[y0:y1, x0:x1] > t).astype(np.uint8)
        n, lab, st, _ = c.connectedComponentsWithStats(m, connectivity=8); keep = np.zeros_like(m, bool)
        for i in range(1, n):
            x, y, w, h, ar = st[i]
            R = x + w < x1 - x0 or x1 >= orig.shape[1]
            if x > 0 and y > 0 and R and y + h < y1 - y0: keep |= lab == i
        ys, xs = np.nonzero(keep)
        if len(ys) == 0: print("  no ink:", run["text"]); fitted.append(None); continue
        box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
        near = c.dilate(keep.astype(np.uint8), np.ones((3, 3), np.uint8)) > 0
        D = np.where(near, np.clip(dark[y0:y1, x0:x1], 0, None), 0)
        faces = run.get("faces") or list(FACES)
        if "same_as" in run or "fix" in run:
            face, px4, tr4 = (run["fix"][0], run["fix"][1] * 4, run["fix"][2] * 4) if "fix" in run else fitted[run["same_as"]][1:]
            cv4 = render4(run["text"], face, px4, tr4); best = None
            for dy in range(-6, 7):
                for dx in range(-6, 7):
                    ox, oy = (round((run["left"] - x0) * 4) if "left" in run else box[0] * 4) + dx, box[1] * 4 + (box[3] - box[1]) * 2 - cv4.shape[0] // 2 + dy
                    sc = ncc(down(cv4, ox, oy, y1 - y0, x1 - x0)[0], D)
                    if best is None or sc > best[0]: best = (sc, face, px4, tr4, ox, oy)
            s, face, px4, tr4, ox, oy = best
        else: s, face, px4, tr4, ox, oy = fit(run["text"], D, box, faces)
        fitted.append((s, face, px4, tr4))
        c1, c4 = down(render4(run["text"], face, px4, tr4), ox, oy, y1 - y0, x1 - x0)
        o = orig[y0:y1, x0:x1].astype(np.float32); p = medc[y0:y1, x0:x1]; w8 = (c1 > 0.02)
        A = c1[w8][:, None]; col = ((A * (o[w8] - p[w8] + A * p[w8])).sum(0) / (A * A).sum(0))
        if "colour" in run: col = np.array(run["colour"], np.float32)
        k = run.get("fade", op.get("fade", 1.0)); col = np.median(p[w8], 0) + (col - np.median(p[w8], 0)) * k
        print(f"  {run['text']!r}: {face} {px4 / 4:.2f}px tracking {tr4 / 4:+.3f} at ({x0 + ox / 4:.2f},{y0 + oy / 4:.2f}) ncc {s:.4f}")
        soft = (dark[y0:y1, x0:x1] > 8) & (c.dilate(keep.astype(np.uint8), np.ones((5, 5), np.uint8)) > 0)
        big = c.resize(soft.astype(np.uint8) * 255, ((x1 - x0) * 4, (y1 - y0) * 4), interpolation=c.INTER_LINEAR) > 40
        big = c.dilate(big.astype(np.uint8), np.ones((run.get("grow", 11),) * 2, np.uint8))
        reg = (pmed - pg)[y0 * 4:y1 * 4, x0 * 4:x1 * 4] > run.get("pack_threshold", 12)
        band = np.zeros_like(reg); band[max(box[1] - 1, 0) * 4:(box[3] + 1) * 4, max(box[0] - 2, 0) * 4:(box[2] + 2) * 4] = True
        reg &= band
        big |= (reg & (c.dilate(big, np.ones((9, 9), np.uint8)) > 0)).astype(np.uint8)
        big |= c.dilate((c4 > 0.03).astype(np.uint8), np.ones((run.get("cover_grow", 13),) * 2, np.uint8))
        big = c.dilate(big, np.ones((3, 3), np.uint8))
        lim = np.zeros_like(big); lim[max(box[1] - run.get("band", 2), 0) * 4:(box[3] + run.get("band", 2)) * 4, :] = 1; big &= lim
        erase[y0 * 4:y1 * 4, x0 * 4:x1 * 4] |= big
        jobs.append((run, (x0, y0, x1, y1), c4, col, run.get("gain", 1.0)))
    k = np.ones((15, 15), np.uint8)
    paper = c.GaussianBlur(c.morphologyEx(pack, c.MORPH_CLOSE, k), (0, 0), 2.0).astype(np.float32)
    ring = (c.dilate(erase, np.ones((9, 9), np.uint8)) > 0) & (erase == 0)
    nl, lab = c.connectedComponents(c.dilate(erase, np.ones((9, 9), np.uint8)))
    for i in range(1, nl):
        sel = lab == i; r = ring & sel
        if r.sum() > 30: paper[sel] += np.median(pack[r].astype(np.float32), 0) - np.median(paper[r], 0)
    fe = c.GaussianBlur(erase.astype(np.float32), (0, 0), 1.5)[..., None]
    acc = pack.astype(np.float32) * (1 - fe) + paper * fe
    for run, (x0, y0, x1, y1), c4, col, gain in jobs:
        cc = np.clip(c4 * gain, 0, 1)[..., None]; r = acc[y0 * 4:y1 * 4, x0 * 4:x1 * 4]
        acc[y0 * 4:y1 * 4, x0 * 4:x1 * 4] = r * (1 - cc) + col[None, None, :] * cc
    return np.clip(acc + 0.5, 0, 255).astype(np.uint8)


# ---- circled key marks (r8-form-key relabel.py, relabel2.py) ----

def fit_lanczos(a, w, h):
    return np.asarray(Image.fromarray(a).resize((max(1, w), max(1, h)), Image.LANCZOS))


def raster(font, text, size=200):
    """The text's coverage at size px, trimmed to its ink (ShellGlyphs.Raster)."""
    f = font if isinstance(font, tuple) else (font, 0)
    face = ImageFont.truetype(f[0], size); sw = f[1]
    l, t, r, b = face.getbbox(text, stroke_width=sw)
    im = Image.new("L", (r - l + 8 + 2 * sw, b - t + 8 + 2 * sw), 0)
    ImageDraw.Draw(im).text((4 - l + sw, 4 - t + sw), text, font=face, fill=255, stroke_width=sw, stroke_fill=255)
    a = np.asarray(im); ys, xs = np.nonzero(a)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def ringed(lee, letter, tw, th, embolden=1.3, size=200):
    """The circled mark as the game draws record 69's braces (ShellText.Ringed): the face's O stretched over the ink box, the
    letter inside it (7/26 in from the left, 5/26 from the right, 3/22 from top and bottom), emboldened. Godot's embolden of
    1.3 grows the outline by about 1.3 / 32 of the size on each side; the stroke here does the same."""
    ring = fit_lanczos(raster(lee, "O", size), tw, th).copy()
    sw = int(round(embolden * size / 32))
    lx, ly = tw * 7 // 26, th * 3 // 22; lw, lh = tw - lx - tw * 5 // 26, th - 2 * ly
    inner = fit_lanczos(raster((lee, sw), letter, size), lw, lh)
    ring[ly:ly + lh, lx:lx + lw] = np.maximum(ring[ly:ly + lh, lx:lx + lw], inner)
    return ring


def mark_glyph(ctx, letter):
    """The mark at 4x (coverage 0..1): the atlas cell's ink box (13 x 11 shell pixels at 8x) taken down to 4x by Lanczos, as
    relabel.py did with the game's atlas."""
    import json
    g = json.load(open(os.path.join(ctx["export"], "glyphs", "font69.json"), encoding="utf-8"))
    brace = {"C": "{", "V": "|", "R": "}"}[letter]
    x0, y0, x1, y1 = g["glyphs"][brace]["ink"]; w, h = x1 - x0, y1 - y0
    lee = os.path.join(ctx["export"], "glyphs", "lee.ttf")
    if not os.path.exists(lee): sys.exit(f"{lee}: missing (the install's Lee_____.ttf; `i76 menu-export` copies it)")
    cov8 = ringed(lee, letter, w * 8, h * 8)
    return fit_lanczos(cov8, w * 4, h * 4).astype(np.float32) / 255


def marks(pack, orig, op, ctx):
    """Each [x0, y0, x1, y1, letter] (original texels): the printed mark inpainted out, the circled one multiplied in black,
    its left edge at x0, centred on the box's middle row."""
    c = cv(); im = pack.copy(); h, w, _ = im.shape
    mask = np.zeros((h, w), np.uint8)
    if op.get("erase", "red") == "red":
        r, g = im[..., 0].astype(int), im[..., 1].astype(int)
        for x0, y0, x1, y1, _ in op["marks"]:
            X0, X1, Y0, Y1 = int(x0 * 4) - 6, int(x1 * 4) + 6, int(y0 * 4) - 6, int(y1 * 4) + 6
            reg = (r - g)[Y0:Y1, X0:X1] > op.get("threshold", 25)
            mask[Y0:Y1, X0:X1] |= c.dilate(reg.astype(np.uint8), np.ones((7, 7), np.uint8))
    else:
        lum = im.astype(float).mean(2); med = c.medianBlur(lum.astype(np.uint8), 61).astype(float)
        box = np.zeros(lum.shape, np.uint8)
        for x0, y0, x1, y1, _ in op["marks"]:
            X0, X1, Y0, Y1 = round(x0 * 4), round(x1 * 4), round(y0 * 4), round(y1 * 4)
            reg = (med - lum)[Y0 - 8:Y1 + 8, X0 - 8:X1] > op.get("threshold", 30)
            mask[Y0 - 8:Y1 + 8, X0 - 8:X1] |= c.dilate(reg.astype(np.uint8), np.ones((9, 9), np.uint8))
            box[Y0 - 8:Y1 + 8, X0 - 8:X1] = 1
        mask &= box
    out = c.inpaint(np.ascontiguousarray(im[..., ::-1]), mask * 255, 7, c.INPAINT_TELEA)[..., ::-1].astype(float)
    for x0, y0, x1, y1, ch in op["marks"]:
        a = mark_glyph(ctx, ch); gh, gw = a.shape
        cy = int(round((y0 + y1) / 2 * 4)); px = int(round(x0 * 4)); py = cy - gh // 2
        for k in range(3): out[py:py + gh, px:px + gw, k] *= (1 - a)
    return np.clip(out, 0, 255).astype(np.uint8)


# ---- a title from another pack file (r8-form-key title.py) ----

def title(pack, orig, op, ctx):
    """The page's title (dark ink inside "rect") taken out and the title as "from" prints it (inside "from_rect") laid in,
    centred on the old one's ink and on its top row. Rects in original texels."""
    c = cv()
    def ext(im, r):
        x0, y0, x1, y1 = (int(round(v * 4)) for v in r)
        m = im.mean(2)[y0:y1, x0:x1] < 120
        ys = np.where(m.sum(1) > 25)[0]; xs = np.where(m.sum(0) > 8)[0]
        return x0 + xs[0], x0 + xs[-1], y0 + ys[0], y0 + ys[-1]
    spr = np.array(Image.open(os.path.join(ctx["pack"], op["from"])).convert("RGB")).astype(float)
    sx0, sx1, sy0, sy1 = ext(spr, op["from_rect"])
    lum = spr.mean(2); L = lum[sy0 - 8:sy1 + 9, sx0 - 8:sx1 + 9]
    paper = c.GaussianBlur(c.dilate(L.astype(np.float32), np.ones((41, 41), np.uint8)), (0, 0), 8)
    tc = np.percentile(L, 1)
    alpha = np.clip((paper - L) / np.maximum(paper - tc, 1), 0, 1); alpha = np.clip((alpha - 0.12) / 0.88, 0, 1)
    col = spr[sy0 - 8:sy1 + 9, sx0 - 8:sx1 + 9][L < np.percentile(L, 3)].mean(0)
    im = pack.copy()
    bx0, bx1, by0, by1 = ext(im.astype(float), op["rect"])
    lm = im.astype(float).mean(2); mask = np.zeros(lm.shape, np.uint8)
    reg = lm[by0 - 10:by1 + 11, bx0 - 10:bx1 + 11]
    med = c.medianBlur(lm.astype(np.uint8), 61)[by0 - 10:by1 + 11, bx0 - 10:bx1 + 11].astype(float)
    mask[by0 - 10:by1 + 11, bx0 - 10:bx1 + 11] = c.dilate(((med - reg) > 35).astype(np.uint8), np.ones((9, 9), np.uint8))
    out = c.inpaint(np.ascontiguousarray(im[..., ::-1]), mask * 255, 9, c.INPAINT_TELEA)[..., ::-1].astype(float)
    ph, pw = alpha.shape
    ox = (bx0 + bx1) // 2 - (sx0 + sx1) // 2; oy = by0 - sy0
    dx, dy = sx0 - 8 + ox, sy0 - 8 + oy
    for k in range(3): out[dy:dy + ph, dx:dx + pw, k] = out[dy:dy + ph, dx:dx + pw, k] * (1 - alpha) + col[k] * alpha
    return np.clip(out, 0, 255).astype(np.uint8)


# ---- steps (not spec ops): menu-pack.py calls them per file ----

def small_text(cur, orig, args, ctx):
    """r3-dash-upscale menutext.py "apply c_bicubic_sharp": inside a mask of the original's small ink (Oklab distance to the
    7x7 median over 0.10 and darker; pieces at most 14 texels tall; grown a texel; 4x bilinear, cut at half, feathered by a
    Gaussian of 1 px) the original through bicubic 4x and an unsharp mask (radius 3, 120 %, threshold 0) replaces the model's."""
    from scipy import ndimage
    import importlib.util
    spec = importlib.util.spec_from_file_location("ut", os.path.join(os.path.dirname(os.path.abspath(__file__)), "upscale-textures.py"))
    ut = importlib.util.module_from_spec(spec); spec.loader.exec_module(ut)
    o = np.ascontiguousarray(orig[..., :3])
    L = ut.to_oklab(o)
    med = np.stack([ndimage.median_filter(L[..., k], size=7) for k in range(3)], -1)
    d = np.sqrt(((L - med) ** 2).sum(-1)); ink = (d > args.get("threshold", 0.10)) & (L[..., 0] < med[..., 0])
    lab, n = ndimage.label(ink, np.ones((3, 3), bool))
    if n:
        sl = ndimage.find_objects(lab); keep = np.zeros(n + 1, bool)
        for i, s in enumerate(sl, 1): keep[i] = (s[0].stop - s[0].start) <= args.get("max_height", 14)
        ink = keep[lab]
    m = ndimage.binary_dilation(ink, iterations=1)
    H, W = cur.shape[:2]
    M4 = np.array(Image.fromarray((m * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR)) >= 128
    w = ut.gauss(M4[..., None].astype(np.float32), 1.0)[..., 0][..., None]
    txt = np.array(Image.fromarray(o).resize((W, H), Image.BICUBIC).filter(ImageFilter.UnsharpMask(radius=3, percent=120, threshold=0))).astype(np.float32)
    return np.clip(txt * w + cur.astype(np.float32) * (1 - w) + 0.5, 0, 255).astype(np.uint8)


def lineart(cur, orig, args, ctx):
    """r5-menu-art lineart.py trace(): the sprite's dark lines (luminance under 110) as a graph of texel centres, each point
    moved toward its two neighbours' mean "iters" times (stairs straighten, corners and small pieces stay), drawn as round
    strokes "width" texels wide at 4 x "ss" and taken down; ink three texels thick keeps its shape, smoothed; the model's
    fills under the ink, its dark texels by the ink inpainted."""
    from skimage.morphology import skeletonize
    c = cv()
    width, iters, ink_lum, ss = args.get("width", 1.0), args.get("iters", 4), args.get("ink_lum", 110), args.get("ss", 4)
    orig_rgb = np.ascontiguousarray(orig[..., :3]).copy(); orig_rgb[orig[..., 3] == 0] = [255, 0, 255]
    model_rgba = cur
    key = (orig_rgb == [255, 0, 255]).all(-1)
    lum = orig_rgb.astype(np.float32) @ [0.299, 0.587, 0.114]
    ink = (~key) & (lum < ink_lum)
    h, w = ink.shape
    col = np.median(orig_rgb[ink], 0) if ink.any() else np.array([57, 49, 24], np.float32)
    thick = c.morphologyEx(ink.astype(np.uint8), c.MORPH_OPEN, np.ones((3, 3), np.uint8)) > 0
    sk = skeletonize(ink)
    ys, xs = np.nonzero(sk); idx = -np.ones((h, w), int); idx[ys, xs] = np.arange(len(ys))
    pos = np.stack([xs, ys], 1).astype(np.float32) + 0.5
    edges = set()
    for k, (y, x) in enumerate(zip(ys, xs)):
        for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
            yy, xx = y + dy, x + dx
            if not (0 <= yy < h and 0 <= xx < w) or idx[yy, xx] < 0: continue
            if dy and dx and (sk[y, xx] or sk[yy, x]): continue
            edges.add((k, idx[yy, xx]))
    nb = [[] for _ in range(len(pos))]
    for a, b in edges: nb[a].append(b); nb[b].append(a)
    def on(y, x): return 0 <= y < h and 0 <= x < w and sk[y, x]
    ncomp, labl = c.connectedComponents(sk.astype(np.uint8), connectivity=8)
    small = np.bincount(labl[ys, xs]) <= 24 if len(ys) else np.zeros(1, bool)
    fixed = np.zeros(len(pos), bool)
    for k, n in enumerate(nb):
        if len(n) != 2 or small[labl[ys[k], xs[k]]]: fixed[k] = True; continue
        (ay, ax), (by, bx) = [(ys[j] - ys[k], xs[j] - xs[k]) for j in n]
        if ay * by + ax * bx == 0 and all(on(ys[k] + m * dy, xs[k] + m * dx) for m in (2,) for dy, dx in ((ay, ax), (by, bx))):
            fixed[k] = True
    for _ in range(iters):
        new = pos.copy()
        for k, n in enumerate(nb):
            if not fixed[k]: new[k] = 0.5 * pos[k] + 0.25 * (pos[n[0]] + pos[n[1]])
        pos = new
    Z = S * ss
    canvas = np.zeros((h * Z, w * Z), np.uint8)
    t = max(1, int(round(width * Z)))
    for a, b in edges:
        p, q = (pos[a] * Z).astype(int), (pos[b] * Z).astype(int)
        c.line(canvas, tuple(int(v) for v in p), tuple(int(v) for v in q), 255, t, c.LINE_8)
    for k in range(len(pos)):
        if not nb[k]: c.circle(canvas, tuple(int(v) for v in (pos[k] * Z).astype(int)), t // 2, 255, -1)
    lines = c.resize(canvas.astype(np.float32) / 255, (w * S, h * S), interpolation=c.INTER_AREA)
    def smooth(mask, sigma):
        big = c.resize(mask.astype(np.float32), (mask.shape[1] * S, mask.shape[0] * S), interpolation=c.INTER_NEAREST)
        return np.clip((c.GaussianBlur(big, (0, 0), sigma) - 0.5) / 0.18 + 0.5, 0, 1)
    inkc = np.maximum(lines, smooth(thick, 1.2))
    fillm = (~key) & ~ink
    alpha = np.maximum(smooth(fillm, 1.0), inkc)
    base = np.ascontiguousarray(model_rgba[..., :3]).copy()
    near = c.dilate(c.resize(ink.astype(np.uint8), (w * S, h * S), interpolation=c.INTER_NEAREST), np.ones((7, 7), np.uint8))
    dark = (base.astype(np.float32) @ [0.299, 0.587, 0.114] < ink_lum + 30) & (near > 0)
    hole = (dark | (model_rgba[..., 3] < 128)).astype(np.uint8)
    fill = c.inpaint(base, hole, 5, c.INPAINT_TELEA) if not hole.all() else np.zeros_like(base) + col.astype(np.uint8)
    rgb = fill.astype(np.float32) * (1 - inkc[..., None]) + col[None, None, :] * inkc[..., None]
    return np.dstack([np.clip(rgb + .5, 0, 255).astype(np.uint8), (np.clip(alpha, 0, 1) * 255 + .5).astype(np.uint8)])


# the spec ops text-redraws.py hands over (a page's RGB in, RGB out)
PAGE_OPS = {"retype": retype, "reset": reset, "key-marks": marks, "title": title}
