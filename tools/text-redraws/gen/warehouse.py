#!/usr/bin/env python3
"""Writes signs-cy_1*.json: the lettering of the Fletcher & Sons warehouse (bcywhse1, T08), which the upscale leaves wobbly
(CARLSBAD read CARLSRAD, the safety board's small words torn): the name board's three lines (cy_1sg11: a condensed old-style
serif, set in Libre Caslon Text, the face whose letter widths fall on the painted ones), the safety board (cy_1sg21) and the
wall's OFFICE (cy_1ofc1) in a bold grotesque (Arimo). Each line: its words and the box of its painted letters, read off the
original to a fraction of a texel (the run's ink is set on it: "box"; a line with a comma's tail is set cap line to baseline,
"quad"); the painted letters are taken off (the texels of the box that look like ink, and the upscale's soft edge round them)
and filled from round them, with the grain of the paint beside them.

    python3 tools/text-redraws/gen/warehouse.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CASLON = {"font": "LibreCaslonText", "variation": {"Weight": 550}}
GROT = {"font": "Arimo", "variation": {"Weight": 700}}
# texture: (what, the ink test for the erase, the halo test, the grain's offset, lines)
# a line: (words, box (x0, y0, x1, y1, the painted ink's extent), face, ink, extra: embolden, bands (cap line to baseline))
SIGNS = {
    "cy_1sg11": ("the Fletcher & Sons name board (T08's warehouse)", {"lum_above": 60}, {"lum_above": 70}, [0, 5], [
        ("FLETCHER & SONS", (12.99, 6.54, 115.38, 23.34), CASLON, [250, 248, 240], {}),
        ("IMPORTS - EXPORTS", (24.84, 26.95, 106.38, 40.23), CASLON, [250, 248, 240], {}),
        # the comma hangs below the baseline: the cap band is set, not the ink
        ("MIDLAND - PLAINS - CARLSBAD, NM", (7.73, 48.81, 120.05, 59.0), CASLON, [250, 248, 240], {"bands": True})]),
    "cy_1sg21": ("the warehouse's safety board", {"lum_below": 200}, {"lum_below": 225}, None, [
        ("SAFETY FIRST", (6.0, 3.0, 58.0, 8.0), GROT, [16, 16, 16], {"embolden": 0.08}),
        ("1 0 3", (11.91, 11.0, 32.16, 17.4), GROT, [16, 16, 16], {}),
        ("DAYS", (37.0, 13.0, 53.0, 17.0), GROT, [16, 16, 16], {}),
        ("WITHOUT LOST-", (7.0, 18.0, 56.7, 23.0), GROT, [16, 16, 16], {}),
        ("TIME ACCIDENT", (6.84, 25.0, 54.34, 28.48), GROT, [16, 16, 16], {})]),
    "cy_1ofc1": ("the warehouse's OFFICE wall", {"lum_below": 215}, {"lum_below": 235}, None, [
        ("OFFICE", (68.77, 31.0, 127.44, 44.23), GROT, [41, 117, 32], {"embolden": 0.06})]),
}
PAD = {"cy_1sg21": (0.6, 0.75), "cy_1sg11": (1.2, 0.9), "cy_1ofc1": (0.9, 1.0)}   # the erase's margin round each box (x, y)

def spec(tex, where, halo, grain, lines):
    px, py = PAD[tex]; w, h = (64, 32) if tex == "cy_1sg21" else (128, 128 if tex == "cy_1ofc1" else 64)
    ops = []
    for t, (x0, y0, x1, y1), *_ , extra in lines:
        e = {"op": "erase", "rect": [max(0, x0 - px), max(0, y0 - py), min(w, x1 + px), min(h, y1 + py + (2.6 if extra.get("bands") else 0))],
             "where": where, "grow": 0.6, "halo": {"within": 0.8, **halo}}
        if grain: e["texture"] = grain
        ops.append(e)
    for t, (x0, y0, x1, y1), face, ink, extra in lines:
        op = {"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": ink}
        if extra.get("embolden"): op["embolden"] = extra["embolden"]     # the painting heavier than the face's boldest
        if extra.get("bands"): op["quad"] = [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]
        else: op["box"] = [x0, y0, x1, y1]
        ops.append(op)
    return ops

if __name__ == "__main__":
    for tex, (what, where, halo, grain, lines) in SIGNS.items():
        s = {"textures": [tex], "note": f"{what}: its lettering; written by gen/warehouse.py", "ops": spec(tex, where, halo, grain, lines)}
        with open(os.path.join(OUT, f"signs-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
