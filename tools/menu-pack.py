#!/usr/bin/env python3
"""Make the HD menu art (tex-menu: the shell's pages, the Esc pads, the sprites, the loading cards, the mission maps) from the
player's own install, by the recipe tools/hd-recipes/menu.json, and compare a build with a released pack.

    python tools/menu-pack.py export --install DIR EXPORT          everything the pack is made from (`i76 menu-export`)
    python tools/menu-pack.py build [EXPORT | --install DIR] --out DIR [--only GLOB] [--steps a,b] [--world-export DIR] [--work DIR]
    python tools/menu-pack.py verify DIR --against RELEASE.zip [--report FILE]
    python tools/menu-pack.py list-steps

EXPORT is the folder `i76 --install DIR menu-export EXPORT` writes (bg/, bank/, pad/, load/, glyphs/): the player's game data,
never committed; with --install in its place, build makes it first in WORK/export (tools/hd-pack.py menu calls it so).
build writes DIR/tex-menu (copy that folder into the I26 user folder, or zip it as i76-hd-menus-*.zip) and
keeps the model's own output in --work (default DIR/work), so a second build, or one with --only, runs no model for a file
whose input, model and settings are unchanged. --only takes globs of pack files (shellbg30.webp, sprites/36_*, 6mainmn1.webp,
nitro/load/*, maps/*), comma-separated: every step that names a matching file runs for it, from the model's output on.

The steps, in the order the released pack was made (each recipe step's "note" says which lane's script it reproduces):
  base        the model (4xTextures_GTAV_rgt-s_dither, 128 px tiles with 16 px of reflected context, float32) over every
              picture, its colours matched back to the original's (tools/upscale-textures.py's colour match), key bled
              first and the alpha cut at half; written as WebP as the release did (lossy, quality 95, method 6)
  small-text  the pages' small print through bicubic 4x and an unsharp mask instead of the model's, through a mask of
              small dark ink read from the original
  lineart     the blueprint sprites' 1-texel lines traced into smooth strokes
  lettering   the pages' and pads' printed words set again in Arial (tools/text-redraws/menu/retype/, page 16's spec)
  rules       tools/menu-rules.py --mode chroma (the forms' ruled lines)
  flat-art    tools/flat-art.py --smooth 0.5 --alphamax 0.75 (the title logo)
  fixes       the later fixes in their order: the Parts Catalog's clipped words (page 4), the circled C/V/R key marks and
              the inventory title (pages 1, 3, 30, sprite 36_6), the field salvage lines re-set to the next page's print
  maps        tools/menu-maps.py (the mission maps, from the world texture export: --world-export)
  finish      shellbg.json, pads.json, sprites/sprites.json, pack.json, ATTRIBUTION.txt
Needs Python 3.10+, numpy, Pillow (with WebP), scipy, opencv-python-headless, scikit-image, potracer, resvg-py, and for the
model torch and spandrel (a CUDA build for an NVIDIA card; the CPU works, slowly). The lettering uses Windows' Arial
(arial.ttf, arialbd.ttf, ariali.ttf from the system's font folder, as the released pack did): elsewhere the build stops at
that step and says so (I26_ARIAL_DIR names another folder holding those three files).
"""
import argparse, fnmatch, glob, hashlib, importlib.util, io, json, math, os, shutil, subprocess, sys, time, zipfile
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
RECIPE = os.path.join(HERE, "hd-recipes", "menu.json")
sys.path.insert(0, HERE)


def load_tool(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod)
    return mod


_ut = None
def ut():
    """tools/upscale-textures.py: the bleed under the key and the colour match the world packs use, shared."""
    global _ut
    if _ut is None: _ut = load_tool("upscale_textures", "upscale-textures.py")
    return _ut


# ---- pictures ----

def read_rgb(p): return np.array(Image.open(p).convert("RGB"))

def read_rgba(p): return np.array(Image.open(p).convert("RGBA"))

def save_webp(arr, dst, enc):
    """enc: {"lossless": bool, "quality": q, "method": m, "exact": bool} (the recipe's encoding for the step)."""
    os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)
    mode = "RGBA" if arr.ndim == 3 and arr.shape[2] == 4 else "RGB"
    kw = {"quality": enc.get("quality", 95), "method": enc.get("method", 6)}
    if enc.get("lossless"): kw["lossless"] = True
    if enc.get("exact") and mode == "RGBA": kw["exact"] = True
    Image.fromarray(arr, mode).save(dst + ".part.webp", "WEBP", **kw)
    os.replace(dst + ".part.webp", dst)

def key_alpha(rgb):
    """A sprite's key: magenta texels transparent (as `i76 bank` writes them)."""
    return np.where((rgb == [255, 0, 255]).all(-1), 0, 255).astype(np.uint8)


# ---- the model ----

class Model:
    """4x through one model, as the 2026-11-03 base was made: the whole picture padded by PAD texels of reflection, then cut
    in TILE x TILE tiles each read with its PAD of context, float32, clamped, rounded half up."""
    def __init__(self, path, device, tile, pad, precision="float32"):
        import torch, spandrel
        self.torch = torch; self.tile, self.pad = tile, pad
        self.device = device if device != "auto" else ("cuda" if torch.cuda.is_available() else "cpu")
        self.m = spandrel.ModelLoader().load_from_file(path).to(self.device).eval()
        # float16 only where the release ran it so (and the model and device allow it)
        self.half = precision == "float16" and self.device == "cuda" and self.m.supports_half
        if self.half: self.m = self.m.half()
        self.scale = self.m.scale

    def rgb(self, rgb):
        torch = self.torch; s = self.scale; h, w, _ = rgb.shape; T, P = self.tile, self.pad
        X = np.pad(rgb.astype(np.float32) / 255, ((P, P), (P, P), (0, 0)), mode="reflect")
        out = np.zeros((h * s, w * s, 3), np.float32)
        for y in range(0, h, T):
            for x in range(0, w, T):
                th, tw = min(T, h - y), min(T, w - x)
                t = X[y:y + th + 2 * P, x:x + tw + 2 * P]
                inp = torch.from_numpy(np.ascontiguousarray(t)).permute(2, 0, 1)[None].to(self.device)
                if self.half: inp = inp.half()
                with torch.no_grad(): Y = self.m(inp)[0].float().permute(1, 2, 0).clamp(0, 1).cpu().numpy()
                out[y * s:(y + th) * s, x * s:(x + tw) * s] = Y[P * s:(P + th) * s, P * s:(P + tw) * s]
        return (out * 255 + 0.5).astype(np.uint8)

    def up(self, rgba, colour_match=True):
        """RGBA -> 4x RGBA: colour bled under the key, the model, the alpha bilinear 4x cut at half, colours matched."""
        u = ut(); a = rgba[..., 3]
        rgb = u.bleed(rgba[..., :3], a)
        big = self.rgb(np.clip(rgb + 0.5, 0, 255).astype(np.uint8))
        A = np.array(Image.fromarray(a).resize((a.shape[1] * self.scale, a.shape[0] * self.scale), Image.BILINEAR))
        big = np.dstack([big, np.where(A >= 128, 255, 0).astype(np.uint8)])
        return u.colour_match(big, rgba) if colour_match else big


def sha256(path, cache={}):
    if path not in cache:
        h = hashlib.sha256()
        with open(path, "rb") as f:
            for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
        cache[path] = h.hexdigest()
    return cache[path]


def read_recipe(path):
    """The recipe, its "models" (the files it uses) resolved from tools/hd-recipes/models.json, the one list of pinned models,
    by each pin's "model" name (the name the steps use)."""
    with open(path, encoding="utf-8") as f: recipe = json.load(f)
    with open(os.path.join(os.path.dirname(os.path.abspath(path)), "models.json"), encoding="utf-8") as f: pins = json.load(f)["models"]
    recipe["models"] = {pins[file]["model"]: dict(pins[file], file=file) for file in recipe["models"]}
    return recipe


def model_path(recipe, name, models_dir):
    """The model file, found in models_dir (or downloaded from the recipe's url) and checked against its SHA-256."""
    info = recipe["models"][name]
    p = os.path.join(models_dir, info["file"])
    if not os.path.exists(p):
        if not info.get("url"): sys.exit(f"{info['file']}: not in {models_dir}; get it from {info.get('page', 'its author')}")
        os.makedirs(models_dir, exist_ok=True); print(f"downloading {info['url']}")
        import urllib.request
        urllib.request.urlretrieve(info["url"], p + ".part"); os.replace(p + ".part", p)
    got = sha256(p)
    if got.lower() != info["sha256"].lower(): sys.exit(f"{p}: SHA-256 {got}, the recipe wants {info['sha256']}: not the same model")
    return p


# ---- what the pack holds, and where each picture comes from ----

def pad_original(export, name):
    """An Esc pad as the game draws it: Audio Control's unused channel boxes replaced by the Options pad's rows from y 338."""
    im = read_rgb(os.path.join(export, "pad", f"{name}.ppm"))
    if name == "audcon1":
        main = read_rgb(os.path.join(export, "pad", "mainmn1.ppm"))
        im[338:] = main[338:im.shape[0]]
    return im

def pictures(export):
    """{pack file: (kind, source)}: every picture the base makes, from the export."""
    out = {}
    for p in sorted(glob.glob(os.path.join(export, "bg", "bg*.ppm"))):
        n = int(os.path.basename(p)[2:4]); out[f"shellbg{n}.webp"] = ("bg", n)
    for p in sorted(glob.glob(os.path.join(export, "pad", "*.ppm"))):
        n = os.path.basename(p)[:-4]; out[f"6{n}.webp"] = ("pad", n)
    for p in sorted(glob.glob(os.path.join(export, "load", "*.ppm"))):
        n = os.path.basename(p)[:-4]; out[f"nitro/load/{n}.webp"] = ("load", n)
    for d in sorted(glob.glob(os.path.join(export, "bank", "*")), key=lambda d: int(os.path.basename(d))):
        b = int(os.path.basename(d))
        for p in sorted(glob.glob(os.path.join(d, "*.ppm"))):
            i = int(os.path.basename(p)[:-4]); out[f"sprites/{b}_{i}.webp"] = ("bank", (b, i))
    return out

def original(export, kind, src):
    """The original picture of a pack file, RGBA (a sprite's key transparent)."""
    if kind == "bg": rgb = read_rgb(os.path.join(export, "bg", f"bg{src:02d}.ppm")); a = np.full(rgb.shape[:2], 255, np.uint8)
    elif kind == "pad": rgb = pad_original(export, src); a = np.full(rgb.shape[:2], 255, np.uint8)
    elif kind == "load": rgb = read_rgb(os.path.join(export, "load", f"{src}.ppm")); a = np.full(rgb.shape[:2], 255, np.uint8)
    else: rgb = read_rgb(os.path.join(export, "bank", str(src[0]), f"{src[1]:02d}.ppm")); a = key_alpha(rgb)
    return np.dstack([rgb, a])

def colour_mask(o, spec):
    """A flat stamp found by its colours inside "box" (original texels): "dark" (every channel under it), "yellow"
    ([r_min, g_min, b_max]), "blue" (blue over red), "white" (every channel over it); closed twice by 3x3, holes filled,
    the largest region kept and grown by "grow" texels (its anti-aliased rim)."""
    from scipy import ndimage
    R, G, B = [o[..., i].astype(int) for i in range(3)]
    y = spec["yellow"]
    m = (o.max(-1) < spec["dark"]) | ((R > y[0]) & (G > y[1]) & (B < y[2])) | (B > R if spec.get("blue") else False) | (o.min(-1) > spec["white"])
    x0, y0, x1, y1 = spec["box"]; box = np.zeros(m.shape, bool); box[y0:y1, x0:x1] = True; m &= box
    m = ndimage.binary_fill_holes(ndimage.binary_closing(m, np.ones((3, 3), bool), iterations=2))
    lab, n = ndimage.label(m)
    if n: m = lab == np.bincount(lab[lab > 0]).argmax()
    return ndimage.binary_dilation(m, iterations=spec.get("grow", 1))

def matches(rel, globs):
    return any(fnmatch.fnmatch(rel, g) for g in globs)

def page_of(rel):
    return int(rel[7:-5]) if rel.startswith("shellbg") and rel[7:-5].isdigit() else None


# ---- steps ----

class Build:
    def __init__(self, args, recipe):
        self.a = args; self.r = recipe; self.export = args.export
        self.pack = os.path.join(args.out, "tex-menu"); self.work = args.work or os.path.join(args.out, "work")
        self.only = [g.strip() for g in args.only.split(",")] if args.only else ["*"]
        self.pics = pictures(self.export)
        if not self.pics: sys.exit(f"{self.export}: no bg/, pad/, load/ or bank/ (run: menu-pack.py export --install DIR {self.export})")
        self._model = None; self.log = []
        os.makedirs(self.pack, exist_ok=True); os.makedirs(self.work, exist_ok=True)

    def files(self, step):
        """The pack files a step works on: its own "files" globs (all pictures when it has none) within --only."""
        own = step.get("files", ["*"])
        return [r for r in self.pics if matches(r, own) and matches(r, self.only)]

    def orig(self, rel):
        kind, src = self.pics[rel]; return original(self.export, kind, src)

    def enc(self, step):
        return dict(self.r["encodings"][step.get("encoding", "lossy")])

    def model(self):
        if self._model is None:
            m = self.r["base"]
            self._model = Model(model_path(self.r, m["model"], self.a.models_dir), self.a.device, m["tile"], m["pad"])
            print(f"model {m['model']} on {self._model.device}", flush=True)
        return self._model

    def gpu_lock(self):
        """A lock file shared with other GPU jobs on the machine (I26_GPU_LOCK): wait while another holds it."""
        p = os.environ.get("I26_GPU_LOCK")
        if not p: return None
        while os.path.exists(p):
            print(f"waiting for {p} ({open(p).read().strip()})", flush=True); time.sleep(30)
        open(p, "w").write(f"menu-pack.py {os.getpid()}\n"); return p

    # base: the model over every picture
    def step_base(self, step):
        files = self.files(step); m = self.r["base"]
        key = {"model": self.r["models"][m["model"]]["sha256"], "tile": m["tile"], "pad": m["pad"], "colour_match": True}
        cache = os.path.join(self.work, "model"); jobs = []
        for rel in files:
            o = self.orig(rel)
            k = hashlib.sha256(json.dumps(key, sort_keys=True).encode() + o.tobytes() + str(o.shape).encode()).hexdigest()[:16]
            c = os.path.join(cache, rel[:-5] + f".{k}.png")
            jobs.append((rel, o, c))
        need = [t for t in jobs if not os.path.exists(t[2])]
        lock = self.gpu_lock() if need else None
        try:
            t0 = time.time()
            for i, (rel, o, c) in enumerate(need):
                big = self.model().up(o)
                os.makedirs(os.path.dirname(c), exist_ok=True)
                Image.fromarray(big, "RGBA").save(c + ".part.png"); os.replace(c + ".part.png", c)
                if i % 20 == 0 or i == len(need) - 1: print(f"  model {i + 1}/{len(need)} {rel} ({time.time() - t0:.0f} s)", flush=True)
        finally:
            if lock and os.path.exists(lock): os.remove(lock)
        for rel, o, c in jobs:
            big = read_rgba(c); kind = self.pics[rel][0]
            if kind == "bank": save_webp(big, os.path.join(self.pack, rel), self.r["encodings"]["sprite"])
            else: save_webp(big[..., :3].copy(), os.path.join(self.pack, rel), self.enc(step))
        return len(jobs)

    def step_inset(self, step):
        """A second model's picture laid into the pack inside a mask of the original (feathered by a Gaussian of
        "feather" pack pixels): the release's 2026-11-04 logo pass on page 14, of which the fire photo inside the logo's
        openings is what the later steps keep. The model's output is kept in WORK/model like the base's."""
        files = self.files(step)
        if not files: return 0
        m = step["model"]; info = self.r["models"][m["name"]]
        key = {"model": info["sha256"], "tile": m["tile"], "pad": m["pad"], "precision": m["precision"]}
        model = None; lock = None
        try:
            for rel in files:
                o = self.orig(rel)
                k = hashlib.sha256(json.dumps(key, sort_keys=True).encode() + o.tobytes()).hexdigest()[:16]
                c = os.path.join(self.work, "model", rel[:-5] + f".{step['id']}.{k}.png")
                if not os.path.exists(c):
                    if model is None:
                        lock = self.gpu_lock()
                        model = Model(model_path(self.r, m["name"], self.a.models_dir), self.a.device, m["tile"], m["pad"], m["precision"])
                        print(f"  model {m['name']} on {model.device}{' (float16)' if model.half else ''}", flush=True)
                    big = model.up(o)
                    os.makedirs(os.path.dirname(c), exist_ok=True)
                    Image.fromarray(big, "RGBA").save(c + ".part.png"); os.replace(c + ".part.png", c)
                big = read_rgb(c).astype(np.float32)
                mask = colour_mask(o[..., :3], step["mask"])
                H, W = big.shape[:2]
                M4 = np.array(Image.fromarray((mask * 255).astype(np.uint8)).resize((W, H), Image.BILINEAR)) >= 128
                mf = ut().gauss(M4[..., None].astype(np.float32), step.get("feather", 2.0))[..., 0][..., None]
                dst = os.path.join(self.pack, rel)
                cur = read_rgb(dst).astype(np.float32)
                save_webp(np.clip(big * mf + cur * (1 - mf) + 0.5, 0, 255).astype(np.uint8), dst, self.enc(step))
        finally:
            if lock and os.path.exists(lock): os.remove(lock)
        return len(files)

    def step_tool(self, step):
        """A page-level step done by the lettering engine (menu_lettering.py) on the files the step names."""
        import menu_lettering as ml
        fn = getattr(ml, step["op"].replace("-", "_"))
        n = 0
        for rel in self.files(step):
            dst = os.path.join(self.pack, rel)
            if not os.path.exists(dst): continue
            cur = read_rgba(dst) if rel.startswith("sprites/") else read_rgb(dst)
            out = fn(cur, self.orig(rel), dict(step.get("args", {}), rel=rel), self.ctx())
            save_webp(out, dst, self.enc(step)); n += 1
        return n

    def ctx(self):
        return {"export": self.export, "pack": self.pack, "work": self.work, "orig": self.orig}

    def step_specs(self, step):
        """Lettering specs (tools/text-redraws format, with the menu ops of menu_lettering.py) applied in file order."""
        tr = load_tool("text_redraws", "text-redraws.py"); table = tr.font_table(); n = 0
        for d in step["specs"]:
            for name, target, spec in tr.load_specs(os.path.join(HERE, d), "*"):
                rel = target if target.endswith(".webp") else target + ".webp"
                if not matches(rel, self.only) or rel not in self.pics: continue
                dst = os.path.join(self.pack, rel)
                if not os.path.exists(dst): continue
                sprite = rel.startswith("sprites/")
                px = read_rgba(dst) if sprite else np.dstack([read_rgb(dst), np.full(Image.open(dst).size[::-1], 255, np.uint8)])
                out = tr.apply_ops(px, self.orig(rel), spec["ops"], table=table, ctx=self.ctx())
                enc = dict(self.r["encodings"][spec.get("encoding", step.get("encoding", "lossy"))])
                save_webp(out if sprite else out[..., :3].copy(), dst, enc); n += 1
                print(f"  {rel}: {d}/{name}", flush=True)
        return n

    def step_run(self, step):
        """A repository tool run over a folder of the pages (menu-rules.py, flat-art.py): the pages the step names are put
        in a scratch folder with their originals, the tool rewrites them there, and they come back into the pack."""
        files = self.files(step)
        if not files: return 0
        tmp = os.path.join(self.work, "run-" + step["id"]); shutil.rmtree(tmp, ignore_errors=True)
        bg = os.path.join(tmp, "bg"); pk = os.path.join(tmp, "tex-menu"); os.makedirs(bg); os.makedirs(pk)
        for rel in files:
            n = page_of(rel); shutil.copy2(os.path.join(self.export, "bg", f"bg{n:02d}.ppm"), bg)
            shutil.copy2(os.path.join(self.pack, rel), pk)
        with open(os.path.join(pk, "shellbg.json"), "w", encoding="utf-8") as f:
            json.dump({str(page_of(r)): {"file": r, "affine": [4.0, 0.0, 4.0, 0.0]} for r in files}, f)
        pages = ",".join(str(page_of(r)) for r in files)
        cmd = [sys.executable, os.path.join(HERE, step["tool"]), bg, "--pack", pk] + [a.replace("{pages}", pages) for a in step.get("args", [])]
        print("  " + " ".join(cmd), flush=True)
        r = subprocess.run(cmd, capture_output=True, text=True)
        if r.returncode: sys.exit(f"{step['tool']} failed:\n{r.stdout}\n{r.stderr}")
        for rel in files: shutil.copy2(os.path.join(pk, rel), os.path.join(self.pack, rel))
        return len(files)

    def step_maps(self, step):
        if not self.a.world_export:
            print("  maps: skipped (--world-export DIR, the folder the game's --export-textures wrote, is needed)"); return 0
        if not matches("maps/x.png", self.only) and not any(g.startswith("maps/") for g in self.only): return 0
        m = self.r["base"]
        cmd = [sys.executable, os.path.join(HERE, "menu-maps.py"), self.a.world_export, "--out", self.pack,
               "--model", model_path(self.r, m["model"], self.a.models_dir)] + step.get("args", [])
        print("  " + " ".join(cmd), flush=True)
        lock = self.gpu_lock()
        try: r = subprocess.run(cmd, capture_output=True, text=True)
        finally:
            if lock and os.path.exists(lock): os.remove(lock)
        if r.returncode: sys.exit(f"menu-maps.py failed:\n{r.stdout}\n{r.stderr}")
        return len(glob.glob(os.path.join(self.pack, "maps", "*")))

    def step_finish(self, step):
        """The json files the game reads (affine [ax, bx, ay, by]: picture = a * original + b), pack.json, ATTRIBUTION.txt."""
        aff = [4.0, 0.0, 4.0, 0.0]
        bg = {str(src): {"file": rel, "affine": aff} for rel, (k, src) in self.pics.items() if k == "bg"}
        pads = {src: {"file": rel, "affine": aff} for rel, (k, src) in self.pics.items() if k == "pad"}
        spr = {}
        for rel, (k, src) in self.pics.items():
            if k != "bank": continue
            o = self.orig(rel); spr[f"{src[0]}_{src[1]}"] = {"file": f"{src[0]}_{src[1]}.webp", "affine": aff, "size": [o.shape[1], o.shape[0]]}
        def dump(obj, p):
            os.makedirs(os.path.dirname(p), exist_ok=True)
            with open(p, "w", encoding="utf-8") as f: json.dump(obj, f, indent=1)
        dump(dict(sorted(bg.items(), key=lambda kv: int(kv[0]))), os.path.join(self.pack, "shellbg.json"))
        dump(dict(sorted(pads.items())), os.path.join(self.pack, "pads.json"))
        dump(dict(sorted(spr.items(), key=lambda kv: tuple(int(x) for x in kv[0].split("_")))), os.path.join(self.pack, "sprites", "sprites.json"))
        info = dict(self.r["pack"])
        info["date"] = time.strftime("%Y-%m-%d")
        info["recipe"] = {"file": "tools/hd-recipes/menu.json", "sha256": sha256(self.a.recipe)}
        info["built_with"] = versions()
        info["steps"] = [{"id": s["id"], "note": s.get("note", "")} for s in self.r["steps"]]
        with open(os.path.join(self.pack, "pack.json"), "w", encoding="utf-8") as f: json.dump(info, f, indent=2)
        with open(os.path.join(self.pack, "ATTRIBUTION.txt"), "w", encoding="utf-8") as f: f.write("\n".join(self.r["attribution"]) + "\n")
        return 5


def versions():
    out = {"python": sys.version.split()[0]}
    for m in ("numpy", "PIL", "cv2", "scipy", "torch", "spandrel"):
        try: out[m] = __import__(m).__version__
        except Exception: pass
    try:
        from PIL import features; out["libwebp"] = features.version("webp")
    except Exception: pass
    try:
        import torch
        if torch.cuda.is_available(): out["gpu"] = torch.cuda.get_device_name(0)
    except Exception: pass
    return out


def build(args):
    recipe = read_recipe(args.recipe)
    if not args.export:
        if not args.install: sys.exit("build needs the export folder, or --install DIR to make one")
        args.export = os.path.join(args.work or os.path.join(args.out, "work"), "export")
        if not os.path.exists(os.path.join(args.export, "manifest.json")):
            e = argparse.Namespace(install=args.install, out=args.export, pad_palette=None)
            if export(e): sys.exit("menu-export failed")
    if not args.world_export:
        # the world pack's export beside this work folder (tools/hd-pack.py's layout), for the maps
        w = os.path.dirname(os.path.abspath(args.work or os.path.join(args.out, "work")))
        for c in (os.path.join(w, "world-4x", "export"), os.path.join(w, "export")):
            if glob.glob(os.path.join(c, "zmap*.png")): args.world_export = c; break
    b = Build(args, recipe)
    want = set(args.steps.split(",")) if args.steps else None
    for step in recipe["steps"]:
        if want and step["id"] not in want and step.get("group") not in want: continue
        t0 = time.time()
        fn = getattr(b, "step_" + step["kind"])
        n = fn(step)
        print(f"{step['id']}: {n} files ({time.time() - t0:.0f} s)", flush=True)
    print(f"built {b.pack}")


# ---- verify ----

def decode(data):
    im = Image.open(io.BytesIO(data)); im.load()
    return np.array(im.convert("RGBA")).astype(np.float64)

def psnr(a, b):
    if a.shape != b.shape: return None, None
    # transparent texels compared by their alpha only (their colour is not drawn)
    if a.shape[2] == 4:
        if (a[..., 3] == 255).all() and (b[..., 3] == 255).all(): a, b = a[..., :3], b[..., :3]   # opaque: colour only
        else:
            hid = (a[..., 3] == 0) & (b[..., 3] == 0); a = a.copy(); b = b.copy(); a[hid, :3] = 0; b[hid, :3] = 0
    d = a - b; mse = (d * d).mean()
    return (float("inf") if mse == 0 else 10 * math.log10(255 * 255 / mse)), float(np.abs(d).max())

def verify(args):
    """Every file of the build against the release: identical bytes, the same pictures (decoded), PSNR and the largest
    difference, missing (in the release, not in the build) and extra. Pictures at 45 dB or more are "same", 35 to 45
    "look", under 35 "different"."""
    pack = args.pack if os.path.basename(args.pack.rstrip("/\\")) == "tex-menu" else os.path.join(args.pack, "tex-menu")
    z = zipfile.ZipFile(args.against)
    rel = {}
    for n in z.namelist():
        if n.endswith("/"): continue
        r = n[len("tex-menu/"):] if n.startswith("tex-menu/") else None
        if r: rel[r] = n
    mine = {os.path.relpath(p, pack).replace("\\", "/") for p in glob.glob(os.path.join(pack, "**", "*"), recursive=True)
            if os.path.isfile(p) and "/." not in "/" + os.path.relpath(p, pack).replace("\\", "/")}
    rows = []; only = [g.strip() for g in args.only.split(",")] if args.only else ["*"]
    skip = {"FILES.sha256", "README.txt"}
    for r in sorted(set(rel) | mine):
        if r in skip or not matches(r, only): continue
        if r not in mine: rows.append((r, "missing", None, None)); continue
        if r not in rel: rows.append((r, "extra", None, None)); continue
        a = z.read(rel[r])
        with open(os.path.join(pack, r), "rb") as f: b = f.read()
        if a == b: rows.append((r, "identical", float("inf"), 0.0)); continue
        if r.endswith(".json"):
            try: same = json.loads(a) == json.loads(b)
            except Exception: same = False
            rows.append((r, "same-json" if same else "different", None, None)); continue
        if not r.endswith((".webp", ".png", ".jpg")): rows.append((r, "different", None, None)); continue
        A, B = decode(a), decode(b)
        if A.shape != B.shape: rows.append((r, "different", None, None)); continue
        p, mx = psnr(A, B)
        cls = "same-pixels" if p == float("inf") else "same" if p >= 45 else "look" if p >= 35 else "different"
        rows.append((r, cls, p, mx))
    from collections import Counter
    c = Counter(x[1] for x in rows)
    lines = [f"verify {pack} against {os.path.basename(args.against)}", "summary: " + ", ".join(f"{k} {v}" for k, v in sorted(c.items()))]
    groups = {}
    for r, cls, p, mx in rows:
        g = r.split("/")[0] if "/" in r else ("pages" if r.startswith("shellbg") else "pads" if r.startswith("6") else "other")
        groups.setdefault(g, []).append(p)
    for g, ps in sorted(groups.items()):
        v = [x for x in ps if x is not None and x != float("inf")]
        lines.append(f"  {g}: {len(ps)} files, " + (f"PSNR median {np.median(v):.1f} dB, lowest {min(v):.1f} dB" if v else "no picture differs"))
    worst = sorted([x for x in rows if x[2] is not None and x[2] != float("inf")], key=lambda x: x[2])[:args.worst]
    lines.append(f"worst {len(worst)}:")
    lines += [f"  {r}: {p:.2f} dB, max {mx:.0f}" for r, cls, p, mx in worst]
    lines.append("files:")
    lines += [f"  {r}\t{cls}\t{'' if p is None else ('inf' if p == float('inf') else f'{p:.2f}')}\t{'' if mx is None else f'{mx:.0f}'}" for r, cls, p, mx in rows]
    text = "\n".join(lines) + "\n"
    if args.report:
        with open(args.report, "w", encoding="utf-8") as f: f.write(text)
    print("\n".join(lines[:len(groups) + 3 + len(worst)]))
    return 0


def export(args):
    """`i76 --install DIR menu-export OUT`, through the repository's CLI (dotnet run), or I76_CLI naming a built i76."""
    cli = os.environ.get("I76_CLI")
    cmd = ([cli] if cli else ["dotnet", "run", "--project", os.path.join(HERE, "I76.Cli"), "--"]) + ["--install", args.install, "menu-export", args.out]
    if args.pad_palette: cmd += ["--pad-palette", args.pad_palette]
    print(" ".join(cmd), flush=True)
    return subprocess.call(cmd)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    e = sub.add_parser("export", help="run `i76 menu-export`"); e.add_argument("out"); e.add_argument("--install", required=True)
    e.add_argument("--pad-palette")
    b = sub.add_parser("build", help="make the pack from an export")
    b.add_argument("export", nargs="?", help="the folder `i76 menu-export` wrote"); b.add_argument("--out", required=True); b.add_argument("--work")
    b.add_argument("--install", help="the game's folder: export it first into WORK/export (when EXPORT is not given)")
    b.add_argument("--only", help="globs of pack files, comma-separated (shellbg30.webp, sprites/36_*)")
    b.add_argument("--steps", help="step ids (or groups) to run, comma-separated; default all")
    b.add_argument("--world-export", help="the folder the game's --export-textures wrote (the maps step's input)")
    b.add_argument("--models-dir", default=os.path.join(os.path.expanduser("~/.cache"), "i26", "models"))
    b.add_argument("--device", default="auto", help="cuda, cpu or auto")
    b.add_argument("--recipe", default=RECIPE)
    v = sub.add_parser("verify", help="compare a build with a released zip"); v.add_argument("pack"); v.add_argument("--against", required=True)
    v.add_argument("--report"); v.add_argument("--only"); v.add_argument("--worst", type=int, default=20)
    sub.add_parser("list-steps", help="the recipe's steps")
    args = ap.parse_args()
    if args.cmd == "export": sys.exit(export(args))
    if args.cmd == "build": build(args)
    if args.cmd == "verify": sys.exit(verify(args))
    if args.cmd == "list-steps":
        for s in json.load(open(RECIPE, encoding="utf-8"))["steps"]:
            print(f"{s['id']:<14} {s['kind']:<6} {', '.join(s.get('files', ['*']))[:60]:<60} {s.get('note', '')[:80]}")


if __name__ == "__main__":
    main()
