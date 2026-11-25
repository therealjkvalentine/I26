"""Tests of tools/draft-text-redraws.py (python -m pytest tools/test_draft_text_redraws.py). No game art: made-up
textures, lettered from the text redraws' own fonts."""
import csv, importlib.util, json, os, shutil, tempfile, unittest
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("draft", os.path.join(HERE, "draft-text-redraws.py"))
dr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(dr)
FONTS = os.path.join(HERE, "text-redraws", "fonts")
ANTON = os.path.join(FONTS, "Anton", "Anton-Regular.ttf")


def painted(text, size=(64, 32), px=12, at=(6, 8), ink=(20, 20, 20), ground=(240, 236, 230), font=ANTON, border=False):
    """A made-up sign: the word painted at the original's size (no anti-aliasing, as the 1997 art), opaque."""
    im = Image.new("RGB", size, ground); d = ImageDraw.Draw(im); d.fontmode = "1"
    d.text(at, text, font=ImageFont.truetype(font, px), fill=ink)
    if border:
        for i in range(5): d.point([(at[0] - 4 + i, at[1] - 3 + i), (at[0] - 3 + i, at[1] - 3 + i)], fill=ink)   # a border's corner by the word
    a = np.dstack([np.asarray(im), np.full(size[::-1], 255, np.uint8)])
    return a


def soft(orig, s=4):
    """A stand-in for the model's upscale: the original enlarged and blurred."""
    im = Image.fromarray(orig).resize((orig.shape[1] * s, orig.shape[0] * s), Image.BICUBIC)
    return np.asarray(im).copy()


class QueueTests(unittest.TestCase):
    def test_kinds_and_families(self):
        self.assertEqual(dr.kind_of("sh138e_1"), "sign"); self.assertEqual(dr.kind_of("ao_1sg_9"), "sign")
        self.assertEqual(dr.kind_of("cw_1dr11"), "sign"); self.assertEqual(dr.kind_of("cc12ftf3"), "car")
        self.assertEqual(dr.kind_of("zdash102"), "dash")
        # a car's dent stages and paints share a family; a sign is its own
        self.assertEqual(dr.family_of("cc11ftf1"), dr.family_of("cc13ftf4"))
        self.assertNotEqual(dr.family_of("cc11ftf1"), dr.family_of("cc11bkb1"))
        self.assertEqual(dr.family_of("sl155x_1"), "sl155x_1")

    def test_queue_orders_by_trip_missions_and_leaves_out_done_kept_and_skipped(self):
        survey = {"aa1sig_1": {"missions": "T01,T02", "ink_iou": "0.7"}, "bb1sig_1": {"missions": "M01,M02,M03", "ink_iou": "0.5"},
                  "cc1sig_1": {"missions": "T05", "ink_iou": "0.4"}, "dd1sig_1": {"missions": "T01,T02,T03", "ink_iou": "0.6"},
                  "ee1sig_1": {"missions": "T09", "ink_iou": "0.3"}, "ff1sig_1": {"missions": "T09", "ink_iou": "0.3"}}
        classes = {"aa1sig_1": {"cls": "SOFT"}, "bb1sig_1": {"cls": "WRONG"}, "cc1sig_1": {"cls": "SOFT"},
                   "dd1sig_1": {"cls": "GOOD"}, "ee1sig_1": {"cls": "SOFT"}, "ff1sig_1": {"cls": "SOFT"}}
        q = dr.build_queue(survey, classes, done={"ee1sig_1"}, skip=["ff*"])
        self.assertEqual([f for f, _ in q], ["aa1sig_1", "cc1sig_1", "bb1sig_1"])

    def test_batch_quota_takes_signs_first_and_some_of_each_kind(self):
        q = [(f"s{i}", {"kind": "sign", "rows": [1]}) for i in range(30)] + [("cars", {"kind": "car", "rows": [1] * 5}),
             ("dash", {"kind": "dash", "rows": [1]})]
        got = [f for f, _ in dr.pick_batch(q, 20)]
        self.assertEqual(got[:13], [f"s{i}" for i in range(13)])
        self.assertIn("cars", got); self.assertIn("dash", got)


class RowTests(unittest.TestCase):
    def test_a_heavy_word_is_dark_ink_and_its_box_is_the_word(self):
        # heavy letters close together: the light gaps between them must not be read as light letters
        o = painted("SPEED", size=(72, 32), px=16, at=(5, 6))
        rows = dr.rows_of_texture(o)
        self.assertTrue(rows)
        r = max(rows, key=lambda r: r["mask"].sum())
        self.assertEqual(r["polarity"], -1)
        ys, xs = np.where(o[..., 0] < 128)
        x0, y0, x1, y1 = r["box"]
        self.assertLessEqual(abs(x0 - xs.min()), 1); self.assertLessEqual(abs(x1 - 1 - xs.max()), 1)
        c = dr.row_colours(o, r)
        self.assertLess(max(c["ink"]), 60); self.assertGreater(min(c["ground"]), 200)

    def test_a_border_corner_above_the_word_is_left_out_of_its_box(self):
        o = painted("DEAD", size=(64, 40), px=18, at=(14, 12), border=True)
        r = max(dr.rows_of_texture(o), key=lambda r: r["mask"].sum())
        ys, xs = np.where(painted("DEAD", size=(64, 40), px=18, at=(14, 12))[..., 0] < 128)
        self.assertEqual(r["box"][1], int(ys.min()))                  # the word's top, not up at the border's corner
        self.assertEqual(r["box"][0], int(xs.min()))

    def test_an_added_row_is_tightened_to_its_ink_and_reads_its_ground_from_round_it(self):
        o = painted("217", size=(48, 24), px=8, at=(12, 6))
        ys, xs = np.where(o[..., 0] < 128)
        (_, r, text, fixed), = dr.added_rows({"add": [{"text": "217", "box": [int(xs.min()) - 2, int(ys.min()) - 1, int(xs.max()) + 3, int(ys.max()) + 2]}]}, o)
        self.assertEqual(text, "217"); self.assertEqual(r["polarity"], -1)
        self.assertEqual(fixed["box"], [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1])


class FitTests(unittest.TestCase):
    def test_the_classifier_finds_the_face_the_word_was_painted_in(self):
        table = dr.tr.font_table()
        o = painted("HOTEL", size=(80, 32), px=20, at=(6, 2))
        r = max(dr.rows_of_texture(o), key=lambda r: r["mask"].sum()); r["colours"] = dr.row_colours(o, r)
        cands = [("Anton", None), ("Cinzel", {"Weight": 700}), ("Comfortaa", {"Weight": 700}), ("AlfaSlabOne", None)]
        ft = dr.fit_row(o, r, "HOTEL", table, cands)
        self.assertEqual(ft["op"]["font"], "Anton")
        self.assertGreater(ft["fit"], 0.6)

    def test_a_fixed_face_is_kept(self):
        table = dr.tr.font_table()
        o = painted("HOTEL", size=(80, 32), px=20, at=(6, 2))
        r = max(dr.rows_of_texture(o), key=lambda r: r["mask"].sum()); r["colours"] = dr.row_colours(o, r)
        ft = dr.fit_row(o, r, "HOTEL", table, dr.faces(table), {"font": "Bitter", "variation": {"Weight": 700}})
        self.assertEqual(ft["op"]["font"], "Bitter")


class MeasureTests(unittest.TestCase):
    def setUp(self):
        self.o = painted("ROAD", size=(64, 32), px=14, at=(10, 6))
        r = max(dr.rows_of_texture(self.o), key=lambda r: r["mask"].sum()); r["colours"] = dr.row_colours(self.o, r)
        self.rows = [r]

    def test_blur_corr_ignores_crispness_but_not_a_moved_word(self):
        same = np.repeat(np.repeat(self.o, 4, 0), 4, 1)
        self.assertGreater(dr.blur_corr(self.o, same, self.rows), 0.99)
        self.assertGreater(dr.blur_corr(self.o, soft(self.o), self.rows), 0.95)
        moved = np.repeat(np.repeat(painted("ROAD", size=(64, 32), px=14, at=(14, 9)), 4, 0), 4, 1)
        self.assertLess(dr.blur_corr(self.o, moved, self.rows), 0.7)
        other = np.repeat(np.repeat(painted("LOUD", size=(64, 32), px=14, at=(10, 6)), 4, 0), 4, 1)
        self.assertLess(dr.blur_corr(self.o, other, self.rows), dr.blur_corr(self.o, same, self.rows) - 0.1)

    def test_outside_change_counts_only_texels_away_from_the_ops(self):
        base = soft(self.o); out = base.copy()
        ops = [{"op": "erase", "rect": [8, 4, 40, 24]}]
        out[30:40, 40:60] = 0                                          # inside the rect (texels 10..15, 7..10)
        self.assertEqual(dr.outside_change(base, out, ops, 4), 0)
        out[100:110, 200:220] = 0                                      # far from it
        self.assertGreater(dr.outside_change(base, out, ops, 4), 0)

    def test_verdict_passes_a_clean_redraw_and_names_what_fails(self):
        ok = {"scale": 4, "grid_new": {"corr": 0.9, "extent": 0.3, "drift": 8.0}, "outside": 0}
        self.assertEqual(dr.verdict([ok], [(0, 0.8)], 0.75, 0.45), ("PASS", []))
        bad = dict(ok, grid_new={"corr": 0.5, "extent": 2.5, "drift": 60.0}, outside=7)
        v, why = dr.verdict([bad], [(0, 0.3)], 0.75, 0.45)
        self.assertEqual(v, "FLAG"); self.assertEqual(len(why), 5)


class PipelineTests(unittest.TestCase):
    def test_gather_fit_sheet_accept_on_a_made_up_texture(self):
        tmp = tempfile.mkdtemp()
        try:
            src, pack, batch, specs = (os.path.join(tmp, d) for d in ("export", "pack", "batch", "specs"))
            for d in (src, pack, specs): os.makedirs(d)
            o = painted("MOTEL", size=(80, 32), px=16, at=(8, 6))
            Image.fromarray(o).save(os.path.join(src, "zz1mot_1.png"))
            Image.fromarray(soft(o)).save(os.path.join(pack, "zz1mot_1.png"))
            q = os.path.join(tmp, "queue.tsv")
            with open(q, "w", newline="", encoding="utf-8") as f:
                w = csv.writer(f, delimiter="\t"); w.writerow(["rank", "family", "stem", "kind", "cls", "ink_iou", "missions", "text"])
                w.writerow([1, "zz1mot_1", "zz1mot_1", "sign", "SOFT", 0.5, "T01", "MOTEL"])
            A = type("A", (), {})
            a = A(); a.queue, a.src, a.pack, a.batch, a.take, a.stems = q, src, [pack], batch, 30, None
            dr.cmd_gather(a)
            self.assertTrue(os.path.exists(os.path.join(batch, "read", "zz1mot_1.png")))
            with open(os.path.join(batch, "batch.json"), encoding="utf-8") as f: b = json.load(f)
            row = max(range(len(b["families"][0]["rows"])), key=lambda i: b["families"][0]["rows"][i]["marks"])
            with open(os.path.join(batch, "words.json"), "w", encoding="utf-8") as f:
                json.dump({"zz1mot_1": {"rows": {str(row): "MOTEL"}, "font": "Anton"}}, f)
            a = A(); a.batch, a.only, a.min_corr, a.min_fit = batch, None, 0.75, 0.45
            dr.cmd_fit(a)
            with open(os.path.join(batch, "results.json"), encoding="utf-8") as f: res = json.load(f)
            self.assertEqual(res[0]["verdict"], "PASS", res[0]["why"])
            a = A(); a.batch = batch; dr.cmd_sheet(a)
            self.assertTrue(os.path.exists(os.path.join(batch, "index.html")))
            dec = os.path.join(tmp, "decisions.json")
            with open(dec, "w", encoding="utf-8") as f: json.dump({"zz1mot_1": {"decision": "approve"}}, f)
            a = A(); a.batch, a.decisions, a.specs = batch, dec, specs; dr.cmd_accept(a)
            with open(os.path.join(specs, "signs-zz1mot_1.json"), encoding="utf-8") as f: spec = json.load(f)
            self.assertEqual(spec["textures"], ["zz1mot_1"])
            self.assertEqual([op["text"] for op in spec["ops"] if op["op"] == "text"], ["MOTEL"])
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
