"""Tests of tools/menu-rules.py (python -m unittest tools/test_menu_rules.py, or pytest tools/). No game art: a made-up form,
ochre paper ruled in ink with a word on it, and a made-up 4x "upscale" with the model's pale band along the rules."""
import importlib.util, os, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("menu_rules", os.path.join(HERE, "menu-rules.py"))
mr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mr)

PAPER = (239, 189, 90)
INK = (82, 66, 41)
HALO = (222, 190, 118)      # the band the model leaves: as bright as the paper but greyer


def form():
    """A 64x48 page: a table of three rows with a column rule, a stepper box, an underline, and a word of short strokes."""
    o = np.zeros((48, 64, 3), np.uint8); o[...] = PAPER
    for y in (8, 16, 24, 32):
        o[y, 4:60] = INK                    # rules 56 long
    o[8:33, 4] = INK; o[8:33, 59] = INK; o[8:33, 30] = INK
    o[36:46, 50:60] = INK; o[37:45, 51:59] = PAPER      # a 10x10 box
    o[46, 4:40] = INK                       # an underline
    # a word: an H and an I of 6 px stems (short strokes) at the foot
    o[38:44, 8] = INK; o[38:44, 12] = INK; o[41, 9:12] = INK; o[38:44, 16] = INK
    return o


def upscale(o):
    """The page at 4x with a two-texel pale band along every ink run, the ink itself a texel thinner and darker."""
    p = o.repeat(4, 0).repeat(4, 1).copy()
    ink = mr.lum(o) < mr.INK_LUM
    ink4 = ink.repeat(4, 0).repeat(4, 1)
    from scipy import ndimage
    band = ndimage.binary_dilation(ink4, iterations=2) & ~ink4
    p[band] = HALO
    p[ink4] = (60, 40, 10)
    return p


class RulesTests(unittest.TestCase):
    def test_rules_found_and_lettering_left(self):
        o = form()
        ink, rules = mr.rule_mask(o)
        self.assertTrue(rules[8, 4:60].all() and rules[8:33, 30].all())
        self.assertTrue(rules[36, 50:60].all(), "the stepper box")
        self.assertTrue(rules[46, 4:40].all(), "the underline")
        self.assertFalse(rules[38:44, 8].any() or rules[38:44, 16].any(), "the word's stems are not rules")

    def test_band_takes_the_papers_colour_and_the_rule_the_inks(self):
        o = form(); p = upscale(o)
        out, n = mr.clean(o, p, (4.0, 0.0, 4.0, 0.0), "chroma")
        self.assertGreater(n, 0)
        # above the first rule (shell row 7, its lower two texels) the band is paper again, within the WebP-free tolerance
        band = out[30:32, 40:200].reshape(-1, 3).astype(int)
        self.assertLess(np.abs(band - PAPER).max(), 6)
        # the rule's own texels are the original's ink, not the model's darker brown
        core = out[32:36, 40:200].reshape(-1, 3).astype(int)
        self.assertLess(np.abs(core - INK).max(), 6)
        # the word, and the halo round it, is as the model left it
        word = (slice(38 * 4 - 2, 43 * 4 + 2), slice(8 * 4 - 2, 17 * 4 + 2))   # short of the underline's band
        np.testing.assert_array_equal(out[word], p[word])
        # the paper away from the rules is untouched
        np.testing.assert_array_equal(out[48:60, 60:100], p[48:60, 60:100])

    def test_redraw_lays_the_rule_crisp(self):
        o = form(); p = upscale(o)
        out, _ = mr.clean(o, p, (4.0, 0.0, 4.0, 0.0), "redraw")
        self.assertLess(np.abs(out[32:36, 40:200].astype(int) - INK).max(), 6)
        self.assertLess(np.abs(out[28:31, 40:200].astype(int) - PAPER).max(), 6)

    def test_page_without_rules_is_left(self):
        o = np.zeros((48, 64, 3), np.uint8); o[...] = PAPER
        o[20:24, 10:14] = INK
        out, n = mr.clean(o, o.repeat(4, 0).repeat(4, 1), (4.0, 0.0, 4.0, 0.0), "chroma")
        self.assertIsNone(out); self.assertEqual(n, 0)


if __name__ == "__main__":
    unittest.main()
