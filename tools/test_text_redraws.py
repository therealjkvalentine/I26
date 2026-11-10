"""Tests of tools/text-redraws.py (python -m unittest tools/test_text_redraws.py, or pytest tools/). No game art: made-up
textures and the specs' own geometry."""
import glob, importlib.util, json, os, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("text_redraws", os.path.join(HERE, "text-redraws.py"))
tr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(tr)

def blank(w, h, rgb=(128, 128, 128), a=255):
    im = np.zeros((h, w, 4), np.uint8); im[..., :3] = rgb; im[..., 3] = a
    return im

class FontsTests(unittest.TestCase):
    def test_every_font_loads_and_ships_its_licence(self):
        table = tr.font_table()
        for name, t in table.items():
            if t.get("system"): continue                 # not shipped: the machine's own copy (test_system_font_...)
            self.assertTrue(os.path.exists(os.path.join(tr.FONTS, t["file"])), name)
            d=os.path.join(tr.FONTS, os.path.dirname(t["file"])); self.assertTrue(os.path.exists(os.path.join(d, "OFL.txt")) or os.path.exists(os.path.join(d, "LICENSE.txt")), name)
            self.assertTrue("OFL" in t["license"] or "Apache" in t["license"], name)
            self.assertGreater(tr.cap_ratio(name), 0.4, name)

    def test_system_font_is_not_shipped_and_resolves_where_installed(self):
        table = tr.font_table()
        for name, t in table.items():
            if not t.get("system"): continue
            self.assertFalse(os.path.exists(os.path.join(tr.FONTS, t["file"])), name)    # never a copy in the repo
            self.assertIn("not shipped", t["license"], name)
            p = tr.font_path(t)
            if p is None: continue                       # not installed here: the tool says so when a spec needs it
            self.assertTrue(os.path.isabs(p) and os.path.exists(p), name)
            self.assertGreater(tr.cap_ratio(name), 0.4, name)

class SpecTests(unittest.TestCase):
    def specs(self):
        out = []
        for p in sorted(glob.glob(os.path.join(tr.SPECS, "*.json")) + glob.glob(os.path.join(tr.SPECS, "maps", "*.json"))):
            with open(p, encoding="utf-8") as f: out.append((os.path.basename(p), json.load(f)))
        return out

    def test_specs_parse_and_name_known_fonts_and_ops(self):
        table = tr.font_table(); specs = self.specs()
        self.assertGreaterEqual(len(specs), 6)
        for name, spec in specs:
            self.assertTrue(spec["textures"], name)
            for op in spec["ops"]:
                self.assertIn(op["op"], ("erase", "text", "smooth", "marks"), name)
                if op["op"] == "text":
                    self.assertIn(op["font"], table, name)
                    self.assertGreater(op["cap"], 0, name)
                    self.assertIn(op.get("alpha", "keep"), ("keep", "set", "cover"), name)
                else:
                    x0, y0, x1, y1 = op["rect"]; self.assertLess(x0, x1, name); self.assertLess(y0, y1, name)

    def test_each_spec_letters_inside_its_own_geometry(self):
        """Every spec rendered over a flat made-up texture (the original's size from its rects, 4x): ink lands, and only
        near where the spec's text centres are."""
        for name, spec in self.specs():
            texts = [op for op in spec["ops"] if op["op"] == "text"]
            if not texts: continue                       # an erase-only spec (zmiri101: the mirror's frame) letters nothing
            for op in texts:
                if "quad" in op: op.setdefault("center", [sum(p[0] for p in op["quad"]) / 4, sum(p[1] for p in op["quad"]) / 4])
                if "box" in op:
                    op.setdefault("center", [(op["box"][0] + op["box"][2]) / 2, (op["box"][1] + op["box"][3]) / 2])
                    op.setdefault("width", op["box"][2] - op["box"][0])
            w = int(max(max(op["center"][0] + op["cap"] * 4 for op in texts), max((op["rect"][2] for op in spec["ops"] if op["op"] in ("erase", "marks")), default=0))) + 2
            h = int(max(max(op["center"][1] + op["cap"] * 2 for op in texts), max((op["rect"][3] for op in spec["ops"] if op["op"] in ("erase", "marks")), default=0))) + 2
            orig = blank(w, h, (128, 128, 128))
            px = np.repeat(np.repeat(orig, 4, 0), 4, 1)
            out = tr.apply_ops(px, orig, spec["ops"]).astype(int)
            changed = np.abs(out[..., :3] - px[..., :3].astype(int)).sum(axis=2) > 24
            self.assertGreater(changed.sum(), 50 * len(texts), name)
            ys, xs = np.where(changed)
            near = np.zeros(len(xs), bool)
            for op in texts:
                r = op["cap"] * 4 * 4 + (op.get("width", 0) * 4)       # a generous box round each string
                near |= (np.abs(xs - op["center"][0] * 4) < r) & (np.abs(ys - op["center"][1] * 4) < r)
            self.assertTrue(near.all(), f"{name}: ink away from every text centre")

class CompositorTests(unittest.TestCase):
    def test_an_8x_pack_letters_the_same_place_as_a_4x(self):
        # the spec is in the original's texels: the 8x pack's lettering lands on the 4x's, scaled, and the inpaint relaxes further
        orig = blank(64, 32, (200, 200, 200)); orig[10:20, 12:40, :3] = 30
        ops = [{"op": "erase", "rect": [10, 8, 42, 22], "where": {"lum_below": 60}, "grow": 0.5},
               {"op": "text", "text": "AB", "font": "Anton", "cap": 8, "center": [26, 15], "colour": [10, 10, 10]}]
        boxes = []
        for s in (4, 8):
            px = np.repeat(np.repeat(orig, s, 0), s, 1)
            out = tr.apply_ops(px, orig, ops)
            ink = out[..., :3].mean(axis=2) < 80
            ys, xs = np.where(ink)
            boxes.append(np.array([xs.min(), ys.min(), xs.max(), ys.max()]) / s)
        self.assertLess(np.abs(boxes[0] - boxes[1]).max(), 1.0)
        self.assertEqual(tr.relax_passes(4), 24)
        self.assertEqual(tr.relax_passes(8), 96)

    def test_self_check(self):
        self.assertTrue(tr.self_check())

    def test_erase_inpaints_from_round_the_hole_and_halo_takes_the_soft_edge(self):
        orig = blank(16, 16, (200, 200, 200)); orig[6:10, 6:10, :3] = 10
        px = np.repeat(np.repeat(orig, 4, 0), 4, 1).copy()
        px[22:42, 22:42, :3] = 60                                  # an upscale's soft edge past the painted texels
        px[24:40, 24:40, :3] = 10
        plain = tr.apply_ops(px, orig, [{"op": "erase", "rect": [5, 5, 11, 11], "where": {"lum_below": 50}}])
        halo = tr.apply_ops(px, orig, [{"op": "erase", "rect": [5, 5, 11, 11], "where": {"lum_below": 50}, "halo": {"within": 1, "lum_below": 100}}])
        self.assertLess(plain[20:44, 20:44, :3].min(), 100)          # without the halo the soft edge stays and smears in
        self.assertGreater(halo[18:46, 18:46, :3].min(), 190)        # with it the hole is the background's

    def test_quad_puts_the_run_on_its_corners_and_matches_the_plain_box(self):
        orig = blank(40, 20, (220, 220, 220)); px = np.repeat(np.repeat(orig, 4, 0), 4, 1)
        base = {"op": "text", "text": "HIH", "font": "Anton", "cap": 8, "colour": [0, 0, 0]}
        plain = tr.apply_ops(px, orig, [dict(base, center=[20, 10], width=16)])[..., 0] < 128
        quad = tr.apply_ops(px, orig, [dict(base, quad=[[12, 6], [28, 6], [28, 14], [12, 14]])])[..., 0] < 128
        self.assertGreater((plain & quad).sum() / max(1, (plain | quad).sum()), 0.9)   # an axis-aligned quad is the box
        tilt = tr.apply_ops(px, orig, [dict(base, quad=[[12, 4], [28, 8], [28, 16], [12, 12]])])[..., 0] < 128
        ys, xs = np.where(tilt)
        left, right = ys[xs < xs.mean()].mean(), ys[xs > xs.mean()].mean()
        self.assertGreater((right - left) / 4, 1.5)                                     # the right end sits lower (half the 4-texel drop, by halves)

    def test_marks_draw_the_originals_keyed_texels_in_place_with_a_smooth_outline(self):
        # a distressed word's cracks: keyed texels inside a painted letter, which a car's panel shows black; the pack bled the
        # panel's colour under them. marks: each in its place, its size, the given colour, at 4x as at 8x; a lone texel kept at a low level
        orig = blank(24, 12, (40, 160, 50)); orig[4:6, 6:8, 3] = 0; orig[8, 16, 3] = 0
        for s in (4, 8):
            px = np.repeat(np.repeat(orig, s, 0), s, 1).copy(); px[orig.repeat(s, 0).repeat(s, 1)[..., 3] == 0, :3] = (30, 60, 200)
            out = tr.apply_ops(px, orig, [{"op": "marks", "rect": [0, 0, 24, 12], "where": {"keyed": True}, "colour": [0, 0, 0], "sigma": 0.45, "level": 0.3}])
            dark = out[..., :3].max(axis=2) < 60
            lab = dark[: 7 * s]; ys, xs = np.where(lab)
            box = np.array([xs.min(), ys.min(), xs.max() + 1, ys.max() + 1]) / s
            self.assertLess(np.abs(box - np.array([6, 4, 8, 6])).max(), 0.5, f"{s}x: {box}")     # the 2 x 2 mark where it was painted
            self.assertTrue(dark[8 * s + s // 2, 16 * s + s // 2], f"{s}x: the lone texel's mark kept")
            self.assertFalse((out[..., 2] > 150).any(), f"{s}x: no blue left under the key")
            corner = out[4 * s, 6 * s, :3].max(); middle = out[5 * s, 7 * s, :3].max()
            self.assertGreater(int(corner), int(middle), f"{s}x: the corners rounded, not the texels' squares")
            self.assertTrue((out[..., 3] == px[..., 3]).all(), "the key itself untouched")

    def test_box_sets_the_runs_ink_on_the_box_at_any_scale(self):
        # a badge's letters on the painted letters' own box: the ink (an apostrophe's and an ascender's tops to the baseline,
        # first letter to last) fills it, no larger, the stroke round it, at 4x as at 8x
        orig = blank(40, 16, (0, 0, 0), a=0)
        op = {"op": "text", "text": "'rahna", "font": "Comfortaa", "variation": {"Weight": 700}, "cap": 4.4, "box": [9, 5.6, 31, 10.4],
              "colour": [255, 255, 255], "alpha": "set"}
        for s in (4, 8):
            px = np.repeat(np.repeat(orig, s, 0), s, 1)
            ink = tr.apply_ops(px, orig, [op])[..., 0] > 128
            ys, xs = np.where(ink)
            box = np.array([xs.min(), ys.min(), xs.max() + 1, ys.max() + 1]) / s
            self.assertLess(np.abs(box - np.array(op["box"])).max(), 0.3, f"{s}x: {box}")
            bold = tr.apply_ops(px, orig, [dict(op, embolden=0.25)])[..., 0] > 128
            ys, xs = np.where(bold)
            self.assertLess(np.abs(np.array([xs.min(), ys.min(), xs.max() + 1, ys.max() + 1]) / s - np.array(op["box"])).max(), 0.3)
            self.assertGreater(bold.sum(), 1.3 * ink.sum(), "embolden thickens the strokes inside the same box")
            dark = tr.apply_ops(px, orig, [dict(op, stroke={"width": 0.35, "colour": [40, 40, 40]})])
            rim = (dark[..., 3] == 255) & (dark[..., :3].max(axis=2) < 140)
            self.assertGreater(rim.sum(), 0)
            self.assertLess(np.where(rim)[1].min() / s, op["box"][0])        # the rim outside the ink, dark (no light fringe)
        fit = tr.apply_ops(px, orig, [dict(op, quad=[[9, 5.6], [31, 5.6], [31, 10.4], [9, 10.4]], fit="ink", box=None)])[..., 0] > 128
        self.assertGreater((fit & ink).sum() / max(1, (fit | ink).sum()), 0.95)  # "fit": "ink" on a square quad is the box

    def test_key_under_original_is_the_originals_key_colour_not_its_letters(self):
        # a chrome badge (opaque greys) on a keyed strip whose colour is dark blue; the pack bled the chrome into the strip
        orig = blank(24, 12, (10, 10, 40), a=0); orig[4:8, 6:18, :3] = 200; orig[4:8, 6:18, 3] = 255
        s = 4; px = np.repeat(np.repeat(orig, s, 0), s, 1).copy(); px[..., :3] = 180      # the model's pale ghost everywhere
        out = tr.apply_ops(px, orig, [{"op": "erase", "rect": [0, 0, 24, 12], "where": {"keyed": True}, "fill": "key", "under": "original"},
                                      {"op": "erase", "rect": [5, 3, 19, 9], "where": {"opaque": True}, "grow": 0.5, "fill": "key", "under": "original"}])
        self.assertTrue((out[..., 3] == 0).all())
        self.assertTrue((out[..., :3] == (10, 10, 40)).all(), "the strip and the erased badge both take the key's own colour")

    def test_hang_centres_the_letters_and_lets_the_stop_hang(self):
        orig = blank(60, 16, (230, 230, 230)); px = np.repeat(np.repeat(orig, 4, 0), 4, 1)
        op = {"op": "text", "text": "SUSP.", "font": "RussoOne", "cap": 8, "center": [30, 8], "width": 28, "colour": [0, 0, 0]}
        whole = tr.apply_ops(px, orig, [op])[..., 0] < 128
        hung = tr.apply_ops(px, orig, [dict(op, hang=".")])[..., 0] < 128
        xs = np.where(hung.any(0))[0] / 4
        self.assertAlmostEqual(xs.min(), 16, delta=0.6)                  # the letters' run: 28 wide about x 30
        self.assertGreater(xs.max(), 44.5)                                 # the stop past it
        ys = np.where(hung.any(1))[0] / 4
        self.assertAlmostEqual((ys.min() + ys.max() + 0.25) / 2, 8, delta=0.3)   # the cap band's middle on the centre
        self.assertLess(np.where(whole.any(0))[0].max() / 4, xs.max())   # without it the stop is squeezed in

    def test_set_alpha_keys_by_half_coverage(self):
        orig = blank(24, 16, (0, 0, 0), a=0)
        px = np.repeat(np.repeat(orig, 4, 0), 4, 1)
        out = tr.apply_ops(px, orig, [{"op": "text", "text": "8", "font": "ArchivoNarrow", "cap": 10, "center": [12, 8], "colour": [255, 255, 255], "alpha": "set"}])
        a = out[..., 3]
        self.assertTrue(set(np.unique(a)) <= {0, 255})
        self.assertGreater((a == 255).sum(), 200)
        self.assertGreater(out[a == 255][:, :3].mean(), 180)

    def test_smooth_fades_a_dithered_light_and_leaves_out_its_letters_and_the_rest(self):
        # a light dithered between two levels (a checkerboard), dark letters in its middle, a red cell beside it
        orig = blank(24, 8, (200, 200, 0)); orig[::2, ::2, :3] = (120, 120, 0); orig[1::2, 1::2, :3] = (120, 120, 0)
        orig[3:5, 8:12, :3] = 10; orig[:, 16:, :3] = (255, 0, 0)
        rng = np.random.default_rng(1)
        px = np.repeat(np.repeat(orig, 4, 0), 4, 1).copy(); px[..., :3] = np.clip(px[..., :3] + rng.integers(-40, 40, px[..., :3].shape), 0, 255)
        out = tr.apply_ops(px, orig, [{"op": "smooth", "rect": [0, 0, 16, 8], "sigma": 1.5, "where": {"lum_above": 40}}])
        light = out[:32, :64, :3].astype(float)
        self.assertLess(np.abs(light[:, :, 0] - 160).max(), 12, "the dither's two levels become their mean, the model's mottle gone")
        self.assertGreater(light[12:20, 32:48, 0].min(), 140, "the letters are left out: the light's shade under them")
        self.assertTrue((out[:, 64:] == px[:, 64:]).all(), "nothing outside the rect, and nothing from it")

    def test_note_pack_attributes_fonts_once(self):
        import tempfile
        d = tempfile.mkdtemp()
        with open(os.path.join(d, "pack.json"), "w") as f: json.dump({"attribution": "model X"}, f)
        with open(os.path.join(d, "ATTRIBUTION.txt"), "w") as f: f.write("model X\n")
        table = tr.font_table(); spec = {"ops": [{"op": "text", "font": "Anton"}]}
        for _ in range(2): tr.note_pack(d, [("pp11bkb1", "pp11bkb1.json", spec)], table)
        info = json.load(open(os.path.join(d, "pack.json")))
        self.assertEqual(info["text_redraws"], [{"texture": "pp11bkb1", "spec": "tools/text-redraws/pp11bkb1.json", "fonts": ["Anton"]}])
        self.assertEqual(info["attribution"].count("lettering redrawn in Anton"), 1)
        self.assertTrue(info["attribution"].startswith("model X"))
        lines = open(os.path.join(d, "ATTRIBUTION.txt")).read().splitlines()
        self.assertEqual(lines[0], "model X"); self.assertEqual(sum(l.startswith("lettering redrawn in Anton") for l in lines), 1)

class CompassTests(unittest.TestCase):
    """compass-zcme.json: the heading strip the cockpit's compass window slides along. The heading reads right only if every
    letter and tick is on the original's column, at any scale (the N's ink centred on texel column 19.5 and 283.5: the
    original's, measured)."""
    def strip(self, s):
        with open(os.path.join(tr.SPECS, "compass-zcme.json"), encoding="utf-8") as f: spec = json.load(f)
        orig = blank(303, 18, (221, 221, 221)); orig[0, :, :3] = 153; orig[1, :, :3] = 170; orig[2, :, :3] = 204
        px = np.repeat(np.repeat(orig, s, 0), s, 1)
        return spec, tr.apply_ops(px, orig, spec["ops"])[..., :3].mean(axis=2) < 118

    def test_the_letters_sit_on_the_originals_columns_at_4x_and_8x(self):
        for s in (4, 8):
            spec, ink = self.strip(s)
            for x0, x1 in ((14, 25), (278, 289)):            # the big N at each end of the strip
                xs = np.where(ink[3 * s:16 * s, x0 * s - s:x1 * s + s].any(0))[0] + x0 * s - s
                self.assertAlmostEqual((xs.min() + xs.max() + 1) / 2 / s, (x0 + x1) / 2, delta=0.1, msg=f"{s}x N at {x0}")
            self.assertEqual([(x0 + x1) / 2 for x0, x1 in ((14, 25), (278, 289))], [19.5, 283.5])
            for op in (o for o in spec["ops"] if o["op"] == "text"):
                x0, y0, x1, y1 = op["box"]
                sub = ink[int(y0 * s):int(np.ceil(y1 * s)), int(x0 * s) - 2:int(np.ceil(x1 * s)) + 2]
                xs = np.where(sub.any(0))[0] + int(x0 * s) - 2
                self.assertAlmostEqual((xs.min() + xs.max() + 1) / 2 / s, (x0 + x1) / 2, delta=0.15, msg=f"{s}x {op['text']} at {x0}")

    def test_the_ticks_are_crisp_texel_columns(self):
        s = 8; spec, ink = self.strip(s)
        for op in (o for o in spec["ops"] if o["op"] == "erase" and o.get("fill") == [16, 16, 16]):
            x0, y0, x1, y1 = op["rect"]
            self.assertTrue(ink[y0 * s:y1 * s, x0 * s:x1 * s].all(), f"tick at {x0}")
            self.assertFalse(ink[y0 * s:y1 * s, x0 * s - 1].any() or ink[y0 * s:y1 * s, x1 * s].any(), f"tick at {x0} has a soft edge")

def spec_of(name):
    with open(os.path.join(tr.SPECS, name), encoding="utf-8") as f: return json.load(f)

def ink_box(op, w, h, s):
    """The text op alone (no shadow) set over a flat dark plate at s pack texels a texel: its ink's extent in texels."""
    orig = blank(w, h, (20, 30, 60)); px = np.repeat(np.repeat(orig, s, 0), s, 1)
    o = dict(op, colour=[255, 255, 255]); o.pop("shadow", None)
    ink = tr.apply_ops(px, orig, [o])[..., :3].mean(axis=2) > 140
    ys, xs = np.where(ink)
    return np.array([xs.min(), ys.min(), xs.max() + 1, ys.max() + 1]) / s

class WorldLetteringTests(unittest.TestCase):
    """The Autowerks wall sign (ca_1ws_1, T12), the Milky Way Motel billboard (ao_1sg_6) and the milk tanker (mt1?bk?1, mt31bk?1):
    every line on the painted letters' ink box, measured off the original's texels (pinned here, in texels), at 4x and 8x."""
    BOXES = {
        "signs-ca_1ws_1.json": {"AUTOWERKS": (4.99, 27.41, 77.89, 36.62), "INDUSTRIAL": (89.43, 27.41, 122.16, 31.73),
                                "KOMPLEX": (95.36, 32.72, 122.07, 36.86)},
        "signs-ao_1sg_6.json": {"Kitchenettes": (10.75, 34.33, 43.73, 37.95), "Daily & Weekly Rates": (69.03, 34.15, 123.55, 38.68),
                                "Direct Dial Phones": (15.0, 40.28, 63.31, 43.99), "NMAA Approved": (73.48, 40.14, 116.66, 44.29),
                                "Cafe Nearby": (19.06, 46.34, 51.26, 50.84), "In ROSWELL": (7.12, 54.98, 45.84, 59.66),
                                "15 MILES": (94.0, 55.11, 122.33, 59.65),
                                '"Out of this World Comfort"': (36.0, 24.87, 122.6, 29.85), '"Family Run"': (78.3, 46.0, 111.0, 50.57)},
    }
    for st in "123":
        BOXES[f"milk-truck-mt1{st}bkl1.json"] = {"MILK": (44.0, 21.0, 80.0, 41.0)}
        BOXES[f"milk-truck-mt1{st}bkb1.json"] = {"THE ESSENCE OF": (12.0, 27.0, 119.0, 34.0), "PURITY": (44.0, 37.0, 88.0, 44.0)}
    BOXES["milk-truck-mt31bkl1.json"] = BOXES["milk-truck-mt31bkr1.json"] = {"MILK": (22.0, 10.0, 40.0, 20.0)}
    SIZE = {"mt31bkl1": (64, 32), "mt31bkr1": (64, 32)}

    def texts(self, name):
        return {op["text"]: op for op in spec_of(name)["ops"] if op["op"] == "text" and op["text"] != "•"}

    def test_every_line_is_on_the_originals_ink_box_at_4x_and_8x(self):
        for name, want in self.BOXES.items():
            spec = spec_of(name); ops = self.texts(name)
            self.assertEqual(set(ops), set(want), name)
            w, h = self.SIZE.get(spec["textures"][0], (128, 64))
            for t, box in want.items():
                op = ops[t]
                set_on = op.get("box") or [op["quad"][0][0], op["quad"][0][1], op["quad"][1][0], op["quad"][2][1]]   # slanted: the top corners
                self.assertLess(np.abs(np.array(set_on) - box).max(), 0.01, f"{name} {t}: the spec's box is the measured one")
                for s in (4, 8):
                    got = ink_box(op, w, h, s)
                    # a slanted line's end marks sit a little under its ascenders, so the slant moves them a fraction further
                    self.assertLess(np.abs(got - box).max(), 0.35 if "box" in op else 0.45, f"{name} {t} at {s}x: {got}")

    def test_the_wall_signs_erase_stays_on_its_plate(self):
        # the plate maps the band of rows 25 to 39 (bevelled at 24 and 39): the old letters go, the bevel and the face round it stay
        for op in spec_of("signs-ca_1ws_1.json")["ops"]:
            if op["op"] == "erase": self.assertTrue(25 <= op["rect"][1] and op["rect"][3] <= 39, op["rect"])
        orig = blank(128, 64, (8, 40, 74)); orig[24, :, :3] = (32, 56, 98); orig[39, :, :3] = (8, 28, 57)
        orig[28:36, 10:20, :3] = 222                                                     # a painted letter
        px = np.repeat(np.repeat(orig, 4, 0), 4, 1)
        out = tr.apply_ops(px, orig, [op for op in spec_of("signs-ca_1ws_1.json")["ops"] if op["op"] == "erase"]).astype(int)
        self.assertLess(np.abs(out[28 * 4:36 * 4, 10 * 4:20 * 4, :3] - (8, 40, 74)).max(), 3)    # gone, the plate's navy in its place
        self.assertTrue((out[24 * 4:25 * 4, :, :3] == (32, 56, 98)).all() and (out[39 * 4:40 * 4, :, :3] == (8, 28, 57)).all())

    def test_the_motels_name_is_its_own_texels_shade_by_shade(self):
        ops = spec_of("signs-ao_1sg_6.json")["ops"]
        marks = [op for op in ops if op["op"] == "marks"]
        self.assertEqual(len(marks), 6)                                                  # three shades on each of the two lines
        for line in (marks[:3], marks[3:]):
            lums = [op["where"]["lum_above"] for op in line]; cols = [sum(op["colour"]) for op in line]
            self.assertEqual(lums, sorted(lums)); self.assertEqual(cols, sorted(cols))   # darkest first, the white core last
        # a pixel letter (a 2-texel stem, 6 high: a grey bottom row) comes back in its texels with its bottom row grey
        orig = blank(128, 64, (8, 8, 16)); orig[8:14, 70:72, :3] = 255; orig[14, 70:72, :3] = 150
        out = tr.apply_ops(np.repeat(np.repeat(orig, 4, 0), 4, 1), orig, [op for op in ops if op["op"] in ("marks",)])[..., :3].mean(axis=2)
        self.assertGreater(out[9 * 4:13 * 4, 70 * 4 + 1:72 * 4 - 1].min(), 240)
        self.assertTrue(120 < out[14 * 4 + 2, 71 * 4] < 180)
        self.assertLess(out[9 * 4:13 * 4, 68 * 4:69 * 4].max(), 40)                     # nothing spills a texel away

    def test_milk_keeps_its_black_shadow_and_both_far_sides_alike(self):
        for st in "123":
            op = self.texts(f"milk-truck-mt1{st}bkl1.json")["MILK"]
            self.assertEqual((op["shadow"]["dx"], op["shadow"]["dy"], op["shadow"]["colour"]), (1, 1, [0, 0, 0]))
            self.assertTrue(any(e["op"] == "erase" and e.get("where") == {"keyed": True} and e.get("opaque") for e in spec_of(f"milk-truck-mt1{st}bkl1.json")["ops"]))
        self.assertEqual(spec_of("milk-truck-mt31bkl1.json")["ops"], spec_of("milk-truck-mt31bkr1.json")["ops"])

if __name__ == "__main__":
    unittest.main()
