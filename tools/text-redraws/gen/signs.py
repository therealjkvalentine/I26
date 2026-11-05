#!/usr/bin/env python3
"""Writes signs-*.json: the small print (4-7 texels) of road signs and billboards, which the upscale leaves blobby, set
again on the painted lines' boxes: the Seminole and Seagraves town signs' population and motto lines, the Texas
billboard's "The Lone Star State" and its "Drive friendly" footer, the Ropesville sign's "Last Week of September". The
large lettering (8 texels and more) upscales cleanly and is left as it is. Each line: its words, the box of its painted
letters (the original's texels, inclusive, descenders and quotes in it), font, ink, slant, the capitals' height and the
cap band's offset from the box's middle (up, for a line with descenders); the box is taken off whole and filled from round it.

    python3 tools/text-redraws/gen/signs.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BOLD = {"variation": {"Weight": 700}}
SIGNS = {
    "sw1sem_1": ("the Seminole town sign", [
        ("Pop. 185", (6, 52, 33, 57), "Bitter", [228, 228, 234], 0, 4.4, -0.5),
        ('"Drive Friendly"', (74, 52, 121, 57), "Bitter", [228, 228, 234], 0, 4.4, -0.5)]),
    "sw1sea_1": ("the Seagraves town sign (T01)", [
        ('"Flower of the Panhandle"', (6, 37, 121, 43), "Bitter", [52, 36, 26], 0, 5.4, 0.2),
        ("Pop. 245", (8, 51, 35, 56), "Bitter", [52, 36, 26], 0, 4.4, -0.5),
        ("Elev. 290", (86, 51, 115, 56), "Bitter", [52, 36, 26], 0, 4.6, 0)]),
    "ao_1sg_8": ("the Texas billboard", [
        ('"The Lone', (66, 33, 100, 38), "Bitter", [150, 18, 22], 0, 4.4, 0),
        ('Star State"', (88, 38, 123, 43), "Bitter", [150, 18, 22], 0, 4.4, 0),
        ("Drive friendly · speed laws strictly enforced", (6, 58, 122, 62), "Bitter", [150, 18, 22], 0.22, 2.9, -1.1)]),
    "sw1rop_1": ("the Ropesville sign", [
        ("Last Week of September", (32, 54, 102, 60), "ArchivoNarrow", [222, 240, 200], 0, 3.9, -0.9)]),
}

SKIP = {}
# the footer's descenders reach the frame: stop above it and fill with the paper's white (an inpaint draws the frame's dark up into it)
BOTTOM = {"Drive friendly · speed laws strictly enforced": 62.0}
FLAT = {"Drive friendly · speed laws strictly enforced": [252, 250, 249]}
TOP_PAD = {'"The Lone': 0.2, "Drive friendly · speed laws strictly enforced": 0.6}   # TEXAS, and the map's tip, sit just above these

def spec(lines):
    # the box taken off whole (from just under the line above: TOP_PAD), but round other art that crosses it (SKIP: x ranges kept)
    ops = []
    for t, (x0, y0, x1, y1), *_ in lines:
        xs, keep = [x0 - 1.6], SKIP.get(t, [])
        for k0, k1 in keep: xs += [k0, k1]
        xs.append(x1 + 2.2)
        for a, b in zip(xs[::2], xs[1::2]):
            op = {"op": "erase", "rect": [a, y0 - TOP_PAD.get(t, 0.8), b, min(y1 + 1.8, BOTTOM.get(t, 62.8))]}
            if t in FLAT: op["fill"] = FLAT[t]
            ops.append(op)
    for text, (x0, y0, x1, y1), font, ink, skew, cap, dy in lines:
        t = {"op": "text", "text": text, "font": font, "cap": cap, "center": [(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2 + dy],
             "width": x1 - x0 + 1 - cap * skew, "colour": ink, **BOLD}
        if skew: t["skew"] = skew
        ops.append(t)
    return ops

if __name__ == "__main__":
    for tex, (what, lines) in SIGNS.items():
        s = {"textures": [tex], "note": f"{what}: its small print; written by gen/signs.py", "ops": spec(lines)}
        with open(os.path.join(OUT, f"signs-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
