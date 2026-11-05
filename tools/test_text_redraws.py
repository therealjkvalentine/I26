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
            self.assertTrue(os.path.exists(os.path.join(tr.FONTS, t["file"])), name)
            d=os.path.join(tr.FONTS, os.path.dirname(t["file"])); self.assertTrue(os.path.exists(os.path.join(d, "OFL.txt")) or os.path.exists(os.path.join(d, "LICENSE.txt")), name)
            self.assertTrue("OFL" in t["license"] or "Apache" in t["license"], name)
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
                self.assertIn(op["op"], ("erase", "text"), name)
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
            for op in texts:
                if "quad" in op: op.setdefault("center", [sum(p[0] for p in op["quad"]) / 4, sum(p[1] for p in op["quad"]) / 4])
            w = int(max(max(op["center"][0] + op["cap"] * 4 for op in texts), max((op["rect"][2] for op in spec["ops"] if op["op"] == "erase"), default=0))) + 2
            h = int(max(max(op["center"][1] + op["cap"] * 2 for op in texts), max((op["rect"][3] for op in spec["ops"] if op["op"] == "erase"), default=0))) + 2
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

if __name__ == "__main__":
    unittest.main()
