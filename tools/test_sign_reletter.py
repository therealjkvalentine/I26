"""Tests of tools/sign-reletter.py (python -m unittest tools/test_sign_reletter.py). No game art: a made-up sign in the layout's
places (tools/sign-reletter.py's synthetic()), the layout's own entries and plain strings. Needs numpy, scipy, Pillow and
Arial (Windows' Fonts folder or I26_ARIAL_DIR); without Arial the tests that set type are skipped."""
import importlib.util, json, os, sys, tempfile, unittest
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_spec = importlib.util.spec_from_file_location("sign_reletter", os.path.join(HERE, "sign-reletter.py"))
sr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(sr)

def have_arial():
    t = sr.tr.font_table()
    return all(sr.tr.font_path(t[f]) for f in ("Arial", "ArialRegular"))

S = 2


class LayoutTests(unittest.TestCase):
    def setUp(self): self.lay = sr.load_layout()

    def test_default_is_an_option_and_the_recipes_name_it(self):
        self.assertIn(self.lay["default_option"], self.lay["options"])
        for name in ("world-4x", "world-8x"):
            with open(os.path.join(HERE, "hd-recipes", name + ".json"), encoding="utf-8") as f: r = json.load(f)
            st = [s for s in r["steps"] if s.get("tool") == "sign-reletter.py"]
            self.assertEqual(len(st), 1, name)
            args = st[0]["args"]; self.assertIn(args[args.index("--option") + 1], self.lay["options"])
            self.assertEqual(set(st[0]["only"].split(",")), set(self.lay["signs"]))
            ids = [s["id"] for s in r["steps"]]
            self.assertLess(ids.index("lettering"), ids.index(st[0]["id"]))     # after the lettering step
            self.assertEqual(ids[-1], "finish")

    def test_every_line_is_measured_and_inside_the_texture(self):
        for stem, sign in self.lay["signs"].items():
            for ln in sign["lines"]:
                m = sign["measured"]["lines"][ln["id"]]
                x0, y0, x1, y1 = m["box"]
                self.assertTrue(0 <= x0 < x1 <= 128 and 0 <= y0 < y1 <= 64, (stem, ln["id"]))
                ex0, ey0, ex1, ey1 = ln.get("erase", ln["rect"])
                if ln.get("role") != "block":
                    # the erase rect holds the painted ink box (the upscale's soft edge round it too)
                    self.assertTrue(ex0 <= x0 + 0.5 and ey0 <= y0 + 0.5 and ex1 >= x1 - 0.5 and ey1 >= y1 - 0.5, (stem, ln["id"]))
                self.assertIn(ln["ground"], sign["ground_samples"])
                if "paint" in ln: self.assertIn(ln["paint"], self.lay["paints"])

    def test_wording_is_plain_typed_text(self):
        for k, o in self.lay["options"].items():
            for p in o["paragraphs"]:
                self.assertTrue(p.strip() and p == p.strip(), k)
                self.assertNotIn("  ", p)


class TypesetTests(unittest.TestCase):
    def setUp(self):
        if not have_arial(): self.skipTest("Arial not installed")
        self.lay = sr.load_layout()

    def test_wrap_keeps_every_word_and_the_width(self):
        t = "While on this installation all personnel and the property under their control are subject to search."
        lines = sr.wrap(t, "ArialRegular", 2.32, 44.6)
        self.assertEqual(" ".join(lines), t)
        for l in lines: self.assertLessEqual(sr.text_width(l, "ArialRegular", 2.32), 44.6)
        self.assertGreater(len(lines), 1)

    def test_no_break_space_holds_a_citation_together(self):
        t = "(Sec. 21, Internal Security Act of 1950; 50 U.S.C. 797)"
        for l in sr.wrap(t, "ArialRegular", 2.2, 30): self.assertFalse(l.startswith("U.S.C.") or l.endswith(" 50"))

    def test_every_option_fits_the_box(self):
        # each option's small print stays inside the box: within its width and above the bottom line's lettering
        for stem, sign in self.lay["signs"].items():
            b = sign["block"]; bottom = sign["measured"]["lines"]["deadly"]["box"][1]
            for k, o in self.lay["options"].items():
                lines, capr = sr.block_lines(sign, o)
                for t, f, em, x0, top in lines:
                    self.assertLessEqual(x0 + sr.text_width(t, f, em), b["x0"] + b["width"] + 1e-6, (stem, k, t))
                last = lines[-1]
                self.assertLess(last[4] + last[2] * capr + 0.22 * last[2], bottom, (stem, k))   # descenders clear
                self.assertGreater(lines[0][4], sign["measured"]["lines"]["sub"]["box"][3] - 0.3, (stem, k))

    def test_options_differ(self):
        sign = self.lay["signs"]["sw1mtr_1"]
        got = {k: tuple((l[0], round(l[2], 2), round(l[4], 2)) for l in sr.block_lines(sign, o)[0]) for k, o in self.lay["options"].items()}   # words or setting
        self.assertEqual(len(set(got.values())), len(got))

    def test_installation_line_follows_the_sign(self):
        a = sr.text_ops(self.lay["signs"]["sw1mtr_1"], self.lay["signs"]["sw1mtr_1"]["measured"], self.lay["options"]["A"], self.lay["paints"])
        b = sr.text_ops(self.lay["signs"]["sw1afb_1"], self.lay["signs"]["sw1afb_1"]["measured"], self.lay["options"]["A"], self.lay["paints"])
        self.assertIn("U.S. Army Installation", [o.get("text") for o in a])
        self.assertIn("U.S. Air Force Installation", [o.get("text") for o in b])


class RedrawTests(unittest.TestCase):
    def setUp(self):
        if not have_arial(): self.skipTest("Arial not installed")
        self.lay = sr.load_layout(); self.stem = "sw1mtr_1"
        self.o = sr.synthetic(self.lay, self.stem)
        self.up = np.repeat(np.repeat(self.o, S, 0), S, 1)
        self.meas = self.lay["signs"][self.stem]["measured"]

    def test_reproducible_and_local(self):
        a = sr.redraw(self.o, self.up, self.stem, self.lay, self.meas, "A")
        b = sr.redraw(self.o, self.up, self.stem, self.lay, self.meas, "A")
        self.assertTrue(np.array_equal(a, b))
        inside = np.zeros(self.up.shape[:2], bool)
        for ln in self.lay["signs"][self.stem]["lines"]:
            x0, y0, x1, y1 = ln.get("erase", ln["rect"])
            ys, xs = sr.rect_slices([x0 - 1, y0 - 1, x1 + 1, y1 + 1], S); inside[ys, xs] = True
        self.assertTrue(np.array_equal(a[~inside], self.up[~inside]))
        self.assertTrue(np.array_equal(a[..., 3], self.up[..., 3]))

    def test_the_option_changes_only_the_box(self):
        a = sr.redraw(self.o, self.up, self.stem, self.lay, self.meas, "A").astype(int)
        c = sr.redraw(self.o, self.up, self.stem, self.lay, self.meas, "C").astype(int)
        diff = np.abs(a - c).max(-1) > 0
        ys, xs = np.nonzero(diff)
        self.assertTrue(len(ys))
        sign = self.lay["signs"][self.stem]
        bx0, by0, bx1, by1 = [ln["erase"] for ln in sign["lines"] if ln["id"] == "block"][0]
        self.assertGreaterEqual(xs.min(), (bx0 - 1) * S); self.assertGreaterEqual(ys.min(), (by0 - 1) * S)

    def test_old_letters_are_gone_and_new_ones_set(self):
        # the made-up letters are vertical bars in each line's ink: after the redraw the board between the new letters is
        # the ground, and every text line has ink in its box
        a = sr.redraw(self.o, self.up, self.stem, self.lay, self.meas, "D").astype(float)
        sign = self.lay["signs"][self.stem]
        for ln in sign["lines"]:
            if ln.get("role") in ("erase", "block", "rule"): continue
            x0, y0, x1, y1 = self.meas["lines"][ln["id"]]["box"]
            ys, xs = sr.rect_slices([x0, y0, x1, y1], S)
            g = np.asarray(self.meas["grounds"][ln["ground"]], float)
            d = np.sqrt(((a[ys, xs, :3] - g) ** 2).sum(-1))
            self.assertGreater((d > 60).mean(), 0.08, ln["id"])

    def test_wear_is_light_and_seeded(self):
        H, W = 64 * S, 128 * S
        lettered = np.zeros((H, W, 3)); erased = np.full((H, W, 3), 250.0)
        w = self.lay["print"]["wear"]
        a = sr.wear("x", lettered, erased, S, w, 1976); b = sr.wear("x", lettered, erased, S, w, 1976)
        c = sr.wear("x", lettered, erased, S, w, 1977)
        self.assertTrue(np.array_equal(a, b)); self.assertFalse(np.array_equal(a, c))
        fade = a[..., 0] / 250.0
        self.assertLess(np.median(fade), 0.15)           # most of the ink stays
        self.assertLess((fade > 0.3).mean(), 0.03)        # few pin-holes
        self.assertTrue(np.array_equal(sr.wear("x", lettered, erased, S, None, 1976), lettered))


class PackStepTests(unittest.TestCase):
    def setUp(self):
        if not have_arial(): self.skipTest("Arial not installed")

    def test_pack_png_in_place_keeps_base_and_skips_missing(self):
        lay = sr.load_layout()
        with tempfile.TemporaryDirectory() as d:
            exp, pack = os.path.join(d, "export"), os.path.join(d, "pack")
            os.makedirs(exp); os.makedirs(pack)
            o = sr.synthetic(lay, "sw1mtr_1"); Image.fromarray(o).save(os.path.join(exp, "sw1mtr_1.png"))
            Image.fromarray(np.repeat(np.repeat(o, S, 0), S, 1)).save(os.path.join(pack, "sw1mtr_1.png"))
            with open(os.path.join(pack, "pack.json"), "w") as f: json.dump({"name": "test"}, f)
            files = sr.pack_step(exp, pack, None, lay, sr.LAYOUT, "B")
            self.assertEqual(files, ["sw1mtr_1.png"])                      # the Chennault sign is not in this pack: skipped
            self.assertTrue(os.path.exists(os.path.join(pack, sr.BASE, "sw1mtr_1.png")))
            first = np.array(Image.open(os.path.join(pack, "sw1mtr_1.png")))
            sr.pack_step(exp, pack, None, lay, sr.LAYOUT, "B")             # a rerun starts from the base: the same file
            self.assertTrue(np.array_equal(first, np.array(Image.open(os.path.join(pack, "sw1mtr_1.png")))))
            with open(os.path.join(pack, "pack.json")) as f: pj = json.load(f)
            self.assertEqual(pj["sign_reletter"]["option"], "B")
            self.assertEqual(sr.pack_step(exp, os.path.join(d, "empty-pack"), None, lay, sr.LAYOUT, "D"), [])


if __name__ == "__main__":
    unittest.main()
