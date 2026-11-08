#!/usr/bin/env python3
"""Clean the ruled lines of the HD menu art's forms (the modern look's upscaled shell backgrounds), CPU only, in place.

The notepad forms (the trip's BUILD AND REPAIR, the CHASSIS CONFIGURATION FORM, the host and join forms, the catalogue,
STANDINGS...) are ruled in a dark ink on ochre paper. Upscaled by a model, each rule comes out thinner and darker than the
original's, in a redder brown, with a pale, greyish band a pixel or two wide along both sides (the model's ringing): on a big
display the rules look outlined in a colour the paper does not have. This step takes the pack's page, finds the rules in the
original (runs of ink eight pixels or longer, horizontal or vertical, in a figure at least 32 long or a small closed box: the
tables, frames, check boxes and underlines, never the lettering, whose figures are shorter) and makes the band round each rule again: the paper the original has there, toned as the
pack renders it, the rule's own colour the original's, blended by the pack's brightness (--mode chroma, the default: the
pack's soft line, in the right colours) or laid crisp from the original's pixels (--mode redraw). The page is written back as
lossless WebP (about four times the lossy file): lossy WebP holds the colour at half the resolution each way, so a one-pixel
rule lends its neighbours its greyness and takes their orange, which is most of the band to begin with.

    python3 tools/menu-rules.py SRC_DIR [--pack DIR] [--only N,N,...] [--out DIR] [--mode chroma|redraw] [--restore]

SRC_DIR holds the records as `i76 --install DIR bg SRC_DIR` writes them (bg<NN>.ppm, 640x480 or the record's own size); --pack
the pack folder (default: the game's user://tex-menu), its shellbg<N>.webp (or .png) and shellbg.json's affine. Pages without
rules (the grey pads, the pictures) are left alone. The first run copies each file it changes into PACK/.rules-prev/ (a
sub-folder the game does not look in); --restore puts those back. --out writes the results elsewhere instead (a review), the
pack untouched. pack.json gets a "rules" entry naming the pages done. Needs Python 3.10+, numpy, scipy, Pillow.
"""
import argparse, glob, json, os, re, sys, time
import numpy as np
from PIL import Image
from scipy import ndimage

# ink: a pixel darker than this (0.299 R + 0.587 G + 0.114 B) is ink on any of the forms' papers (ochre about 190, grey 215)
INK_LUM = 110
# a rule: a run of ink at least this long (the steppers' boxes are 10); a figure of such runs is a rule when it is long or a box
MIN_RUN = 8
# a rule's run of lines reaches at least this far one way (a table, a frame, an underline, a button's edge); the lettering's
# strokes, even the titles', are shorter, and a small closed box (a check box, a stepper's [-] or [+]) is kept by its shape
LONG = 32
BOX = 14
# the band round a rule the model discolours, in the original's pixels, either side
BAND = 2


def lum(a):
    return a[..., 0] * 0.299 + a[..., 1] * 0.587 + a[..., 2] * 0.114


def runs(mask, axis, n):
    """The mask's pixels that lie in a run of n or more along the axis (0 down, 1 across)."""
    m = mask if axis == 1 else mask.T
    out = np.zeros_like(m)
    for y in range(m.shape[0]):
        row = m[y]
        edges = np.flatnonzero(np.diff(np.concatenate(([0], row.astype(np.int8), [0]))))
        for x0, x1 in zip(edges[::2], edges[1::2]):
            if x1 - x0 >= n:
                out[y, x0:x1] = True
    return out if axis == 1 else out.T


def rule_mask(orig):
    """The original's rule pixels: ink in long runs, less the lettering's lone strokes."""
    ink = lum(orig) < INK_LUM
    m = runs(ink, 1, MIN_RUN) | runs(ink, 0, MIN_RUN)
    labels, n = ndimage.label(m, structure=np.ones((3, 3), bool))
    if n:
        slices = ndimage.find_objects(labels)
        counts = np.bincount(labels.ravel())
        for i, s in enumerate(slices, 1):
            h, w = s[0].stop - s[0].start, s[1].stop - s[1].start
            # a box: square-ish, its ink all on its rim (a letter's bowl or crossbar runs through the middle)
            cell = labels[s] == i
            rim = np.ones_like(cell); rim[2:-2, 2:-2] = False
            sides = (cell[:2].any(0).mean(), cell[-2:].any(0).mean(), cell[:, :2].any(1).mean(), cell[:, -2:].any(1).mean())
            box = (8 <= h <= BOX and 8 <= w <= BOX and abs(h - w) <= 2 and (cell & rim).sum() >= 0.95 * counts[i]
                   and min(sides) >= 0.7)
            if max(h, w) < LONG and not box:
                m[labels == i] = False
    return ink, m


def affine_of(pack, rec):
    """The page's file and affine (image = a * frame + b) from shellbg.json, or the plain 4x."""
    path = None
    a = (4.0, 0.0, 4.0, 0.0)
    manifest = os.path.join(pack, "shellbg.json")
    if os.path.exists(manifest):
        with open(manifest, encoding="utf-8") as f:
            e = json.load(f).get(str(rec))
        if e:
            if e.get("file"):
                path = os.path.join(pack, e["file"])
            if e.get("affine") and len(e["affine"]) == 4:
                a = tuple(float(v) for v in e["affine"])
    if path is None or not os.path.exists(path):
        for stem in (f"shellbg{rec}", f"shellbg{rec:02d}"):
            for ext in (".webp", ".png", ".jpg"):
                p = os.path.join(pack, stem + ext)
                if os.path.exists(p):
                    return p, a
        return None, a
    return path, a


def clean(orig, page, a, mode):
    """The page with its rules' bands made again. orig: the record (H x W x 3), page: the pack's picture (h x w x 3)."""
    ax, bx, ay, by = a
    H, W = orig.shape[:2]
    h, w = page.shape[:2]
    # each page texel's original pixel (nearest)
    oy = np.clip(np.floor((np.arange(h) + 0.5 - by) / ay).astype(int), 0, H - 1)
    ox = np.clip(np.floor((np.arange(w) + 0.5 - bx) / ax).astype(int), 0, W - 1)
    ink, rules = rule_mask(orig)
    if not rules.any():
        return None, 0
    k = max(1, int(round(min(ax, ay))))
    core = rules[oy][:, ox]
    letters = ndimage.binary_dilation(ink & ~rules, iterations=1)[oy][:, ox]          # the lettering and a pixel round it
    band = ndimage.binary_dilation(core, iterations=BAND * k) & ~core & ~letters
    # the paper's tone as the pack renders it: the pack less the original, averaged over what is neither rule nor band
    o_up = orig[oy][:, ox].astype(np.float32)
    p = page.astype(np.float32)
    keep = ~(core | band)
    sigma = 4.0 * k
    num = ndimage.gaussian_filter((p - o_up) * keep[..., None], sigma=(sigma, sigma, 0))
    den = ndimage.gaussian_filter(keep.astype(np.float32), sigma=sigma)[..., None]
    delta = num / np.maximum(den, 1e-3)
    paper = np.clip(o_up + delta, 0, 255)
    # the rule's own colour, the original's at the nearest rule pixel (the paper's tone shift applied too)
    idx = ndimage.distance_transform_edt(~core, return_distances=False, return_indices=True)
    ink_c = np.clip(o_up[idx[0], idx[1]] + delta, 0, 255)
    out = p.copy()
    if mode == "redraw":
        out[core] = ink_c[core]
        out[band] = paper[band]
        # the rule's edge softened a texel, so the line sits in the picture as the model's did
        edge = ndimage.binary_dilation(core, iterations=1) & band
        out[edge] = 0.5 * paper[edge] + 0.5 * ink_c[edge]
    else:
        sel = core | band
        lp, li, l = lum(paper), lum(ink_c), lum(p)
        t = np.clip((l - lp) / np.minimum(li - lp, -1.0), 0, 1)[..., None]
        mix = paper * (1 - t) + ink_c * t
        out[sel] = mix[sel]
    return np.clip(np.rint(out), 0, 255).astype(np.uint8), int(rules.sum())


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", nargs="?", help="the folder of the records' originals, bg<NN>.ppm")
    ap.add_argument("--pack", default=None, help="the pack folder (default: the game's user://tex-menu)")
    ap.add_argument("--only", default=None, help="record numbers to do, comma-separated (default: every page with rules)")
    ap.add_argument("--out", default=None, help="write the results to this folder instead of the pack")
    ap.add_argument("--mode", default="chroma", choices=["chroma", "redraw"])
    ap.add_argument("--restore", action="store_true", help="put the files from PACK/.rules-prev back and quit")
    args = ap.parse_args()
    pack = args.pack or os.path.join(os.environ.get("APPDATA") or os.path.expanduser("~/.local/share"), "I26", "tex-menu")
    prev = os.path.join(pack, ".rules-prev")
    if args.restore:
        n = 0
        for f in glob.glob(os.path.join(prev, "shellbg*.*")):
            os.replace(f, os.path.join(pack, os.path.basename(f)))
            n += 1
        print(f"restored {n} files")
        return
    if not args.src:
        ap.error("SRC_DIR is needed")
    only = {int(v) for v in args.only.split(",")} if args.only else None
    done = []
    for f in sorted(glob.glob(os.path.join(args.src, "bg*.ppm"))):
        m = re.match(r"bg(\d+)\.ppm$", os.path.basename(f))
        if not m:
            continue
        rec = int(m.group(1))
        if only is not None and rec not in only:
            continue
        path, a = affine_of(pack, rec)
        if path is None:
            continue
        orig = np.asarray(Image.open(f).convert("RGB"))
        page = np.asarray(Image.open(path).convert("RGB"))
        t0 = time.time()
        out, n = clean(orig, page, a, args.mode)
        if out is None:
            print(f"{rec:2d}: no rules")
            continue
        if args.out:
            os.makedirs(args.out, exist_ok=True)
            dst = os.path.join(args.out, os.path.basename(path))
        else:
            os.makedirs(prev, exist_ok=True)
            keep = os.path.join(prev, os.path.basename(path))
            if not os.path.exists(keep):
                os.replace(path, keep)
            dst = path
        if dst.lower().endswith(".webp"):
            # lossless: lossy WebP keeps the colour at a quarter of the resolution, which is what greys the paper beside a
            # one-pixel rule and lends the rule the paper's orange (the band comes back at any quality, 95 or 100)
            Image.fromarray(out, "RGB").save(dst + ".part", "WEBP", lossless=True, quality=100, method=6)
            os.replace(dst + ".part", dst)
        else:
            Image.fromarray(out, "RGB").save(dst)
        done.append(rec)
        print(f"{rec:2d}: {n} rule pixels, {os.path.basename(dst)} in {time.time() - t0:.1f}s")
    pj = os.path.join(pack, "pack.json")
    if done and not args.out and os.path.exists(pj):
        with open(pj, encoding="utf-8") as fh:
            d = json.load(fh)
        d["rules"] = {"date": time.strftime("%Y-%m-%d"), "tool": f"tools/menu-rules.py --mode {args.mode}",
                      "pages": sorted(set(d.get("rules", {}).get("pages", [])) | set(done)),
                      "note": "the forms' ruled lines made again from the originals (the model's pale band along them gone); previous files in .rules-prev/"}
        with open(pj, "w", encoding="utf-8") as fh:
            json.dump(d, fh, indent=2)
            fh.write("\n")


if __name__ == "__main__":
    main()
