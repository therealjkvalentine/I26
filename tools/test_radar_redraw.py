"""Tests of tools/radar-redraw.py (python -m unittest tools/test_radar_redraw.py). No game art: a made-up radar in the
original's layout, and the spec's own entries. No network (the label sets with words are not drawn here)."""
import importlib.util, math, os, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("radar_redraw", os.path.join(HERE, "radar-redraw.py"))
rr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(rr)


class GeometryTests(unittest.TestCase):
    def test_self_check(self):
        self.assertTrue(rr.self_check())

    def test_frame_angles_run_clockwise_in_the_game_view(self):
        # frame 0 is the car's front: +y in the file, up once the game flips the rows (RadarScope.LockedFrame: bearing 12f)
        self.assertEqual(rr.frame_angle(0), 90.0)
        # a quarter turn on (frame 7.5) is the car's right: +x in both views
        self.assertAlmostEqual(rr.frame_angle(7.5), 0.0)
        # the game shows rows flipped, so a file angle a is a (y-up) screen angle a: decreasing = clockwise
        self.assertLess(rr.frame_angle(1), rr.frame_angle(0))

    def test_circle_and_ring_fits(self):
        t = np.linspace(0, 2 * math.pi, 200, endpoint=False)
        X = 10 + 5 * np.cos(t); Y = -3 + 5 * np.sin(t)
        (cx, cy, r), rms = rr.fit_circle(X, Y)
        self.assertAlmostEqual(cx, 10, 6); self.assertAlmostEqual(cy, -3, 6); self.assertAlmostEqual(r, 5, 6)
        X2 = np.r_[X, 10 + 9 * np.cos(t)]; Y2 = np.r_[Y, -3 + 9 * np.sin(t)]
        c, radii, rms, n = rr.fit_rings(X2, Y2, (10.5, -2.5), [[3, 7], [7.5, 11]])
        self.assertAlmostEqual(c[0], 10, 4); self.assertAlmostEqual(radii[1], 9, 4)

    def test_coverage_is_area(self):
        cv = rr.Canvas(64, 64, 8)
        self.assertAlmostEqual(cv.disc((32, 32), 10).sum() / 64, math.pi * 100, delta=2)
        self.assertAlmostEqual(cv.seg((10, 20), (40, 20), 1.0, cap=False).sum() / 64, 30, delta=0.6)


class LineTests(unittest.TestCase):
    def test_grid_lines_are_thinner_with_soft_edges(self):
        spec = rr.load_spec()
        sc = spec["scope"]
        self.assertAlmostEqual(sc["line_width"], 0.7)            # round 1 drew 1.0
        self.assertAlmostEqual(spec["inset"]["line_width"], 0.7)
        self.assertGreater(sc["line_edge"], 1 / 8)                # wider than one texel at 8x: a soft edge
        cv = rr.Canvas(64, 64, 8)
        cov = cv.seg((10, 20.0), (40, 20.0), sc["line_width"], cap=False, edge=sc["line_edge"])
        col = cov[:, 25 * 8]
        self.assertAlmostEqual(col.sum() / 8, sc["line_width"], delta=0.02)   # the line's area is its width
        lit = col[col > 0]
        self.assertTrue(0 < lit[0] < 0.5 and 0 < lit[-1] < 0.5, lit)          # its outermost texels partly covered
        self.assertGreaterEqual((lit > 0.99).sum(), 3)                       # a solid core
        # the round-1 edge (one texel) is still what no edge gives
        hard = cv.seg((10, 20.0), (40, 20.0), 1.0, cap=False)[:, 25 * 8]
        self.assertEqual(int((hard > 0.99).sum()), 8)


def synthetic_sheets(frames, spec, d):
    """Made-up range plate and lamp sheets in the original's layout (not the game's art), written as an export would be:
    the dark plate and the green lamp are the frame's own pixels a row lower (as the original's art lies), the lit plate a
    bright disc, the lit lamp a red one."""
    gv = frames[spec["frames"]["sweep"] % 30][::-1]
    zr = np.zeros((72, 24, 4), np.uint8); zr[..., 3] = 255
    for y in (0, 24, 48): zr[y:y + 24] = gv[5:29, 139:163]
    yy, xx = np.mgrid[0:24, 0:24] + 0.5; r = np.hypot(xx - 11.5, yy - 11.5)
    lit = zr[48:72]; lit[r < 10.5] = (126, 243, 126, 255); lit[(r < 10.5) & (np.abs(xx - 11.5) < 0.6)] = (99, 189, 99, 255)
    zr[24:48][r < 4.5] = (126, 243, 126, 255)
    tg = np.zeros((22, 11, 4), np.uint8); tg[..., 3] = 255
    tg[0:11] = gv[84:95, 150:161]
    yy, xx = np.mgrid[0:11, 0:11] + 0.5; r = np.hypot(xx - 5.5, yy - 5.5)
    red = tg[0:11]; red[r < 4.2] = (209, 0, 41, 255); red[r < 1.5] = (250, 0, 50, 255)
    tg[12:22] = gv[85:95, 150:161]
    from PIL import Image
    Image.fromarray(zr).save(os.path.join(d, "zrge.png")); Image.fromarray(tg).save(os.path.join(d, "ztge.png"))


class StampTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        import tempfile
        cls.spec = rr.load_spec()
        cls.frames = rr.synthetic(cls.spec)
        cls.tmp = tempfile.TemporaryDirectory()
        synthetic_sheets(cls.frames, cls.spec, cls.tmp.name)
        cls.g, _ = rr.measure(cls.frames, cls.spec, cls.tmp.name)
        cls.s = 2
        cls.up = {k: np.repeat(np.repeat(v, cls.s, 0), cls.s, 1) for k, v in cls.frames.items()}
        cls.rd = rr.Redraw(cls.g, cls.spec, "A", {"name": "marks-only", "texts": [], "blob_groups": []}, cls.s,
                           cls.up[cls.spec["frames"]["sweep"] % 30])
        cls.sheets = cls.rd.sprites()

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_measures_where_the_art_lies(self):
        st = self.g["stamps"]
        self.assertEqual(st["plate"]["art_offset"], [0, 1])
        self.assertEqual(st["led"]["art_offset"], [0, 1])
        self.assertAlmostEqual(st["plate"]["frame_centre"][1], 16.5, delta=0.3)
        self.assertAlmostEqual(st["led"]["frame_centre"][0], 155.5, delta=0.3)
        self.assertAlmostEqual(st["led"]["frame_centre"][1], 89.5, delta=0.3)
        self.assertEqual(st["plate"]["lit"]["range_3"]["sprite_centre"], [11.5, 11.5])
        self.assertEqual(st["led"]["lit"]["radled_1"]["sprite_centre"], [5.5, 5.5])
        self.assertEqual(st["plate"]["lit"]["range_3"]["face"][0][1], [126, 243, 126])

    def stamped(self, frame, sprite):
        """The frame (file rows) in the game's view with a sprite stamped as the game's Blit does: keyed at alpha 128."""
        s = self.s; out = frame[::-1].copy()
        for key, part in self.spec["stamps"]["parts"].items():
            if sprite not in part["sprites"]: continue
            sh = self.sheets[part["sheet"]]; y = part["sprites"][sprite]; w, h = part["size"]; ax, ay = part["anchor"]
            src = sh[y * s:(y + h) * s, :w * s]; dst = out[ay * s:ay * s + src.shape[0], ax * s:(ax + w) * s]
            keep = src[..., 3] >= 128; dst[keep] = src[keep]
        return out

    def test_sheets_keep_the_layout_and_key_outside_the_disc(self):
        s = self.s
        self.assertEqual(self.sheets["zrge"].shape, (72 * s, 24 * s, 4))
        self.assertEqual(self.sheets["ztge"].shape, (22 * s, 11 * s, 4))
        for name, sh in self.sheets.items():
            self.assertEqual(sh[0, 0, 3], 0, name)                 # the corners keyed: the frame shows
        self.assertEqual(self.sheets["zrge"][12 * s, 11 * s, 3], 255)
        # rows as shown, as the export: range_1 (100 m, dark) on top, range_3 (600 m, lit) at the foot
        self.assertLess(int(self.sheets["zrge"][12 * s, 7 * s, 1]), 150)
        self.assertGreater(int(self.sheets["zrge"][(48 + 12) * s, 7 * s, 1]), 200)

    def test_the_dark_stamps_change_no_texel_of_any_frame(self):
        # the seam: wherever the game stamps the dark plate or the green lamp, the frame is left exactly as it was
        for kind, i in (("sweep", 0), ("sweep", 4), ("locked", 26), ("sweep", 30)):
            f = self.rd.frame(self.up[self.spec["frames"][kind] % i], kind, i)
            for sprite in ("range_1", "radled_2"):
                self.assertTrue(np.array_equal(self.stamped(f, sprite), f[::-1]), (kind, i, sprite))

    def test_the_lit_stamps_change_only_inside_their_face(self):
        f = self.rd.frame(self.up[self.spec["frames"]["sweep"] % 7], "sweep", 7)
        st = self.g["stamps"]; s = self.s
        for sprite, key in (("range_3", "plate"), ("radled_1", "led")):
            d = np.abs(self.stamped(f, sprite).astype(int) - f[::-1]).sum(-1) > 0
            ys, xs = np.nonzero(d)
            c = st[key]["frame_centre"]; r = np.hypot((xs + 0.5) / s - c[0], (ys + 0.5) / s - c[1])
            part = self.spec["stamps"]["parts"][key]
            self.assertLessEqual(r.max(), part["lit_max_radius"] + part["edge"] + 0.5, sprite)
        lit = self.stamped(f, "range_3")[int(16.5 * s), int(146 * s), :3]
        self.assertGreater(int(lit[1]), 200)
        red = self.stamped(f, "radled_1")[int(89.5 * s), int(155.5 * s), :3]
        self.assertGreater(int(red[0]), 200)

    def test_style_b_cone_covers_the_grid(self):
        # round 1 laid style B's cone under the graticule: the grid's dark lines showed through it
        k = 4
        base = np.repeat(np.repeat(self.frames[self.spec["frames"]["sweep"] % 30], k, 0), k, 1)
        rd = rr.Redraw(self.g, self.spec, "B", None, k, base)
        f = rd.frame(np.repeat(np.repeat(self.frames[self.spec["frames"]["locked"] % 0], k, 0), k, 1), "locked", 0)
        gc = self.g["grid"]["centre"]; pv = self.g["sweep"]["pivot"]
        # frame 0's cone points along +y in the file: the vertical spoke runs down its axis
        px = f[int((pv[1] + 20) * k), int(gc[0] * k), :3].astype(int)
        self.assertGreater(px[1], 140, px)


class SpecTests(unittest.TestCase):
    def test_lead_label_set_letters_the_game_ranges(self):
        spec = rr.load_spec()
        lead = spec["label_sets"][0]
        self.assertEqual(lead["name"], "lock-range")
        self.assertEqual([t["text"] for t in lead["texts"]], ["LOCK VOL", "100", "TGT", "600", "RANGE"])
        # the three marks over the lower knob, left to right: the left tick, the X, the right tick
        self.assertEqual([t["mark"] for t in lead["texts"] if "mark" in t], [["ticks", 0], ["ticks", 1], ["ticks", 2]])

    def test_label_sets_name_known_groups_and_free_fonts(self):
        spec = rr.load_spec()
        groups = spec["marks"]["groups"]
        names = [ls["name"] for ls in spec["label_sets"]]
        self.assertIn("marks-only", names)
        self.assertEqual(len(names), len(set(names)))
        for ls in spec["label_sets"]:
            for t in ls.get("texts", []):
                self.assertTrue(t.get("box") in groups or t.get("mark", [None])[0] in groups, t)
            for g in ls.get("blob_groups", []): self.assertIn(g, groups)
            self.assertIn(ls.get("font", spec["default_font"]), spec["fonts"])
        for name, f in spec["fonts"].items():
            self.assertIn("OFL", f["license"], name)
            self.assertTrue("url" in f or os.path.exists(os.path.join(rr.REPO_FONTS, f["repo"])), name)

    def test_no_font_files_beside_the_spec(self):
        d = os.path.join(HERE, "radar-redraw")
        self.assertFalse([f for f in os.listdir(d) if f.lower().endswith((".ttf", ".otf", ".png"))])


if __name__ == "__main__":
    unittest.main()
