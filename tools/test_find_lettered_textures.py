"""Tests of tools/find-lettered-textures.py (python -m pytest tools/test_find_lettered_textures.py). No game art: made-up
textures, lettered from the text redraws' own fonts."""
import importlib.util, os, unittest
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("find_lettered", os.path.join(HERE, "find-lettered-textures.py"))
fl = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(fl)
FONT = os.path.join(HERE, "text-redraws", "fonts", "Anton", "Anton-Regular.ttf")


def lettered(text, size=(128, 64), px=14, ink=(20, 20, 20), ground=(235, 230, 220), at=(8, 20), turn=False):
    im = Image.new("RGBA", size, ground + (255,)); d = ImageDraw.Draw(im)
    d.text(at, text, font=ImageFont.truetype(FONT, px), fill=ink + (255,))
    if turn: im = im.rotate(90, expand=True)
    return np.asarray(im, np.float32)


class DetectTests(unittest.TestCase):
    def test_a_painted_word_is_a_row_of_letters(self):
        im = lettered("ROADSIDE")
        score, boxes, setting = fl.detect(fl.luminance(im))
        self.assertGreaterEqual(score, 5)
        ys, xs = np.where(im[..., 0] < 128)                             # the word's own ink
        x0, y0, x1, y1 = boxes[0]
        self.assertLessEqual(abs(x0 - xs.min()), 1); self.assertLessEqual(abs(x1 - 1 - xs.max()), 1)   # the row spans the word
        self.assertLessEqual(abs(y0 - ys.min()), 1); self.assertLessEqual(abs(y1 - 1 - ys.max()), 1)
        self.assertEqual(setting["polarity"], -1)                    # dark ink on a light ground

    def test_light_lettering_on_dark_and_a_word_laid_sideways(self):
        self.assertGreaterEqual(fl.detect(fl.luminance(lettered("MOTEL", ink=(240, 240, 240), ground=(30, 30, 60))))[0], 4)
        score, _, setting = fl.detect(fl.luminance(lettered("SHERIFF", turn=True)))
        self.assertGreaterEqual(score, 5); self.assertTrue(setting["turned"])

    def test_plain_and_noisy_surfaces_have_no_rows(self):
        flat = np.full((64, 128, 4), 128, np.float32)
        self.assertEqual(fl.detect(fl.luminance(flat))[0], 0)
        rng = np.random.default_rng(1)
        noise = flat.copy(); noise[..., :3] += rng.normal(0, 10, (64, 128, 1))
        self.assertLess(fl.detect(fl.luminance(noise))[0], 3)

    def test_a_regular_row_counts_half(self):
        im = np.full((64, 128, 4), 220, np.float32)
        for k in range(8): im[20:30, 8 + k * 14:16 + k * 14, :3] = 30      # a row of windows: one size, one fill
        self.assertLessEqual(fl.detect(fl.luminance(im))[0], 4)

    def test_keyed_texels_are_no_ink(self):
        im = np.full((64, 128, 4), 200, np.float32); im[:, :40, 3] = 0; im[:, :40, :3] = 0
        self.assertEqual(fl.detect(fl.luminance(im))[0], 0)
        self.assertIsNone(fl.luminance(np.zeros((8, 8, 4), np.float32)))


class CompareTests(unittest.TestCase):
    def test_the_same_lettering_upscaled_overlaps_and_keeps_its_weight(self):
        o = lettered("ROADSIDE")
        score, boxes, setting = fl.detect(fl.luminance(o))
        p = np.asarray(Image.fromarray(o.astype(np.uint8)).resize((512, 256), Image.NEAREST), np.float32)
        m = fl.compare(o, p, boxes, setting)
        self.assertGreater(m["ink_iou"], 0.9); self.assertAlmostEqual(m["stroke"], 1.0, delta=0.15); self.assertLess(m["drift"], 5)
        self.assertEqual(fl.class_hint(dict(m, ocr_sim="")), "GOOD")

    def test_lettering_melted_away_is_wrong(self):
        o = lettered("ROADSIDE")
        score, boxes, setting = fl.detect(fl.luminance(o))
        p = np.full((256, 512, 4), 255, np.float32); p[..., :3] = (235, 230, 220)
        p[80:140, 40:300, :3] = 20                                           # one blot where the word was
        m = fl.compare(o, p, boxes, setting)
        self.assertEqual(fl.class_hint(dict(m, ocr_sim="")), "WRONG")

    def test_a_reading_kept(self):
        self.assertEqual(fl.kept("54 mi. Ahead", "54 MI AHEAD"), 1.0)
        self.assertLess(fl.kept("54 mi. Ahead", "S4 mi Ahood"), 0.85)
        self.assertEqual(fl.kept("", "anything"), "")


if __name__ == "__main__":
    unittest.main()
