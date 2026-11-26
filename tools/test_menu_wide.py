"""Tests of tools/menu-wide.py (python -m unittest tools/test_menu_wide.py). No game art and no model: made-up pages, a made-up
"model output" cut from the same made-up scene, and the upscaler replaced by plain 4x pixel repetition."""
import importlib.util, io, json, os, shutil, sys, tempfile, unittest, zipfile
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("menu_wide", os.path.join(HERE, "menu-wide.py"))
mw = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mw)
try:
    import cv2, scipy  # noqa: F401
except ImportError:
    cv2 = None

W, H = 2560, 1920


def scene(w, h, seed=1):
    """A smooth made-up picture with structure everywhere (blobs of colour and some lines), w x h."""
    rng = np.random.default_rng(seed)
    small = rng.integers(0, 255, (h // 32 + 2, w // 32 + 2, 3)).astype(np.uint8)
    a = np.array(Image.fromarray(small).resize((w, h), Image.BICUBIC)).astype(np.int16)
    for k in range(12):
        x = int(rng.integers(0, w)); a[:, max(0, x - 2):x + 2] -= 60
        y = int(rng.integers(0, h)); a[max(0, y - 2):y + 2, :] += 40
    return np.clip(a, 0, 255).astype(np.uint8)


@unittest.skipIf(cv2 is None, "needs opencv-python-headless and scipy")
class Geometry(unittest.TestCase):
    def test_the_canvas_is_the_models_own_ratio_with_the_page_in_the_middle(self):
        g = mw.canvas_geom((W, H), 3414, mw.MODEL_OUT[("gemini-3-pro-image", "16:9", "2K")])
        self.assertEqual((g["cw"], g["ch"], g["x0"], g["y0"], g["iw"], g["ih"]), (2064, 1152, 264, 0, 1536, 1152))
        # 21:9 at 2K is narrower than 3440x1440: the inner is made smaller, with bands above and below
        g = mw.canvas_geom((3414, H), 4588, mw.MODEL_OUT[("gemini-3-pro-image", "21:9", "2K")])
        self.assertLess(g["ih"], g["ch"]); self.assertGreater(g["y0"], 0)
        self.assertGreaterEqual((g["cw"] - g["iw"]) / 2 * H / g["ih"], 587)

    def test_side_regions_are_whole_native_texels_with_the_context(self):
        r = {"upscale": {"context": 65}}
        self.assertEqual(mw.region_width(r, 427), 492)
        self.assertEqual(mw.region_width(r, 587), 652)
        self.assertEqual(mw.region_width(r, 293), 360)

    def test_layers_and_their_sides(self):
        r = {"height": H, "layers": {"21x9": {"width": 4588, "over": "16x9"}, "16x9": {"width": 3414, "over": "4x3"}}}
        self.assertEqual(mw.layer_info(r, "16x9"), (3414, H, 427, "4x3"))
        self.assertEqual(mw.layer_info(r, "21x9"), (4588, H, 587, "16x9"))
        self.assertEqual(mw.layer_order(r, ["21x9"]), ["16x9", "21x9"])

    def test_alignment_finds_the_page_in_a_model_output_to_a_fraction_of_a_pixel(self):
        g = mw.canvas_geom((W, H), 3414, mw.MODEL_OUT[("gemini-3-pro-image", "16:9", "2K")])
        world = scene(g["cw"] * 2, g["ch"] * 2)                       # the scene at twice the canvas
        page = np.array(Image.fromarray(world[:, 2 * g["x0"]:2 * (g["x0"] + g["iw"])]).resize((W, H), Image.LANCZOS))
        out = np.array(Image.fromarray(world).resize((2752, 1536), Image.LANCZOS))   # the "model" returns the whole scene, 2K
        A, info = mw.align(out, page, g)
        self.assertGreater(info["ncc"], 0.95)
        M = mw.to_frame(A, g, 427, H)
        # the output's pixel that should land on the page's top-left corner and bottom-right corner
        for ox, oy, fx, fy in ((2752 * g["x0"] / g["cw"], 0, 427, 0), (2752 * (g["x0"] + g["iw"]) / g["cw"], 1536, 427 + W, H)):
            px, py = M @ np.array([ox, oy, 1.0])
            self.assertAlmostEqual(px, fx, delta=1.5); self.assertAlmostEqual(py, fy, delta=1.5)


@unittest.skipIf(cv2 is None, "needs opencv-python-headless and scipy")
class Seam(unittest.TestCase):
    def test_the_membrane_meets_the_edge_and_keeps_the_sides_own_detail_away_from_it(self):
        h, w = 256, 120
        rng = np.random.default_rng(3)
        side = np.clip(128 + rng.normal(0, 12, (h, w, 3)), 0, 255).astype(np.uint8)
        d = np.zeros((h, 3)); d[:, 0] = 30; d[: h // 2, 1] = -20          # a step at the join (low and higher frequency)
        out = mw.membrane(side, d, "R")                                    # the join is column 0 of a right side
        self.assertLess(np.abs(out[:, 0].astype(float) - side[:, 0] - d).mean(), 2.0)
        hf = lambda a: a.astype(float) - cv2.GaussianBlur(a.astype(float), (0, 0), 2)
        self.assertLess(np.abs(hf(out[:, -40:]) - hf(side[:, -40:])).mean(), 0.6)   # the side's grain kept

    def test_join_keeps_the_inner_page_and_pad_repeats_the_edge(self):
        inner = scene(64, 32)
        L, R = mw.pad_sides(inner, 10, 4)
        full = mw.join(inner, L, R)
        self.assertTrue(np.array_equal(full[:, 10:74], inner))
        self.assertTrue(np.array_equal(full[:, 0], np.round(inner[:, :4].astype(np.float32).mean(1)).astype(np.uint8)))
        with self.assertRaises(AssertionError):
            bad = np.concatenate([L, inner, R], 1); bad[0, 20] ^= 1; assert np.array_equal(bad[:, 10:74], inner), "the inner page changed"

    def test_the_fingerprint_tells_another_page(self):
        a = scene(W, H, 5); b = scene(W, H, 6)
        fp = mw.fingerprint(a, 4, 16)
        self.assertTrue(mw.fingerprint_ok(fp, a, 4, 16, 8))
        self.assertTrue(mw.fingerprint_ok(fp, np.clip(a.astype(int) + 3, 0, 255).astype(np.uint8), 4, 16, 8))
        self.assertFalse(mw.fingerprint_ok(fp, b, 4, 16, 8))


@unittest.skipIf(cv2 is None, "needs opencv-python-headless and scipy")
class Build(unittest.TestCase):
    """A whole build from a made-up 4:3 pack and master: a model page, a page that borrows its sides, a pad page."""
    def setUp(self):
        self.tmp = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_build_and_verify(self):
        g = mw.canvas_geom((W, H), 3414, mw.MODEL_OUT[("gemini-3-pro-image", "16:9", "2K")])
        world = scene(g["cw"] * 2, g["ch"] * 2, 9)
        page = np.array(Image.fromarray(world[:, 2 * g["x0"]:2 * (g["x0"] + g["iw"])]).resize((W, H), Image.LANCZOS))
        borrower = page.copy(); borrower[:, 600:2000] = 30                 # the same art at the edges, other middle
        black = np.zeros((H, W, 3), np.uint8); black[800:900, 1000:1500] = 200
        zp = os.path.join(self.tmp, "i76-hd-menus-test.zip")
        with zipfile.ZipFile(zp, "w") as z:
            for name, a in (("shellbg1", page), ("shellbg2", borrower), ("shellbg26", black)):
                b = io.BytesIO(); Image.fromarray(a).save(b, "WEBP", lossless=True); z.writestr(f"tex-menu/{name}.webp", b.getvalue())
            z.writestr("tex-menu/pack.json", json.dumps({"name": "Test 4:3", "version": "1"}))
        masters = os.path.join(self.tmp, "masters"); os.makedirs(os.path.join(masters, "shellbg1", "16x9"))
        mp = os.path.join(masters, "shellbg1", "16x9", "m.png")
        Image.fromarray(np.array(Image.fromarray(world).resize((2752, 1536), Image.LANCZOS))).save(mp)
        recipe = json.load(open(mw.RECIPE, encoding="utf-8"))
        recipe["layers"] = {"16x9": recipe["layers"]["16x9"]}
        recipe["pages"] = {
            "shellbg1": {"class": "model", "layers": {"16x9": {"method": "model", "model": "gemini-3-pro-image", "aspect": "16:9", "size": "2K",
                         "canvas": "mirror", "prompt": "-", "pick": "test", "master": "shellbg1/16x9/m.png", "master_sha256": mw.sha256_file(mp)}}},
            "shellbg2": {"class": "borrow", "from": "shellbg1"},
            "shellbg17": {"class": "skip", "why": "-"},
            "shellbg26": {"class": "pad"},
        }
        rp = os.path.join(self.tmp, "menu-wide.json"); json.dump(recipe, open(rp, "w"))
        real = mw.Upscaler.run
        mw.Upscaler.run = lambda self, regions: [np.repeat(np.repeat(r, 4, 0), 4, 1) for r in regions]
        try:
            out = os.path.join(self.tmp, "out")
            args = type("A", (), dict(recipe=rp, pack43=[zp], masters=masters, out=out, layers="16x9", only=None, full=None, work=None,
                                       models_dir=self.tmp, device="cpu"))
            mw.build(args)
        finally:
            mw.Upscaler.run = real
        man = json.load(open(os.path.join(out, "tex-menu-wide", "wide.json")))
        self.assertEqual(man["layers"]["16x9"], {"width": 3414, "side": 427, "over": "4x3"})
        self.assertEqual(man["pages"]["1"]["16x9"], "16x9/shellbg1.webp")
        self.assertIn("16x9", man["pages"]["2"])
        self.assertTrue(man["pages"]["26"]["pad"])
        self.assertNotIn("17", man["pages"])
        s = Image.open(os.path.join(out, "tex-menu-wide", "16x9", "shellbg1.webp"))
        self.assertEqual(s.size, (2 * 427, H))
        info = json.load(open(os.path.join(out, "tex-menu-wide", "pack.json")))
        self.assertEqual(info["over"]["name"], "Test 4:3")
        args = type("A", (), dict(recipe=rp, pack43=[zp], dir=out, full=None))
        self.assertEqual(mw.verify(args), 0)
        # the sides are the scene the made-up model saw, put back where it belongs (alignment, flow, membrane): close to the
        # truth next to the joins and on the whole, far from another scene's
        wp = mw.WidePack(out)
        full = wp.compose("shellbg1", page, "16x9").astype(float)
        K = H / g["ch"]
        truth = np.array(Image.fromarray(world).resize((int(round(g["cw"] * K)), H), Image.LANCZOS)).astype(float)
        x = int(round(g["x0"] * K)) - 427
        truth = truth[:, x:x + 3414]
        sides = np.s_[:, list(range(0, 427)) + list(range(2987, 3414))]
        self.assertLess(np.abs(full[sides] - truth[sides]).mean(), 8.0)
        for c in (np.s_[:, 400:427], np.s_[:, 2987:3014]):
            self.assertLess(np.abs(full[c] - truth[c]).mean(), 8.0)
        other = np.array(Image.fromarray(scene(3414, H, 77))).astype(float)
        self.assertGreater(np.abs(other[sides] - truth[sides]).mean(), 20.0)
        # another 4:3 pack's page is not widened
        self.assertIsNone(wp.compose("shellbg1", scene(W, H, 99), "16x9"))


if __name__ == "__main__":
    unittest.main()
