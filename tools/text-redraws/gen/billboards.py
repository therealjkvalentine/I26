#!/usr/bin/env python3
"""Writes signs-ao_1sg_3.json, signs-ao_1sg_a.json, signs-ao_1sg_b.json: the small print of three roadside billboards, which the
upscale melts (3 to 7 texels: Gas Parade's "32 miles - at Ropesville" comes out "32 milos - at Ropoosvillo", the Foxy Cat's
"GOOD FOOD - CHEAP BEER" lines and the Gila Truck Stop's side columns, LOWEST PRICE #2 DIESEL and CIGS CHEW BEER, turn to blots).
The large lettering (the names, 8 texels and more) upscales cleanly and is left as it is.

Each line is set from a font on its painted ink box, read off the original to a fraction of a texel (the r8 lanes' rule: a side at
the last texel half covered plus the next one's share; the box takes ascenders and descenders, so the words are set with the same
ones): Gas Parade's heavy serif in Bitter Black, the Foxy Cat's and the Gila's heavy condensed capitals in Anton, emboldened
0.12 texel a side (the painted strokes are a texel and more). Each box's
painted letters are taken off (the texels on the ink's side of a cut between ink and ground, with the upscale's soft edge, made
opaque: the painters keyed a texel here and there in the ink) and filled from the board round them.

    python3 tools/text-redraws/gen/billboards.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERIF = {"font": "Bitter", "variation": {"Weight": 900}}
NARROW = {"font": "Anton", "embolden": 0.12}    # the painting is heavier than Anton at 3 to 4 texels
# texture: (what, where it stands, cut: ("below"|"above", ink side of the original's mean RGB, halo on the pack's),
#           [(words, box in the original's texels, face, ink)])
BOARDS = {
    "ao_1sg_3": ("the Gas Parade billboard (aobilbd3: M10, T02, T06)", ("above", 140, 120), [
        ("Gas - #2 Diesel - Food - Trucks Welcome", (9.97, 44.94, 119.35, 51.84), SERIF, [241, 241, 247]),
        ("32 miles - at Ropesville", (20.11, 54.25, 108.65, 60.73), SERIF, [234, 235, 243])]),
    "ao_1sg_a": ("the Foxy Cat billboard (aobilbda)", ("below", 110, 120), [
        ("GOOD FOOD – CHEAP BEER", (57.63, 37.41, 110.96, 41.09), NARROW, [58, 26, 176]),
        ("50 – BEAUTIFUL GIRLS – 50", (58.54, 43.72, 110.72, 47.4), NARROW, [58, 26, 176]),
        ("DON'T MISS IT – TRUCKERS WELCOME", (33.91, 51.6, 120.09, 56.09), NARROW, [51, 24, 179])]),
    "ao_1sg_b": ("the Gila Truck Stop billboard (aobilbdb)", ("below", 170, 200), [
        ("LOWEST", (11.23, 21.78, 25.88, 25.52), NARROW, [199, 18, 32]),
        ("PRICE", (13.29, 27.88, 23.58, 31.93), NARROW, [203, 26, 40]),
        ("#2 DIESEL", (8.43, 34.1, 28.72, 37.99), NARROW, [200, 20, 34]),
        ("CIGS", (105.37, 21.48, 113.92, 25.79), NARROW, [203, 26, 40]),
        ("CHEW", (104.32, 27.89, 114.82, 31.88), NARROW, [200, 20, 34]),
        ("BEER", (105.06, 34.09, 114.04, 37.96), NARROW, [202, 25, 41])]),
}


def ops(cut, lines):
    side, orig_cut, halo_cut = cut
    out = []
    for t, (x0, y0, x1, y1), face, ink in lines:
        out.append({"op": "erase", "rect": [round(x0 - 1.0, 2), round(y0 - 0.8, 2), round(x1 + 1.0, 2), round(y1 + 0.8, 2)],
                    "where": {f"lum_{side}": orig_cut}, "grow": 0.6, "halo": {"within": 0.8, f"lum_{side}": halo_cut}, "opaque": True})
    for t, box, face, ink in lines:
        x0, y0, x1, y1 = box
        out.append({"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": ink, "box": [x0, y0, x1, y1], "alpha": "set"})
    return out


if __name__ == "__main__":
    for tex, (what, cut, lines) in BOARDS.items():
        s = {"textures": [tex], "note": f"{what}: its small print; written by gen/billboards.py", "ops": ops(cut, lines)}
        with open(os.path.join(OUT, f"signs-{tex}.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
