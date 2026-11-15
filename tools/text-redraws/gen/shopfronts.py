#!/usr/bin/env python3
"""Writes signs-cx_1bdf1.json, signs-cg_1b1f2.json and signs-cy_1sg23.json: the lettering of three shop signs that the upscale
reads wrong. Dear Jane
Tractors' board (bcxwhse1) comes out "AUTHORIZED DEALER - SALES B SERVICE" and its STANFILL above the name blurs; Krager Mags'
sign (bcgarag2) melts PERFORMANCE WHEELS AND SUSPENSION under the name into blots; the warehouse's 32 x 16 OFFICE sign comes out
0FFICE with blots in the letters.

Each line is set from a font on its painted ink box, read off the original to a fraction of a texel (the r8 lanes' rule): Dear
Jane's condensed capitals in Archivo Narrow Bold, the name and the two small lines in the name's dark red; Krager's slanted white
capitals in Arimo Bold emboldened 0.2 texel a side, slanted as painted; OFFICE's heavy condensed
capitals in Anton. The painted letters are taken off (with the upscale's soft edge, made opaque) and
filled from the board round them.

    python3 tools/text-redraws/gen/shopfronts.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
NARROW = {"font": "ArchivoNarrow", "variation": {"Weight": 700}}
ARIMO = {"font": "Arimo", "variation": {"Weight": 700}, "embolden": 0.2}    # the painting is heavier than Arimo Bold
# texture: (what, cut: (ink side of the original's mean RGB, cut, halo cut on the pack's),
#           [(words, box in the original's texels, face, ink, slant: x per unit of height)])
SIGNS = {
    "cx_1bdf1": ("Dear Jane Tractors' board (bcxwhse1)", ("below", 200, 215), [
        ("STANFILL", (53.17, 19.62, 76.6, 23.99), NARROW, [112, 7, 8], 0),
        ("DEAR JANE TRACTORS", (21.06, 24.63, 108.98, 31.34), NARROW, [112, 7, 8], 0),
        ("AUTHORIZED DEALER - SALES & SERVICE", (12.28, 33.02, 116.33, 37.64), NARROW, [112, 7, 8], 0)]),
    "cg_1b1f2": ("Krager Mags' sign (bcgarag2)", ("above", 120, 110), [
        ("PERFORMANCE WHEELS", (35.0, 26.28, 93.0, 31.0), ARIMO, [250, 250, 252], 0.2),
        ("AND SUSPENSION", (40.35, 31.74, 85.13, 37.0), ARIMO, [250, 250, 252], 0.2)]),
    "cy_1sg23": ("the Fletcher & Sons warehouse's small OFFICE sign (bcywhse1, T08)", ("above", 150, 140), [
        ("OFFICE", (3.82, 3.24, 27.24, 12.0), {"font": "Anton"}, [252, 246, 246], 0)]),
}


def ops(cut, lines):
    side, oc, hc = cut
    out = []
    for t, (x0, y0, x1, y1), face, ink, slant in lines:
        out.append({"op": "erase", "rect": [round(x0 - 1.0, 2), round(y0 - 0.8, 2), round(x1 + 1.0, 2), round(y1 + 0.8, 2)],
                    "where": {f"lum_{side}": oc}, "grow": 0.6, "halo": {"within": 0.8, f"lum_{side}": hc}, "opaque": True})
    for t, (x0, y0, x1, y1), face, ink, slant in lines:
        op = {"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": ink, "alpha": "set"}
        if slant:
            d = slant * (y1 - y0)
            op["quad"] = [[x0 + d, y0], [x1, y0], [x1 - d, y1], [x0, y1]]; op["fit"] = "ink"
        else: op["box"] = [x0, y0, x1, y1]
        out.append(op)
    return out


if __name__ == "__main__":
    for tex, (what, cut, lines) in SIGNS.items():
        s = {"textures": [tex], "note": f"{what}: its small print; written by gen/shopfronts.py", "ops": ops(cut, lines)}
        with open(os.path.join(OUT, f"signs-{tex}.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
