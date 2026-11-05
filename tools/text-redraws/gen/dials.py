#!/usr/bin/env python3
"""Writes the gauge specs: dash-dials.json (the 3D dashes' speedometer 0-150 and tachometer 0-8) and zspeedo6.json /
ztach6.json (the external instruments' keyed figures). The figures' places were read off the originals: each figure's
painted texels (their box, in the original's texels), the figures in the dial's order.

The 3D dash dials are one painting shared by four dash families (texel-for-texel the same in every erase box):
zdash101 (Piranha, Cavera, Manta, Leopard and most cars), zdash201 (police car, Royale, Stag, Clyde, Dreadnought,
Sovereign, Vikea, ambulance, hearse), zdash301 (cargo truck, Loadmaster, tanker, Moth, van, bus), zdash501 (Grappler).
The helicopters' panels (zdash401/402 Huey, zdash601 Cobra) are photographed aircraft instruments, not redrawn.

    python3 tools/text-redraws/gen/dials.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONT = {"font": "ArchivoNarrow", "variation": {"Weight": 700}}

# the 3D dash: the speedometer's figures (box x0, y0, x1, y1 inclusive), the tachometer's (centre)
SPEEDO = [("0", (106, 99, 107, 103)), ("10", (101, 94, 105, 98)), ("20", (99, 87, 102, 91)), ("30", (98, 81, 102, 85)),
          ("40", (100, 74, 104, 78)), ("50", (105, 68, 110, 72)), ("60", (112, 63, 116, 67)), ("70", (120, 61, 123, 64)),
          ("80", (127, 61, 131, 64)), ("90", (134, 63, 137, 66)), ("100", (139, 67, 144, 71)), ("110", (144, 73, 149, 77)),
          ("120", (146, 80, 152, 83)), ("130", (147, 87, 152, 91)), ("140", (144, 93, 149, 97)), ("150", (139, 99, 144, 103))]
TACH = [("0", (222, 106.7)), ("1", (214.7, 109)), ("2", (201.7, 109)), ("3", (191, 102)), ("4", (185, 93)), ("5", (184.7, 83.8)),
        ("6", (190.5, 72)), ("7", (204.6, 65.5)), ("8", (220, 67.6))]

def dash():
    ops, txt = [], []
    lit = dict(colour=[226, 226, 222], alpha="cover", under=[0, 0, 0], **FONT)
    for s, (x0, y0, x1, y1) in SPEEDO:
        ops.append({"op": "erase", "rect": [x0 - 0.5, y0 - 0.5, x1 + 1.5, y1 + 1.5], "where": {"lum_above": 20, "opaque": True}, "grow": 0.6, "fill": "key"})
        t = dict(op="text", text=s, cap=3.4, center=[(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2], **lit)
        if len(s) == 3: t["width"] = 6.0
        txt.append(t)
    for s, (cx, cy) in TACH:
        ops.append({"op": "erase", "rect": [cx - 2, cy - 3, cx + 2, cy + 3], "where": {"lum_above": 20, "opaque": True}, "grow": 0.6, "fill": "key"})
        txt.append(dict(op="text", text=s, cap=3.8, center=[cx, cy], **lit))
    return {"textures": ["zdash101", "zdash201", "zdash301", "zdash501"],
            "note": "the 3D dashes' speedometer 0-150 and tachometer 0-8 on the dials' keyed faces (the cockpit draws the key black); written by gen/dials.py",
            "ops": ops + txt}

def overlay(tex, items, cap, note):
    ops, txt = [], []
    for s, (x0, y0, x1, y1) in items:
        ops.append({"op": "erase", "rect": [x0, y0, x1 + 1, y1 + 1], "where": {"opaque": True}, "grow": 0.5, "fill": "key"})
        t = dict(op="text", text=s, cap=cap, center=[(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2], colour=[255, 255, 255], alpha="set", **FONT)
        if len(s) > 1: t["width"] = x1 - x0 + 1
        txt.append(t)
    return {"textures": [tex], "note": note + "; written by gen/dials.py", "ops": ops + txt}

SPEEDO6 = [("0", (20, 58, 22, 62)), ("10", (13, 52, 18, 56)), ("20", (10, 44, 16, 48)), ("30", (8, 35, 13, 39)), ("40", (10, 27, 16, 31)),
           ("50", (14, 20, 20, 24)), ("60", (20, 14, 27, 18)), ("70", (28, 10, 34, 14)), ("80", (36, 8, 43, 12)), ("90", (46, 9, 52, 13)),
           ("100", (53, 13, 62, 17)), ("110", (59, 20, 67, 24)), ("120", (62, 28, 71, 32)), ("130", (64, 35, 72, 39)), ("140", (62, 43, 71, 47)),
           ("150", (58, 50, 67, 54))]
TACH6 = [("8", (44, 10, 48, 16)), ("7", (30, 11, 34, 17)), ("6", (18, 16, 22, 22)), ("5", (11, 28, 15, 34)), ("4", (11, 41, 15, 47)),
         ("3", (16, 52, 20, 58)), ("2", (26, 60, 30, 66)), ("1", (40, 62, 42, 68)), ("0", (53, 57, 57, 63))]

def write(name, spec):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f: json.dump(spec, f, indent=1); f.write("\n")

if __name__ == "__main__":
    write("dash-dials.json", dash())
    write("zspeedo6.json", overlay("zspeedo6", SPEEDO6, 5, "the speedometer's figures over the world (the external instruments and the 2D dash): keyed white figures"))
    write("ztach6.json", overlay("ztach6", TACH6, 7, "the tachometer's figures over the world (the external instruments and the 2D dash)"))
