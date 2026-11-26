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


class SpecTests(unittest.TestCase):
    def test_label_sets_name_known_groups_and_free_fonts(self):
        spec = rr.load_spec()
        groups = spec["marks"]["groups"]
        names = [ls["name"] for ls in spec["label_sets"]]
        self.assertIn("marks-only", names)
        self.assertEqual(len(names), len(set(names)))
        for ls in spec["label_sets"]:
            for t in ls.get("texts", []): self.assertIn(t["box"], groups)
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
