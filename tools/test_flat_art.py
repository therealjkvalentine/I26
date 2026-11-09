"""Tests of tools/flat-art.py (python -m unittest tools/test_flat_art.py, or pytest tools/). No game art: a made-up stamp
of four flat colours, drawn anti-aliased at 1x by supersampling, as the originals are."""
import importlib.util, os, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("flat_art", os.path.join(HERE, "flat-art.py"))
fa = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(fa)

COLS = ("#000000", "#0042ce", "#ffbd00", "#ffffff")
BLACK, BLUE, YELLOW, WHITE = (np.array(fa.rgb(c)) for c in COLS)
SS = 8                                   # the supersampling of the made-up art


def paint(scale):
    """The stamp at `scale` pixels a texel, drawn exactly: a blue band, a slanted yellow stem across it with a black line a
    texel wide round it, a white disc; and the stamp's own region (the rest is ground)."""
    w, h = 96 * scale, 64 * scale
    yy, xx = np.mgrid[0:h, 0:w]
    x, y = (xx + 0.5) / scale, (yy + 0.5) / scale
    img = np.zeros((h, w, 3), np.float32)
    img[...] = BLACK
    img[(y > 20) & (y < 44)] = BLUE                                   # the band
    stem = (x - 0.3 * y > 30) & (x - 0.3 * y < 42) & (y > 8) & (y < 56)   # a stem slanted like an italic
    line = (x - 0.3 * y > 29) & (x - 0.3 * y < 43) & (y > 7) & (y < 57)
    img[line] = BLACK
    img[stem] = YELLOW
    disc = (x - 72) ** 2 + (y - 32) ** 2 < 14 ** 2
    img[disc] = WHITE
    return img, ((y > 20) & (y < 44)) | line | disc


def original(region=False):
    """The 1x art: the exact picture box-filtered down, as a painter's anti-aliasing leaves it."""
    big, inside = paint(SS)
    o = np.clip(big.reshape(64, SS, 96, SS, 3).mean((1, 3)) + 0.5, 0, 255).astype(np.uint8)
    return (o, inside.reshape(64, SS, 96, SS).mean((1, 3)) >= 0.5) if region else o


def left_edge(rgb, colour, rows, x0, x1):
    """For each row, where the colour's share first crosses a half between x0 and x1, to a fraction of a pixel."""
    d = colour - BLACK
    out = []
    for r in rows:
        t = np.clip(((rgb[r, x0:x1].astype(np.float32) - BLACK) @ d) / (d @ d), 0, 1)
        i = int(np.argmax(t >= 0.5))
        a, b = t[i - 1], t[i]
        out.append(x0 + i - 1 + (0.5 - a) / max(b - a, 1e-6))
    return np.array(out)


class FlatArtTests(unittest.TestCase):
    def test_unmix_reads_an_edge_texels_share(self):
        o = original()
        W, _ = fa.unmix(o, [fa.rgb(c) for c in COLS])
        truth, _ = paint(SS)
        yel = (truth == YELLOW).all(-1).reshape(64, SS, 96, SS).mean((1, 3))
        self.assertLess(np.abs(W[..., 2] - yel).max(), 0.08, "the yellow's share of every texel, edges included")
        self.assertTrue(np.allclose(W.sum(-1), 1))

    def test_straight_edges_stay_straight_and_in_place(self):
        o = original()
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75)
        rgb = out[..., :3]
        rows = np.arange(12 * 4, 52 * 4)
        xs = left_edge(rgb, YELLOW, rows, 28 * 4, 50 * 4)
        p = np.polyfit(rows, xs, 1)
        self.assertLess(np.abs(xs - np.polyval(p, rows)).max(), 0.6, "the stem's left edge is one straight line")
        truth = 4 * (30 + 0.3 * (rows + 0.5) / 4)                      # x = 30 + 0.3 y in the exact art
        self.assertLess(np.abs(xs - truth).mean(), 1.0, "and within a pixel (a quarter texel) of where the art has it")
        self.assertEqual(tuple(rgb[32 * 4, 45 * 4]), tuple(YELLOW.astype(np.uint8)), "flat yellow inside")
        self.assertEqual(tuple(rgb[32 * 4, 10 * 4]), tuple(BLUE.astype(np.uint8)), "flat blue in the band")

    def test_curves_stay_round(self):
        o = original()
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75)
        white = (out[..., :3] == WHITE.astype(np.uint8)).all(-1)
        area = white.sum()
        self.assertLess(abs(area - np.pi * (14 * 4) ** 2) / (np.pi * (14 * 4) ** 2), 0.03, "the disc's area")
        ys, xs = np.nonzero(white)
        r = np.sqrt((xs + 0.5 - 72 * 4) ** 2 + (ys + 0.5 - 32 * 4) ** 2)
        self.assertLess(r.max() - 14 * 4, 1.5, "no pixel of it lies outside the circle by more than a pixel")

    def test_keyline_is_one_continuous_line(self):
        o = original()
        key = dict(colour="#000000", width=1.0, on=("#0042ce",), of=("#ffbd00",))
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, lines=[key])
        rgb = out[..., :3]
        for r in range(22 * 4, 42 * 4):
            row = rgb[r, 20 * 4:56 * 4]
            dark = (row.max(-1) < 60)
            runs = np.diff(np.concatenate(([0], dark.astype(np.int8), [0])))
            lengths = np.flatnonzero(runs == -1) - np.flatnonzero(runs == 1)
            self.assertEqual(len(lengths), 2, f"row {r}: a black line each side of the stem")
            self.assertTrue(all(2 <= n <= 7 for n in lengths), f"row {r}: about a texel wide, got {lengths}")

    def test_a_dark_fringe_goes_to_the_band_and_the_arts_own_black_stays(self):
        """Between the band and a white bar: a row of darker blue (the band's anti-aliased edge) is the band reaching the
        white, no black pixel between them; a row of pure black is the art's own gap and stays black."""
        fringe = dict(colour="#000000", width=0, on=("#0042ce",), of=("#ffffff",))
        for row, want_black in (((0, 41, 166), False), ((0, 0, 0), True)):
            o = original()
            o[17:19, 4:60] = WHITE.astype(np.uint8)            # a white bar just above the band (y 20)
            o[19, 4:60] = row
            out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, lines=[fringe])
            col = out[17 * 4 + 2:22 * 4, 30 * 4, :3]                 # from inside the white bar into the band
            black = int((col.max(-1) < 60).sum())
            if want_black:
                self.assertGreaterEqual(black, 3, f"a texel of black kept between band and white ({row})")
            else:
                self.assertEqual(black, 0, f"no black between band and white ({row})")

    def test_a_stroked_line_follows_its_own_colour_not_the_layers_above(self):
        """The lettering's keyline is stroked along the yellow's own outline: a white bar on the band gets none."""
        o = original()
        o[24:28, 60:90] = WHITE.astype(np.uint8)           # a white bar on the blue band, away from the stem
        key = dict(colour="#000000", width=1.0, on=("#0042ce",), of=("#ffbd00",))
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, lines=[key])
        col = out[22 * 4:30 * 4, 75 * 4, :3]
        self.assertFalse((col.max(-1) < 60).any(), "no black stroked round the white bar")

    def test_a_mark_is_fitted_to_the_originals_letters_and_blanks_nothing_else(self):
        """A TM drawn rotated and slanted at a known size, box-filtered to texels as the original's is: the fit finds its
        height and angle (not the ink box's, which a rotated mark overfills), and a blue post in the box is kept."""
        fonts = fa.find_fonts()
        if not fonts:
            self.skipTest("no Arial")
        truth = (6.5, -18.0, 0.0, 0.05, 14.0, 8.0)
        cover = fa.place(fonts[0], "TM", truth, (16, 28), 8)
        cover = cover.reshape(16, 8, 28, 8).mean((1, 3))
        f, p, e = fa.fit_mark(cover, "TM", fonts)
        # (the face itself is not asserted: a bold face sheared and its italic draw nearly the same texels)
        self.assertLess(abs(p[0] - truth[0]), 0.5, f"the cap height, {p[0]:.2f} texels")
        self.assertLess(abs(p[1] - truth[1]), 5.0, f"the angle, {p[1]:.1f} degrees (a slant trades a few degrees with it)")
        self.assertLess(e, 0.002, "the texels matched")
        o = original()
        box = (66, 46, 94, 62)
        mark = (cover[..., None] * YELLOW).astype(np.uint8)
        o[box[1]:box[3], box[0]:box[2]] = mark
        o[48:60, 90:93] = BLUE.astype(np.uint8)            # a blue post right of the mark, inside the mark's box
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, marks=[dict(box=box, text="TM", colour="#ffbd00")])
        drawn = out[box[1] * 4:box[3] * 4, box[0] * 4:box[2] * 4, :3].astype(np.float32)
        got = (drawn @ YELLOW / (YELLOW @ YELLOW)).reshape(16, 4, 28, 4).mean((1, 3))
        self.assertLess(float(np.abs(got[:, :22] - cover[:, :22]).mean()), 0.03, "the drawn mark, box-filtered, is the original's")
        post = out[50 * 4:58 * 4, 91 * 4, :3]
        self.assertTrue((np.abs(post.astype(int) - BLUE.astype(int)).max(-1) < 10).all(), "the post beside it is untouched")

    def test_a_stamp_on_a_picture_keeps_the_picture(self):
        o, stamp = original(region=True)
        yy, xx = np.mgrid[0:64, 0:96]
        photo = np.dstack([30 + 30 * np.sin(xx / 5.0), 150 + 40 * np.cos(yy / 7.0), 40 + 0 * xx]).astype(np.float32)   # a green, far from the stamp's colours and their mixes
        art = np.where(stamp[..., None], o.astype(np.float32), photo).astype(np.uint8)
        out, _ = fa.redraw(art, COLS, 4, 0.5, 0.75, picture=True)
        alpha = out[..., 3]
        self.assertEqual(alpha[2 * 4, 2 * 4], 0, "clear away from the stamp")
        self.assertEqual(alpha[32 * 4, 72 * 4], 255, "solid inside it")
        mask = fa.stamp_mask(art, [fa.rgb(c) for c in COLS])
        self.assertLess((mask ^ stamp)[:, 1:-1].mean(), 0.03, "the stamp found to within a texel (the band runs off the edges)")

    def test_a_mark_is_set_from_a_font(self):
        if fa.find_font() is None:
            self.skipTest("no Arial")
        o = original()
        o[50:58, 60:72] = YELLOW.astype(np.uint8)                      # a blob standing for a tiny TM mark
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, marks=[dict(box=(58, 48, 74, 60), text="TM", colour="#ffbd00")])
        box = out[50 * 4:58 * 4, 60 * 4:72 * 4, :3]
        yellow = (box == YELLOW.astype(np.uint8)).all(-1).mean()
        self.assertTrue(0.05 < yellow < 0.6, f"lettering, not a filled blob: {yellow:.2f} yellow")


if __name__ == "__main__":
    unittest.main()
