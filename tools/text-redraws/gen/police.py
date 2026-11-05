#!/usr/bin/env python3
"""Writes police-doors.json-style specs: the police car's door lettering in its three liveries (cp1D md L/R V: damage
level D 1-3, the left door L or the right R, livery V 1-3), in D-DIN Condensed Bold slanted as the painted letters are.
The right door's texture is laid on mirrored, so its lettering is painted mirrored: the spec mirrors it too. The boxes
are the painted letters' (the original's texels), the same at every damage level. NEW MEXICO STATE's POLICE on the
undamaged door is outline lettering whose faces are the colour key, which a car's panel draws in the key's colour (dark),
and solid on the damaged doors: it is lettered solid on all three.

    python3 tools/text-redraws/gen/police.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
F = {"font": "DDINCondensed", "colour": [20, 20, 26]}
# livery: (top line, its box on the left door, on the right door), (bottom line, box left, box right, style)
LIVERIES = {
    1: (("VALDEZ COUNTY", (75, 24, 118, 29), (76, 24, 119, 29)), ("SHERIFF", (76, 34, 116, 44), (78, 34, 118, 44), "solid")),
    2: (("NEW MEXICO STATE", (74, 24, 123, 29), (74, 24, 123, 29)), ("POLICE", (75, 33, 116, 45), (81, 33, 121, 45), "outline")),
    3: (("TEXAS HIGHWAY", (80, 24, 118, 29), (77, 24, 116, 29)), ("TROOPER", (75, 33, 120, 43), (75, 33, 120, 43), "solid")),
}

def spec(livery, side):
    (t1, b1l, b1r), (t2, b2l, b2r, style) = LIVERIES[livery]
    b1, b2 = (b1l, b2l) if side == "l" else (b1r, b2r)
    mirror = side == "r"
    ops = []
    for (x0, y0, x1, y1) in (b1, b2):
        # the line's whole box (the upscale's dark edge and light ring with it), filled from the door round it
        ops.append({"op": "erase", "rect": [x0 - 1, y0 - 1, min(x1 + 2, 125), y1 + 2], "opaque": True})
    def text(s, box, cap, skew, **kw):
        x0, y0, x1, y1 = box
        t = dict(op="text", text=s, cap=cap, center=[(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2], width=x1 - x0 + 1 - cap * abs(skew), skew=skew, **F, **kw)
        if mirror: t["mirror"] = True
        return t
    ops.append(text(t1, b1, b1[3] - b1[1] + 1, 0.12))
    ops.append(text(t2, b2, b2[3] - b2[1] + (0 if style == "outline" else 1), 0.2))
    return ops

if __name__ == "__main__":
    for livery in LIVERIES:
        for side in "lr":
            texs = [f"cp1{d}md{side}{livery}" for d in (1, 2, 3)]
            name = f"police-{side}{livery}.json"
            s = {"textures": texs, "note": f"the police car's {'left' if side == 'l' else 'right (mirrored)'} door, livery {livery}: {' / '.join(x[0] for x in LIVERIES[livery])}; written by gen/police.py",
                 "ops": spec(livery, side)}
            with open(os.path.join(OUT, name), "w", encoding="utf-8") as f: json.dump(s, f, indent=1); f.write("\n")
