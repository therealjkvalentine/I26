"""Tests of tools/hud-redraw.py and tools/panel_distress.py (python -m unittest tools/test_hud_redraw.py). No game art: a
made-up damage panel, lights sheet and housing strip in the original's layout, and the spec's own entries."""
import importlib.util, json, math, os, sys, tempfile, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_spec = importlib.util.spec_from_file_location("hud_redraw", os.path.join(HERE, "hud-redraw.py"))
hr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(hr)
import panel_distress as pd

S = 2


def up(a, s=S): return np.repeat(np.repeat(a, s, 0), s, 1)


def made_up_sheet(spec):
    """A lights sheet whose five states of every cell differ only in the lens colour (a dark frame, a lettered bar)."""
    sh = np.zeros((130, 305, 4), np.uint8); sh[..., 3] = 255
    lens = [(200, 200, 200), (90, 220, 90), (220, 220, 30), (220, 20, 50), (25, 25, 25)]
    for name, sp in spec["damage"]["sprites"].items():
        w, h = sp["size"]
        for st, (x, y) in enumerate(sp["cells"]):
            u = 1 if sp["cells"][0][1] > 0 else 0
            c = sh[y - u:y - u + h, x:x + w]
            c[..., :3] = (60, 60, 60); c[1:-1, 1:-1, :3] = lens[st]
            if sp.get("lettered") and w > 10: c[h // 2 - 2:h // 2 + 2, 4:w - 4, :3] = 14
    return sh


class DistressTests(unittest.TestCase):
    def setUp(self):
        self.spec = hr.load_spec()
        a = np.zeros((40, 60, 4), np.uint8); a[..., 3] = 255; a[..., :3] = (66, 72, 32)
        a[10:30, 10:50, :3] = (20, 20, 20); a[12:28, 12:48, :3] = (101, 101, 101)
        self.art = a

    def test_level_0_is_no_layer(self):
        self.assertIsNone(self.spec["distress"]["levels"]["0"])
        self.assertIsNone(pd.make_layer(up(self.art), S, None, 1, "x"))
        self.assertTrue(np.array_equal(pd.apply_layer(up(self.art), None, self.spec["distress"]["colours"]), up(self.art)))

    def test_reproducible_and_seeded(self):
        lv = hr.level_of(self.spec, "medium")
        a = pd.make_layer(up(self.art), S, lv, 1976, "unit"); b = pd.make_layer(up(self.art), S, lv, 1976, "unit")
        c = pd.make_layer(up(self.art), S, lv, 1977, "unit")
        for k in a: self.assertTrue(np.array_equal(a[k], b[k]), k)
        self.assertFalse(np.array_equal(a["grime"], c["grime"]))

    def test_the_same_pattern_at_4x_and_8x(self):
        # the random fields are drawn on one grid and brought to the pack's scale: an 8x layer averaged down is the 4x one
        lv = hr.level_of(self.spec, "light")
        n4 = pd.noise(pd.rng_for(5, "a"), 20, 30, 2.0, 4); n8 = pd.noise(pd.rng_for(5, "a"), 20, 30, 2.0, 8)
        down = n8.reshape(80, 2, 120, 2).mean((1, 3))
        self.assertGreater(np.corrcoef(down.ravel(), n4.ravel())[0, 1], 0.99)

    def test_light_is_subtle_and_medium_stronger(self):
        cols = self.spec["distress"]["colours"]; base = up(self.art, 4).astype(float)
        d = {}
        for name in ("light", "medium"):
            L = pd.make_layer(up(self.art, 4), 4, hr.level_of(self.spec, name), 1976, "unit")
            out = pd.apply_layer(up(self.art, 4), L, cols).astype(float)
            d[name] = np.abs(out - base)[..., :3].mean()
            self.assertTrue(np.array_equal(out[..., 3], base[..., 3]))              # the silhouette is never changed
        self.assertLess(d["light"], 6.0)                                             # a few levels on average: not grunge
        self.assertGreater(d["medium"], d["light"])

    def test_the_mask_holds(self):
        m = np.ones((80, 120)); m[20:60, 20:100] = 0
        L = pd.make_layer(up(self.art), S, hr.level_of(self.spec, "medium"), 1976, "unit", mask=m)
        out = pd.apply_layer(up(self.art), L, self.spec["distress"]["colours"])
        self.assertTrue(np.array_equal(out[20:60, 20:100], up(self.art)[20:60, 20:100]))


class DamageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.spec = hr.load_spec()
        cls.p, _, cls.hz = hr.synthetic(cls.spec)
        cls.sh = made_up_sheet(cls.spec)
        cls.lv = hr.level_of(cls.spec, "medium")
        cls.panel, cls.sheet = hr.redraw_damage(cls.p, up(cls.p), up(cls.sh), cls.spec, S, cls.lv)
        cls.wired = hr.redraw_wires(up(cls.p), cls.p, cls.spec["damage"]["wires"], S)

    def test_footprints_are_the_layouts(self):
        fp = {a: r for a, _, r, _ in hr.footprints(self.spec)}
        self.assertEqual(fp["engine"], (25, 34, 61, 12))         # the first row of the sheet: drawn where its label says
        self.assertEqual(fp["fltire"], (74, 8, 38, 12))          # the others a row above (CockpitPanels.RowsUp)
        self.assertEqual(hr.cell_rect(self.spec, "tire", 3), (183, 36, 38, 12))
        self.assertEqual(len(fp), 15)

    def test_the_stamps_places_on_the_panel_are_untouched(self):
        # the game stamps an opaque cell over each: the pack's texels kept exactly
        for _, _, (x, y, w, h), _ in hr.footprints(self.spec):
            sl = (slice(y * S, (y + h) * S), slice(x * S, (x + w) * S))
            self.assertTrue(np.array_equal(self.panel[sl], self.wired[sl]))

    def test_a_lights_states_carry_one_layer(self):
        # the made-up states differ only in the lens: the redraw's change to each state is the same where the state is the same
        for name, sp in self.spec["damage"]["sprites"].items():
            w, h = sp["size"]
            deltas = []
            for st in range(5):
                x, y, _, _ = hr.cell_rect(self.spec, name, st)
                a = self.sheet[y * S:(y + h) * S, x * S:(x + w) * S].astype(int); b = up(self.sh)[y * S:(y + h) * S, x * S:(x + w) * S].astype(int)
                deltas.append((a - b, b))
            frame = np.zeros((h * S, w * S), bool); frame[:S, :] = frame[-S:, :] = True; frame[:, :S] = frame[:, -S:] = True
            for st in range(1, 5):
                self.assertTrue(np.array_equal(deltas[st][0][frame], deltas[0][0][frame]), (name, st))

    def test_the_cells_continue_the_panels_wear(self):
        # the panel as the game first shows it (every light stamped) is the layer laid on that picture as a whole: the
        # lights and the panel round them carry one field, so no stamp's edge shows
        L, _ = hr.damage_layer(self.wired, up(self.sh), self.spec, S, self.lv)
        got = hr.composite(self.panel, self.sheet, self.spec, S).astype(int)
        want = pd.apply_layer(hr.composite(self.wired, up(self.sh), self.spec, S), L, self.spec["distress"]["colours"], s=S).astype(int)
        diff = np.abs(got - want)[..., :3].max(-1)
        self.assertLessEqual(int(diff[L["ink_cov"] == 0].max()), 1)

    def test_shared_sprites_at_their_other_places(self):
        L, _ = hr.damage_layer(self.wired, up(self.sh), self.spec, S, self.lv)
        fp = {a: r for a, _, r, _ in hr.footprints(self.spec)}
        for first, other in (("fltire", "rrtire"), ("fltire", "frtire"), ("larm", "rarm")):
            (x0, y0, w, h), (x1, y1, _, _) = fp[first], fp[other]
            for k in ("grime", "scr_l"):
                a = L[k][y0 * S:(y0 + h) * S, x0 * S:(x0 + w) * S]; b = L[k][y1 * S:(y1 + h) * S, x1 * S:(x1 + w) * S]
                self.assertTrue(np.allclose(a, b), (first, other, k))

    def test_level_0_changes_only_the_wires(self):
        a, b = hr.redraw_damage(self.p, up(self.p), up(self.sh), self.spec, S, None)
        self.assertTrue(np.array_equal(a, self.wired)); self.assertTrue(np.array_equal(b, up(self.sh)))


class WireTests(unittest.TestCase):
    def test_pieces_widths_and_order(self):
        spec = hr.load_spec(); wz = spec["damage"]["wires"]
        p = np.zeros((128, 256, 4), np.uint8)
        red, yel = wz["colours"]["red"], wz["colours"]["yellow"]
        # a red wire down x 4-5 and a yellow one crossing over it (the yellow painted last where they meet)
        for y in range(5, 60): p[y, 4] = (*red[0], 255); p[y, 5] = (*red[1], 255)
        for y in range(5, 60):
            x = 1 + int(round(10 * (y - 5) / 55)); p[y, x] = (*yel[0], 255); p[y, x + 1] = (*yel[1], 255)
        pieces = hr.measure_wires(p, wz)
        self.assertGreaterEqual(len(pieces), 2)
        for q in pieces: self.assertAlmostEqual(q["width"], 2.0, delta=0.6)
        self.assertEqual(pieces[-1]["colour"], "yellow")            # on top where they cross, as painted
        out = hr.redraw_wires(up(p, 4), p, wz, 4, pieces)
        # the red tube's middle row is red, its texels left and right of it clear
        row = out[30 * 4 + 2]
        self.assertGreater(int(row[4 * 4 + 4, 0]), 100); self.assertLess(int(row[4 * 4 + 4, 1]), 60)
        self.assertEqual(int(row[13 * 4, 3]), 0)


class HousingTests(unittest.TestCase):
    def test_stamped_places_untouched_and_one_layer(self):
        spec = hr.load_spec(); _, _, hz = hr.synthetic(spec)
        hz = hz.copy()
        for k in range(5): hz[k * 24 + 5:k * 24 + 15, 3 + k:12 + k, :3] = 200     # a screw slot that differs per housing
        u = up(hz); out = hr.redraw_housings(u, spec, S, hr.level_of(spec, "medium"))
        m = hr.housing_mask(spec, S) == 0
        deltas = []
        for x, y in spec["weapons"]["variants"]:
            a = out[y * S:(y + 24) * S, x * S:(x + 182) * S]; b = u[y * S:(y + 24) * S, x * S:(x + 182) * S]
            self.assertTrue(np.array_equal(a[m], b[m]))
            deltas.append(a.astype(int) - b.astype(int))
        body = np.ones((24 * S, 182 * S), bool); body[:, :20 * S] = False      # away from the screws
        for d in deltas[1:]: self.assertTrue(np.array_equal(d[body], deltas[0][body]))
        self.assertTrue(np.any(deltas[0] != 0))

    def test_stamped_rects_are_the_layouts(self):
        spec = hr.load_spec()
        # diode_n - bracket_n = (18, 4) 14x16; dymo_n (32, 1) 93x22; num_thous..ones x 127..154 at y 7, 8x10, a pixel left
        self.assertEqual(spec["weapons"]["stamped"], [[18, 4, 32, 20], [32, 1, 125, 23], [126, 7, 162, 17]])


class PackStepTests(unittest.TestCase):
    """The recipes' step on a made-up pack of .dds at 2x: its own format and rows, the same files again, --out, refusal."""

    def setUp(self):
        import dds_mips, etcpak
        from PIL import Image
        self.dds = dds_mips
        self.spec = hr.load_spec()
        self.tmp = tempfile.TemporaryDirectory(); d = self.tmp.name
        self.export = os.path.join(d, "export"); self.pack = os.path.join(d, "tex-world-2x")
        os.makedirs(self.export); os.makedirs(self.pack)
        p, _, hz = hr.synthetic(self.spec); sh = made_up_sheet(self.spec)
        rails = np.zeros((128, 256, 4), np.uint8); rails[2:120, 3:16] = (90, 90, 90, 255)
        arts = {"zsy_": p, "zsye": sh, "zwpe": hz, "zbk_": rails, "zbks_": rails}
        for st, a in arts.items():
            Image.fromarray(a).save(os.path.join(self.export, st + ".png"))
            dds_mips.write_dds(os.path.join(self.pack, st + ".dds"), np.ascontiguousarray(up(a)[::-1]), etcpak)
        with open(os.path.join(self.export, "bottom-up.txt"), "w") as f: f.write("\n".join(arts) + "\n")
        with open(os.path.join(self.pack, "pack.json"), "w", newline="") as f: f.write('{\r\n  "name": "t"\r\n}')

    def tearDown(self): self.tmp.cleanup()

    def files(self, d):
        return {f: hr.rr.sha256_of(os.path.join(d, f)) for f in sorted(os.listdir(d)) if os.path.isfile(os.path.join(d, f))}

    def test_in_place_again_the_same_and_named(self):
        before = self.files(self.pack)
        written, s = hr.pack_step(self.export, self.pack, None, self.spec, hr.SPEC, "light")
        self.assertEqual(s, 2); self.assertEqual(sorted(written), sorted(st + ".dds" for st in hr.stems(self.spec)))
        first = self.files(self.pack)
        self.assertNotEqual(first["zsy_.dds"], before["zsy_.dds"])
        self.assertEqual(hr.rr.sha256_of(os.path.join(self.pack, hr.BASE, "zsy_.dds")), before["zsy_.dds"])
        hr.pack_step(self.export, self.pack, None, self.spec, hr.SPEC, "light")
        self.assertEqual(self.files(self.pack), first)
        with open(os.path.join(self.pack, "pack.json"), encoding="utf-8", newline="") as f: raw = f.read()
        self.assertIn("\r\n", raw)
        meta = json.loads(raw)["hud_redraw"]
        self.assertEqual((meta["distress"], meta["scale"], meta["seed"]), ("light", 2, 1976))
        # stored bottom up: the panel's top row (as shown) is the file's last
        z = self.dds.read_levels(os.path.join(self.pack, "zsy_.dds"), upto=0)[0][0]
        self.assertEqual(z.shape, (256, 512, 4))

    def test_out_leaves_the_pack_and_a_redrawn_pack_needs_its_base(self):
        import shutil
        before = self.files(self.pack)
        out = os.path.join(self.tmp.name, "stage")
        backup = os.path.join(self.tmp.name, "backup"); shutil.copytree(self.pack, backup)
        written, _ = hr.pack_step(self.export, self.pack, out, self.spec, hr.SPEC, "medium")
        self.assertEqual(self.files(self.pack), before)
        self.assertFalse(os.path.exists(os.path.join(self.pack, hr.BASE)))
        ref = self.files(out)
        for f in written + ["pack.json"]: shutil.copy2(os.path.join(out, f), self.pack)
        with self.assertRaises(SystemExit): hr.pack_step(self.export, self.pack, os.path.join(self.tmp.name, "x"), self.spec, hr.SPEC, "medium")
        again = os.path.join(self.tmp.name, "again")
        hr.pack_step(self.export, self.pack, again, self.spec, hr.SPEC, "medium", base=backup)
        got = self.files(again)
        self.assertEqual({k: v for k, v in got.items() if k.endswith(".dds")}, {k: v for k, v in ref.items() if k.endswith(".dds")})


class RadarWearTests(unittest.TestCase):
    """The same wear offered for the radar (radar-redraw.py --distress): none by default; the same on every frame."""

    def test_radar_default_adds_none_and_the_layer_is_alike_on_every_frame(self):
        rr = hr.rr
        spec = rr.load_spec(); frames = rr.synthetic(spec); g, _ = rr.measure(frames, spec)
        k = 2; base = up(frames[spec["frames"]["sweep"] % 30], k)
        self.assertIsNone(rr.distress_of("0"))
        plain = rr.Redraw(g, spec, "B", None, k, base)
        worn = rr.Redraw(g, spec, "B", None, k, base, distress=rr.distress_of("medium"))
        f = lambda rd, i: rd.frame(up(frames[spec["frames"]["sweep"] % i], k), "sweep", i)
        d3 = f(worn, 3).astype(int) - f(plain, 3).astype(int); d17 = f(worn, 17).astype(int) - f(plain, 17).astype(int)
        self.assertTrue(np.any(d3 != 0))
        # outside the scope's face the change is the same on both frames (one layer); the face itself is never worn
        fc = g["face"]["centre"]; fr = g["face"]["radius"]
        H, W = d3.shape[:2]
        X = (np.arange(W) + 0.5)[None, :] / k; Y = (np.arange(H) + 0.5)[:, None] / k
        face = np.hypot(X - fc[0], Y - fc[1]) < fr - 0.5
        self.assertTrue(np.array_equal(d3[~face], d17[~face]))
        self.assertFalse(np.any(d3[face] != 0))


if __name__ == "__main__":
    unittest.main()
