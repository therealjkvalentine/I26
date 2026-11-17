#!/usr/bin/env python3
"""Writes signs-ao_1sg_2.json, signs-ao_1sg_3.json, signs-ao_1sg_a.json, signs-ao_1sg_b.json, signs-ao_1sg_e.json: the small
print of five roadside billboards, which the
upscale melts (3 to 7 texels: Gas Parade's "32 miles - at Ropesville" comes out "32 milos - at Ropoosvillo", the Foxy Cat's
"GOOD FOOD - CHEAP BEER" lines and the Gila Truck Stop's side columns, LOWEST PRICE #2 DIESEL and CIGS CHEW BEER, turn to blots; the Quick-E Freeze's pale blue UP AHEAD / TRUCK PARKING
blurs away and its Burgers and Famous Sundaes go ragged; Mod wheels' dealer, V. SHISHKIN COURCHEVAL, and its slanted
"See it all in your Courcheval!" melt). The two lines on Mod wheels too small to read in the original (over and under the
dealer's name) are left as the upscale has them.
The large lettering (the names, 8 texels and more) upscales cleanly and is left as it is.

Each line is set from a font on its painted ink box, read off the original to a fraction of a texel (the r8 lanes' rule: a side at
the last texel half covered plus the next one's share; the box takes ascenders and descenders, so the words are set with the same
ones): Gas Parade's heavy serif in Bitter Black, the Foxy Cat's and the Gila's heavy condensed capitals in Anton, emboldened
0.12 texel a side (the painted strokes are a texel and more), the Quick-E Freeze's sans and Mod wheels' slogan in Arimo Bold
(Famous Sundaes and the slogan slanted as painted), the dealer's name in Anton. Each box's
painted letters are taken off (the texels on the ink's side of a cut between ink and ground, with the upscale's soft edge, made
opaque: the painters keyed a texel here and there in the ink) and filled from the board round them.

    python3 tools/text-redraws/gen/billboards.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SERIF = {"font": "Bitter", "variation": {"Weight": 900}}
SANS = {"font": "Arimo", "variation": {"Weight": 700}}
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
    "ao_1sg_2": ("the Mod wheels billboard (aobilbd2: M07, M10, S01, S06, T08, T10)", ("above", 150, 130), [
        ("V. SHISHKIN", (87.03, 42.83, 120.32, 48.0), NARROW, [252, 250, 250]),
        ("COURCHEVAL", (85.83, 49.23, 122.12, 55.0), NARROW, [252, 250, 250]),
        ("See it all in your Courcheval!", (3.77, 55.68, 79.03, 60.94), dict(SANS, embolden=0.2), [247, 238, 238], 0.25)]),
    "ao_1sg_e": ("the Quick-E Freeze billboard (aobilbde)", ("below", 215, 225), [
        ("UP AHEAD", (82.18, 34.32, 108.56, 38.73), SANS, [70, 128, 214]),
        ("TRUCK PARKING", (79.43, 39.81, 111.08, 42.84), SANS, [70, 128, 214]),
        ("Burgers", (11.79, 48.58, 39.76, 56.75), SANS, [70, 128, 214]),
        ("Famous Sundaes", (53.87, 48.01, 117.22, 55.35), SANS, [70, 128, 214], 0.2)]),
}


def ops(cut, lines):
    side, orig_cut, halo_cut = cut
    lines = [l if len(l) > 4 else l + (0,) for l in lines]     # (words, box, face, ink, slant)
    out = []
    for t, (x0, y0, x1, y1), face, ink, slant in lines:
        out.append({"op": "erase", "rect": [round(x0 - 1.0, 2), round(y0 - 0.8, 2), round(x1 + 1.0, 2), round(y1 + 0.8, 2)],
                    "where": {f"lum_{side}": orig_cut}, "grow": 0.6, "halo": {"within": 0.8, f"lum_{side}": halo_cut}, "opaque": True})
    for t, box, face, ink, slant in lines:
        x0, y0, x1, y1 = box
        op = {"op": "text", "text": t, **face, "cap": round(y1 - y0, 2), "colour": ink}
        if slant:
            d = slant * (y1 - y0)
            op["quad"] = [[x0 + d, y0], [x1, y0], [x1 - d, y1], [x0, y1]]; op["fit"] = "ink"
        else: op["box"] = [x0, y0, x1, y1]
        op["alpha"] = "set"
        out.append(op)
    return out


if __name__ == "__main__":
    for tex, (what, cut, lines) in BOARDS.items():
        s = {"textures": [tex], "note": f"{what}: its small print; written by gen/billboards.py", "ops": ops(cut, lines)}
        with open(os.path.join(OUT, f"signs-{tex}.json"), "w", encoding="utf-8", newline="\n") as f:
            json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
