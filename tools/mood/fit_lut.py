"""Fit a 3D colour LUT taking the modern frames' display colours to the 1997 look's, from matched views.
  python fit_lut.py OUT.png N SRC1.png DST1.png [SRC2 DST2 ...]
Pixel pairs from 1/8-size blurred frames (the two looks' geometry differs in detail); each colour cell's median target,
cells without data from their neighbours (a Gaussian fill), blended to the identity where data is thin.
The PNG is a strip N*N x N: x = r + b*N, y = g; texel i holds f((i + 0.5) / N), as Godot samples the colour correction at the colour."""
import sys, numpy as np
from PIL import Image, ImageFilter
from scipy import ndimage
out, N = sys.argv[1], int(sys.argv[2]); fs = sys.argv[3:]
S, D = [], []
for s, d in zip(fs[0::2], fs[1::2]):
    a = Image.open(s).convert('RGB'); b = Image.open(d).convert('RGB').resize(a.size)
    w, h = a.size[0] // 8, a.size[1] // 8
    a = np.asarray(a.filter(ImageFilter.GaussianBlur(4)).resize((w, h), Image.BOX), float) / 255
    b = np.asarray(b.filter(ImageFilter.GaussianBlur(4)).resize((w, h), Image.BOX), float) / 255
    S.append(a.reshape(-1, 3)); D.append(b.reshape(-1, 3))
S = np.concatenate(S); D = np.concatenate(D)
idx = np.clip((S * N).astype(int), 0, N - 1)
key = (idx[:, 0] * N + idx[:, 1]) * N + idx[:, 2]
acc = np.zeros((N * N * N, 3)); cnt = np.zeros(N * N * N)
# median would be nicer; the mean of the target minus source (a correction field) is smooth and robust enough here
np.add.at(acc, key, D - S); np.add.at(cnt, key, 1)
acc = acc.reshape(N, N, N, 3); cnt = cnt.reshape(N, N, N)
sig = 1.5
num = np.stack([ndimage.gaussian_filter(acc[..., c], sig) for c in range(3)], -1)
den = ndimage.gaussian_filter(cnt, sig)
corr = num / np.maximum(den, 1e-6)[..., None]
conf = np.clip(den / 3.0, 0, 1)[..., None]   # thin data -> identity
# far from any data the correction is whatever the widest blur says, faded
num2 = np.stack([ndimage.gaussian_filter(acc[..., c], 4) for c in range(3)], -1); den2 = ndimage.gaussian_filter(cnt, 4)
corr2 = num2 / np.maximum(den2, 1e-6)[..., None] * np.clip(den2 / 0.5, 0, 1)[..., None]
corr = conf * corr + (1 - conf) * corr2
g = (np.arange(N) + 0.5) / N
R, G, B = np.meshgrid(g, g, g, indexing='ij')
f = np.clip(np.stack([R, G, B], -1) + corr, 0, 1)
img = np.zeros((N, N * N, 3))
for b in range(N): img[:, b * N:(b + 1) * N] = f[:, :, b].transpose(1, 0, 2)   # y = g, x = r
Image.fromarray((img * 255 + 0.5).astype(np.uint8)).save(out)
print(out, len(S), 'pairs, cells with data', int((cnt > 0).sum()))
