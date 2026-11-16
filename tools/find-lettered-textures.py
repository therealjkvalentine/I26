#!/usr/bin/env python3
"""Find the textures that carry lettering (a word, a figure, a logo a person would read) and score how the HD pack keeps it.

The 1997 art paints its words in a few texels: sign boards, billboards, shop fronts, a car's markings and plate, the dash's
labels. An upscaling model makes most of it soft and some of it wrong (letters melted, invented or missing). Rather than wait
for a player to report each sign, this lists them all: every original texture is searched for rows of letter-sized marks, and,
with --pack, the pack's file is measured against it over those rows. The list is the start of a review, not its end: it is
tuned to miss little (recall over precision), and a person looks at each hit's original beside the pack's before calling it.

    python3 tools/find-lettered-textures.py SRC_DIR --out OUT.tsv [--pack DIR] [--pack DIR ...] [--usage USAGE.tsv]
                                            [--ocr] [--only GLOB] [--all]

SRC_DIR is the folder the game's --export-textures (or I76.Cli) wrote: the originals at their own size and upright, keyed texels
in alpha, and bottom-up.txt (the textures a pack's .dds stores bottom row first: turned upright here, as tools/text-redraws.py does). --pack (one
or more, first match wins: the 8x car and dash pack before the 4x world pack) adds the pack's measures. --usage is a table
(stem, defs, missions) of who uses each texture, copied into the output. --ocr reads the words with EasyOCR when it is
installed (pip install easyocr; CPU is enough: a texture is small); without it the columns stay empty. Writes OUT.tsv, one row
a texture (all with --all, else only the hits), sorted by score. Needs Python 3.10+, numpy, Pillow, scipy.

How a texture is found (any one is enough):
- rows: the luminance less its local mean, cut at a contrast (dark ink on a light ground and light on dark, two neighbourhood
  sizes, the texture upright and turned a quarter for lettering laid sideways), split into connected marks; marks of a letter's
  proportions (3 texels tall or more, not much wider than tall, partly filled) that sit side by side at one height and size
  make a row; three marks or more a row. A regular row (windows, bricks, a grille: marks all of one size and fill) counts
  half. score = the letters found in rows.
- ocr: EasyOCR's detector and reader on the original enlarged 4x (smooth), three letters or digits read at a confidence of 0.3.
- name: the families whose job is lettering (road signs s?1..., billboards ao_1sg?, shop signs ?_1sg..., the dash's z...).

The pack's measures, over the rows' boxes (the original's texels scaled to the pack's): ink_iou, the overlap of the original's
ink (each texel spread over its pack pixels) with the pack's, cut at the same contrast (1 the same letters in the same places;
an upscale's soft edge alone gives about 0.6-0.8, a letter melted or invented far less); stroke, the pack's letters' mean
stroke width over the original's (scaled: 1 the same weight, above it blotted, below it thinned); drift, the RGB distance of
the ink's and the ground's mean colours; ocr_pack and ocr_sim (the share of the original's reading the pack's reading keeps,
0..1) with --ocr. class_hint: a first guess from those (WRONG, SOFT, GOOD); the review decides.
"""
import argparse, csv, difflib, fnmatch, glob, json, os, re, sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

# the families whose job is lettering, by name: road and town signs (sd1, se1, sh1, sj1, sl1, sm1, ss1, sw1), billboards
# (ao_1sg?), a building's sign board (??_1sg??), the dash's and cockpit's art (z...)
NAME_PRIOR = [r"^s[dehjlmsw]1", r"^ao_1sg", r"^.._1sg", r"^z"]
CONTRASTS = (24, 48)
WINDOWS = (5, 11)


def upright(stem, img, bottom_up, path):
    """A pack's .dds as the game shows it: the textures stored bottom row first are flipped (the export's PNGs are upright)."""
    return img.transpose(Image.FLIP_TOP_BOTTOM) if stem.lower() in bottom_up and path.lower().endswith(".dds") else img


def load_rgba(path):
    return np.asarray(Image.open(path).convert("RGBA"), np.float32)


def luminance(rgba):
    """Luminance, keyed texels (alpha under a half) filled with the opaque texels' median so a cut-out's edge is no ink."""
    L = rgba[..., 0] * 0.299 + rgba[..., 1] * 0.587 + rgba[..., 2] * 0.114
    keyed = rgba[..., 3] < 128
    if keyed.all(): return None
    if keyed.any(): L = np.where(keyed, np.median(L[~keyed]), L)
    return L


def marks(L, polarity, window, contrast):
    """Connected marks of ink (polarity +1 light on dark, -1 dark on light) at the contrast against the window's mean."""
    c = polarity * (L - ndi.uniform_filter(L, window, mode="nearest"))
    ink = c > contrast
    lab, n = ndi.label(ink, structure=np.ones((3, 3)))
    out = []
    for i, sl in enumerate(ndi.find_objects(lab), 1):
        if sl is None: continue
        h, w = sl[0].stop - sl[0].start, sl[1].stop - sl[1].start
        area = int((lab[sl] == i).sum())
        out.append((sl[1].start, sl[0].start, sl[1].stop, sl[0].stop, area))
    return out, ink


def letter_like(m, H):
    x0, y0, x1, y1, area = m
    h, w = y1 - y0, x1 - x0
    if h < 3 or h > max(4, min(48, 0.7 * H)): return False
    if w > 3 * h + 2 or area < 3: return False
    fill = area / float(h * w)
    return 0.12 <= fill <= 0.92


def rows_of(ms):
    """Marks grouped into rows: neighbours at one height and size, close beside each other."""
    ms = sorted(ms, key=lambda m: m[0])
    parent = list(range(len(ms)))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for i, a in enumerate(ms):
        ha, cya = a[3] - a[1], (a[1] + a[3]) / 2
        for j in range(i + 1, len(ms)):
            b = ms[j]
            hb, cyb = b[3] - b[1], (b[1] + b[3]) / 2
            hm = max(ha, hb)
            if b[0] - a[2] > 1.4 * 48: break      # sorted by left edge: none further on is near
            if b[0] - a[2] > 1.4 * hm or b[0] - a[2] < -0.5 * hm: continue
            if abs(cya - cyb) > 0.35 * hm or max(ha, hb) > 1.8 * min(ha, hb): continue
            parent[find(j)] = find(i)
    groups = {}
    for i in range(len(ms)): groups.setdefault(find(i), []).append(ms[i])
    return [g for g in groups.values() if len(g) >= 3]


def regular(row):
    """A row of marks all of one size and fill (windows, bricks, a grille), not letters."""
    a = np.array([m[4] for m in row], float); w = np.array([m[2] - m[0] for m in row], float)
    return a.std() / max(a.mean(), 1) < 0.08 and w.std() / max(w.mean(), 1) < 0.08


def detect(L):
    """The best reading of rows over polarities, windows, contrasts and the quarter turn: (score, rows, setting)."""
    best = (0.0, [], None)
    H, W = L.shape
    for turned in (False, True):
        Lt = L.T if turned else L
        for pol in (-1, 1):
            for win in WINDOWS:
                for con in CONTRASTS:
                    ms, _ = marks(Lt, pol, win, con)
                    ms = [m for m in ms if letter_like(m, Lt.shape[0])]
                    rows = rows_of(ms)
                    score = sum(len(r) * (0.5 if regular(r) else 1.0) for r in rows)
                    if score > best[0]:
                        boxes = []
                        for r in rows:
                            x0, y0 = min(m[0] for m in r), min(m[1] for m in r)
                            x1, y1 = max(m[2] for m in r), max(m[3] for m in r)
                            boxes.append((y0, x0, y1, x1) if turned else (x0, y0, x1, y1))
                        best = (score, boxes, dict(turned=turned, polarity=pol, window=win, contrast=con))
    return best


# ---- the words read (optional) ----

_reader = None
def ocr(rgb_u8, scale=4):
    """EasyOCR's reading of an image enlarged `scale` times (smooth), turned a quarter too; (text, mean confidence)."""
    global _reader
    if _reader is None:
        import easyocr
        _reader = easyocr.Reader(["en"], gpu=False, verbose=False)
    im = Image.fromarray(rgb_u8)
    if scale != 1: im = im.resize((im.width * scale, im.height * scale), Image.BICUBIC)
    best = ("", 0.0)
    for k in (0, 1):
        a = np.asarray(im.rotate(-90 * k, expand=True))
        got = [(t, c) for _, t, c in _reader.readtext(a) if c >= 0.3 and len(re.sub(r"[^A-Za-z0-9]", "", t)) >= 2]
        text = " ".join(t for t, _ in got)
        if len(re.sub(r"[^A-Za-z0-9]", "", text)) > len(re.sub(r"[^A-Za-z0-9]", "", best[0])):
            best = (text, float(np.mean([c for _, c in got])) if got else 0.0)
    return best


def norm(t):
    return re.sub(r"[^A-Z0-9]", "", t.upper())


def kept(a, b):
    """The share of reading a that reading b keeps (0..1)."""
    a, b = norm(a), norm(b)
    if not a: return ""
    return round(sum(m.size for m in difflib.SequenceMatcher(None, a, b).get_matching_blocks()) / len(a), 2)


# ---- the pack against the original ----

def stroke_width(ink):
    """Mean stroke width of a mask: twice the mean distance to the ground along its skeleton-ish core."""
    if ink.sum() < 4: return 0.0
    d = ndi.distance_transform_edt(ink)
    core = d >= ndi.maximum_filter(d, 3) - 1e-6
    v = d[core & ink]
    return float(2 * v.mean()) if v.size else 0.0


def compare(orig, pack, boxes, setting):
    """ink_iou, stroke ratio and colour drift of the pack's lettering against the original's over the rows' boxes."""
    s = pack.shape[1] / orig.shape[1]
    Lo, Lp = luminance(orig), luminance(pack)
    if Lo is None or Lp is None or not boxes: return {}
    pol, win, con = setting["polarity"], setting["window"], setting["contrast"]
    ink_o = pol * (Lo - ndi.uniform_filter(Lo, win, mode="nearest")) > con
    ink_p = pol * (Lp - ndi.uniform_filter(Lp, max(3, int(round(win * s))) | 1, mode="nearest")) > con
    up = np.kron(ink_o, np.ones((int(round(s)), int(round(s))), bool))[:Lp.shape[0], :Lp.shape[1]]
    inter = union = 0; so = sp = 0.0; n = 0; inks_o, inks_p, gr_o, gr_p = [], [], [], []
    for x0, y0, x1, y1 in boxes:
        X0, Y0, X1, Y1 = (int(round(v * s)) for v in (max(0, x0 - 1), max(0, y0 - 1), x1 + 1, y1 + 1))
        a, b = up[Y0:Y1, X0:X1], ink_p[Y0:Y1, X0:X1]
        inter += int((a & b).sum()); union += int((a | b).sum())
        wo = stroke_width(ink_o[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1]); wp = stroke_width(b) / s
        if wo > 0 and wp > 0: so += wo; sp += wp; n += 1
        po, pp = orig[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1, :3], pack[Y0:Y1, X0:X1, :3]
        io = ink_o[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1]
        if io.any() and (~io).any(): inks_o.append(po[io].mean(0)); gr_o.append(po[~io].mean(0))
        if b.any() and (~b).any(): inks_p.append(pp[b].mean(0)); gr_p.append(pp[~b].mean(0))
    out = {"ink_iou": round(inter / union, 3) if union else ""}
    out["stroke"] = round(sp / so, 2) if n else ""
    if inks_o and inks_p:
        d1 = np.linalg.norm(np.mean(inks_o, 0) - np.mean(inks_p, 0)); d2 = np.linalg.norm(np.mean(gr_o, 0) - np.mean(gr_p, 0))
        out["drift"] = round(float(max(d1, d2)), 1)
    else: out["drift"] = ""
    return out


def class_hint(row):
    iou, stroke, sim = row.get("ink_iou"), row.get("stroke"), row.get("ocr_sim")
    if iou == "" or iou is None: return ""
    if (sim != "" and sim is not None and sim < 0.5) or iou < 0.35 or (stroke != "" and not 0.6 <= stroke <= 1.7): return "WRONG"
    if iou < 0.6 or (sim != "" and sim is not None and sim < 0.85): return "SOFT"
    return "GOOD"


def pack_file(packs, stem):
    for p in packs:
        for ext in (".dds", ".png", ".webp"):
            f = os.path.join(p, stem + ext)
            if os.path.exists(f): return f
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("src", help="the folder --export-textures wrote (the originals and bottom-up.txt)")
    ap.add_argument("--out", required=True, help="the table to write (.tsv)")
    ap.add_argument("--pack", action="append", default=[], help="a pack folder to measure (repeatable; first match wins)")
    ap.add_argument("--usage", help="a table (stem, defs, missions) of who uses each texture")
    ap.add_argument("--ocr", action="store_true", help="read the words with EasyOCR (pip install easyocr)")
    ap.add_argument("--only", default="*", help="a glob of texture names")
    ap.add_argument("--all", action="store_true", help="write every texture, not only the hits")
    ap.add_argument("--min-score", type=float, default=3, help="the rows' letters a hit needs (default 3)")
    a = ap.parse_args()
    bu_path = os.path.join(a.src, "bottom-up.txt")
    bottom_up = set(l.strip().lower() for l in open(bu_path)) if os.path.exists(bu_path) else set()
    usage = {}
    if a.usage and os.path.exists(a.usage):
        with open(a.usage, encoding="utf-8") as f:
            for r in csv.DictReader(f, delimiter="\t"): usage[r["stem"].lower()] = r
    files = sorted(f for f in glob.glob(os.path.join(a.src, "*.png")) if fnmatch.fnmatch(os.path.basename(f)[:-4].lower(), a.only.lower()))
    rows = []
    for i, f in enumerate(files):
        stem = os.path.basename(f)[:-4].lower()
        img = Image.open(f).convert("RGBA")
        orig = np.asarray(img, np.float32)            # the export is upright as painted
        L = luminance(orig)
        score, boxes, setting = detect(L) if L is not None and min(L.shape) >= 8 else (0.0, [], None)
        named = any(re.search(p, stem) for p in NAME_PRIOR)
        row = {"stem": stem, "size": f"{orig.shape[1]}x{orig.shape[0]}", "score": round(score, 1), "named": int(named),
               "boxes": json.dumps(boxes), "setting": json.dumps(setting) if setting else ""}
        if a.ocr and L is not None:
            t, c = ocr(orig[..., :3].astype(np.uint8)); row["ocr"] = t; row["ocr_conf"] = round(c, 2)
        hit = score >= a.min_score or named or (len(norm(row.get("ocr", ""))) >= 3)
        row["hit"] = int(hit)
        u = usage.get(stem, {}); row["defs"] = u.get("defs", ""); row["missions"] = u.get("missions", "")
        if a.pack and (hit or a.all):
            pf = pack_file(a.pack, stem)
            row["pack"] = pf or ""
            if pf and setting:
                p = np.asarray(upright(stem, Image.open(pf).convert("RGBA"), bottom_up, pf), np.float32)
                row.update(compare(orig, p, boxes, setting))
                if a.ocr:
                    t, c = ocr(p[..., :3].astype(np.uint8), scale=1); row["ocr_pack"] = t
                    row["ocr_sim"] = kept(row.get("ocr", ""), t)
            row["class_hint"] = class_hint(row)
        if hit or a.all: rows.append(row)
        if (i + 1) % 500 == 0: print(f"{i + 1}/{len(files)}", file=sys.stderr)
    cols = ["stem", "size", "score", "named", "hit", "ocr", "ocr_conf", "pack", "ink_iou", "stroke", "drift", "ocr_pack", "ocr_sim",
            "class_hint", "defs", "missions", "boxes", "setting"]
    rows.sort(key=lambda r: -r["score"])
    with open(a.out, "w", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, cols, delimiter="\t", extrasaction="ignore"); w.writeheader()
        for r in rows: w.writerow(r)
    print(f"{len(rows)} rows ({sum(r['hit'] for r in rows)} hits) of {len(files)} textures -> {a.out}")


if __name__ == "__main__":
    main()
