#!/usr/bin/env python3
"""Writes the gauge specs: dash-dials.json (the 3D dashes' speedometer 0-150 and tachometer 0-8) and zspeedo6.json /
ztach6.json (the external instruments' keyed figures). The figures' places were read off the originals: each figure's
painted texels (their box, in the original's texels), the figures in the dial's order.

The 3D dash dials are one painting shared by four dash families (texel-for-texel the same in every erase box):
zdash101 (Piranha, Cavera, Manta, Leopard and most cars), zdash201 (police car, Royale, Stag, Clyde, Dreadnought,
Sovereign, Vikea, ambulance, hearse), zdash301 (cargo truck, Loadmaster, tanker, Moth, van, bus), zdash501 (Grappler).
The helicopters' panels (zdash401/402 Huey, zdash601 Cobra) are photographed aircraft instruments, not redrawn.

The graduations (the ticks round each dial, and on the 3D dash the grey scale line they cross) are drawn again as clean lines
on the original's places: a model upscale leaves them wobbly, uneven and soft-ended. Read off the originals' texels (the
retail art as --export-textures writes it): on the 3D dash the scale line is an ellipse fitted to its brightness peak between
the ticks, each tick the peak of brightness along it (its angle a the ellipse's, the point (cx + rx sin a, cy - ry cos a): 0 up, clockwise), majors and minors
told apart by their inner reach, extents the half-peak points along the ray (each class's median), each tick's grey its
brightest texel's (a running median of three: the art shades the dial darker at the top); the external instruments' ticks are
one-texel white strokes, each a component of the original's texels outside the figures' boxes, the centre and outer radius
fitted to their outer ends, each angle its centroid's, the inner end its class's (a tick whose own reach is more than a texel
from both classes keeps its own). The 3D dash's ring is the same painting in all four families, but zdash301's bezel comes up
to the ticks' outer ends: there the ring is cut out of the key only to half a texel past the line, and the ticks' tips past it
(and the upscale's bright halo round them) are filled from the bezel round them (dash-dials-301.json); zdash501's comes within
a texel and a half of the line, so its ring is cut to a texel and a quarter (dash-dials-501.json). The tachometer's band from
6 to 8 also carries fine ticks, dim, where they stand out of it (tenths over part of it), drawn as a third, shorter class.

    python3 tools/text-redraws/gen/dials.py
"""
import json, math, os, sys
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONT = {"font": "ArchivoNarrow", "variation": {"Weight": 700}}

# the 3D dash: the speedometer's figures (box x0, y0, x1, y1 inclusive), the tachometer's (centre)
SPEEDO = [("0", (106, 99, 107, 103)), ("10", (101, 94, 105, 98)), ("20", (99, 87, 102, 91)), ("30", (98, 81, 102, 85)),
          ("40", (100, 74, 104, 78)), ("50", (105, 68, 110, 72)), ("60", (112, 63, 116, 67)), ("70", (120, 61, 123, 64)),
          ("80", (127, 61, 131, 64)), ("90", (134, 63, 137, 66)), ("100", (139, 67, 144, 71)), ("110", (144, 73, 149, 77)),
          ("120", (146, 80, 152, 83)), ("130", (147, 87, 152, 91)), ("140", (144, 93, 149, 97)), ("150", (139, 99, 144, 103))]
TACH = [("0", (222, 106.7)), ("1", (214.7, 109)), ("2", (201.7, 109)), ("3", (191, 102)), ("4", (185, 93)), ("5", (184.7, 83.8)),
        ("6", (190.5, 72)), ("7", (204.6, 65.5)), ("8", (220, 67.6))]

# the graduations, measured (see above): centre and radii of the 3D dash's scale line (texels), the line's width, the line's
# grey midway between ticks [(angle, grey)], the ticks [(angle, class: 0 minor, 1 major, 2 fine, grey)] (the scale's run
# first, then any fine ticks), and each class's extent along the ray from the line (inner, outer, texels; minus is towards
# the centre)
SPEEDO_RING = dict(center=(125.373, 85.699), radii=(31.946, 29.657), width=2.0,
    line=[(227.5, 60), (235.0, 84), (244.12, 88), (253.12, 84), (261.88, 82), (271.25, 82), (280.88, 62), (290.5, 62), (299.62, 62),
          (308.75, 57), (318.12, 68), (327.88, 58), (337.25, 58), (346.5, 58), (355.88, 64), (4.75, 72), (13.25, 73), (21.5, 73),
          (30.25, 68), (39.38, 62), (48.75, 56), (57.88, 56), (67.0, 58), (86.5, 70), (96.25, 78), (114.88, 86), (123.5, 90), (130.62, 100)],
    ticks=[(224.25, 1, 199), (230.75, 0, 203), (239.25, 1, 203), (249.0, 0, 188), (257.25, 1, 188), (266.5, 0, 195), (276.0, 1, 204),
           (285.75, 0, 195), (295.25, 1, 195), (304.0, 0, 154), (313.5, 1, 130), (322.75, 0, 130), (333.0, 1, 130), (341.5, 0, 130),
           (351.5, 1, 122), (0.25, 0, 106), (9.25, 1, 114), (17.25, 0, 114), (25.75, 1, 122), (34.75, 0, 129), (44.0, 1, 129),
           (53.5, 0, 137), (62.25, 1, 155), (71.75, 0, 171), (81.5, 1, 187), (91.5, 0, 196), (101.0, 1, 188), (110.0, 0, 203),
           (119.75, 1, 188), (127.25, 0, 188), (134.0, 1, 188)],
    major=(-2.66, 1.56), minor=(-2.0, 1.5))
TACH_RING = dict(center=(209.406, 88.021), radii=(30.958, 29.117), width=2.0,
    line=[(157.12, 96), (174.38, 90), (190.0, 90), (204.5, 97), (219.12, 104), (233.5, 97), (247.62, 97), (260.75, 97), (272.88, 97),
          (302.75, 64), (320.75, 49), (340.0, 49), (359.12, 52), (19.0, 58)],
    ticks=[(148.5, 1, 180), (165.75, 1, 211), (183.0, 0, 171), (197.0, 1, 204), (212.0, 0, 203), (226.25, 1, 204), (240.75, 0, 203),
           (254.5, 1, 204), (267.0, 0, 219), (278.75, 1, 219), (294.5, 0, 188), (311.0, 1, 147), (330.5, 0, 130), (349.5, 1, 130),
           (8.75, 0, 130), (29.25, 1, 151),
           # the fine ticks the art paints in the band from 6 to 8, dim (peaks of 70-95 over a band of about 45), where they
           # stand out of it: tenths from 6 to 6.5 and from 7 to 7.5, two between 7.5 and 8
           (315.0, 2, 100), (318.75, 2, 100), (322.75, 2, 100), (327.0, 2, 100), (352.75, 2, 100), (356.5, 2, 100), (0.25, 2, 100),
           (4.0, 2, 100), (17.25, 2, 100), (23.25, 2, 100)],
    major=(-2.94, 1.25), minor=(-2.19, 1.38), fine=(-1.0, 1.25))
TICK_WIDTH = 0.5          # texels: the painted ticks are a texel wide; drawn half as wide (the owner's pick, 2026-11-10)
LINE_SCALE = 0.5          # the 3D dash's scale line (painted 2 texels wide) drawn at this share of its width (the owner's pick)
TINT = (1.0, 0.975, 1.0)  # the art's greys lean a shade to magenta (r = b, g a little lower)

def grey(v): return [round(v * k, 1) for k in TINT]

def on_line(ring, a, d):
    """The point d texels along the ray from the ring's centre past its line's point at angle a (the ellipse's angle: the
    point (cx + rx sin a, cy - ry cos a), degrees, 0 up, clockwise)."""
    (cx, cy), (rx, ry) = ring["center"], ring["radii"]; t = math.radians(a)
    ex, ey = math.sin(t) * rx, -math.cos(t) * ry; n = math.hypot(ex, ey)
    return round(cx + ex + ex / n * d, 3), round(cy + ey + ey / n * d, 3)

def tick_segments(ring, width=None):
    out = []
    for a, major, g in ring["ticks"]:
        lo, hi = ring[("minor", "major", "fine")[major]]
        out.append(list(on_line(ring, a, lo) + on_line(ring, a, hi)) + [width or TICK_WIDTH, grey(g)])
    return out

def sweep(ring, pad):
    a = [t[0] for t in ring["ticks"] if t[1] < 2]
    return round((a[0] - pad) % 360, 2), round((a[-1] + pad) % 360, 2)

def ring_ops(ring, outer, tips=None, width=None, line=None):
    """A dial's graduations: the ring's painted texels cut out of the key (from inside the majors' reach to `outer` texels
    past the line), with `tips` (an outer limit) the ticks' bright tips past that filled from the bezel round them; then the
    scale line, and the ticks over it."""
    (cx, cy), (rx, ry) = ring["center"], ring["radii"]; f, t = sweep(ring, 4)
    rect = [math.floor(cx - rx - 5), math.floor(cy - ry - 5), math.ceil(cx + rx + 5), math.ceil(cy + ry + 5)]
    geo = {"center": list(ring["center"]), "radii": list(ring["radii"]), "from": f, "to": t}
    ops = [{"op": "erase", "rect": rect, "where": {"lum_above": 20, "opaque": True, "ring": dict(geo, inner=-3.75, outer=outer)}, "grow": 0.6, "fill": "key"}]
    if tips:
        ops.append({"op": "erase", "rect": rect, "where": {"lum_above": 140, "opaque": True, "ring": dict(geo, inner=outer, outer=tips)}, "grow": 0.4,
                    "halo": {"within": 0.75, "lum_above": 120}, "fill": "inpaint"})
    lf, lt = sweep(ring, 0)
    ops.append({"op": "lines", "arcs": [{"center": list(ring["center"]), "radii": list(ring["radii"]), "width": round(ring["width"] * (LINE_SCALE if line is None else line), 3), "step": 1.0,
                "colours": [[a, grey(g)] for a, g in ring["line"]], "from": lf, "to": lt}], "alpha": "cover", "under": [0, 0, 0]})
    ops.append({"op": "lines", "segments": tick_segments(ring, width), "cap": "butt", "alpha": "cover", "under": [0, 0, 0]})
    return ops

def dash(textures=("zdash101", "zdash201"), outer=1.75, tips=None, width=None, line=None):
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
    for ring in (SPEEDO_RING, TACH_RING): ops += ring_ops(ring, outer, tips, width, line)
    return {"textures": list(textures),
            "note": "the 3D dashes' speedometer 0-150 and tachometer 0-8 on the dials' keyed faces (the cockpit draws the key black): figures,"
                    " ticks and scale line; written by gen/dials.py",
            "ops": ops + txt}

# the external instruments' ticks: centre, outer radius (texels), each class's inner end (texels from the outer circle), the
# ticks [(angle, major[, own inner end])] in the scale's order
SPEEDO6_TICKS = dict(center=(39.462, 39.386), radius=39.493, major=-7.4, minor=-4.05, ticks=[
    (224.83, 1), (230.22, 0), (235.29, 0), (240.53, 1), (246.32, 0), (251.87, 0), (258.05, 1), (265.18, 0), (274.59, 1), (281.45, 0),
    (287.8, 0), (293.37, 1), (299.37, 0), (304.42, 0), (309.6, 1), (314.92, 0), (320.32, 0), (325.37, 1), (330.47, 0), (336.48, 0),
    (342.07, 1), (348.41, 0), (354.71, 0), (0.06, 1), (5.41, 0), (11.7, 0), (18.04, 1), (23.62, 0), (29.63, 0), (34.73, 1),
    (39.77, 0), (45.16, 0), (50.48, 1), (55.64, 0), (60.68, 0), (66.68, 1), (72.23, 0), (78.57, 0), (85.48, 0), (94.81, 0),
    (101.78, 0, -5.31), (108.1, 0), (113.63, 0), (119.48, 1, -5.6)])
TACH6_TICKS = dict(center=(39.513, 39.51), radius=39.617, major=-7.58, minor=-4.07, ticks=[
    (145.43, 1), (150.52, 0), (156.51, 0), (162.07, 0), (168.41, 0), (174.8, 1), (180.02, 0), (185.35, 0), (191.63, 0), (197.97, 0),
    (203.54, 1), (209.52, 0), (214.56, 0), (219.64, 0), (225.02, 0), (230.32, 1), (235.49, 0), (240.53, 0), (246.52, 0), (252.07, 0),
    (258.26, 1), (264.68, 0), (270.01, 0), (275.34, 0), (281.62, 0), (287.94, 1), (293.5, 0), (299.5, 0), (304.54, 0), (309.61, 0),
    (315.0, 1), (320.38, 0), (325.46, 0), (330.49, 0), (336.49, 0), (342.06, 1), (348.37, 0), (354.65, 0), (359.98, 0), (5.31, 0),
    (11.73, 1)])

def overlay_tick_segments(d):
    (cx, cy), R = d["center"], d["radius"]; out = []
    for t in d["ticks"]:
        a = math.radians(t[0]); lo = t[2] if len(t) > 2 else (d["major"] if t[1] else d["minor"])
        u = (math.sin(a), -math.cos(a))
        out.append([round(cx + u[0] * (R + lo), 3), round(cy + u[1] * (R + lo), 3), round(cx + u[0] * R, 3), round(cy + u[1] * R, 3)])
    return out

def overlay_tick_ops(d, width=None):
    """The ticks' texels cut back into the key (the ring from a texel inside the longest tick's reach to a texel past the outer
    circle, within the scale's sweep), then drawn again as white strokes TICK_WIDTH wide, opaque where they cover half a texel."""
    (cx, cy), R = d["center"], d["radius"]; a = [t[0] for t in d["ticks"]]
    geo = {"center": [cx, cy], "radii": [R, R], "inner": min([d["major"]] + [t[2] for t in d["ticks"] if len(t) > 2]) - 1, "outer": 1.0,
           "from": round((a[0] - 3) % 360, 2), "to": round((a[-1] + 3) % 360, 2)}
    return [{"op": "erase", "rect": [0, 0, math.ceil(cx + R + 2), math.ceil(cy + R + 2)], "where": {"opaque": True, "ring": geo}, "grow": 0.5, "fill": "key"},
            {"op": "lines", "segments": overlay_tick_segments(d), "width": width or TICK_WIDTH, "colour": [255, 255, 255], "cap": "butt", "alpha": "set"}]

def overlay(tex, items, cap, note, ticks=None, width=None):
    ops, txt = [], []
    for s, (x0, y0, x1, y1) in items:
        ops.append({"op": "erase", "rect": [x0, y0, x1 + 1, y1 + 1], "where": {"opaque": True}, "grow": 0.5, "fill": "key"})
        t = dict(op="text", text=s, cap=cap, center=[(x0 + x1 + 1) / 2, (y0 + y1 + 1) / 2], colour=[255, 255, 255], alpha="set", **FONT)
        if len(s) > 1: t["width"] = x1 - x0 + 1
        txt.append(t)
    if ticks: ops += overlay_tick_ops(ticks, width)
    return {"textures": [tex], "note": note + "; written by gen/dials.py", "ops": ops + txt}

SPEEDO6 = [("0", (20, 58, 22, 62)), ("10", (13, 52, 18, 56)), ("20", (10, 44, 16, 48)), ("30", (8, 35, 13, 39)), ("40", (10, 27, 16, 31)),
           ("50", (14, 20, 20, 24)), ("60", (20, 14, 27, 18)), ("70", (28, 10, 34, 14)), ("80", (36, 8, 43, 12)), ("90", (46, 9, 52, 13)),
           ("100", (53, 13, 62, 17)), ("110", (59, 20, 67, 24)), ("120", (62, 28, 71, 32)), ("130", (64, 35, 72, 39)), ("140", (62, 43, 71, 47)),
           ("150", (58, 50, 67, 54))]
TACH6 = [("8", (44, 10, 48, 16)), ("7", (30, 11, 34, 17)), ("6", (18, 16, 22, 22)), ("5", (11, 28, 15, 34)), ("4", (11, 41, 15, 47)),
         ("3", (16, 52, 20, 58)), ("2", (26, 60, 30, 66)), ("1", (40, 62, 42, 68)), ("0", (53, 57, 57, 63))]

def write(name, spec):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f: json.dump(spec, f, indent=1); f.write("\n")

def specs(width=None, line=None):
    """{file name: spec} of every gauge spec (width: the ticks' width in texels, TICK_WIDTH by default; line: the 3D dash's
    scale line's share of its painted width, LINE_SCALE by default)."""
    d301 = dash(("zdash301",), outer=0.5, tips=2.25, width=width, line=line)
    d301["note"] += "; zdash301, whose bezel comes up to the ticks' tips"
    d501 = dash(("zdash501",), outer=1.25, width=width, line=line)
    d501["note"] += "; zdash501, whose bezel comes within a texel and a half of the line"
    return {"dash-dials.json": dash(width=width, line=line), "dash-dials-301.json": d301, "dash-dials-501.json": d501,
            "zspeedo6.json": overlay("zspeedo6", SPEEDO6, 5, "the speedometer's figures and ticks over the world (the external instruments and the 2D dash): keyed white", SPEEDO6_TICKS, width),
            "ztach6.json": overlay("ztach6", TACH6, 7, "the tachometer's figures and ticks over the world (the external instruments and the 2D dash)", TACH6_TICKS, width)}

if __name__ == "__main__":
    # --out DIR --tick-width W --line-scale L: a variant's specs written elsewhere (the spec folder holds the pick's)
    if "--out" in sys.argv: OUT = sys.argv[sys.argv.index("--out") + 1]
    w = float(sys.argv[sys.argv.index("--tick-width") + 1]) if "--tick-width" in sys.argv else None
    ls = float(sys.argv[sys.argv.index("--line-scale") + 1]) if "--line-scale" in sys.argv else None
    for name, spec in specs(w, ls).items(): write(name, spec)
