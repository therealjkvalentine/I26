#!/usr/bin/env python3
"""Make the optional widescreen menu pack (tex-menu-wide) from its masters and the 4:3 menu pack, by the recipe
tools/hd-recipes/menu-wide.json, and check it.

    python tools/menu-wide.py build --pack43 PACK --masters DIR --out DIR [--layers 16x9,21x9] [--only KEYS] [--full DIR]
    python tools/menu-wide.py verify DIR --pack43 PACK [--full DIR]
    python tools/menu-wide.py regenerate KEY --pack43 PACK --masters DIR [--layer 16x9] [--variants 3] [--inner FILE]
    python tools/menu-wide.py compose DIR --pack43 PACK --key KEY --layer L --png OUT
    python tools/menu-wide.py list

PACK is the 4:3 menu pack the wide pages were made against: a tex-menu folder, or a release zip such as
i76-hd-menus-i26-4x3-2026.11.23.zip (its tex-menu/ folder); --pack43 again for the Nitro pages' folder when it is not
inside the first (a file is read from the first that has it). DIR (--masters) holds the masters: the image model's own
outputs, one per page and layer (the recipe's "master" paths, checked against their SHA-256). They are NOT in this
repository (they are game art extended by a model) and they cannot be made again byte for byte: the models have no seed and
change under the same name. The pack's release keeps them as its source; this tool makes the pack from them exactly the
same way every time (the model output is aligned, its sides re-upscaled through the 4:3 pack's own upscaler and joined).

What a page's recipe can say ("class"):
  model    the sides are the image model's (method "model"): the 4:3 page (or the next narrower layer's whole page) put
           on a canvas of the model's own output ratio with a placeholder where the new parts go, the recipe's prompt
           sent with it (regenerate), and the master it returned aligned back onto the page (a template match, then an
           ECC affine refine: sub-pixel; the affine found is recorded per page so a build does not depend on it being
           found again) and warped once into the layer's frame. Then the seam treatment S4: a local optical flow (DIS)
           between the model's own drawing of the page and the page at each join, faded to zero at the outer edge; the
           side (plus CONTEXT px of the page) taken to the art's own size (/4, box) and up again through the 4:3 pack's
           upscaler (4xTextures_GTAV_rgt-s_dither, the same tiles), so the sides have the pack's grain; then a Poisson
           membrane: the side plus the harmonic function that equals (the page's edge - the model's drawing of it) at the
           join (Neumann elsewhere, solved by a DCT along y). The inner page is never touched: the join is exactly the
           inner page's edge and the tool asserts every inner pixel equal.
  borrow   the page is printed on the same art as another ("from"): its sides are that page's, with the membrane
           fitted again to this page's own edge (or the very same file when the two edges are equal within EQUAL levels).
  pad      the page's edges are one colour (black, or the credits' bottom line): no file; the game repeats each row's
           outer columns (the 4:3 pack's own pixels) out to the layer's width.
  skip     no wide version (the recipe's "why").

The pack (DIR/tex-menu-wide): wide.json (the layers, and per record the strip file of each layer, or pad, and the 4:3 page's
edge fingerprint), 16x9/shellbg<N>.webp and 16x9/nitro/shellbg<N>.webp (each strip file is the left side then the right
side, side by side, 1920 tall), the same under 21x9/ for the ultrawide layer (its sides go outside the 16:9 ones), pack.json,
ATTRIBUTION.txt, FILES.sha256. The game composes a wide page at load: the 4:3 pack's page in the middle, the strips beside
it, only when the 4:3 page's edges match the fingerprint (another 4:3 pack's page is shown at 4:3).

regenerate calls the model again with the recorded prompt and canvas (GEMINI_API_KEY in the environment; nothing else is
read), writing new variants beside the masters; it never changes the recipe or the pack: look at them, then point the
page's "master" at the one you want (and drop its "affine", so the next build aligns it).

Needs Python 3.10+, numpy, Pillow (with WebP), opencv-python-headless, scipy, and for the upscale torch and spandrel.
"""
import argparse, datetime, hashlib, importlib.util, io, json, os, sys, time, zipfile
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
RECIPE = os.path.join(HERE, "hd-recipes", "menu-wide.json")
INNER = {"4x3": (2560, 1920)}            # the 4:3 pack's pages (4 texels per frame pixel)
WORK_H = 1152                             # the model canvas's height (the model's own output is larger: 2K)
REGION_MULT = 4                           # the upscaler's scale: side regions are cut in whole native texels


def load_tool(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
    return mod


def cv():
    import cv2
    return cv2


def sha256_bytes(b): return hashlib.sha256(b).hexdigest()


def sha256_file(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def read_recipe(path):
    with open(path, encoding="utf-8") as f: return json.load(f)


# ---- the recipe's geometry ----

def layer_info(recipe, layer):
    """(width, height, side, over) of a layer: side = the px added on each side of the layer it goes over."""
    L = recipe["layers"][layer]; over = L["over"]
    inner_w = INNER[over][0] if over in INNER else recipe["layers"][over]["width"]
    side = (L["width"] - inner_w) // 2
    assert inner_w + 2 * side == L["width"], f"{layer}: width {L['width']} - {inner_w} is odd"
    return L["width"], recipe["height"], side, over


def layer_order(recipe, layers):
    """The layers asked for, each after the one it goes over."""
    out = []
    def add(l):
        if l in out or l in INNER: return
        add(recipe["layers"][l]["over"]); out.append(l)
    for l in layers: add(l)
    return out


def page_rel(key):
    """'shellbg6' -> 'shellbg6.webp', 'nitro/shellbg2' -> 'nitro/shellbg2.webp' (the 4:3 pack's names)."""
    return key + ".webp"


def manifest_key(key):
    """('pages' or 'nitro', record number) for a page key."""
    nitro = key.startswith("nitro/")
    return ("nitro" if nitro else "pages"), int(key.split("shellbg")[-1])


# ---- the 4:3 pack ----

class Pack43:
    """The 4:3 menu pack: one or more tex-menu folders or zips holding one (its tex-menu/ folder, or one folder down), read
    only, a file from the first that has it (the release zip and the Nitro pages' nitro/ folder built beside it)."""
    def __init__(self, paths):
        paths = [paths] if isinstance(paths, str) else list(paths)
        self.parts = [_Pack43One(p) for p in paths]; self.path = " + ".join(paths)

    def read(self, rel):
        for p in self.parts:
            b = p.read(rel)
            if b is not None: return b
        return None

    def page(self, key):
        b = self.read(page_rel(key))
        if b is None: sys.exit(f"{self.path}: no {page_rel(key)}")
        return np.array(Image.open(io.BytesIO(b)).convert("RGB")), b

    def info(self):
        b = self.parts[0].read("pack.json")
        return json.loads(b) if b else {}


class _Pack43One:
    def __init__(self, path):
        self.path = path; self.zip = None; self.root = ""
        if os.path.isfile(path):
            self.zip = zipfile.ZipFile(path)
            names = self.zip.namelist()
            roots = sorted({n[:n.lower().index("tex-menu/") + 9] for n in names if "tex-menu/" in n.lower()}, key=len)
            if not roots: sys.exit(f"{path}: no tex-menu/ folder in the zip")
            self.root = roots[0]
        elif not os.path.isdir(path): sys.exit(f"{path}: not a folder or zip")

    def read(self, rel):
        if self.zip is not None:
            try: return self.zip.read(self.root + rel)
            except KeyError: return None
        p = os.path.join(self.path, rel)
        return open(p, "rb").read() if os.path.exists(p) else None


# ---- the model's canvas ----

# the model's own output size for an aspect and size (measured: the 2K "16:9" is 2752x1536 = 1.7917:1, not 16:9)
MODEL_OUT = {
    ("gemini-3-pro-image", "16:9", "2K"): (2752, 1536),
    ("gemini-3-pro-image", "21:9", "2K"): (3168, 1344),
}


def canvas_geom(inner_wh, target_w, out_wh, work_h=WORK_H):
    """Where the inner picture sits on the model's canvas (the model's own output ratio, work_h tall): cw, ch, x0, y0, iw,
    ih. When the canvas is at least as wide as the target the inner fills its height; when it is narrower (21:9 at 2K is
    2.357:1, the 3440x1440 target 2.389:1) the inner is made smaller so the target's width fits, with thin bands above
    and below for the model to fill too (cropped away after)."""
    W, H = inner_wh; ow, oh = out_wh
    cw = int(round(work_h * ow / oh)); ch = work_h
    r_t = target_w / H; r_c = cw / ch
    ih = ch if r_c >= r_t else int(round(ch * r_c / r_t))
    iw = int(round(ih * W / H))
    return dict(cw=cw, ch=ch, x0=(cw - iw) // 2, y0=(ch - ih) // 2, iw=iw, ih=ih)


def mirror_fill(a, side, which):
    """A side made of the inner picture's edge mirrored, blurred more with distance from the join (round 2's placeholder)."""
    a = a.astype(np.float32)
    near = a[:, :side] if which == "L" else a[:, -side:][:, ::-1]
    levels = [0, 4, 12, 28, 56]
    stack = np.stack([np.asarray(Image.fromarray(near.astype(np.uint8)).filter(ImageFilter.GaussianBlur(r))).astype(np.float32)
                      if r else near for r in levels])
    d = np.arange(side, dtype=np.float32) / max(side - 1, 1) * (len(levels) - 1)
    i0 = np.floor(d).astype(int); i1 = np.minimum(i0 + 1, len(levels) - 1); t = d - i0
    out = np.empty_like(near)
    for x in range(side): out[:, x] = stack[i0[x], :, x] * (1 - t[x]) + stack[i1[x], :, x] * t[x]
    if which == "L": out = out[:, ::-1]
    return out.astype(np.uint8)


def make_canvas(inner, g, kind):
    """The canvas sent to the model: the inner picture (Lanczos to iw x ih) with placeholders round it.
    kind "mirror": the edge mirrored and blurred (P2); "telea": the edge colours carried outward with no structure (P3)."""
    small = np.asarray(Image.fromarray(inner).resize((g["iw"], g["ih"]), Image.LANCZOS))
    cw, ch, x0, y0, iw, ih = g["cw"], g["ch"], g["x0"], g["y0"], g["iw"], g["ih"]
    if kind == "mirror":
        L = mirror_fill(small, x0, "L"); R = mirror_fill(small, cw - x0 - iw, "R")
        bl = lambda s: np.asarray(Image.fromarray(s).filter(ImageFilter.GaussianBlur(24)))
        row = np.concatenate([bl(L), small, bl(R)], 1)
        if y0 or ch - y0 - ih:
            top = np.asarray(Image.fromarray(np.ascontiguousarray(row[:max(y0, 1)][::-1])).filter(ImageFilter.GaussianBlur(24)))[:y0]
            bot = np.asarray(Image.fromarray(np.ascontiguousarray(row[-max(ch - y0 - ih, 1):][::-1])).filter(ImageFilter.GaussianBlur(24)))[:ch - y0 - ih]
            row = np.concatenate([top, row, bot], 0)
        return row
    if kind == "telea":
        c = cv()
        can = np.zeros((ch, cw, 3), np.uint8); can[y0:y0 + ih, x0:x0 + iw] = small
        m = np.full((ch, cw), 255, np.uint8); m[y0:y0 + ih, x0:x0 + iw] = 0
        sm = c.resize(can, (cw // 4, ch // 4), interpolation=c.INTER_AREA); ms = c.resize(m, (cw // 4, ch // 4), interpolation=c.INTER_NEAREST)
        inp = c.inpaint(sm, ms, 12, c.INPAINT_TELEA)
        inp = c.GaussianBlur(c.resize(inp, (cw, ch), interpolation=c.INTER_CUBIC), (0, 0), 20)
        return np.where(m[..., None] > 0, inp, can)
    raise ValueError(kind)


def gemini(model, prompt, image, aspect, size, retries=3):
    """One call of the Gemini image model: (picture or None, note, usage). The key from GEMINI_API_KEY only."""
    import base64, requests
    key = os.environ.get("GEMINI_API_KEY")
    if not key: sys.exit("GEMINI_API_KEY is not set")
    b = io.BytesIO(); Image.fromarray(image).save(b, "PNG")
    body = {"contents": [{"role": "user", "parts": [{"inline_data": {"mime_type": "image/png", "data": base64.b64encode(b.getvalue()).decode()}}, {"text": prompt}]}],
            "generationConfig": {"responseModalities": ["IMAGE", "TEXT"], "imageConfig": {"aspectRatio": aspect, "imageSize": size}}}
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"
    note = ""
    for k in range(retries):
        try: r = requests.post(url, headers={"x-goog-api-key": key}, json=body, timeout=300)
        except requests.RequestException as e: note = "network " + type(e).__name__; time.sleep(5); continue
        if r.status_code in (401, 403): sys.exit(f"{model}: HTTP {r.status_code} (the key was refused)")
        if r.status_code == 429:
            if "per day" in r.text.lower(): sys.exit(f"{model}: daily quota reached")
            note = "429"; time.sleep(20 * (k + 1)); continue
        if r.status_code >= 500: note = f"HTTP {r.status_code}"; time.sleep(20 * (k + 1)); continue
        if not r.ok: return None, f"HTTP {r.status_code}: {r.text[:300]}", {}
        j = r.json(); usage = j.get("usageMetadata", {}); cands = j.get("candidates") or []
        if not cands: return None, "blocked: " + json.dumps(j.get("promptFeedback", {}))[:200], usage
        pic = None; text = ""
        for p in cands[0].get("content", {}).get("parts", []):
            d = p.get("inlineData") or p.get("inline_data")
            if d and not p.get("thought"): pic = np.array(Image.open(io.BytesIO(base64.b64decode(d["data"]))).convert("RGB"))
            text += p.get("text", "")
        if pic is not None: return pic, "ok", usage
        return None, f"no picture ({cands[0].get('finishReason')}): {text[:200]}", usage
    return None, "gave up: " + note, {}


# ---- alignment: the master back onto the canvas ----

def _grey(a):
    c = cv(); return c.cvtColor(a, c.COLOR_RGB2GRAY)


def find_scale(O, T, tx0, ty0, expect):
    """Where the template T (cut from the canvas at tx0, ty0) is in the output O, over scales round `expect` (canvas px per
    output px): (scale, (ox, oy), ncc) with canvas = scale * output + (ox, oy). A coarse search at 1/4, then a fine one."""
    c = cv(); h, w = O.shape[:2]; best = (-2, None)
    def match(a, ds):
        s = a / ds; nw, nh = int(round(w * s)), int(round(h * s))
        im = c.resize(O, (nw, nh), interpolation=c.INTER_AREA if s < 1 else c.INTER_CUBIC)
        t = c.resize(T, (int(round(T.shape[1] / ds)), int(round(T.shape[0] / ds))), interpolation=c.INTER_AREA)
        if t.shape[0] > im.shape[0] or t.shape[1] > im.shape[1]: return -2, None
        r = c.matchTemplate(_grey(im), _grey(t), c.TM_CCOEFF_NORMED)
        _, mx, _, loc = c.minMaxLoc(r)
        return mx, (loc[0] * ds, loc[1] * ds)
    for f in np.linspace(0.8, 1.25, 46):
        sc, loc = match(expect * f, 4)
        if sc > best[0]: best = (sc, (expect * f, loc))
    a0 = best[1][0]
    for f in np.linspace(0.975, 1.025, 11):
        sc, loc = match(a0 * f, 1)
        if sc > best[0] - 1e-9 and loc is not None: best = (sc, (a0 * f, loc))
    sc, (a, loc) = best
    return a, (tx0 - loc[0], ty0 - loc[1]), sc


def align(O, inner, g):
    """The affine (2x3) taking the model's output to the canvas, sub-pixel: the scale and offset by template matching, then
    a full affine refined by ECC over the inner picture. Returns (A, info)."""
    c = cv()
    small = np.asarray(Image.fromarray(inner).resize((g["iw"], g["ih"]), Image.LANCZOS))
    mx, my = int(g["iw"] * .05), int(g["ih"] * .05)
    a, off, ncc = find_scale(O, small[my:g["ih"] - my, mx:g["iw"] - mx], g["x0"] + mx, g["y0"] + my, g["ch"] / O.shape[0])
    A = np.array([[a, 0, off[0]], [0, a, off[1]]], np.float64)
    can = c.warpAffine(O, A, (g["cw"], g["ch"]), flags=c.INTER_AREA if a < 1 else c.INTER_CUBIC, borderMode=c.BORDER_REPLICATE)
    g1 = _grey(small).astype(np.float32); g2 = _grey(can[g["y0"]:g["y0"] + g["ih"], g["x0"]:g["x0"] + g["iw"]]).astype(np.float32)
    W = np.eye(2, 3, dtype=np.float32); ecc = True
    try:
        _, W = c.findTransformECC(g1, g2, W, c.MOTION_AFFINE, (c.TERM_CRITERIA_EPS | c.TERM_CRITERIA_COUNT, 300, 1e-7), None, 5)
        S = np.array([[1, 0, g["x0"]], [0, 1, g["y0"]], [0, 0, 1]], np.float64)
        M = S @ np.linalg.inv(np.vstack([W.astype(np.float64), [0, 0, 1]])) @ np.linalg.inv(S) @ np.vstack([A, [0, 0, 1]])
        A = M[:2]
    except c.error:
        ecc = False
    return A, dict(scale=round(float(a), 5), ncc=round(float(ncc), 3), ecc=ecc)


def to_frame(A, g, side, height):
    """The affine taking the model's output straight into the layer's frame (the inner page at x = side, 1:1)."""
    K = height / g["ih"]
    F = np.array([[K, 0, side - g["x0"] * K], [0, K, -g["y0"] * K], [0, 0, 1]])
    return (F @ np.vstack([np.asarray(A, np.float64), [0, 0, 1]]))[:2]


# ---- the seam (S4) ----

def local_flow(inner, can, side, p):
    """Displacement for the sides: DIS optical flow between the inner page and the model's drawing of it next to each join
    (the median of the third of the band nearest the join, smoothed along y), carried into the side fading linearly to zero
    at the outer edge. (fx, fy) over the whole canvas, zero over the inner page."""
    c = cv(); h, w = can.shape[:2]; cw = inner.shape[1]
    band = max(p["band_min"], side // p["band_div"])
    fx = np.zeros((h, w), np.float32); fy = np.zeros((h, w), np.float32)
    dis = c.DISOpticalFlow_create(c.DISOPTICAL_FLOW_PRESET_MEDIUM)
    for which in "LR":
        o, m = (inner[:, :band], can[:, side:side + band]) if which == "L" else (inner[:, cw - band:], can[:, side + cw - band:side + cw])
        fl = dis.calc(_grey(np.ascontiguousarray(o)), _grey(np.ascontiguousarray(m)), None)
        f = np.median(fl[:, :band // 3] if which == "L" else fl[:, -band // 3:], 1)
        f = np.stack([c.GaussianBlur(f[:, k].reshape(-1, 1), (1, 0), 0.1, sigmaY=max(2, h / p["sigma_rows"])).ravel() for k in range(2)], 1)
        lim = p["clip"] * h / WORK_H
        f = np.clip(f, -lim, lim)
        fade = np.clip(1 - np.arange(side, dtype=np.float32) / side, 0, 1)
        sl = slice(0, side) if which == "L" else slice(side + cw, w)
        if which == "L": fade = fade[::-1]
        fx[:, sl] = f[:, :1] * fade[None]; fy[:, sl] = f[:, 1:] * fade[None]
    return fx, fy


def apply_flow(can, fx, fy):
    c = cv(); h, w = can.shape[:2]
    gx, gy = np.meshgrid(np.arange(w, dtype=np.float32), np.arange(h, dtype=np.float32))
    return c.remap(can, gx + fx, gy + fy, c.INTER_LANCZOS4, borderMode=c.BORDER_REPLICATE)


def membrane(side_arr, d, which):
    """side + c, c harmonic over the side, c = d(y) at the join column, Neumann at the top, bottom and outer edge: exact by
    a DCT along y (each frequency decays as cosh into the side; low frequencies spread far, fine detail a few px)."""
    from scipy.fft import dct, idct
    s = side_arr.astype(np.float64); h, w = s.shape[:2]
    D = dct(d, type=2, axis=0, norm="ortho")
    k = np.arange(h); lam = (2 * np.sin(np.pi * k / (2 * h))) ** 2
    mu = np.arccosh(1 + lam / 2)
    xs = np.arange(w)[::-1] if which == "L" else np.arange(w)
    prof = np.cosh(np.minimum(mu[:, None] * (w - xs[None, :]), 700)) / np.cosh(np.minimum(mu[:, None] * w, 700))
    C = np.stack([idct(D[:, ch:ch + 1] * prof, type=2, axis=0, norm="ortho") for ch in range(3)], -1)
    return np.clip(s + C + .5, 0, 255).astype(np.uint8)


def edge_residual(edge_inner, edge_drawn, sig):
    """d(y): the inner page's edge columns minus the drawing's (averaged over the columns, smoothed along y)."""
    c = cv()
    d = (edge_inner.astype(np.float64) - edge_drawn.astype(np.float64)).mean(1)
    return c.GaussianBlur(d.reshape(-1, 1, 3), (1, 0), 0.1, sigmaY=sig).reshape(-1, 3) if sig else d


def join(inner, L, R):
    """The wide page: L, the inner page untouched, R (asserted)."""
    out = np.concatenate([L, inner, R], 1)
    assert np.array_equal(out[:, L.shape[1]:L.shape[1] + inner.shape[1]], inner), "the inner page changed"
    return out


def pad_sides(inner, side, cols):
    """A pad page's sides: each row the mean of its outer `cols` columns, repeated (as the game does)."""
    L = np.repeat(np.round(inner[:, :cols].astype(np.float32).mean(1, keepdims=True)).astype(np.uint8), side, 1)
    R = np.repeat(np.round(inner[:, -cols:].astype(np.float32).mean(1, keepdims=True)).astype(np.uint8), side, 1)
    return L, R


def fingerprint(page, cols, bands):
    """The 4:3 page's edges as the game checks them: per side the mean RGB of the outer `cols` columns in `bands` row bands."""
    h = page.shape[0]; out = {}
    for which, sl in (("L", slice(0, cols)), ("R", slice(page.shape[1] - cols, page.shape[1]))):
        e = page[:, sl].astype(np.float64)
        out[which] = [[int(round(v)) for v in e[b * h // bands:(b + 1) * h // bands].reshape(-1, 3).mean(0)] for b in range(bands)]
    return out


def fingerprint_ok(fp, page, cols, bands, tol):
    got = fingerprint(page, cols, bands)
    return max(abs(a - b) for s in "LR" for ra, rb in zip(fp[s], got[s]) for a, b in zip(ra, rb)) <= tol


# ---- the upscaler ----

class Upscaler:
    """The 4:3 pack's own model (menu-pack.py's Model: tiles with reflected context), its file found and checked as the menu
    pack's are; a cache in WORK keyed by the region's pixels and the settings, and the shared GPU lock (I26_GPU_LOCK) held
    only while a batch runs."""
    def __init__(self, recipe, models_dir, device, work):
        self.r = recipe; self.models_dir = models_dir; self.device = device; self.work = work; self.m = None
        os.makedirs(work, exist_ok=True)

    def key(self, region):
        u = self.r["upscale"]
        return hashlib.sha256(json.dumps(u, sort_keys=True).encode() + str(region.shape).encode() + region.tobytes()).hexdigest()[:20]

    def cached(self, region):
        p = os.path.join(self.work, self.key(region) + ".png")
        return np.array(Image.open(p).convert("RGB")) if os.path.exists(p) else None

    def run(self, regions):
        """regions: list of native-size arrays; returns their 4x, from the cache or the model (one lock for the batch)."""
        out = [self.cached(r) for r in regions]
        pending = [i for i, o in enumerate(out) if o is None]
        if not pending: return out
        mp = load_tool("menu_pack", "menu-pack.py")
        u = self.r["upscale"]
        pins = json.load(open(os.path.join(HERE, "hd-recipes", "models.json"), encoding="utf-8"))["models"]
        file = next(f for f, p in pins.items() if p["model"] == u["model"])
        path = mp.model_path({"models": {u["model"]: dict(pins[file], file=file)}}, u["model"], self.models_dir)
        lock = os.environ.get("I26_GPU_LOCK")
        if lock:
            while os.path.exists(lock): print(f"waiting for {lock}", flush=True); time.sleep(20)
            open(lock, "w").write(f"menu-wide.py {os.getpid()} since {time.ctime()}\n")
        try:
            if self.m is None:
                self.m = mp.Model(path, self.device, u["tile"], u["pad"], u.get("precision", "float32"))
                print(f"upscaler {u['model']} on {self.m.device}", flush=True)
            for i in pending:
                t = time.time(); out[i] = self.m.rgb(regions[i])
                Image.fromarray(out[i]).save(os.path.join(self.work, self.key(regions[i]) + ".png"))
                print(f"  upscaled a side region {regions[i].shape[1]}x{regions[i].shape[0]} in {time.time() - t:.1f}s", flush=True)
        finally:
            if lock and os.path.exists(lock): os.remove(lock)
            if self.m is not None and self.m.device == "cuda":
                self.m = None
                import torch; torch.cuda.empty_cache()
        return out


# ---- one page, one layer ----

def region_width(recipe, side):
    """The side region the upscaler redraws: the side and at least CONTEXT px of the inner page, whole native texels."""
    return -(-(side + recipe["upscale"]["context"]) // REGION_MULT) * REGION_MULT


def model_sides_prepare(recipe, spec, inner, layer, masters):
    """The master warped into the layer's frame and flow-corrected; the side regions to upscale. (canf, regions, info)"""
    c = cv(); W, H, side, _ = layer_info(recipe, layer)
    mpath = os.path.join(masters, spec["master"])
    if not os.path.exists(mpath): sys.exit(f"{mpath}: master missing")
    got = sha256_file(mpath)
    if spec.get("master_sha256") and got != spec["master_sha256"]: sys.exit(f"{mpath}: SHA-256 {got}, the recipe wants {spec['master_sha256']}")
    O = np.array(Image.open(mpath).convert("RGB"))
    g = canvas_geom((inner.shape[1], inner.shape[0]), W, MODEL_OUT[(spec["model"], spec["aspect"], spec["size"])])
    info = {}
    if spec.get("affine"): A = np.array(spec["affine"], np.float64)
    else: A, info = align(O, inner, g)
    info["affine"] = np.round(A, 12).tolist()
    M = to_frame(A, g, side, H)
    can = c.warpAffine(O, M, (W, H), flags=c.INTER_LANCZOS4, borderMode=c.BORDER_REPLICATE)
    info["drift"] = round(float(np.abs(can[:, side:side + inner.shape[1]].astype(int) - inner.astype(int)).mean()), 2)
    fx, fy = local_flow(inner, can, side, recipe["seam"]["flow"])
    canf = apply_flow(can, fx, fy)
    reg = region_width(recipe, side)
    assert H % REGION_MULT == 0, "the height must be whole native texels"
    nat = lambda a: np.asarray(Image.fromarray(np.ascontiguousarray(a)).resize((reg // REGION_MULT, H // REGION_MULT), Image.BOX))
    return canf, [nat(canf[:, :reg]), nat(canf[:, W - reg:])], info


def membrane_params(recipe, spec):
    """The membrane's settings: the recipe's, with the page's own "membrane" over them. cols: how many of the inner page's
    columns the correction is fitted to; inset: how far inside the page they start (a page whose outermost texels are a thin
    border of another colour is fitted to the paper inside it, so the border does not tint the whole side)."""
    m = dict(recipe["seam"]["membrane"]); m.update(spec.get("membrane", {}) if spec else {})
    m.setdefault("inset", 0); return m


def _edge_cols(a, x0, cw, which, m):
    """The columns of a picture `a` that hold the inner page's edge (the inner page at x0, cw wide)."""
    k, i = m["cols"], m["inset"]
    return a[:, x0 + i:x0 + i + k] if which == "L" else a[:, x0 + cw - i - k:x0 + cw - i]


def model_sides_finish(recipe, inner, layer, canf, ups, m=None):
    W, H, side, _ = layer_info(recipe, layer); reg = region_width(recipe, side); cw = inner.shape[1]
    nat = canf.copy(); nat[:, :reg] = ups[0]; nat[:, W - reg:] = ups[1]
    m = m or membrane_params(recipe, None)
    L = membrane(nat[:, :side], edge_residual(_edge_cols(inner, 0, cw, "L", m), _edge_cols(nat, side, cw, "L", m), m["sigma"]), "L")
    R = membrane(nat[:, side + cw:], edge_residual(_edge_cols(inner, 0, cw, "R", m), _edge_cols(nat, side, cw, "R", m), m["sigma"]), "R")
    return L, R


def borrow_sides(recipe, inner, donor_inner, donor_L, donor_R, m=None):
    """A borrowing page's sides: the donor's, the membrane fitted to this page's edge (the donor's sides continue the donor's
    edge, so the correction is this edge minus the donor's)."""
    m = m or membrane_params(recipe, None); cw = inner.shape[1]
    L = membrane(donor_L, edge_residual(_edge_cols(inner, 0, cw, "L", m), _edge_cols(donor_inner, 0, cw, "L", m), m["sigma"]), "L")
    R = membrane(donor_R, edge_residual(_edge_cols(inner, 0, cw, "R", m), _edge_cols(donor_inner, 0, cw, "R", m), m["sigma"]), "R")
    return L, R


def edges_equal(a, b, tol):
    """The two pages' outer columns the same within tol levels (mean over the outer 4 columns, every row band of 16)."""
    return max(np.abs(a[:, s].astype(np.float32) - b[:, s].astype(np.float32)).reshape(16, -1, 3).mean(1).max()
               for s in (slice(0, 4), slice(-4, None))) <= tol


def save_webp(arr, path, enc):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(arr).save(path + ".part", "WEBP", quality=enc["quality"], method=enc["method"])
    os.replace(path + ".part", path)


def decode(b): return np.array(Image.open(io.BytesIO(b)).convert("RGB"))


# ---- build ----

def select(recipe, only):
    keys = list(recipe["pages"])
    if not only: return keys
    want = set(only.split(","))
    # a borrowing page needs its donor
    for k in list(want):
        s = recipe["pages"].get(k)
        if s is None: sys.exit(f"{k}: not in the recipe")
        if s["class"] == "borrow": want.add(s["from"])
    return [k for k in keys if k in want]


def build(args):
    recipe = read_recipe(args.recipe); pack = Pack43(args.pack43)
    layers = layer_order(recipe, args.layers.split(","))
    out = os.path.join(args.out, "tex-menu-wide"); os.makedirs(out, exist_ok=True)
    enc = recipe["encoding"]; fpp = recipe["fingerprint"]
    up = Upscaler(recipe, args.models_dir, args.device, os.path.join(args.work or os.path.join(args.out, "work"), "upscale"))
    keys = select(recipe, args.only)
    old = {}
    mp = os.path.join(out, "wide.json")
    if os.path.exists(mp) and args.only: old = json.load(open(mp, encoding="utf-8"))
    man = {"height": recipe["height"], "layers": {}, "pages": dict(old.get("pages", {})), "nitro": dict(old.get("nitro", {}))}
    found = {}
    inners = {}                       # (key, layer) -> the page the layer goes over (the 4:3 page, or the composed narrower layer)
    sides = {}                        # (key, layer) -> (L, R)
    for k in keys:
        spec = recipe["pages"][k]
        if spec["class"] == "skip": continue
        page, raw = pack.page(k)
        if page.shape[:2] != INNER["4x3"][::-1]: sys.exit(f"{k}: {page.shape[1]}x{page.shape[0]}, not the 4:3 pack's 2560x1920")
        inners[(k, "4x3")] = page
        sect, rec = manifest_key(k)
        e = {"edge": fingerprint(page, fpp["cols"], fpp["bands"]), "made_for": sha256_bytes(raw)[:16]}
        if spec["class"] == "pad": e["pad"] = True
        man[sect][str(rec)] = e
    for layer in layers:
        W, H, side, over = layer_info(recipe, layer)
        man["layers"][layer] = {"width": W, "side": side, "over": over}
        pending = []                     # model pages: prepare all, upscale in one batch, then finish
        for k in keys:
            spec = recipe["pages"][k]
            if spec["class"] != "model": continue
            ls = spec["layers"].get(layer)
            if ls is None: continue
            inner = inners[(k, over)]
            canf, regions, info = model_sides_prepare(recipe, ls, inner, layer, args.masters)
            found[(k, layer)] = info
            pending.append((k, inner, canf, regions))
            print(f"{k} {layer}: aligned (drift {info['drift']}{', ncc ' + str(info['ncc']) if 'ncc' in info else ''})", flush=True)
        ups = up.run([r for t in pending for r in t[3]])
        for i, (k, inner, canf, _) in enumerate(pending):
            sides[(k, layer)] = model_sides_finish(recipe, inner, layer, canf, ups[2 * i:2 * i + 2], membrane_params(recipe, recipe["pages"][k]))
        for k in keys:
            spec = recipe["pages"][k]
            if spec["class"] == "borrow":
                d = spec["from"]
                if (d, layer) not in sides: sys.exit(f"{k}: borrows from {d}, which has no {layer} sides")
                inner = inners[(k, over)]; dinner = inners[(d, over)]
                if edges_equal(inner, dinner, recipe["borrow"]["equal"]):
                    sides[(k, layer)] = sides[(d, layer)]; spec["_same_file"] = True
                else:
                    sides[(k, layer)] = borrow_sides(recipe, inner, dinner, *sides[(d, layer)], m=membrane_params(recipe, spec))
            elif spec["class"] == "pad":
                sides[(k, layer)] = pad_sides(inners[(k, over)], side, recipe["pad"]["cols"])
        for k in keys:
            spec = recipe["pages"][k]
            if (k, layer) not in sides: continue
            L, R = sides[(k, layer)]
            full = join(inners[(k, over)], L, R)
            inners[(k, layer)] = full
            sect, rec = manifest_key(k)
            if spec["class"] == "pad": continue
            rel = f"{layer}/{k}.webp"
            if spec.get("_same_file"):
                rel = f"{layer}/{spec['from']}.webp"
            else:
                save_webp(np.concatenate([L, R], 1), os.path.join(out, rel), enc)
            man[sect][str(rec)][layer] = rel
            if args.full:
                os.makedirs(os.path.join(args.full, layer), exist_ok=True)
                Image.fromarray(full).save(os.path.join(args.full, layer, k.replace("/", "_") + ".png"))
            print(f"{k} {layer}: {spec['class']}{' (same file as ' + spec['from'] + ')' if spec.get('_same_file') else ''}", flush=True)
    man["fingerprint"] = {"cols": fpp["cols"], "bands": fpp["bands"], "tolerance": fpp["tolerance"]}
    man["pad"] = {"cols": recipe["pad"]["cols"]}
    with open(mp, "w", encoding="utf-8") as f: json.dump(man, f, indent=1)
    finish(recipe, args, out, pack, layers)
    if found:
        fp = os.path.join(args.out, "aligned.json")
        prev = json.load(open(fp)) if os.path.exists(fp) else {}
        prev.update({f"{k} {l}": v for (k, l), v in found.items()})
        json.dump(prev, open(fp, "w"), indent=1)
        print(f"alignment of each master: {fp} (record an affine in the recipe to pin it)")


def finish(recipe, args, out, pack, layers):
    info = {"name": recipe["pack"]["name"], "version": recipe["pack"]["version"], "date": recipe["pack"]["date"],
            "min_engine": recipe["pack"]["min_engine"], "layers": [l for l in recipe["layers"] if os.path.isdir(os.path.join(out, l))],
            "over": {k: pack.info().get(k) for k in ("name", "version")},
            "recipe": {"file": "tools/hd-recipes/menu-wide.json", "sha256": sha256_file(args.recipe)},
            "models": recipe["pack"]["models"], "licence": recipe["pack"]["licence"], "aspect": recipe["pack"]["aspect"]}
    with open(os.path.join(out, "pack.json"), "w", encoding="utf-8") as f: json.dump(info, f, indent=2)
    with open(os.path.join(out, "ATTRIBUTION.txt"), "w", encoding="utf-8") as f: f.write("\n".join(recipe["pack"]["attribution"]) + "\n")
    lines = []
    for root, _, files in os.walk(out):
        for n in sorted(files):
            p = os.path.join(root, n); rel = os.path.relpath(p, out).replace(os.sep, "/")
            if rel != "FILES.sha256": lines.append(f"{sha256_file(p)}  {rel}")
    with open(os.path.join(out, "FILES.sha256"), "w") as f: f.write("\n".join(sorted(lines, key=lambda l: l[66:])) + "\n")


# ---- reading a built pack back (as the game does) ----

class WidePack:
    def __init__(self, path):
        self.path = path
        if os.path.isfile(path):
            z = zipfile.ZipFile(path); root = next(n[:n.index("tex-menu-wide/") + 14] for n in z.namelist() if "tex-menu-wide/" in n)
            self.read = lambda rel: z.read(root + rel)
        else:
            d = path if os.path.exists(os.path.join(path, "wide.json")) else os.path.join(path, "tex-menu-wide")
            self.read = lambda rel: open(os.path.join(d, rel), "rb").read()
        self.man = json.loads(self.read("wide.json"))

    def compose(self, key, page, layer):
        """The wide page as the game makes it, or None (no entry, a page another 4:3 pack made, or no file of the layer)."""
        sect, rec = manifest_key(key); e = self.man[sect].get(str(rec))
        if e is None: return None
        fp = self.man["fingerprint"]
        if not fingerprint_ok(e["edge"], page, fp["cols"], fp["bands"], fp["tolerance"]): return None
        chain = []; l = layer
        while l in self.man["layers"]: chain.append(l); l = self.man["layers"][l]["over"]
        img = page
        for l in reversed(chain):
            side = self.man["layers"][l]["side"]
            if e.get("pad"): L, R = pad_sides(img, side, self.man["pad"]["cols"])
            else:
                if l not in e: return None
                s = decode(self.read(e[l])); L, R = s[:, :side], s[:, side:]
            img = join(img, L, R)
        return img


def verify(args):
    recipe = read_recipe(args.recipe); pack = Pack43(args.pack43); wp = WidePack(args.dir)
    bad = 0; n = 0
    for k, spec in recipe["pages"].items():
        if spec["class"] == "skip": continue
        page, _ = pack.page(k)
        for layer in wp.man["layers"]:
            img = wp.compose(k, page, layer)
            if img is None: print(f"{k} {layer}: NOT composed"); bad += 1; continue
            W, H, side, _ = layer_info(recipe, layer)
            off = (W - page.shape[1]) // 2
            ok = img.shape == (H, W, 3) and np.array_equal(img[:, off:off + page.shape[1]], page)
            n += 1
            if not ok: print(f"{k} {layer}: the 4:3 page is not pixel-identical in the middle"); bad += 1
            if args.full:
                os.makedirs(os.path.join(args.full, layer), exist_ok=True)
                Image.fromarray(img).save(os.path.join(args.full, layer, k.replace("/", "_") + ".png"))
    print(f"{n} wide pages composed, the 4:3 page pixel-identical in each" if not bad else f"{bad} problems")
    return 1 if bad else 0


def compose_cmd(args):
    pack = Pack43(args.pack43); page, _ = pack.page(args.key)
    img = WidePack(args.dir).compose(args.key, page, args.layer)
    if img is None: sys.exit(f"{args.key}: no {args.layer} page")
    Image.fromarray(img).save(args.png); print(args.png, img.shape[1], "x", img.shape[0])


# ---- regenerate ----

def regenerate(args):
    recipe = read_recipe(args.recipe); spec = recipe["pages"].get(args.key)
    if spec is None or spec["class"] != "model": sys.exit(f"{args.key}: not a model page in the recipe")
    ls = spec["layers"].get(args.layer)
    if ls is None: sys.exit(f"{args.key}: no {args.layer} layer in the recipe")
    W, H, side, over = layer_info(recipe, args.layer)
    if over in INNER: inner, _ = Pack43(args.pack43).page(args.key)
    elif args.inner: inner = np.array(Image.open(args.inner).convert("RGB"))
    else: sys.exit(f"{args.layer} goes over {over}: give that layer's whole page with --inner (build --full writes it)")
    g = canvas_geom((inner.shape[1], inner.shape[0]), W, MODEL_OUT[(ls["model"], ls["aspect"], ls["size"])])
    canvas = make_canvas(inner, g, ls["canvas"])
    d = os.path.join(args.masters, args.key, args.layer); os.makedirs(d, exist_ok=True)
    for v in range(args.variants):
        n = 1
        while os.path.exists(os.path.join(d, f"regen__v{n}.png")): n += 1
        t = time.time(); pic, note, usage = gemini(ls["model"], ls["prompt"], canvas, ls["aspect"], ls["size"])
        print(f"{args.key} {args.layer} v{n}: {note} {time.time() - t:.0f}s", flush=True)
        if pic is None: continue
        p = os.path.join(d, f"regen__v{n}.png"); Image.fromarray(pic).save(p)
        json.dump({"model": ls["model"], "date": datetime.date.today().isoformat(), "prompt": ls["prompt"], "canvas": ls["canvas"],
                   "aspect": ls["aspect"], "size": ls["size"], "sha256": sha256_file(p), "usage": usage}, open(p[:-4] + ".json", "w"), indent=1)


def list_cmd(args):
    recipe = read_recipe(args.recipe)
    for k, s in recipe["pages"].items():
        extra = s.get("from") or ", ".join(f"{l}: {v['pick']}" for l, v in s.get("layers", {}).items()) or s.get("why", "")
        print(f"{k:22s} {s['class']:7s} {extra}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--recipe", default=RECIPE)
    sub = ap.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("build"); b.add_argument("--pack43", required=True, action="append"); b.add_argument("--masters", required=True)
    b.add_argument("--out", required=True); b.add_argument("--layers", default="16x9,21x9"); b.add_argument("--only")
    b.add_argument("--full", help="also write each whole wide page as PNG here"); b.add_argument("--work")
    b.add_argument("--models-dir", default=os.path.join(os.path.expanduser("~"), ".cache", "i26", "models"))
    b.add_argument("--device", default="auto")
    v = sub.add_parser("verify"); v.add_argument("dir"); v.add_argument("--pack43", required=True, action="append"); v.add_argument("--full")
    r = sub.add_parser("regenerate"); r.add_argument("key"); r.add_argument("--pack43", required=True, action="append"); r.add_argument("--masters", required=True)
    r.add_argument("--layer", default="16x9"); r.add_argument("--variants", type=int, default=1); r.add_argument("--inner")
    c = sub.add_parser("compose"); c.add_argument("dir"); c.add_argument("--pack43", required=True, action="append"); c.add_argument("--key", required=True)
    c.add_argument("--layer", required=True); c.add_argument("--png", required=True)
    sub.add_parser("list")
    a = ap.parse_args()
    if a.cmd == "build": build(a)
    elif a.cmd == "verify": sys.exit(verify(a))
    elif a.cmd == "regenerate": regenerate(a)
    elif a.cmd == "compose": compose_cmd(a)
    elif a.cmd == "list": list_cmd(a)


if __name__ == "__main__":
    main()
