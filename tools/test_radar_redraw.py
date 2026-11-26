"""Tests of tools/radar-redraw.py (python -m unittest tools/test_radar_redraw.py). No game art: a made-up radar in the
original's layout, and the spec's own entries. No network: the labels' font is in tools/text-redraws/fonts/."""
import importlib.util, math, os, sys, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
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
    def test_grid_lines_are_crisp(self):
        # round 2-3 drew a 0.25 px ramp; the owner asked for crisp lines with about a texel of anti-aliasing
        spec = rr.load_spec()
        sc = spec["scope"]
        self.assertAlmostEqual(sc["line_width"], 0.7)
        self.assertAlmostEqual(spec["inset"]["line_width"], 0.7)
        self.assertIsNone(sc.get("line_edge")); self.assertIsNone(spec["inset"].get("line_edge"))
        for s in (8, 4):
            cv = rr.Canvas(64, 64, s)
            col = cv.seg((10, 20.0), (40, 20.0), sc["line_width"], cap=False, edge=sc["line_edge"])[:, 25 * s]
            self.assertAlmostEqual(col.sum() / s, sc["line_width"], delta=0.02)        # the line's area is its width
            self.assertLessEqual(int(((col > 0) & (col < 1)).sum()), 2, (s, col[col > 0]))   # one partial texel a side


class GratOverTests(unittest.TestCase):
    """Style B: the afterglow, the beam and the lock cone under the graticule, the graticule printed over them."""

    @classmethod
    def setUpClass(cls):
        cls.spec = rr.load_spec(); cls.frames = rr.synthetic(cls.spec)
        cls.g, _ = rr.measure(cls.frames, cls.spec)
        cls.k = 4
        base = np.repeat(np.repeat(cls.frames[cls.spec["frames"]["sweep"] % 30], cls.k, 0), cls.k, 1)
        cls.rd = rr.Redraw(cls.g, cls.spec, "B", None, cls.k, base)

    def frame(self, kind, i):
        return self.rd.frame(np.repeat(np.repeat(self.frames[self.spec["frames"][kind] % i], self.k, 0), self.k, 1), kind, i)

    def test_the_grid_lies_over_the_cone(self):
        k = self.k; f = self.frame("locked", 0)
        gc = self.g["grid"]["centre"]; pv = self.g["sweep"]["pivot"]
        # frame 0's cone points along +y in the file: the vertical spoke runs down its axis, dark over it
        px = f[int((pv[1] + 20) * k), int(gc[0] * k), :3].astype(int)
        self.assertLess(px[1], 60, px)
        beside = f[int((pv[1] + 20) * k), int((gc[0] + 1.0) * k), :3].astype(int)
        self.assertGreater(beside[1], 140, beside)

    def test_the_grid_lies_over_the_beam_and_the_beam_is_thin(self):
        k = self.k; f = self.frame("sweep", 3)
        pv = np.array(self.g["sweep"]["pivot"]); a = math.radians(rr.frame_angle(3) + self.g["sweep"]["offset_deg"])
        u = np.array([math.cos(a), math.sin(a)]); n = np.array([-u[1], u[0]])
        gc = np.array(self.g["grid"]["centre"])
        R = self.g["grid"]["rings"][1]["radius"]
        on_ring = pv + u * ((gc - pv) @ u + math.sqrt(R * R - ((gc - pv) @ n) ** 2))
        self.assertLess(int(f[int(on_ring[1] * k), int(on_ring[0] * k), 1]), 80)      # the ring crosses the beam, dark
        p = pv + u * 20
        self.assertGreater(int(f[int(p[1] * k), int(p[0] * k), 1]), 200)              # the beam's core
        b = self.spec["styles"]["B"]["beam"]
        self.assertLessEqual(b["width"], 1.6); self.assertNotIn("glow_width", b)        # thin, no halo round it
        # across the beam: bright for little more than its width, the afterglow (one side only) dimmer
        prof = [int(f[int(q[1] * k), int(q[0] * k), 1]) for q in (p + n * t for t in np.arange(-3, 3.01, 0.25))]
        self.assertLessEqual(sum(v > 200 for v in prof), int(b["width"] / 0.25) + 1, prof)


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


try:
    import etcpak
except ImportError:
    etcpak = None


@unittest.skipIf(etcpak is None, "etcpak not installed")
class PackStepTests(unittest.TestCase):
    """The recipes' step on a made-up pack of .dds at 2x: in place, its own format and row order, the same files again on a
    second run, the font named; and --out leaves the pack alone."""

    def setUp(self):
        import tempfile, dds_mips
        from PIL import Image
        self.dds = dds_mips
        self.spec = rr.load_spec(); self.s = 2
        self.tmp = tempfile.TemporaryDirectory(); d = self.tmp.name
        self.export = os.path.join(d, "export"); self.pack = os.path.join(d, "tex-world-2x")
        os.makedirs(self.export); os.makedirs(self.pack)
        frames = rr.synthetic(self.spec)
        for stem, a in frames.items(): Image.fromarray(a).save(os.path.join(self.export, stem + ".png"))
        synthetic_sheets(frames, self.spec, self.export)
        with open(os.path.join(self.export, "bottom-up.txt"), "w") as f: f.write("zradmask\nzrge\nztge\n")
        for stem, a in frames.items():
            dds_mips.write_dds(os.path.join(self.pack, stem + ".dds"), np.repeat(np.repeat(a, self.s, 0), self.s, 1), etcpak)
        with open(os.path.join(self.pack, "pack.json"), "w", newline="") as f:
            f.write('{\r\n  "name": "t",\r\n  "attribution": "m by x | lettering redrawn in Anton by y, SIL OFL 1.1, z",\r\n'
                    '  "text_fonts": [\r\n    {"font": "Anton", "by": "y", "license": "SIL OFL 1.1", "source": "z"}\r\n  ]\r\n}')
        with open(os.path.join(self.pack, "ATTRIBUTION.txt"), "w", newline="") as f:
            f.write("m by x\nlettering redrawn in Anton by y, SIL OFL 1.1, z\n")

    def tearDown(self):
        self.tmp.cleanup()

    def files(self, d):
        return {f: rr.sha256_of(os.path.join(d, f)) for f in sorted(os.listdir(d)) if os.path.isfile(os.path.join(d, f))}

    def run_step(self, out=None):
        return rr.pack_step(self.export, self.pack, out, self.spec, rr.SPEC, "B", "lock-range")

    def test_in_place_in_the_packs_format_and_again_the_same(self):
        before = self.files(self.pack)
        written, s = self.run_step()
        self.assertEqual(s, 2)
        self.assertEqual(len(written), 67)                                            # 62 frames, 2 sheets, the sweep's 3
        self.assertTrue(all(f.endswith(".dds") for f in written if f.startswith(("zradf", "zradb", "zrge", "ztge"))))
        self.assertEqual(sorted(f for f in written if f.startswith("zrads")), ["zrads.json", "zrads_face.dds", "zrads_grid.png"])
        self.assertEqual(len(os.listdir(os.path.join(self.pack, rr.BASE))), 62)      # the frames it started from
        for stem in ("zradf000", "zradb017"):
            self.assertEqual(rr.sha256_of(os.path.join(self.pack, rr.BASE, stem + ".dds")), before[stem + ".dds"])
        first = self.files(self.pack)
        self.assertNotEqual(first["zradf007.dds"], before["zradf007.dds"])
        self.run_step()
        self.assertEqual(self.files(self.pack), first)                               # from .radar-base/: the same files
        # the sheets stored bottom up as bottom-up.txt says: the lit 600 m plate (range_3, the sheet's foot as shown) first
        z = self.dds.read_levels(os.path.join(self.pack, "zrge.dds"), upto=0)[0][0]
        self.assertEqual(z.shape, (72 * s, 24 * s, 4))
        self.assertGreater(int(z[12 * s, 7 * s, 1]), 200)
        self.assertLess(int(z[(48 + 12) * s, 7 * s, 1]), 150)
        self.assertEqual(int(z[0, 0, 3]), 0)
        # a frame stays in the export's rows (not in bottom-up.txt): the corner scope low in the file, as the export has it
        f = self.dds.read_levels(os.path.join(self.pack, "zradf000.dds"), upto=0)[0][0]
        self.assertEqual(f.shape, (128 * s, 256 * s, 4))

    def test_names_the_redraw_and_its_font_once(self):
        import json
        self.run_step(); self.run_step()
        with open(os.path.join(self.pack, "pack.json"), encoding="utf-8", newline="") as f: raw = f.read()
        self.assertIn("\r\n", raw)
        meta = json.loads(raw)
        self.assertEqual(meta["radar_redraw"]["style"], "B")
        self.assertEqual(meta["radar_redraw"]["labels"], "lock-range")
        self.assertEqual([f["font"] for f in meta["text_fonts"]], ["Anton", "B612"])
        self.assertEqual(meta["attribution"].count("lettering redrawn in B612 "), 1)
        with open(os.path.join(self.pack, "ATTRIBUTION.txt"), encoding="utf-8") as f: at = f.read().splitlines()
        self.assertEqual([l for l in at if "B612" in l], [at[2]])
        self.assertTrue(at[2].startswith("lettering redrawn in B612 by "))

    def test_a_redrawn_pack_needs_the_upscales_frames(self):
        # the owner's packs carry a redraw already (pack.json names it) without .radar-base: starting from those frames would
        # redraw the redraw; the step refuses, and --base starts from the upscale's own frames
        import shutil
        out = os.path.join(self.tmp.name, "stage")
        backup = os.path.join(self.tmp.name, "backup"); os.makedirs(backup)
        for f in os.listdir(self.pack): shutil.copy2(os.path.join(self.pack, f), backup)
        first, _ = self.run_step(out)
        ref = self.files(out)
        for f in first + ["pack.json"]: shutil.copy2(os.path.join(out, f), self.pack)   # installed, as the live packs are
        with self.assertRaises(SystemExit): self.run_step(os.path.join(self.tmp.name, "again"))
        again = os.path.join(self.tmp.name, "again2")
        rr.pack_step(self.export, self.pack, again, self.spec, rr.SPEC, "B", "lock-range", base=backup)
        got = self.files(again)
        self.assertEqual({k: v for k, v in got.items() if k.endswith(".dds")}, {k: v for k, v in ref.items() if k.endswith(".dds")})

    def test_out_leaves_the_pack_as_it_was(self):
        before = self.files(self.pack)
        out = os.path.join(self.tmp.name, "stage")
        written, _ = self.run_step(out)
        self.assertEqual(self.files(self.pack), before)
        self.assertFalse(os.path.exists(os.path.join(self.pack, rr.BASE)))
        self.assertEqual(sorted(os.listdir(out)), sorted(written + ["ATTRIBUTION.txt", "pack.json"]))


class SmoothSweepTests(unittest.TestCase):
    """The live sweep's layers and numbers (Radar Sweep: Smooth): drawn from them alone at a step's angle, the scope is the stepped
    frame of that step; the angle law is the frames' own."""

    @classmethod
    def setUpClass(cls):
        cls.spec = rr.load_spec(); cls.frames = rr.synthetic(cls.spec)
        cls.g, _ = rr.measure(cls.frames, cls.spec)
        cls.k = 4
        cls.rd = rr.Redraw(cls.g, cls.spec, "B", None, cls.k, cls.up(cls.spec["frames"]["sweep"] % 30))
        cls.layers = cls.rd.layers(); cls.p = cls.rd.sweep_params()
        cls.face = cls.layers[rr.SWEEP_LAYERS["face"]]; cls.grid = cls.layers[rr.SWEEP_LAYERS["grid"]]

    @classmethod
    def up(cls, stem):
        return np.repeat(np.repeat(cls.frames[stem], cls.k, 0), cls.k, 1)

    def off_rim(self):
        """Texels not on the face circle's one anti-aliased texel (where the layers' order leaves a level or two)."""
        H, W = self.face.shape[:2]; k = self.k
        Y, X = np.mgrid[0:H, 0:W]
        r = np.hypot((X + 0.5) / k - self.p["face"]["centre"][0], (Y + 0.5) / k - self.p["face"]["centre"][1])
        return np.abs(r - self.p["face"]["radius"]) > 0.3

    def test_the_layers_at_a_steps_angle_are_its_stepped_frame(self):
        keep = self.off_rim()
        for kind in ("sweep", "locked"):
            for f in (0, 4, 11, 17, 29):
                stepped = self.rd.frame(self.up(self.spec["frames"][kind] % f), kind, f)
                angle = rr.sweep_file_angle(self.p, f) if kind == "sweep" else rr.frame_angle(f)
                smooth = rr.compose_smooth(self.face, self.grid, self.p, angle, locked=kind == "locked")
                d = np.abs(stepped.astype(int) - smooth.astype(int)).max(-1)
                self.assertLessEqual(int(d[keep].max()), 2, (kind, f))

    def test_between_steps_the_beam_lies_between_them(self):
        k = self.k; pv = np.array(self.p["pivot"])
        a = self.p["sweep"]["step_deg"]
        for f in (2, 13):
            ang = rr.sweep_file_angle(self.p, f + 0.5)
            self.assertAlmostEqual(ang, rr.sweep_file_angle(self.p, f) - a / 2, places=9)
            sm = rr.compose_smooth(self.face, self.grid, self.p, ang)
            u = np.array([math.cos(math.radians(ang)), math.sin(math.radians(ang))]); q = pv + u * 30
            self.assertGreater(int(sm[int(q[1] * k), int(q[0] * k), 1]), 200)

    def test_the_numbers(self):
        p = self.p; sw = p["sweep"]
        self.assertEqual((sw["frames"], sw["step_deg"], sw["step_s"], sw["deg_per_s"]), (30, 12.0, 0.2, 60.0))
        self.assertEqual(sw["offset_deg"], self.g["sweep"]["offset_deg"])
        for f in range(30): self.assertAlmostEqual(rr.sweep_file_angle(p, f), rr.frame_angle(f) + self.g["sweep"]["offset_deg"])
        x0, y0, x1, y1 = p["layers"]["grid_box"]
        self.assertEqual(self.grid.shape[:2], (y1 - y0, x1 - x0))
        self.assertEqual(self.face.shape[:2], (128 * self.k, 256 * self.k))
        self.assertEqual(p["size"], [256 * self.k, 128 * self.k])
        self.assertEqual(p["afterglow"]["decay_s"], round(32 / 60, 4))
        self.assertEqual(p["layers"]["off"], "zradf030")

    def test_the_face_has_no_beam_grid_or_pip(self):
        x0, y0, x1, y1 = self.p["layers"]["grid_box"]; k = self.k
        cx, cy = self.p["face"]["centre"]; r = self.p["face"]["radius"] - 1
        H, W = self.face.shape[:2]; Y, X = np.mgrid[0:H, 0:W]
        inside = np.hypot((X + 0.5) / k - cx, (Y + 0.5) / k - cy) < r
        self.assertTrue((self.face[inside][:, :3] == np.array(self.p["face"]["colour"])).all())
        self.assertGreater(int(self.grid[..., 3].max()), 200)                        # the graticule is on its own layer

    def test_style_a_writes_no_layers(self):
        rd = rr.Redraw(self.g, self.spec, "A", None, self.k, self.up(self.spec["frames"]["sweep"] % 30))
        self.assertFalse(rd.has_layers())


class SpecTests(unittest.TestCase):
    # the lower knob's marks as measured on the original (game view): the X, the left tick, the right tick; the knobs
    MARKS = {"groups": {"ticks": {"blobs": [{"centre": [28.333, 60.167]}, {"centre": [21.5, 62.5]}, {"centre": [37.5, 63.5]}]},
                        "between": {"ink_box": [15, 45, 40, 48]}, "under_lower": {"ink_box": [27, 90, 39, 91]}},
             "knobs": [{"centre": [29.266, 31.022], "radius": 7.355}, {"centre": [29.963, 75.954], "radius": 6.461}]}

    def ink(self, spec, t):
        box = rr.text_box(t, self.MARKS, spec["marks"])
        if t.get("pad"): box = [box[0] - t["pad"][0], box[1] - t["pad"][1], box[2] + t["pad"][0], box[3] + t["pad"][1]]
        cv = rr.Canvas(256, 128, 8, (12, 40, 48, 96))
        cov = rr.text_coverage(cv, t["text"], rr.font_file(spec["default_font"], spec), box, cap=t["cap"])
        ys, xs = np.nonzero(cov > 0.05)
        return 12 + xs.min() / 8, 40 + ys.min() / 8, 12 + (xs.max() + 1) / 8, 40 + (ys.max() + 1) / 8

    def test_lock_range_labels_one_size_in_the_marks_places(self):
        spec = rr.load_spec()
        ls = spec["label_sets"][0]; lead = {t["text"]: t for t in ls["texts"]}
        self.assertEqual(len({t["cap"] for t in ls["texts"]}), 1)                     # every word one cap height
        self.assertGreater(lead["100"]["cap"], 1.8)                                   # larger than round 3's numbers
        self.assertNotIn("ticks", ls["tick_groups"])                                  # the painted ticks and X not drawn
        self.assertIn("ticks", spec["marks"]["erase_groups"])                         # but erased
        boxes = {w: self.ink(spec, lead[w]) for w in lead}
        # the blobs are sorted left to right by text_box: 100 on the left tick, TGT on the X, 600 on the right tick
        for w, c in (("100", (21.5, 62.5)), ("TGT", (28.333, 60.167)), ("600", (37.5, 63.5))):
            b = boxes[w]
            self.assertLess(abs((b[0] + b[2]) / 2 - c[0]), 1.0, w)                   # where its mark was
            self.assertLess(abs((b[1] + b[3]) / 2 - c[1]), 1.0, w)
        # clear of the bezel (its outer edge at x 41.0 on these rows), of each other and of the knob's rim
        self.assertLessEqual(boxes["600"][2], 41.0 - 1.0)
        self.assertLessEqual(boxes["100"][2] + 0.8, boxes["TGT"][0])
        self.assertLessEqual(boxes["TGT"][2] + 0.8, boxes["600"][0])
        k = self.MARKS["knobs"][1]; rim = k["radius"] + spec["marks"]["rim_width"]
        for w in ("100", "TGT", "600", "RANGE"):
            b = boxes[w]; q = (min(max(k["centre"][0], b[0]), b[2]), min(max(k["centre"][1], b[1]), b[3]))
            self.assertGreater(math.hypot(q[0] - k["centre"][0], q[1] - k["centre"][1]) - rim, 2.0, w)

    def test_ink_is_printed_darker(self):
        # the panel's shade darkened (printed, the panel showing through), darker than round 3's flat (30, 30, 15)
        spec = rr.load_spec(); m = spec["marks"]["ink_multiply"]
        self.assertTrue(0.2 <= m <= 0.4, m)
        self.assertLess(max(rr.OLIVE) * m, 30)
        frames = rr.synthetic(spec); g, _ = rr.measure(frames, spec)
        base = np.repeat(np.repeat(frames[spec["frames"]["sweep"] % 30][::-1], 4, 0), 4, 1)
        ls = {"name": "t", "texts": [{"text": "H", "cap": 4.0, "at": [26, 48, 34, 54]}], "tick_groups": []}
        patch, (x0, y0, x1, y1), _ = rr.panel_patch(g, spec, ls, base, 4)
        p = patch[(51 - y0) * 4 - 2:(51 - y0) * 4 + 2, (26 - x0) * 4:(34 - x0) * 4]
        dark = p.reshape(-1, 3)[p.sum(-1).reshape(-1).argmin()]
        self.assertTrue(np.allclose(dark, np.array(rr.OLIVE) * m, atol=4), dark)

    def test_the_font_is_in_the_repository(self):
        spec = rr.load_spec()
        f = spec["fonts"][spec["default_font"]]
        self.assertIn("repo", f)
        self.assertTrue(os.path.exists(os.path.join(rr.REPO_FONTS, os.path.dirname(f["repo"]), "OFL.txt")))


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



class EdgeTests(unittest.TestCase):
    """The edges: a ring's band keeps its profile with the silhouette's steps gone; a wire's centreline and width."""

    def test_ring_band_keeps_the_profile_and_smooths_the_silhouette(self):
        # a pixel-art ring (as the original's bezel): grey bands about a circle, transparent outside it
        H, W = 128, 128; c = (64.3, 63.8); R = 40.0
        yy, xx = np.mgrid[0:H, 0:W] + 0.5; r = np.hypot(xx - c[0], yy - c[1])
        gm = np.zeros((H, W, 4), np.uint8); gm[r < R + 3] = (66, 72, 32, 255)
        gm[(r >= R) & (r < R + 1)] = (50, 50, 50, 255); gm[(r >= R + 1) & (r < R + 2)] = (136, 136, 136, 255)
        gm[(r >= R + 2) & (r < R + 3)] = (16, 16, 16, 255)
        band = rr.Band(rr.curve_circle(c, R), gm, (-0.4, 4.0), 8.0, 0.12)
        cv = rr.Canvas(W, H, 8, (0, 0, W, H))
        rgb, al, cov = rr.band_layer(band, cv)
        t, d = rr.curve_coords(band.cu, cv.X, cv.Y)
        mid = np.abs(d - 1.5) < 0.2; out = d > 3.4; inn = (d > -0.3) & (d < 2.6)
        self.assertGreater(rgb[mid][:, 0].mean(), 120)                     # the light band where it was
        self.assertLess(al[out].max(), 0.05)                                 # transparent past the ring, all round
        self.assertGreater(al[inn].min(), 0.95)
        # the silhouette is a circle: the alpha edge's radius the same all round (the pixel steps gone)
        edge = (al > 0.3) & (al < 0.7)
        X = np.broadcast_to(cv.X, al.shape); Y = np.broadcast_to(cv.Y, al.shape)
        re = np.hypot(X[edge] - c[0], Y[edge] - c[1])
        self.assertLess(re.std(), 0.12, re.std())
        self.assertAlmostEqual(float(re.mean()), R + 3, delta=0.25)

    def test_wire_centreline_and_width(self):
        # a 2-px wire along a sine, drawn in pixels: its fitted centreline lies on the curve, its width 2
        H, W = 64, 120
        yy, xx = np.mgrid[0:H, 0:W] + 0.5
        f = lambda x: 30 + 8 * np.sin(x / 15)
        m = (np.abs(yy - f(xx)) < 1.0 / np.cos(np.arctan(8 / 15 * np.cos(xx / 15)))) & (xx > 10) & (xx < 110)
        w = rr.measure_wire(m)
        P = np.array(w["points"])
        inner = (P[:, 0] > 15) & (P[:, 0] < 105)
        self.assertLess(np.abs(P[inner, 1] - f(P[inner, 0])).max(), 0.3)
        self.assertAlmostEqual(w["width"], 2.0, delta=0.25)
        cu = rr.curve_open(P); cv = rr.Canvas(W, H, 4)
        cov, t, d = rr.tube_coverage(cu, cv, w["width"], ext=0.0)
        self.assertAlmostEqual(cov.sum() / 16 / cu["length"], w["width"], delta=0.2)

    def test_layers_without_the_original_do_nothing(self):
        spec = rr.load_spec(); frames = rr.synthetic(spec); g, _ = rr.measure(frames, spec)
        self.assertEqual(rr.edge_layers(g, spec, None, 2), [])


if __name__ == "__main__":
    unittest.main()
