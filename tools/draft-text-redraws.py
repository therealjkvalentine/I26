#!/usr/bin/env python3
"""Draft text-redraw specs in bulk: the lettered textures the survey (tools/find-lettered-textures.py) found soft or wrong,
worked through in batches, each fix a spec in tools/text-redraws/ (never art), the words read by a person from the original.

    python3 tools/draft-text-redraws.py queue  --survey SURVEY.tsv --classes CLASSES.tsv --out QUEUE.tsv
    python3 tools/draft-text-redraws.py gather QUEUE.tsv SRC_DIR --pack DIR [--pack DIR] --batch BATCH_DIR [--take 30]
    (read BATCH_DIR/read/*.png and write BATCH_DIR/words.json: below)
    python3 tools/draft-text-redraws.py fit    BATCH_DIR
    python3 tools/draft-text-redraws.py sheet  BATCH_DIR
    python3 tools/draft-text-redraws.py accept BATCH_DIR DECISIONS.json     (copies the approved specs into tools/text-redraws/)

queue: the survey's rows (its --out table: stem, boxes, setting, ink_iou, missions) joined with a table of the review's
classes (stem, cls, text: the words as the review read them), the rows classed SOFT or WRONG that no spec in
tools/text-redraws/ letters yet (and none --skip names), in the order they are seen: missions using it (the trip's, T..,
first), then the pack's ink IoU (the worst first). A car's dent stages and paints (az11bkb1, az12bkb1, az13bkb2: the
model, the part, any stage and paint) are one family; signs that paint the same words in the same texels are grouped when
gathered. Each row's kind: sign (road and town signs, billboards, a building's boards), car, dash, other.

gather: per family, the original and the pack's file (4x, and 8x for a car or the dash: every pack given that holds it),
the letter rows (the survey's detector, both polarities and the quarter turn, each row grown over the thin letters and the
punctuation the detector leaves out), each row's cap height, ink and ground colours, and a reading sheet
(BATCH_DIR/read/<family>.png: the original enlarged with its rows numbered, the pack beside it, each row's crop large).
BATCH_DIR/batch.json holds it all; the textures of a family whose rows hold the same texels as its first share its spec.

words.json, written by the person drafting (the words are read off the original, never guessed):
    {"<family>": {"rows": {"0": "BAR MUSIC", "1": "keep", "2": {"text": "POOL", "font": "Anton", "turn": "cw"}},
                  "add": [{"text": "...", "box": [x0, y0, x1, y1]}], "note": "..."}}
"keep" leaves a row's upscale as it is (a word that cannot be read with confidence, a picture, a pattern); a row not named
is kept too. A dict may fix the font (and "variation"), the colour, the box (the painted letters' extent, original texels,
edges), "turn" ("ccw" reading bottom to top, "cw" top to bottom) for a row laid sideways, "embolden". "add" sets a row the
detector missed. A family whose every row is kept is kept as the upscale.

fit: per row, a face from tools/text-redraws/fonts/ (each shipped face but the hand lettering, at a few weights, the words
set on the row's box at the original's size and compared with the painted letters' coverage: the best overlap wins), its
weight, its box (the row's, the detector's own, or without a border piece at either end) and a quarter texel either way
chosen the same way; the spec rendered by tools/text-redraws.py over each pack's file (a scratch copy) and measured on the
original's grid (blur_corr: the redraw's letters on the painted ones, crispness aside; the ink box's extent; the ink's
colour drift; texels changed outside the rows). PASS when every measure is within what the redraws approved by eye
reach, else FLAG with the reasons; KEEP when no row is set. PASS is a pre-tick on the sheet, not an acceptance: the sheet
is the gate (verdict's note says why). A stroke out of 0.65..1.5 (find-lettered-textures.py's) is answered by another
weight up to three times. BATCH_DIR/specs/ holds the specs, BATCH_DIR/results.tsv the verdicts, measures and times.

sheet: per texture a card (original enlarged | released pack | redraw, the scores and the words), sheets of eight, and
BATCH_DIR/index.html, where each texture is approved or rejected and the choices saved as a decisions file for accept.
Nothing is written into a pack: the renders are scratch copies in BATCH_DIR.

Needs Python 3.10+, numpy, Pillow, scipy (and etcpak only for tools/text-redraws.py's own writing of a pack).
"""
import argparse, csv, glob, hashlib, html, json, math, os, re, shutil, sys, time
import importlib.util
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage as ndi

HERE = os.path.dirname(os.path.abspath(__file__))
def _load(name, file):
    sp = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    m = importlib.util.module_from_spec(sp); sp.loader.exec_module(m); return m
tr = _load("text_redraws", "text-redraws.py")
fl = _load("find_lettered", "find-lettered-textures.py")

CAR = re.compile(r"^([a-z]{2})([0-9c])([0-9x])([a-z]{3})([0-9x])$")
SIGN = [r"^s[a-z]1", r"^ao_1sg", r"^[a-z]{2}_[12]"]
KINDS = ("sign", "car", "dash", "other")
# weights tried for a variable face (clipped to its axis); a fixed face is tried as it is
WEIGHTS = (300, 400, 500, 600, 700, 800, 900)
# the face a sign family is lettered in, once the owner has approved it on the family's first textures (a glob of the
# stem: the face, its weight left to the fit unless given); a row or texture in words.json may still say otherwise
FACE_RULES = {
    "sh1*": {"font": "ArchivoNarrow", "variation": {"Weight": 700}},   # the US shields' EAST, NORTH (sh128n, sh138e, sh138n, sh162e)
    "sd1*": {"font": "Arimo"},                                         # the diamond warning signs (sd1ded_1's DEAD END)
}
# faces kept out of the classifier: hand lettering and scripts, which only a person picks for a row ("font" in words.json)
HAND = {"PermanentMarker", "MrDafoe", "Lobster"}

# ---- the queue ----

def kind_of(stem):
    if stem.startswith("z"): return "dash"
    if CAR.match(stem): return "car"
    if any(re.search(p, stem) for p in SIGN): return "sign"
    return "other"

def family_of(stem):
    """A car's dent stages and paints share their lettering's family (the model, the part); other textures are their own."""
    m = CAR.match(stem)
    return f"{m.group(1)}{m.group(2)}x{m.group(4)}x" if m else stem

def missions_of(s):
    return [m for m in (s or "").replace(" ", "").split(",") if m]

def seen_key(missions):
    """How often a texture is seen: the trip's missions (T..) first, then all."""
    return (sum(1 for m in missions if m.startswith("T")), len(missions))

def specced(specs_dir=tr.SPECS):
    return {t for _, t, _ in tr.load_specs(specs_dir)}

def read_tsv(path):
    with open(path, encoding="utf-8") as f: return list(csv.DictReader(f, delimiter="\t"))

def build_queue(survey, classes, done, skip=()):
    """The SOFT and WRONG rows without a spec, grouped by family, ordered by how often seen then by the pack's ink IoU.
    survey: {stem: survey row}; classes: {stem: {"cls", "text"}}; done: stems a spec letters; skip: globs left out."""
    import fnmatch
    fams = {}
    for stem, c in classes.items():
        if c.get("cls") not in ("SOFT", "WRONG") or stem in done: continue
        if any(fnmatch.fnmatch(stem, g) for g in skip): continue
        s = survey.get(stem, {})
        iou = s.get("ink_iou", ""); iou = float(iou) if iou not in ("", None) else 1.0
        row = {"stem": stem, "cls": c["cls"], "text": c.get("text", ""), "kind": kind_of(stem), "missions": missions_of(s.get("missions")),
               "ink_iou": iou, "stroke": s.get("stroke", ""), "size": s.get("size", ""), "family": family_of(stem)}
        fams.setdefault(row["family"], []).append(row)
    out = []
    for fam, rows in fams.items():
        miss = sorted({m for r in rows for m in r["missions"]})
        for r in rows: r["fam_missions"] = miss
        rows.sort(key=lambda r: (-int(r["cls"] == "WRONG"), r["ink_iou"]))
        out.append((fam, rows))
    out.sort(key=lambda fr: (tuple(-v for v in seen_key(fr[1][0]["fam_missions"])), min(r["ink_iou"] for r in fr[1]), fr[0]))
    return out

def cmd_queue(a):
    survey = {r["stem"].lower(): r for r in read_tsv(a.survey)}
    classes = {}
    for r in read_tsv(a.classes): classes[r["stem"].lower()] = {"cls": r.get("cls", r.get("class", "")), "text": r.get("text", "")}
    q = build_queue(survey, classes, specced(), a.skip)
    with open(a.out, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, delimiter="\t")
        w.writerow(["rank", "family", "stem", "kind", "cls", "ink_iou", "missions", "text"])
        for i, (fam, rows) in enumerate(q, 1):
            for r in rows: w.writerow([i, fam, r["stem"], r["kind"], r["cls"], r["ink_iou"], ",".join(r["fam_missions"]), r["text"]])
    n = sum(len(r) for _, r in q)
    by = {k: sum(len(r) for _, r in q if r[0]["kind"] == k) for k in KINDS}
    print(f"{n} textures in {len(q)} families -> {a.out} ({', '.join(f'{k} {v}' for k, v in by.items())})")

def read_queue(path):
    fams = {}
    for r in read_tsv(path):
        fams.setdefault(r["family"], {"rank": int(r["rank"]), "kind": r["kind"], "missions": r["missions"], "rows": []})["rows"].append(r)
    return sorted(fams.items(), key=lambda kv: kv[1]["rank"])

def pick_batch(queue, take, quota=None):
    """The next families up to `take` textures, signs first: quota {kind: share} gives each kind its share of the batch
    (0.65 signs, 0.25 cars, 0.1 the dash by default), the rest filled from the queue in order."""
    quota = quota or {"sign": 0.65, "car": 0.25, "dash": 0.10}
    chosen, n = [], 0
    for kind in ("sign", "car", "dash", "other"):
        cap = int(round(take * quota.get(kind, 0)))
        got = 0
        for fam, f in queue:
            if f["kind"] != kind or any(fam == c[0] for c in chosen): continue
            if got >= cap: break
            chosen.append((fam, f)); got += len(f["rows"]); n += len(f["rows"])
    for fam, f in queue:
        if n >= take: break
        if not any(fam == c[0] for c in chosen): chosen.append((fam, f)); n += len(f["rows"])
    return chosen

# ---- rows ----

def lum_w(rgb):
    return rgb[..., 0] * 0.299 + rgb[..., 1] * 0.587 + rgb[..., 2] * 0.114

def _best_groups(Lt, pol):
    best = (0.0, [], None)
    for win in fl.WINDOWS:
        for con in fl.CONTRASTS:
            ms, _ = fl.marks(Lt, pol, win, con)
            ms = [m for m in ms if fl.letter_like(m, Lt.shape[0])]
            groups = fl.rows_of(ms)
            score = sum(len(g) * (0.5 if fl.regular(g) else 1.0) for g in groups)
            if score > best[0]: best = (score, groups, (win, con))
    return best

def grow_row(ink, group):
    """The row's box grown over the ink beside and within it the detector left out (a thin I or l, full stops, dashes): the
    components of the ink mask that sit in the row's band, no taller than its letters, chained on at a letter's gap."""
    lab, n = ndi.label(ink, structure=np.ones((3, 3)))
    objs = ndi.find_objects(lab)
    x0, y0 = min(m[0] for m in group), min(m[1] for m in group)
    x1, y1 = max(m[2] for m in group), max(m[3] for m in group)
    h = float(np.median([m[3] - m[1] for m in group])); hmax = max(m[3] - m[1] for m in group)
    comps = []; members = set(); own = {(m[0], m[1], m[2], m[3]) for m in group}
    for i, sl in enumerate(objs, 1):
        if sl is None: continue
        cy0, cy1, cx0, cx1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        if (cx0, cy0, cx1, cy1) in own: members.add(i); continue
        area = int((lab[sl] == i).sum())
        comps.append((cx0, cy0, cx1, cy1, i, area))
    changed = True
    while changed:
        changed = False
        for cx0, cy0, cx1, cy1, i, area in comps:
            if i in members: continue
            ch, cw = cy1 - cy0, cx1 - cx0
            fill = area / float(ch * cw)
            if ch >= 0.55 * h:
                # a letter: about the row's height, not much wider than a letter pair, partly filled, in the row's band
                if ch > 1.35 * hmax + 1 or cw > 1.8 * hmax + 1 or fill < 0.12 or (cw > 1.2 * h and fill < 0.2): continue
                ov = min(cy1, y1 + 0.2 * h) - max(cy0, y0 - 0.2 * h)
                if ov < 0.75 * ch: continue
            else:
                # punctuation (a full stop, a dash, an i's dot): small and wholly within the band
                if cw > 0.8 * h + 1 or area > 0.25 * h * h + 1 or cy0 < y0 - 0.1 * h or cy1 > y1 + 0.1 * h: continue
            gap = max(cx0 - x1, x0 - cx1, 0)
            if gap > 0.9 * h + 1: continue
            members.add(i); changed = True
            x0, y0, x1, y1 = min(x0, cx0), min(y0, cy0), max(x1, cx1), max(y1, cy1)
    # the letters' band: the tall members' median top and bottom; a member wholly above it or reaching well below it (a
    # sign's border, a frame's corner the detector took for a letter) is dropped
    boxes = {i: (sl[1].start, sl[0].start, sl[1].stop, sl[0].stop) for i, sl in enumerate(objs, 1) if i in members}
    if boxes:
        tall = [b for b in boxes.values() if b[3] - b[1] >= 0.6 * max(c[3] - c[1] for c in boxes.values())]
        top, bot = float(np.median([b[1] for b in tall])), float(np.median([b[3] for b in tall])); hb = bot - top
        keep = {i for i, b in boxes.items() if b[1] >= top - 0.25 * hb and b[3] <= bot + 0.45 * hb and b[1] < bot}
        if keep: members = keep
        bs = [boxes[i] for i in members]
        x0, y0, x1, y1 = min(b[0] for b in bs), min(b[1] for b in bs), max(b[2] for b in bs), max(b[3] for b in bs)
    mask = np.isin(lab, list(members)) if members else np.zeros_like(ink)
    comps = sorted(boxes[i] for i in members) if boxes else []
    return (x0, y0, x1, y1), mask, len(members), h, comps

def group_box(group):
    return (min(m[0] for m in group), min(m[1] for m in group), max(m[2] for m in group), max(m[3] for m in group))

def rows_of_texture(orig):
    """The letter rows of an original (H x W x 4): [{box: [x0, y0, x1, y1] edges in texels, turned, polarity, window,
    contrast, height, marks, regular, mask (H x W bool, the row's ink)}], from the detector's best reading per polarity and
    turn, a row of another reading kept where it does not overlap one already taken."""
    L = fl.luminance(orig.astype(np.float32))
    if L is None: return []
    cands = []
    for turned in (False, True):
        Lt = L.T if turned else L
        for pol in (-1, 1):
            score, groups, wc = _best_groups(Lt, pol)
            if score >= 3: cands.append((score, turned, pol, wc, groups))
    cands.sort(key=lambda c: -c[0])
    rows = []; taken = np.zeros(L.shape, bool)
    for score, turned, pol, (win, con), groups in cands:
        Lt = L.T if turned else L
        _, ink = fl.marks(Lt, pol, win, con)
        for g in groups:
            (x0, y0, x1, y1), mask, n, h, comps = grow_row(ink, g)
            c0, c1, c2, c3 = group_box(g)
            if turned: x0, y0, x1, y1 = y0, x0, y1, x1; mask = mask.T; c0, c1, c2, c3 = c1, c0, c3, c2; comps = []
            box_m = np.zeros(L.shape, bool); box_m[y0:y1, x0:x1] = True
            if (taken & box_m).sum() > 0.25 * box_m.sum(): continue
            taken |= box_m
            # the ink is what differs from the ground: heavy letters close together leave light gaps the
            # detector can read as light letters. The ground is the ring of texels round the box where that ring is even (a
            # plain board); where it is not (a plate's frame, a border crossing it) the box's majority: lighter than the
            # box's mean is dark ink, and the reverse
            ring = np.zeros(L.shape, bool); ring[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1] = True; ring &= ~box_m
            pad = L[max(0, y0 - 1):y1 + 1, max(0, x0 - 1):x1 + 1]
            ground = float(np.median(L[ring])) if ring.any() and L[ring].std() < 20 else float(np.median(pad))
            p = -1 if ground > L[box_m].mean() else 1
            if p != pol:
                # the row read again at the other polarity: its letter marks near the box grouped and grown as the detector's
                Lt2 = L.T if turned else L
                ms2, ink2 = fl.marks(Lt2, p, win, con)
                bx0, by0, bx1, by1 = (y0, x0, y1, x1) if turned else (x0, y0, x1, y1)
                ms2 = [m for m in ms2 if fl.letter_like(m, Lt2.shape[0]) and m[0] >= bx0 - 2 and m[2] <= bx1 + 2 and m[1] >= by0 - 2 and m[3] <= by1 + 2]
                gs = fl.rows_of(ms2) or ([ms2] if ms2 else [])
                if not gs: continue
                g2 = max(gs, key=len)
                (x0, y0, x1, y1), mask, n, h, comps = grow_row(ink2, g2)
                c0, c1, c2, c3 = group_box(g2)
                if turned: x0, y0, x1, y1 = y0, x0, y1, x1; mask = mask.T; c0, c1, c2, c3 = c1, c0, c3, c2; comps = []
            rows.append({"box": [int(x0), int(y0), int(x1), int(y1)], "core": [int(c0), int(c1), int(c2), int(c3)], "turned": turned, "polarity": p, "window": win,
                         "contrast": con, "height": round(float(h), 1), "marks": int(n), "regular": bool(fl.regular(g)), "mask": mask,
                         "comps": [[int(v) for v in c] for c in comps]})
    rows.sort(key=lambda r: (r["box"][1], r["box"][0]))
    return rows

def row_colours(orig, row):
    """The row's ink and ground (RGB, the original's): the ink the texels farthest from the ground (the most contrasting
    sixth, at least three: the paint itself, not the texels a letter's edge only partly covers, which would set the
    lettering faded), the ground the texels round the letters (a texel off them), medians; and the erase's cuts on
    text-redraws' brightness (mean RGB)."""
    x0, y0, x1, y1 = row["box"]; H, W = orig.shape[:2]
    X0, Y0, X1, Y1 = max(0, x0 - 2), max(0, y0 - 2), min(W, x1 + 2), min(H, y1 + 2)
    rgb = orig[Y0:Y1, X0:X1, :3].astype(np.float32); a = orig[Y0:Y1, X0:X1, 3]
    ink = row["mask"][Y0:Y1, X0:X1]
    near = ndi.binary_dilation(ink, np.ones((3, 3)))
    gm = ~near & (a > 127)
    if gm.sum() < 3: gm = ~ink & (a > 127)
    ground = np.median(rgb[gm], axis=0) if gm.any() else np.array([128, 128, 128], np.float32)
    iv = rgb[ink & (a > 127)]
    if len(iv) == 0: iv = rgb[ink]
    if len(iv) == 0: return None
    c = row["polarity"] * (lum_w(iv) - lum_w(ground))
    core = iv[c >= np.sort(c)[::-1][min(len(c) - 1, max(2, len(c) // 6))]]
    inkc = np.median(core, axis=0)
    gm_l, ik_l = float(ground.mean()), float(inkc.mean())
    return {"ink": [int(round(v)) for v in inkc], "ground": [int(round(v)) for v in ground],
            "cut": round((gm_l + ik_l) / 2, 1), "halo": round(gm_l + (ik_l - gm_l) * 0.25, 1), "contrast": round(abs(gm_l - ik_l), 1),
            "keyed": bool((a[ink] < 128).any() if ink.any() else False)}

def pack_ink(orig, px, row):
    """The ink as the pack paints the row: its texels over the row's box beyond the original's cut toward the ink, the most
    contrasting sixth, median (RGB); None when the pack has none there."""
    s = px.shape[1] // orig.shape[1]; x0, y0, x1, y1 = (int(round(v)) for v in row["box"])
    sub = px[y0 * s:y1 * s, x0 * s:x1 * s, :3].astype(np.float32).reshape(-1, 3)
    if not len(sub): return None
    gl = float(lum_w(np.array(row["colours"]["ground"], np.float32)))
    c = row["polarity"] * (lum_w(sub) - gl)
    c_cut = row["polarity"] * (row["colours"]["cut"] - float(np.mean(row["colours"]["ground"])))
    on = c > max(c_cut, 0)
    if on.sum() < 6: return None
    v, cv = sub[on], c[on]
    return np.median(v[cv >= np.sort(cv)[::-1][max(2, len(cv) // 6)]], axis=0)

def solid_ink(orig, packs, row):
    """The colour a row is lettered in: the original's paint (row_colours' ink) or, where the pack paints the same ink
    further from the ground (a model sets a dark paint near black), the pack's: a redraw never lighter than the upscale
    it replaces, so it does not read faded beside it."""
    ink = np.array(row["colours"]["ink"], np.float32); gl = float(lum_w(np.array(row["colours"]["ground"], np.float32)))
    best = ink
    for _, px in packs[:1]:
        pk = pack_ink(orig, px, row)
        if pk is not None and row["polarity"] * (float(lum_w(pk)) - gl) > row["polarity"] * (float(lum_w(best)) - gl): best = pk
    return [int(round(v)) for v in best]

def target_cov(orig, row, crop):
    """The painted letters' coverage (0..1) over crop (x0, y0, x1, y1): the texel's brightness between the ground's and
    the ink's, the row's own ink only (other marks in the crop count as ground)."""
    x0, y0, x1, y1 = crop
    L = lum_w(orig[y0:y1, x0:x1, :3].astype(np.float32))
    c = row["colours"]; gl = float(lum_w(np.array(c["ground"], np.float32))); il = float(lum_w(np.array(c["ink"], np.float32)))
    t = np.clip((L - gl) / (il - gl), 0, 1) if abs(il - gl) > 1 else np.zeros_like(L)
    near = ndi.binary_dilation(row["mask"], np.ones((3, 3)))[y0:y1, x0:x1]
    return t * near

# ---- the reading sheet ----

def _font(px):
    for p in (os.path.join(tr.FONTS, "Arimo", "Arimo[wght].ttf"),):
        if os.path.exists(p): return ImageFont.truetype(p, px)
    return ImageFont.load_default()

PALETTE = [(255, 60, 60), (40, 200, 255), (255, 200, 0), (120, 255, 90), (255, 90, 255), (255, 140, 0), (0, 255, 200), (200, 160, 255)]

def nearest(img, k):
    return img.resize((img.width * k, img.height * k), Image.NEAREST)

def reading_sheet(orig, pack, rows, title, path):
    """The original enlarged (nearest) with its rows boxed and numbered, the pack beside it at the same size, then each
    row's crop large (original above, pack below)."""
    h, w = orig.shape[:2]
    k = max(2, min(8, 640 // max(w, 1)))
    o = nearest(Image.fromarray(orig[..., :3]), k)
    p = Image.fromarray(pack[..., :3]).resize(o.size, Image.LANCZOS)
    d = ImageDraw.Draw(o); f = _font(14)
    for i, r in enumerate(rows):
        x0, y0, x1, y1 = r["box"]; c = PALETTE[i % len(PALETTE)]
        d.rectangle([x0 * k - 1, y0 * k - 1, x1 * k, y1 * k], outline=c, width=2)
        d.text((x0 * k + 1, max(0, y0 * k - 15)), str(i), fill=c, font=f, stroke_width=2, stroke_fill=(0, 0, 0))
    crops = []
    for i, r in enumerate(rows):
        x0, y0, x1, y1 = r["box"]
        X0, Y0, X1, Y1 = max(0, x0 - 2), max(0, y0 - 2), min(w, x1 + 2), min(h, y1 + 2)
        cw, ch = X1 - X0, Y1 - Y0
        kk = max(4, min(16, int(900 / max(cw, 1)), int(120 / max(ch, 1))))
        co = nearest(Image.fromarray(orig[Y0:Y1, X0:X1, :3]), kk)
        s = pack.shape[1] / w
        cp = Image.fromarray(pack[int(Y0 * s):int(Y1 * s), int(X0 * s):int(X1 * s), :3]).resize(co.size, Image.LANCZOS)
        if r["turned"]: co, cp = co.rotate(90, expand=True), cp.rotate(90, expand=True)
        crops.append((i, co, cp))
    W = max(o.width * 2 + 10, max([c[1].width + 40 for c in crops], default=0))
    H = 24 + o.height + 10 + sum(c[1].height * 2 + 30 for c in crops)
    sheet = Image.new("RGB", (W, H), (32, 32, 36)); ds = ImageDraw.Draw(sheet)
    ds.text((4, 4), title, fill=(240, 240, 240), font=f)
    sheet.paste(o, (0, 24)); sheet.paste(p, (o.width + 10, 24))
    y = 24 + o.height + 10
    for i, co, cp in crops:
        ds.text((4, y), f"row {i}", fill=PALETTE[i % len(PALETTE)], font=f); y += 18
        sheet.paste(co, (36, y)); y += co.height + 2
        sheet.paste(cp, (36, y)); y += cp.height + 10
    sheet.save(path)

# ---- packs ----

def bottom_up_of(src):
    p = os.path.join(src, "bottom-up.txt")
    if not os.path.exists(p): return set()
    with open(p) as f: return {l.strip().lower() for l in f if l.strip()}

def load_pack(packs, stem, bottom_up):
    """[(scale, path, RGBA upright)] for every pack folder that holds the texture."""
    out = []
    for d in packs:
        f = tr.pack_file(d, stem)
        if not f: continue
        px = tr.read_pack_file(f)
        if stem in bottom_up and f.endswith(".dds"): px = px[::-1]
        out.append((f, np.ascontiguousarray(px)))
    return out

def load_orig(src, stem):
    return np.array(Image.open(os.path.join(src, stem + ".png")).convert("RGBA"))

def row_key(orig, row):
    x0, y0, x1, y1 = row["box"]
    return hashlib.sha1(orig[y0:y1, x0:x1].tobytes()).hexdigest()[:12]

def cmd_gather(a):
    q = read_queue(a.queue)
    bu = bottom_up_of(a.src)
    os.makedirs(os.path.join(a.batch, "read"), exist_ok=True)
    if a.stems:
        want = [s.strip().lower() for s in a.stems.split(",")]
        chosen = [(fam, f) for fam, f in q if any(r["stem"] in want for r in f["rows"])]
        for fam, f in chosen: f["rows"] = [r for r in f["rows"] if r["stem"] in want]
    else:
        chosen = pick_batch(q, a.take)
    batch = {"src": os.path.abspath(a.src), "packs": [os.path.abspath(p) for p in a.pack], "families": []}
    for fam, f in chosen:
        t0 = time.time()
        # the family's textures split by their lettering: those whose rows hold the same texels as the first share its spec
        groups = []
        for r in f["rows"]:
            stem = r["stem"]
            if not os.path.exists(os.path.join(a.src, stem + ".png")): print(f"{stem}: no original, skipped"); continue
            orig = load_orig(a.src, stem); rows = rows_of_texture(orig)
            keys = [row_key(orig, x) for x in rows]
            for g in groups:
                if g["keys"] == keys and g["size"] == list(orig.shape[:2]): g["members"].append(stem); break
            else: groups.append({"lead": stem, "members": [stem], "keys": keys, "size": list(orig.shape[:2]), "rows": rows, "orig": orig, "q": r})
        for g in groups:
            name = g["lead"] if len(groups) > 1 or fam == g["lead"] else fam
            packs = load_pack(a.pack, g["lead"], bu)
            if not packs: print(f"{g['lead']}: in no pack, skipped"); continue
            rows = []
            for i, r in enumerate(g["rows"]):
                r["colours"] = row_colours(g["orig"], r)
                if r["colours"] is None: continue
                rows.append(r)
            pack = packs[0][1]
            title = f"{name}: {', '.join(g['members'])}  [{g['q']['cls']}]  review read: {g['q']['text'] or '-'}  missions: {f['missions'] or '-'}"
            reading_sheet(g["orig"], pack, rows, title, os.path.join(a.batch, "read", f"{name}.png"))
            batch["families"].append({"family": name, "members": g["members"], "lead": g["lead"], "kind": f["kind"], "cls": g["q"]["cls"],
                                      "review_text": g["q"]["text"], "missions": f["missions"], "size": g["size"],
                                      "rows": [{k: v for k, v in r.items() if k != "mask"} for r in rows],
                                      "gather_s": round((time.time() - t0) / max(1, len(groups)), 2)})
            print(f"{name}: {len(g['members'])} texture(s), {len(rows)} rows")
    with open(os.path.join(a.batch, "batch.json"), "w", encoding="utf-8") as fo: json.dump(batch, fo, indent=1)
    n = sum(len(x["members"]) for x in batch["families"])
    print(f"{n} textures in {len(batch['families'])} families -> {a.batch} (read/*.png, then words.json)")

# ---- the draft spec ----

def faces(table):
    """(font, variation) candidates: each shipped face that is not hand lettering, a variable one at a few weights."""
    out = []
    for name, t in sorted(table.items()):
        if t.get("system") or name in HAND: continue
        path = tr.font_path(t)
        if not path or not os.path.exists(path): continue
        f = ImageFont.truetype(path, 40)
        try: axes = f.get_variation_axes()
        except Exception: axes = []
        wa = [a for a in axes if (a["name"].decode() if isinstance(a["name"], bytes) else a["name"]) == "Weight"]
        if wa:
            lo, hi = wa[0]["minimum"], wa[0]["maximum"]
            for w in sorted({min(hi, max(lo, w)) for w in WEIGHTS}): out.append((name, {"Weight": w}))
        else: out.append((name, None))
    return out

def text_op(row, text, font, variation, box, colour, embolden=0.0, turn=None):
    x0, y0, x1, y1 = box
    op = {"op": "text", "text": text, "font": font, "colour": colour}
    if variation: op["variation"] = variation
    if turn:
        # lettering laid sideways: the run's ink on the box turned a quarter ("ccw" reads bottom to top)
        q = [[x0, y1], [x0, y0], [x1, y0], [x1, y1]] if turn == "ccw" else [[x1, y0], [x1, y1], [x0, y1], [x0, y0]]
        op["quad"] = [[round(v, 2) for v in p] for p in q]; op["fit"] = "ink"; op["cap"] = round(x1 - x0, 2)
    else:
        op["box"] = [round(v, 2) for v in box]; op["cap"] = round(y1 - y0, 2)
    if embolden: op["embolden"] = round(embolden, 2)
    if row["colours"]["keyed"]: op["alpha"] = "set"
    return op

def render_cov(op, crop, table, k=4):
    """The op's fill and stroke coverage at the original's size over crop, rendered at k x."""
    x0, y0, x1, y1 = crop
    o = json.loads(json.dumps(op))
    for key in ("box",):
        if key in o: o[key] = [o[key][0] - x0, o[key][1] - y0, o[key][2] - x0, o[key][3] - y0]
    if "quad" in o: o["quad"] = [[p[0] - x0, p[1] - y0] for p in o["quad"]]
    W, H = (x1 - x0) * k, (y1 - y0) * k
    cov, st, sh, _ = tr.text_layers(o, W, H, k, table=table)
    if st is not None: cov = np.maximum(cov, st)
    return cov.reshape(H // k, k, W // k, k).mean(axis=(1, 3))

def ink_weight(c, t):
    """The lettering's weight against the painted letters': its ink (coverage summed) over theirs on the same box, at the
    original's size; 1 the same weight, above it bolder, below it thinner."""
    st = float(t.sum())
    return float(c.sum()) / st if st > 0 else 0.0

def weighed(c, t):
    """The fit's score: the overlap, less a penalty for a weight off the painted letters' (a bold face overlaps a thin
    painting well but reads too heavy)."""
    w = ink_weight(c, t)
    return soft_iou(c, t) - 0.5 * abs(math.log(max(w, 1e-3)))

def soft_iou(a, b):
    den = np.maximum(a, b).sum()
    return float(np.minimum(a, b).sum() / den) if den > 0 else 0.0

def fit_row(orig, row, text, table, cands, fixed=None):
    """The face, weight, embolden and box that set `text` closest to the painted letters (soft IoU at the original's size)."""
    fixed = fixed or {}
    H, W = orig.shape[:2]
    x0, y0, x1, y1 = fixed.get("box", row["box"])
    pad = max(2, int(math.ceil(row["height"] * 0.5)))
    crop = (max(0, int(x0) - pad), max(0, int(y0) - pad), min(W, int(math.ceil(x1)) + pad), min(H, int(math.ceil(y1)) + pad))
    t = target_cov(orig, row, crop)
    colour = fixed.get("colour", row["colours"]["ink"])
    turns = [fixed["turn"]] if fixed.get("turn") else (["ccw", "cw"] if row["turned"] else [None])
    if fixed.get("font") and fixed.get("variation"): pool = [(fixed["font"], fixed["variation"])]
    elif fixed.get("font"): pool = [c for c in cands if c[0] == fixed["font"]] or [(fixed["font"], None)]
    else: pool = cands
    scored = []
    for turn in turns:
        for font, var in pool:
            op = text_op(row, text, font, var, (x0, y0, x1, y1), colour, 0, turn)
            try: c = render_cov(op, crop, table)
            except Exception: continue
            scored.append((weighed(c, t), font, var, turn))
    if not scored: return None
    scored.sort(key=lambda s: -s[0])
    best = None
    ebs = [fixed["embolden"]] if "embolden" in fixed else (0.0, 0.12)
    # the row's box as grown over thin letters and punctuation, and the detector's own (the growth may have taken in a border)
    boxes = [(x0, y0, x1, y1)]
    if "box" not in fixed:
        if row.get("core") and list(row["core"]) != list(row["box"]): boxes.append(tuple(row["core"]))
        cs = row.get("comps") or []
        # a piece of border or frame at either end of the row, taken for a letter: the box without it
        for a, b in ((1, len(cs)), (0, len(cs) - 1), (1, len(cs) - 1)):
            sub = cs[a:b]
            if len(sub) >= max(1, len(cs) - 2) and sub:
                boxes.append((min(c[0] for c in sub), min(c[1] for c in sub), max(c[2] for c in sub), max(c[3] for c in sub)))
        boxes = list(dict.fromkeys(boxes))
    def target_in(b):
        m = np.zeros_like(t); bx0, by0, bx1, by1 = b
        m[max(0, int(math.floor(by0)) - crop[1] - 1):int(math.ceil(by1)) - crop[1] + 1, max(0, int(math.floor(bx0)) - crop[0] - 1):int(math.ceil(bx1)) - crop[0] + 1] = 1
        return t * m
    for _, font, var, turn in scored[:3]:
        for eb in ebs:
            for (bx0, by0, bx1, by1), d in ((b, d) for b in boxes for d in ((0, 0, 0, 0), (-0.25, 0, 0.25, 0), (0.25, 0, -0.25, 0), (0, -0.25, 0, 0.25), (0, 0.25, 0, -0.25))):
                if "box" in fixed and d != (0, 0, 0, 0): continue
                b = (bx0 + d[0], by0 + d[1], bx1 + d[2], by1 + d[3])
                op = text_op(row, text, font, var, b, colour, eb, turn)
                try: c = render_cov(op, crop, table); tb = target_in((bx0, by0, bx1, by1)); sc = weighed(c, tb)
                except Exception: continue
                if best is None or sc > best[0]: best = (sc, op, ink_weight(c, tb))
    return {"fit": round(best[0], 3), "op": best[1], "weight": round(best[2], 2), "ranked": [(round(s, 3), f, v) for s, f, v, _ in scored[:5]],
            "faces": {json.dumps([f, v]): sc for sc, f, v, _ in scored}}

def op_box(op):
    if "box" in op: return op["box"]
    xs, ys = [p[0] for p in op["quad"]], [p[1] for p in op["quad"]]
    return [min(xs), min(ys), max(xs), max(ys)]

def erase_op(row, box=None):
    c = row["colours"]; x0, y0, x1, y1 = box or row["box"]
    key = "lum_below" if row["polarity"] < 0 else "lum_above"
    return {"op": "erase", "rect": [round(x0 - 0.8, 2), round(y0 - 0.8, 2), round(x1 + 0.8, 2), round(y1 + 0.8, 2)],
            "where": {key: c["cut"]}, "grow": 0.6, "halo": {"within": 0.8, key: c["halo"]}}

def entry_face(entry, stem=""):
    """The face words.json sets for the whole texture ("font", "variation"), else the family's (FACE_RULES): every row's
    unless a row says otherwise."""
    import fnmatch
    own = {k: entry[k] for k in ("font", "variation") if k in (entry or {})}
    if own: return own
    for g, face in FACE_RULES.items():
        if stem and fnmatch.fnmatch(stem, g): return dict(face)
    return {}

def words_for(entry, rows, stem=""):
    """[(row index, row, text, fixed)] the person set (kept rows left out), with rows they added."""
    out = []
    spec = (entry or {}).get("rows", {})
    for i, r in enumerate(rows):
        w = spec.get(str(i))
        if w is None or w == "keep" or (isinstance(w, dict) and w.get("text", "keep") == "keep"): continue
        fixed = dict(entry_face(entry, stem), **(w if isinstance(w, dict) else {}))
        text = fixed.pop("text", w if isinstance(w, str) else "")
        if text: out.append((i, r, text, fixed))
    return out

def added_rows(entry, orig, stem=""):
    """Rows the person added (a box the detector missed), measured as the detector's are."""
    out = []
    for j, ad in enumerate((entry or {}).get("add", [])):
        x0, y0, x1, y1 = (int(round(v)) for v in ad["box"])
        # the polarity given, else the one whose ink stands further from its ground (dark letters on a light board read
        # as dark ink; the light gaps between them, read the other way, contrast less)
        L = fl.luminance(orig.astype(np.float32)); con = int(ad.get("contrast", 24))
        best = None
        for pol in ([int(ad["polarity"])] if "polarity" in ad else (-1, 1)):
            ink = pol * (L - ndi.uniform_filter(L, 5, mode="nearest")) > con
            m = np.zeros(L.shape, bool); m[y0:y1, x0:x1] = ink[y0:y1, x0:x1]
            bx = (x0, y0, x1, y1)
            if not ad.get("exact") and m.any():
                # the rough box drawn by eye tightened to the ink in it
                ys, xs = np.where(m); bx = (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)
            r = {"box": list(bx), "turned": bool(ad.get("turn")), "polarity": pol, "window": 5, "contrast": con,
                 "height": float(bx[3] - bx[1]), "marks": 0, "regular": False, "mask": m, "added": True}
            r["colours"] = row_colours(orig, r) if m.any() else None
            if r["colours"] is None: continue
            side = (lum_w(np.array(r["colours"]["ink"], np.float32)) - lum_w(np.array(r["colours"]["ground"], np.float32))) * pol
            if side <= 0: continue
            if best is None or r["colours"]["contrast"] > best["colours"]["contrast"]: best = r
        if best is None: continue
        r = best; x0, y0, x1, y1 = r["box"]
        fixed = dict(entry_face(entry, stem), **{k: v for k, v in ad.items() if k not in ("text", "box", "polarity", "contrast", "exact")})
        fixed["box"] = [x0, y0, x1, y1]
        out.append((f"a{j}", r, ad["text"], fixed))
    return out

# ---- scoring ----

def measures(orig, img, rows):
    """find-lettered-textures.py's ink IoU and stroke over the rows (each with its own contrast setting), area weighted."""
    iou = st = wsum = 0.0; ss = 0.0; ns = 0
    for r in rows:
        x0, y0, x1, y1 = r["box"]
        setting = {"polarity": r["polarity"], "window": r["window"], "contrast": r["contrast"]}
        m = fl.compare(orig.astype(np.float32), img.astype(np.float32), [(x0, y0, x1, y1)], setting)
        if not m or m.get("ink_iou", "") == "": continue
        a = float((x1 - x0) * (y1 - y0)); iou += m["ink_iou"] * a; wsum += a
        if m.get("stroke", "") != "": ss += m["stroke"]; ns += 1
    return {"ink_iou": round(iou / wsum, 3) if wsum else None, "stroke": round(ss / ns, 2) if ns else None}

def to_grid(orig, img):
    """The image box-filtered down to the original's grid (float RGB)."""
    h, w = orig.shape[:2]; k = img.shape[1] // w
    return img[..., :3].astype(np.float32).reshape(h, k, w, k, 3).mean(axis=(1, 3))

def grid_measures(orig, img, rows):
    """The image against the original on the original's grid (the image box-filtered down to it, so a crisp letter and a soft
    one are judged alike, by where their ink falls): per row the ink IoU (each texel cut halfway between the row's ink and
    ground), the brightness difference, the ink's extent (the largest edge offset of the ink's box, texels) and the ink's
    colour drift; area weighted over the rows."""
    small = to_grid(orig, img); o = orig[..., :3].astype(np.float32)
    acc = {"grid_iou": 0.0, "fid": 0.0, "extent": 0.0, "drift": 0.0}; wsum = 0.0
    for r in rows:
        c = r["colours"]; x0, y0, x1, y1 = r["box"]
        X0, Y0, X1, Y1 = max(0, x0 - 1), max(0, y0 - 1), min(o.shape[1], x1 + 1), min(o.shape[0], y1 + 1)
        gl, il = float(lum_w(np.array(c["ground"], np.float32))), float(lum_w(np.array(c["ink"], np.float32)))
        cut = (gl + il) / 2; sgn = 1 if il > gl else -1
        io = sgn * (lum_w(o[Y0:Y1, X0:X1]) - cut) > 0; ii = sgn * (lum_w(small[Y0:Y1, X0:X1]) - cut) > 0
        u = (io | ii).sum(); iou = float((io & ii).sum() / u) if u else 0.0
        fid = float(np.abs(lum_w(o[Y0:Y1, X0:X1]) - lum_w(small[Y0:Y1, X0:X1])).mean())
        if io.any() and ii.any():
            yo, xo = np.where(io); yi, xi = np.where(ii)
            ext = float(max(abs(xo.min() - xi.min()), abs(xo.max() - xi.max()), abs(yo.min() - yi.min()), abs(yo.max() - yi.max())))
            drift = float(np.linalg.norm(o[Y0:Y1, X0:X1][io].mean(0) - small[Y0:Y1, X0:X1][ii].mean(0)))
        else: ext, drift = 99.0, 999.0
        a = float((X1 - X0) * (Y1 - Y0)); wsum += a
        for k_, v in (("grid_iou", iou), ("fid", fid), ("extent", ext), ("drift", drift)): acc[k_] += v * a
    out = {k_: round(v / wsum, 3) for k_, v in acc.items()} if wsum else {k_: None for k_ in acc}
    out["corr"] = blur_corr(orig, img, rows)
    return out

def blur_corr(orig, img, rows, sigma=0.8):
    """How well the image's letters fall on the original's, crispness aside: both on the original's grid (the image box
    filtered down), blurred by a Gaussian of `sigma` texels (the original's stair steps and the upscale's soft edge alike
    smoothed away), and correlated (normalised cross-correlation of brightness) over each row's box and a texel round it;
    area weighted. 1 the same letters in the same places; a letter moved, missing, invented or set in another word falls."""
    small = lum_w(to_grid(orig, img)); o = lum_w(orig[..., :3].astype(np.float32))
    bo, bi = ndi.gaussian_filter(o, sigma), ndi.gaussian_filter(small, sigma)
    acc = wsum = 0.0
    for r in rows:
        x0, y0, x1, y1 = r["box"]
        X0, Y0, X1, Y1 = max(0, x0 - 1), max(0, y0 - 1), min(o.shape[1], x1 + 1), min(o.shape[0], y1 + 1)
        a, b = bo[Y0:Y1, X0:X1].ravel(), bi[Y0:Y1, X0:X1].ravel()
        a, b = a - a.mean(), b - b.mean(); den = math.sqrt(float((a * a).sum() * (b * b).sum()))
        c = float((a * b).sum() / den) if den > 0 else 0.0
        w = float((X1 - X0) * (Y1 - Y0)); acc += c * w; wsum += w
    return round(acc / wsum, 3) if wsum else None

def fidelity(orig, img, rows):
    """The mean brightness difference from the original over the rows (on its grid): grid_measures' "fid"."""
    return grid_measures(orig, img, rows)["fid"]

def outside_change(base, out, ops, s):
    """Pack texels changed outside the ops' rects and boxes (each grown a texel and a half): 0 for a clean redraw."""
    H, W = base.shape[:2]; allow = np.zeros((H, W), bool)
    for op in ops:
        if op["op"] == "erase": x0, y0, x1, y1 = op["rect"]
        elif "box" in op: x0, y0, x1, y1 = op["box"]
        elif "quad" in op:
            xs, ys = [p[0] for p in op["quad"]], [p[1] for p in op["quad"]]; x0, y0, x1, y1 = min(xs), min(ys), max(xs), max(ys)
        else: continue
        allow[max(0, int((y0 - 1.5) * s)):int(math.ceil((y1 + 1.5) * s)), max(0, int((x0 - 1.5) * s)):int(math.ceil((x1 + 1.5) * s))] = True
    diff = (np.abs(out.astype(int) - base.astype(int)).max(axis=2) > 1) & ~allow
    return int(diff.sum())

def verdict(scales, fits, min_corr, min_fit, drift_allow=40.0):
    """PASS when at every scale the redraw's letters fall on the painted ones (blur_corr `min_corr` or more), its ink's box
    is within a texel and a half of theirs, its ink colour within 40 of theirs (and of the solid ink chosen), nothing changed outside the rows, and every
    row's face fits at `min_fit`; else FLAG with the reasons. PASS is a pre-tick on the sheet, not an acceptance: no
    measure against the original's texels can tell a good redraw from the pack's upscale (an upscale box filtered down is
    the original, near enough; the redraws approved by eye score from 0.65 to 0.97 in blur_corr, the pack 0.99), so the
    measures catch the gross failures (a word moved, stretched over a border, the wrong colour) and the person decides.
    find-lettered-textures.py's pack-size ink IoU and stroke are reported only: they score a soft blob above a crisp letter."""
    why = []
    for s in scales:
        tag = f"{s['scale']}x"; n = s["grid_new"]
        if n["corr"] is None: why.append(f"{tag}: no ink measured"); continue
        if n["corr"] < min_corr: why.append(f"{tag}: letters off the painted ones (corr {n['corr']})")
        if n["extent"] > 1.5: why.append(f"{tag}: ink box off by {n['extent']} texels")
        if n["drift"] > drift_allow: why.append(f"{tag}: ink colour drift {n['drift']}")
        if s["outside"]: why.append(f"{tag}: {s['outside']} texels changed outside the rows")
    for i, f in fits:
        if f < min_fit: why.append(f"row {i}: face fit {f}")
    return ("PASS" if not why else "FLAG"), why

def spec_name(fam):
    k = fam["kind"]
    return {"sign": "signs", "car": "plates", "dash": "dash"}.get(k, "lettering") + f"-{fam['family']}.json"

def cmd_fit(a):
    with open(os.path.join(a.batch, "batch.json"), encoding="utf-8") as f: batch = json.load(f)
    wp = os.path.join(a.batch, "words.json")
    with open(wp, encoding="utf-8") as f: words = json.load(f)
    table = tr.font_table(); cands = faces(table); bu = bottom_up_of(batch["src"])
    for d in ("specs", "renders"): os.makedirs(os.path.join(a.batch, d), exist_ok=True)
    results = []
    for fam in batch["families"]:
        if a.only and fam["family"] not in a.only.split(","): continue
        t0 = time.time(); name = fam["family"]; entry = words.get(name)
        orig = load_orig(batch["src"], fam["lead"])
        rows = rows_of_texture(orig)
        for r in rows: r["colours"] = row_colours(orig, r)
        rows = [r for r in rows if r["colours"] is not None]
        if len(rows) != len(fam["rows"]):
            print(f"{name}: {len(rows)} rows now, {len(fam['rows'])} when gathered: gather again and check words.json's row numbers")
        to_set = words_for(entry, rows, fam["lead"]) + added_rows(entry, orig, fam["lead"])
        res = {"family": name, "members": fam["members"], "kind": fam["kind"], "cls": fam["cls"], "missions": fam["missions"],
               "review_text": fam["review_text"], "gather_s": fam.get("gather_s", 0), "note": (entry or {}).get("note", ""),
               "words": [], "rows_total": len(rows), "rows_set": len(to_set), "scales": [], "why": []}
        if entry is None:
            res.update(verdict="UNREAD", why=["no words.json entry"]); results.append(res); continue
        if not to_set:
            res.update(verdict="KEEP", why=[(entry or {}).get("note") or "every row kept as the upscale"]); results.append(res)
            for path, px in load_pack(batch["packs"], fam["lead"], bu)[:1]:
                s_ = px.shape[1] // orig.shape[1]; res["scales"] = []; res["keep_scale"] = s_
                Image.fromarray(px).save(os.path.join(a.batch, "renders", f"{fam['lead']}-{s_}x-pack.png"))
            res["fit_s"] = round(time.time() - t0, 1); continue
        fits, texts = [], []
        packs = load_pack(batch["packs"], fam["lead"], bu)
        for i, r, text, fixed in to_set:
            if "colour" not in fixed: fixed["colour"] = solid_ink(orig, packs, r)
        set_fixed = {i: fixed for i, _, _, fixed in to_set}
        for i, r, text, fixed in to_set:
            ft = fit_row(orig, r, text, table, cands, fixed)
            if ft is None: res["why"].append(f"row {i}: no face renders {text!r}"); continue
            fits.append((i, ft["fit"])); texts.append((i, r, ft))
            res["words"].append({"row": i, "text": text, "font": ft["op"]["font"], "variation": ft["op"].get("variation"), "weight": ft["weight"],
                                 "embolden": ft["op"].get("embolden", 0), "fit": ft["fit"], "ranked": ft["ranked"]})
        # one face for the texture's lettering where it costs each row little: the face with the best summed overlap over
        # the rows the classifier chose (a row whose face the person fixed keeps it), refitted if it loses no row more than 0.08
        free = [(i, r, ft) for i, r, ft in texts if "font" not in dict(set_fixed.get(i, {}))]
        if len(free) > 1:
            keys = set.intersection(*(set(ft["faces"]) for _, _, ft in free))
            if keys:
                best_key = max(keys, key=lambda k: sum(ft["faces"][k] for _, _, ft in free))
                if all(ft["faces"][best_key] >= max(ft["faces"].values()) - 0.08 for _, _, ft in free):
                    font, var = json.loads(best_key)
                    for n, (i, r, ft) in enumerate(texts):
                        if any(i == j for j, _, _ in free):
                            text = ft["op"]["text"]; fx = dict(set_fixed.get(i, {}), font=font)
                            if var: fx["variation"] = var
                            nf = fit_row(orig, r, text, table, cands, fx)
                            if nf: texts[n] = (i, r, nf); fits = [(k, nf["fit"] if k == i else f) for k, f in fits]
                    res["words"] = [dict(w, font=font, variation=var, fit=next(ft["fit"] for i, _, ft in texts if i == w["row"]),
                                         embolden=next(ft["op"].get("embolden", 0) for i, _, ft in texts if i == w["row"]))
                                    if any(w["row"] == i for i, _, _ in free) else w for w in res["words"]]
        set_rows = [r for _, r, _ in texts]
        ops = [erase_op(r, op_box(ft["op"])) for _, r, ft in texts] + [ft["op"] for _, _, ft in texts]
        for (_, r, ft) in texts: r["box"] = [int(math.floor(v + 0.25)) if k < 2 else int(math.ceil(v - 0.25)) for k, v in enumerate(op_box(ft["op"]))]
        packs = load_pack(batch["packs"], fam["lead"], bu)
        res["scales"] = []
        for path, px in packs:
            s = px.shape[1] // orig.shape[1]
            out = tr.apply_ops(px, orig, ops, table=table)
            sc = {"scale": s, "pack": path, "old": measures(orig, px, set_rows), "new": measures(orig, out, set_rows),
                  "grid_old": grid_measures(orig, px, set_rows), "grid_new": grid_measures(orig, out, set_rows), "outside": outside_change(px, out, ops, s)}
            res["scales"].append(sc)
            Image.fromarray(out).save(os.path.join(a.batch, "renders", f"{fam['lead']}-{s}x.png"))
            Image.fromarray(px).save(os.path.join(a.batch, "renders", f"{fam['lead']}-{s}x-pack.png"))
        # each row's ink on the side of the ground the original's is (dark on light stays dark), and at the painted weight
        for i, r, ft in texts:
            gl = float(lum_w(np.array(r["colours"]["ground"], np.float32)))
            il = float(lum_w(np.array(ft["op"].get("colour", r["colours"]["ink"]), np.float32)))
            if (il - gl) * r["polarity"] <= 0: res["why"].append(f"row {i}: ink on the wrong side of the ground")
            if not 0.75 <= ft.get("weight", 1) <= 1.3: res["why"].append(f"row {i}: weight {ft.get('weight')} of the painted letters'")
        # the drift allowed: 40, and the shift from the original's paint to the solid ink chosen (solid_ink)
        shift = max([float(np.linalg.norm(np.array(ft["op"].get("colour", r["colours"]["ink"]), np.float32) - np.array(r["colours"]["ink"], np.float32)))
                     for _, r, ft in texts] or [0.0])
        v, why = verdict(res["scales"], fits, a.min_corr, a.min_fit, 40.0 + shift)
        res["why"] += why; res["verdict"] = "FLAG" if res["why"] else v
        spec = {"textures": fam["members"],
                "note": f"{fam['review_text'] or name}: drafted by tools/draft-text-redraws.py, the words read off the original"
                        + (f"; {entry['note']}" if entry.get("note") else ""),
                "ops": ops}
        res["spec"] = spec_name(fam)
        with open(os.path.join(a.batch, "specs", res["spec"]), "w", encoding="utf-8") as fo: json.dump(spec, fo, indent=1, ensure_ascii=False); fo.write("\n")
        res["fit_s"] = round(time.time() - t0, 1)
        results.append(res)
        print(f"{name}: {res['verdict']} {'; '.join(res['why'])} ({res['fit_s']} s)")
    old = {}
    rp = os.path.join(a.batch, "results.json")
    if a.only and os.path.exists(rp):
        with open(rp, encoding="utf-8") as f: old = {r["family"]: r for r in json.load(f)}
    for r in results: old[r["family"]] = r
    allr = [old[f["family"]] for f in batch["families"] if f["family"] in old]
    with open(rp, "w", encoding="utf-8") as f: json.dump(allr, f, indent=1)
    with open(os.path.join(a.batch, "results.tsv"), "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f, delimiter="\t")
        w.writerow(["family", "textures", "kind", "verdict", "rows_set", "rows_total", "grid_iou_old", "grid_iou_new", "fid_old", "fid_new", "iou_old", "iou_new", "stroke_old", "stroke_new",
                    "gather_s", "fit_s", "read_min", "why", "words"])
        for r in allr:
            s0 = r["scales"][0] if r.get("scales") else {"old": {}, "new": {}, "grid_old": {}, "grid_new": {}}
            w.writerow([r["family"], ",".join(r["members"]), r["kind"], r["verdict"], r["rows_set"], r["rows_total"],
                        s0["grid_old"].get("grid_iou"), s0["grid_new"].get("grid_iou"), s0["grid_old"].get("fid"), s0["grid_new"].get("fid"),
                        s0["old"].get("ink_iou"), s0["new"].get("ink_iou"), s0["old"].get("stroke"), s0["new"].get("stroke"),
                        r.get("gather_s"), r.get("fit_s"), (words.get(r["family"]) or {}).get("read_min", ""), "; ".join(r["why"]),
                        " | ".join(x["text"] for x in r["words"])])
    print({v: sum(1 for r in allr if r["verdict"] == v) for v in ("PASS", "FLAG", "KEEP", "UNREAD")})

# ---- the sheet ----

def card(batch_dir, res, src, prev=None, fam=None):
    """original enlarged (nearest) | released pack | redraw, each at the first pack's size (at most 512 wide), with the scores;
    with `prev` (an earlier round's batch folder) its redraw too, before this one, and the owner's note on it and what changed
    (batch.json's "owner" and "change" for the family)."""
    lead = res["members"][0]
    orig = Image.open(os.path.join(src, lead + ".png")).convert("RGB")
    s0 = res["scales"][0] if res.get("scales") else None
    ks = s0["scale"] if s0 else res.get("keep_scale")
    rp = os.path.join(batch_dir, "renders", f"{lead}-{ks}x-pack.png") if ks else None
    pk = Image.open(rp).convert("RGB") if rp and os.path.exists(rp) else None
    rd = Image.open(os.path.join(batch_dir, "renders", f"{lead}-{s0['scale']}x.png")).convert("RGB") if s0 else None
    W = min(512, (pk.width if pk else orig.width * 4)); H = int(round(W * orig.height / orig.width))
    if H > 384: H = 384; W = int(round(H * orig.width / orig.height))
    shown = [pk]; labels = ["original (nearest)", "released pack"]
    if prev is not None:
        pp = os.path.join(prev, "renders", f"{lead}-{ks}x.png")
        shown.append(Image.open(pp).convert("RGB") if ks and os.path.exists(pp) else None); labels.append("earlier round's redraw")
    shown.append(rd); labels.append("redraw" if prev is None else "this round's redraw")
    panels = [orig.resize((W, H), Image.NEAREST)] + [im.resize((W, H), Image.LANCZOS) if im else Image.new("RGB", (W, H), (60, 60, 60)) for im in shown]
    f = _font(13); fb = _font(15)
    lines = [f"{res['family']}  ({', '.join(res['members'])})  {res['cls']} -> {res['verdict']}   missions: {res['missions'] or '-'}"]
    if fam and fam.get("owner"):
        o = fam["owner"]; lines.append(f"owner: {o.get('decision') or '-'}" + (f": \"{o['note']}\"" if o.get("note") else ""))
    if fam and fam.get("change"): lines.append(f"changed: {fam['change']}")
    for sc in res.get("scales", []):
        go, gn = sc["grid_old"], sc["grid_new"]
        lines.append(f"{sc['scale']}x on the original's grid: ink IoU {go['grid_iou']} -> {gn['grid_iou']}   off the original {go['fid']} -> {gn['fid']}"
                     f"   extent {gn['extent']}   drift {gn['drift']}   outside {sc['outside']}   (pack-size ink IoU {sc['old']['ink_iou']} -> {sc['new']['ink_iou']}, stroke {sc['old']['stroke']} -> {sc['new']['stroke']})")
    for wd in res.get("words", []):
        v = wd["variation"]["Weight"] if wd.get("variation") else ""
        lines.append(f"  row {wd['row']}: \"{wd['text']}\"  {wd['font']} {v}{'  +' + str(wd['embolden']) if wd.get('embolden') else ''}  fit {wd['fit']}")
    if res.get("why"): lines.append("why: " + "; ".join(res["why"]))
    th = 22 + 17 * len(lines)
    im = Image.new("RGB", (W * len(panels) + 10 * (len(panels) - 1), H + th + 20), (28, 28, 32)); d = ImageDraw.Draw(im)
    for i, (p, lab) in enumerate(zip(panels, labels)):
        im.paste(p, (i * (W + 10), 20)); d.text((i * (W + 10) + 2, 2), lab, fill=(200, 200, 200), font=f)
    col = {"PASS": (120, 230, 120), "FLAG": (255, 200, 60), "KEEP": (170, 170, 255)}.get(res["verdict"], (230, 230, 230))
    y = H + 26
    for j, l in enumerate(lines):
        d.text((4, y), l, fill=col if j == 0 else (225, 225, 225), font=fb if j == 0 else f); y += 17
    return im

def cmd_sheet(a):
    with open(os.path.join(a.batch, "batch.json"), encoding="utf-8") as f: batch = json.load(f)
    with open(os.path.join(a.batch, "results.json"), encoding="utf-8") as f: results = json.load(f)
    os.makedirs(os.path.join(a.batch, "cards"), exist_ok=True)
    cards = []; fams = {f["family"]: f for f in batch["families"]}
    for r in results:
        im = card(a.batch, r, batch["src"], a.prev, fams.get(r["family"])); p = os.path.join("cards", f"{r['family']}.png"); im.save(os.path.join(a.batch, p)); cards.append((r, p, im))
    sheets = []
    for i in range(0, len(cards), 8):
        grp = cards[i:i + 8]; W = max(c[2].width for c in grp); H = sum(c[2].height + 8 for c in grp)
        sh = Image.new("RGB", (W, H), (16, 16, 18)); y = 0
        for _, _, im in grp: sh.paste(im, (0, y)); y += im.height + 8
        p = f"sheet-{i // 8 + 1:02d}.png"; sh.save(os.path.join(a.batch, p)); sheets.append(p)
    order = {"FLAG": 0, "PASS": 1, "KEEP": 2, "UNREAD": 3}
    cards.sort(key=lambda c: (order.get(c[0]["verdict"], 9), c[0]["family"]))
    counts = {v: sum(1 for r in results if r["verdict"] == v) for v in order}
    ntex = {v: sum(len(r["members"]) for r in results if r["verdict"] == v) for v in order}
    items = []
    for r, p, _ in cards:
        fam = html.escape(r["family"]); default = "approve" if r["verdict"] == "PASS" else ("keep" if r["verdict"] == "KEEP" else "")
        items.append(f"""<section class="card" data-fam="{fam}" data-verdict="{r['verdict']}">
<h2>{fam} <span class="v {r['verdict']}">{r['verdict']}</span> <small>{html.escape(', '.join(r['members']))}</small></h2>
<img src="{p}" alt="{fam}: original, released pack and redraw" loading="lazy">
<p class="why">{html.escape('; '.join(r.get('why', [])) or 'all measures pass')}</p>
<div class="pick">
<label><input type="radio" name="{fam}" value="approve"{' checked' if default == 'approve' else ''}> approve</label>
<label><input type="radio" name="{fam}" value="reject"> reject</label>
<label><input type="radio" name="{fam}" value="keep"{' checked' if default == 'keep' else ''}> keep the upscale</label>
<input type="text" name="{fam}-note" placeholder="note (what is wrong)">
</div></section>""")
    page = f"""<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Relettering batch</title>
<style>
:root {{ --bg:#f6f6f4; --fg:#1d1d1f; --card:#fff; --mute:#666; --line:#ddd; --auto:#1f7a33; --flag:#9a6a00; --keep:#3b4aa8; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#141416; --fg:#eee; --card:#1f1f23; --mute:#aaa; --line:#333; --auto:#7ddc8e; --flag:#ffcc55; --keep:#9fb0ff; }} }}
:root[data-theme="dark"] {{ --bg:#141416; --fg:#eee; --card:#1f1f23; --mute:#aaa; --line:#333; --auto:#7ddc8e; --flag:#ffcc55; --keep:#9fb0ff; }}
body {{ background:var(--bg); color:var(--fg); font:15px/1.45 system-ui, sans-serif; margin:0 auto; max-width:1620px; padding:16px; }}
.card {{ background:var(--card); border:1px solid var(--line); border-radius:8px; padding:12px; margin:0 0 16px; }}
.card img {{ max-width:100%; height:auto; display:block; image-rendering:auto; }}
h2 {{ font-size:17px; margin:0 0 8px; }} h2 small {{ color:var(--mute); font-weight:400; }}
.v {{ font-size:13px; padding:1px 7px; border-radius:10px; border:1px solid currentColor; }}
.PASS {{ color:var(--auto); }} .FLAG {{ color:var(--flag); }} .KEEP {{ color:var(--keep); }}
.why {{ color:var(--mute); margin:6px 0; }} .pick label {{ margin-right:14px; }} .pick input[type=text] {{ width:min(420px, 100%); }}
.bar {{ position:sticky; top:0; background:var(--bg); padding:8px 0; border-bottom:1px solid var(--line); margin-bottom:12px; z-index:2; }}
button {{ font:inherit; padding:4px 12px; }}
</style></head><body>
<h1>Relettering batch</h1>
<p>Each card: the original enlarged (nearest) | the released pack | the redraw, at the pack's size, with the scores and the words set.
PASS: no measure caught a gross fault (approve is pre-ticked; the measures cannot tell a good redraw from a poor one, so look). FLAG: a measure failed (reasons under the card): approve only if it looks right.
KEEP: no row set (the upscale stays). {counts['PASS']} families PASS ({ntex['PASS']} textures), {counts['FLAG']} FLAG ({ntex['FLAG']}), {counts['KEEP']} KEEP ({ntex['KEEP']}).
Sheets: {' '.join(f'<a href="{p}">{p}</a>' for p in sheets)}.</p>
<div class="bar"><button id="save">Save decisions.json</button> <span id="count"></span></div>
{''.join(items)}
<script>
const KEY = 'reletter-' + location.pathname;
function collect() {{ const out = {{}}; document.querySelectorAll('.card').forEach(c => {{ const f = c.dataset.fam;
  const v = c.querySelector('input[type=radio]:checked'); const n = c.querySelector('input[type=text]').value;
  out[f] = {{ decision: v ? v.value : '', note: n }}; }}); return out; }}
function count() {{ const d = collect(); const k = Object.values(d); document.getElementById('count').textContent =
  k.filter(x => x.decision == 'approve').length + ' approved, ' + k.filter(x => x.decision == 'reject').length + ' rejected, ' + k.filter(x => !x.decision).length + ' undecided'; }}
try {{ const s = JSON.parse(localStorage.getItem(KEY) || '{{}}'); for (const [f, v] of Object.entries(s)) {{
  const c = document.querySelector(`.card[data-fam="${{f}}"]`); if (!c) continue; if (v.decision) {{ const r = c.querySelector(`input[value="${{v.decision}}"]`); if (r) r.checked = true; }}
  c.querySelector('input[type=text]').value = v.note || ''; }} }} catch (e) {{}}
document.addEventListener('change', () => {{ try {{ localStorage.setItem(KEY, JSON.stringify(collect())); }} catch (e) {{}} count(); }});
document.addEventListener('input', () => {{ try {{ localStorage.setItem(KEY, JSON.stringify(collect())); }} catch (e) {{}} }});
document.getElementById('save').onclick = () => {{ const b = new Blob([JSON.stringify(collect(), null, 1)], {{type: 'application/json'}});
  const a = document.createElement('a'); a.href = URL.createObjectURL(b); a.download = 'decisions.json'; a.click(); }};
count();
</script></body></html>
"""
    with open(os.path.join(a.batch, "index.html"), "w", encoding="utf-8") as f: f.write(page)
    print(f"{len(cards)} cards, {len(sheets)} sheets -> {os.path.join(a.batch, 'index.html')}")

def cmd_accept(a):
    with open(os.path.join(a.batch, "results.json"), encoding="utf-8") as f: results = {r["family"]: r for r in json.load(f)}
    with open(a.decisions, encoding="utf-8") as f: dec = json.load(f)
    n = 0
    for fam, d in dec.items():
        if d.get("decision") != "approve" or fam not in results or not results[fam].get("spec"): continue
        src = os.path.join(a.batch, "specs", results[fam]["spec"]); dst = os.path.join(a.specs, results[fam]["spec"])
        shutil.copy2(src, dst); n += 1; print(f"{fam}: {dst}")
    print(f"{n} specs approved into {a.specs}")

def cmd_look(a):
    """A texture's region large, the original's texels on a ruled grid (every texel, numbered every five) beside the pack's:
    to read small lettering and draw a row's box for words.json's "add"."""
    orig = load_orig(a.src, a.stem); h, w = orig.shape[:2]
    x0, y0, x1, y1 = (int(v) for v in a.box.split(",")) if a.box else (0, 0, w, h)
    k = max(6, min(32, 900 // max(1, x1 - x0)))
    o = nearest(Image.fromarray(orig[y0:y1, x0:x1, :3]), k)
    packs = load_pack(a.pack, a.stem, bottom_up_of(a.src)) if a.pack else []
    pad = 24; W = o.width + pad; H = o.height + pad
    im = Image.new("RGB", (W * (1 + len(packs)), H), (30, 30, 34)); im.paste(o, (pad, pad)); d = ImageDraw.Draw(im); f = _font(11)
    for i in range(x1 - x0 + 1):
        X = pad + i * k; d.line([(X, pad), (X, H)], fill=(90, 90, 90) if (x0 + i) % 5 else (255, 80, 80))
        if (x0 + i) % 5 == 0: d.text((X - 4, 2), str(x0 + i), fill=(255, 200, 200), font=f)
    for j in range(y1 - y0 + 1):
        Y = pad + j * k; d.line([(pad, Y), (W, Y)], fill=(90, 90, 90) if (y0 + j) % 5 else (255, 80, 80))
        if (y0 + j) % 5 == 0: d.text((1, Y - 6), str(y0 + j), fill=(255, 200, 200), font=f)
    for n, (_, px) in enumerate(packs):
        s = px.shape[1] // w
        im.paste(Image.fromarray(px[y0 * s:y1 * s, x0 * s:x1 * s, :3]).resize(o.size, Image.LANCZOS), (W * (n + 1) + pad, pad))
    im.save(a.out); print(a.out)

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    q = sub.add_parser("queue"); q.add_argument("--survey", required=True); q.add_argument("--classes", required=True)
    q.add_argument("--out", required=True); q.add_argument("--skip", action="append", default=[], help="a glob of stems left out (repeatable)")
    g = sub.add_parser("gather"); g.add_argument("queue"); g.add_argument("src"); g.add_argument("--pack", action="append", required=True)
    g.add_argument("--batch", required=True); g.add_argument("--take", type=int, default=30); g.add_argument("--stems", help="these stems instead of the next in the queue")
    f = sub.add_parser("fit"); f.add_argument("batch"); f.add_argument("--min-corr", type=float, default=0.75, help="blur_corr a row needs (default 0.75: a quarter of the redraws approved by eye score under it)")
    f.add_argument("--min-fit", type=float, default=0.45); f.add_argument("--only", help="families (comma separated)")
    s = sub.add_parser("sheet"); s.add_argument("batch")
    s.add_argument("--prev", help="an earlier round's batch folder: its redraw shown beside this one's, with the owner's notes")
    l = sub.add_parser("look"); l.add_argument("src"); l.add_argument("stem"); l.add_argument("--box", help="x0,y0,x1,y1 (texels)")
    l.add_argument("--pack", action="append", default=[]); l.add_argument("--out", required=True)
    c = sub.add_parser("accept"); c.add_argument("batch"); c.add_argument("decisions"); c.add_argument("--specs", default=tr.SPECS)
    a = ap.parse_args()
    {"queue": cmd_queue, "gather": cmd_gather, "look": cmd_look, "fit": cmd_fit, "sheet": cmd_sheet, "accept": cmd_accept}[a.cmd](a)

if __name__ == "__main__":
    main()
