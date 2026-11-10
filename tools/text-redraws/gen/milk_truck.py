#!/usr/bin/env python3
"""Writes milk-truck-*.json: the milk tanker's lettering (vmtanker in its milk scheme, mtanker1.vtf: vxmilk1, s07xm01; S01, S07,
T12), which the upscale leaves torn: the tank's sides read MILK in tall condensed white capitals on a blue panel, with a black
drop shadow one texel right and down (texels the painter keyed, which a car's panel shows in the key's black), and the rear
reads THE ESSENCE OF / PURITY in white on a blue diamond, a darker blue shadow under and right of the strokes. The tank's sides
both map the left side's texture (mt1?bkl1); the far model's sides have one each (mt31bkl1, mt31bkr1, painted alike). Each of
the three dent stages (mt11, mt12, mt13) paints the words in the same place, a shade darker each stage. The cab (the Marx
tractor's textures) carries no lettering of the milk scheme. The far model's rear (mt31bkb1, 32 x 16) paints the words as a
row of single texels: no letters to set, left as it is.

Each word: its painted letters and their shadow taken off (the texels brighter than the panel, and the keyed ones, made opaque),
filled from the panel round them, and set again in Archivo Narrow (700: of the free faces tried, its letters fall closest on the
painted ones) on the painted letters' ink box, read off the original, with the shadow drawn as painted.

    python3 tools/text-redraws/gen/milk_truck.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FACE = {"font": "ArchivoNarrow", "variation": {"Weight": 700}}
# per stage: (the panel's blue, the letters' white), read off the original (lum and colour)
STAGE = {"1": (73, [255, 255, 255]), "2": (64, [242, 238, 242]), "3": (52, [203, 197, 203])}
REAR_INK = {"1": [255, 255, 255], "2": [231, 225, 231], "3": [192, 188, 192]}
KEY_BLACK = [0, 0, 0]
REAR_SHADOW = [0, 40, 84]

def word(text, box, ground, ink, shadow, pad=1.0, grow=0.6):
    x0, y0, x1, y1 = box
    rect = [x0 - pad, y0 - pad, x1 + pad + 1, y1 + pad]
    cut = round(ground + 0.45 * (sum(ink) / 3 - ground))
    ops = [{"op": "erase", "rect": rect, "where": {"lum_above": cut}, "grow": grow, "halo": {"within": 0.8, "lum_above": cut - 10}, "opaque": True},
           {"op": "erase", "rect": rect, "where": {"keyed": True}, "grow": 0.6, "opaque": True}]
    t = {"op": "text", "text": text, **FACE, "cap": round(y1 - y0, 2), "colour": ink, "box": [x0, y0, x1, y1], "shadow": shadow}
    return ops, [t]

def spec(parts):
    erase, text = [], []
    for p in parts:
        e, t = word(*p); erase += e; text += t
    return erase + text

SPECS = {}
for st, (ground, ink) in STAGE.items():
    SPECS[f"mt1{st}bkl1"] = ("the tank's side (both sides map it)",
                             [("MILK", (44.0, 21.0, 80.0, 41.0), ground, ink, {"dx": 1, "dy": 1, "colour": KEY_BLACK, "opacity": 1.0})])
    sh = {"dx": 0.8, "dy": 0.8, "colour": REAR_SHADOW, "opacity": 0.8}
    SPECS[f"mt1{st}bkb1"] = ("the tank's rear",
                             [("THE ESSENCE OF", (12.0, 27.0, 119.0, 34.0), ground, REAR_INK[st], sh, 1.0, 1.1),
                              ("PURITY", (44.0, 37.0, 88.0, 44.0), ground, REAR_INK[st], sh, 1.0, 1.1)])
for side in ("l", "r"):
    SPECS[f"mt31bk{side}1"] = (f"the far model's {'left' if side == 'l' else 'right'} side",
                               [("MILK", (22.0, 10.0, 40.0, 20.0), 73, [255, 255, 255], {"dx": 1, "dy": 1, "colour": KEY_BLACK, "opacity": 1.0})])

if __name__ == "__main__":
    for tex, (what, parts) in SPECS.items():
        s = {"textures": [tex], "note": f"the milk tanker's {what}: its lettering; written by gen/milk_truck.py", "ops": spec(parts)}
        with open(os.path.join(OUT, f"milk-truck-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
