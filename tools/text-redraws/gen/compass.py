#!/usr/bin/env python3
"""Writes compass-zcme.json: the compass's heading strip (zcme, 303 x 18: N NE E SE S SW W NW N and the ticks between),
which the cockpit's compass (zcm_) shows through a 30-texel window that moves along it with the heading (vpit_1.elt's
left/right, every car's layout, so one strip serves every dash). The upscale leaves its letters smeared and its ticks soft.
The strip is made again from the original: its plain grey ground and bevel from the original's texels (smoothed, no letters),
the ticks as the original's texel columns, crisp, and the letters set from a font on the painted letters' ink boxes, read
off the original to a fraction of a texel, so every letter sits on the original's column and the heading reads as before.
The painting is a bold grotesque for the four points (11 x 13 texels; its S lighter), a lighter one for the half points
(5 x 7, strokes a texel wide): Arimo, at 700 and 600.

    python3 tools/text-redraws/gen/compass.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INK = [16, 16, 16]
BIG = {"font": "Arimo", "variation": {"Weight": 700}}
SMALL = {"font": "Arimo", "variation": {"Weight": 600}}
BIG_S = SMALL          # the painted S is lighter than N, E and W (its arcs two texels, its spine one)
# the letters: (letter, ink box x0, y0, x1, y1 in the original's texels, face)
LETTERS = [("N", (14, 3, 25, 16), BIG), ("N", (44, 6, 49, 13), SMALL), ("E", (54, 6, 59, 13), SMALL),
           ("E", (79, 3, 89, 16), BIG), ("S", (109, 6, 114, 13), SMALL), ("E", (118, 6, 123, 13), SMALL),
           ("S", (146, 2, 157, 16), BIG_S), ("S", (176, 6, 181, 13), SMALL), ("W", (184, 6, 193, 13), SMALL),
           ("W", (207.59, 3, 225, 16), BIG), ("N", (244, 6, 249, 13), SMALL), ("W", (253, 6, 262, 13), SMALL),
           ("N", (278, 3, 289, 16), BIG)]
# the ticks' columns: the short ones (rows 5 to 13), the long ones between a half point's two letters (rows 2 to 16, the
# top texel a shade lighter where it meets the bevel)
SHORT = [2, 35, 67, 99, 131, 166, 198, 235, 267, 299]
LONG = [51, 115, 182, 251]

def ops():
    out = [{"op": "smooth", "rect": [0, 0, 303, 2], "sigma": 0.3},
           {"op": "smooth", "rect": [0, 2, 303, 18], "where": {"lum_above": 190}, "sigma": 0.3}]
    for x in SHORT: out.append({"op": "erase", "rect": [x, 5, x + 1, 14], "fill": INK})
    for x in LONG:
        out.append({"op": "erase", "rect": [x, 3, x + 1, 17], "fill": INK})
        out.append({"op": "erase", "rect": [x, 2, x + 1, 3], "fill": [33, 33, 33]})
    for t, (x0, y0, x1, y1), face in LETTERS:
        out.append({"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "box": [x0, y0, x1, y1], "colour": INK})
    return out

if __name__ == "__main__":
    s = {"textures": ["zcme"], "note": "the compass's heading strip: ground, ticks and letters made again on the original's places; written by gen/compass.py",
         "ops": ops()}
    with open(os.path.join(OUT, "compass-zcme.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
