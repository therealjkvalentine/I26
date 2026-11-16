#!/usr/bin/env python3
"""Writes signs-ao_1sg_7.json: the Giant Robot billboard (aobilbd7, T14: "see the WORLD'S LARGEST GIANT ROBOT"). The upscale
reads its lettering wrong: 54 comes out S4, Ahead Ahood, 285 2BS, "see the" ooo tho, TRUCKERS WELCOME a row of blots, GIANT
BIANT, ROBOT RDBOT; and the painter keyed a texel here and there in the black ink, which the pack bleeds white, so WORLD'S and
LARGEST are holed with white specks.

Every line is set again from a font on its painted ink box, read off the original to a fraction of a texel (the r8 lanes' rule:
a side at the last texel half covered plus the next one's share): the small print (54 mi. Ahead, Off 285 North, FOOD,
SOUVENIERS (the board's own spelling), TRUCKERS, WELCOME, see the) in a rounded heavy sans (Nunito Black), WORLD'S and LARGEST
in a wide heavy grotesque (Archivo Black), GIANT and ROBOT, ten texels of condensed capitals, in Anton; each matched against the
painting on a sheet of candidates. The painted letters are taken off (the texels darker than the white board, the keyed ones
among them made opaque, with the upscale's soft edge) and filled from the board round them; the lettering is opaque where it
covers half a texel. The robot, the frame and the board's shading are left as they are.

    python3 tools/text-redraws/gen/giant_robot.py [OUT.json]
"""
import json, os, sys
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SMALL = {"font": "Nunito", "variation": {"Weight": 900}}
HEAVY = {"font": "ArchivoBlack"}
DISPLAY = {"font": "Anton"}
INK = [14, 13, 13]
# (words, box: the painted ink's extent in the original's texels, face, emboldening in texels a side). GIANT stops left of
# the robot's legs.
LINES = [
    ("54 mi. Ahead", (4.11, 3.35, 53.23, 7.68), SMALL, 0.0),
    ("Off 285 North", (3.62, 9.3, 55.13, 14.33), SMALL, 0.0),
    ("FOOD", (92.57, 2.92, 112.33, 7.09), SMALL, 0.0),
    ("SOUVENIERS", (78.73, 8.63, 125.2, 12.64), SMALL, 0.0),
    ("TRUCKERS", (80.47, 16.29, 106.14, 19.07), SMALL, 0.0),
    ("WELCOME", (101.47, 19.99, 124.83, 22.65), SMALL, 0.0),
    ("see the", (21.99, 23.24, 47.64, 27.45), SMALL, 0.0),
    ("WORLD'S", (6.64, 29.51, 55.28, 36.35), HEAVY, 0.0),
    ("LARGEST", (6.49, 37.94, 55.98, 44.45), HEAVY, 0.0),
    ("GIANT", (22.74, 47.47, 62.64, 57.64), DISPLAY, 0.0),
    ("ROBOT", (83.12, 47.51, 124.36, 57.49), DISPLAY, 0.0),
]
GROUND = 190      # the board is lighter than this; the ink darker
RIGHT = 125.6     # the frame's inner edge: no erase reaches it


def ops():
    out = []
    for t, (x0, y0, x1, y1), face, eb in LINES:
        out.append({"op": "erase", "rect": [round(x0 - 1.0, 2), round(y0 - 0.9, 2), round(min(x1 + 1.0, RIGHT), 2), round(y1 + 0.9, 2)],
                    "where": {"lum_below": GROUND}, "grow": 0.6, "halo": {"within": 0.8, "lum_below": GROUND + 20}, "opaque": True})
    for t, (x0, y0, x1, y1), face, eb in LINES:
        op = {"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": INK, "box": [x0, y0, x1, y1], "alpha": "set"}
        if eb: op["embolden"] = eb
        out.append(op)
    return out


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(OUT, "signs-ao_1sg_7.json")
    s = {"textures": ["ao_1sg_7"], "note": "the Giant Robot billboard (aobilbd7): its lettering; written by gen/giant_robot.py",
         "ops": ops()}
    with open(out, "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
