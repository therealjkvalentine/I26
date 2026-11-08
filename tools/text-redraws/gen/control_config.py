#!/usr/bin/env python3
"""Writes menu/shellbg16.json: Control Configuration's page (shell record 16, 640 x 480) lettered again for the HD menu art.
The title reads CONTROLS (the menu's name in the port) and the four buttons keep their words, all in Arial Bold, the
original's own face, as the rest of the HD menu art's pages are lettered: each word at the original's cap height on the
original's centre, squeezed across as the original's is (a button's word to its own ink's width, CONTROLS by the old title's
squeeze, 0.931), in one ink for the page, the commonest solid ink of the original's title. Arial is not shipped: the tool
takes the machine's own copy (fonts.json's "system" face). The row names are taken off and not set again: over the
HD art the game letters them itself, in its caption face, on the same ink boxes (ControlsScreen.Names), and the painted ones
showed through beside them. Each painted word is taken off and filled from the paper round it (the title's and the buttons' ink, darker than the
paper, grown over the light ring the menu art's sharpening left round it; a row name's whole cell between its rules, with
an empty row's grain), and the title and the buttons' words are set again.

    python3 tools/text-redraws/gen/control_config.py
"""
import json, os
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "menu")
TITLE_FONT, TITLE_WEIGHT = "Arial", None     # Arial Bold's own file; a variable face would take its weight here
INK = [82, 66, 41]
# measured on the original (record 16): the ink boxes' cap height, centre and run width (Arial Bold's advances squeezed
# to the ink's width), as tools/text-redraws' "cap", "center" and "width"
TITLE = {"cap": 21.26, "center": [329.5, 21.0], "width": 154.76}     # CONTROL CONFIGURATION's cap and squeeze, CONTROLS' run
BUTTON_CAP = 9.66
# the rows' words (for reading the spec; they are not set again), the rules above and below each row
LEFT = ["Accelerate", "Brake", "Steer Left", "Steer Right", "Trans. Shift Left", "Trans. Shift Right", "Target Front Enemy",
        "Target Next Enemy", "Target Nearest Enemy", "Cancel Target", "Radar Zoom", "Toggle Weapon Cam", "Ignition", "Map", "Notepad",
        "Binoculars", "Horn", "Emergency Brake", "Headlights"]
RIGHT = ["Look at Dash", "Look Left", "Look Right", "Look Rear", "Look at Target", "Fire", "Cycle Weapon", "Link Like Weapon",
         "Fire Hardpoint 1", "Fire Hardpoint 2", "Fire Hardpoint 3", "Fire Hardpoint 4", "Fire Hardpoint 5", "Use Special 1",
         "Use Special 2", "Use Special 3", "Poem", "Quick View Toggle", "Toggle Reverse"]
EMPTY = [366, 383, 400]          # the empty rows' top rules, under the last names
RULES = [42, 59, 76, 93, 110, 127, 144, 161, 178, 195, 212, 229, 247, 264, 281, 298, 315, 332, 349, 366]
# the painted runs' left and right pixels, row by row
RUNS_L = [(35, 99), (35, 67), (35, 101), (35, 107), (35, 143), (35, 149), (35, 157), (35, 151), (35, 170), (35, 123), (35, 103),
          (38, 154), (35, 84), (35, 55), (35, 82), (35, 97), (35, 62), (34, 135), (35, 98)]
RUNS_R = [(333, 409), (333, 393), (333, 399), (333, 391), (333, 425), (334, 357), (334, 413), (334, 433), (334, 428), (334, 432),
          (334, 432), (334, 431), (334, 433), (334, 411), (334, 416), (334, 416), (334, 361), (333, 442), (336, 425)]
BUTTONS = [("CALIBRATE", (43, 115)), ("RESTORE", (209, 269)), ("DONE", (382, 417)), ("CANCEL", (535, 588))]
# each button word's centre and run width on the original
BUTTON_SET = {"CALIBRATE": (79.49, 72.99), "RESTORE": (239.0, 62.0), "DONE": (400.0, 36.0), "CANCEL": (562.0, 54.0)}

def spec(title_font=TITLE_FONT, title_weight=TITLE_WEIGHT, title="CONTROLS"):
    tv = {"variation": {"Weight": title_weight}} if title_weight else {}
    # the old title's ends, outside the new word, take the paper's grain from beside the title; its middle is under the new word
    ops = [{"op": "erase", "rect": [138, 6, 250, 35], "where": {"lum_below": 120}, "grow": 1.6, "texture": [-130, 0]},
           {"op": "erase", "rect": [250, 6, 410, 35], "where": {"lum_below": 120}, "grow": 1.6},
           {"op": "erase", "rect": [410, 6, 522, 35], "where": {"lum_below": 120}, "grow": 1.6, "texture": [112, 0]}]
    txt = [dict({"op": "text", "text": title, "font": title_font, **TITLE, "colour": INK}, **tv)]
    for words, runs in ((LEFT, RUNS_L), (RIGHT, RUNS_R)):
        for i, (w, (x0, x1)) in enumerate(zip(words, runs)):
            a, b = RULES[i], RULES[i + 1]
            # the cell's paper between its rules, the name's run and a margin, filled from round it and given the grain of an
            # empty ruled row below the table's last name, the same place in its row (some names are a pale ink)
            t = EMPTY[i % len(EMPTY)]
            ops.append({"op": "erase", "rect": [x0 - 4, a + 2, x1 + 6, b - 1], "texture": [0, t - a]})
    for w, (x0, x1) in BUTTONS:
        ops.append({"op": "erase", "rect": [x0 - 1, 453, x1 + 2, 466.5], "where": {"lum_below": 100}, "grow": 1.6})
        cx, width = BUTTON_SET[w]
        txt.append(dict({"op": "text", "text": w, "font": title_font, "cap": BUTTON_CAP, "center": [cx, 460.0], "width": width, "colour": INK}, **tv))
    return {"textures": ["shellbg16"], "note": "Control Configuration's page: the title CONTROLS and the buttons' words set again, the painted row names taken off (the game captions them); written by gen/control_config.py",
            "ops": ops + txt}

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "shellbg16.json"), "w", encoding="utf-8") as f: json.dump(spec(), f, indent=1); f.write("\n")
