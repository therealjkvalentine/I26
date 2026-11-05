#!/usr/bin/env python3
"""Upscale the game's textures on your own machine into the folder I26 reads them from (user://tex-world-Nx/, N the --scale: 2, 3 or 4; sizes can sit side by side and hd_texture_size picks one).

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
which is drawn near full screen; the cockpit's lettering and frames that are pixel art (--pixel: the weapon and special
labels, the ammunition digits, the mirror's frame) not through the model but smoothed in their own colours (pixel_scale), as
the model recolours thin strokes and leaves stair steps. The model's output (or the --from pack's) is colour matched back to
the original before it is written: the model shifts broad colour and brightness (darker reds, lighter dashboards), so its
blurred picture is brought to the original's blurred picture in Oklab and the model's fine detail kept (--no-colour-match
skips it; the --pixel art keeps its own colours and is not touched). Each as a BC7 .dds with its mipmaps, keyed (alpha 0 or 255) and with colour bled under the key,
in the texture's stored row order (SRC_DIR/bottom-up.txt), so the game sends it to the GPU as it is; a PNG where etcpak is
missing or the size is not a multiple of 4. The output folder is the game's own (Windows %APPDATA%/I26/tex-world-Nx, Linux
~/.local/share/I26/tex-world-Nx, macOS ~/Library/Application Support/I26/tex-world-Nx) unless --out says otherwise; a texture already
there is skipped, so a stopped run resumes. Only the modern look draws these; Graphic Detail's HD Textures row switches them off.

The cockpit by family (--families): the cockpit's art suits different models, so --families runs each family of z* textures
through its own colour model (the table COCKPIT below, or --family-table FILE.json with the same rows) and, for the art
drawn as a keyed shape, the alpha step:
           python3 tools/upscale-textures.py SRC_DIR --families --models-dir DIR [--from 8X_MASTERS] --only "z*" --max-size 640
The family models are not downloaded: put them in --models-dir (default ~/.cache/i26/models); a missing one stops the run and
names its source. A family without a colour model of its own takes the run's colour (--from, else --model). --families
overrides --pixel for the textures it lists. The alpha step (--alpha for every texture, or a family's alpha column): the
game's keyed textures have a binary alpha, so a keyed shape's outline (dial ticks and numerals, needles, the reticle) is
otherwise the alpha enlarged bilinear and cut at one half, stair-stepped whatever the colour model; with it the alpha goes
through 4xHDcube-Alpha (by Venomalia, made for alpha: grey in all three channels, reflect-padded and tiled like the
colour), and the model's output, the three channels averaged, is cut at one half at the written size. Colour is bled
under the new key as always. Models run on tiles (--tile, 128 px by default, the context padded 16 px) and in half
precision on a GPU where the model allows it; --gpu-share F idles F times each tile's time after it, for a GPU shared
with other work. pack.json and ATTRIBUTION.txt name every model used. Lettering painted a few texels high stays soft from any
model: tools/text-redraws.py sets it again from fonts in the finished pack (its --help says how).
"""
import argparse, fnmatch, json, os, sys, time, struct, urllib.request
import numpy as np
from PIL import Image

MODEL = "4xTextures_GTAV_rgt-s_dither"
MODEL_URL = f"https://github.com/Phhofm/models/releases/download/{MODEL}/{MODEL}.pth"
ATTRIBUTION = f"{MODEL} by Philip Hofmann (Phhofm), CC-BY-4.0, https://openmodeldb.info/models/4x-Textures-GTAV-rgt-s-dither"
PAD = 16   # reflect padding, against ringing at the edges
ALPHA_MODEL = "4xHDcube-Alpha.pth"
# model file -> (name, author, licence, source); NC = non-commercial: such a pack is for your own use
MODELS = {
    MODEL + ".pth": (MODEL, "Philip Hofmann (Phhofm)", "CC-BY-4.0", "https://openmodeldb.info/models/4x-Textures-GTAV-rgt-s-dither"),
    "4xHDcube3_500k.pth": ("4xHDcube3_500k", "Venomalia", "CC-BY-NC-SA-4.0 (non-commercial)", "https://github.com/Venomalia/HDcube/releases/download/3.0/4xHDcube3_500k.pth"),
    "4x-UltraSharpV2.safetensors": ("4x-UltraSharpV2", "Kim2091", "CC-BY-NC-SA-4.0 (non-commercial)", "https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2.safetensors"),
    "4xHDCube4Compact.pth": ("4xHDcube4Compact", "Venomalia", "HDcube licence (the author claims no rights in outputs)", "https://github.com/Venomalia/HDcube/releases/download/4.0/4xHDcube4Compact.zip"),
    ALPHA_MODEL: ("4xHDcube-Alpha", "Venomalia", "HDcube licence (the author claims no rights in outputs)", "https://github.com/Venomalia/HDcube/releases/download/4.0/4xHDcube4Compact.zip (Alpha folder)"),
}
# The cockpit's families: (family, name globs, colour model file or None = the run's colour (--from, else --model), alpha step)
COCKPIT = [
    ("radar", "zradf*,zradb*,zradmask", "4xHDcube3_500k.pth", False),
    ("panels", "zdash*", "4x-UltraSharpV2.safetensors", False),
    ("gauges", "zspeedo*,ztach*", None, True),
    ("needles", "zneedle*", None, True),
    ("gear", "zgear*", "4x-UltraSharpV2.safetensors", False),
    ("compass", "zcm_*,zcme*", "4x-UltraSharpV2.safetensors", False),
    ("damage", "zsy_*,zsye*", "4xHDcube3_500k.pth", False),
    ("weapons", "zwpe*,zbk_*,zbks_*,zdde*,zdse*,zdue*,zsle*,zrge*,ztge*,zbar_*", "4xHDcube3_500k.pth", False),
    ("mirror", "zmiri*,zmiro*", "4x-UltraSharpV2.safetensors", False),
    ("notepad", "znpd*,zhpd*,zcbh*", "4xHDCube4Compact.pth", False),
    ("hands", "zswls*,zhl45*,zhr45*,zsl45*,zsr45*", "4xHDcube3_500k.pth", False),
    ("misc", "zretc*,ztarg*,zteam,zbinc", None, True),
]

def user_dir(scale=2):
    if sys.platform == "win32": return os.path.join(os.environ.get("APPDATA", ""), "I26", f"tex-world-{scale}x")
    if sys.platform == "darwin": return os.path.expanduser(f"~/Library/Application Support/I26/tex-world-{scale}x")
    return os.path.join(os.environ.get("XDG_DATA_HOME", os.path.expanduser("~/.local/share")), "I26", f"tex-world-{scale}x")

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

TILE = 128      # --tile: model input tiles (each with PAD of context), so no pass is larger than TILE + 2 PAD
GPU_SHARE = 0.0  # --gpu-share: idle this many times each tile's time after it

def run_tiled(model, device, torch, x):
    """float HxWx3 in 0..1 -> float at the model's scale, through the model tile by tile (reflect-padded, each tile with
    PAD texels of context on every side), in the precision the model was loaded in (load_model)."""
    s = model.scale; h, w = x.shape[:2]
    dtype = next(model.model.parameters()).dtype
    P = np.pad(x.astype(np.float32), ((PAD, PAD), (PAD, PAD), (0, 0)), mode="reflect")
    out = np.zeros((h * s, w * s, 3), np.float32)
    for y in range(0, h, TILE):
        for x0 in range(0, w, TILE):
            th, tw = min(TILE, h - y), min(TILE, w - x0)
            t = torch.from_numpy(np.ascontiguousarray(P[y:y + th + 2 * PAD, x0:x0 + tw + 2 * PAD])).permute(2, 0, 1)[None].to(device)
            t = t.to(dtype)
            t0 = time.time()
            with torch.no_grad(): Y = model(t)
            Y = Y[0].float().permute(1, 2, 0).clamp(0, 1).cpu().numpy()
            if GPU_SHARE > 0: time.sleep((time.time() - t0) * GPU_SHARE)
            out[y * s:(y + th) * s, x0 * s:(x0 + tw) * s] = Y[PAD * s:(PAD + th) * s, PAD * s:(PAD + tw) * s]
    return out

def upscale(model, device, torch, im):
    """RGBA in, RGBA at the model's scale out: colour through the model, alpha scaled on its own and cut at one half."""
    a = im[..., 3]; s = model.scale
    y = run_tiled(model, device, torch, np.clip(bleed(im[..., :3], a) + 0.5, 0, 255).astype(np.uint8) / 255.0)
    A = np.array(Image.fromarray(a).resize((a.shape[1] * s, a.shape[0] * s), Image.BILINEAR))
    return np.dstack([(y * 255 + 0.5).astype(np.uint8), np.where(A >= 128, 255, 0).astype(np.uint8)])

def load_model(spandrel, torch, path, device):
    """A spandrel model on the device, in half precision on a GPU where the model allows it."""
    m = spandrel.ModelLoader().load_from_file(path).to(device).eval()
    if device != "cpu" and m.supports_half: m = m.half()
    return m

def alpha_up(model, device, torch, a):
    """The alpha step: a texture's alpha (0..255) through the alpha model, grey in all three channels; the channels'
    mean, 0..1 at the model's scale (cut at one half where it is written, by finish)."""
    return run_tiled(model, device, torch, np.repeat(a[..., None].astype(np.float32) / 255.0, 3, -1)).mean(-1)

def pixel_scale(im, s=4):
    """Pixel art at s x in its own colours: a bicubic enlargement, then each texel takes the closest of the 3 x 3 original
    texels around it (alpha counted), so diagonals and corners come out rounded with no colour the art does not have."""
    h, w = im.shape[:2]
    sm = np.array(Image.fromarray(im, "RGBA").resize((w * s, h * s), Image.BICUBIC)).astype(np.int32)
    pad = np.pad(im, ((1, 1), (1, 1), (0, 0)), mode="edge").astype(np.int32)
    oy, ox = np.mgrid[0:h * s, 0:w * s] // s
    best = bd = None
    for dy in (0, -1, 1):
        for dx in (0, -1, 1):
            c = pad[oy + 1 + dy, ox + 1 + dx]
            d = ((c - sm) ** 2).sum(axis=2)
            if best is None: best, bd = c, d            # the texel's own colour first: it wins ties
            else:
                m = d < bd; best = np.where(m[..., None], c, best); bd = np.where(m, d, bd)
    return best.astype(np.uint8)

# Oklab (Bjorn Ottosson's), from and to sRGB 0..255
OK_M1 = np.array([[0.4122214708, 0.5363325363, 0.0514459929], [0.2119034982, 0.6806995451, 0.1073969566], [0.0883024619, 0.2817188376, 0.6299787005]])
OK_M2 = np.array([[0.2104542553, 0.7936177850, -0.0040720468], [1.9779984951, -2.4285922050, 0.4505937099], [0.0259040371, 0.7827717662, -0.8086757660]])

def to_oklab(rgb):
    c = rgb.astype(np.float64) / 255.0
    c = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return np.cbrt(c @ OK_M1.T) @ OK_M2.T

def from_oklab(lab):
    c = np.clip(((lab @ np.linalg.inv(OK_M2).T) ** 3) @ np.linalg.inv(OK_M1).T, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055) * 255

def gauss(a, sigma):
    """Separable Gaussian blur of an (h, w, c) array, edges repeated."""
    r = max(1, int(3 * sigma + 0.5)); k = np.exp(-np.arange(-r, r + 1) ** 2 / (2 * sigma * sigma)); k /= k.sum()
    for ax in (0, 1):
        pad = [(0, 0)] * a.ndim; pad[ax] = (r, r); p = np.pad(a, pad, mode="edge"); n = a.shape[ax]
        a = sum(k[i] * p.take(range(i, i + n), axis=ax) for i in range(2 * r + 1))
    return a

def colour_match(big, orig, sigma=1.5):
    """The upscale's broad colour put back to the original's: the upscale brought down to the original's grid (box) and the
    original, both blurred (sigma in original texels), their difference in Oklab enlarged (bicubic) and added to the upscale.
    The model's fine detail stays, the regional colour and brightness are the original's; alpha is kept. `orig` is the
    original's RGBA; returns `big` unchanged if it is not a whole multiple of the original's size."""
    oh, ow = orig.shape[:2]; H, W = big.shape[:2]; s = W // ow
    if s < 1 or (H, W) != (oh * s, ow * s): return big
    o = bleed(orig[..., :3], orig[..., 3])
    b = big[..., :3].astype(np.float32)
    if (big[..., 3] == 0).any(): b = bleed(b, big[..., 3])
    B = to_oklab(b)
    d = gauss(to_oklab(o), sigma) - gauss(B.reshape(oh, s, ow, s, 3).mean(axis=(1, 3)), sigma)
    D = np.stack([np.array(Image.fromarray(d[..., c].astype(np.float32), "F").resize((W, H), Image.BICUBIC)) for c in range(3)], -1)
    rgb = np.clip(from_oklab(B + D) + 0.5, 0, 255).astype(np.uint8)
    return np.dstack([rgb, big[..., 3]])

def alpha_ok(big, h, w, s):
    """finish() with an alpha step's output: a disc at twice the written size comes back as the disc, cut at one half."""
    yy, xx = np.mgrid[0:h * s * 2, 0:w * s * 2]; disc = (((yy - h * s) ** 2 + (xx - w * s) ** 2) < (h * s * 0.6) ** 2).astype(np.float32)
    px = finish(big, w, h, s, alpha=disc * 0.8)          # 0.8 > 0.5: the disc opaque, the rest keyed
    want = np.array(Image.fromarray(disc * 0.8, "F").resize((w * s, h * s), Image.BILINEAR)) >= 0.5
    return np.array_equal(px[..., 3] == 255, want) and set(np.unique(px[..., 3])) <= {0, 255} and px[..., :3][~want].sum() > 0

def self_check():
    """Colour match on a made-up texture: a model-like upscale with a colour cast and fine detail comes back to the original's
    colour, keeps its detail and its alpha."""
    rng = np.random.default_rng(76)
    h, w, s = 24, 32, 4
    orig = np.dstack([np.clip(gauss(rng.uniform(30, 220, (h, w, 3)), 2.0), 0, 255).astype(np.uint8), np.full((h, w), 255, np.uint8)])
    orig[:4, :6, 3] = 0; orig[:4, :6, :3] = 0                             # a keyed corner, black under the key as exported
    near = np.repeat(np.repeat(orig[..., :3].astype(np.float64), s, 0), s, 1)
    detail = rng.normal(0, 6, near.shape[:2])[..., None]
    cast = np.array([-25.0, 10.0, 18.0])
    big = np.dstack([np.clip(near + detail + cast, 0, 255).astype(np.uint8), np.repeat(np.repeat(orig[..., 3], s, 0), s, 1)])
    m = orig[..., 3] > 0
    def err(x):
        down = to_oklab(x[..., :3]).reshape(h, s, w, s, 3).mean(axis=(1, 3))
        return float(np.sqrt(((down - to_oklab(orig[..., :3])) ** 2).sum(-1))[m].mean() * 100)
    def hf(x):
        L = to_oklab(x[..., :3])[..., :1]; return L - gauss(L, s)
    out = colour_match(big, orig)
    rt = np.abs(from_oklab(to_oklab(orig[..., :3])) - orig[..., :3]).max()
    e0, e1 = err(big), err(out)
    keep = float(np.corrcoef(                                            # rows below the keyed corner
        hf(big)[24:-8, 8:-8].ravel(), hf(out)[24:-8, 8:-8].ravel())[0, 1])
    checks = [("Oklab round trip within half a level", rt < 0.5),
              (f"colour error {e0:.2f} -> {e1:.2f} (under a third)", e1 < e0 / 3),
              (f"fine detail kept (correlation {keep:.3f} > 0.95)", keep > 0.95),
              ("alpha unchanged", np.array_equal(out[..., 3], big[..., 3])),
              ("a size that is not a multiple is left alone", np.array_equal(colour_match(big[:-1], orig), big[:-1])),
              ("the alpha step's alpha is cut at one half at the written size, colour bled under it", alpha_ok(big, h, w, s))]
    for name, ok in checks: print(("ok   " if ok else "FAIL ") + name)
    return all(ok for _, ok in checks)

def matches(stem, globs):
    return any(fnmatch.fnmatch(stem, g.strip().lower()) for g in globs.split(",") if g.strip())

def finish(big, ow, oh, scale, alpha=None):
    """The model's RGBA (upright) at `scale` times the original (ow x oh): colour Lanczos, alpha box and cut at one half, then the
    colour bled under the key all the way (the mipmaps average it in). `alpha`: the alpha step's output (0..1, any size),
    taken to the written size bilinear and cut at one half in place of big's alpha."""
    w, h = ow * scale, oh * scale
    if big.shape[1] != w or big.shape[0] != h:
        rgb = np.array(Image.fromarray(big[..., :3]).resize((w, h), Image.LANCZOS))
        a = np.array(Image.fromarray(big[..., 3]).resize((w, h), Image.BOX))
    else:
        rgb, a = big[..., :3], big[..., 3]
    if alpha is not None:
        A = alpha if alpha.shape == (h, w) else np.array(Image.fromarray(alpha.astype(np.float32), "F").resize((w, h), Image.BILINEAR))
        a = np.where(A >= 0.5, 255, 0)
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

class Runner:
    """The models of a run, loaded when first needed (two kept at a time, the jobs sorted by family): run(im, file) is
    upscale() through that colour model (None: the run's own, --model), alpha(a) the alpha step."""
    def __init__(self, device, default, models_dir, alpha_file):
        import torch, spandrel
        self.torch, self.spandrel, self.device = torch, spandrel, device
        self.default, self.models_dir, self.alpha_file, self.loaded, self.used = default, models_dir, alpha_file, {}, []
    def model(self, f):
        if f is None: f = self.default = model_file(self.default)       # the run's own model (downloaded once if needed)
        path = f if os.path.isabs(f) or f == self.default else os.path.join(self.models_dir, f)
        if path not in self.loaded:
            if not os.path.exists(path):
                src = MODELS.get(os.path.basename(path), (None, None, None, "its author's release"))[3]
                sys.exit(f"\nmodel {path} not found: download it from {src} into --models-dir")
            while len(self.loaded) >= 2: self.loaded.pop(next(iter(self.loaded)))
            if self.device != "cpu": self.torch.cuda.empty_cache()
            self.loaded[path] = load_model(self.spandrel, self.torch, path, self.device)
            if os.path.basename(path) not in self.used: self.used.append(os.path.basename(path))
        return self.loaded[path]
    def run(self, im, f=None):
        return upscale(self.model(f), self.device, self.torch, im)
    def alpha(self, a):
        return alpha_up(self.model(self.alpha_file), self.device, self.torch, a)

def one(job, run_model=None):
    """One texture into the pack; (name, whether it was written). job[11], when there: (colour model file or None, alpha
    step on, listed by --families); run_model: a Runner, or a function of the RGBA original (the run's model)."""
    n, src, out_dir, frm, max_size, scale, full, pixel, flip, dds, cm = job[:11]
    fam = job[11] if len(job) > 11 else None
    stem = n[:-4].lower()
    if any(os.path.exists(os.path.join(out_dir, stem + e)) for e in (".dds", ".png")): return n, False
    im = np.array(Image.open(os.path.join(src, n)).convert("RGBA"))
    oh, ow = im.shape[:2]
    pix = matches(stem, pixel) and not (fam and fam[2])
    if max(ow, oh) > max_size and not pix: return n, False
    run = run_model.run if isinstance(run_model, Runner) else (lambda im, f=None: run_model(im))
    if pix:
        big = pixel_scale(im, 4)
    elif fam and fam[0]:
        big = run(im, fam[0])
    elif frm:
        p = os.path.join(frm, stem + ".png")
        if not os.path.exists(p): return n, False
        big = np.array(Image.open(p).convert("RGBA"))
    else:
        big = run(im)
    if cm and not pix: big = colour_match(big, im)
    a = im[..., 3]
    alpha = run_model.alpha(a) if fam and fam[1] and not pix and (a == 0).any() and (a > 0).any() else None
    scale = min(4 if matches(stem, full) else scale, big.shape[1] // ow)
    px = finish(big, ow, oh, scale, alpha)
    out = os.path.join(out_dir, stem)
    if dds and px.shape[0] % 4 == 0 and px.shape[1] % 4 == 0:
        import etcpak
        write_dds(out + ".part", px[::-1] if flip else px, etcpak); os.replace(out + ".part", out + ".dds")
    else:
        Image.fromarray(px, "RGBA").save(out + ".part.png"); os.replace(out + ".part.png", out + ".png")
    return n, True

def family_of(stem, table, alpha_all):
    """(colour model file or None, alpha step, listed) for a texture: its row of the family table, else the run's."""
    for name, globs, colour, alpha in table:
        if matches(stem, globs): return (colour, alpha or alpha_all, True), name
    return ((None, True, False), None) if alpha_all else (None, None)

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src", nargs="?", help="the folder --export-textures wrote")
    ap.add_argument("--out", default=None, help="where the upscales go (default: the game's tex-world-Nx folder, N the --scale)")
    ap.add_argument("--from", dest="frm", help="repack the 4x PNGs of a pack made earlier instead of running the model")
    ap.add_argument("--model", help="a spandrel-loadable model file (default: download " + MODEL + ")")
    ap.add_argument("--device", default=None, help="cuda or cpu (default: cuda when available)")
    ap.add_argument("--only", default="*", help="a glob of texture names to do, e.g. 'pp11*' for one car")
    ap.add_argument("--max-size", type=int, default=512, help="skip textures wider or taller than this (the ground sheets)")
    ap.add_argument("--scale", type=int, default=2, choices=(2, 3, 4), help="the world and car textures' size over the original (default 2)")
    ap.add_argument("--full", default="z*", help="globs (comma-separated) of textures kept at the model's 4x (default z*: the cockpit's own art)")
    ap.add_argument("--pixel", default="zdue*,zdse*,znbe*,zmir*", help="globs of pixel art scaled 4x in its own colours instead of by the model, whatever"
                    " their size (default: the weapon and special labels, the ammunition digits, the mirror's frames)")
    ap.add_argument("--no-colour-match", dest="cm", action="store_false", help="write the model's colours as they come (default: its broad"
                    " colour matched back to the original's, its detail kept)")
    ap.add_argument("--self-check", action="store_true", help="test the colour match on a made-up texture and quit")
    ap.add_argument("--format", default="dds", choices=("dds", "png"), help="dds (BC7 with mipmaps, needs etcpak) or png")
    ap.add_argument("--families", action="store_true", help="the cockpit by family: each family of z* art through its own colour model"
                    " and, where the table says, the alpha step (the table COCKPIT in this script)")
    ap.add_argument("--family-table", help="a JSON file of [family, globs, colour model file or null, alpha step] rows to use instead (implies --families)")
    ap.add_argument("--models-dir", default=os.path.join(os.path.expanduser("~/.cache"), "i26", "models"),
                    help="where the family models and the alpha model are (default ~/.cache/i26/models; not downloaded)")
    ap.add_argument("--alpha", action="store_true", help="the alpha step for every keyed texture (default: only the families whose row says so)")
    ap.add_argument("--alpha-model", default=ALPHA_MODEL, help="the alpha model's file (default " + ALPHA_MODEL + " in --models-dir)")
    ap.add_argument("--tile", type=int, default=128, help="model input tile size in texels (default 128; each tile also gets 16 texels of context)")
    ap.add_argument("--gpu-share", type=float, default=0.0, help="after each tile idle this many times its time (for a GPU shared with other work)")
    args = ap.parse_args()
    global TILE, GPU_SHARE
    TILE, GPU_SHARE = max(16, args.tile), max(0.0, args.gpu_share)
    if args.out is None: args.out = user_dir(args.scale)
    if args.self_check: sys.exit(0 if self_check() else 1)
    if not args.src: ap.error("the folder --export-textures wrote is needed")
    table = []
    if args.family_table:
        with open(args.family_table) as f: table = [tuple(r) for r in json.load(f)]
    elif args.families: table = COCKPIT
    etcpak = None
    if args.format == "dds":
        try: import etcpak
        except ImportError: print("etcpak not installed (pip install etcpak): writing PNGs, which the game loads more slowly")
    bottom_up = set()
    if os.path.exists(os.path.join(args.src, "bottom-up.txt")):
        with open(os.path.join(args.src, "bottom-up.txt")) as f: bottom_up = {l.strip().lower() for l in f if l.strip()}
    elif etcpak: print("no bottom-up.txt in the export (an export from before it): writing PNGs"); etcpak = None
    names = sorted(n for n in os.listdir(args.src) if n.lower().endswith(".png") and fnmatch.fnmatch(n.lower(), args.only.lower()))
    fams = {n: family_of(n[:-4].lower(), table, args.alpha) for n in names}
    order = {row[0]: i for i, row in enumerate(table)}
    names.sort(key=lambda n: (order.get(fams[n][1], len(order)), n))              # a family's textures together: its model loads once
    needs_model = not args.frm or any(f and (f[0] or f[1]) for f, _ in fams.values())
    runner = None
    if needs_model:
        import torch
        device = args.device or ("cuda" if torch.cuda.is_available() else "cpu")
        runner = Runner(device, args.model, args.models_dir, args.alpha_model)
        print(f"models on {device}, {TILE} px tiles" + (f", families: {', '.join(r[0] for r in table)}" if table else "") + (", alpha step on every keyed texture" if args.alpha else ""))
    os.makedirs(args.out, exist_ok=True)
    jobs = [(n, args.src, args.out, args.frm, args.max_size, args.scale, args.full, args.pixel, n[:-4].lower() in bottom_up, etcpak is not None, args.cm)
            + ((fams[n][0],) if fams[n][0] else ()) for n in names]
    done = skipped = 0; t0 = time.time()
    def tick(i, n, ok):
        nonlocal done, skipped
        if not ok: skipped += 1; return
        done += 1; el = time.time() - t0
        print(f"\r{i}/{len(names)} {n}  {el / done:.2f} s each, about {el / done * (len(names) - i) / 60:.0f} min left   ", end="", flush=True)
    if not needs_model:
        # no model: the pictures are independent, one process a core
        import multiprocessing
        with multiprocessing.Pool() as pool:
            for i, (n, ok) in enumerate(pool.imap_unordered(one, jobs, chunksize=8), 1): tick(i, n, ok)
    else:
        for i, j in enumerate(jobs, 1): tick(i, j[0], one(j, runner)[1])
    used = [MODEL + ".pth"] if not args.frm else []
    used += [os.path.basename(m) for m in (runner.used if runner else []) if os.path.basename(m) not in used]
    models = [{"file": f, "model": MODELS.get(f, (f,))[0], "by": MODELS.get(f, (0, "?"))[1], "license": MODELS.get(f, (0, 0, "see its source"))[2],
               "source": MODELS.get(f, (0, 0, 0, "?"))[3]} for f in used]
    attribution = [f"{m['model']} by {m['by']}, {m['license']}, {m['source']}" for m in models]
    if args.frm: attribution.insert(0, f"colour of the textures without a model of their own: the --from pack ({args.frm}) and its models")
    with open(os.path.join(args.out, "pack.json"), "w") as f:
        json.dump({"name": "Upscaled textures (own machine)", "version": "2", "date": time.strftime("%Y-%m-%d"),
                   "model": MODEL, "model_license": "; ".join(f"{m['model']}: {m['license']}" for m in models) or "CC-BY-4.0",
                   "attribution": " | ".join(attribution) or ATTRIBUTION, "models": models,
                   "families": [{"family": r[0], "globs": r[1], "colour": r[2] or ("--from" if args.frm else MODEL), "alpha_step": bool(r[3])} for r in table],
                   "alpha_step_all": args.alpha, "scale": args.scale, "full_scale": args.full, "pixel": args.pixel, "colour_match": args.cm,
                   "format": "dds-bc7" if etcpak else "png"}, f, indent=2)
    with open(os.path.join(args.out, "ATTRIBUTION.txt"), "w") as f: f.write("\n".join(attribution or [ATTRIBUTION]) + "\n")
    print(f"\n{done} written, {skipped} skipped, in {args.out}")

if __name__ == "__main__":
    main()
