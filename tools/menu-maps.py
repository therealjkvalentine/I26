#!/usr/bin/env python3
"""The mission maps for the map screen (M) and the briefing map at 4x, into the HD menu art's maps/ folder.

The map screen draws its map from the HD menu art (game/Scripts/Boot/Main.cs HdPicture: maps/<stem>.webp, .png or .jpg in
user://tex-menu, I76_TEX_DIR or the pack's zip), not from the world pack, so an upscale in tex-world-4x is never drawn there.
This step makes them:

    1. Export the install's textures (the game does it, then quits):  i26 -- --install DIR --export-textures SRC_DIR
    2. python3 tools/menu-maps.py SRC_DIR [--out TEX_MENU_DIR] [--writing plain|clean|ink] [--only GLOB] [--tile 256]

Each zmap* in SRC_DIR (the 640x480 zmap6* the modern look shows, and the 320x200 zmap3* twins) goes through the world pack's
model (tools/upscale-textures.py: 4xTextures_GTAV_rgt-s_dither, tiled, half precision on a GPU) at 4x, colour matched back to
the original, opaque (the screen draws the key's texels as their colour). That plain upscale is kept in --plain-dir (default
OUT/maps/.plain, a folder the game does not look in) and reused on the next run, so changing --writing needs no GPU. Then
the map's writing is finished one of three ways and written as OUT/maps/<stem>.png (a .webp or .jpg of the same stem there,
which the game would read first, is removed):

    plain   the upscale as the model left it.
    clean   the upscale with its ink cleaned: the model's halo (ringing lighter or darker than anything the original has
            within two texels) clipped away, the strokes' edges feathered (--feather, pack pixels) and the ink taken
            --opacity of the way from the paper, so the paper shows faintly through, as felt-tip ink on paper does. All the
            map's ink alike (the hand-drawn roads are the same pen as the writing); the maps in NO_INK (the melee
            missions' "no map" picture, a photo with type on it) stay plain.
    ink     the writing of the maps with a spec in tools/text-redraws/maps/ lifted (inpainted, the paper's grain given back)
            and set again in Permanent Marker as ink: each word's opacity, feather, baseline and stroke weight varied a
            little (INK, seeded by the map and the word, so a run is repeatable), multiplied into the paper. Maps
            without a spec stay plain.

Needs Python 3.10+, numpy and Pillow; torch and spandrel for the model step (pip install torch spandrel numpy pillow).
The model is the world pack's (--model FILE, else downloaded once into ~/.cache/i26/models/).
"""
import argparse, fnmatch, hashlib, importlib.util, json, os, random, sys, time
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))

def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m

tr = _load("text_redraws", "text-redraws.py")
ut = _load("upscale_textures", "upscale-textures.py")

MAP_SPECS = os.path.join(tr.SPECS, "maps")
WRITINGS = ("plain", "clean", "ink")
NO_INK = {"zmap6m00", "zmap3m00"}   # "NO MAP FOR MELEE MISSIONS": type over a photo of the cab, no pen
MAX_TILE = 256
# the ink variant's ranges, per word: opacity, feather (pack px), baseline jitter and turn (original texels, degrees),
# stroke weight added (original texels)
INK = {"opacity": (0.70, 0.85), "feather": (1.0, 1.5), "jitter": 0.6, "turn": 1.2, "weight": (0.0, 0.35)}

def user_menu():
    if sys.platform == "win32": return os.path.join(os.environ.get("APPDATA", ""), "I26", "tex-menu")
    if sys.platform == "darwin": return os.path.expanduser("~/Library/Application Support/I26/tex-menu")
    return os.path.join(os.environ.get("XDG_DATA_HOME", os.path.expanduser("~/.local/share")), "I26", "tex-menu")

def stems(src, only="zmap*"):
    """The maps in the export folder: lower-case stems of zmap*.png matching the glob, sorted."""
    out = []
    for fn in os.listdir(src):
        stem, ext = os.path.splitext(fn)
        if ext.lower() == ".png" and stem.lower().startswith("zmap") and fnmatch.fnmatch(stem.lower(), only.lower()): out.append(stem.lower())
    return sorted(set(out))

def original(src, stem):
    """The export's picture, upright, opaque: the screen draws the key's texels (index 255) as their colour."""
    p = next(os.path.join(src, f) for f in os.listdir(src) if f.lower() == stem + ".png")
    im = np.array(Image.open(p).convert("RGBA")); im[..., 3] = 255
    return im

def plain_file(plain_dir, stem):
    for e in (".png", ".webp"):
        p = os.path.join(plain_dir, stem + e)
        if os.path.exists(p): return p
    return None

def upscale(src, todo, plain_dir, model_path, device, tile):
    """The model's 4x of each map not yet in plain_dir, colour matched, as an opaque PNG."""
    import torch, spandrel
    if device == "auto": device = "cuda" if torch.cuda.is_available() else "cpu"
    ut.TILE = max(16, min(MAX_TILE, tile))
    model = ut.load_model(spandrel, torch, ut.model_file(model_path), device)
    dtype = next(model.model.parameters()).dtype
    print(f"model on {device} ({dtype}), {ut.TILE} px tiles")
    os.makedirs(plain_dir, exist_ok=True)
    for i, stem in enumerate(todo):
        t0 = time.time(); im = original(src, stem)
        big = ut.colour_match(ut.upscale(model, device, torch, im), im); big[..., 3] = 255
        dst = os.path.join(plain_dir, stem + ".png")
        Image.fromarray(big[..., :3]).save(dst + ".part.png"); os.replace(dst + ".part.png", dst)
        print(f"[{i + 1}/{len(todo)}] {stem} {time.time() - t0:.1f} s", flush=True)

# ---- clean: the ink's halo off, its edges feathered, its paper showing through ----

def _blur(a, sigma):
    """Gaussian blur of a float (h, w) or (h, w, 3) array in 0..255 through Pillow (8-bit steps are fine here)."""
    if a.ndim == 2: return np.asarray(Image.fromarray(np.clip(a + 0.5, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(sigma)), np.float32)
    return np.stack([_blur(a[..., c], sigma) for c in range(a.shape[2])], -1)

def _rank(a, size, fn):
    im = Image.fromarray(np.clip(a + 0.5, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.MinFilter(size) if fn == "min" else ImageFilter.MaxFilter(size)), np.float32)

def clean(big, orig, opacity=0.88, feather=1.2, margin=4.0):
    """big: the plain upscale (H x W x 3|4 uint8), orig: the original (h x w x 3|4). Returns the cleaned RGB uint8.
    1. halo: each channel clipped to the range the original has within two texels (plus margin levels), so the
       model's ringing round a stroke (a light rim on the paper, a dark one inside) goes and nothing else;
    2. ink: texels clearly darker than the brightest paper within three texels (depth 0.25 to 0.5 of it, smoothly) are ink;
    3. feather: the ink's edges (where that weight is neither 0 nor 1, a texel wide) blurred over `feather` pack pixels;
    4. transparency: the ink taken (1 - opacity) of the way back to the paper beside it."""
    H, W = big.shape[:2]; h, w = orig.shape[:2]; s = W // w
    U = big[..., :3].astype(np.float32)
    o = orig[..., :3].astype(np.float32)
    def up(a): return np.asarray(Image.fromarray(a.astype(np.float32), "F").resize((W, H), Image.BILINEAR), np.float32)
    # the rank filters at the original's size (cheap), enlarged
    lo = np.stack([up(_rank(o[..., c], 5, "min")) for c in range(3)], -1); hi = np.stack([up(_rank(o[..., c], 5, "max")) for c in range(3)], -1)
    U = np.clip(U, lo - margin, hi + margin)
    L = U.mean(axis=2)
    paper = up(_rank(_blur(o.mean(axis=2), 0.5), 7, "max"))
    d = (paper - L) / np.maximum(paper, 1.0)
    ink = np.clip((d - 0.25) / 0.25, 0, 1); ink = ink * ink * (3 - 2 * ink)
    edge = _blur(ink * 255, 0.5 * s) / 255; edge = np.clip(4 * edge * (1 - edge), 0, 1)[..., None]
    U = U + (_blur(U, feather / 2.0) - U) * edge
    nw = 1 - ink
    P = _blur(U * nw[..., None], 3 * s) / np.maximum(_blur(nw * 255, 3 * s)[..., None] / 255, 1e-3)
    U = U + (P - U) * (ink * (1 - opacity))[..., None]
    return np.clip(U + 0.5, 0, 255).astype(np.uint8)

# ---- ink: the writing lifted and set again in Permanent Marker ----

def ink_ops(stem, spec, ranges=INK):
    """The spec's ops with each text op inked: an "ink" block (opacity, feather) and a little of a hand's unevenness
    (baseline and turn jitter, stroke weight as a stroke in the ink's own colour), drawn from a generator seeded by the
    map and the word, so the same spec always gives the same map."""
    out = []
    for op in spec["ops"]:
        if op.get("op") != "text": out.append(op); continue
        r = random.Random(int(hashlib.sha1(f"{stem}|{op['text']}".encode()).hexdigest()[:8], 16))
        o = dict(op)
        o["ink"] = {"opacity": round(r.uniform(*ranges["opacity"]), 3), "feather": round(r.uniform(*ranges["feather"]), 3)}
        cx, cy = op["center"]; o["center"] = [cx, round(cy + r.uniform(-1, 1) * ranges["jitter"], 3)]
        o["rotate"] = round(float(op.get("rotate", 0)) + r.uniform(-1, 1) * ranges["turn"], 3)
        wt = r.uniform(*ranges["weight"])
        if wt > 0.02: o["stroke"] = {"width": round(wt, 3), "colour": op.get("colour", [0, 0, 0])}
        out.append(o)
    return out

def ink_colours(ops, big, orig):
    """Each word's ink as a multiplier: its painted letters' colour in the upscale over the paper's round them (the
    darkest fifth and the lightest two fifths of the word's erase box, the n-th text op's box the n-th erase op's), so
    the ink multiplied in at full strength gives the colour the map's pen has there, and at the ink's opacity a little
    of the paper's grain shows through."""
    s = big.shape[1] / orig.shape[1]
    boxes = [o["rect"] for o in ops if o["op"] == "erase"]
    out, n = [], 0
    for op in ops:
        if op["op"] == "text" and op.get("ink") and n < len(boxes):
            x0, y0, x1, y1 = (int(round(v * s)) for v in boxes[n])
            r = big[max(0, y0):y1, max(0, x0):x1, :3].reshape(-1, 3).astype(np.float32)
            if len(r) > 20:
                lum = r.mean(axis=1); q20, q60 = np.percentile(lum, [20, 60])
                ink, paper = np.median(r[lum <= q20], axis=0), np.median(r[lum >= q60], axis=0)
                op = dict(op, colour=[int(v) for v in np.clip(ink / np.maximum(paper, 1) * 255 + 0.5, 0, 255)])
        if op["op"] == "text": n += 1
        out.append(op)
    return out

def map_spec(stem, specs_dir=MAP_SPECS):
    p = os.path.join(specs_dir, stem + ".json")
    if not os.path.exists(p): return None
    with open(p, encoding="utf-8") as f: return json.load(f)

def finish(stem, big, orig, writing, specs_dir=MAP_SPECS, table=None):
    """The written map (H x W x 3 uint8) and what was done to it."""
    if writing == "clean" and stem not in NO_INK: return clean(big, orig), "clean"
    if writing == "ink" and stem not in NO_INK and (spec := map_spec(stem, specs_dir)):
        px = np.dstack([big[..., :3], np.full(big.shape[:2], 255, np.uint8)])
        return tr.apply_ops(px, orig, ink_colours(ink_ops(stem, spec), big, orig), table=table)[..., :3], "ink"
    return big[..., :3], "plain"

def write(out_maps, stem, rgb):
    os.makedirs(out_maps, exist_ok=True)
    for e in (".webp", ".jpg"):
        p = os.path.join(out_maps, stem + e)
        if os.path.exists(p): os.remove(p)       # the game reads .webp first, then .png
    dst = os.path.join(out_maps, stem + ".png")
    Image.fromarray(rgb).save(dst + ".part.png"); os.replace(dst + ".part.png", dst)

def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", help="the folder --export-textures wrote (zmap*.png, upright)")
    ap.add_argument("--out", default=None, help="the HD menu art folder (default: the game's tex-menu); maps go in OUT/maps/")
    ap.add_argument("--plain-dir", default=None, help="where the plain upscales are kept (default OUT/maps/.plain)")
    ap.add_argument("--writing", choices=WRITINGS, default="clean", help="how the maps' writing is finished (default clean)")
    ap.add_argument("--only", default="zmap*", help="a glob of map names (default zmap*)")
    ap.add_argument("--model", default=None, help="the model file (default: the world pack's, downloaded once)")
    ap.add_argument("--device", default="auto", help="cuda, cpu or auto")
    ap.add_argument("--tile", type=int, default=256, help=f"model input tile size (default and most {MAX_TILE})")
    ap.add_argument("--no-model", action="store_true", help="only finish maps whose plain upscale is already there")
    a = ap.parse_args(argv)
    out = a.out or user_menu(); out_maps = os.path.join(out, "maps"); plain_dir = a.plain_dir or os.path.join(out_maps, ".plain")
    names = stems(a.src, a.only)
    if not names: ap.error(f"no zmap*.png matching {a.only} in {a.src}")
    todo = [m for m in names if plain_file(plain_dir, m) is None]
    if todo and not a.no_model: upscale(a.src, todo, plain_dir, a.model, a.device, a.tile)
    table = tr.font_table(); n = 0; t0 = time.time()
    for stem in names:
        p = plain_file(plain_dir, stem)
        if p is None: print(f"{stem}: no plain upscale, skipped"); continue
        rgb, how = finish(stem, np.array(Image.open(p).convert("RGB")), original(a.src, stem), a.writing, table=table)
        write(out_maps, stem, rgb); n += 1
        print(f"{stem}: {how}", flush=True)
    print(f"{n} maps in {out_maps} ({time.time() - t0:.1f} s)")

if __name__ == "__main__":
    main()
