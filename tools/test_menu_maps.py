"""Tests of tools/menu-maps.py (python -m unittest tools/test_menu_maps.py, or pytest tools/). No game art, no model:
made-up maps and the specs' own text."""
import importlib.util, json, os, tempfile, unittest
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("menu_maps", os.path.join(HERE, "menu-maps.py"))
mm = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mm)

def paper_with_stroke(s=4, w=40, h=24):
    """An orange paper with a dark 2-texel stroke down column 18..19, the original and a 4x 'upscale' of it with the
    model's ringing: a light rim either side of the stroke."""
    o = np.zeros((h, w, 4), np.uint8); o[..., :3] = (200, 120, 70); o[..., 3] = 255
    o[:, 18:20, :3] = (30, 25, 20)
    big = np.repeat(np.repeat(o, s, 0), s, 1).copy()
    big[:, 18 * s - 3:18 * s, :3] = (240, 170, 120)       # halo left
    big[:, 20 * s:20 * s + 3, :3] = (240, 170, 120)       # halo right
    return o, big

class StemsTests(unittest.TestCase):
    def test_only_zmap_pngs_matching_the_glob_lower_cased(self):
        with tempfile.TemporaryDirectory() as d:
            for f in ("ZMAP6T01.png", "zmap6t02.png", "zmap3t01.png", "zmap6t03.jpg", "znpd6101.png", "bottom-up.txt"):
                open(os.path.join(d, f), "wb").close()
            self.assertEqual(mm.stems(d), ["zmap3t01", "zmap6t01", "zmap6t02"])
            self.assertEqual(mm.stems(d, "zmap6*"), ["zmap6t01", "zmap6t02"])
            self.assertEqual(mm.stems(d, "ZMAP6T0[2]"), ["zmap6t02"])

class InkOpsTests(unittest.TestCase):
    def specs(self):
        for f in sorted(os.listdir(mm.MAP_SPECS)):
            with open(os.path.join(mm.MAP_SPECS, f), encoding="utf-8") as fh: yield f[:-5], json.load(fh)

    def test_each_word_inked_within_the_ranges_and_repeatably(self):
        n = 0
        for stem, spec in self.specs():
            a, b = mm.ink_ops(stem, spec), mm.ink_ops(stem, spec)
            self.assertEqual(a, b)
            self.assertEqual(len(a), len(spec["ops"]))
            for op, src in zip(a, spec["ops"]):
                if src["op"] != "text": self.assertEqual(op, src); continue
                n += 1
                lo, hi = mm.INK["opacity"]; self.assertTrue(lo <= op["ink"]["opacity"] <= hi)
                lo, hi = mm.INK["feather"]; self.assertTrue(lo <= op["ink"]["feather"] <= hi)
                self.assertLessEqual(abs(op["center"][1] - src["center"][1]), mm.INK["jitter"] + 1e-6)
                self.assertEqual(op["center"][0], src["center"][0])
                self.assertLessEqual(abs(op["rotate"] - src.get("rotate", 0)), mm.INK["turn"] + 1e-6)
                if "stroke" in op: self.assertLessEqual(op["stroke"]["width"], mm.INK["weight"][1]); self.assertEqual(op["stroke"]["colour"], src["colour"])
        self.assertGreater(n, 5)
        # the words differ from one another (a hand, not a stamp)
        ops = [o for o in mm.ink_ops("zmap6t01", dict(next(self.specs())[1])) if o["op"] == "text"]
        self.assertGreater(len({o["ink"]["opacity"] for o in ops}), 1)

    def test_ink_is_multiplied_into_the_paper_feathered_and_translucent(self):
        paper = np.zeros((32, 64, 4), np.uint8); paper[..., :3] = (200, 120, 70); paper[..., 3] = 255
        s = 4; px = np.repeat(np.repeat(paper, s, 0), s, 1)
        op = {"op": "text", "text": "I", "font": "PermanentMarker", "cap": 20, "center": [32, 16], "colour": [34, 28, 24]}
        solid = mm.tr.apply_ops(px, paper, [op])
        inked = mm.tr.apply_ops(px, paper, [dict(op, ink={"opacity": 0.8, "feather": 1.5})])
        core = solid[..., :3].mean(axis=2) < 60
        self.assertTrue(core.sum() > 100)
        # the core: the paper times the ink at 80 %, keeping the paper's warmth (the solid ink's red is 10 over its blue)
        c = inked[core][:, :3].astype(int).mean(axis=0)
        self.assertGreater(c[0] - c[2], 25)
        # feathered: more partly inked texels round the stroke than the solid one has
        part = lambda im: ((im[..., :3].mean(axis=2) < 125) & (im[..., :3].mean(axis=2) > 50)).sum()
        self.assertGreater(part(inked), part(solid))

class CleanTests(unittest.TestCase):
    def test_the_halo_goes_and_the_stroke_stays(self):
        o, big = paper_with_stroke()
        out = mm.clean(big, o).astype(int)
        s = 4; row = out[40]
        # the light rim is clipped back to about the paper
        self.assertLess(row[18 * s - 2, 0], 210); self.assertLess(row[20 * s + 1, 0], 210)
        # far paper untouched, the stroke still dark but a little of the paper through it
        self.assertEqual(tuple(row[4 * s, :3]), (200, 120, 70))
        self.assertLess(row[19 * s, 0], 80); self.assertGreater(row[19 * s, 0], 30)

    def test_the_no_ink_maps_and_plain_pass_through(self):
        o, big = paper_with_stroke()
        for stem, writing in (("zmap6m00", "clean"), ("zmap6m00", "ink"), ("zmap6t01", "plain"), ("zmap6x99", "ink")):
            out, how = mm.finish(stem, big, o, writing)
            self.assertEqual(how, "plain"); self.assertTrue((out == big[..., :3]).all())

class WriteTests(unittest.TestCase):
    def test_a_png_written_and_a_webp_of_the_stem_removed(self):
        with tempfile.TemporaryDirectory() as d:
            open(os.path.join(d, "zmap6t01.webp"), "wb").close(); open(os.path.join(d, "zmap6t02.webp"), "wb").close()
            mm.write(d, "zmap6t01", np.zeros((8, 8, 3), np.uint8))
            self.assertEqual(sorted(os.listdir(d)), ["zmap6t01.png", "zmap6t02.webp"])
            self.assertEqual(Image.open(os.path.join(d, "zmap6t01.png")).size, (8, 8))

    def test_tile_is_capped(self):
        self.assertEqual(mm.MAX_TILE, 256)

if __name__ == "__main__":
    unittest.main()
