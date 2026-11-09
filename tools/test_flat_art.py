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

    def test_a_dark_fringe_between_two_colours_goes_to_the_one_named(self):
        """Where the band's edge texels against the white are a darker blue (a texel of near-black between), the blue
        reaches the white: no black pixel between them in the render."""
        o = original()
        # the band's top edge (y 20) against a white bar laid just above it, with a near-black texel row between
        o[17:19, 4:60] = WHITE.astype(np.uint8)
        o[19, 4:60] = (0, 0, 33)
        fringe = dict(colour="#000000", width=0, on=("#0042ce",), of=("#ffffff",))
        for lines, want in (((), False), ((fringe,), True)):
            out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, lines=lines)
            col = out[17 * 4 + 2:22 * 4, 30 * 4, :3]                 # from inside the white bar into the band
            black = (col.max(-1) < 60).any()
            self.assertEqual(not black, want, f"black between blue and white: {black} with lines={bool(lines)}")

    def test_a_stroked_line_follows_its_own_colour_not_the_layers_above(self):
        """The lettering's keyline is stroked along the yellow's own outline: a white bar on the band gets none."""
        o = original()
        o[24:28, 60:90] = WHITE.astype(np.uint8)           # a white bar on the blue band, away from the stem
        key = dict(colour="#000000", width=1.0, on=("#0042ce",), of=("#ffbd00",))
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, lines=[key])
        col = out[22 * 4:30 * 4, 75 * 4, :3]
        self.assertFalse((col.max(-1) < 60).any(), "no black stroked round the white bar")

    def test_a_mark_is_set_to_its_ink_box_and_blanks_nothing_else(self):
        if fa.find_font() is None:
            self.skipTest("no Arial")
        o = original()
        o[50:58, 70:82] = YELLOW.astype(np.uint8)          # a 12x8 blob standing for the mark, below the band
        o[48:60, 86:90] = BLUE.astype(np.uint8)            # a blue post 4 texels right of the mark, inside the mark's box
        out, _ = fa.redraw(o, COLS, 4, 0.5, 0.75, marks=[dict(box=(66, 46, 94, 62), text="TM", colour="#ffbd00")])
        rgb = out[..., :3]
        yel = (np.abs(rgb.astype(int) - YELLOW.astype(int)).max(-1) < 60)
        ys, xs = np.nonzero(yel[44 * 4:64 * 4, 64 * 4:96 * 4])
        self.assertLessEqual(abs((xs.max() - xs.min() + 1) / 4 - 12), 0.5, "the mark as wide as its ink box")
        self.assertLessEqual(abs((ys.max() - ys.min() + 1) / 4 - 8), 0.5, "and as high")
        post = rgb[50 * 4:58 * 4, 87 * 4]
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
