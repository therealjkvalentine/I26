#!/usr/bin/env python3
"""Writes pp12bkb1.json and pp13bkb1.json: the Piranha's tail once dented (damage levels 2 and 3), its plate CHAMP and
badge 'rahna as on the undamaged tail (pp11bkb1.json) but bent with the panel: each line set on four corners read off
the dented painting (the plate's main line, the badge), with the 4-point warp ("quad"). The badge's quad is the painted
letters' own ink (their bright texels, the apostrophe's and the h's tops to the baseline), the run's ink set on it ("fit": "ink"),
in the original's greys with its dark rim, the strokes thickened a quarter texel a side to the painting's weight; the keyed strip round it takes the original's black (the model's chrome bled there
showed as a pale box).

    python3 tools/text-redraws/gen/piranha_dented.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
# texture: plate face erase rect, CHAMP quad, badge erase rect, badge ink quad (TL, TR, BR, BL in the original's texels), the keyed
# strip round the badge, the rim's colour (the original's dark texels at the letters' edge)
DENTS = {
    "pp12bkb1": ([55.5, 15, 70.5, 24.6], [[55.6, 18.7], [70.6, 18.5], [70.6, 23.4], [55.6, 23.6]],
                 [77, 15, 93, 23.5], [[79, 17.64], [92, 17.64], [92, 22.14], [79, 22.14]], [73, 14, 120, 28], [28, 29, 30]),
    "pp13bkb1": ([53.5, 15, 70, 24], [[54.2, 18.0], [69.6, 17.6], [69.6, 22.6], [54.2, 23.0]],
                 [76, 15, 92, 23.5], [[78.6, 17.4], [91.0, 18.4], [91.0, 22.05], [78.6, 21.0]], [71, 14, 106, 27], [39, 41, 45]),
}
INK = [28, 28, 34]

def spec(tex, plate_rect, champ, badge_rect, badge, strip, rim):
    return {"textures": [tex], "note": f"the Piranha's dented tail ({tex}): plate CHAMP and badge 'rahna on the bent panel; written by gen/piranha_dented.py",
            "ops": [
                {"op": "erase", "rect": plate_rect, "where": {"lum_below": 128}, "grow": 0.9, "halo": {"within": 0.8, "lum_below": 125}},
                {"op": "text", "text": "CHAMP", "font": "Anton", "cap": 5, "quad": champ, "colour": INK,
                 "shadow": {"dx": 0.25, "dy": 0.25, "colour": [235, 235, 238], "opacity": 0.5}},
                {"op": "text", "text": "TEXAS", "font": "Anton", "cap": 2.1, "colour": [40, 40, 48],
                 "quad": [[champ[0][0] + 4.6, champ[0][1] - 3.6], [champ[1][0] - 3.4, champ[1][1] - 3.6], [champ[1][0] - 3.4, champ[1][1] - 1.5], [champ[0][0] + 4.6, champ[0][1] - 1.5]]},
                {"op": "erase", "rect": strip, "where": {"keyed": True}, "fill": "key", "under": "original"},
                {"op": "erase", "rect": badge_rect, "where": {"opaque": True}, "grow": 0.5, "fill": "key", "under": "original"},
                {"op": "text", "text": "'rahna", "font": "Comfortaa", "variation": {"Weight": 700}, "cap": 4.4, "quad": badge, "fit": "ink", "embolden": 0.25, "alpha": "set",
                 "gradient": [[215, 213, 217], [135, 134, 137]], "stroke": {"width": 0.35, "colour": rim}},
            ]}

if __name__ == "__main__":
    for tex, args in DENTS.items():
        with open(os.path.join(OUT, f"{tex}.json"), "w", encoding="utf-8") as f: json.dump(spec(tex, *args), f, indent=1); f.write("\n")
