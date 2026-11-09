#!/usr/bin/env python3
"""Writes dover-lightning-*.json: the Dover's second paint scheme (lightng2.vtf, the blue and white "66" car that T09's mission
chases, t09dl01.vcf), its panels' painted words set again, in each of the three dent stages (dl11, dl12, dl13). The words are
painted 6 to 9 texels high; the model left them torn, and the texels the painter keyed inside or round them (which a car's panel
shows in the key's black) came out in the blue bled under the key. Each word: its painted texels taken off (filled from the
panel round them), set again on the painted letters' own ink box (read off the original, per stage: the third stage's panels
are dented and the words moved), in the original's colours; CRACK keeps its cracks (the keyed texels inside the letters, as
"marks"); the panels' other keyed texels (the chequered stripes, the grille's slots) are the key's black with a smooth outline.
The right-hand front and rear quarter panels (dl1?ftr2, dl1?bkr2) are the left's mirror copies, so their specs are the left's
mirrored; the doors (dl1?mdr2) are painted the same way round as the left (the right door's XYZZY reads backwards in the original
too, as NEMESIS does on the right rear quarter). The key's colour is black (index 255, (1, 1, 1) in T09's palette).

Faces: CRACK, NEMESIS and XYZZY in Alfa Slab One (heavy slab capitals; CRACK slanted as painted, XYZZY stretched to the
painted letters' width), PYRO in Archivo Narrow, SPYCRAFT in Bitter Black, JEFF in Russo One, the STB badges (the rear quarters and the
roof) in Bitter Bold.

    python3 tools/text-redraws/gen/dover_lightning.py
"""
import json, os
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
W = 128   # the panels' width in texels (a right-hand panel is the left's mirror about it)

def crack(box, slant):
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 2, y1 + 1], "where": {"lum_below": 150}, "grow": 0.4, "opaque": True},
            {"op": "text", "text": "CRACK", "font": "AlfaSlabOne", "cap": y1 - y0, "quad": [[x0 + slant, y0], [x1, y0], [x1 - slant, y1], [x0, y1]],
             "fit": "ink", "gradient": [[44, 140, 52], [22, 104, 36]], "stroke": {"width": 1.0, "colour": [8, 26, 14]}, "alpha": "set"},
            {"op": "marks", "rect": [x0, y0, x1, y1], "where": {"keyed": True}, "colour": [8, 22, 14], "sigma": 0.45, "level": 0.3}]

def pyro(box, slant=0):
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 1, y1 + 1], "where": {"lum_below": 90}, "grow": 0.5, "halo": {"within": 0.6, "lum_below": 110}, "opaque": True},
            {"op": "text", "text": "PYRO", "font": "ArchivoNarrow", "variation": {"Weight": 500}, "cap": y1 - y0,
             "quad": [[x0 + slant, y0], [x1 + slant, y0], [x1, y1], [x0, y1]], "fit": "ink", "colour": [34, 32, 36], "alpha": "set"}]

def nemesis(box):
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 1, y1 + 1], "where": {"lum_below": 60}, "grow": 0.5, "opaque": True},
            {"op": "text", "text": "NEMESIS", "font": "AlfaSlabOne", "cap": y1 - y0, "box": [x0, y0, x1, y1], "colour": [6, 10, 22], "alpha": "set"}]

def stb(box, grey=(150, 150, 156), light=100):
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 0.5, y0 - 0.5, x1 + 0.5, y1 + 0.5], "where": {"lum_above": light}, "grow": 0.4, "halo": {"within": 0.5, "lum_above": light - 5}, "opaque": True},
            {"op": "text", "text": "STB", "font": "Bitter", "variation": {"Weight": 700}, "cap": y1 - y0, "box": [x0, y0, x1, y1],
             "colour": list(grey), "alpha": "set"}]

def xyzzy(box):
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 1, y0, x1 + 1, y1], "where": {"opaque": True}, "grow": 0.3},
            {"op": "text", "text": "XYZZY", "font": "AlfaSlabOne", "cap": y1 - y0, "box": [x0, y0, x1, y1], "tracking": 0.12,
             "gradient": [[226, 116, 30], [176, 88, 4]], "stroke": {"width": 0.35, "colour": [26, 30, 52]}, "alpha": "set"}]

def spycraft(box):
    # turned a quarter clockwise on the bonnet (it reads from the windscreen forward): the run's top on the box's right
    x0, y0, x1, y1 = box
    return [{"op": "erase", "rect": [x0 - 0.5, y0 - 1, x1 + 1, y1 + 1], "where": {"lum_below": 90}, "grow": 0.5, "halo": {"within": 0.6, "lum_below": 120}, "opaque": True},
            {"op": "text", "text": "SPYCRAFT", "font": "Bitter", "variation": {"Weight": 900}, "cap": x1 - x0,
             "quad": [[x1, y0], [x1, y1], [x0, y1], [x0, y0]], "fit": "ink", "colour": [30, 34, 44], "alpha": "set"}]

def jeff(box):
    # on the boot lid, its four letters stacked (it reads J, E, F, F downward from behind the car), each turned half round in the
    # texture (the lid's texture is laid upside down): J at the box's foot; each letter on its own painted rows
    x0, y0, x1, y1 = box
    rows = [(21, 26, "J"), (14, 19, "E"), (8, 13, "F"), (2, 7, "F")]
    ops = [{"op": "erase", "rect": [x0 - 1, y0 - 1, x1 + 1, y1 + 1], "where": {"lum_below": 150}, "grow": 0.4, "opaque": True}]
    for r0, r1, c in rows:
        ops.append({"op": "text", "text": c, "font": "RussoOne", "cap": r1 - r0, "quad": [[x1, r1], [x0, r1], [x0, r0], [x1, r0]], "fit": "ink",
                    "gradient": [[52, 150, 54], [28, 112, 30]], "stroke": {"width": 0.3, "colour": [22, 34, 22]}, "alpha": "set"})
    return ops

# per stage: the painted letters' ink boxes (the original's texels, exclusive), read off each stage's own painting
STAGES = {
    "11": {"CRACK": ((40, 14, 78, 23), 2.5), "PYRO": ((11, 40, 35, 50), 0), "NEMESIS": (70, 13, 120, 21), "STB": (34, 11, 48, 18),
           "XYZZY": (18, 56, 119, 62), "STB roof": (27, 59, 45, 64), "SPYCRAFT": (21, 16, 27, 59), "JEFF": (92, 2, 99, 26)},
    "12": {"CRACK": ((40, 14, 78, 23), 2.5), "PYRO": ((11, 40, 35, 50), 0), "NEMESIS": (70, 13, 120, 21), "STB": (34, 11, 48, 18),
           "XYZZY": (18, 56, 119, 62), "STB roof": (27, 59, 45, 64), "SPYCRAFT": (21, 16, 27, 59), "JEFF": (92, 2, 99, 26)},
    "13": {"CRACK": ((46, 22, 90, 30), 2.5), "PYRO": ((11, 44, 37, 53), -1.5), "NEMESIS": (75, 15, 127, 22), "STB": (37, 13, 55, 20),
           "XYZZY": (18, 56, 118, 62), "STB roof": (27, 59, 45, 64), "SPYCRAFT": (21, 16, 27, 59), "JEFF": (92, 2, 99, 26)},
}

def mirrored(ops):
    """The ops for the left panel's mirror copy: every rect, box and quad reflected about the panel's middle (a quad's corners
    keep their roles, so the lettering comes out mirrored, as the copy's painting is)."""
    out = []
    for op in ops:
        op = json.loads(json.dumps(op))
        if "rect" in op: x0, y0, x1, y1 = op["rect"]; op["rect"] = [W - x1, y0, W - x0, y1]
        if "box" in op:
            x0, y0, x1, y1 = op.pop("box"); op["quad"] = [[W - x0, y0], [W - x1, y0], [W - x1, y1], [W - x0, y1]]; op["fit"] = "ink"
        elif "quad" in op: op["quad"] = [[W - x, y] for x, y in op["quad"]]
        out.append(op)
    return out

# the side panels' other keyed texels (the chequered band, the lamps' lower halves, the roundel's rim): the key's black, which the
# original's panel shows, each texel's square with a smooth outline (the model bled the panel's blue and grey into them: blobs)
def key(rect=(0, 0, W, 64)): return [{"op": "marks", "rect": list(rect), "where": {"keyed": True}, "colour": [0, 0, 0], "sigma": 0.3}]
KEY = key()

def specs():
    for st, b in STAGES.items():
        front = KEY + crack(*b["CRACK"]) + pyro(*b["PYRO"]); rear = KEY + nemesis(b["NEMESIS"]) + stb(b["STB"]); door = KEY + xyzzy(b["XYZZY"])
        yield f"dl{st}ftl2", "CRACK and PYRO", front
        yield f"dl{st}ftr2", "CRACK and PYRO (the left panel's mirror copy)", mirrored(front)
        yield f"dl{st}bkl2", "NEMESIS and the STB badge", rear
        yield f"dl{st}bkr2", "NEMESIS and the STB badge (the left panel's mirror copy)", mirrored(rear)
        yield f"dl{st}mdl2", "XYZZY", door
        yield f"dl{st}mdr2", "XYZZY (painted as the left door's)", door
        # the bonnet's, the boot lid's and the roof's chequered strip (only it: the keyed texels round the glass stay as they are)
        yield f"dl{st}ftt2", "SPYCRAFT", key((55, 0, 69, 16)) + spycraft(b["SPYCRAFT"])
        yield f"dl{st}bkt2", "JEFF", key((55, 20, 69, 64)) + jeff(b["JEFF"])
        yield f"dl{st}ftf2", "the grille's keyed slots (the key's black; the model left the blue bled under them)", key()
        yield f"dl{st}tp_2", "the STB badge", key((53, 0, 70, 128)) + stb(b["STB roof"], (100, 100, 104), 60)

if __name__ == "__main__":
    for tex, words, ops in specs():
        s = {"textures": [tex], "note": f"the Dover's blue and white scheme (lightng2, T09's chased car), {tex}: {words}; written by gen/dover_lightning.py", "ops": ops}
        with open(os.path.join(OUT, f"dover-lightning-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1); f.write("\n")
