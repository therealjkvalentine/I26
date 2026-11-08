#!/usr/bin/env python3
"""Draw the HD menu art's flat-colour stamps again from the originals, as vector art, CPU only, in place.

The title logo (shell background 26, the main menu: the lettering, the band and the figure on black; a smaller colourway of
it on 14, mission select, over the fire photo) is flat art: a few solid colours with anti-aliased edges and nothing else.
Upscaled by a model it comes out with edges the art never had: the lettering's outlines wobble, the band rings and shows a
doubled inner edge, the small TM mark smears. This step takes the original's few colours, unmixes each anti-aliased texel
into its shares of the two colours it lies between (which places every edge to a fraction of a texel), traces each colour's
region with potrace at the pack's scale (straight edges stay straight, curves stay curves, corners stay corners) and renders
the stamp anti-aliased at that scale, no model. A stamp on a picture (14) keeps the pack's picture round it: the picture
under its rim is read from the texels round it, so the rim is placed as the inner edges are.

    python3 tools/flat-art.py SRC_DIR [--pack DIR] [--only N,N] [--out DIR] [--smooth S] [--alphamax A] [--restore]
    python3 tools/flat-art.py --image FILE --colours "#000000,#ffffff,#ffbd00,#0042ce" --out FILE.png [--box X0,Y0,X1,Y1]
          [--scale 4] [--smooth S] [--alphamax A] [--picture] [--svg FILE.svg]

SRC_DIR holds the records as `i76 --install DIR bg SRC_DIR` writes them (bg<NN>.ppm); --pack the pack folder (default: the
game's user://tex-menu), its shellbg<N>.webp and shellbg.json's affine. STAMPS below says which records carry a stamp, where
and in which colours. The first run copies each file it changes into PACK/.flat-prev/ (a sub-folder the game does not look
in); --restore puts those back. --out writes the pages elsewhere (a review), the pack untouched. pack.json gets a "flat_art"
entry naming the pages done. The second form traces any flat picture (the stamp's colours given; --picture when it lies on
a photo, which is then left transparent). Needs Python 3.10+, numpy, scipy, Pillow, potracer and resvg-py.
"""
import argparse, io, json, os, shutil, sys, time
import numpy as np
from PIL import Image
from scipy import ndimage

# record: the stamp's box in the record's texels (None: the whole picture), its colours (the first is the ground, drawn
# behind), whether it lies on a picture (the ground is then the picture, kept transparent and the pack's own shown)
STAMPS = {
    # the main menu's logo: the lettering keeps a black outline about a texel wide where it crosses the blue band
    26: dict(box=None, colours=("#000000", "#0042ce", "#ffbd00", "#ffffff"), picture=False,
             keyline=dict(colour="#000000", width=0.9, on=("#0042ce",), of=("#ffbd00", "#ffffff")),
             marks=[dict(box=(466, 179, 497, 197), text="TM", colour="#ffbd00")]),
    # mission select's: a yellow ring and lettering edged in teal over the fire photo, the ring's inner edge half a texel
    14: dict(box=(136, 0, 494, 154), colours=("#000000", "#007fb2", "#ffd900", "#ffffff"), picture=True,
             marks=[dict(box=(414, 88, 430, 102), text="TM", colour="#ffd900")]),
}
# a texel within this (RGB, 0-255) of a stamp colour, or of the line between two, belongs to the stamp
NEAR = 48
# a texel within this of a colour is that colour, and the edges round it are between it and its neighbours
PURE = 24
# a hole in a stamp on a picture up to this many texels is the stamp's own (a letter's counter), not the picture
HOLE = 40
# lettering a few texels high (a TM mark) is set again from this face, as high as the mark's ink box
MARK_FONTS = ("ariali.ttf", "Arial Italic.ttf", "arial.ttf", "Arial.ttf")
FONT_DIRS = [os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts"), "/usr/share/fonts/truetype/msttcorefonts",
             "/usr/share/fonts/msttcore", "/usr/share/fonts/corefonts", os.path.expanduser("~/Library/Fonts"),
             "/Library/Fonts", "/System/Library/Fonts/Supplemental"]
# the smoothing of each colour's edge before it is traced, in texels (0: the anti-aliasing alone places it)
SMOOTH = 0.5
# potrace's corner threshold: 0 keeps every corner, 1.334 rounds every one; the lettering's terminals want their corners
ALPHAMAX = 0.75
# the trace runs on a grid of this many pixels a texel: potrace straightens a stair within half a pixel of its grid, and
# the original's own stairs (its anti-aliasing is palette-quantised) are a quarter texel; a finer grid follows them
GRID = 2


def rgb(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


def unmix(img, cols, picture=None, rim=None, local=True):
    """Each texel's share of each colour: coverage (H, W, K), rows summing to 1. A texel between two colours (an anti-
    aliased edge) is placed on the line between them, and only between colours that are themselves next to it (a grey
    between blue and yellow is not a black-and-white one); with `picture` (H, W, 3: the ground under the stamp) the last
    column is the picture's share, and a texel on the `rim` (a mask) may lie between the picture there and a stamp
    colour. Also returns each texel's distance from the line it was placed on."""
    h, w = img.shape[:2]
    P = img.reshape(-1, 3).astype(np.float32)
    C = np.array(cols, np.float32)
    K = len(C) + (1 if picture is not None else 0)
    pairs = [(i, j) for i in range(len(C)) for j in range(i, len(C))]
    pic = len(C) if picture is not None else -1
    if picture is not None:
        pairs += [(i, pic) for i in range(len(C))]
    B = picture.reshape(-1, 3).astype(np.float32) if picture is not None else None
    # which colours are present (a texel within PURE of them) in each texel's 3x3 neighbourhood
    present = [ndimage.binary_dilation(np.sqrt(((P - c) ** 2).sum(-1)).reshape(h, w) < PURE, iterations=1).ravel() for c in C]
    if picture is not None:
        present.append((rim if rim is not None else np.ones((h, w), bool)).ravel())
    W = np.zeros((len(P), K), np.float32)
    best = np.full(len(P), np.inf, np.float32)

    def fit(todo, neighbours):
        for i, j in pairs:
            a = C[i]
            b = B if j == pic else C[j]
            d = b - a
            n = (d * d).sum(-1)
            t = np.clip(((P - a) * d).sum(-1) / np.maximum(n, 1e-6), 0, 1)
            r = ((P - (a + t[:, None] * d)) ** 2).sum(-1)
            if neighbours:
                r = np.where(present[i] & present[j], r, np.inf)
            m = (r < best) & todo
            best[m] = r[m]
            W[m] = 0
            W[m, i] = 1 - t[m]
            W[m, j] += t[m]

    fit(np.ones(len(P), bool), local)
    poor = best > PURE * PURE              # texels no neighbouring pair explains (a line a texel wide has no pure
    if local and poor.any():               # texel of its own beside it) take the nearest pair of any
        best[poor] = np.inf
        fit(poor, False)
    return W.reshape(h, w, K), np.sqrt(best).reshape(h, w)


def stamp_mask(img, cols):
    """The texels that are the stamp: within NEAR of a colour or of the line between two, closed, the largest piece,
    its small holes (a counter the picture does not show through) filled."""
    _, r = unmix(img, cols, local=False)
    m = ndimage.binary_closing(r < NEAR, np.ones((3, 3), bool), iterations=1)
    lab, n = ndimage.label(m)
    if n > 1:
        m = lab == (np.bincount(lab[lab > 0]).argmax())
    holes = ndimage.binary_fill_holes(m) & ~m
    lab, n = ndimage.label(holes)
    if n:
        small = np.bincount(lab.ravel()) <= HOLE
        small[0] = False
        m |= small[lab]
    return m


def ground(img, mask):
    """The picture under the stamp as the texels round it have it: outside the stamp the picture itself, inside it the
    nearest outside texels' colours smoothed in (a normalised blur, repeated until the stamp is covered)."""
    out = img.astype(np.float32).copy()
    outside = ~ndimage.binary_dilation(mask, iterations=1)
    w = outside.astype(np.float32)
    f = out * w[..., None]
    known = outside.copy()
    while not known.all():
        fb = ndimage.gaussian_filter(f, (2, 2, 0))
        wb = ndimage.gaussian_filter(w, 2)
        fill = ~known & (wb > 1e-3)
        out[fill] = fb[fill] / wb[fill][:, None]
        known |= fill
        w = known.astype(np.float32)
        f = out * w[..., None]
    out[outside] = img[outside]
    return out


def trace(field, scale, smooth, alphamax):
    """potrace's curves round the region where `field` (H, W, floats 0-1: a colour's coverage) is over a half, fitted on
    a grid of GRID pixels a texel; returns the SVG path data in the output's pixels, or None for an empty region."""
    h, w = field.shape
    R = GRID
    big = np.array(Image.fromarray(field.astype(np.float32), "F").resize((w * R, h * R), Image.BICUBIC))
    if smooth > 0:
        big = ndimage.gaussian_filter(big, smooth * R)
    bm = big >= 0.5
    if not bm.any():
        return None
    import potrace
    path = potrace.Bitmap(~bm).trace(turdsize=max(2, R * R // 4), alphamax=alphamax, opttolerance=0.2)
    d = []
    f = scale / R
    for curve in path:
        s = curve.start_point
        d.append(f"M{s.x * f:.2f},{s.y * f:.2f}")
        for seg in curve.segments:
            e = seg.end_point
            if seg.is_corner:
                d.append(f"L{seg.c.x * f:.2f},{seg.c.y * f:.2f}L{e.x * f:.2f},{e.y * f:.2f}")
            else:
                d.append(f"C{seg.c1.x * f:.2f},{seg.c1.y * f:.2f} {seg.c2.x * f:.2f},{seg.c2.y * f:.2f} {e.x * f:.2f},{e.y * f:.2f}")
        d.append("Z")
    return "".join(d)


def hexc(c):
    return f"#{int(c[0]):02x}{int(c[1]):02x}{int(c[2]):02x}"


def vectorise(img, cols, scale=4, smooth=SMOOTH, alphamax=ALPHAMAX, picture=False, keyline=None):
    """The stamp in `img` (H, W, 3) drawn again at `scale` as SVG: the colours' regions from the ground up, each layer's
    region its own and every layer's above it (so no hairline shows between two), each traced from its coverage. With
    `picture` the ground is the picture under the stamp, left clear. A `keyline` (colour, width in texels, the colours
    `on` which it shows, the colours `of` whose outline it is) is a line too thin to trace, drawn as a stroke along the
    `of` regions' edges where they meet an `on` region; its texels' share of its colour goes to the `on` colour first.
    Returns (svg, mask) where mask is the stamp's texels (all of them without `picture`)."""
    C = [rgb(c) for c in cols]
    names = list(cols)
    h, w = img.shape[:2]
    if picture:
        # the rim is the texel each side of the mask's edge: those may share with the picture; further in they are
        # the stamp's colours alone, further out the picture's (whose own dark grain would otherwise read as ink)
        mask = stamp_mask(img, C)
        outer = ndimage.binary_dilation(mask, iterations=1)
        rim = outer & ~ndimage.binary_erosion(mask, iterations=1)
        W, _ = unmix(img, C, ground(img, mask), rim)
        W[~outer] = 0
        W[~outer, len(C)] = 1
        order = [len(C)] + list(range(len(C)))          # the picture lowest, then the stamp's colours as given
        names.append("picture")
    else:
        mask = np.ones((h, w), bool)
        W, _ = unmix(img, C)
        order = list(range(len(C)))
    if keyline:
        kc = names.index(keyline["colour"])
        on = [names.index(n) for n in keyline["on"]]
        of = [names.index(n) for n in keyline["of"]]
        near = lambda idx: ndimage.binary_dilation(W[..., idx].sum(-1) > 0.5, iterations=2)
        band = near(on) & near(of) & (W[..., kc] > 0) & (W[..., kc] < 1)
        W[band, on[0]] += W[band, kc]
        W[band, kc] = 0
    layers = []
    for k in range(1, len(order)):
        above = W[..., order[k:]].sum(-1)
        d = trace(above, scale, smooth, alphamax)
        layers.append((order[k], d))
    parts = [f'<svg version="1.1" xmlns="http://www.w3.org/2000/svg" width="{w * scale}" height="{h * scale}" viewBox="0 0 {w * scale} {h * scale}">']
    if not picture:
        parts.append(f'<rect width="{w * scale}" height="{h * scale}" fill="{hexc(C[0])}"/>')
    stroke = None
    if keyline:
        # the stroke, twice the width (its inner half lies under the `of` fills), clipped to where it shows: the `on`
        # layer's region, or the picture (everything outside the stamp)
        ds = [d for i, d in layers if i in of and d]
        if on[0] == len(C):
            d0 = next((d for i, d in layers if d), "")
            clip = f'<clipPath id="k"><path clip-rule="evenodd" d="M0,0H{w * scale}V{h * scale}H0Z{d0}"/></clipPath>'
            at = -1
        else:
            d0 = next((d for i, d in layers if i == on[0]), None) or ""
            clip = f'<clipPath id="k"><path d="{d0}"/></clipPath>'
            at = on[0]
        stroke = (at, clip + '<g clip-path="url(#k)">' + "".join(
            f'<path fill="none" stroke="{hexc(C[kc])}" stroke-width="{2 * keyline["width"] * scale:.2f}" stroke-miterlimit="3" d="{d}"/>' for d in ds) + "</g>")
    if stroke and stroke[0] == -1:
        parts.append(stroke[1])
    for i, d in layers:
        if d:
            parts.append(f'<path fill="{hexc(C[i])}" fill-rule="nonzero" d="{d}"/>')
        if stroke and stroke[0] == i:
            parts.append(stroke[1])
    parts.append("</svg>")
    return "\n".join(parts), mask


def render(svg):
    """The SVG as RGBA pixels at its own size (resvg, anti-aliased)."""
    import resvg_py
    png = resvg_py.svg_to_bytes(svg_string=svg, shape_rendering="geometric_precision")
    return np.array(Image.open(io.BytesIO(png)).convert("RGBA"))


def find_font():
    for d in FONT_DIRS:
        for f in MARK_FONTS:
            p = os.path.join(d, f)
            if os.path.exists(p):
                return p
    return None


def ink_box(img, box, ground_col, colour):
    """Where a mark's ink is within its box: the texels at least a quarter of the way from the ground to its colour."""
    x0, y0, x1, y1 = box
    reg = img[y0:y1, x0:x1].astype(np.float32)
    d = colour - ground_col
    t = ((reg - ground_col) * d).sum(-1) / max((d * d).sum(), 1e-6)
    off = np.sqrt(((reg - (ground_col + t[..., None] * d)) ** 2).sum(-1))     # a picture's own colours are not ink
    ys, xs = np.nonzero((t >= 0.25) & (off < NEAR))
    if len(ys) == 0:
        return None
    return x0 + xs.min(), y0 + ys.min(), x0 + xs.max() + 1, y0 + ys.max() + 1


def set_marks(out, img, marks, ground_col, scale, font=None):
    """Lettering a few texels high is set again from the face (`font`, or the system's Arial Bold Italic), each mark's
    text fitted into its ink box, in its colour, over the picture already drawn."""
    from PIL import ImageDraw, ImageFont
    font = font or find_font()
    if font is None:
        return out
    for mk in marks:
        colour = rgb(mk["colour"])
        ib = ink_box(img, mk["box"], ground_col, colour)
        if ib is None:
            continue
        x0, y0, x1, y1 = ib
        f = ImageFont.truetype(font, 256)
        bb = f.getbbox(mk["text"])
        can = Image.new("L", (bb[2] - bb[0] + 8, bb[3] - bb[1] + 8), 0)
        ImageDraw.Draw(can).text((4 - bb[0], 4 - bb[1]), mk["text"], fill=255, font=f)
        can = can.crop(can.getbbox())
        H = (y1 - y0) * scale
        can = can.resize((max(1, round(can.width * H / can.height)), H), Image.LANCZOS)    # the face's own width
        a = np.array(can).astype(np.float32)[..., None] / 255
        X0, Y0 = ((x0 + x1) * scale - a.shape[1]) // 2, y0 * scale
        if X0 < 0 or X0 + a.shape[1] > out.shape[1] or Y0 + a.shape[0] > out.shape[0]:
            continue
        reg = out[Y0:Y0 + a.shape[0], X0:X0 + a.shape[1]].astype(np.float32)
        reg[..., :3] = colour * a + reg[..., :3] * (1 - a)
        reg[..., 3:] = np.maximum(reg[..., 3:], a * 255)
        out[Y0:Y0 + a.shape[0], X0:X0 + a.shape[1]] = np.clip(reg + 0.5, 0, 255).astype(np.uint8)
    return out


def redraw(img, cols, scale=4, smooth=SMOOTH, alphamax=ALPHAMAX, picture=False, marks=(), font=None, keyline=None):
    """The stamp redrawn at `scale`: RGBA pixels (clear where the picture shows through) and the SVG. `marks` (each a
    box in the picture's texels, a text and a colour) are blanked to the ground before the trace and set from a font
    after it."""
    src = img
    ground_col = rgb(cols[0])
    if marks:
        src = img.copy()
        for mk in marks:
            x0, y0, x1, y1 = mk["box"]
            src[y0:y1, x0:x1] = ground_col.astype(np.uint8)
    svg, _ = vectorise(src, cols, scale, smooth, alphamax, picture, keyline)
    out = render(svg)
    if marks:
        out = set_marks(out, img, marks, ground_col, scale, font)
    return out, svg


def affine_of(pack, rec):
    """The page's file and affine (image = a * frame + b) from shellbg.json, or the plain 4x."""
    path = None
    a = (4.0, 0.0, 4.0, 0.0)
    man = os.path.join(pack, "shellbg.json")
    if os.path.exists(man):
        with open(man, encoding="utf-8") as fh:
            e = json.load(fh).get(str(rec))
        if e:
            path = os.path.join(pack, e["file"])
            a = tuple(float(v) for v in e.get("affine", a))
    if path is None:
        for ext in ("webp", "png", "jpg"):
            p = os.path.join(pack, f"shellbg{rec}.{ext}")
            if os.path.exists(p):
                path = p
                break
    return path, a


def apply(orig, page, a, spec, smooth, alphamax):
    """The stamp drawn again on the pack's page (H, W, 3 at the affine): the whole page for a stamp on a ground, the
    stamp over the page's own picture for one on a picture (its rim anti-aliased onto it)."""
    ax, bx, ay, by = a
    scale = int(round(ax))
    if abs(ax - scale) > 1e-6 or abs(ay - scale) > 1e-6 or bx or by:
        raise SystemExit("flat-art: only a whole-number scale with no offset is handled")
    box = spec["box"] or (0, 0, orig.shape[1], orig.shape[0])
    x0, y0, x1, y1 = box
    crop = orig[y0:y1, x0:x1]
    marks = [dict(mk, box=(mk["box"][0] - x0, mk["box"][1] - y0, mk["box"][2] - x0, mk["box"][3] - y0)) for mk in spec.get("marks", ())]
    out, svg = redraw(crop, spec["colours"], scale, smooth, alphamax, spec["picture"], marks, keyline=spec.get("keyline"))
    page = page.astype(np.float32).copy()
    X0, Y0 = x0 * scale, y0 * scale
    H, W = out.shape[:2]
    al = out[..., 3:4].astype(np.float32) / 255
    page[Y0:Y0 + H, X0:X0 + W] = out[..., :3] * al + page[Y0:Y0 + H, X0:X0 + W] * (1 - al)
    return np.clip(page + 0.5, 0, 255).astype(np.uint8), svg


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("src", nargs="?", help="the records as `i76 bg SRC_DIR` writes them")
    ap.add_argument("--pack", help="the HD menu art (default: the user folder's tex-menu)")
    ap.add_argument("--only", help="records, comma-separated (default: every stamp known)")
    ap.add_argument("--out", help="write here instead of the pack (a file with --image)")
    ap.add_argument("--smooth", type=float, default=SMOOTH, help=f"edge smoothing in texels (default {SMOOTH})")
    ap.add_argument("--alphamax", type=float, default=ALPHAMAX, help=f"potrace's corner threshold (default {ALPHAMAX})")
    ap.add_argument("--restore", action="store_true", help="put the files from PACK/.flat-prev back and quit")
    ap.add_argument("--image", help="trace this picture instead of the pack's records")
    ap.add_argument("--colours", help="the stamp's colours, #rrggbb comma-separated, the ground first (with --image)")
    ap.add_argument("--box", help="X0,Y0,X1,Y1 of the stamp in the picture (with --image)")
    ap.add_argument("--scale", type=int, default=4, help="the output's scale (with --image)")
    ap.add_argument("--picture", action="store_true", help="the stamp lies on a photo: leave the ground clear (with --image)")
    ap.add_argument("--svg", help="also write the vector art here (with --image)")
    args = ap.parse_args()
    if args.image:
        if not (args.colours and args.out):
            raise SystemExit("flat-art: --image needs --colours and --out")
        img = np.array(Image.open(args.image).convert("RGB"))
        if args.box:
            x0, y0, x1, y1 = (int(v) for v in args.box.split(","))
            img = img[y0:y1, x0:x1]
        out, svg = redraw(img, args.colours.split(","), args.scale, args.smooth, args.alphamax, args.picture)
        Image.fromarray(out, "RGBA").save(args.out)
        if args.svg:
            with open(args.svg, "w", encoding="utf-8") as fh:
                fh.write(svg)
        print(f"{args.out}: {out.shape[1]}x{out.shape[0]}")
        return
    pack = args.pack or os.path.join(os.environ.get("APPDATA") or os.path.expanduser("~/.local/share"), "I26", "tex-menu")
    prev = os.path.join(pack, ".flat-prev")
    if args.restore:
        n = 0
        for f in sorted(os.listdir(prev)) if os.path.isdir(prev) else []:
            shutil.copy2(os.path.join(prev, f), os.path.join(pack, f))
            n += 1
        print(f"{n} files put back from {prev}")
        return
    if not args.src:
        raise SystemExit("flat-art: SRC_DIR (or --image) is needed")
    only = {int(v) for v in args.only.split(",")} if args.only else set(STAMPS)
    done = []
    for rec in sorted(STAMPS):
        if rec not in only:
            continue
        src = os.path.join(args.src, f"bg{rec:02d}.ppm")
        path, a = affine_of(pack, rec)
        if not os.path.exists(src) or path is None:
            print(f"{rec:2d}: skipped ({'no original' if not os.path.exists(src) else 'no page in the pack'})")
            continue
        t0 = time.time()
        orig = np.array(Image.open(src).convert("RGB"))
        page = np.array(Image.open(path).convert("RGB"))
        out, _ = apply(orig, page, a, STAMPS[rec], args.smooth, args.alphamax)
        if args.out:
            os.makedirs(args.out, exist_ok=True)
            dst = os.path.join(args.out, os.path.basename(path))
        else:
            os.makedirs(prev, exist_ok=True)
            keep = os.path.join(prev, os.path.basename(path))
            if not os.path.exists(keep):
                shutil.copy2(path, keep)
            dst = path
        # lossless: lossy WebP holds the colour at a quarter of the resolution, which would soften the edges just drawn
        Image.fromarray(out, "RGB").save(dst + ".part", "WEBP", lossless=True, quality=100, method=6)
        os.replace(dst + ".part", dst)
        done.append(rec)
        print(f"{rec:2d}: {os.path.basename(dst)} in {time.time() - t0:.1f}s")
    pj = os.path.join(pack, "pack.json")
    if done and not args.out and os.path.exists(pj):
        with open(pj, encoding="utf-8") as fh:
            d = json.load(fh)
        d["flat_art"] = {"date": time.strftime("%Y-%m-%d"), "tool": f"tools/flat-art.py --smooth {args.smooth} --alphamax {args.alphamax}",
                         "pages": sorted(set(d.get("flat_art", {}).get("pages", [])) | set(done)),
                         "note": "the title logo drawn again as flat vector art from the original (no model); previous files in .flat-prev/"}
        with open(pj, "w", encoding="utf-8") as fh:
            json.dump(d, fh, indent=2)
            fh.write("\n")


if __name__ == "__main__":
    main()
