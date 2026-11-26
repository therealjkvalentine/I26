"""Sky texture upscale recipes for the mood comps (outputs in scratch only; no game art in the repo).
  python sky_upscale.py ORIGINAL.ppm|png OUTDIR STEM
Writes STEM-lanczos4.png (smooth 4x, no invented detail) and STEM-snap4.png (4x Lanczos, then each texel snapped back to the
original's own colours with a 4x4 ordered dither: the painted bands and their exact colours kept, edges 4x finer)."""
import sys, numpy as np
from PIL import Image
src, out, stem = sys.argv[1:4]
im = Image.open(src).convert('RGB'); w, h = im.size
# wrap-pad so the tiling sky stays seamless
a = np.asarray(im); pad = np.pad(a, ((8, 8), (8, 8), (0, 0)), mode='wrap')
big = Image.fromarray(pad).resize(((w + 16) * 4, (h + 16) * 4), Image.LANCZOS)
big = np.asarray(big)[32:32 + h * 4, 32:32 + w * 4].astype(float)
Image.fromarray(np.clip(big, 0, 255).astype(np.uint8)).save(f'{out}/{stem}-lanczos4.png')
cols = np.unique(a.reshape(-1, 3), axis=0).astype(float)
bayer = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.47
H, W = big.shape[:2]
d = big + np.tile(bayer, (H // 4, W // 4))[..., None] * 10.0
flat = d.reshape(-1, 3); idx = np.empty(len(flat), int)
for s in range(0, len(flat), 65536):
    idx[s:s + 65536] = ((flat[s:s + 65536, None, :] - cols[None]) ** 2).sum(-1).argmin(1)
Image.fromarray(cols[idx].reshape(H, W, 3).astype(np.uint8)).save(f'{out}/{stem}-snap4.png')
print(stem, len(cols), 'colours')
