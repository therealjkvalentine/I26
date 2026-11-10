#!/usr/bin/env python3
"""Writes signs-ca_1ws_1.json: the Autowerks compound's wall sign (T12's industrial complex, bcaikwl4: the plate on the wall
piece with the sign), which the upscale leaves torn (the R, I and A of INDUSTRIAL and the M and P of KOMPLEX broken up). The
plate maps the texture's band (rows 25 to 39, the whole width) onto a flat quad: AUTOWERKS in a bold upright grotesque, and
INDUSTRIAL over KOMPLEX in the same face at half the size, light grey on the plate's navy. The navy behind the words is the
plate's own colour, the same as the rest of the texture: the original paints no darker panel there.

Each line: its words and the box of its painted letters, read off the original to a fraction of a texel ("box": the run's
ink set on it); the painted letters are taken off (the texels that look like ink, and the upscale's soft edge round them) and
filled from the navy round them. Arimo: of the free grotesques tried on the painted letters (Arimo, Archivo Narrow, Russo
One, Anton), its letters fall closest on the painted ones (700 for AUTOWERKS; 600 for the small lines, whose painting is
lighter).

    python3 tools/text-redraws/gen/autowerks.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INK = [218, 212, 218]
# (words, box (x0, y0, x1, y1): the painted ink's extent, weight)
LINES = [
    ("AUTOWERKS", (4.99, 27.41, 77.89, 36.62), 700),
    ("INDUSTRIAL", (89.43, 27.41, 122.16, 31.73), 600),
    ("KOMPLEX", (95.36, 32.72, 122.07, 36.86), 600),
]
# the erase: each group's rect inside the plate's band (its bevelled rows 24 and 39 left as painted)
ERASE = [(3.4, 26.3, 79.6, 37.8), (87.8, 26.3, 124.2, 37.9)]

def ops():
    out = [{"op": "erase", "rect": list(r), "where": {"lum_above": 60}, "grow": 0.6, "halo": {"within": 0.8, "lum_above": 52}}
           for r in ERASE]
    for t, (x0, y0, x1, y1), w in LINES:
        out.append({"op": "text", "text": t, "font": "Arimo", "variation": {"Weight": w}, "cap": round(y1 - y0, 2),
                    "colour": INK, "box": [x0, y0, x1, y1]})
    return out

if __name__ == "__main__":
    s = {"textures": ["ca_1ws_1"], "note": "the Autowerks compound's wall sign (T12): its lettering; written by gen/autowerks.py",
         "ops": ops()}
    with open(os.path.join(OUT, "signs-ca_1ws_1.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
