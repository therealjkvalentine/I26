#!/usr/bin/env python3
"""Writes school-bus.json: the school bus's rear (xb11bkb1, and the dented xb12bkb1 and xb13bkb1): SCHOOL BUS, EMERGENCY
EXIT, STOP WHEN / RED LIGHTS / FLASH. The painting's letters carry texels of the colour key inside their strokes, which the
bus's panel draws see-through ("W IEN", "RFD LIGI" in play); each line's box is taken off whole, its keyed texels made
part of the panel ("opaque"), filled from the paint round it, and the words set again in D-DIN Condensed Bold, black, on
the painted letters' boxes.

    python3 tools/text-redraws/gen/school_bus.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INK = [22, 18, 14]
# text, the painted letters' box (x0, y0, x1, y1 inclusive, the original's texels), per damage level: the dented tails
# (xb12bkb1, xb13bkb1) carry the same words, the lower block shifted up and bent a little on xb13
LINES = {
    "xb11bkb1": [("SCHOOL BUS", (36, 5, 90, 17)), ("EMERGENCY EXIT", (32, 53, 89, 58)),
                 ("STOP WHEN", (35, 71, 93, 77)), ("RED LIGHTS", (35, 80, 95, 86)), ("FLASH", (48, 88, 79, 94))],
    "xb12bkb1": [("SCHOOL BUS", (36, 5, 90, 17)), ("EMERGENCY EXIT", (32, 54, 88, 58)),
                 ("STOP WHEN", (35, 71, 93, 77)), ("RED LIGHTS", (35, 80, 94, 86)), ("FLASH", (48, 88, 73, 94))],
    "xb13bkb1": [("SCHOOL BUS", (36, 4, 90, 16)), ("EMERGENCY EXIT", (32, 54, 88, 58)),
                 ("STOP WHEN", (35, 69, 94, 75)), ("RED LIGHTS", (35, 78, 94, 84)), ("FLASH", (47, 86, 73, 93))],
}

def spec(textures, lines):
    ops = [{"op": "erase", "rect": [x0 - 0.6, y0 - 0.6, x1 + 1.6, y1 + 1.6], "opaque": True,
            "halo": {"within": 1.0, "lum_below": 90}} for _, (x0, y0, x1, y1) in lines]
    ops += [{"op": "text", "text": t, "font": "DDINCondensed", "cap": y1 - y0 + 1, "center": [(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2],
             "width": x1 - x0 + 1, "colour": INK} for t, (x0, y0, x1, y1) in lines]
    return {"textures": textures, "note": "the school bus's rear lettering, its see-through holes closed; written by gen/school_bus.py", "ops": ops}

if __name__ == "__main__":
    for tex, lines in LINES.items():
        name = "school-bus.json" if tex == "xb11bkb1" else f"school-bus-{tex}.json"
        with open(os.path.join(OUT, name), "w", encoding="utf-8") as f: json.dump(spec([tex], lines), f, indent=1); f.write(chr(10))
