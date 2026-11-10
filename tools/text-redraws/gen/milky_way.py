#!/usr/bin/env python3
"""Writes signs-ao_1sg_6.json: the Milky Way Motel billboard (aobilbd6: M07, T13, T14; the motel has no building of its own in
the missions, the billboard is its one sign). The upscale tears its lettering: the small print (4 to 5 texels: "Out of this World
Comfort", the six bullet points, In ROSWELL, 15 MILES) melts into blots and the name's pixel capitals go ragged.

The name (MILKY WAY over MOTEL) is pixel lettering, drawn on the texel grid with one- and two-texel strokes and shaded in rows
(a light grey top row, a white body, a darker grey bottom row): it is made again from the original's own texels ("marks", one
layer a shade, each texel spread over its pack texels with a smooth outline), so every stroke, gap and shade row stays where it
was painted. The small print is a heavy grotesque, set in Arimo (700, emboldened a little: the painting is heavier) on each
line's painted ink box, read off the original to a fraction of a texel; the bullets apart, each on its own painted dot; the
motto and "Family Run" slanted as painted. Set on the full painted box the small print read crowded beside the original's soft
painting (its ink 5 to 20 % over the original's on the motto, ROSWELL and 15 MILES), so each line is set 5 % smaller about its
box's centre (SMALLER), its emboldening thinned alike: the same centre, a little more sky between the letters. The painted letters are taken off (the texels brighter than the night sky round them,
with the upscale's soft edge) and filled from the sky round them; the stars, the galaxy and the frame are left as they are.

    python3 tools/text-redraws/gen/milky_way.py [SMALLER OUT.json]     (a trial: the small print that much smaller, written to OUT)
"""
import json, os, sys
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FACE = {"font": "Arimo", "variation": {"Weight": 700}}
EMBOLDEN = 0.15   # texels a side at the painted box's size
SMALLER = 0.05    # the small print's size under its painted box (a share), about the box's centre
SLANT = 0.22    # the motto's and "Family Run"'s slant (x per unit of height), as painted
# (words, box: the painted ink's extent, ink, slanted)
LINES = [
    ('"Out of this World Comfort"', (36.0, 24.87, 122.6, 29.85), [224, 222, 228], True),
    ("Kitchenettes", (10.75, 34.33, 43.73, 37.95), [200, 198, 206], False),
    ("Daily & Weekly Rates", (69.03, 34.15, 123.55, 38.68), [201, 201, 208], False),
    ("Direct Dial Phones", (15.0, 40.28, 63.31, 43.99), [186, 186, 193], False),
    ("NMAA Approved", (73.48, 40.14, 116.66, 44.29), [208, 207, 214], False),
    ("Cafe Nearby", (19.06, 46.34, 51.26, 50.84), [194, 191, 194], False),
    ('"Family Run"', (78.3, 46.0, 111.0, 50.57), [188, 187, 193], True),
    ("In ROSWELL", (7.12, 54.98, 45.84, 59.66), [209, 207, 209], False),
    ("15 MILES", (94.0, 55.11, 122.33, 59.65), [214, 213, 216], False),
]
BULLETS = [(7.7, 34.73, 9.49, 37.11), (64.8, 34.74, 67.0, 37.15), (11.5, 40.76, 13.37, 43.11), (69.65, 41.07, 71.43, 43.34),
           (15.17, 46.72, 17.18, 49.13), (73.67, 46.46, 75.61, 49.03), (89.04, 56.04, 91.2, 58.32)]
BULLET_INK = [196, 195, 202]
# the name's two lines (the rect of each: its pixel letters and nothing else; the star right of MOTEL left out)
NAME = [(63.5, 7.3, 115.8, 15.0), (75.5, 14.9, 106.6, 22.5)]
# the name's shades, darkest first: the texels at least this bright, in this colour (the original's rows: about 150, 204, 255)
SHADES = [(90, [150, 150, 156]), (190, [206, 206, 210]), (240, [255, 255, 255])]

def shrink(box, k):
    """The box scaled by k about its centre."""
    x0, y0, x1, y1 = box; cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    return tuple(round(v, 3) for v in (cx - (cx - x0) * k, cy - (cy - y0) * k, cx + (x1 - cx) * k, cy + (y1 - cy) * k))

def ops(smaller=SMALLER):
    out = []
    for r in NAME:
        out.append({"op": "erase", "rect": list(r), "where": {"lum_above": 40}, "grow": 0.6, "halo": {"within": 0.8, "lum_above": 40}})
    for t, (x0, y0, x1, y1), ink, slant in LINES:
        out.append({"op": "erase", "rect": [x0 - 1.2, y0 - 0.8, x1 + 1.2, y1 + 0.8], "where": {"lum_above": 52}, "grow": 0.6,
                    "halo": {"within": 0.8, "lum_above": 46}})
    for x0, y0, x1, y1 in BULLETS:
        out.append({"op": "erase", "rect": [x0 - 1.0, y0 - 0.8, x1 + 1.0, y1 + 0.8], "where": {"lum_above": 52}, "grow": 0.6,
                    "halo": {"within": 0.8, "lum_above": 46}})
    for r in NAME:
        for lum, colour in SHADES:
            out.append({"op": "marks", "rect": list(r), "where": {"lum_above": lum}, "colour": colour, "sigma": 0.35})
    k = 1 - smaller
    for t, box, ink, slant in LINES:
        x0, y0, x1, y1 = shrink(box, k)
        op = {"op": "text", "text": t, **FACE, "embolden": round(EMBOLDEN * k, 3), "cap": round(y1 - y0, 2), "colour": ink}
        if slant:
            # both slanted lines open and close with a quote mark, at the cap line: the box's sides are the top corners
            d = SLANT * (y1 - y0)
            op["quad"] = [[x0, y0], [x1, y0], [x1 - d, y1], [x0 - d, y1]]; op["fit"] = "ink"
        else: op["box"] = [x0, y0, x1, y1]
        out.append(op)
    for x0, y0, x1, y1 in BULLETS:
        out.append({"op": "text", "text": "•", "font": "Arimo", "variation": {"Weight": 700}, "cap": round(y1 - y0, 2),
                    "colour": BULLET_INK, "box": [x0, y0, x1, y1]})
    return out

if __name__ == "__main__":
    smaller, out = (float(sys.argv[1]), sys.argv[2]) if len(sys.argv) > 2 else (SMALLER, os.path.join(OUT, "signs-ao_1sg_6.json"))
    s = {"textures": ["ao_1sg_6"], "note": "the Milky Way Motel billboard (M07, T13, T14): its lettering; written by gen/milky_way.py",
         "ops": ops(smaller)}
    with open(out, "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
