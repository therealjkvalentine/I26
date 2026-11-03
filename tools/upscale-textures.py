#!/usr/bin/env python3
"""Upscale the game's textures on your own machine into the folder I26 reads them from (user://tex-world/).

    1. Export the install's textures as PNGs (the game does it, then quits):
           i26 -- --install DIR --export-textures SRC_DIR
    2. Run the model over them (a one-off; hours on a CPU, minutes on a GPU):
           python3 tools/upscale-textures.py SRC_DIR [--out DIR] [--model FILE] [--device cuda|cpu] [--only GLOB]
    Or repack a pack made earlier (4x PNGs) into the game's fast format without running the model again:
           python3 tools/upscale-textures.py SRC_DIR --from OLD_PACK_DIR --out NEW_DIR

Needs Python 3.10+ with numpy and Pillow, torch and spandrel to run the model, and etcpak for the .dds the game loads fastest
(pip install torch spandrel numpy pillow etcpak). The model is 4xTextures_GTAV_rgt-s_dither by Philip Hofmann (Phhofm),
CC-BY-4.0, downloaded once into ~/.cache/i26/models/. Nothing of the game is in this script: it reads what you exported and
writes textures beside a pack.json.

What it writes: world and car textures at twice the original's size (the model's 4x brought down: 4x is not visible at the
distances they are drawn from, and costs four times the memory and load time), the cockpit's own art (z*) at the model's 4x,
which is drawn near full screen. Each as a BC7 .dds with its mipmaps, keyed (alpha 0 or 255) and with colour bled under the key,
in the texture's stored row order (SRC_DIR/bottom-up.txt), so the game sends it to the GPU as it is; a PNG where etcpak is
missing or the size is not a multiple of 4. The output folder is the game's own (Windows %APPDATA%/I26/tex-world, Linux
~/.local/share/I26/tex-world, macOS ~/Library/Application Support/I26/tex-world) unless --out says otherwise; a texture already
there is skipped, so a stopped run resumes. Only the modern look draws these; Graphic Detail's HD Textures row switches them off.
"""
import argparse, fnmatch, json, os, sys, time, struct, urllib.request
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
    """Give transparent (colour-keyed) texels the colour of their opaque neighbours, so the model and the mipmaps never see the key."""
    rgb = rgb.astype(np.float32); m = alpha > 0
    for _ in range(passes):
        if m.all() or not m.any(): break
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

def finish(big, ow, oh, scale):
    """The model's RGBA (upright) at `scale` times the original (ow x oh): colour Lanczos, alpha box and cut at one half, then the
    colour bled under the key all the way (the mipmaps average it in)."""
    w, h = ow * scale, oh * scale
    if big.shape[1] != w or big.shape[0] != h:
        rgb = np.array(Image.fromarray(big[..., :3]).resize((w, h), Image.LANCZOS))
        a = np.array(Image.fromarray(big[..., 3]).resize((w, h), Image.BOX))
    else:
        rgb, a = big[..., :3], big[..., 3]
    a = np.where(a >= 128, 255, 0).astype(np.uint8)
    rgb = bleed(rgb, a, passes=64)
    if (a == 0).any() and (a > 0).any():
        left = (a == 0) & (rgb.sum(axis=2) == 0)
        if left.any(): rgb[left] = rgb[a > 0].mean(axis=0)
    return np.dstack([np.clip(rgb + 0.5, 0, 255).astype(np.uint8), a])

def write_dds(path, rgba, etcpak):
    """BC7 (sRGB, DX10 header) with its full mip chain, box-filtered."""
    im = Image.fromarray(rgba, "RGBA"); levels = [im]
    while im.size != (1, 1):
        im = im.resize((max(1, im.size[0] // 2), max(1, im.size[1] // 2)), Image.BOX); levels.append(im)
    def bc7(l):
        px = np.array(l); h, w = px.shape[:2]; pw, ph = (w + 3) // 4 * 4, (h + 3) // 4 * 4
        if (pw, ph) != (w, h): px = np.pad(px, ((0, ph - h), (0, pw - w), (0, 0)), mode="edge")   # the last blocks' unused texels
        return etcpak.compress_bc7(np.ascontiguousarray(px).tobytes(), pw, ph)
    blocks = [bc7(l) for l in levels]
    w, h = rgba.shape[1], rgba.shape[0]
    hdr = struct.pack("<4sIIIIIII44x", b"DDS ", 124, 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000, h, w, len(blocks[0]), 0, len(levels))
    hdr += struct.pack("<II4s20x", 32, 0x4, b"DX10")                      # pixel format: a DX10 header follows
    hdr += struct.pack("<IIII4x", 0x1000 | 0x400000 | 0x8, 0, 0, 0)       # caps: texture, mipmap, complex
    dx10 = struct.pack("<IIIII", 99, 3, 0, 1, 0)                           # DXGI_FORMAT_BC7_UNORM_SRGB, a 2D texture
    with open(path, "wb") as f: f.write(hdr + dx10 + b"".join(blocks))

def one(job, run_model=None):
    """One texture into the pack; (name, whether it was written)."""
    n, src, out_dir, frm, max_size, scale, full, flip, dds = job
    stem = n[:-4].lower()
    if any(os.path.exists(os.path.join(out_dir, stem + e)) for e in (".dds", ".png")): return n, False
    im = np.array(Image.open(os.path.join(src, n)).convert("RGBA"))
    oh, ow = im.shape[:2]
    if max(ow, oh) > max_size: return n, False
    if frm:
        p = os.path.join(frm, stem + ".png")
        if not os.path.exists(p): return n, False
        big = np.array(Image.open(p).convert("RGBA"))
    else:
        big = run_model(im)
    scale = min(4 if fnmatch.fnmatch(stem, full.lower()) else scale, big.shape[1] // ow)
    px = finish(big, ow, oh, scale)
    out = os.path.join(out_dir, stem)
    if dds and px.shape[0] % 4 == 0 and px.shape[1] % 4 == 0:
        import etcpak
        write_dds(out + ".part", px[::-1] if flip else px, etcpak); os.replace(out + ".part", out + ".dds")
    else:
        Image.fromarray(px, "RGBA").save(out + ".part.png"); os.replace(out + ".part.png", out + ".png")
    return n, True

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", help="the folder --export-textures wrote")
    ap.add_argument("--out", default=user_dir(), help="where the upscales go (default: the game's tex-world folder)")
    ap.add_argument("--from", dest="frm", help="repack the 4x PNGs of a pack made earlier instead of running the model")
    ap.add_argument("--model", help="a spandrel-loadable model file (default: download " + MODEL + ")")
    ap.add_argument("--device", default=None, help="cuda or cpu (default: cuda when available)")
    ap.add_argument("--only", default="*", help="a glob of texture names to do, e.g. 'pp11*' for one car")
    ap.add_argument("--max-size", type=int, default=512, help="skip textures wider or taller than this (the ground sheets)")
    ap.add_argument("--scale", type=int, default=2, choices=(2, 4), help="the world and car textures' size over the original (default 2)")
    ap.add_argument("--full", default="z*", help="a glob of textures kept at the model's 4x (default z*: the cockpit's own art)")
    ap.add_argument("--format", default="dds", choices=("dds", "png"), help="dds (BC7 with mipmaps, needs etcpak) or png")
    args = ap.parse_args()
    etcpak = None
    if args.format == "dds":
        try: import etcpak
        except ImportError: print("etcpak not installed (pip install etcpak): writing PNGs, which the game loads more slowly")
    bottom_up = set()
    if os.path.exists(os.path.join(args.src, "bottom-up.txt")):
        with open(os.path.join(args.src, "bottom-up.txt")) as f: bottom_up = {l.strip().lower() for l in f if l.strip()}
    elif etcpak: print("no bottom-up.txt in the export (an export from before it): writing PNGs"); etcpak = None
    model = device = torch = None
    if not args.frm:
        import torch, spandrel
        device = args.device or ("cuda" if torch.cuda.is_available() else "cpu")
        model = spandrel.ModelLoader().load_from_file(model_file(args.model)).to(device).eval()
        print(f"model {MODEL} x{model.scale} on {device} ({ATTRIBUTION})")
    names = sorted(n for n in os.listdir(args.src) if n.lower().endswith(".png") and fnmatch.fnmatch(n.lower(), args.only.lower()))
    os.makedirs(args.out, exist_ok=True)
    jobs = [(n, args.src, args.out, args.frm, args.max_size, args.scale, args.full, n[:-4].lower() in bottom_up, etcpak is not None) for n in names]
    done = skipped = 0; t0 = time.time()
    def tick(i, n, ok):
        nonlocal done, skipped
        if not ok: skipped += 1; return
        done += 1; el = time.time() - t0
        print(f"\r{i}/{len(names)} {n}  {el / done:.2f} s each, about {el / done * (len(names) - i) / 60:.0f} min left   ", end="", flush=True)
    if args.frm:
        # no model: the pictures are independent, one process a core
        import multiprocessing
        with multiprocessing.Pool() as pool:
            for i, (n, ok) in enumerate(pool.imap_unordered(one, jobs, chunksize=8), 1): tick(i, n, ok)
    else:
        for i, j in enumerate(jobs, 1): tick(i, j[0], one(j, lambda im: upscale(model, device, torch, im))[1])
    with open(os.path.join(args.out, "pack.json"), "w") as f:
        json.dump({"name": "Upscaled textures (own machine)", "version": "2", "date": time.strftime("%Y-%m-%d"),
                   "model": MODEL, "model_license": "CC-BY-4.0", "attribution": ATTRIBUTION,
                   "scale": args.scale, "full_scale": args.full, "format": "dds-bc7" if etcpak else "png"}, f, indent=2)
    with open(os.path.join(args.out, "ATTRIBUTION.txt"), "w") as f: f.write(ATTRIBUTION + "\n")
    print(f"\n{done} written, {skipped} skipped, in {args.out}")

if __name__ == "__main__":
    main()
