#!/usr/bin/env python3
"""Redraw the cockpit radar's frames (zradf000-030, zradb000-030) for an HD pack, CPU only.

    python tools/radar-redraw.py measure EXPORT_DIR [geometry.json]
    python tools/radar-redraw.py fit-labels EXPORT_DIR [fit.json]
    python tools/radar-redraw.py render  EXPORT_DIR UPSCALE_DIR OUT_DIR [--scale 8] [--style A|B] [--labels NAME]   (frames, zrge.png, ztge.png)
    python tools/radar-redraw.py pack    EXPORT_DIR PACK_DIR [--out DIR] [--base DIR] [--scale N] [--style A|B] [--labels NAME] [--glass none|light|medium]
    python tools/radar-redraw.py sheet   EXPORT_DIR UPSCALE_DIR OUT_DIR
    python tools/radar-redraw.py --self-check

EXPORT_DIR is the folder `i76 export-textures` wrote (the originals, 256x128, as PNGs); UPSCALE_DIR a pack's upscale of the
same frames (any whole multiple of 256 wide, PNG or .dds), whose bezel, knobs and wiring are kept. The defaults are the
packs' choice: style B and the label set lock-range.

`pack` is the HD recipes' step (tools/hd-recipes/world-4x.json, world-8x.json): it redraws a pack's own frames in place,
in the pack's format (.dds: BC7 with the full mip chain, rows stored as EXPORT_DIR/bottom-up.txt says), writes zrge and
ztge beside them (the two belong together), keeps the frames it started from in PACK_DIR/.radar-base/ (a second run starts
from those, so it gives the same files) and names the redraw and its font in pack.json and ATTRIBUTION.txt (FILES.sha256
made again when there is one). With --out DIR the pack is only read and the files go to DIR. A pack whose pack.json names a
redraw already and that has no .radar-base/ is refused: --base DIR gives the upscale's own frames (a backup) to start from.
Everything is measured from the
original's pixels each run (nothing of the game's art is in the repository), then drawn again as vectors:

- the scope: the 60 frames' per-pixel median is the clean grid and panel; the rings are circles fitted to its grid pixels, the
  spokes lines fitted through them, the face the circle fitted to the green's edge; the small scope at the corner likewise.
- the sweep (zradf): each frame's beam fitted as a line; all 30 meet at one pivot, frame f at 12f degrees plus a measured offset,
  clockwise as the game shows it. The lock cone (zradb): its two edges fitted, the half-angle and how far behind the pivot they meet.
  Style A draws them as the original does (a line, a cone, the original's colours); style B as a phosphor scope (a thin sharp
  beam with a bright core and a fading afterglow behind it, a cone with soft edges, the graticule printed crisp over them).
- the left panel's marks: the dotted arc round one knob, the ticks by the other, the knob pointers and the two small lettered
  rows, as dark blobs found in the median; they are erased from the upscale and drawn again as shapes, with a label set's words
  set in a free font (tools/radar-redraw/radar.json names it: B612, in tools/text-redraws/fonts/ with its licence), printed
  by darkening the panel's own shade.
- the edges: the scope's bezel ring, the wires (the cable and the red and orange twisted pair) and the body's bottom corners'
  outline, curves fitted to the original's pixels and drawn again from the original's own colours measured along them (see
  "the edges" below); the rest of the bezel, body and handles stay the upscale's.

- the stamps: the range plate's and the contact lamp's sheets (zrge, ztge), which the game stamps over every frame, are
  written beside the frames: the redrawn frame's texels inside a disc about the frame's own scope or lamp, keyed outside,
  the lit faces from profiles measured on the original sprites (EXPORT_DIR must hold zrge.png and ztge.png).

The game shows these frames with their rows flipped (ExtInstruments: the slot's y runs up; the scope's forward is +y in the
file), so the spec's label positions are in the game's view (y down from the top as the player sees it) and lettering is set
upright there. The game overlays blips, the LED and the range plate at fixed original pixels, so nothing here moves: the
scope's rings, pivot and face stay where the original has them, scaled exactly.
Needs Python 3.10+, numpy, scipy and Pillow.
"""
import argparse, json, math, os, sys, urllib.request
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = os.path.join(HERE, "radar-redraw", "radar.json")
REPO_FONTS = os.path.join(HERE, "text-redraws", "fonts")

# palette colours the art uses (measured; the tool checks they are present)
GREEN = (0, 69, 0)          # the scope's face
GRID = (41, 41, 20)         # rings, spokes, the panel's printed marks
PIP = (57, 115, 57)         # the centre pip
OLIVE = (66, 72, 32)        # the panel


def load_spec(path=SPEC):
    with open(path, encoding="utf-8") as f: return json.load(f)


# ------------------------------------------------------------------------------------------------------------- loading

def load_rgba(path):
    return np.asarray(Image.open(path).convert("RGBA"))


def load_frames(export_dir, spec):
    """{stem: rgba uint8} of the 62 frames (sweep and locked 0..29 and the radar-off 30 of each)."""
    fr = spec["frames"]; out = {}
    for kind in ("sweep", "locked"):
        for i in range(fr["count"] + 1):
            stem = fr[kind] % i
            out[stem] = load_rgba(os.path.join(export_dir, stem + ".png"))
    return out


def stems(spec, kind):
    fr = spec["frames"]; return [fr[kind] % i for i in range(fr["count"])]


def is_col(a, c):
    return (a[..., 0] == c[0]) & (a[..., 1] == c[1]) & (a[..., 2] == c[2])


def median_of(frames, spec):
    a = np.stack([frames[s] for s in stems(spec, "sweep") + stems(spec, "locked")]).astype(np.int16)
    return np.median(a, 0).astype(np.uint8)


# ------------------------------------------------------------------------------------------------------------- fitting
# coordinates: continuous, pixel (row i, col j) has its centre at (j + 0.5, i + 0.5), in the file's own rows (top down)

def fit_circle(X, Y):
    """Algebraic circle fit, then the rms of the radial residuals."""
    M = np.stack([X, Y, np.ones_like(X)], 1)
    p = np.linalg.lstsq(M, -(X ** 2 + Y ** 2), rcond=None)[0]
    cx, cy = -p[0] / 2, -p[1] / 2
    r = math.sqrt(max(cx * cx + cy * cy - p[2], 1e-9))
    res = np.hypot(X - cx, Y - cy) - r
    return (float(cx), float(cy), float(r)), float(np.sqrt((res ** 2).mean()))


def fit_rings(X, Y, c, bands):
    """Circles through the points in each radial band about c: a common centre (least squares over all bands, each its own
    radius), then each band's radius; returns centre, radii, per-ring rms."""
    sel = []
    for lo, hi in bands:
        r = np.hypot(X - c[0], Y - c[1]); sel.append((r > lo) & (r < hi))
    for _ in range(4):
        # unknowns: cx, cy, and k_i = r_i^2 - cx^2 - cy^2 per band:  x^2 + y^2 = 2 cx x + 2 cy y + k_i
        rows, rhs = [], []
        for bi, s in enumerate(sel):
            for x, y in zip(X[s], Y[s]):
                row = [2 * x, 2 * y] + [0.0] * len(sel); row[2 + bi] = 1.0
                rows.append(row); rhs.append(x * x + y * y)
        p = np.linalg.lstsq(np.array(rows), np.array(rhs), rcond=None)[0]
        c = (float(p[0]), float(p[1]))
        radii = [math.sqrt(p[2 + i] + c[0] ** 2 + c[1] ** 2) for i in range(len(sel))]
        # keep each band's points near its circle (drops strays)
        tol = min([1.5] + [abs(a - b) / 2 for a, b in zip(radii, radii[1:])])
        sel = [(np.abs(np.hypot(X - c[0], Y - c[1]) - R) < tol) for R in radii]
    rms = [float(np.sqrt(((np.hypot(X[s] - c[0], Y[s] - c[1]) - R) ** 2).mean())) for s, R in zip(sel, radii)]
    return c, [float(r) for r in radii], rms, [int(s.sum()) for s in sel]


def fit_line_tls(P, w=None):
    w = np.ones(len(P)) if w is None else w
    mu = (P * w[:, None]).sum(0) / w.sum()
    _, _, vt = np.linalg.svd((P - mu) * np.sqrt(w)[:, None], full_matrices=False)
    return mu, vt[0]


def fit_spokes(X, Y, c, angles, radii, near=1.6):
    """Lines through the grid's spoke pixels near each angle (degrees, in the file's axes), ring bands left out."""
    out = []
    r = np.hypot(X - c[0], Y - c[1])
    ring = np.zeros(len(X), bool)
    for R in radii: ring |= np.abs(r - R) < 1.6
    for a0 in angles:
        u = np.array([math.cos(math.radians(a0)), math.sin(math.radians(a0))]); mu = np.array(c, float)
        for _ in range(6):
            n = np.array([-u[1], u[0]]); d = (X - mu[0]) * n[0] + (Y - mu[1]) * n[1]
            s = (np.abs(d) < near) & (r > 3) & ~ring
            P = np.stack([X[s], Y[s]], 1); mu, u = fit_line_tls(P)
        n = np.array([-u[1], u[0]]); res = (P - mu) @ n; t = (P - mu) @ u
        ang = math.degrees(math.atan2(u[1], u[0])) % 180
        out.append({"angle": round(ang, 3), "through_centre": round(float((np.array(c) - mu) @ n), 3),
                    "rms": round(float(res.std()), 3), "pixels": int(s.sum()),
                    "px_per_length": round(float(s.sum() / max(t.max() - t.min(), 1)), 2),
                    "extent": [round(float(t.min()), 1), round(float(t.max()), 1)]})
    return out


SWEEP_SHADES = {(57, 115, 57): 0.2, (63, 143, 63): 0.4, (82, 156, 82): 0.6, (99, 189, 99): 0.8, (126, 243, 126): 1.0}


def measure(frames, spec, export_dir=None):
    """Everything the redraw needs, from the original's pixels (the stamps' too where export_dir holds their sheets)."""
    med = median_of(frames, spec)
    g = {"note": "file coordinates (top-down rows, pixel centres at +0.5); the game shows the rows flipped"}
    green = is_col(med, GREEN); grid = is_col(med, GRID)
    lab, n = ndimage.label(green | grid)
    sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
    main = lab == (int(np.argmax(sizes)) + 1)
    # the scope's face: a circle fitted to its outermost face pixels (the face's edge half a pixel further out)
    disk = ndimage.binary_fill_holes(main)
    edge = disk & ~ndimage.binary_erosion(disk)
    ys, xs = np.nonzero(edge)
    (fx, fy, fr), frms = fit_circle(xs + 0.5, ys + 0.5)
    g["face"] = {"centre": [round(fx, 3), round(fy, 3)], "radius": round(fr + 0.5, 3), "rms": round(frms, 3), "pixels": int(len(xs))}
    # the grid: rings and spokes
    ys, xs = np.nonzero(grid & main); X, Y = xs + 0.5, ys + 0.5
    c0 = (fx, fy)
    sc = spec["scope"]
    c, radii, rrms, rn = fit_rings(X, Y, c0, sc["ring_bands"])
    spokes = fit_spokes(X, Y, c, sc["spoke_angles"], radii)
    # the spokes' own crossing (least squares)
    A = []; b = []
    for s_ in spokes:
        a = math.radians(s_["angle"]); n_ = np.array([-math.sin(a), math.cos(a)])
        A.append(n_); b.append(n_ @ np.array(c) - s_["through_centre"])
    cs = np.linalg.lstsq(np.array(A), np.array(b), rcond=None)[0]
    g["grid"] = {"centre": [round(c[0], 3), round(c[1], 3)], "spoke_crossing": [round(float(cs[0]), 3), round(float(cs[1]), 3)],
                 "rings": [{"radius": round(R, 3), "rms": round(e, 3), "pixels": k} for R, e, k in zip(radii, rrms, rn)],
                 "spokes": spokes}
    pip = is_col(med, PIP) & disk
    ys, xs = np.nonzero(pip)
    g["pip"] = {"centre": [float(xs.mean() + 0.5), float(ys.mean() + 0.5)], "pixels": int(len(xs))} if len(xs) else None
    # the small scope at the corner: its own green, rings about its crosshair
    ins = spec["inset"]; x0, y0, x1, y1 = ins["search"]
    sub = np.zeros_like(main); sub[y0:y1, x0:x1] = True
    ig = is_col(med, GRID) & sub; ign = is_col(med, GREEN) & sub
    row = int(np.argmax(ig.sum(1))); col = int(np.argmax(ig.sum(0)))
    ic = (col + 0.5, row + 0.5)
    ys, xs = np.nonzero(ig); X, Y = xs + 0.5, ys + 0.5
    keep = (np.abs(Y - ic[1]) > 0.6) & (np.abs(X - ic[0]) > 0.6)
    icc, iradii, irms, irn = fit_rings(X[keep], Y[keep], ic, ins["ring_bands"])
    # its face: about the rings' centre, out to its green's outer edge (the median of the outermost green per direction)
    ys, xs = np.nonzero(ign); rg = np.hypot(xs + 0.5 - icc[0], ys + 0.5 - icc[1]); ag = np.degrees(np.arctan2(ys + 0.5 - icc[1], xs + 0.5 - icc[0]))
    outer = [rg[(np.abs(angdiff(ag, a_)) < 10) & (rg < ins["max_radius"])].max() for a_ in range(-180, 180, 15)
             if ((np.abs(angdiff(ag, a_)) < 10) & (rg < ins["max_radius"])).any()]
    ix, iy, ir = icc[0], icc[1], float(np.median(outer)); irr = float(np.std(outer))
    g["inset"] = {"cross": [ic[0], ic[1]], "centre": [round(icc[0], 3), round(icc[1], 3)],
                  "rings": [{"radius": round(R, 3), "rms": round(e, 3), "pixels": k} for R, e, k in zip(iradii, irms, irn)],
                  "face": {"centre": [round(ix, 3), round(iy, 3)], "radius": round(ir + 0.5, 3), "rms": round(irr, 3)},
                  "cross_extent": [int(np.nonzero(ig[row])[0].min()), int(np.nonzero(ig[row])[0].max()) + 1,
                                   int(np.nonzero(ig[:, col])[0].min()), int(np.nonzero(ig[:, col])[0].max()) + 1]}
    g["sweep"] = measure_sweep(frames, med, spec, c)
    g["cone"] = measure_cone(frames, med, spec, g["sweep"]["pivot"])
    g["marks"] = measure_marks(med, spec)
    g["stamps"] = measure_stamps(export_dir, med, g, spec)
    g["edges"] = measure_edges(med, spec, g)
    return g, med


def frame_angle(f, step=12.0):
    """The beam's direction in the file's axes for frame f before the measured offset: frame 0 points +y (the car's front
    once the game flips the rows), each frame 12 degrees on, clockwise in the game's view (anticlockwise in the file's)."""
    return 90.0 - step * f


def measure_sweep(frames, med, spec, c):
    lines = []
    for i, s in enumerate(stems(spec, "sweep")):
        fr = frames[s]
        w = np.zeros(fr.shape[:2])
        for col, v in SWEEP_SHADES.items(): w[is_col(fr, col)] = v
        w[is_col(med, PIP)] = 0
        w[np.abs(fr.astype(int) - med).sum(-1) == 0] = 0
        ys, xs = np.nonzero(w >= 0.6); P = np.stack([xs + 0.5, ys + 0.5], 1); ww = w[ys, xs]
        k = np.hypot(P[:, 0] - c[0], P[:, 1] - c[1]) > 5
        mu, u = fit_line_tls(P[k], ww[k])
        for _ in range(3):
            if (mu - np.array(c)) @ u < 0: u = -u
            n = np.array([-u[1], u[0]]); k2 = k & (np.abs((P - mu) @ n) < 2.0)
            mu, u = fit_line_tls(P[k2], ww[k2])
        if (mu - np.array(c)) @ u < 0: u = -u
        lines.append((mu, u))
    A = np.array([[-u[1], u[0]] for _, u in lines]); b = np.array([np.array([-u[1], u[0]]) @ mu for mu, u in lines])
    pv = np.linalg.lstsq(A, b, rcond=None)[0]
    dist = A @ pv - b
    dev = []
    for i, (mu, u) in enumerate(lines):
        a = math.degrees(math.atan2(mu[1] - pv[1], mu[0] - pv[0]))
        dev.append(((a - frame_angle(i)) + 180) % 360 - 180)
    # the beam's cross profile: mean brightness against the signed distance from its line (positive: behind the beam)
    off = float(np.mean(dev)); prof = {}
    for i, s in enumerate(stems(spec, "sweep")):
        fr = frames[s]; d = np.abs(fr.astype(int) - med).sum(-1) > 0
        a = math.radians(frame_angle(i) + off); u = np.array([math.cos(a), math.sin(a)]); n = np.array([-u[1], u[0]])
        ys, xs = np.nonzero(d); P = np.stack([xs + 0.5, ys + 0.5], 1) - pv; t = P @ u; sd = P @ n
        for k_ in np.nonzero((t > 6) & (t < 50))[0]:
            prof.setdefault(round(sd[k_] * 2) / 2, []).append(fr[ys[k_], xs[k_], :3].mean())
    profile = {str(k): round(float(np.mean(v)), 1) for k, v in sorted(prof.items()) if len(v) > 40}
    lum_bg = float(np.mean(GREEN)); pk = max(profile.values())
    above = [float(k) for k, v in profile.items() if v - lum_bg >= (pk - lum_bg) / 2]
    return {"pivot": [round(float(pv[0]), 3), round(float(pv[1]), 3)], "line_rms": round(float(np.sqrt((dist ** 2).mean())), 3),
            "offset_deg": round(off, 3), "offset_sd": round(float(np.std(dev)), 3),
            "offset_range": [round(min(dev), 3), round(max(dev), 3)], "per_frame": [round(x, 3) for x in dev],
            "profile": profile, "fwhm": round(max(above) - min(above) + 0.5, 2)}


def measure_cone(frames, med, spec, pv):
    pv = np.array(pv); halves = []; backs = []; skews = []
    for i, s in enumerate(stems(spec, "locked")):
        fr = frames[s]
        d = (np.abs(fr.astype(int) - med).sum(-1) > 0) & ~is_col(fr, OLIVE) & ~is_col(med, PIP)
        ys, xs = np.nonzero(d); P = np.stack([xs + 0.5, ys + 0.5], 1) - pv
        a = math.radians(frame_angle(i)); u = np.array([math.cos(a), math.sin(a)]); n = np.array([-u[1], u[0]])
        t = P @ u; sd = P @ n; sides = []
        for sg in (1, -1):
            pts = []
            for t0 in np.arange(8, 50, 1.0):
                sel = (np.abs(t - t0) < 0.51) & (sd * sg > -0.5)
                if sel.any(): pts.append((t0, (sd[sel] * sg).max() + 0.5))
            pts = np.array(pts); sides.append(np.polyfit(pts[:, 0], pts[:, 1], 1))
        (a1, b1), (a2, b2) = sides
        hw = [math.degrees(math.atan(a1)), math.degrees(math.atan(a2))]
        halves.append(sum(hw) / 2); skews.append((hw[0] - hw[1]) / 2); backs.append((b1 + b2) / (a1 + a2))
    lum = []
    for s in stems(spec, "locked"):
        fr = frames[s]; d = (np.abs(fr.astype(int) - med).sum(-1) > 0)
        lum += [tuple(p) for p in fr[d][:, :3]]
    from collections import Counter
    return {"half_angle": round(float(np.mean(halves)), 3), "half_angle_sd": round(float(np.std(halves)), 3),
            "apex_behind_pivot": round(float(np.mean(backs)), 2), "apex_sd": round(float(np.std(backs)), 2),
            "skew_deg": round(float(np.mean(skews)), 3),
            "colours": [[list(map(int, k)), v] for k, v in Counter(lum).most_common(4)]}


def to_game_rows(a):
    return a[::-1]


def measure_marks(med, spec):
    """The left panel's printed marks (small dark blobs) in the game's view (rows flipped), grouped by the spec's boxes;
    and the knobs (their faces' circles and pointers)."""
    mk = spec["marks"]; gm = to_game_rows(med).astype(int)
    lum = gm[..., :3].mean(-1)
    x0, y0, x1, y1 = mk["region"]
    reg = np.zeros(lum.shape, bool); reg[y0:y1, x0:x1] = True
    dark = reg & (lum < mk["dark_below"]) & ~is_col(gm, OLIVE)
    lab, n = ndimage.label(dark, structure=np.ones((3, 3)))
    blobs = []
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if len(xs) > mk["max_blob"]: continue
        blobs.append({"pixels": [[int(x), int(y)] for x, y in zip(xs, ys)], "centre": [float(xs.mean() + 0.5), float(ys.mean() + 0.5)],
                      "box": [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1],
                      "lum": float(lum[ys, xs].min())})
    groups = {}
    for name, box in mk["groups"].items():
        bx0, by0, bx1, by1 = box
        inside = [b for b in blobs if bx0 <= b["centre"][0] < bx1 and by0 <= b["centre"][1] < by1]
        if not inside: groups[name] = {"blobs": [], "ink_box": None}; continue
        ib = [min(b["box"][0] for b in inside), min(b["box"][1] for b in inside), max(b["box"][2] for b in inside), max(b["box"][3] for b in inside)]
        cols = sorted({p[0] for b in inside for p in b["pixels"]})
        runs = []
        for x in cols:
            if runs and x == runs[-1][1]: runs[-1][1] = x + 1
            else: runs.append([x, x + 1])
        groups[name] = {"blobs": inside, "ink_box": ib, "column_runs": runs}
    knobs = []
    for kb in mk["knobs"]:
        bx0, by0, bx1, by1 = kb
        # the knob's whole disc (its light face and black rim), its outline fitted; the face is the rim's width inside
        sub = lum[by0:by1, bx0:bx1]
        knob = np.zeros(lum.shape, bool); knob[by0:by1, bx0:bx1] = (sub > mk["face_above"]) | (sub < mk["rim_below"])
        lab2, n2 = ndimage.label(knob); big = np.argmax(ndimage.sum(knob, lab2, range(1, n2 + 1))) + 1
        knob = ndimage.binary_fill_holes(lab2 == big)
        ys, xs = np.nonzero(knob & ~ndimage.binary_erosion(knob))
        (cx, cy, r), rms = fit_circle(xs + 0.5, ys + 0.5)
        r = r + 0.5 - mk["rim_width"]
        yy, xx = np.mgrid[0:lum.shape[0], 0:lum.shape[1]] + 0.5
        inface = np.hypot(xx - cx, yy - cy) < r - 0.5
        ys, xs = np.nonzero(inface & (lum < mk["pointer_below"]))
        P = np.stack([xs + 0.5, ys + 0.5], 1)
        mu, u = fit_line_tls(P)
        if (mu - np.array([cx, cy])) @ u < 0: u = -u
        t = (P - np.array([cx, cy])) @ u
        knobs.append({"centre": [round(cx, 3), round(cy, 3)], "radius": round(r, 3), "rms": round(rms, 3),
                      "pointer": {"angle": round(math.degrees(math.atan2(u[1], u[0])), 2), "from": round(float(t.min()), 2),
                                  "to": round(float(t.max()) + 0.5, 2), "pixels": [[int(x), int(y)] for x, y in zip(xs, ys)],
                                  "offset": round(float((mu - np.array([cx, cy])) @ np.array([-u[1], u[0]])), 2)}})
    # the dotted arc: a circle through its dots
    arc = groups.get(mk["arc_group"])
    arcfit = None
    if arc and arc["blobs"]:
        C = np.array([b["centre"] for b in arc["blobs"]])
        (ax, ay, ar), arms = fit_circle(C[:, 0], C[:, 1])
        angs = sorted(math.degrees(math.atan2(p[1] - ay, p[0] - ax)) for p in C)
        arcfit = {"centre": [round(ax, 3), round(ay, 3)], "radius": round(ar, 3), "rms": round(arms, 3),
                  "angles": [round(a, 2) for a in angs], "sizes": [len(b["pixels"]) for b in sorted(arc["blobs"], key=lambda b: math.atan2(b["centre"][1] - ay, b["centre"][0] - ax))]}
    return {"groups": groups, "knobs": knobs, "arc": arcfit,
            "panel_colour": [int(v) for v in np.median(gm[reg & is_col(gm, OLIVE)][:, :3], 0)]}


# ------------------------------------------------------------------------------------------------------------- drawing
# every shape is a coverage (0..1) on the target's texel grid; a texel's centre at ((j + 0.5) / S, (i + 0.5) / S) original px

class Canvas:
    def __init__(self, w, h, s, box=None):
        """A window of the S-times picture: box (x0, y0, x1, y1) in original pixels (the whole picture by default)."""
        self.s = s
        x0, y0, x1, y1 = box or (0, 0, w, h)
        self.box = (x0, y0, x1, y1)
        self.X = (np.arange(int(x0 * s), int(x1 * s)) + 0.5)[None, :] / s
        self.Y = (np.arange(int(y0 * s), int(y1 * s)) + 0.5)[:, None] / s

    def aa(self, d, edge=None):
        """Coverage from a signed distance (original px, negative inside): one texel of soft edge, or a ramp edge original
        px wide centred on the boundary (the line's area kept: its outermost texels partly covered)."""
        if edge: return np.clip(0.5 - d / max(edge, 1.0 / self.s), 0, 1)
        return np.clip(0.5 - d * self.s, 0, 1)

    def disc(self, c, r, edge=None):
        return self.aa(np.hypot(self.X - c[0], self.Y - c[1]) - r, edge)

    def ring(self, c, r, w, edge=None):
        return self.aa(np.abs(np.hypot(self.X - c[0], self.Y - c[1]) - r) - w / 2, edge)

    def seg(self, p0, p1, w, cap=True, edge=None):
        p0 = np.array(p0, float); p1 = np.array(p1, float); d = p1 - p0; L = float(np.hypot(*d)); u = d / max(L, 1e-9)
        t = (self.X - p0[0]) * u[0] + (self.Y - p0[1]) * u[1]
        n = -(self.X - p0[0]) * u[1] + (self.Y - p0[1]) * u[0]
        if cap:
            tc = np.clip(t, 0, L); dist = np.hypot(t - tc, n)
        else:
            dist = np.maximum(np.abs(n), np.maximum(-t, t - L) + w / 2)
        return self.aa(dist - w / 2, edge)

    def polar(self, c):
        return np.hypot(self.X - c[0], self.Y - c[1]), np.degrees(np.arctan2(self.Y - c[1], self.X - c[0]))


def over(img, cov, colour, alpha=1.0):
    """img (h, w, 3 float) with colour laid over at coverage cov."""
    a = (np.clip(cov, 0, 1) * alpha)[..., None]
    img *= 1 - a; img += a * np.asarray(colour, float)
    return img


def angdiff(a, b):
    return (a - b + 180) % 360 - 180


def draw_graticule(paint, g, spec, cv):
    """The scope's rings and spokes as coverages, each handed to paint(cov, colour)."""
    sc = spec["scope"]; gc = g["grid"]["centre"]; fr = g["face"]["radius"]
    lw = sc["line_width"]; le = sc.get("line_edge")
    for R in g["grid"]["rings"]:
        paint(cv.ring(gc, R["radius"], lw, le), GRID)
    for s_ in g["grid"]["spokes"]:
        a = math.radians(s_["angle"]); u = np.array([math.cos(a), math.sin(a)])
        L = fr + 2
        paint(cv.seg(np.array(gc) - u * L, np.array(gc) + u * L, sc.get("spoke_widths", {}).get(str(int(round(s_["angle"]))), lw), cap=False, edge=le), GRID)


def draw_pip(paint, g, spec, cv, on=True):
    """The centre pip (radar on) or the radar-off frames' dark 4x4 hub."""
    if not g.get("pip"): return
    sc = spec["scope"]; pc = g["pip"]["centre"]
    if on:
        paint(cv.disc(pc, sc["pip_ring"]), OLIVE)
        paint(cv.disc(pc, sc["pip_radius"]), PIP)
    else:
        paint(cv.disc(pc, sc["hub_radius"]), GRID)


def scope_face(g, spec, cv, style, frame=None, locked=False, parts="all"):
    """The scope's face (original-pixel window cv) as colour and its coverage: face, grid, pip, beam or cone.
    parts "face": the face's colour alone (the live sweep's face layer: no graticule, pip, beam or cone)."""
    st = spec["styles"][style]
    fc = g["face"]["centre"]; fr = g["face"]["radius"]
    h, w = cv.Y.shape[0], cv.X.shape[1]
    img = np.empty((h, w, 3)); img[:] = st.get("face", GREEN)
    inside = cv.disc(fc, fr)
    if parts == "face": return img, inside
    sw = g["sweep"]
    a0 = None if frame is None else frame_angle(frame) + (0 if locked else sw["offset_deg"])
    over_all = st.get("grid_over", False)
    under = st.get("glow_under_grid", False)
    paint = lambda cov, colour: over(img, cov, colour)
    if over_all:
        # style "grid_over": the afterglow, the beam and the cone first, the graticule printed crisp over all of them
        if a0 is not None:
            if locked: cone(img, cv, g, st, a0)
            else: beam(img, cv, g, st, a0)
        draw_graticule(paint, g, spec, cv)
    else:
        # the afterglow lies under the graticule ("glow_under_grid"); the beam's line and the lock cone over it, as the original's
        if a0 is not None and under and not locked:
            beam(img, cv, g, st, a0, part="trail")
        draw_graticule(paint, g, spec, cv)
    draw_pip(paint, g, spec, cv, on=frame is not None)
    if a0 is not None and not over_all:
        if locked: cone(img, cv, g, st, a0)
        if not locked: beam(img, cv, g, st, a0, part="line" if under else "all")
    draw_glass(paint, g, spec, cv)
    return img, inside


def scope_grid(g, spec, cv):
    """The live sweep's grid layer over the scope's window: the graticule, the pip and the glass as one layer (straight RGB, alpha),
    clipped to the face's circle, which laid over a face drawn with the beam or cone gives the frame style B draws."""
    h, w = cv.Y.shape[0], cv.X.shape[1]
    C = np.zeros((h, w, 3)); A = np.zeros((h, w))
    def paint(cov, colour):
        c = np.clip(cov, 0, 1)
        C[:] = C * (1 - c[..., None]) + c[..., None] * np.asarray(colour, float); A[:] = A * (1 - c) + c
    draw_graticule(paint, g, spec, cv)
    draw_pip(paint, g, spec, cv, on=True)
    draw_glass(paint, g, spec, cv)
    inside = cv.disc(g["face"]["centre"], g["face"]["radius"])
    rgb = np.where(A[..., None] > 1e-6, C / np.maximum(A, 1e-6)[..., None], 0.0)
    return rgb, A * inside


# ------------------------------------------------------------------------------------------------------------- the glass
# The scope's glass (spec "glass", --glass LEVEL): a fixed soft sheen near the rim at the top left as the player sees it, a slight
# darkening toward the bezel, and a little second-hand wear: fine scratches catching the light, a smudge or two, dust at the rim.
# Every part is a coverage in original pixels from the spec's seed (the same at any scale, the same in every frame), painted over
# the beam, the cone, the graticule and the pip; the game's blips, lamp and plate lie over it. In the live sweep it is part of the
# grid layer (one layer over the face), so the stepped frames and the smooth sweep carry the same glass.

def glass_level(spec):
    """The glass's level as chosen (spec glass.use, set by --glass), or None."""
    gl = spec.get("glass") or {}
    use = gl.get("use", gl.get("default", "none"))
    return None if use in (None, "none") else gl["levels"][use]


def screen_bearing(X, Y, c):
    """Degrees clockwise from the top of the screen about c (the frames' rows are shown flipped: the file's +y is up)."""
    return np.degrees(np.arctan2(X - c[0], Y - c[1])) % 360


def value_noise(rng, box, cell):
    """A smooth noise field over box (original px), cells of `cell` px: (sample(X, Y) -> 0..1)."""
    x0, y0, x1, y1 = box
    nx = int(math.ceil((x1 - x0) / cell)) + 4; ny = int(math.ceil((y1 - y0) / cell)) + 4
    grid = rng.random((ny, nx))
    def sample(X, Y):
        Xb, Yb = np.broadcast_arrays(X, Y)
        return ndimage.map_coordinates(grid, [(Yb - y0) / cell + 1.5, (Xb - x0) / cell + 1.5], order=3, mode="nearest").clip(0, 1)
    return sample


def draw_glass(paint, g, spec, cv):
    """The glass's parts as coverages handed to paint(cov, colour), in order: darkening, sheen, smudges, scratches, dust."""
    lv = glass_level(spec)
    if lv is None: return
    gl = spec["glass"]; fc = np.array(g["face"]["centre"]); fr = g["face"]["radius"]
    import zlib
    rng = np.random.default_rng([int(gl["seed"]), zlib.crc32(b"zrad-glass")])   # seeded as the panels' wear is: the seed and the unit's name
    X, Y = np.broadcast_arrays(cv.X, cv.Y)
    r = np.hypot(X - fc[0], Y - fc[1]) / fr
    def ss(a, b, x): t = np.clip((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t)
    # the bezel's shadow on the glass
    paint(lv["vignette"] * ss(gl["vignette_from"], 1.0, r) ** 1.5, gl.get("vignette_colour", (0, 0, 0)))
    # the sheen: a soft crescent inside the rim at the top left (a fixed light), and a faint broad glare inside it
    sh = gl["sheen"]
    d = (screen_bearing(X, Y, fc) - sh["bearing"] + 180) % 360 - 180
    crescent = np.exp(-0.5 * (d / sh["spread_deg"]) ** 2) * ss(sh["inner"], sh["inner"] + 0.18, r) * (1 - ss(sh["outer"] - 0.07, sh["outer"], r))
    b = math.radians(sh["bearing"]); gc_ = fc + np.array([math.sin(b), math.cos(b)]) * sh["glare_at"] * fr
    glare = np.exp(-0.5 * (np.hypot(X - gc_[0], Y - gc_[1]) / (sh["glare_sigma"] * fr)) ** 2)
    paint(lv["sheen"] * np.maximum(crescent, sh["glare_share"] * glare), gl["sheen_colour"])
    # every random draw is made whatever the level (the levels share one layout and add parts, so light lies inside medium)
    box = (fc[0] - fr - 1, fc[1] - fr - 1, fc[0] + fr + 1, fc[1] + fr + 1)
    noise = value_noise(rng, box, 1.3)
    sm = []
    for i in range(gl["max_smudges"]):
        a = rng.uniform(0, 2 * math.pi); rr = math.sqrt(rng.uniform(0.05, 0.45)) * fr
        sm.append((fc + np.array([math.cos(a), math.sin(a)]) * rr, rng.uniform(4.0, 7.5), rng.uniform(0.6, 1.0), rng.uniform(0.5, 0.9)))
    sc = []
    for i in range(gl["max_scratches"]):
        a = rng.uniform(0, 2 * math.pi); rr = math.sqrt(rng.uniform(0, 0.8)) * fr
        p = fc + np.array([math.cos(a), math.sin(a)]) * rr
        th = rng.uniform(0, math.pi); L = rng.uniform(3, 15); bend = rng.uniform(-0.15, 0.15) * L
        w = rng.uniform(0.12, 0.22); al = rng.uniform(0, 1)
        sc.append((p, th, L, bend, w, al))
    du = []
    for i in range(gl["max_dust"]):
        a = rng.uniform(0, 2 * math.pi); rim = rng.random() < 0.8
        rr = (1 - rng.uniform(0, 0.17) ** 1.5 if rim else math.sqrt(rng.uniform(0, 0.8))) * fr
        du.append((fc + np.array([math.cos(a), math.sin(a)]) * rr, rng.uniform(0.12, 0.33), rng.uniform(0, 1)))
    # smudges: a soft haze, its density uneven (the noise), as a thumb leaves on glass
    for c, rad, k, aspect in sm[:lv["smudges"]]:
        q = np.hypot((X - c[0]) / aspect, Y - c[1]) / rad
        paint(lv["smudge_alpha"] * k * np.exp(-2.5 * q * q) * (0.35 + 0.65 * noise(X, Y)), gl["smudge_colour"])
    # scratches: thin bent lines catching the light, faint at their ends
    lo, hi = lv["scratch_alpha"]
    for p, th, L, bend, w, al in sc[:lv["scratches"]]:
        u = np.array([math.cos(th), math.sin(th)]); n = np.array([-u[1], u[0]])
        pts = [p + u * (t - 0.5) * L + n * bend * (1 - (2 * t - 1) ** 2) for t in np.linspace(0, 1, 7)]
        cov = np.zeros(X.shape)
        for k in range(6):
            fade = math.sin(math.pi * (k + 0.5) / 6) ** 0.7
            cov = np.maximum(cov, cv.seg(pts[k], pts[k + 1], w) * fade)
        paint(cov * (lo + (hi - lo) * al), gl["scratch_colour"])
    # dust: specks, most of them settled at the rim
    lo, hi = lv["dust_alpha"]
    for c, rad, al in du[:lv["dust"]]:
        paint(cv.disc(c, rad) * (lo + (hi - lo) * al), gl["dust_colour"])


def beam(img, cv, g, st, a0, part="all"):
    pv = g["sweep"]["pivot"]; fr = g["face"]["radius"]
    r, ang = cv.polar(pv)
    b = st["beam"]
    if b.get("trail_deg") and part in ("all", "trail"):
        # afterglow behind the beam: behind is the side the beam has left, larger angles in the file's axes
        behind = angdiff(ang, a0)
        k = (behind > 0) & (behind < b["trail_deg"])
        inten = np.where(k, b["trail_peak"] * np.exp(-np.clip(behind, 0, None) / b["trail_decay_deg"]), 0.0)
        inten *= np.clip((r - 1.0) * 0.5, 0, 1)
        over(img, inten, b["trail_colour"])
    if part == "trail": return
    u = np.array([math.cos(math.radians(a0)), math.sin(math.radians(a0))])
    end = np.array(pv) + u * (fr + 1)
    if b.get("glow_width"):
        over(img, cv.seg(pv, end, b["glow_width"]), b["glow_colour"], b.get("glow_alpha", 0.5))
    over(img, cv.seg(pv, end, b["width"]), b["edge_colour"])
    over(img, cv.seg(pv, end, b["core_width"]), b["core_colour"])


def cone(img, cv, g, st, a0):
    pv = np.array(g["sweep"]["pivot"]); cn = g["cone"]; c = st["cone"]
    half = cn["half_angle"]; back = cn["apex_behind_pivot"]
    a = math.radians(a0 + cn.get("skew_deg", 0) * 0)
    u = np.array([math.cos(a), math.sin(a)]); n = np.array([-u[1], u[0]])
    X = cv.X - pv[0]; Y = cv.Y - pv[1]
    t = X * u[0] + Y * u[1]; s = X * n[0] + Y * n[1]
    th = math.radians(half)
    # distance outside each edge (the edges meet "back" px behind the pivot)
    d_edge = np.abs(s) * math.cos(th) - (t + back) * math.sin(th)
    d = np.maximum(d_edge, -t)                       # starts at the pivot
    soft = c.get("soft", 0)
    if soft:
        cov = np.clip(0.5 - d / soft, 0, 1); cov = cov * cov * (3 - 2 * cov)
    else:
        cov = cv.aa(d)
    if c.get("rim_colour"):
        over(img, cov, c["rim_colour"])
        inner = cv.aa(d + c["rim"]) if not soft else np.clip(0.5 - (d + c["rim"]) / soft, 0, 1)
        over(img, inner, c["fill"])
    else:
        over(img, cov, c["fill"])
    if c.get("axis_colour"):
        rel = np.clip(1 - np.abs(s) / np.maximum((t + back) * math.tan(th), 1e-3), 0, 1) * (t > 0)
        over(img, rel ** 2, c["axis_colour"], c.get("axis_alpha", 0.5))


# ------------------------------------------------------------------------------------------------------------- the live sweep
# The game's Radar Sweep: Smooth draws the scope each frame from three files beside the 62 frames instead of swapping them:
# zrads_face (the frame with nothing on the scope's face but its green), zrads_grid (the graticule and the pip over the scope's
# window) and zrads.json (the numbers below). Its shader draws, over the face's circle, the afterglow and the beam at a
# continuous angle (or the lock cone at the target's true bearing), then the grid layer, then the game's blips, lamp and plate.
# compose_smooth is the same drawing in numpy (the reference the tests hold the stepped frames to). docs/hd-textures.md.

SWEEP_LAYERS = {"face": "zrads_face", "grid": "zrads_grid", "params": "zrads.json"}
SWEEP_FORMAT = 1


def sweep_params(g, spec, style, s, sbox):
    st = spec["styles"][style]; fr = spec["frames"]
    x0, y0, x1, y1 = sbox
    b = st["beam"]; c = st["cone"]
    beam_p = {k: b[k] for k in ("width", "core_width", "edge_colour", "core_colour") if k in b}
    for k in ("glow_width", "glow_colour", "glow_alpha"):
        if k in b: beam_p[k] = b[k]
    cone_p = {"half_angle": g["cone"]["half_angle"], "apex_behind_pivot": g["cone"]["apex_behind_pivot"]}
    cone_p.update({k: c[k] for k in ("fill", "soft", "axis_colour", "axis_alpha", "rim", "rim_colour") if k in c})
    step = 360.0 / fr["count"]
    return {
        "format": SWEEP_FORMAT,
        "what": "the live radar sweep's numbers (tools/radar-redraw.py, docs/hd-textures.md)",
        "style": style, "scale": s, "size": [256 * s, 128 * s],
        "layers": {"face": SWEEP_LAYERS["face"], "grid": SWEEP_LAYERS["grid"], "off": fr["sweep"] % fr["count"],
                   "grid_box": [int(x0 * s), int(y0 * s), int(x1 * s), int(y1 * s)]},
        "face": {"centre": g["face"]["centre"], "radius": g["face"]["radius"], "colour": list(st.get("face", GREEN))},
        "pivot": g["sweep"]["pivot"],
        "sweep": {"frames": fr["count"], "step_deg": step, "step_s": 0.2, "deg_per_s": step / 0.2,
                  "offset_deg": g["sweep"]["offset_deg"], "direction": "clockwise on screen"},
        "beam": dict(beam_p, length=g["face"]["radius"] + 1),
        "afterglow": {"span_deg": b.get("trail_deg", 0), "peak": b.get("trail_peak", 0), "decay_deg": b.get("trail_decay_deg", 1),
                      "decay_s": round(b.get("trail_decay_deg", 1) / (step / 0.2), 4), "colour": b.get("trail_colour", [0, 0, 0])},
        "cone": cone_p,
    }


def sweep_file_angle(p, phase):
    """The beam's direction in the file's axes at a continuous sweep phase (frame index; f + the share of the step gone)."""
    return frame_angle(phase, p["sweep"]["step_deg"]) + p["sweep"]["offset_deg"]


def compose_smooth(face, grid, p, angle, locked=False):
    """The live sweep drawn from the layers and the numbers alone (the game's shader, in numpy): the face, over its circle the
    afterglow and beam at file angle `angle` (or the lock cone along it), the grid layer over that. RGBA uint8, the frames' rows."""
    s = p["scale"]; gx0, gy0, gx1, gy1 = p["layers"]["grid_box"]
    cv = Canvas(256, 128, s, (gx0 / s, gy0 / s, gx1 / s, gy1 / s))
    G = np.asarray(p["face"]["colour"], float)
    gg = {"sweep": {"pivot": p["pivot"]}, "face": {"radius": p["face"]["radius"]}, "cone": p["cone"]}
    b = p["beam"]; ag = p["afterglow"]
    st = {"beam": dict({k: v for k, v in b.items() if k != "length"}, trail_deg=ag["span_deg"], trail_peak=ag["peak"],
                       trail_decay_deg=ag["decay_deg"], trail_colour=ag["colour"]), "cone": p["cone"]}
    img = np.empty((gy1 - gy0, gx1 - gx0, 3)); img[:] = G
    if locked: cone(img, cv, gg, st, angle)
    else: beam(img, cv, gg, st, angle)
    inside = cv.disc(p["face"]["centre"], p["face"]["radius"])
    out = face.copy(); reg = out[gy0:gy1, gx0:gx1]
    rgb = reg[..., :3].astype(float) + inside[..., None] * (img - G)
    ga = grid[..., 3:4].astype(float) / 255
    rgb = rgb * (1 - ga) + grid[..., :3].astype(float) * ga
    reg[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
    return out


def inset_face(g, spec, cv, style):
    st = spec["styles"][style]; ins = g["inset"]
    h, w = cv.Y.shape[0], cv.X.shape[1]
    img = np.empty((h, w, 3)); img[:] = st.get("face", GREEN)
    fc = ins["face"]["centre"]; fr = ins["face"]["radius"]
    inside = cv.disc(fc, fr)
    inset_grid(img, cv, g, spec, lambda r: np.broadcast_to(np.array(GRID, float), r.shape + (3,)))
    return img, inside


def inset_grid(img, cv, g, spec, colour_at):
    """The corner scope's rings and crosshair over img, each texel's line colour colour_at(radius) (the lit plate's lines
    follow its glow)."""
    ins = g["inset"]; lw = spec["inset"]["line_width"]; le = spec["inset"].get("line_edge")
    fr = ins["face"]["radius"]; cx, cy = ins["centre"]
    r = np.hypot(cv.X - cx, cv.Y - cy) + np.zeros((cv.Y.shape[0], 1)); col = colour_at(r)
    cov = np.zeros(r.shape)
    for R in ins["rings"]: cov = np.maximum(cov, cv.ring(ins["centre"], R["radius"], lw, le))
    cov = np.maximum(cov, cv.seg((cx - fr - 1, cy), (cx + fr + 1, cy), lw, cap=False, edge=le))
    cov = np.maximum(cov, cv.seg((cx, cy - fr - 1), (cx, cy + fr + 1), lw, cap=False, edge=le))
    a = cov[..., None]; img *= 1 - a; img += a * col
    return img


# ------------------------------------------------------------------------------------------------------------- the stamps
# The game stamps two sprite sheets over every frame at the layout's anchors (the zrad.map block of the cockpit layout):
# zrge.map's range plate (range_1 at 100 m, range_3 at 600 m; range_2 unused) over the corner scope, ztge.map's lamp
# (radled_1 red with a car listed, radled_2 green without) over the frame's lamp. Their pixels are copied whole (alpha keyed
# at one half), so a pack's upscale of the sheets on their own ends in a square whose edge does not continue the frame's
# upscale; and the original's plate art sits one pixel higher than the frame's own corner scope. Redrawn: the sheets take
# the redrawn frame's texels inside a disc about the frame's own scope (or lamp) and are keyed outside it, the lit faces
# drawn there from radial profiles measured on the original sprites; every frame gets the same texels inside the discs, so
# the stamp's edge is invisible whatever the state.

def load_sheet(export_dir, name):
    """A sprite sheet's export (the .map's rows as shown: i76 export-textures flips them) or None."""
    p = os.path.join(export_dir, name + ".png")
    return load_rgba(p) if os.path.exists(p) else None


def stamp_offset(sprite, frame_gv, anchor, reach=2):
    """Where the sprite's art lies against the frame's (game view): the (dx, dy) at which the sprite's pixels best equal the
    frame's from anchor + (dx, dy), and how many differ there."""
    h, w = sprite.shape[:2]; ax, ay = anchor; best = None
    for dy in range(-reach, reach + 1):
        for dx in range(-reach, reach + 1):
            fr = frame_gv[ay + dy:ay + dy + h, ax + dx:ax + dx + w]
            if fr.shape[:2] != (h, w): continue
            d = int((np.abs(sprite[..., :3].astype(int) - fr[..., :3]).sum(-1) > 0).sum())
            if best is None or d < best[0]: best = (d, dx, dy)
    return best[1], best[2], best[0]


def radial_profile(sprite, dark, centre, line_mask=None, step=1.0):
    """The lit sprite's colour against the radius from centre (sprite px), over the pixels where it differs from the dark
    one, the grid's line pixels apart: {"face": [[r, rgb]...], "line": [...], "extent": r}."""
    h, w = sprite.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w] + 0.5
    r = np.hypot(xx - centre[0], yy - centre[1])
    lit = np.abs(sprite[..., :3].astype(int) - dark[..., :3]).sum(-1) > 0
    lm = np.zeros((h, w), bool) if line_mask is None else line_mask(xx, yy, r)
    out = {}
    for name, sel in (("face", lit & ~lm), ("line", lit & lm)):
        prof = []
        for k in np.arange(0, r.max() + step, step):
            m = sel & (r >= k) & (r < k + step)
            if m.sum() >= 2: prof.append([round(float(k + step / 2), 2), [int(v) for v in np.median(sprite[m][:, :3], 0)]])
        out[name] = prof
    out["extent"] = round(float(np.percentile(r[lit], 97) + 0.5), 2) if lit.any() else 0.0
    return out


def measure_stamps(export_dir, med, g, spec):
    """The sprites' placement against the frame and the lit faces' radial profiles (None without the sheets' export)."""
    st = spec.get("stamps")
    if not st or export_dir is None: return None
    gv = to_game_rows(med); out = {}
    ins = g["inset"]
    for key, part in st["parts"].items():
        sh = load_sheet(export_dir, part["sheet"])
        if sh is None: return None
        w, h = part["size"]
        sprite = {n: sh[y:y + h, 0:w] for n, y in part["sprites"].items()}
        ax, ay = part["anchor"]
        for n in sprite:      # radled_2's last row lies past its sheet: the game leaves the frame's there
            if sprite[n].shape[0] < h: sprite[n] = np.concatenate([sprite[n], gv[ay + sprite[n].shape[0]:ay + h, ax:ax + w]], 0)
        dark = sprite[part["dark"]]
        dx, dy, nd = stamp_offset(dark, gv, part["anchor"])
        # the frame's own centre (game view): the corner scope's rings, or the lamp's green disc (its centroid)
        if key == "plate":
            c = (ins["centre"][0], 128 - ins["centre"][1]); face_r = ins["face"]["radius"]
        else:
            x0, y0, x1, y1 = part["search"]; sub = gv[y0:y1, x0:x1].astype(int)
            m = sub[..., 1] > sub[..., 0] + 30
            ys, xs = np.nonzero(m); c = (float(xs.mean() + 0.5 + x0), float(ys.mean() + 0.5 + y0))
            face_r = math.sqrt(m.sum() / math.pi)
        sc = (c[0] - part["anchor"][0] - dx, c[1] - part["anchor"][1] - dy)

        def lines(xx, yy, r, sc=sc, key=key):
            if key != "plate": return np.zeros(r.shape, bool)
            m = (np.abs(xx - sc[0]) < 0.75) | (np.abs(yy - sc[1]) < 0.75)
            for R in ins["rings"]: m |= np.abs(r - R["radius"]) < 0.75
            return m
        lit = {}
        for n, a in sprite.items():
            if n == part["dark"]: continue
            # each lit sprite about its own centre: the middle of its coloured pixels that differ from the dark one (the
            # original's lit lamp sits where the frame's does, its green one and both plates a pixel higher)
            ai = a[..., :3].astype(int)
            col = (np.abs(ai - dark[..., :3]).sum(-1) > 0) & (ai.max(-1) - ai.min(-1) > 40)
            ys, xs = np.nonzero(col)
            lc = ((xs.min() + xs.max() + 1) / 2, (ys.min() + ys.max() + 1) / 2) if len(xs) else sc
            lit[n] = radial_profile(a, dark, lc, lines if key != "plate" else (lambda xx, yy, r, lc=lc: lines(xx, yy, r, lc)))
            lit[n]["sprite_centre"] = [float(lc[0]), float(lc[1])]
        out[key] = {"frame_centre": [round(c[0], 3), round(c[1], 3)], "face_radius": round(face_r, 3), "art_offset": [dx, dy],
                    "differing_at_offset": nd, "sprite_centre": [round(sc[0], 3), round(sc[1], 3)], "lit": lit}
    return out


def interp_rgb(prof, r):
    """A radial profile's colour at radii r (h, w): piecewise linear between its bins, held at the ends."""
    xs = np.array([p[0] for p in prof]); cs = np.array([p[1] for p in prof], float)
    return np.stack([np.interp(r, xs, cs[:, k]) for k in range(3)], -1)


def stamp_centre_file(ms, key):
    """A stamp's disc centre in the file's rows."""
    c = ms[key]["frame_centre"]; return (c[0], 128 - c[1])


def draw_sprites(rd, static_gv):
    """The two sheets (rows as shown, RGBA) at rd.s from the static frame's texels (game view): every sprite the frame's
    texels inside its disc, keyed outside; the lit ones with their face drawn from the measured profile."""
    spec, g, s, ms = rd.spec, rd.g, rd.s, rd.stamps
    out = {}
    for key, part in spec["stamps"]["parts"].items():
        m = ms[key]; w, h = part["size"]; ax, ay = part["anchor"]
        sw, sh_ = part["sheet_size"]
        sheet = np.zeros((sh_ * s, sw * s, 4), np.uint8)
        cv = Canvas(256, 128, s, (ax, ay, ax + w, ay + h))
        base = static_gv[ay * s:(ay + h) * s, ax * s:(ax + w) * s].astype(float)
        c = m["frame_centre"]
        r = np.hypot(cv.X - c[0], cv.Y - c[1]) + np.zeros((cv.Y.shape[0], 1))
        keep = (r < part["mask_radius"]).astype(np.uint8) * 255
        for name, y in part["sprites"].items():
            img = base[..., :3].copy()
            if name != part["dark"]:
                lit = m["lit"][name]
                R = min(lit["extent"], part["lit_max_radius"])
                cov = cv.disc(c, R, part["edge"])
                if lit["face"]: img = over(img, cov, interp_rgb(lit["face"], r))
                if key == "plate":
                    # the grid over the lit face in the lit lines' colours, the frame's dark lines outside the glow
                    lc = (interp_rgb(lit["line"], r) if lit["line"] else img * 0.8) * cov[..., None] + np.array(GRID, float) * (1 - cov[..., None])
                    gg = dict(g); gg["inset"] = dict(g["inset"], centre=list(c))
                    lined = inset_grid(img.copy(), cv, gg, spec, lambda rr, lc=lc: lc)
                    a = cv.disc(c, m["face_radius"])[..., None]; img = img * (1 - a) + lined * a
            rows = min(h * s, sheet.shape[0] - y * s)
            sheet[y * s:y * s + rows, :w * s, :3] = np.clip(np.round(img[:rows]), 0, 255).astype(np.uint8)
            sheet[y * s:y * s + rows, :w * s, 3] = keep[:rows]
        out[part["sheet"]] = sheet
    return out


# ------------------------------------------------------------------------------------------------------------- the panel

FONT_CACHE = os.environ.get("I76_RADAR_FONT_CACHE", os.path.join(os.path.expanduser("~"), ".cache", "i26-radar-fonts"))


def font_file(name, spec):
    """A face named in the spec's "fonts": downloaded once into the cache (its licence beside it), or one of
    tools/text-redraws/fonts' faces."""
    f = spec["fonts"][name]
    if "repo" in f: return os.path.join(REPO_FONTS, f["repo"])
    os.makedirs(FONT_CACHE, exist_ok=True)
    path = os.path.join(FONT_CACHE, os.path.basename(f["url"]))
    if not os.path.exists(path):
        print(f"fetching {name} ({f['license']}) from {f['url']}")
        urllib.request.urlretrieve(f["url"], path + ".part"); os.replace(path + ".part", path)
        if f.get("license_url"):
            try: urllib.request.urlretrieve(f["license_url"], os.path.join(FONT_CACHE, name + "-OFL.txt"))
            except OSError: pass
    return path


def text_coverage(cv, text, font_path, box, cap=None, tracking=0.0):
    """The text's coverage on cv, its ink fitted to box (x0, y0, x1, y1 original px, the game's view) by height (cap) and
    centred; squeezed to the box's width if wider."""
    ss = 4; s = cv.s
    x0, y0, x1, y1 = box; bh = (y1 - y0) if cap is None else cap
    px = 400
    f = ImageFont.truetype(font_path, px)
    # draw at a big size, letters spaced
    chars = list(text); widths = [f.getlength(ch) for ch in chars]; trk = tracking * px
    W = int(sum(widths) + trk * (len(chars) - 1) + px); H = int(px * 1.6)
    im = Image.new("L", (W, H), 0); d = ImageDraw.Draw(im); x = px * 0.5
    for ch, wd in zip(chars, widths):
        d.text((x, px * 0.2), ch, font=f, fill=255); x += wd + trk
    a = np.asarray(im, np.float32) / 255
    ys, xs = np.nonzero(a > 0.02)
    if not len(xs): return np.zeros((cv.Y.shape[0], cv.X.shape[1]))
    # the cap band: the font's H
    hb = f.getbbox("H", anchor="la")
    capt, capb = px * 0.2 + hb[1], px * 0.2 + hb[3]
    ix0, ix1 = xs.min(), xs.max() + 1
    scale = bh / (capb - capt)                      # original px per drawn px
    wpx = (ix1 - ix0) * scale
    sx = min(1.0, (x1 - x0) / wpx) if wpx > 0 else 1.0
    # target placement: cap band on [cy - bh/2, cy + bh/2], centred in x
    cx = (x0 + x1) / 2; cy = (y0 + y1) / 2
    # sample: for each target texel, its source position
    gx = (cv.X - cx) / (scale * sx) + (ix0 + ix1) / 2
    gy = (cv.Y - cy) / scale + (capt + capb) / 2
    # supersampled lookup (bilinear on a pre-blurred source to avoid aliasing)
    sigma = max(0.0, 0.5 / (scale * s) - 0.5)
    if sigma > 0.3: a = ndimage.gaussian_filter(a, sigma)
    cov = ndimage.map_coordinates(a, [np.broadcast_to(gy, (cv.Y.shape[0], cv.X.shape[1])).ravel(),
                                      np.broadcast_to(gx, (cv.Y.shape[0], cv.X.shape[1])).ravel()], order=1, cval=0.0)
    return cov.reshape(cv.Y.shape[0], cv.X.shape[1])


def inpaint(img, mask, known=None):
    """Fill the masked texels smoothly from the known texels round them (normalised convolution at falling scales: the
    widest pass fills everything, the narrower ones bring the nearby surface's shade back)."""
    out = img.copy(); kn = (~mask if known is None else known & ~mask).astype(float)
    for sigma in (24, 12, 6, 3):
        num = np.stack([ndimage.gaussian_filter(img[..., k] * kn, sigma) for k in range(img.shape[2])], -1)
        den = ndimage.gaussian_filter(kn, sigma)[..., None]
        ok = den[..., 0] > 0.02
        upd = mask & ok
        out[upd] = (num / np.maximum(den, 1e-6))[upd]
    return out


def lum_of(a):
    return a[..., :3].mean(-1)


def text_box(t, gm, mk):
    """A label's box (original px, game view): its group's ink box ("box"), a given one ("at"), or ("mark": [group, n]) a
    box centred "lift" px further out from the knob ("knob", index into the measured knobs) than the group's n-th mark from
    the left, moved by "nudge", "width" wide and the cap high."""
    if "mark" in t:
        grp, n = t["mark"]
        blobs = sorted(gm["groups"][grp]["blobs"], key=lambda b: b["centre"][0])
        if n >= len(blobs): return None
        p = np.array(blobs[n]["centre"]); k = np.array(gm["knobs"][t.get("knob", mk.get("label_knob", 1))]["centre"])
        u = (p - k) / max(float(np.hypot(*(p - k))), 1e-6)
        c = p + u * t.get("lift", 0.0) + np.array(t.get("nudge", [0.0, 0.0]))
        if "x" in t: c[0] = t["x"]
        if "y" in t: c[1] = t["y"]
        w, h = t.get("width", 8.0), t.get("cap", 2.0)
        return [float(c[0] - w / 2), float(c[1] - h / 2), float(c[0] + w / 2), float(c[1] + h / 2)]
    grp = gm["groups"].get(t.get("box"))
    return t.get("at") or (grp["ink_box"] if grp else None)


def panel_patch(g, spec, label_set, base_game, s):
    """The left panel's window (game view, S-times texels): the base's marks erased and drawn again; returns the patch,
    its window (original px, game view) and the mask of texels changed."""
    mk = spec["marks"]; gm = g["marks"]
    x0, y0, x1, y1 = mk["region"]
    cv = Canvas(base_game.shape[1] // s, base_game.shape[0] // s, s, (x0, y0, x1, y1))
    win = base_game[int(y0 * s):int(y1 * s), int(x0 * s):int(x1 * s), :3].astype(float)
    # what to erase: every measured mark's pixels, grown; the pointers on the knob faces
    erase = np.zeros(win.shape[:2], bool)
    grow = spec["marks"]["erase_grow"]
    def stamp(pixels, grow_):
        for (px_, py_) in pixels:
            erase[...] |= (np.abs(cv.X - (px_ + 0.5)) <= 0.5 + grow_) & (np.abs(cv.Y - (py_ + 0.5)) <= 0.5 + grow_)
    for name in mk["erase_groups"]:
        for b in gm["groups"][name]["blobs"]: stamp(b["pixels"], grow)
    # the upscale's soft ink round the marks: dark texels within a pixel of the erased ones go too; the fill reads only the
    # surface (texels not darker than the panel's own shade)
    panel_l = float(np.mean(gm["panel_colour"]))
    wl = lum_of(win)
    near = ndimage.binary_dilation(erase, iterations=max(1, int(round(mk["halo"] * s))))
    erase |= near & (wl < panel_l - mk["halo_darker"])
    patch = inpaint(win, erase, wl > panel_l - mk["halo_darker"])
    knob_mask = np.zeros(win.shape[:2], bool)
    if mk.get("redraw_pointers"):
        for kn in gm["knobs"]:
            pm = np.zeros(win.shape[:2], bool)
            for (px_, py_) in kn["pointer"]["pixels"]:
                pm |= (np.abs(cv.X - (px_ + 0.5)) <= 0.5 + mk["pointer_grow"]) & (np.abs(cv.Y - (py_ + 0.5)) <= 0.5 + mk["pointer_grow"])
            face = cv.disc(kn["centre"], kn["radius"] - 0.6) > 0.5
            pm &= face
            # the knob's face is a smooth light: fill from the face round the old pointer only
            pm |= face & ndimage.binary_dilation(pm, iterations=max(1, int(round(mk["halo"] * s)))) & (wl < mk["face_light"])
            fill = inpaint(win, pm, face & (wl >= mk["face_light"])) if pm.any() else win
            patch[pm] = fill[pm]; erase |= pm; knob_mask |= face
    ink = spec["marks"]["ink"]
    cov_all = np.zeros(win.shape[:2])
    # the dotted arc: dots at the measured places, the ends larger
    arc = gm["arc"]
    if arc and mk.get("arc"):
        am = mk["arc"]
        angs = arc["angles"]
        if am.get("even"):
            a_lo, a_hi = angs[0], angs[-1]; angs = list(np.linspace(a_lo, a_hi, len(angs)))
        for i, a in enumerate(angs):
            end = i in (0, len(angs) - 1)
            p = (arc["centre"][0] + arc["radius"] * math.cos(math.radians(a)), arc["centre"][1] + arc["radius"] * math.sin(math.radians(a)))
            if end and am.get("end_wedge"):
                # a short radial bar, as the original's larger end marks
                u = np.array([math.cos(math.radians(a)), math.sin(math.radians(a))])
                cov_all = np.maximum(cov_all, cv.seg(np.array(p) - u * am["end_len"] / 2, np.array(p) + u * am["end_len"] / 2, am["end_width"]))
            else:
                cov_all = np.maximum(cov_all, cv.disc(p, am["end_radius"] if end else am["dot_radius"]))
    # ticks: straight strokes along each measured mark's long axis
    for name in label_set.get("tick_groups", mk.get("tick_groups", [])):
        for b in gm["groups"][name]["blobs"]:
            P = np.array(b["pixels"], float) + 0.5
            if len(P) < 2:
                cov_all = np.maximum(cov_all, cv.disc(P[0], mk["tick_width"] / 2)); continue
            mu, u = fit_line_tls(P); t = (P - mu) @ u
            L = max(t.max() - t.min(), 1.0) + mk["tick_extend"]
            if mk.get("x_mark") and len(P) >= 5 and name == mk["x_mark"]:
                c_ = mu; h_ = L / 2 * 0.75
                cov_all = np.maximum(cov_all, cv.seg(c_ + np.array([-h_, -h_]) * 0.8, c_ + np.array([h_, h_]) * 0.8, mk["tick_width"]))
                cov_all = np.maximum(cov_all, cv.seg(c_ + np.array([-h_, h_]) * 0.8, c_ + np.array([h_, -h_]) * 0.8, mk["tick_width"]))
                continue
            cov_all = np.maximum(cov_all, cv.seg(mu - u * L / 2, mu + u * L / 2, mk["tick_width"]))
    # the knob pointers
    if mk.get("redraw_pointers"):
        for kn in gm["knobs"]:
            a = math.radians(kn["pointer"]["angle"]); u = np.array([math.cos(a), math.sin(a)])
            c_ = np.array(kn["centre"])
            pc = np.maximum(np.zeros_like(cov_all), cv.seg(c_ + u * mk["pointer_from"], c_ + u * (kn["radius"] - mk["pointer_inset"]), mk["pointer_width"]))
            patch = over(patch, pc, mk["pointer_colour"])
            erase |= pc > 0
    # the words
    for t in label_set.get("texts", []):
        box = text_box(t, gm, mk)
        if box is None: continue
        if t.get("pad"): box = [box[0] - t["pad"][0], box[1] - t["pad"][1], box[2] + t["pad"][0], box[3] + t["pad"][1]]
        cov = text_coverage(cv, t["text"], font_file(t.get("font", label_set.get("font", spec["default_font"])), spec), box,
                            cap=t.get("cap"), tracking=t.get("tracking", 0.0))
        cov_all = np.maximum(cov_all, cov)
    # marks kept as the original paints them (no lettering asked for): each painted pixel a square, the squares blurred and
    # cut at a level, so the blobs keep their places and sizes with a smooth outline
    for name in label_set.get("blob_groups", []):
        sq = np.zeros(win.shape[:2])
        for b in gm["groups"][name]["blobs"]:
            for (px_, py_) in b["pixels"]:
                sq[(np.abs(cv.X - (px_ + 0.5)) <= 0.5) & (np.abs(cv.Y - (py_ + 0.5)) <= 0.5)] = 1.0
        bl = ndimage.gaussian_filter(sq, mk["blob_sigma"] * s)
        cov_all = np.maximum(cov_all, np.clip((bl - mk["blob_level"]) * 4 + 0.5, 0, 1))
    if mk.get("ink_multiply") is not None:
        # printed on the panel: the panel's own shade darkened, so its shading shows through the ink
        a = np.clip(cov_all, 0, 1)[..., None]; patch = patch * (1 - a) + patch * mk["ink_multiply"] * a
    else:
        patch = over(patch, cov_all, ink)
    changed = erase | (cov_all > 0.002)
    return patch, (x0, y0, x1, y1), changed


# ------------------------------------------------------------------------------------------------------------- the edges
# The unit's outline, the scope's bezel ring and the wires, which the pack's upscale leaves wobbly and smeared: each is a
# curve fitted to the original's pixels (a circle, the body's rounded rectangle, a wire's centreline) and its texels drawn
# again from the original's own colours measured against that curve: a pixel's colour as a function of how far along the
# curve it lies (t) and how far across (d), regressed over the pixels round it (normalised convolution, narrow across,
# wide along) and looked up at each texel's (t, d). The shapes and the shading are the original's, the edges straight and
# round again. Pixels that do not fit the band (a knob, a wire, the inset crossing it) are left out of the regression and
# keep the layer below at their place. All in the game's view (rows as shown); the frames get the layer flipped back.

def curve_circle(c, r, step=0.1):
    n = max(64, int(2 * math.pi * r / step))
    a = np.arange(n) / n * 2 * math.pi
    N = np.stack([np.cos(a), np.sin(a)], 1)
    return {"P": np.array(c, float) + N * r, "N": N, "T": a * r, "closed": True, "length": 2 * math.pi * r}


def curve_rrect(box, rad, step=0.1):
    """A rounded rectangle (x0, y0, x1, y1) with corner radius rad as a closed curve, normals outward."""
    x0, y0, x1, y1 = box; P = []; N = []
    def line(p, q, n):
        L = math.hypot(q[0] - p[0], q[1] - p[1]); k = max(1, int(L / step))
        for i in range(k):
            f = i / k; P.append((p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f)); N.append(n)
    def arc(c, a0):
        k = max(4, int(math.pi / 2 * rad / step))
        for i in range(k):
            a = math.radians(a0 + 90 * i / k); u = (math.cos(a), math.sin(a))
            P.append((c[0] + u[0] * rad, c[1] + u[1] * rad)); N.append(u)
    line((x0 + rad, y0), (x1 - rad, y0), (0, -1)); arc((x1 - rad, y0 + rad), -90)
    line((x1, y0 + rad), (x1, y1 - rad), (1, 0)); arc((x1 - rad, y1 - rad), 0)
    line((x1 - rad, y1), (x0 + rad, y1), (0, 1)); arc((x0 + rad, y1 - rad), 90)
    line((x0, y1 - rad), (x0, y0 + rad), (-1, 0)); arc((x0 + rad, y0 + rad), 180)
    P = np.array(P); N = np.array(N, float)
    T = np.concatenate([[0], np.cumsum(np.hypot(*np.diff(P, axis=0).T))])
    return {"P": P, "N": N, "T": T, "closed": True, "length": float(T[-1] + math.hypot(*(P[0] - P[-1])))}


def curve_open(points, step=0.1):
    """An open curve through points (a wire's smoothed centreline), resampled every step; normals to its left."""
    P = np.asarray(points, float)
    seg = np.hypot(*np.diff(P, axis=0).T); T = np.concatenate([[0], np.cumsum(seg)])
    t = np.arange(0, T[-1], step)
    Q = np.stack([np.interp(t, T, P[:, 0]), np.interp(t, T, P[:, 1])], 1)
    D = np.gradient(Q, axis=0); D /= np.maximum(np.hypot(D[:, 0], D[:, 1]), 1e-9)[:, None]
    return {"P": Q, "N": np.stack([-D[:, 1], D[:, 0]], 1), "T": t, "closed": False, "length": float(t[-1])}


def curve_coords(cu, X, Y):
    """(t, d) of points (X, Y): along the curve to its nearest point, and across it (along that point's normal)."""
    from scipy.spatial import cKDTree
    X, Y = np.broadcast_arrays(X, Y); sh = X.shape
    Q = np.stack([X.ravel(), Y.ravel()], 1)
    _, k = cKDTree(cu["P"]).query(Q)
    d = ((Q - cu["P"][k]) * cu["N"][k]).sum(1)
    t = cu["T"][k].copy()
    if not cu["closed"]:      # past the ends: along the end's tangent
        for e in (0, len(cu["T"]) - 1):
            sel = k == e
            tan = np.array([cu["N"][e][1], -cu["N"][e][0]])
            t[sel] = cu["T"][e] + (Q[sel] - cu["P"][e]) @ tan
    return t.reshape(sh), d.reshape(sh)


class Band:
    """The original's colours (premultiplied RGB and alpha) regressed over (t, d) about a curve, pixels that do not fit
    left out (outlier: the largest channel difference, 0..255, from the band's own prediction)."""

    def __init__(self, cu, gm, d_range, sigma_t, sigma_d, outlier=60.0, mask=None, iters=2, step_t=0.25, step_d=0.05):
        self.cu = cu; self.d0, self.d1 = d_range; self.st, self.sd = step_t, step_d
        H, W = gm.shape[:2]
        yy, xx = np.mgrid[0:H, 0:W] + 0.5
        t, d = curve_coords(cu, xx, yy)
        sel = (d > self.d0 - 1) & (d < self.d1 + 1)
        if mask is not None: sel &= mask
        self.t_px, self.d_px = t, d
        a = (gm[..., 3] > 0).astype(float)
        self.V = np.concatenate([gm[..., :3] * a[..., None], a[..., None] * 255], -1)
        self.nt = int(math.ceil(cu["length"] / step_t)) + 1
        self.nd = int(math.ceil((self.d1 - self.d0 + 2) / step_d)) + 1
        self.sig = (sigma_t / step_t, sigma_d / step_d)
        self.outlier = np.zeros(sel.shape, bool)
        use = sel
        for _ in range(iters + 1):
            self.fit(use)
            # colour against colour where both are opaque (a step of the silhouette is the edge itself, not an outlier)
            pr = self.at(t, d); pa = pr[..., 3] / 255
            err = np.abs(pr[..., :3] / np.maximum(pa, 1e-3)[..., None] - self.V[..., :3]).max(-1)
            self.outlier = sel & (err > outlier) & (pa > 0.6) & (self.V[..., 3] > 0)
            use = sel & ~self.outlier
        self.fit(use)
        self.used = use

    def fit(self, use):
        it = np.round(self.t_px[use] / self.st).astype(int)
        it = np.mod(it, self.nt - 1) if self.cu["closed"] else np.clip(it, 0, self.nt - 1)
        id_ = np.clip(np.round((self.d_px[use] - (self.d0 - 1)) / self.sd).astype(int), 0, self.nd - 1)
        grid = np.zeros((self.nt, self.nd, 5))
        np.add.at(grid, (it, id_), np.concatenate([self.V[use], np.ones((int(use.sum()), 1))], 1))
        mode = ("wrap" if self.cu["closed"] else "nearest", "nearest")
        f = np.stack([ndimage.gaussian_filter(grid[..., k], self.sig, mode=mode) for k in range(5)], -1)
        self.grid = f[..., :4] / np.maximum(f[..., 4:5], 1e-9)
        self.weight = f[..., 4]

    def at(self, t, d):
        """Premultiplied RGB and alpha (0..255) at (t, d)."""
        ti = t / self.st; di = (d - (self.d0 - 1)) / self.sd
        if self.cu["closed"]: ti = np.mod(ti, self.nt - 1)
        return np.stack([ndimage.map_coordinates(self.grid[..., k], [ti.ravel(), di.ravel()], order=1, mode="nearest").reshape(t.shape)
                         for k in range(4)], -1)


def soft_mask(px_mask, cv, feather):
    """A mask of original pixels as a texel coverage on cv: each pixel a square, blurred by feather original px."""
    x0, y0, x1, y1 = cv.box; s = cv.s
    m = px_mask[int(y0):int(y1), int(x0):int(x1)].astype(float)
    up = np.repeat(np.repeat(m, s, 0), s, 1)
    if feather > 0: up = ndimage.gaussian_filter(up, feather * s)
    return up[:cv.Y.shape[0], :cv.X.shape[1]]


def band_layer(band, cv, edge_feather=0.3, keep_feather=0.4, alpha_gain=None):
    """The band on cv's texels: RGB, alpha and coverage (inside its d range, off where the original's pixels did not
    fit it)."""
    t, d = curve_coords(band.cu, cv.X, cv.Y)
    v = band.at(t, d)
    a = v[..., 3] / 255
    rgb = v[..., :3] / np.maximum(a, 1e-3)[..., None]
    g = alpha_gain or cv.s
    alpha = np.clip((a - 0.5) * g + 0.5, 0, 1)
    cov = np.clip(np.minimum(d - band.d0, band.d1 - d) / edge_feather + 0.5, 0, 1)
    out = ndimage.binary_dilation(band.outlier, np.ones((3, 3)))
    cov = cov * (1 - np.clip(soft_mask(out, cv, keep_feather) * 1.5, 0, 1))
    return np.clip(rgb, 0, 255), alpha, cov


def fit_rrect(gm, guess, d_range, mask, bin_t=12.0):
    """The body's rounded rectangle (x0, y0, x1, y1, radius): the one that makes the colours of its band most alike along
    it (least spread of luminance in each (t, d) bin), from the spec's guess."""
    from scipy.optimize import minimize
    H, W = gm.shape[:2]
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    lum = (gm[..., :3].astype(float).mean(-1) * (gm[..., 3] > 0))[mask]
    X, Y = xx[mask], yy[mask]
    def cost(p):
        cu = curve_rrect(p[:4], max(p[4], 1.0), 0.25)
        t, d = curve_coords(cu, X, Y)
        k = (d > d_range[0]) & (d < d_range[1])
        key = (np.floor(t[k] / bin_t) * 1000 + np.floor((d[k] - d_range[0]) / 0.5)).astype(int)
        u, inv, cnt = np.unique(key, return_inverse=True, return_counts=True)
        L = lum[k]
        s1 = np.bincount(inv, L); s2 = np.bincount(inv, L * L)
        var = np.maximum(s2 / cnt - (s1 / cnt) ** 2, 0)
        return float(np.sqrt(var).mean())
    r = minimize(cost, np.array(guess, float), method="Nelder-Mead", options={"xatol": 0.02, "fatol": 0.005, "maxiter": 800})
    return [round(float(v), 3) for v in r.x], float(r.fun), cost(np.array(guess, float))


def wire_zone(shape, ez, g):
    """The original pixels where the wires lie (game view): left of the body's left edge or below its bottom edge, inside
    the spec's box, clear of the scope's bezel."""
    H, W = shape
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    z = ez["zone"]; x0, y0, x1, y1 = z["box"]
    fc = g["face"]["centre"]; fcg = (fc[0], H - fc[1])
    return (((xx < z["left_of"]) | (yy > z["below"])) & (xx > x0) & (xx < x1) & (yy > y0) & (yy < y1)
            & (np.hypot(xx - fcg[0], yy - fcg[1]) > g["face"]["radius"] + z["clear_of_scope"]))


def wire_mask(gm, w, zone):
    """A wire's pixels: its palette colours (or any grey), the largest connected piece inside the zone."""
    a = gm.astype(int); op = a[..., 3] > 0
    if w.get("grey"):
        m = op & (np.abs(a[..., 0] - a[..., 1]) < 3) & (np.abs(a[..., 1] - a[..., 2]) < 3)
    else:
        m = np.zeros(op.shape, bool)
        for c in w["colours"]: m |= op & is_col(a, c)
    m &= zone
    lab, n = ndimage.label(m, np.ones((3, 3)))
    if n == 0: return m
    sz = ndimage.sum(m, lab, range(1, n + 1))
    return lab == (int(np.argmax(sz)) + 1)


def geodesic(mask, start):
    """Steps from start to every pixel of mask (8-connected), -1 outside."""
    from collections import deque
    d = np.full(mask.shape, -1, int); d[start] = 0; q = deque([start]); H, W = mask.shape
    while q:
        y, x = q.popleft()
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                yy, xx = y + dy, x + dx
                if 0 <= yy < H and 0 <= xx < W and mask[yy, xx] and d[yy, xx] < 0:
                    d[yy, xx] = d[y, x] + 1; q.append((yy, xx))
    return d


def measure_wire(m, bin_px=2, smooth=0.05):
    """A wire's centreline and width from its pixels: ordered from one end to the other (the pixels' steps from the far
    end, in bins), each bin's centroid, a smoothing spline through them; the width is its pixels over its length."""
    from scipy.interpolate import splprep, splev
    ys, xs = np.nonzero(m)
    if len(xs) < 8: return None
    d = geodesic(m, (ys[0], xs[0])); e = np.unravel_index(int(np.argmax(d)), d.shape); d = geodesic(m, e)
    pts = []
    for k in range(0, int(d.max()) + 1, bin_px):
        yy, xx = np.nonzero((d >= k) & (d < k + bin_px))
        if len(xx): pts.append((xx.mean() + 0.5, yy.mean() + 0.5))
    pts = np.array(pts)
    if len(pts) < 4: return None
    tck, _ = splprep([pts[:, 0], pts[:, 1]], s=len(pts) * smooth)
    u = np.linspace(0, 1, max(20, len(pts) * 4))
    x, y = splev(u, tck)
    P = np.stack([x, y], 1)
    L = float(np.hypot(*np.diff(P, axis=0).T).sum())
    return {"points": [[round(float(a), 3), round(float(b), 3)] for a, b in P], "length": round(L, 2),
            "width": round(float(m.sum()) / max(L, 1.0), 3), "pixels": int(m.sum()),
            "fit_rms": round(float(np.sqrt(np.mean([np.min(np.hypot(P[:, 0] - px, P[:, 1] - py)) ** 2 for px, py in pts]))), 3)}


def silhouette_points(op, box, side):
    """The silhouette's pixel edges in box (game view) of a body corner whose outside lies to the given side ("left" or
    "right") and below: the outer edge of the last opaque pixel of each row and each column."""
    x0, y0, x1, y1 = box; P = []
    for y in range(y0, y1):
        if side == "left":
            xs = [x for x in range(x0, x1) if op[y, x] and not op[y, x - 1]]
            if xs: P.append((xs[-1], y + 0.5))
        else:
            xs = [x for x in range(x0, x1) if op[y, x] and not op[y, x + 1]]
            if xs: P.append((xs[0] + 1, y + 0.5))
    for x in range(x0, x1):
        ys = [y for y in range(y0, y1) if op[y, x] and not op[y + 1, x]]
        if ys: P.append((x + 0.5, ys[-1] + 1))
    return np.array(P, float)


def measure_edges(med, spec, g):
    """The wires' centrelines and widths (game view); None without the spec's "edges"."""
    ez = spec.get("edges")
    if not ez: return None
    gm = to_game_rows(med)
    zone = wire_zone(gm.shape[:2], ez, g)
    out = {"wires": {}, "corners": []}
    op = gm[..., 3] > 0
    for c in ez.get("corners", []):
        P = silhouette_points(op, c["box"], c["side"])
        if len(P) < 5: out["corners"].append(None); continue
        (cx, cy, r), rms = fit_circle(P[:, 0], P[:, 1])
        out["corners"].append({"centre": [round(cx, 3), round(cy, 3)], "radius": round(r, 3), "rms": round(rms, 3), "points": len(P)})
    for w in ez.get("wires", []):
        out["wires"][w["name"]] = measure_wire(wire_mask(gm, w, zone), w.get("bin", 2), w.get("smooth", 0.05))
    return out


def tube_coverage(cu, cv, width, ext=1.5):
    """A tube of the given width along an open curve on cv (one-texel edge), its ends carried on ext px."""
    t, d = curve_coords(cu, cv.X, cv.Y)
    end = np.maximum(-t - ext, t - cu["length"] - ext)
    return cv.aa(np.maximum(np.abs(d) - width / 2, end)), t, d


def edge_layers(g, spec, med, s):
    """The edge layers, bottom first, each (box, RGB, alpha, coverage, mode) in the game's view at s: the body's bottom
    corners, the wires' zone cleared where the original is transparent, the cable, the twisted pair, then the bezel rings.
    mode "all" lays the layer's colour and alpha; "silhouette" its alpha, and its colour only where the frame below was
    transparent (the outline made round again, the upscale's own shading kept)."""
    ez = spec.get("edges"); me = g.get("edges")
    if not ez or me is None or med is None: return []
    gm = to_game_rows(med); H, W = gm.shape[:2]
    layers = []
    for c, mc in zip(ez.get("corners", []), me.get("corners", [])):
        if not mc: continue
        band = Band(curve_circle(mc["centre"], mc["radius"]), gm, c["d"], c["sigma_t"], c["sigma_d"], c.get("outlier", 60))
        R = mc["radius"] + c["d"][1] + 1; cx, cy = mc["centre"]
        box = [max(int(math.floor(cx - R)), 0), max(int(math.floor(cy - R)), 0), min(int(math.ceil(cx + R)), W), min(int(math.ceil(cy + R)), H)]
        cv = Canvas(W, H, s, box)
        rgb, al, cov = band_layer(band, cv)
        ang = np.degrees(np.arctan2(cv.Y - cy, cv.X - cx)); a0, a1 = c["arc"]
        mid = (a0 + a1) / 2; half = (a1 - a0) / 2
        cov = cov * np.clip((half - np.abs(angdiff(ang, mid))) / c.get("arc_feather", 6.0) + 0.5, 0, 1)
        layers.append((box, rgb, al, cov, c.get("mode", "all")))
    zone = wire_zone((H, W), ez, g)
    z = ez["zone"]; zb = [int(v) for v in z["box"]]
    cvz = Canvas(W, H, s, zb)
    zs = soft_mask(zone, cvz, 0)
    # the zone redrawn: transparent where the original is, the original's other pixels (the shadows beside the wires) in
    # their own dark shade, the outline smoothed; the wires over that
    op = gm[..., 3] > 0
    wires = np.zeros(op.shape, bool)
    for w in ez.get("wires", []): wires |= wire_mask(gm, w, zone)
    rest = zone & op & ~wires
    shade = np.median(gm[rest][:, :3], 0) if rest.any() else np.zeros(3)
    a = np.clip((soft_mask(op, cvz, z["feather"]) - 0.5) * cvz.s + 0.5, 0, 1)
    layers.append((zb, np.broadcast_to(shade.astype(float), zs.shape + (3,)), a, zs, "all"))
    for w in ez.get("wires", []):
        mw = me["wires"].get(w["name"])
        if not mw: continue
        cu = curve_open(mw["points"])
        width = mw["width"] * w.get("width_scale", 1.0)
        cov, t, d = tube_coverage(cu, cvz, width, w.get("extend", 1.5))
        cov = cov * zs
        m = wire_mask(gm, w, zone)
        classes = w.get("classes") or {"all": None}
        rgb = None; prob = []
        for cname, cols in classes.items():
            cm = m.copy()
            if cols is not None:
                cm = np.zeros(m.shape, bool)
                for c in cols: cm |= m & is_col(gm.astype(int), c)
            band = Band(cu, gm, (-width / 2 - 0.5, width / 2 + 0.5), w["sigma_t"], w["sigma_d"], outlier=1e9, mask=cm, iters=0)
            v = band.at(t, d); col = v[..., :3] / np.maximum(v[..., 3:4] / 255, 1e-3)
            pr = soft_mask(cm, cvz, w.get("class_sigma", 0.5))
            prob.append((col, pr))
        if len(prob) == 1:
            rgb = prob[0][0]
        else:
            tot = sum(p for _, p in prob) + 1e-6
            # the colour whose pixels are nearest wins, its edge a texel wide (the original's twist kept, its steps smoothed)
            p0 = np.clip((prob[0][1] / tot - 0.5) * w.get("class_gain", 6) + 0.5, 0, 1)[..., None]
            rgb = prob[0][0] * p0 + prob[1][0] * (1 - p0)
        layers.append((zb, np.clip(rgb, 0, 255), np.ones(cov.shape), cov, "all"))
    for r in ez.get("rings", []):
        if r["circle"] == "face": c, R = g["face"]["centre"], g["face"]["radius"]
        else: c, R = g["inset"]["face"]["centre"], g["inset"]["face"]["radius"]
        cg = (c[0], H - c[1])
        band = Band(curve_circle(cg, R), gm, (r["d"][0], r["d_free"]), r["sigma_t"], r["sigma_d"], r.get("outlier", 60))
        box = [int(math.floor(cg[0] - R - r["d_free"] - 1)), int(math.floor(cg[1] - R - r["d_free"] - 1)),
               int(math.ceil(cg[0] + R + r["d_free"] + 1)), int(math.ceil(cg[1] + R + r["d_free"] + 1))]
        box = [max(box[0], 0), max(box[1], 0), min(box[2], W), min(box[3], H)]
        cv = Canvas(W, H, s, box)
        rgb, al, cov = band_layer(band, cv)
        # the band's outer edge: d[1] over the panel, d_free where the original is transparent beyond the ring (the
        # silhouette, its steps gone); arcs where another part takes over are left out
        t, d = curve_coords(band.cu, cv.X, cv.Y)
        free = ring_free(band, gm, r)
        fr_t = np.interp(np.mod(t, band.cu["length"]), free[0], free[1])
        d1 = r["d"][1] + (r["d_free"] - r["d"][1]) * fr_t
        cov = cov * np.clip((d1 - d) / 0.3 + 0.5, 0, 1)
        ang = np.degrees(np.arctan2(cv.Y - cg[1], cv.X - cg[0]))
        if r.get("under_inset") is not None:      # the corner scope's ring lies over this one
            ic = g["inset"]["face"]["centre"]; ir = g["inset"]["face"]["radius"] + r["under_inset"]
            cov = cov * np.clip((np.hypot(cv.X - ic[0], cv.Y - (H - ic[1])) - ir) / 0.3 + 0.5, 0, 1)
        for a0, a1 in r.get("skip", []):
            mid = (a0 + a1) / 2; half = (a1 - a0) / 2
            cov = cov * np.clip((np.abs(angdiff(ang, mid)) - half) / r.get("skip_feather", 4.0), 0, 1)
        layers.append((box, rgb, al, cov, r.get("mode", "all")))
    return layers


def ring_free(band, gm, r, bin_px=2.0):
    """Along a ring: 1 where the original is transparent just outside it (its silhouette), 0 over the panel, smoothed."""
    H, W = gm.shape[:2]
    t, d = band.t_px, band.d_px
    sel = (d > r["d"][1] + 0.3) & (d < r["d_free"])
    nb = int(math.ceil(band.cu["length"] / bin_px)) + 1
    b = np.clip((t[sel] / bin_px).astype(int), 0, nb - 1)
    tot = np.bincount(b, minlength=nb).astype(float)
    clear = np.bincount(b, (gm[..., 3][sel] == 0).astype(float), minlength=nb)
    f = ndimage.gaussian_filter1d(clear, 1.0, mode="wrap") / np.maximum(ndimage.gaussian_filter1d(tot, 1.0, mode="wrap"), 1e-6)
    return np.arange(nb) * bin_px, np.clip((f - 0.3) / 0.4, 0, 1)


# ------------------------------------------------------------------------------------------------------------- frames

def flip_rows(a):
    return np.ascontiguousarray(a[::-1])


def resize_to(a, w, h):
    if a.shape[1] == w and a.shape[0] == h: return a
    return np.asarray(Image.fromarray(a).resize((w, h), Image.BOX if w < a.shape[1] else Image.LANCZOS))


class Redraw:
    """One label set and style at scale s: the static parts made once, each frame composited over its upscale."""

    def __init__(self, g, spec, style, label_set, s, panel_base, med=None, distress=None):
        self.g, self.spec, self.style, self.s = g, spec, style, s
        self.wear = None
        # the edges (wires, bezel rings): drawn once, game view, flipped into the file's rows; need the original's median
        self.edges = []
        for box, rgb, al, cov, mode in edge_layers(g, spec, med, s):
            x0, y0, x1, y1 = box
            self.edges.append(((x0, 128 - y1, x1, 128 - y0), flip_rows(rgb), flip_rows(al), flip_rows(cov), mode))
        self.ls = label_set
        self.panel_base = panel_base
        H, W = 128, 256
        fc = g["face"]["centre"]; fr = g["face"]["radius"]
        m = spec["scope"]["window_margin"]
        self.sbox = (math.floor(fc[0] - fr - m), math.floor(fc[1] - fr - m), math.ceil(fc[0] + fr + m), math.ceil(fc[1] + fr + m))
        self.scv = Canvas(W, H, s, self.sbox)
        ic = g["inset"]["face"]["centre"]; ir = g["inset"]["face"]["radius"]
        self.ibox = (math.floor(ic[0] - ir - 1), math.floor(ic[1] - ir - 1), math.ceil(ic[0] + ir + 1), math.ceil(ic[1] + ir + 1))
        self.icv = Canvas(W, H, s, self.ibox)
        self.inset_img, self.inset_cov = inset_face(g, spec, self.icv, style)
        self.inset_cov *= spec["inset"].get("blend", 1.0)
        # the panel: worked in the game's view on one base frame, the same patch on every frame (no flicker from the upscale)
        pg = flip_rows(panel_base)
        if label_set is not None:
            patch, box, changed = panel_patch(g, spec, label_set, pg, s)
            x0, y0, x1, y1 = box
            # back to the file's rows
            self.panel = (flip_rows(patch), flip_rows(changed), (x0, H - y1, x1, H - y0))
        else:
            self.panel = None
        self.cache = {}
        # the wear (tools/panel_distress.py, the damage panel's and the counters' layer): made once from the base frame as
        # drawn so far, the same on every frame, the layers and the stamps' discs; not on the scopes' faces
        if distress: self.wear = self.make_wear(panel_base, distress)
        # the stamps' discs: the same texels in every frame (the base frame's, redrawn), which the redrawn sheets carry too
        self.stamps = g.get("stamps") if spec.get("stamps") else None
        self.static = self.core(panel_base, "sweep", spec["frames"]["count"]) if self.stamps else None
        if self.stamps:
            st = spec["stamps"]; H_, W_ = self.static.shape[:2]
            X = (np.arange(W_) + 0.5)[None, :] / s; Y = (np.arange(H_) + 0.5)[:, None] / s
            self.paste = np.zeros((H_, W_))
            for key, part in st["parts"].items():
                cx, cy = stamp_centre_file(self.stamps, key)
                r = np.hypot(X - cx, Y - cy)
                self.paste = np.maximum(self.paste, np.clip((part["mask_radius"] + st["paste_hold"] + st["paste_feather"] - r) / st["paste_feather"], 0, 1))

    def make_wear(self, panel_base, distress):
        """distress: (level, colours, seed) as tools/hud-redraw/hud.json's "distress" gives them; the maps in the file's rows."""
        import panel_distress as pd
        level, colours, seed = distress
        if not level: return None
        base = self.core(panel_base, "face", None)
        gv = flip_rows(base); s = self.s; H, W = gv.shape[:2]
        X = (np.arange(W) + 0.5)[None, :] / s; Y = (np.arange(H) + 0.5)[:, None] / s
        m = (gv[..., 3] > 127).astype(float)
        for c, r in ((self.g["face"]["centre"], self.g["face"]["radius"]), (self.g["inset"]["face"]["centre"], self.g["inset"]["face"]["radius"])):
            m *= np.clip(np.hypot(X - c[0], Y - (128 - c[1])) - r - 0.3, 0, 1)
        L = pd.make_layer(gv, s, level, seed, "radar", mask=m)
        return {k: flip_rows(v) for k, v in L.items()}, colours

    def has_layers(self):
        """Whether the style can be drawn live from the layers: the graticule printed over the beam and cone (grid_over)."""
        return bool(self.spec["styles"][self.style].get("grid_over"))

    def layers(self):
        """The live sweep's layers ({name: RGBA, the frames' rows}): the face (the whole frame, radar on, with no beam, cone,
        afterglow, graticule or pip; the stamps' discs as every frame has them) and the grid (the graticule and the pip over the
        scope's window, alpha their coverage inside the face's circle)."""
        out = self.core(self.panel_base, "face", None)
        if self.stamps:
            a = self.paste[..., None]
            out[..., :3] = np.clip(np.round(out[..., :3] * (1 - a) + self.static[..., :3] * a), 0, 255).astype(np.uint8)
        rgb, al = scope_grid(self.g, self.spec, self.scv)
        grid = np.zeros(al.shape + (4,), np.uint8)
        grid[..., :3] = np.clip(np.round(rgb), 0, 255).astype(np.uint8)
        grid[..., 3] = np.clip(np.round(al * 255), 0, 255).astype(np.uint8)
        return {SWEEP_LAYERS["face"]: out, SWEEP_LAYERS["grid"]: grid}

    def sweep_params(self):
        """The fitted numbers the game's live sweep needs (docs/hd-textures.md's format; zrads.json in a pack)."""
        return sweep_params(self.g, self.spec, self.style, self.s, self.sbox)

    def sprites(self):
        """The range plate's and the lamp's sheets ({name: RGBA, rows as shown}), or {} without the stamps' measures."""
        return draw_sprites(self, flip_rows(self.static)) if self.stamps else {}

    def frame(self, base, kind, i):
        """base: the upscale of this frame (s-times, RGBA uint8); kind 'sweep' | 'locked'; i 0..30 (30: radar off)."""
        out = self.core(base, kind, i)
        if self.stamps:
            a = self.paste[..., None]
            out[..., :3] = np.clip(np.round(out[..., :3] * (1 - a) + self.static[..., :3] * a), 0, 255).astype(np.uint8)
        return out

    def core(self, base, kind, i):
        """The frame redrawn over its upscale, the stamps' discs as the upscale has them."""
        s = self.s; out = base.copy(); img = out[..., :3].astype(float)
        if self.panel is not None:
            patch, changed, (x0, y0, x1, y1) = self.panel
            sl = (slice(int(y0 * s), int(y1 * s)), slice(int(x0 * s), int(x1 * s)))
            reg = img[sl]; reg[changed] = patch[changed]
        if self.edges:
            alpha = out[..., 3].astype(float)
            for (x0, y0, x1, y1), rgb, al, cov, mode in self.edges:
                sl = (slice(int(y0 * s), int(y1 * s)), slice(int(x0 * s), int(x1 * s)))
                cc = cov * np.clip(1 - alpha[sl] / 255, 0, 1) if mode == "silhouette" else cov
                c = cc[..., None]; img[sl] = img[sl] * (1 - c) + rgb * c
                alpha[sl] = alpha[sl] * (1 - cov) + al * 255 * cov
            out[..., 3] = np.clip(np.round(alpha), 0, 255).astype(np.uint8)
        if self.wear is not None:
            import panel_distress as pd
            pd.apply_float(img, self.wear[0], self.wear[1], s=s)
        frame = None if i is None or i >= self.spec["frames"]["count"] else i
        face, cov = scope_face(self.g, self.spec, self.scv, self.style, frame, kind == "locked", parts="face" if kind == "face" else "all")
        x0, y0, x1, y1 = self.sbox
        sl = (slice(int(y0 * s), int(y1 * s)), slice(int(x0 * s), int(x1 * s)))
        a = cov[..., None]; img[sl] = img[sl] * (1 - a) + face * a
        x0, y0, x1, y1 = self.ibox
        sl = (slice(int(y0 * s), int(y1 * s)), slice(int(x0 * s), int(x1 * s)))
        a = self.inset_cov[..., None]; img[sl] = img[sl] * (1 - a) + self.inset_img * a
        out[..., :3] = np.clip(np.round(img), 0, 255).astype(np.uint8)
        return out


def find_texture(folder, stem):
    """folder/stem.png or .dds (the PNG first, as the game takes it), or None."""
    for ext in (".png", ".dds"):
        p = os.path.join(folder, stem + ext)
        if os.path.exists(p): return p
    return None


def upscale_frame(upscale_dir, stem, s, bottom_up=frozenset()):
    """A frame of the upscale in the export's rows: a PNG as it is, a .dds's top level (Pillow reads BC7) turned upright when
    bottom-up.txt names it."""
    p = find_texture(upscale_dir, stem)
    if p is None: raise SystemExit(f"{stem}: neither .png nor .dds in {upscale_dir}")
    a = load_rgba(p)
    if p.endswith(".dds") and stem in bottom_up: a = np.ascontiguousarray(a[::-1])
    return resize_to(a, 256 * s, 128 * s)


def label_set_named(spec, name):
    for ls in spec["label_sets"]:
        if ls["name"] == name: return ls
    raise SystemExit(f"no label set {name!r}: " + ", ".join(l["name"] for l in spec["label_sets"]))


def all_stems(spec):
    """The 62 frames' names, sweep then locked, each 0..30."""
    fr = spec["frames"]; return [fr[kind] % i for kind in ("sweep", "locked") for i in range(fr["count"] + 1)]


def redraw_frames(export_dir, load, spec, s, style, ls_name, g=None, distress=None):
    """Yields (stem, RGBA, geometry) for the 62 frames (the export's rows) and then the two sheets (rows as shown, as the
    export has them); load(stem) gives a frame's upscale at s in the export's rows."""
    frames = load_frames(export_dir, spec)
    if g is None: g, _ = measure(frames, spec, export_dir)
    ls = label_set_named(spec, ls_name)
    rd = Redraw(g, spec, style, ls, s, load(spec["frames"]["sweep"] % spec["frames"]["count"]), median_of(frames, spec), distress)
    for kind in ("sweep", "locked"):
        for i in range(spec["frames"]["count"] + 1):
            stem = spec["frames"][kind] % i
            yield stem, rd.frame(load(stem), kind, i), g
    # a .map's PNG upscale is read with its rows as shown (the game flips it to the stored order, HdTexture.Fit; only a .dds
    # is written bottom up), as the export is
    for name, sheet in rd.sprites().items():
        yield name, sheet, g
    # the live sweep's layers and numbers (a style whose graticule lies over the beam and cone: B)
    if rd.has_layers():
        for name, layer in rd.layers().items():
            yield name, layer, g
        yield SWEEP_LAYERS["params"], rd.sweep_params(), g


def render_all(export_dir, upscale_dir, out_dir, spec, s, style, ls_name, g=None, distress=None):
    os.makedirs(out_dir, exist_ok=True)
    bu = read_bottom_up(export_dir)
    for stem, a, g in redraw_frames(export_dir, lambda st: upscale_frame(upscale_dir, st, s, bu), spec, s, style, ls_name, g, distress):
        if isinstance(a, dict): write_params(os.path.join(out_dir, stem), a)
        else: Image.fromarray(a).save(os.path.join(out_dir, stem + ".png"))
    with open(os.path.join(out_dir, "radar-geometry.json"), "w") as f: json.dump(strip(g), f, indent=1)
    return g


# ------------------------------------------------------------------------------------------------------------- the pack step

BASE = ".radar-base"


def read_bottom_up(export_dir):
    p = os.path.join(export_dir, "bottom-up.txt")
    if not os.path.exists(p): return frozenset()
    with open(p) as f: return frozenset(l.strip().lower() for l in f if l.strip())


def fonts_used(spec, ls):
    return sorted({t.get("font", ls.get("font", spec["default_font"])) for t in ls.get("texts", [])})


def font_credit(spec, name):
    f = spec["fonts"][name]
    return {"font": f.get("name", name), "by": f["by"], "license": f["license"], "source": f.get("source", f.get("url", ""))}


def sha256_of(path):
    import hashlib
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 22), b""): h.update(b)
    return h.hexdigest()


def note_in_pack(src, dst, spec, spec_path, style, ls_name, s, distress_name="0"):
    """src's pack.json (radar_redraw; the font in text_fonts) and ATTRIBUTION.txt (the font's line, in the file's own style)
    written into dst; FILES.sha256 made again when dst is the pack itself and has one."""
    ls = label_set_named(spec, ls_name); credits = [font_credit(spec, n) for n in fonts_used(spec, ls)]
    pj = os.path.join(src, "pack.json")
    if os.path.exists(pj):
        with open(pj, encoding="utf-8", newline="") as f: raw = f.read()
        meta = json.loads(raw); nl = "\r\n" if "\r\n" in raw else "\n"
        fr = spec["frames"]; last = "%03d" % fr["count"]
        meta["radar_redraw"] = {"tool": "tools/radar-redraw.py", "tool_sha256": sha256_of(os.path.abspath(__file__)),
                                "spec": "tools/radar-redraw/radar.json", "spec_sha256": sha256_of(spec_path),
                                "style": style, "labels": ls_name, "scale": s,
                                "glass": (spec.get("glass") or {}).get("use", (spec.get("glass") or {}).get("default", "none")),
                                "textures": ", ".join([fr["sweep"] % 0 + "-" + last, fr["locked"] % 0 + "-" + last]
                                                      + sorted(p["sheet"] for p in spec["stamps"]["parts"].values())
                                                      + ([SWEEP_LAYERS[k] for k in ("face", "grid", "params")] if spec["styles"][style].get("grid_over") else [])),
                                "fonts": [c["font"] for c in credits]}
        if distress_name not in (None, "0"):
            meta["radar_redraw"]["distress"] = distress_name
            meta["radar_redraw"]["distress_sha256"] = sha256_of(os.path.join(HERE, "panel_distress.py"))
        have = {f_.get("font") for f_ in meta.get("text_fonts", [])}
        new = [c for c in credits if c["font"] not in have]
        meta["text_fonts"] = meta.get("text_fonts", []) + new
        if "lettering redrawn in " in meta.get("attribution", ""):      # the earlier packs' one-line credits
            meta["attribution"] += "".join(f" | lettering redrawn in {c['font']} by {c['by']}, {c['license']}, {c['source']}" for c in new)
        with open(os.path.join(dst, "pack.json.part"), "w", encoding="utf-8", newline="") as f:
            f.write(json.dumps(meta, indent=2, ensure_ascii=False).replace("\n", nl) + nl)
        os.replace(os.path.join(dst, "pack.json.part"), os.path.join(dst, "pack.json"))
    at = os.path.join(src, "ATTRIBUTION.txt")
    if os.path.exists(at):
        with open(at, encoding="utf-8", newline="") as f: text = f.read()
        nl = "\r\n" if "\r\n" in text else "\n"
        lines = text.split(nl)
        while lines and lines[-1] == "": lines.pop()
        for c in credits:
            if any(c["font"] + " " in l or c["font"] + ":" in l for l in lines): continue
            head = [i for i, l in enumerate(lines) if l.startswith("Fonts the redrawn lettering")]
            older = [i for i, l in enumerate(lines) if l.startswith("lettering redrawn in ")]
            if head:                # tools/hd-pack.py finish's list: "- Face: by, licence, source" under its heading
                i = head[0] + 1
                while i < len(lines) and lines[i].startswith("- "): i += 1
                lines.insert(i, f"- {c['font']}: {c['by']}, {c['license']}, {c['source']}")
            elif older:             # the earlier packs' one line a face
                lines.insert(older[-1] + 1, f"lettering redrawn in {c['font']} by {c['by']}, {c['license']}, {c['source']}")
            else:
                lines += ["", "Fonts the redrawn lettering was set in (rendered into the pictures; no font file is included):",
                          f"- {c['font']}: {c['by']}, {c['license']}, {c['source']}"]
        with open(os.path.join(dst, "ATTRIBUTION.txt.part"), "w", encoding="utf-8", newline="") as f: f.write(nl.join(lines) + nl)
        os.replace(os.path.join(dst, "ATTRIBUTION.txt.part"), os.path.join(dst, "ATTRIBUTION.txt"))
    if os.path.abspath(src) == os.path.abspath(dst) and os.path.exists(os.path.join(dst, "FILES.sha256")):
        import importlib.util
        sp = importlib.util.spec_from_file_location("hd_pack", os.path.join(HERE, "hd-pack.py"))
        hp = importlib.util.module_from_spec(sp); sp.loader.exec_module(hp); hp.write_sums(dst)


def write_params(path, p):
    with open(path + ".part", "w", encoding="utf-8", newline="\n") as f: f.write(json.dumps(p, indent=1) + "\n")
    os.replace(path + ".part", path)


def pack_redrawn(pack):
    """pack.json names a radar redraw already (its frames are not the upscale's own)."""
    pj = os.path.join(pack, "pack.json")
    if not os.path.exists(pj): return False
    with open(pj, encoding="utf-8") as f:
        try: return "radar_redraw" in json.load(f)
        except ValueError: return False


def pack_step(export_dir, pack, out, spec, spec_path, style, ls_name, s=None, base=None, distress=None, distress_name="0"):
    """A pack's radar frames and sheets redrawn in its own format: in place (out None; the frames it starts from kept in
    pack/.radar-base/, which later runs start from) or into out (the pack only read). The redraw always starts from the
    upscale's own frames: base (a folder holding them, e.g. a backup) when given, else pack/.radar-base/, else the pack's
    frames, refused when pack.json says they are a redraw already. Returns the files written and s."""
    bu = read_bottom_up(export_dir)
    base_dir = os.path.join(pack, BASE); dst = out or pack
    if base is None and not os.path.isdir(base_dir) and pack_redrawn(pack):
        raise SystemExit(f"{pack}: its radar frames are a redraw already (pack.json has radar_redraw) and there is no "
                         f"{BASE}; give the upscale's own frames with --base DIR")
    src = {}
    for stem in all_stems(spec):
        p = (find_texture(base, stem) if base else None) or find_texture(base_dir, stem) or (None if base else find_texture(pack, stem))
        if p is None: raise SystemExit(f"{stem}: not in {base or pack}")
        src[stem] = p
    if out is None:
        os.makedirs(base_dir, exist_ok=True)
        for stem, p in src.items():
            if os.path.dirname(os.path.abspath(p)) != os.path.abspath(base_dir):
                b = os.path.join(base_dir, os.path.basename(p))
                try: os.link(p, b)                  # the pack's file is replaced below, never written into
                except OSError:
                    import shutil; shutil.copy2(p, b)
                src[stem] = b
    first = src[all_stems(spec)[0]]
    if s is None:
        with Image.open(first) as im: w = im.size[0]
        if w % 256: raise SystemExit(f"{first}: {w} texels wide, not a whole multiple of 256")
        s = w // 256
    fmt = os.path.splitext(first)[1]
    if fmt == ".dds":
        import etcpak
        sys.path.insert(0, HERE); import dds_mips
    os.makedirs(dst, exist_ok=True)

    def load(stem):
        a = load_rgba(src[stem])
        if src[stem].endswith(".dds") and stem in bu: a = np.ascontiguousarray(a[::-1])
        return resize_to(a, 256 * s, 128 * s)
    written = []
    for stem, a, g in redraw_frames(export_dir, load, spec, s, style, ls_name, distress=distress):
        if isinstance(a, dict):
            write_params(os.path.join(dst, stem), a); written.append(stem); continue
        # the grid layer always a PNG: its one-texel lines and their alpha kept exactly (BC7 would blotch them); small
        f_ = ".png" if stem == SWEEP_LAYERS["grid"] else fmt
        path = os.path.join(dst, stem + f_)
        if f_ != fmt:
            Image.fromarray(a).save(path + ".part", format="PNG"); os.replace(path + ".part", path)
            other = os.path.join(dst, stem + ".dds")
            if out is None and os.path.exists(other): os.remove(other)
            written.append(os.path.basename(path)); continue
        if fmt == ".dds": dds_mips.write_dds(path + ".part", np.ascontiguousarray(a[::-1] if stem in bu else a), etcpak)
        else: Image.fromarray(a).save(path + ".part", format="PNG")
        os.replace(path + ".part", path)
        other = os.path.join(dst, stem + (".png" if fmt == ".dds" else ".dds"))
        if out is None and os.path.exists(other): os.remove(other)
        written.append(os.path.basename(path))
    note_in_pack(pack, dst, spec, spec_path, style, ls_name, s, distress_name)
    return written, s


def strip(g):
    """The geometry without the per-pixel lists (for the report)."""
    def walk(x):
        if isinstance(x, dict): return {k: walk(v) for k, v in x.items() if k != "pixels"}
        if isinstance(x, list): return [walk(v) for v in x]
        return x
    return walk(g)


# ------------------------------------------------------------------------------------------------------------- label fit

def area_matrix(n_out, n_in, start, length):
    e = start + length * np.arange(n_in + 1) / n_in
    s0, s1 = e[:-1][None, :], e[1:][None, :]
    j = np.arange(n_out)[:, None]
    return np.clip(np.minimum(s1, j + 1) - np.maximum(s0, j), 0, None)


def word_fit(obs, box, text, font_path):
    """Best correlation of the text, shrunk by area onto the observed window's texels (box in window coords), over small
    shifts and widths: how well the painted blobs could be this word (a painter's downsample)."""
    f = ImageFont.truetype(font_path, 120)
    l, t, r, b = f.getbbox(text)
    im = Image.new("L", (r - l + 8, b - t + 8)); ImageDraw.Draw(im).text((4 - l, 4 - t), text, font=f, fill=255)
    cov = np.asarray(im, float) / 255; ys, xs = np.nonzero(cov > 0.05); cov = cov[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    x0, y0, x1, y1 = box; best = -2.0
    for dx in np.arange(-1.0, 1.01, 0.5):
        for dw in np.arange(-2, 2.01, 1.0):
            for dy in (-0.5, 0, 0.5):
                Ax = area_matrix(obs.shape[1], cov.shape[1], x0 + dx - dw / 2, (x1 - x0) + dw)
                Ay = area_matrix(obs.shape[0], cov.shape[0], y0 + dy, (y1 - y0))
                sm = Ay @ cov @ Ax.T
                a = sm - sm.mean(); o = obs - obs.mean(); d = math.sqrt((a * a).sum() * (o * o).sum())
                if d > 0: best = max(best, float((a * o).sum() / d))
    return best


def fit_labels(med, g, spec, words, baseline, fonts):
    """Each candidate's fit to each lettered group, in both row orders, with the baseline words' spread (z = how far above
    random words of the same kind of list the candidate sits)."""
    out = {}
    lum = med[..., :3].astype(float).mean(-1)
    panel = float(np.mean(g["marks"]["panel_colour"]))
    for grp in spec["marks"]["lettered_groups"]:
        ib = g["marks"]["groups"][grp]["ink_box"]
        if ib is None: continue
        x0, y0, x1, y1 = ib; pad = 1
        res = {}
        for view in ("game", "file"):
            L = lum[::-1] if view == "game" else lum
            ry0, ry1 = (y0, y1) if view == "game" else (128 - y1, 128 - y0)
            obs = np.clip((panel - L[ry0 - pad:ry1 + pad, x0 - pad:x1 + pad]) / max(panel - 34, 1), 0, 1)
            box = (pad, pad, pad + x1 - x0, pad + y1 - y0)
            sc = {w: max(word_fit(obs, box, w, fp) for fp in fonts) for w in sorted(set(words) | set(baseline))}
            base = np.array([sc[w] for w in baseline])
            mu, sd = float(base.mean()), float(base.std() or 1)
            res[view] = {"baseline_mean": round(mu, 3), "baseline_sd": round(sd, 3), "baseline_max": round(float(base.max()), 3),
                         "words": sorted(([w, round(sc[w], 3), round((sc[w] - mu) / sd, 2)] for w in words), key=lambda r: -r[1])}
        out[grp] = res
    return out


# ------------------------------------------------------------------------------------------------------------- self-check

def synthetic(spec):
    """A made-up radar in the original's layout (not the game's art): a face, three rings, four spokes, a pip, a beam per
    frame and a cone per locked frame, a panel with an arc of dots, ticks and two rows of marks, two knobs."""
    W, H = 256, 128; cx, cy, R = 98.5, 65.0, 54.0
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    def base():
        a = np.zeros((H, W, 4), np.uint8); a[..., 3] = 255; a[..., :3] = OLIVE
        r = np.hypot(xx - cx, yy - cy)
        a[r < R] = GREEN + (255,)
        for rr in (13.0, 25.5, 40.0): a[(np.abs(r - rr) < 0.55) & (r < R)] = GRID + (255,)
        a[(np.abs(xx - cx) < 0.5) & (r < R)] = GRID + (255,); a[(np.abs(yy - cy) <= 0.5) & (r < R)] = GRID + (255,)
        for s_ in (1, -1): a[(np.abs((xx - cx) - s_ * (yy - cy)) < 0.6) & (r < R)] = GRID + (255,)
        a[64:66, 98:100] = PIP + (255,)
        ri = np.hypot(xx - 150.5, yy - 111.5)
        a[ri < 10.5] = GREEN + (255,)
        for rr in (6.0, 8.0): a[np.abs(ri - rr) < 0.5] = GRID + (255,)
        a[111, 140:162] = GRID + (255,); a[101:122, 150] = GRID + (255,)
        rl = np.hypot(xx - 155.5, yy - 38.5)           # the contact lamp (game view 155.5, 89.5): a dark ring, a green disc
        a[rl < 5.0] = GRID + (255,); a[rl < 3.6] = GREEN + (255,)
        g = a[::-1].copy()                    # the panel drawn in the game's view, then flipped into the file's rows
        for k, (kx, ky) in enumerate(((29.5, 30.5), (29.5, 76.5))):
            rk = np.hypot(xx - kx, yy - ky); g[rk < 7] = (200, 200, 200, 255); g[(rk >= 7) & (rk < 9)] = (16, 16, 16, 255)
            ang = math.radians(-135 if k == 0 else -45)
            for t in np.arange(1.0, 6.0, 0.5):
                g[int(ky + t * math.sin(ang)), int(kx + t * math.cos(ang))] = (16, 16, 16, 255)
        for a_ in np.linspace(-158, -22, 7):
            px_, py_ = 29.6 + 14.6 * math.cos(math.radians(a_)), 28.5 + 14.6 * math.sin(math.radians(a_))
            g[int(py_), int(px_)] = GRID + (255,)
        for x in range(16, 40):
            if x % 3: g[46, x] = GRID + (255,)
        for x in (27, 30, 34, 37): g[90, x] = GRID + (255,)
        g[62, 21] = g[61, 20] = GRID + (255,); g[63, 37] = g[62, 38] = GRID + (255,)
        return g[::-1].copy()
    frames = {}
    b = base()
    for kind in ("sweep", "locked"):
        for i in range(31):
            a = b.copy()
            if i < 30:
                ang = math.radians(frame_angle(i) + (1.0 if kind == "sweep" else 0))
                for t in np.arange(1, 53, 0.25):
                    if kind == "sweep":
                        for o in (-1, 0, 1):
                            x = cx + 0.4 + t * math.cos(ang) - o * math.sin(ang); y = cy + t * math.sin(ang) + o * math.cos(ang)
                            a[int(y), int(x)] = ((99, 189, 99) if o == 0 else (82, 156, 82)) + (255,)
                    else:
                        half = math.radians(6)
                        for o in np.arange(-1, 1.01, 0.1):
                            th = ang + o * half
                            x = cx + 0.4 + t * math.cos(th); y = cy + t * math.sin(th)
                            a[int(y), int(x)] = (82, 156, 82, 255)
            frames[spec["frames"][kind] % i] = a
    return frames


def self_check():
    spec = load_spec(); ok = True
    frames = synthetic(spec)
    g, med = measure(frames, spec)
    def check(name, cond, detail):
        nonlocal ok
        print(("ok   " if cond else "FAIL ") + name + ": " + detail); ok &= bool(cond)
    gc = g["grid"]["centre"]
    check("grid centre", abs(gc[0] - 98.5) < 0.3 and abs(gc[1] - 65.0) < 0.3, str(gc))
    radii = [r["radius"] for r in g["grid"]["rings"]]
    check("ring radii", all(abs(a - b) < 0.4 for a, b in zip(radii, (13.0, 25.5, 40.0))), str(radii))
    check("face radius", abs(g["face"]["radius"] - 54.0) < 0.6, str(g["face"]))
    check("sweep offset", abs(g["sweep"]["offset_deg"] - 1.0) < 0.4, f'{g["sweep"]["offset_deg"]} sd {g["sweep"]["offset_sd"]}')
    check("cone half-angle", 4.5 < g["cone"]["half_angle"] < 7.5, str(g["cone"]["half_angle"]))
    check("inset", abs(g["inset"]["centre"][0] - 150.5) < 0.5 and abs(g["inset"]["centre"][1] - 111.5) < 0.5, str(g["inset"]["centre"]))
    check("arc of dots", g["marks"]["arc"] is not None and len(g["marks"]["arc"]["angles"]) == 7, str(g["marks"]["arc"] and g["marks"]["arc"]["angles"]))
    # a render at 2x over a nearest-neighbour "upscale": registration and untouched outside
    s = 2
    up = {k: np.repeat(np.repeat(v, s, 0), s, 1) for k, v in frames.items()}
    ls = {"name": "marks-only", "texts": [], "blob_groups": ["under_lower"]}
    rd = Redraw(g, spec, "A", ls, s, up[spec["frames"]["sweep"] % 30])
    out = rd.frame(up[spec["frames"]["sweep"] % 7], "sweep", 7)
    check("size", out.shape == up[spec["frames"]["sweep"] % 7].shape, str(out.shape))
    far = np.zeros(out.shape[:2], bool); far[:, 170 * s:] = True
    check("outside untouched", np.array_equal(out[far], up[spec["frames"]["sweep"] % 7][far]), "")
    check("alpha untouched", np.array_equal(out[..., 3], up[spec["frames"]["sweep"] % 7][..., 3]), "")
    # the beam lies along frame 7's angle from the pivot
    pv = g["sweep"]["pivot"]; a = math.radians(frame_angle(7) + g["sweep"]["offset_deg"])
    p = (pv[0] + 30 * math.cos(a), pv[1] + 30 * math.sin(a))
    px_ = out[int(p[1] * s), int(p[0] * s), :3]
    check("beam placed", px_[1] > 150, str(px_))
    q = (pv[0] + 30 * math.cos(a + 0.4), pv[1] + 30 * math.sin(a + 0.4))
    check("face off the beam", tuple(out[int(q[1] * s), int(q[0] * s), :3]) in (GREEN,) or out[int(q[1] * s), int(q[0] * s), 1] < 90, str(out[int(q[1] * s), int(q[0] * s), :3]))
    print("self-check", "passed" if ok else "FAILED")
    return ok


# ------------------------------------------------------------------------------------------------------------- main

def distress_of(name):
    """(level, colours, seed) of tools/hud-redraw/hud.json's wear level name (the damage panel's and counters'), None for 0."""
    if name in (None, "0"): return None
    with open(os.path.join(HERE, "hud-redraw", "hud.json"), encoding="utf-8") as f: d = json.load(f)["distress"]
    if name not in d["levels"]: raise SystemExit(f"no wear level {name!r}: " + ", ".join(d["levels"]))
    if HERE not in sys.path: sys.path.insert(0, HERE)
    return (d["levels"][name], d["colours"], d["seed"])


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", nargs="?", choices=["measure", "render", "pack", "sheet", "fit-labels"])
    ap.add_argument("export", nargs="?"); ap.add_argument("upscale", nargs="?"); ap.add_argument("out", nargs="?")
    ap.add_argument("--spec", default=SPEC)
    ap.add_argument("--scale", type=int, default=None, help="texels a pixel (render: 8; pack: the pack's own)")
    ap.add_argument("--style", default="B", help="A (the original's line and cone) or B (an afterglow; the packs')")
    ap.add_argument("--labels", default="lock-range", help="a label set of the spec (lock-range: the packs')")
    ap.add_argument("--glass", default=None, help="the scope's glass: none, light or medium (the spec's glass.default when not given)")
    ap.add_argument("--out", dest="out_dir", help="pack: write the files here and leave the pack as it is")
    ap.add_argument("--base", help="pack: the upscale's own radar frames (a backup), when the pack's are a redraw already")
    ap.add_argument("--distress", default="0", help="render, pack: the damage panel's wear laid on the unit too (0, light, medium: "
                    "tools/hud-redraw/hud.json's levels); 0, the packs' choice, adds none")
    ap.add_argument("--self-check", action="store_true")
    a = ap.parse_args(argv)
    if a.self_check: return 0 if self_check() else 1
    spec = load_spec(a.spec)
    if a.glass is not None:
        if a.glass != "none" and a.glass not in spec["glass"]["levels"]: ap.error(f"--glass: none or one of {', '.join(spec['glass']['levels'])}")
        spec["glass"]["use"] = a.glass
    distress = distress_of(a.distress)
    if a.cmd == "measure":
        g, _ = measure(load_frames(a.export, spec), spec, a.export)
        txt = json.dumps(strip(g), indent=1)
        if a.upscale: open(a.upscale, "w").write(txt)
        else: print(txt)
        return 0
    if a.cmd == "fit-labels":
        frames = load_frames(a.export, spec); g, med = measure(frames, spec)
        lf = spec["label_fit"]
        base = [l.split("\t")[0].strip() for l in open(os.path.join(HERE, "radar-redraw", lf["baseline"]), encoding="utf-8") if l.strip() and not l.startswith("#")]
        fonts = [os.path.join(REPO_FONTS, f) for f in lf["fonts"]]
        words = sorted({w for w in lf["words"]} | {t["text"] for ls in spec["label_sets"] for t in ls.get("texts", [])})
        res = fit_labels(med, g, spec, words, base, fonts)
        txt = json.dumps(res, indent=1)
        if a.upscale: open(a.upscale, "w").write(txt)
        else: print(txt)
        return 0
    if a.cmd == "render":
        render_all(a.export, a.upscale, a.out, spec, a.scale or 8, a.style, a.labels, distress=distress)
        return 0
    if a.cmd == "pack":
        if not a.export or not a.upscale or a.out: ap.error("pack takes EXPORT_DIR and PACK_DIR (and --out DIR)")
        files, s = pack_step(a.export, a.upscale, a.out_dir, spec, a.spec, a.style, a.labels, a.scale, a.base, distress, a.distress)
        wear = "" if a.distress == "0" else f", wear {a.distress}"
        print(f"radar redrawn at {s}x, style {a.style}, labels {a.labels}{wear}: {len(files)} files into {a.out_dir or a.upscale}")
        return 0
    if a.cmd == "sheet":
        import importlib.util
        sp = importlib.util.spec_from_file_location("radar_sheet", os.path.join(HERE, "radar-redraw", "sheet.py"))
        mod = importlib.util.module_from_spec(sp); sp.loader.exec_module(mod)
        return mod.sheet(sys.modules[__name__], a.export, a.upscale, a.out, spec)
    ap.print_help(); return 2


if __name__ == "__main__":
    sys.exit(main())
