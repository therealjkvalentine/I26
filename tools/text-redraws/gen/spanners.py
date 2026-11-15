#!/usr/bin/env python3
"""Writes signs-fn_1*.json and signs-se1spn_1.json: the lettering of Spanner's truck stop (bfnspan1: T13, T14, S01) and its
guide sign (sespn_1), which the upscale tears: the cafe's name board and its window cards come out as glyph soup, the garage's
two lines as blots, the sign's capitals with key texels inside them, and the spanner round SPANNER'S lumpy.

The cafe front (fn_1cff1) is the place the briefing movie before T14 opens on, rendered by the artists: the movie is the
reference for how its lettering looks (a bold book face with a moderate contrast and a traditional ampersand for the window's
LUNCH & DINNER SERVED and the card's capitals, a brush script for "Lola's" and "Family Restaurant"), the texture for what it
says, where and in which colours. The window card is the movie's (Lola's, TRUCKER'S FEAST, ALL YOU CAN EAT, STEAK & EGGS,
$4.99, Family Restaurant): its painted lines fall where the movie's card has them, so each line is set on the card's paper at
the movie card's proportions. The name board (Lola's over the door, FAMILY RESTAURANT) is set on its painted ink boxes; the
window frame's bars that cross the board and the band are drawn again over the lettering as straight lines, as the movie shows
them in front of it.

On the two SPANNER'S signs (fn_1s1_1 on the building, se1spn_1 by the road) the upscale's spanner is kept (its outline
follows the painting); the letters on its bar are taken off, the bar filled from its white round them, and SPANNER'S set
again in a heavy grotesque (Arimo); the road sign's three lines alike. TRUCK STOP - RESTAURANT (fn_1s2_1) and
the garage's lines (fn_1gar1) are a condensed grotesque (Archivo Narrow); the sign's key texels inside its letters (the
original's palette accident: holes in the letters) are filled.

    python3 tools/text-redraws/gen/spanners.py [SERIF]     (SERIF: a font name of fonts.json for the cafe's book face; a trial)
"""
import json, os, sys
OUT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BOOK = {"font": "LibreCaslonText", "variation": {"Weight": 700}}
SCRIPT = {"font": "Lobster"}
GROT = {"font": "Arimo", "variation": {"Weight": 700}}
NARROW = {"font": "ArchivoNarrow", "variation": {"Weight": 700}}

# the cafe's name board: its paper, its ink, the lines (words, painted ink box, face) and the frame's bars over it (x of the
# texel's left edge, width, colour), from the board's top to its bottom
BOARD = (35, 8, 123, 35)
BOARD_PAPER = [255, 250, 246]
BOARD_INK = [16, 29, 54]
BOARD_LINES = [("Lola's", (45.85, 9.99, 62.94, 31.04), "script"),
               ("FAMILY", (74.0, 8.29, 90.63, 19.0), "book"),
               ("RESTAURANT", (68.07, 21.22, 102.78, 32.75), "book")]
# (the pale pane line at 52 lies under the lettering, the dark bars over it, as painted)
BOARD_UNDER = [(52, 2, [193, 186, 185])]
BOARD_BARS = [(63, 1, [86, 84, 86]), (73, 1, [98, 97, 90]), (103, 1, [78, 76, 78]), (113, 1, [106, 103, 106])]
# the window's band: LUNCH & DINNER SERVED in grey on the dark glass, the window's bars in front of it (x, width, colour)
BAND = ("LUNCH & DINNER SERVED", (46.36, 98.33, 98.55, 102.0), [150, 150, 154])
BAND_BARS = [(63, 1, [123, 121, 123]), (73, 1, [106, 105, 106]), (88, 2, [102, 135, 140])]
# the window cards: the paper (x0, y0, x1, y1) of each, its colour and the card's ink
CARDS = [(46, 58, 52, 96), (75, 56, 82, 95)]
CARD_PAPER = [156, 210, 222]
CARD_INK = [22, 38, 56]
# the movie's card (its paper's box, then each line's ink box, in the movie's pixels): the lines' places on the card
MOVIE_CARD = (333, 21, 517, 297)
CARD_LINES = [("Lola's", (398, 28, 446, 42), "script"), ("TRUCKER'S", (342, 70, 506, 88), "book"), ("FEAST", (369, 94, 484, 119), "book"),
              ("ALL YOU CAN EAT", (340, 141, 512, 156), "book"), ("STEAK &", (357, 165, 497, 186), "book"),
              ("EGGS", (382, 198, 471, 225), "book"), ("$4.99", (388, 233, 462, 262), "book"),
              ("Family Restaurant", (358, 275, 497, 292), "script")]

# the spanner signs: the bar's rect round the letters (inside the spanner), SPANNER'S' box and ink; the road sign's lines
# (words, box) in white below it
SPANNERS = {
    "fn_1s1_1": ("the SPANNER'S sign on the truck stop", (17, 25, 112, 37), (17.84, 25.63, 111.06, 36.88), [0, 18, 139], []),
    "se1spn_1": ("Spanner's guide sign by the road", (19, 9, 108.3, 21), (20.63, 10.23, 107.65, 20.7), [1, 8, 135],
                 [("TRUCK STOP", (19.99, 30.16, 105.92, 39.35)), ("NEXT EXIT - 1/2 MILE", (11.13, 42.01, 114.9, 48.81)),
                  ("GAS - FOOD - SHOWERS", (6.26, 54.69, 122.85, 61.77))]),
}
WHITE_INK = [252, 250, 252]
EMBOLDEN = 0.25     # the signs' grotesque: the painting heavier than Arimo's bold (texels a side)

def card_box(card, line):
    """A line's box on a window card: the movie card's line box moved onto the card's paper."""
    (px0, py0, px1, py1), (mx0, my0, mx1, my1) = card, MOVIE_CARD
    x0, y0, x1, y1 = line
    sx, sy = (px1 - px0) / (mx1 - mx0), (py1 - py0) / (my1 - my0)
    return tuple(round(v, 3) for v in (px0 + (x0 - mx0) * sx, py0 + (y0 - my0) * sy, px0 + (x1 - mx0) * sx, py0 + (y1 - my0) * sy))

def bars(defs, y0, y1):
    return {"op": "lines", "width": 1, "cap": "butt",
            "segments": [[x + w / 2, y0, x + w / 2, y1, w, c] for x, w, c in defs]}

def cafe(book):
    ops = [{"op": "erase", "rect": list(BOARD), "fill": BOARD_PAPER}, bars(BOARD_UNDER, BOARD[1], BOARD[3])]
    for t, box, face in BOARD_LINES:
        ops.append({"op": "text", "text": t, **(SCRIPT if face == "script" else book), "cap": round(box[3] - box[1], 2),
                    "colour": BOARD_INK, "box": list(box)})
    ops.append(bars(BOARD_BARS, BOARD[1], BOARD[3]))
    t, (x0, y0, x1, y1), ink = BAND
    ops.append({"op": "erase", "rect": [x0 - 1.0, y0 - 0.6, x1 + 0.8, y1 + 0.4], "where": {"lum_above": 60}, "grow": 0.5,
                "halo": {"within": 0.8, "lum_above": 50}})
    ops.append({"op": "text", "text": t, **book, "cap": round(y1 - y0, 2), "colour": ink, "box": [x0, y0, x1, y1]})
    ops.append(bars(BAND_BARS, y0 - 0.6, y1 + 0.4))
    for card in CARDS:
        ops.append({"op": "erase", "rect": list(card), "fill": CARD_PAPER})
    for card in CARDS:
        for t, line, face in CARD_LINES:
            b = card_box(card, line)
            ops.append({"op": "text", "text": t, **(SCRIPT if face == "script" else book), "cap": round(b[3] - b[1], 3),
                        "colour": CARD_INK, "box": list(b)})
    return ops

def spanner(bar, box, ink, lines):
    # the painted letters off the spanner's bar (filled from the bar's white round them), the lines below off the blue
    ops = [{"op": "erase", "rect": list(bar), "where": {"lum_below": 150}, "grow": 0.6, "halo": {"within": 0.8, "lum_below": 200}}]
    for t, (x0, y0, x1, y1) in lines:
        ops.append({"op": "erase", "rect": [x0 - 1.0, y0 - 0.8, x1 + 1.0, min(64, y1 + 0.8)], "where": {"lum_above": 120}, "grow": 0.6,
                    "halo": {"within": 0.8, "lum_above": 110}})
    ops.append({"op": "text", "text": "SPANNER'S", **GROT, "embolden": EMBOLDEN, "cap": round(box[3] - box[1], 2), "colour": ink, "box": list(box)})
    for t, (x0, y0, x1, y1) in lines:
        ops.append({"op": "text", "text": t, **GROT, "embolden": EMBOLDEN, "cap": round(y1 - y0, 2), "colour": WHITE_INK, "box": [x0, y0, x1, y1]})
    return ops

def truck_stop():
    box = (5.71, 27.95, 122.65, 36.94)
    return [{"op": "erase", "rect": [4.5, 27.0, 124.0, 38.0], "where": {"keyed": True}, "opaque": True, "grow": 0.3},
            {"op": "erase", "rect": [4.5, 27.0, 124.0, 38.0], "where": {"lum_below": 150}, "grow": 0.6, "halo": {"within": 0.8, "lum_below": 170}},
            {"op": "text", "text": "TRUCK STOP - RESTAURANT", **NARROW, "embolden": 0.2, "cap": round(box[3] - box[1], 2), "colour": [6, 5, 6],
             "box": list(box)}]

def garage():
    lines = [('We are a "Service Station"', (29.15, 68.19, 104.07, 79.07)), ("Visit our Full-Service Garage", (28.0, 89.93, 105.19, 101.0))]
    ops = [{"op": "erase", "rect": [x0 - 1.0, y0 - 0.8, x1 + 1.0, y1 + 0.8], "where": {"lum_below": 200}, "grow": 0.6,
            "halo": {"within": 0.8, "lum_below": 225}} for t, (x0, y0, x1, y1) in lines]
    for t, (x0, y0, x1, y1) in lines:
        ops.append({"op": "text", "text": t, **NARROW, "cap": round(y1 - y0, 2), "colour": [0, 11, 147], "box": [x0, y0, x1, y1]})
    return ops

# the building's roof edges, which the upscale draws wavy: the cafe's side frames (a 1-texel line stepping down the curved
# roof: its dark texels off, the line drawn again through each row's centre of ink, as a polyline) and the dark specks the
# painting leaves on its keyed roof line (off); the store's blue roof against its white wall (a colour edge the upscale ticks:
# a band of each colour laid along the edge fitted to the painting's per-column crossing, y = a x + b, white first)
FRAMES = {
    "fn_1cfl1": ("the cafe's left side", [69, 64, 66],
                 [(121.35, 13.0), (116.74, 17.75), (112.1, 22.5), (107.43, 27.25), (102.73, 32.0), (98.0, 36.75), (93.23, 41.5),
                  (88.44, 46.25), (83.61, 51.0)],
                 [(82.6, 12.5, 122.0, 51.0), (66.0, 13.0, 104.0, 42.0)]),
    "fn_1cfr1": ("the cafe's right side", [65, 62, 60], [(5.56, 11.0), (26.6, 34.0)],
                 [(6.0, 10.5, 17.5, 21.5), (17.0, 21.0, 27.5, 34.5), (18.0, 10.5, 25.0, 16.0)]),
}
ROOF_EDGES = {
    "fn_1stl1": ("the store's left side", (-0.34841, 52.954), (0.0, 100.0), [0, 24, 139], [255, 250, 246]),
    "fn_1str1": ("the store's right side", (0.34843, 8.539), (28.0, 128.0), [24, 40, 131], [222, 218, 213]),
}

def frame(ink, poly, rects):
    ops = [{"op": "erase", "rect": list(r), "where": {"lum_below": 100}, "grow": 0.5, "halo": {"within": 0.7, "lum_below": 110}}
           for r in rects]
    ops.append({"op": "lines", "width": 1.2, "colour": ink, "cap": "round",
                "segments": [[x0, y0, x1, y1] for (x0, y0), (x1, y1) in zip(poly, poly[1:])]})
    return ops

def roof_edge(line, span, blue, white):
    a, b = line; x0, x1 = span
    def seg(off):
        return [x0, round(a * x0 + b + off, 3), x1, round(a * x1 + b + off, 3)]
    # white from half a texel over the edge to two under it, then blue from two over it to the edge: one crisp crossing
    return [{"op": "lines", "width": 2.5, "colour": white, "cap": "butt", "segments": [seg(0.75)]},
            {"op": "lines", "width": 2.0, "colour": blue, "cap": "butt", "segments": [seg(-1.0)]}]

def edges():
    out = {t: (f"{what} of Spanner's truck stop: its roof frame line", frame(ink, poly, rects)) for t, (what, ink, poly, rects) in FRAMES.items()}
    out.update({t: (f"{what} of Spanner's truck stop: its roof edge", roof_edge(line, span, blue, white))
                for t, (what, line, span, blue, white) in ROOF_EDGES.items()})
    return out

def specs(book=BOOK):
    out = {"fn_1cff1": ("the cafe front of Spanner's truck stop: its name board, window band and cards", cafe(book)),
           "fn_1s2_1": ("TRUCK STOP - RESTAURANT on Spanner's truck stop", truck_stop()),
           "fn_1gar1": ("the garage wall of Spanner's truck stop", garage())}
    for tex, (what, bar, box, ink, lines) in SPANNERS.items():
        out[tex] = (what, spanner(bar, box, ink, lines))
    return out

if __name__ == "__main__":
    book = dict(BOOK, font=sys.argv[1]) if len(sys.argv) > 1 else BOOK
    out_dir = sys.argv[2] if len(sys.argv) > 2 else OUT
    for tex, (what, ops) in specs(book).items():
        s = {"textures": [tex], "note": f"{what}: its lettering; written by gen/spanners.py", "ops": ops}
        with open(os.path.join(out_dir, f"signs-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
    for tex, (what, ops) in edges().items():
        s = {"textures": [tex], "note": f"{what}; written by gen/spanners.py", "ops": ops}
        with open(os.path.join(out_dir, f"truck-stop-{tex}.json"), "w", encoding="utf-8") as f: json.dump(s, f, indent=1, ensure_ascii=False); f.write("\n")
