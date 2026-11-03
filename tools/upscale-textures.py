#!/usr/bin/env python3
"""Upscale the game's textures on your own machine into the folder I26 reads them from (user://tex-world/).

    1. Export the install's textures as PNGs (the game does it, then quits):
           i26 -- --install DIR --export-textures SRC_DIR
    2. Run the model over them (a one-off; hours on a CPU, minutes on a GPU):
           python3 tools/upscale-textures.py SRC_DIR [--out DIR] [--model FILE] [--device cuda|cpu] [--only GLOB]

Needs Python 3.10+ with torch, spandrel, numpy and Pillow (pip install torch spandrel numpy pillow). The model is
4xTextures_GTAV_rgt-s_dither by Philip Hofmann (Phhofm), CC-BY-4.0, downloaded once into ~/.cache/i26/models/.
Nothing of the game is in this script: it reads what you exported and writes PNGs beside a pack.json.
The output folder is the game's own (Windows %APPDATA%/I26/tex-world, Linux ~/.local/share/I26/tex-world, macOS
~/Library/Application Support/I26/tex-world) unless --out says otherwise; a texture already there is skipped, so a
stopped run resumes. Only the modern look draws these; the HD Menu Art setting switches them off.
"""
import argparse, fnmatch, json, os, sys, time, urllib.request
import numpy as np
from PIL import Image

MODEL = "4xTextures_GTAV_rgt-s_dither"
MODEL_URL = f"https://github.com/Phhofm/models/releases/download/{MODEL}/{MODEL}.pth"
ATTRIBUTION = f"{MODEL} by Philip Hofmann (Phhofm), CC-BY-4.0, https://openmodeldb.info/models/4x-Textures-GTAV-rgt-s-dither"
PAD = 16   # reflect padding, against ringing at the edges

def user_dir():
    if sys.platform == "win32": return os.path.join(os.environ.get("APPDATA", ""), "I26", "tex-world")
    if sys.platform == "darwin": return os.path.expanduser("~/Library/Application Support/I26/tex-world")
    return os.path.join(os.environ.get("XDG_DATA_HOME", os.path.expanduser("~/.local/share")), "I26", "tex-world")

def model_file(path):
    if path: return path
    cache = os.path.join(os.path.expanduser("~/.cache"), "i26", "models")
    os.makedirs(cache, exist_ok=True)
    p = os.path.join(cache, MODEL + ".pth")
    if not os.path.exists(p):
        print(f"downloading {MODEL_URL}")
        urllib.request.urlretrieve(MODEL_URL, p + ".part"); os.replace(p + ".part", p)
    return p

def bleed(rgb, alpha, passes=8):
    """Give transparent (colour-keyed) texels the colour of their opaque neighbours, so the model never sees the key."""
    rgb = rgb.astype(np.float32); m = alpha > 0
    for _ in range(passes):
        if m.all(): break
        pad = np.pad(rgb, ((1, 1), (1, 1), (0, 0)), mode="edge"); mp = np.pad(m, 1, mode="edge")
        acc = np.zeros_like(rgb); cnt = np.zeros(m.shape, np.float32)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                s = pad[1 + dy:1 + dy + rgb.shape[0], 1 + dx:1 + dx + rgb.shape[1]]
                sm = mp[1 + dy:1 + dy + m.shape[0], 1 + dx:1 + dx + m.shape[1]]
                acc += s * sm[..., None]; cnt += sm
        fill = (~m) & (cnt > 0)
        rgb[fill] = acc[fill] / cnt[fill][..., None]; m = m | fill
    return rgb

def upscale(model, device, torch, im):
    """RGBA in, RGBA at the model's scale out: colour through the model, alpha scaled on its own and cut at one half."""
    a = im[..., 3]; rgb = bleed(im[..., :3], a) / 255.0
    x = torch.from_numpy(np.pad(rgb, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="reflect")).permute(2, 0, 1)[None].float().to(device)
    with torch.no_grad(): y = model(x)
    s = model.scale
    y = y[0].permute(1, 2, 0).clamp(0, 1).cpu().numpy()[PAD * s:-PAD * s, PAD * s:-PAD * s]
    A = np.array(Image.fromarray(a).resize((a.shape[1] * s, a.shape[0] * s), Image.BILINEAR))
    return np.dstack([(y * 255 + 0.5).astype(np.uint8), np.where(A >= 128, 255, 0).astype(np.uint8)])

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", help="the folder --export-textures wrote")
    ap.add_argument("--out", default=user_dir(), help="where the upscales go (default: the game's tex-world folder)")
    ap.add_argument("--model", help="a spandrel-loadable model file (default: download " + MODEL + ")")
    ap.add_argument("--device", default=None, help="cuda or cpu (default: cuda when available)")
    ap.add_argument("--only", default="*", help="a glob of texture names to do, e.g. 'pp11*' for one car")
    ap.add_argument("--max-size", type=int, default=512, help="skip textures wider or taller than this (the ground sheets)")
    args = ap.parse_args()
    import torch, spandrel
    device = args.device or ("cuda" if torch.cuda.is_available() else "cpu")
    model = spandrel.ModelLoader().load_from_file(model_file(args.model)).to(device).eval()
    print(f"model {MODEL} x{model.scale} on {device} ({ATTRIBUTION})")
    names = sorted(n for n in os.listdir(args.src) if n.lower().endswith(".png") and fnmatch.fnmatch(n.lower(), args.only.lower()))
    os.makedirs(args.out, exist_ok=True)
    done = skipped = 0; t0 = time.time()
    for i, n in enumerate(names, 1):
        out = os.path.join(args.out, n.lower())
        if os.path.exists(out): skipped += 1; continue
        im = np.array(Image.open(os.path.join(args.src, n)).convert("RGBA"))
        if max(im.shape[:2]) > args.max_size: skipped += 1; continue
        Image.fromarray(upscale(model, device, torch, im), "RGBA").save(out + ".part.png")
        os.replace(out + ".part.png", out); done += 1
        el = time.time() - t0
        print(f"\r{i}/{len(names)} {n}  {el / done:.1f} s each, about {el / done * (len(names) - i) / 60:.0f} min left   ", end="", flush=True)
    with open(os.path.join(args.out, "pack.json"), "w") as f:
        json.dump({"name": "Upscaled textures (own machine)", "version": "1", "date": time.strftime("%Y-%m-%d"),
                   "model": MODEL, "model_license": "CC-BY-4.0", "attribution": ATTRIBUTION}, f, indent=2)
    with open(os.path.join(args.out, "ATTRIBUTION.txt"), "w") as f: f.write(ATTRIBUTION + "\n")
    print(f"\n{done} upscaled, {skipped} skipped, in {args.out}")

if __name__ == "__main__":
    main()
