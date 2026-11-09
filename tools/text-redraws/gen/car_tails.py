#!/usr/bin/env python3
"""Writes tails-*.json: the AI cars' licence plates and name badges on their tails (the undamaged first paint scheme,
??11bkb1, the one most seen; the other schemes repaint the plate). Plates: the face's painted letters taken off (filled
from the face round them) and set again in Anton, the main line and the state line above it, as the Piranha's plate
(pp11bkb1.json). Badges: the chrome name on the tail panel's key (cut out of the key and lettered opaque, as the
Piranha's badge, the strip's keyed texels the original's black): CAVERA in Cinzel Bold, DOVER (heavy caps spaced wide) in Russo One,
PALOMINO in Bitter Bold (the Mustang's spaced serif capitals). Each badge's letters are set on the painted letters' own ink box (their
bright texels, to a fraction of a texel from the partly lit ones: "box"), in the original's greys, with its dark rim round them.

The plate numbers are read off 4-5 texel paintings and the 4x pack's guesses at them; a reading is marked "?" where
a character is doubtful (the plates are made up, so a near reading reads as well as the original's).

    python3 tools/text-redraws/gen/car_tails.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
INK = [28, 28, 34]
# car: (face x0, y0, x1, y1 inclusive), (state line y0, y1), (main line x0, y0, x1, y1), main text
PLATES = {
    "az11bkb1": ((52, 32, 76, 44), (33, 34), (55, 37, 73, 40), "456 RGL"),
    "cc11bkb1": ((55, 42, 72, 52), (43, 44), (56, 47, 72, 50), "DCK 217"),
    "cg11bkb1": ((52, 43, 75, 50), (44, 45), (54, 47, 73, 50), "616 MSB"),
    "cr11bkb1": ((53, 34, 76, 46), (36, 37), (56, 40, 72, 43), "500 WPL"),
    "dl11bkb1": ((55, 23, 72, 34), (24, 25), (57, 28, 71, 32), "369 JSJ"),
    "pj11bkb1": ((52, 37, 74, 51), (39, 41), (54, 44, 73, 49), "223 AYO"),
}
# badges: texture -> [(text, font, variation, the painted letters' texels x0, y0, x1, y1 (inclusive, erased), their ink box (the
# original's texels, exclusive), tracking, the keyed strip they sit on, the rim's colour, the fill's top and bottom greys, the strokes
# thickened (texels a side) to the painted letters' weight)]
BADGES = {
    "cc11bkb1": [("CAVERA", "Cinzel", {"Weight": 700}, (44, 22, 82, 28), (44.64, 23, 83, 28.13), 0.0, (5, 15, 123, 36), [30, 30, 32], [[200, 198, 201], [140, 138, 142]], 0.12)],
    "dl11bkb1": [("DOVER", "RussoOne", None, (31, 15, 95, 20), (32, 16.71, 95.23, 20.1), 1.9, (13, 12, 114, 22), [32, 32, 34], [[220, 219, 221], [165, 164, 166]], 0.3)],
    "fp11bkb1": [("PALOMINO", "Bitter", {"Weight": 700}, (38, 31, 90, 36), (39, 32.98, 91, 36.06), 0.75, (36, 28, 93, 39), [31, 31, 34], [[200, 198, 201], [115, 112, 116]], 0.2)],
}

def plate(face, state, main, text):
    fx0, fy0, fx1, fy1 = face; mx0, my0, mx1, my1 = main; sy0, sy1 = state
    return [
        {"op": "erase", "rect": [fx0 + 0.5, sy0 - 0.5, fx1 + 0.5, my1 + 1.5], "where": {"lum_below": 128}, "grow": 0.9, "halo": {"within": 0.8, "lum_below": 125}},
        {"op": "text", "text": text, "font": "Anton", "cap": my1 - my0 + 1, "center": [(mx0 + mx1 + 1) / 2, (my0 + my1 + 1) / 2],
         "width": mx1 - mx0 + 1, "colour": INK, "shadow": {"dx": 0.25, "dy": 0.25, "colour": [235, 235, 238], "opacity": 0.5}},
        {"op": "text", "text": "TEXAS", "font": "Anton", "cap": max(1.8, sy1 - sy0 + 0.4), "center": [(fx0 + fx1 + 1) / 2, (sy0 + sy1 + 1) / 2],
         "width": (fx1 - fx0) * 0.38, "colour": [40, 40, 48]},
    ]

def badge(text, font, var, box, ink, track, strip, rim, grey, bold):
    x0, y0, x1, y1 = box
    t = {"op": "text", "text": text, "font": font, "cap": round(ink[3] - ink[1], 2), "box": list(ink),
         "tracking": track, "embolden": bold, "alpha": "set", "gradient": grey, "stroke": {"width": 0.35, "colour": rim}}
    if var: t["variation"] = var
    # the strip is the colour key, which a car's panel draws in the colour under it: the pack bled the old chrome letters'
    # colour there (a pale ghost of them round the new ones), so the whole keyed strip takes the original's colour there (black)
    return [{"op": "erase", "rect": list(strip), "where": {"keyed": True}, "fill": "key", "under": "original"},
            {"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 2, y1 + 2], "where": {"opaque": True}, "grow": 0.5, "fill": "key", "under": "original"}, t]

if __name__ == "__main__":
    for tex in sorted(set(PLATES) | set(BADGES)):
        ops, words = [], []
        if tex in PLATES:
            ops += plate(*PLATES[tex]); words.append(f"plate {PLATES[tex][3]}")
        for b in BADGES.get(tex, []):
            ops += badge(*b); words.append(f"badge {b[0]}")
        s = {"textures": [tex], "note": f"{tex}'s tail: {', '.join(words)}; written by gen/car_tails.py", "ops": ops}
        with open(os.path.join(OUT, f"tails-{tex[:2]}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1); f.write("\n")
