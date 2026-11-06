#!/usr/bin/env python3
"""Writes zsye.json: the damage display's part labels (ENGINE, SUSP., BRAKES, WHEEL) in its five states, columns 61
texels apart: white, green, yellow, red (each light shaded again from the original, its dither blurred into a smooth fade,
then dark letters on the colour; the label's box taken off whole, the upscale's soft edge with it) and off (dark grey letters on black: only the letters taken off).

Each word is centred on its light, not on where the 1997 art painted it (which sits 1-2 texels high): the light is the
sprite the cockpit stamps into the panel's window (vpit_1.elt's engine_off ... tire_drk: 61 x 12 texels for ENGINE, SUSP.
and BRAKES, 38 x 12 for WHEEL, whose last row is black, so its lit rectangle is 38 x 11), the same in all five states.
The centring is optical: the cap height's middle on the light's middle (Russo One has no descenders in these words), and
SUSP.'s full stop hangs outside the centred run. The width is the painted letters' (without the stop).

    python3 tools/text-redraws/gen/damage_labels.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# word, the painted letters' box (x0, y0, x1, y1 in the white column, inclusive, the stop included: what is erased),
# the letters' width without the stop, the lit rectangle (x, y, w, h)
LABELS = [("ENGINE", (11, 1, 50, 9), 40, (0, 0, 61, 12)),
          ("SUSP.", (16, 13, 45, 21), 28, (0, 13, 61, 12)),
          ("BRAKES", (8, 25, 54, 33), 45, (0, 25, 61, 12)),
          ("WHEEL", (1, 37, 36, 45), 34, (0, 37, 38, 11))]
INK = [150, 95, 100, 60]        # the four lit states' brightness under which a texel is ink
CAP = 8                         # the painted capitals' height
SIGMA = 1.5                     # the lit lights' fade: the blur (texels) that takes the original's dither to a smooth shade

def spec():
    ops, txt = [], []
    # the four lit states' lights shaded again from the original: its few dithered levels blurred into a fade (a model made
    # the dither at the lights' ends a mottle, torn fringes in the colours); the letters are left out and set again below
    for c in range(4):
        for s, _, _, (bx, by, bw, bh) in LABELS:
            ops.append({"op": "smooth", "rect": [bx + 61 * c, by, bx + 61 * c + bw, by + bh], "sigma": SIGMA, "where": {"lum_above": 40}})
    for c in range(5):
        dx, dark = 61 * c, c == 4
        for s, (x0, y0, x1, y1), width, (bx, by, bw, bh) in LABELS:
            if dark:
                ops.append({"op": "erase", "rect": [x0 + dx - 0.5, y0 - 0.5, x1 + dx + 1.5, y1 + 1.5], "where": {"lum_above": 24, "opaque": True}, "grow": 0.9})
            else:
                ops.append({"op": "erase", "rect": [x0 + dx - 0.6, y0 - 0.6, x1 + dx + 1.6, y1 + 1.6], "halo": {"within": 1.5, "lum_below": INK[c]}})
            t = {"op": "text", "text": s, "font": "RussoOne", "cap": CAP, "center": [bx + dx + bw / 2, by + bh / 2], "width": width,
                 "colour": [42, 42, 42] if dark else [14, 14, 14]}
            if s.endswith("."): t["hang"] = "."
            txt.append(t)
    return {"textures": ["zsye"], "note": "the damage display's part labels (ENGINE, SUSP., BRAKES, WHEEL) in its five states, centred on their lights; written by gen/damage_labels.py",
            "ops": ops + txt}

if __name__ == "__main__":
    with open(os.path.join(OUT, "zsye.json"), "w", encoding="utf-8") as f: json.dump(spec(), f, indent=1); f.write("\n")
