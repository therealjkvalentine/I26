#!/usr/bin/env python3
"""Writes zsye.json: the damage display's part labels (ENGINE, SUSP., BRAKES, WHEEL) in its five states, columns 61
texels apart: white, green, yellow, red (dark letters on the colour; the label's box taken off whole, the upscale's
soft edge with it) and off (dark grey letters on black: only the letters taken off).

    python3 tools/text-redraws/gen/damage_labels.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LABELS = [("ENGINE", 11, 1, 49, 9), ("SUSP.", 16, 13, 45, 21), ("BRAKES", 8, 25, 54, 33), ("WHEEL", 1, 37, 36, 45)]
INK = [150, 95, 100, 60]        # the four lit states' brightness under which a texel is ink

def spec():
    ops, txt = [], []
    for c in range(5):
        dx, dark = 61 * c, c == 4
        for s, x0, y0, x1, y1 in LABELS:
            if dark:
                ops.append({"op": "erase", "rect": [x0 + dx - 0.5, y0 - 0.5, x1 + dx + 0.5, y1 + 0.5], "where": {"lum_above": 24, "opaque": True}, "grow": 0.9})
            else:
                ops.append({"op": "erase", "rect": [x0 + dx - 0.6, y0 - 0.6, x1 + dx + 0.6, y1 + 0.6], "halo": {"within": 1.5, "lum_below": INK[c]}})
            txt.append({"op": "text", "text": s, "font": "RussoOne", "cap": 8, "center": [(x0 + x1) / 2 + dx, (y0 + y1) / 2], "width": x1 - x0,
                        "colour": [42, 42, 42] if dark else [14, 14, 14]})
    return {"textures": ["zsye"], "note": "the damage display's part labels (ENGINE, SUSP., BRAKES, WHEEL) in its five states: white, green, yellow, red, off; written by gen/damage_labels.py",
            "ops": ops + txt}

if __name__ == "__main__":
    with open(os.path.join(OUT, "zsye.json"), "w", encoding="utf-8") as f: json.dump(spec(), f, indent=1); f.write("\n")
