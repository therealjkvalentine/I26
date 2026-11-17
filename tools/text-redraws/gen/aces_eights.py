#!/usr/bin/env python3
"""Writes signs-ao_1sg_5.json: the Aces & Eights Motor Lodge billboard (aobilbd5: M07, M10, T10, T14). The upscale leaves its
serif lettering ragged and its two italic lines (TV - Phones - Pets OK, RV Parking - Truckers Welcome) and the red band's
"U.S. 380 to Plains - 14 miles" smeared into blots.

Every line is set again from a serif face on its painted ink box, read off the original to a fraction of a texel (the r8 lanes'
rule: a side at the last texel half covered plus the next one's share; the box takes ascenders and descenders, so the words are
set with the same ones), in the painted colours: ACES & in red, EIGHTS and Motor Lodge in black, the two italic lines slanted as
painted, the band's line in dark red-black on the red. The painted letters are taken off (the texels darker than the board, or
than the red band, with the upscale's soft edge; the painter's few keyed texels in the ink made opaque) and filled from round
them; the cards, the frame and the band are left as they are.

    python3 tools/text-redraws/gen/aces_eights.py [FACE WEIGHT OUT.json]     (a trial in another face)
"""
import json, os, sys
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FACE = {"font": "LibreCaslonText", "variation": {"Weight": 700}}
SLANT = 0.22      # the italic lines' slant (x per unit of height), as painted
# (words, box: the painted ink's extent in the original's texels, ink, slanted, ground: "board" | "band")
LINES = [
    ("ACES &", (56.28, 7.63, 90.96, 13.96), [232, 14, 14], False, "board"),
    ("EIGHTS", (85.14, 15.59, 121.1, 21.75), [18, 18, 16], False, "board"),
    ("Motor Lodge", (62.99, 24.26, 116.6, 31.37), [24, 24, 22], False, "board"),
    ("TV - Phones - Pets OK", (6.84, 37.98, 80.94, 43.29), [30, 28, 28], True, "board"),
    ("RV Parking - Truckers Welcome", (14.45, 44.73, 122.62, 50.88), [30, 28, 28], True, "board"),
    ("U.S. 380 to Plains - 14 miles", (8.4, 54.58, 122.68, 60.72), [24, 2, 2], False, "band"),
]
# the ink is darker than the white board (mean RGB about 250) or than the red band (about 90); halo on the pack's texels
CUT = {"board": (200, 215), "band": (55, 68)}


def ops(face=FACE):
    out = []
    for t, (x0, y0, x1, y1), ink, slant, ground in LINES:
        oc, hc = CUT[ground]
        pad = 0.6 if ground == "band" else 1.0       # the band's dark edge rows lie a texel off the line
        out.append({"op": "erase", "rect": [round(x0 - 1.0, 2), round(y0 - pad, 2), round(x1 + 1.0, 2), round(y1 + pad, 2)],
                    "where": {"lum_below": oc}, "grow": 0.6, "halo": {"within": 0.8, "lum_below": hc}, "opaque": True})
    for t, (x0, y0, x1, y1), ink, slant, ground in LINES:
        op = {"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": ink, "alpha": "set"}
        if slant:
            d = SLANT * (y1 - y0)
            op["quad"] = [[x0 + d, y0], [x1, y0], [x1 - d, y1], [x0, y1]]; op["fit"] = "ink"
        else: op["box"] = [x0, y0, x1, y1]
        out.append(op)
    return out


if __name__ == "__main__":
    face, out = FACE, os.path.join(OUT, "signs-ao_1sg_5.json")
    if len(sys.argv) > 3: face, out = {"font": sys.argv[1], "variation": {"Weight": int(sys.argv[2])}}, sys.argv[3]
    s = {"textures": ["ao_1sg_5"], "note": "the Aces & Eights Motor Lodge billboard (M07, M10, T10, T14): its lettering; written by "
         "gen/aces_eights.py", "ops": ops(face)}
    with open(out, "w", encoding="utf-8", newline="\n") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
