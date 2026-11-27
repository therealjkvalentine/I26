"""Apply a LUT strip from fit_lut.py to an image, as the game would (trilinear): python apply_lut.py LUT.png IN.png OUT.png"""
import sys, numpy as np
from PIL import Image
from scipy import ndimage
lut = np.asarray(Image.open(sys.argv[1]).convert('RGB'), float) / 255; N = lut.shape[0]
L = np.stack([lut[:, b * N:(b + 1) * N].transpose(1, 0, 2) for b in range(N)], 2)  # [r,g,b,3]
im = np.asarray(Image.open(sys.argv[2]).convert('RGB'), float) / 255
c = np.clip(im * N - 0.5, 0, N - 1).reshape(-1, 3).T
o = np.stack([ndimage.map_coordinates(L[..., k], c, order=1, mode='nearest') for k in range(3)], -1)
Image.fromarray((o.reshape(im.shape) * 255 + 0.5).astype(np.uint8)).save(sys.argv[3])
