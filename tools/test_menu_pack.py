"""Tests of tools/menu-pack.py and tools/menu_lettering.py (python -m unittest tools/test_menu_pack.py). No game art and no model:
made-up pages, sprites and exports; the repository's Arimo stands in for Arial and for the install's Lee face."""
import importlib.util, io, json, os, shutil, sys, tempfile, unittest, zipfile
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_spec = importlib.util.spec_from_file_location("menu_pack", os.path.join(HERE, "menu-pack.py"))
mp = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mp)
try:
    import cv2
    import menu_lettering as ml
except ImportError:
    cv2 = ml = None
ARIMO = os.path.join(HERE, "text-redraws", "fonts", "Arimo", "Arimo[wght].ttf")
PAPER, INK = (239, 189, 90), (40, 30, 20)


def ppm(path, rgb):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f: f.write(f"P6 {rgb.shape[1]} {rgb.shape[0]} 255\n".encode() + rgb.tobytes())


def page_with_word(text="FORM", size=14):
    """A 160x60 page of paper with a word printed in dark ink at (20, 20), as a 1x original."""
    im = Image.new("RGB", (160, 60), PAPER)
    ImageDraw.Draw(im).text((20, 20), text, font=ImageFont.truetype(ARIMO, size), fill=INK)
    return np.array(im)


@unittest.skipIf(ml is None, "needs opencv-python-headless")
class LetteringTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.mkdtemp()
        for f in ("arial.ttf", "arialbd.ttf", "ariali.ttf"): shutil.copy(ARIMO, os.path.join(self.tmp, f))
        self.old = os.environ.get("I26_ARIAL_DIR"); os.environ["I26_ARIAL_DIR"] = self.tmp

    def tearDown(self):
        if self.old is None: os.environ.pop("I26_ARIAL_DIR", None)
        else: os.environ["I26_ARIAL_DIR"] = self.old
        shutil.rmtree(self.tmp, ignore_errors=True)

    def test_retype_sets_the_word_in_its_box(self):
        o = page_with_word()
        blurry = cv2.GaussianBlur(o.repeat(4, 0).repeat(4, 1), (0, 0), 3)      # a soft "upscale"
        out = ml.retype(blurry, o, {"runs": [{"text": "FORM", "rect": [15, 15, 80, 45]}]})
        ink1 = (o.mean(2) < 120); ys, xs = np.nonzero(ink1)
        dark = out.mean(2) < 120; Y, X = np.nonzero(dark)
        self.assertGreater(dark.sum(), 200)                                  # sharp dark letters are there
        self.assertAlmostEqual(X.min() / 4, xs.min(), delta=1.0)            # stretched onto the original's ink box
        self.assertAlmostEqual(X.max() / 4, xs.max() + 1, delta=1.0)
        self.assertAlmostEqual(Y.min() / 4, ys.min(), delta=1.0)
        far = out[:, 400:].astype(int) - blurry[:, 400:].astype(int)
        self.assertEqual(np.abs(far).max(), 0)                               # nothing changed away from the run

    def test_retype_reports_a_run_without_ink(self):
        o = page_with_word()
        out = ml.retype(o.repeat(4, 0).repeat(4, 1), o, {"runs": [{"text": "X", "rect": [100, 5, 150, 15]}]})
        self.assertEqual(out.shape, (240, 640, 3))

    def test_reset_fits_the_size_of_the_print(self):
        o = page_with_word("indicates", 12)
        pack = cv2.GaussianBlur(o.repeat(4, 0).repeat(4, 1), (0, 0), 3)
        out = ml.reset(pack, o, {"threshold": 30, "runs": [{"text": "indicates", "rect": [15, 15, 100, 45], "faces": ["regular"], "colour": [0, 0, 0]}]})
        ys, xs = np.nonzero(o.mean(2) < 120); Y, X = np.nonzero(out.mean(2) < 100)
        self.assertAlmostEqual((X.max() - X.min()) / 4, xs.max() - xs.min(), delta=1.5)
        self.assertAlmostEqual((Y.max() - Y.min()) / 4, ys.max() - ys.min(), delta=1.5)

    def test_ringed_mark_is_a_ring_round_its_letter(self):
        cov = ml.ringed(ARIMO, "C", 104, 88)
        self.assertEqual(cov.shape, (88, 104))
        self.assertGreater(cov[44, :6].max(), 128)        # the ring's left side
        self.assertGreater(cov[:6, 52].max(), 128)        # its top
        self.assertGreater(cov[30:58, 30:70].max(), 128)  # the letter inside
        self.assertLess(cov[44, 14:24].max(), 128)        # the gap between them

    def test_key_marks_replace_a_red_mark_with_a_black_ring(self):
        ex = tempfile.mkdtemp(dir=self.tmp); os.makedirs(os.path.join(ex, "glyphs"))
        json.dump({"record": 69, "height": 15, "glyphs": {b: {"advance": 14, "ink": [1, 3, 14, 14]} for b in "{|}"}}, open(os.path.join(ex, "glyphs", "font69.json"), "w"))
        shutil.copy(ARIMO, os.path.join(ex, "glyphs", "lee.ttf"))
        page = np.zeros((120, 160, 3), np.uint8); page[...] = PAPER
        page[44:84, 44:96] = (200, 40, 30)                                    # the printed red mark
        out = ml.marks(page, None, {"erase": "red", "marks": [[11, 11, 24, 21, "C"]]}, {"export": ex})
        r, g = out[..., 0].astype(int), out[..., 1].astype(int)
        self.assertEqual(((r - g) > 100).sum(), 0)                            # the red is gone
        self.assertGreater((out.mean(2) < 60).sum(), 200)                     # a black mark is there
        self.assertEqual(out[0:30].tolist(), page[0:30].tolist())             # and nothing above it changed

    def test_small_text_changes_only_the_small_print(self):
        o = page_with_word("abc", 10)
        cur = np.zeros((240, 640, 3), np.uint8); cur[...] = (100, 150, 200)
        out = ml.small_text(cur, np.dstack([o, np.full(o.shape[:2], 255, np.uint8)]), {}, {})
        self.assertTrue((out[:, 500:] == cur[:, 500:]).all())
        self.assertFalse((out[60:140, 60:180] == cur[60:140, 60:180]).all())

    def test_lineart_keeps_the_key_and_smooths_a_diagonal(self):
        o = np.zeros((24, 24, 3), np.uint8); o[...] = (255, 0, 255)
        o[4:20, 4:20] = (200, 190, 150)
        for i in range(14): o[5 + i, 5 + i] = (40, 30, 20)                   # a 1-texel diagonal line
        a = mp.key_alpha(o)
        model = np.dstack([o.repeat(4, 0).repeat(4, 1), a.repeat(4, 0).repeat(4, 1)])
        out = ml.lineart(model, np.dstack([o, a]), {}, {})
        self.assertEqual(out.shape, (96, 96, 4))
        self.assertEqual(out[0:8, 0:8, 3].max(), 0)                          # the key stays transparent
        d = out[..., :3].mean(2) < 100
        self.assertTrue(d[40, 36:48].any() and d[60, 56:68].any())          # the line is drawn along the diagonal


class PackTests(unittest.TestCase):
    def test_pictures_and_finish_from_an_export(self):
        tmp = tempfile.mkdtemp()
        try:
            ex = os.path.join(tmp, "export")
            ppm(os.path.join(ex, "bg", "bg01.ppm"), np.zeros((48, 64, 3), np.uint8))
            ppm(os.path.join(ex, "pad", "mainmn1.ppm"), np.zeros((40, 30, 3), np.uint8))
            spr = np.zeros((6, 5, 3), np.uint8); spr[0, 0] = (255, 0, 255)
            ppm(os.path.join(ex, "bank", "36", "06.ppm"), spr)
            ppm(os.path.join(ex, "load", "p01load.ppm"), np.zeros((48, 64, 3), np.uint8))
            pics = mp.pictures(ex)
            self.assertEqual(set(pics), {"shellbg1.webp", "6mainmn1.webp", "sprites/36_6.webp", "nitro/load/p01load.webp"})
            self.assertEqual(mp.original(ex, *pics["sprites/36_6.webp"])[0, 0, 3], 0)
            args = type("A", (), {"export": ex, "out": os.path.join(tmp, "out"), "work": None, "only": None, "recipe": mp.RECIPE})()
            b = mp.Build(args, json.load(open(mp.RECIPE, encoding="utf-8")))
            b.step_finish({})
            sj = json.load(open(os.path.join(b.pack, "sprites", "sprites.json")))
            self.assertEqual(sj["36_6"], {"file": "36_6.webp", "affine": [4.0, 0.0, 4.0, 0.0], "size": [5, 6]})
            self.assertEqual(json.load(open(os.path.join(b.pack, "shellbg.json")))["1"]["file"], "shellbg1.webp")
            self.assertIn("mainmn1", json.load(open(os.path.join(b.pack, "pads.json"))))
        finally: shutil.rmtree(tmp, ignore_errors=True)

    def test_nitro_build_makes_its_own_and_reuses_the_shared(self):
        tmp = tempfile.mkdtemp()
        try:
            base, nitro, bp = (os.path.join(tmp, d) for d in ("base", "nitro", "basepack"))
            rng = np.random.default_rng(2)
            same, pop, new = (rng.integers(0, 255, (6, 5, 3), np.uint8) for _ in range(3))
            ppm(os.path.join(base, "bank", "40", "00.ppm"), same); ppm(os.path.join(base, "bank", "36", "03.ppm"), pop)
            ppm(os.path.join(nitro, "bank", "34", "01.ppm"), same)     # the base game's picture at Nitro's numbers
            ppm(os.path.join(nitro, "bank", "34", "02.ppm"), new)      # Nitro's own
            ppm(os.path.join(nitro, "bank", "30", "04.ppm"), pop)      # a pop-up the game takes from the base pack itself
            for n in (1, 2): ppm(os.path.join(nitro, "bg", f"bg{n:02d}.ppm"), np.zeros((48, 64, 3), np.uint8))
            os.makedirs(os.path.join(bp, "sprites")); open(os.path.join(bp, "sprites", "40_0.webp"), "wb").write(b"base pack file")
            section = {"pages": [2], "leave_to_base": ["nitro/sprites/30_*"]}
            own, shared = mp.nitro_pictures(nitro, base, section)
            self.assertEqual(set(own), {"nitro/shellbg2.webp", "nitro/sprites/34_2.webp"})
            self.assertEqual(shared, {"nitro/sprites/34_1.webp": "sprites/40_0.webp"})
            recipe = json.load(open(mp.RECIPE, encoding="utf-8")); recipe["nitro"]["pages"] = [2]
            args = type("A", (), {"export": nitro, "base_export": base, "base_pack": bp, "nitro": True, "out": os.path.join(tmp, "out"),
                                  "work": None, "only": None, "recipe": mp.RECIPE})()
            b = mp.Build(args, recipe)
            self.assertEqual(b.step_reuse({}), 1)
            self.assertEqual(open(os.path.join(b.pack, "nitro", "sprites", "34_1.webp"), "rb").read(), b"base pack file")
            mp.save_webp(np.zeros((192, 256, 3), np.uint8), os.path.join(b.pack, "nitro", "shellbg2.webp"), {"lossless": True})
            b.step_finish({})
            sj = json.load(open(os.path.join(b.pack, "nitro", "sprites", "sprites.json")))
            self.assertEqual(sj, {"34_1": {"file": "34_1.webp", "affine": [4.0, 0.0, 4.0, 0.0], "size": [5, 6]}})
            self.assertEqual(json.load(open(os.path.join(b.pack, "nitro", "shellbg.json"))), {"2": {"file": "shellbg2.webp", "affine": [4.0, 0.0, 4.0, 0.0]}})
            self.assertFalse(os.path.exists(os.path.join(b.pack, "pack.json")))   # the base pack's own is left alone
        finally: shutil.rmtree(tmp, ignore_errors=True)

    def test_audio_pad_takes_the_options_pads_lower_rows(self):
        tmp = tempfile.mkdtemp()
        try:
            a = np.zeros((400, 8, 3), np.uint8); a[...] = 10; m = np.zeros((420, 8, 3), np.uint8); m[...] = 200
            ppm(os.path.join(tmp, "pad", "audcon1.ppm"), a); ppm(os.path.join(tmp, "pad", "mainmn1.ppm"), m)
            p = mp.pad_original(tmp, "audcon1")
            self.assertEqual(p[337, 0, 0], 10); self.assertEqual(p[338, 0, 0], 200); self.assertEqual(p.shape, a.shape)
        finally: shutil.rmtree(tmp, ignore_errors=True)

    def test_verify_classes(self):
        tmp = tempfile.mkdtemp()
        try:
            rng = np.random.default_rng(1); img = rng.integers(0, 255, (32, 32, 3), np.uint8)
            def enc(a):
                b = io.BytesIO(); Image.fromarray(a).save(b, "WEBP", lossless=True); return b.getvalue()
            near = img.copy(); near[0, 0] = near[0, 0] ^ 1
            self.assertAlmostEqual(mp.psnr(np.dstack([img, np.full((32, 32), 255)]).astype(float), np.dstack([near, np.full((32, 32), 255)]).astype(float))[0],
                                   10 * np.log10(255 * 255 / (3 / (32 * 32 * 3))), places=6)   # an opaque pair: colour only
            far = 255 - img
            files = {"same.webp": enc(img), "near.webp": enc(img), "far.webp": enc(img), "gone.webp": enc(img), "shellbg.json": b'{"1": 2}'}
            z = os.path.join(tmp, "r.zip")
            with zipfile.ZipFile(z, "w") as zf:
                for k, v in files.items(): zf.writestr("tex-menu/" + k, v)
            pk = os.path.join(tmp, "tex-menu"); os.makedirs(pk)
            for k, v in {"same.webp": enc(img), "near.webp": enc(near), "far.webp": enc(far), "new.webp": enc(img), "shellbg.json": b'{ "1":2 }'}.items():
                open(os.path.join(pk, k), "wb").write(v)
            rep = os.path.join(tmp, "rep.txt")
            args = type("A", (), {"pack": tmp, "against": z, "report": rep, "only": None, "worst": 5})()
            mp.verify(args)
            with open(rep, encoding="utf-8") as f:
                rows = dict(l.strip().split("\t")[:2] for l in f if l.startswith("  ") and "\t" in l)
            self.assertEqual(rows, {"same.webp": "identical", "near.webp": "same", "far.webp": "different", "gone.webp": "missing",
                                    "new.webp": "extra", "shellbg.json": "same-json"})
        finally: shutil.rmtree(tmp, ignore_errors=True)

    def test_recipe_is_complete(self):
        r = json.load(open(mp.RECIPE, encoding="utf-8"))
        kinds = {"base", "inset", "tool", "specs", "run", "maps", "finish"}
        self.assertTrue(all(s["kind"] in kinds for s in r["steps"]))
        self.assertEqual([s["id"] for s in r["steps"]][0], "base")
        for s in r["steps"]:
            for d in s.get("specs", []): self.assertTrue(os.path.isdir(os.path.join(HERE, d)), d)
            if s["kind"] == "run": self.assertTrue(os.path.exists(os.path.join(HERE, s["tool"])))
            self.assertIn(s.get("encoding", "lossy"), r["encodings"])
        for s in r["steps"]:
            if s["kind"] == "inset": self.assertIn(s["model"]["name"], r["models"])
        for m in r["models"].values(): self.assertEqual(len(m["sha256"]), 64); self.assertTrue(m["url"].startswith("https://"))
        n = r["nitro"]
        self.assertEqual([s["id"] for s in n["steps"]][0], "base")
        self.assertTrue(all(s["kind"] in kinds | {"reuse"} for s in n["steps"]))
        for s in n["steps"]:
            for d in s.get("specs", []): self.assertTrue(os.path.isdir(os.path.join(HERE, d)), d)
            self.assertTrue(all(f.startswith("nitro/") for f in s.get("files", [])), s["id"])
        self.assertEqual(n["export"]["env"], {"I76_NATIVE": "1"})

    def test_colour_mask_finds_the_stamp_and_its_openings(self):
        o = np.zeros((60, 80, 3), np.uint8); o[...] = (120, 60, 30)            # a photo
        o[10:40, 20:60] = (250, 200, 20)                                       # a yellow stamp
        o[20:30, 30:40] = (200, 80, 40)                                        # an opening showing the photo
        o[45:55, 5:15] = (250, 200, 20)                                        # a smaller yellow piece apart
        m = mp.colour_mask(o, {"box": [0, 0, 80, 60], "dark": 60, "yellow": [240, 195, 60], "blue": False, "white": 200, "grow": 1})
        self.assertTrue(m[25, 35])                                             # the opening is inside (holes filled)
        self.assertTrue(m[9, 30] and not m[5, 30])                             # grown by a texel, no further
        self.assertFalse(m[50, 10])                                            # only the largest region


if __name__ == "__main__":
    unittest.main()
