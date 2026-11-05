#!/usr/bin/env python3
"""Writes map-*.json: the mission maps' hand lettering (zmap6*, the 640x480 maps the modern look shows; the 320x200 zmap3*
twins are the 1997 low-resolution set) set again in Permanent Marker (Apache 2.0), the felt-tip capitals the maps are
lettered in. Each label: the words, the box of its painted letters (the original's texels), its slope (degrees,
anticlockwise), its ink (dark or red); the painted letters are taken off (inpainted, the paper's grain given back from
beside the label) and the words set on the same box.

Only T01 and T02 so far: a trial of the look (the 4x pack already draws the maps' 8-12 texel lettering legibly, and a
font in place of the hand reads as a font). Add a map: its labels below, then run this and tools/text-redraws.py.

    python3 tools/text-redraws/gen/maps.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DARK, RED = [34, 28, 24], [118, 8, 18]
# map: [(text, (x0, y0, x1, y1), slope, ink, grain offset [dx, dy])]
MAPS = {
    "zmap6t01": [
        ("WAGON", (112, 168, 176, 180), 0, RED, [0, -14]),
        ("WHEEL", (131, 190, 193, 204), 0, RED, [0, -12]),
        ("DINER", (156, 213, 210, 227), 0, RED, [0, 16]),
        ("FIREWORKS", (351, 154, 443, 171), -3, RED, [0, -16]),
        ("STAND", (384, 177, 439, 194), -2, RED, [24, 0]),
        ("SEAGRAVES", (325, 296, 462, 318), -2, DARK, [0, 22]),
        ("SHORTCUT", (122, 349, 212, 362), 0, DARK, [0, -14]),
    ],
    "zmap6t02": [
        ("LEVELAND", (178, 50, 284, 78), 9, DARK, [0, 30]),
        ("SCHOOL", (400, 38, 460, 58), -9, RED, [0, 22]),
        ("OUTLAW", (322, 132, 392, 148), -3, RED, [0, -14]),
        ("TERRITORY", (346, 158, 440, 174), -4, RED, [10, 18]),
        ("BROWN-", (184, 185, 276, 207), -4, DARK, [0, 26]),
        ("FIELD", (200, 218, 260, 236), 0, DARK, [-30, 0]),
    ],
}

def spec(labels):
    ops, txt = [], []
    for text, (x0, y0, x1, y1), slope, ink, grain in labels:
        ops.append({"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 2, y1 + 2], "where": {"lum_below": 92}, "grow": 1.0,
                    "halo": {"within": 1.0, "lum_below": 110}, "opaque": True, "texture": grain})
        # the box's height includes the slope's rise: the caps are the box's height less it
        import math
        w = x1 - x0 + 1; rise = abs(math.tan(math.radians(slope))) * w
        cap = max(6.0, (y1 - y0 + 1) - rise) * 0.92
        txt.append({"op": "text", "text": text, "font": "PermanentMarker", "cap": round(cap, 1), "center": [(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2],
                    "width": round(w / math.cos(math.radians(slope)) * 0.97, 1), "rotate": slope, "colour": ink})
    return ops + txt

if __name__ == "__main__":
    for m, labels in MAPS.items():
        s = {"textures": [m], "note": f"{m}'s hand lettering: {', '.join(l[0] for l in labels)}; written by gen/maps.py", "ops": spec(labels)}
        with open(os.path.join(OUT, "maps", f"{m}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1); f.write("\n")
