"""Tests of tools/upscale-textures.py's context padding (--neighbours): the adjacency plan from an `i76 tex-seams` file, the
context canvas and the crop back (python -m unittest tools/test_upscale_context.py, or pytest tools/). No model, no game
art: made-up textures and a stand-in model."""
import importlib.util, os, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("upscale_textures", os.path.join(HERE, "upscale-textures.py"))
ut = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(ut)

def picture(w, h, seed):
    rng = np.random.default_rng(seed)
    im = np.zeros((h, w, 4), np.uint8); im[..., :3] = rng.integers(0, 256, (h, w, 3)); im[..., 3] = 255
    return im

# a fixture in the form `i76 tex-seams` writes: A's right border (u = 1) against B's left (u = 0) all the way down, B stored
# bottom up (v = 0 its bottom row) and twice A's height; C repeats along u only; D's top meets nothing on a border
FIXTURE = {
    "seams": [
        {"cls": "car", "model": "x.vcf", "a": "aa11side", "auv": [1, 0, 1, 1], "b": "aa11back", "buv": [0, 0, 0, 1], "ac": [0.9, 0.5], "bc": [0.1, 0.5]},
        {"cls": "car", "model": "x.vcf", "a": "aa11side", "auv": [0.2, 0.3, 0.4, 0.3], "b": "dd11roof", "buv": [0.5, 0.5, 0.6, 0.5]},
        {"cls": "car", "model": "x.vcf", "a": "aa11side", "auv": [1, 0, 1, 1], "b": "zz11other", "buv": [0, 0, 0, 1]},
    ],
    "tiles": {"cc_1wall": {"u": True, "v": False}},
}

class Runner:
    """A stand-in for the models: run() a nearest k x enlargement (or a blur at k x, which reaches across tile and texture
    borders like a model's receptive field), then() a nearest 2x."""
    def __init__(self, k=4, blur=False, then=False): self.k, self.blur, self.then_file = k, blur, ("2x.pth" if then else None)
    def run(self, im, colour=None):
        big = np.repeat(np.repeat(im, self.k, 0), self.k, 1)
        if self.blur:
            rgb = np.pad(big[..., :3].astype(np.float32), ((0, 0), (8, 8), (0, 0)), mode="reflect")
            rgb = sum(rgb[:, i:i + big.shape[1]] for i in range(17)) / 17
            big = np.dstack([np.clip(rgb + 0.5, 0, 255).astype(np.uint8), big[..., 3]])
        return big
    def then(self, big): return np.repeat(np.repeat(big, 2, 0), 2, 1)

class PlanTests(unittest.TestCase):
    def test_sides_of_the_borders_and_the_stored_row_order(self):
        self.assertEqual(ut.side_of((0, 0), (0, 1), False), ("left", 0, 1))
        self.assertEqual(ut.side_of((1, 0), (1, 1), True), ("right", 1, 0))         # bottom up: v = 0 is the picture's last row
        self.assertEqual(ut.side_of((0, 0), (1, 0), True), ("bottom", 0, 1))
        self.assertEqual(ut.side_of((0, 0), (1, 0), False), ("top", 0, 1))
        self.assertIsNone(ut.side_of((0.2, 0.3), (0.4, 0.3), False))

    def test_the_plan_from_a_tex_seams_fixture(self):
        plan = ut.neighbour_plan(FIXTURE, frozenset({"aa11back"}))
        self.assertEqual(set(plan), {"aa11side", "aa11back", "zz11other", "cc_1wall"})
        right = plan["aa11side"]["sides"]["right"]
        self.assertEqual(len(right), 1, "the stretch is claimed once")
        s0, s1, nb, nside, t0, t1 = right[0]
        self.assertEqual((nb, nside), ("aa11back", "left"), "the same model's neighbour wins")
        self.assertEqual((s0, s1, t0, t1), (0, 1, 1, 0), "B is stored bottom up: its v = 0 is the picture's foot")
        self.assertEqual(plan["aa11back"]["sides"]["left"][0][2:4], ("aa11side", "right"))
        self.assertEqual(plan["cc_1wall"]["wrap"], (True, False))
        self.assertNotIn("dd11roof", plan, "an edge inside the picture is not a border")

class CanvasTests(unittest.TestCase):
    def test_wrap_and_mirror(self):
        im = picture(40, 36, 1); m = 8
        c = ut.context_canvas(im, {"wrap": (True, False), "sides": {}}, m, lambda n: None)
        self.assertEqual(c.shape, (36 + 2 * m, 40 + 2 * m, 4))
        self.assertTrue(np.array_equal(ut.trim(c, m), im), "the texture sits in the middle")
        self.assertTrue(np.array_equal(c[m:-m, :m], im[:, -m:]), "left context: the texture's own right columns")
        self.assertTrue(np.array_equal(c[m:-m, -m:], im[:, :m]))
        self.assertTrue(np.array_equal(c[:m, m:-m], im[m:0:-1]), "top: mirrored (no wrap along y)")

    def test_a_neighbours_facing_texels_at_its_own_density(self):
        a, b = picture(16, 16, 2), picture(16, 32, 3)                              # B twice as many texels down the shared edge
        plan = ut.neighbour_plan(FIXTURE, frozenset({"aa11back"}))
        c = ut.context_canvas(a, plan["aa11side"], 4, lambda n: {"aa11back": b}.get(n))
        # A's column right of its border: B's left texels, one of each two of B's rows, B upside down (stored bottom up, so
        # A's top row meets B's foot)
        self.assertTrue(np.array_equal(c[4:-4, 4 + 16], b[31::-2, 0]))
        self.assertTrue(np.array_equal(c[4:-4, 4 + 16 + 3], b[31::-2, 6]), "A's fourth texel out is B's seventh in")
        self.assertTrue(np.array_equal(c[4:-4, :4], a[:, 4:0:-1]), "the left: mirrored, nothing meets it")

    def test_a_texture_wider_than_the_context_or_narrower(self):
        im = picture(8, 8, 4)
        c = ut.context_canvas(im, {"wrap": (True, True), "sides": {}}, 32, lambda n: None)
        self.assertEqual(c.shape, (72, 72, 4))
        self.assertTrue(np.array_equal(c[:8, :8], im), "wraps round many times")
        c = ut.context_canvas(im, None, 32, lambda n: None)
        self.assertEqual(c.shape, (72, 72, 4))

class CropTests(unittest.TestCase):
    def setUp(self):
        self.src = os.path.join(HERE, "__no_such_export__")
        ut.CONTEXT, ut.CONTEXT_LATE = 32, 16

    def test_the_crop_lands_on_the_texture(self):
        im = picture(24, 20, 5)
        for runner, scale in ((Runner(), 4), (Runner(then=True), 8)):
            out = ut.with_context(im, {"wrap": (True, True), "sides": {}}, runner, None, False, scale, self.src)
            self.assertTrue(np.array_equal(out, np.repeat(np.repeat(im, scale, 0), scale, 1)), f"{scale}x")

    def test_the_eight_x_brought_down_keeps_its_place(self):
        im = picture(24, 20, 6)
        out = ut.with_context(im, {"wrap": (True, True), "sides": {}}, Runner(then=True), None, False, 4, self.src)
        self.assertEqual(out.shape, (80, 96, 4))
        down = out[..., :3].reshape(20, 4, 24, 4, 3).mean(axis=(1, 3))
        self.assertLess(np.abs(down - im[..., :3]).mean(), 3.0, "each texel's block is the texel")

    def test_a_wrapped_context_closes_the_repeat_seam(self):
        # a horizontal ramp that repeats seamlessly; the blur stands in for a model's reach across the border
        x = np.arange(32); ramp = (128 + 100 * np.sin(2 * np.pi * x / 32)).astype(np.uint8)
        im = np.dstack([np.tile(ramp, (8, 1))] * 3 + [np.full((8, 32), 255, np.uint8)])
        def wrap_step(big):
            c = big[..., 0].astype(float); inner = np.abs(np.diff(c, axis=1)).mean()
            return np.abs(c[:, 0] - c[:, -1]).mean() / inner
        plain = Runner(blur=True).run(im)                                               # mirrored at the borders
        ctx = ut.with_context(im, {"wrap": (True, False), "sides": {}}, Runner(blur=True), None, False, 4, self.src)
        self.assertGreater(wrap_step(plain), 3.0)
        self.assertLess(wrap_step(ctx), 2.0)

class CellTests(unittest.TestCase):
    LAYOUT = """dst zpanel.map
    label anchor              10   20       0    0
src zsheet.map
    label lamp_off             0    0       4    3
    label lamp_on              0    3       4    3
    label lamp_on_too          0    3       4    3
    label past_the_foot        4    5       2    9
"""

    def test_a_layouts_cells_by_sheet(self):
        cells = ut.layout_cells(self.LAYOUT)
        self.assertEqual(set(cells), {"zsheet"}, "a dst section's anchors are not cells")
        self.assertEqual(cells["zsheet"], [(0, 0, 4, 3), (0, 3, 4, 3), (4, 5, 2, 9)], "a cell named twice is run once")

    def test_no_cell_takes_in_its_neighbours_picture(self):
        im = np.zeros((6, 6, 4), np.uint8); im[..., 3] = 255
        im[0:3, 0:4, :3] = (250, 250, 250); im[3:6, 0:4, :3] = (0, 200, 0)
        blur = Runner(4, blur=True)
        def vblur(p):     # the stand-in's blur turned down the columns: reaches across the cells' shared row
            t = np.ascontiguousarray(p.transpose(1, 0, 2))
            return np.ascontiguousarray(blur.run(t).transpose(1, 0, 2))
        whole = vblur(im)
        cells = ut.by_cells(im, [(0, 0, 4, 3), (0, 3, 4, 3)], vblur)
        self.assertLess(int(whole[11, 0, 0]), 240, "the whole run mixes the next cell into the first's foot")
        self.assertTrue((cells[0:12, 0:16, :3] == 250).all(), "the first cell on its own is its own white to its foot")
        self.assertTrue((cells[12:24, 0:16, 1] == 200).all() and (cells[12:24, 0:16, 0] == 0).all(), "the second its own green")
        self.assertTrue((cells[:, 16:] == whole[:, 16:]).all(), "texels in no cell keep the whole run's")
        self.assertEqual(cells.shape, whole.shape)

    PLACED = """dst zpanel.map
    label lamp                10   20       0    0
    label rrtyre              40   20       0    0
    label rltyre               2   20       0    0
dst zpanel320.map
    label 3lamp                5   10       0    0
    label 3rrtyre             20   10       0    0
src zsheet.map
    label lamp_off             0    0       4    3
    label lamp_on              0    3       4    3
    label tyre_off             4    0       2    2
    label tyre_on              4    0       2    2
    label nowhere_on           8    0       2    2
src zsheet320.map
    label 3lamp_off            0    0       2    2
    label 3tyre_on             2    0       2    2
"""

    def test_a_cells_place_is_its_anchor_on_the_panel(self):
        places = ut.layout_places(self.PLACED)
        self.assertEqual(places["zsheet"][(0, 0, 4, 3)], ("zpanel", 10, 20), "the label's name without its state is the anchor")
        self.assertEqual(places["zsheet"][(0, 3, 4, 3)], ("zpanel", 10, 20))
        self.assertEqual(places["zsheet"][(4, 0, 2, 2)], ("zpanel", 40, 20), "a cell stamped at several anchors takes the first named")
        self.assertNotIn((8, 0, 2, 2), places["zsheet"], "a cell with no anchor has no place")
        self.assertEqual(places["zsheet320"][(0, 0, 2, 2)], ("zpanel320", 5, 10), "the low-resolution set among its own anchors")
        self.assertEqual(places["zsheet320"][(2, 0, 2, 2)], ("zpanel320", 20, 10))

    def test_a_cell_is_set_in_the_panel_round_it_not_its_mirror(self):
        # a cell whose shape touches its top (a white point over grey): mirrored, the model (a vertical blur) sees the point
        # continue upward and keeps it white to the edge; set in the panel's green, the edge takes the green the game shows there
        cell = np.zeros((3, 4, 4), np.uint8); cell[..., :3] = 100; cell[..., 3] = 255; cell[0, 1:3, :3] = 250
        panel = np.zeros((40, 40, 4), np.uint8); panel[..., 1] = 200; panel[..., 3] = 255
        blur = Runner(4, blur=True)
        def vblur(p):
            t = np.ascontiguousarray(p.transpose(1, 0, 2))
            return np.ascontiguousarray(blur.run(t).transpose(1, 0, 2))
        mirrored = ut.by_cells(cell, [(0, 0, 4, 3)], vblur, m=2)
        placed = ut.by_cells(cell, [(0, 0, 4, 3)], vblur, lambda c: ut.panel_around(panel, 10, 20, c[2], c[3], 2), m=2)
        self.assertEqual(int(mirrored[0, 6, 0]), int(mirrored[0, 6, 1]), "mirrored, the point's edge mixes only the cell's own greys")
        self.assertGreater(int(placed[0, 6, 1]), int(placed[0, 6, 0]) + 20, "placed, the point's edge blends toward the panel's green")
        self.assertTrue((placed[..., 3] == 255).all() and placed.shape == (12, 16, 4), "the cell's own alpha and size")
        # a keyed texel shows the panel through to the model and stays keyed in the output
        cell[2, 0, 3] = 0
        placed = ut.by_cells(cell, [(0, 0, 4, 3)], vblur, lambda c: ut.panel_around(panel, 10, 20, c[2], c[3], 2), m=2)
        self.assertTrue((placed[8:12, 0:4, 3] == 0).all() and (placed[0:8, :, 3] == 255).all())
        # no anchor: the cell's own border colour round it, the point blending toward the grey, not the green
        alone = ut.by_cells(cell, [(0, 0, 4, 3)], vblur, lambda c: None, m=2)
        self.assertLess(int(alone[0, 6, 0]), 250); self.assertEqual(int(alone[0, 6, 0]), int(alone[0, 6, 1]))
        # a place off the panel: none
        self.assertIsNone(ut.panel_around(panel, 39, 20, 4, 3, 2))

    @staticmethod
    def chevrons():
        """Two chevron cells (12 x 4 each, one over the other) on the panel's grey, packed as the damage sheet's are: each chevron
        (three rows, its arms joined to its point) sits at its cell's top, and the second's apex outline (dark) lies in the first
        cell's last row, clear of the first chevron's arms; the panel grey 100 everywhere."""
        sheet = np.zeros((8, 12, 4), np.uint8); sheet[..., :3] = 100; sheet[..., 3] = 255
        for top in (0, 4):
            sheet[top, 5:7, :3] = 250; sheet[top + 1, 3:5, :3] = 250; sheet[top + 1, 7:9, :3] = 250; sheet[top + 2, 1:3, :3] = 250; sheet[top + 2, 9:11, :3] = 250
        sheet[3, 5:7, :3] = 0                                                       # the second chevron's apex outline, in the first cell's last row
        panel = np.zeros((40, 40, 4), np.uint8); panel[..., :3] = 100; panel[..., 3] = 255
        return sheet, panel

    def test_a_neighbours_art_packed_into_the_border_shows_the_panel(self):
        sheet, panel = self.chevrons()
        under = panel[20:24, 10:22]
        nb = ut.neighbour_art(sheet[0:4], under, sheet, 0, 0)
        self.assertEqual(sorted(zip(*np.nonzero(nb))), [(3, 5), (3, 6)], "the apex outline in the last row, and nothing of the chevron")
        self.assertFalse(ut.neighbour_art(sheet[4:8], under, sheet, 0, 4).any(), "the second cell's point is its own, joined to its arms")
        own = sheet[0:4].copy(); own[2, 4:8, :3] = 0                            # the run joined to the chevron is the chevron's own outline
        self.assertFalse(ut.neighbour_art(own, under, sheet, 0, 0).any())
        alone = sheet.copy(); alone[4] = (100, 100, 100, 255)                   # nothing across the border: a line of the cell's own
        self.assertFalse(ut.neighbour_art(alone[0:4], under, alone, 0, 0).any())
        # on its own, the cell goes to the model with the panel in that row: the stand-in (nearest 2x) shows no dark there
        placed = ut.by_cells(sheet, [(0, 0, 12, 4), (0, 4, 12, 4)], lambda p: np.repeat(np.repeat(p, 2, 0), 2, 1), lambda c: ut.panel_around(panel, 10, 20, c[2], c[3], 2), m=2)
        self.assertTrue((placed[6:8, 10:14, :3] == 100).all(), "the first cell's last row is the panel's grey, not the next chevron's outline")
        self.assertTrue((placed[0:2, 10:14, :3] == 250).all() and (placed[8:10, 10:14, :3] == 250).all(), "both chevrons' points as they were")

    def test_a_cell_carries_the_panels_own_picture_where_the_stamp_changes_nothing(self):
        # the sheet's cells in their own colours (white, green) on the panel's grey; the panel's upscale a blue-grey the model
        # never made: stamped, the tile's background is the panel's own picture, its lit shape the model's, and its last row
        # (the next cell's outline) the panel's, not the next cell's
        sheet, panel = self.chevrons(); sheet[4:8][sheet[4:8, :, 0] == 250] = (0, 200, 0, 255)
        sheet[3, 0, 3] = 0                                                       # a keyed texel in the first cell's corner
        px = np.repeat(np.repeat(sheet, 2, 0), 2, 1)                             # the model's finished sheet, 2x
        big = np.zeros((80, 80, 4), np.uint8); big[..., :3] = (90, 100, 120); big[..., 3] = 255
        cells = [(0, 0, 12, 4), (0, 4, 12, 4)]
        out = ut.cells_in_panel(px.copy(), sheet, cells, 2, lambda c: ut.panel_around(panel, 10, 20, c[2], c[3], 2), lambda c: (big, 10, 20), m=2)
        from scipy import ndimage
        for cell, lit, colour in ((out[0:8, 0:24], np.repeat(np.repeat((sheet[0:4, :, :3] == 250).all(2), 2, 0), 2, 1), 250),
                                  (out[8:16, 0:24], np.repeat(np.repeat(sheet[4:8, :, 1] == 200, 2, 0), 2, 1), (0, 200, 0))):
            d = ndimage.distance_transform_edt(~lit)
            self.assertTrue((cell[lit, :3] == colour).all(), "the lit shape is the model's")
            self.assertTrue((cell[d >= 1.25, :3] == (90, 100, 120)).all(), "from five eighths of a texel out every texel is the panel's own picture")
            beside = (d > 0) & (d < 1.25)
            self.assertTrue(beside.any() and (cell[beside, :3] != (90, 100, 120)).any() and (cell[beside, :3] != 100).any(), "beside it the model's edge fades into the panel's")
        self.assertTrue((out[6:8, 10:14, :3] == (90, 100, 120)).all(), "the first cell's last row (the next chevron's outline) is the panel's, not the next cell's")
        self.assertTrue((out[..., 3] == px[..., 3]).all() and (out[6:8, 0:2, 3] == 0).all() and (out[6:8, 0:2, :3] == (90, 100, 120)).all(), "the alpha stays the cell's")
        # no upscale of the panel to read: the sheet as the model made it
        same = ut.cells_in_panel(px.copy(), sheet, cells, 2, lambda c: ut.panel_around(panel, 10, 20, c[2], c[3], 2), lambda c: None, m=2)
        self.assertTrue((same == px).all())


class Recut(unittest.TestCase):
    def test_the_alpha_is_the_fields_cut_and_the_edge_the_originals_colour(self):
        # a disc's original (dark rim texels round a white face) and a model's 4x with a lumpy alpha and a light fringe
        yy, xx = np.mgrid[0:16, 0:16]; r = np.hypot(yy - 7.5, xx - 7.5)
        orig = np.zeros((16, 16, 4), np.uint8); orig[r < 6.5] = (255, 255, 255, 255); orig[(r >= 5.5) & (r < 6.5)] = (16, 16, 16, 255)
        Y, X = np.mgrid[0:64, 0:64]; R = np.hypot(Y - 31.5, X - 31.5)
        px = np.zeros((64, 64, 4), np.uint8); px[..., :3] = 230
        px[..., 3] = np.where(R + 3 * np.sin(np.arctan2(Y - 31.5, X - 31.5) * 7) < 26, 255, 0)
        field = np.clip(128 + (26 - R) / 4 / 2 * 127, 0, 255).astype(np.uint8)      # 26 texels at 4x: the circle the outline makes
        out = ut.recut(px, field, orig)
        self.assertTrue(np.array_equal(out[..., 3] > 0, field >= 128))
        self.assertEqual(set(np.unique(out[..., 3])), {0, 255})
        rim = (out[..., 3] > 0) & (R > 24.5)
        self.assertLess(out[..., :3][rim].mean(), 120, "the cut's edge is the original's dark rim, not the model's light fringe")
        self.assertGreater(out[..., :3][R < 12].mean(), 200, "inside, the model's colour is kept")
        self.assertTrue((out[..., :3][out[..., 3] == 0].sum(axis=1) > 0).all(), "colour is bled under the key")

class PaintCut(unittest.TestCase):
    """--paint-masks and --paint-cut: a two-tone door (a white panel on black) with a small amber lamp and a thin gold ring."""
    def setUp(self):
        orig = np.zeros((32, 64, 4), np.uint8); orig[..., :3] = 24; orig[..., 3] = 255
        orig[8:28, 20:60, :3] = 200                                  # the white panel
        orig[14:16, 4:8, :3] = (240, 160, 0)                         # a lamp: 4 x 2 amber texels on the black
        yy, xx = np.mgrid[0:32, 0:64]; r = np.hypot(yy - 18, xx - 40)
        orig[(r > 6) & (r < 7) & (orig[..., 0] == 200), :3] = (220, 170, 20)   # a thin gold ring on the panel
        self.orig = orig
    def test_the_masks_find_the_panel_and_the_lamp_but_not_the_ring(self):
        two, det = ut.paint_masks(self.orig)
        self.assertIsNotNone(two); self.assertIsNotNone(det)
        self.assertTrue(two[8:28, 20:60].all(), "the panel is the two-tone region, its ring hole filled")
        self.assertFalse(two[:8].any() or two[:, :20].any())
        self.assertTrue(det[14:16, 4:8].all(), "the lamp is a detail")
        self.assertFalse(det[8:28, 20:60].any(), "the ring's pieces are not")
    def test_no_masks_for_one_flat_tone(self):
        flat = self.orig.copy(); flat[..., :3] = 90; flat[14:16, 4:8, :3] = (240, 160, 0)
        two, det = ut.paint_masks(flat)
        self.assertIsNone(two); self.assertIsNotNone(det)
    def test_the_cut_straightens_the_edge_and_keeps_the_rest(self):
        # the model's 4x: the panel's left edge wobbling two texels, the rest flat
        Y, X = np.mgrid[0:128, 0:256]
        px = np.zeros((128, 256, 4), np.uint8); px[..., 3] = 255; px[..., :3] = 24
        edge = 80 + 4 * np.sin(Y / 5.0)
        px[(Y >= 32) & (Y < 112) & (X >= edge) & (X < 240), :3] = 200
        px[60:70, 150:160, :3] = (0, 0, 255)                         # a detail of the model's inside the panel, to be kept
        field = np.clip(128 + (X - 80) / 4 / 2 * 127, 0, 255).astype(np.uint8)   # the straight outline at x = 80 (4x), inside to the right
        out = ut.paint_cut(px, field, 4)
        self.assertTrue((out[32:112, 84:100, :3] == 200).all(), "right of the outline the panel's own colour, no wobble")
        self.assertTrue((out[32:112, 60:76, :3] == 24).all(), "left of it the body's")
        self.assertTrue((out[60:70, 150:160, :3] == (0, 0, 255)).all(), "away from the edge the upscale is kept")
        self.assertTrue(np.array_equal(out[..., 3], px[..., 3]))
    def test_a_keyed_strip_beside_the_panel_takes_the_dark_side(self):
        # the panel ends at a keyed strip two texels wide, black beyond it; the model bled the panel's white under the key
        Y, X = np.mgrid[0:128, 0:256]
        px = np.zeros((128, 256, 4), np.uint8); px[..., 3] = 255; px[..., :3] = 24
        px[(Y >= 32) & (Y < 112) & (X < 160 + 4 * np.sin(Y / 5.0)), :3] = 200
        px[:, 160:168, :3] = (180, 180, 180); px[:, 160:168, 3] = 0           # the keyed strip, the model's bleed in it
        keyed = np.zeros((32, 64), bool); keyed[:, 40:42] = True
        field = np.clip(128 - (X - 160) / 4 / 2 * 127, 0, 255).astype(np.uint8)   # the outline at x = 160, the panel to the left
        out = ut.paint_cut(px, field, 4, keyed=keyed)
        self.assertTrue((out[32:112, 162:168, :3] == 24).all(), "the keyed strip is the black beyond it, not the bled white (the outline itself fades over a texel)")
        self.assertTrue((out[32:112, 140:156, :3] == 200).all(), "the panel's side is its own white up to the outline")
        self.assertTrue((out[:, 160:168, 3] == 0).all(), "the key stays")
    def test_keyed_texels_away_from_the_outline_keep_the_packs_colour(self):
        # a chequered band of keyed squares (the pack's black there) across the body, well away from the panel's edge: the keyed
        # rule reaches only as far as the field (2 texels out), so the squares are not washed with the body's bled colour
        Y, X = np.mgrid[0:128, 0:256]
        px = np.zeros((128, 256, 4), np.uint8); px[..., 3] = 255; px[..., :3] = (30, 60, 170)
        px[(Y >= 64) & (X < 120), :3] = 200                                   # the panel, its edge at x = 120 (4x)
        sq = (Y < 24) & (((X // 8) + (Y // 8)) % 2 == 0)
        px[sq, :3] = 0; px[sq, 3] = 0                                         # keyed squares, black in the pack, at the top
        keyed = np.zeros((32, 64), bool); keyed[:6] = sq[::4, ::4][:6]
        field = np.clip(128 - (X - 120) / 4 / 2 * 127, 0, 255).astype(np.uint8)
        field[Y < 64] = 0                                                      # the panel only below: far from the band
        out = ut.paint_cut(px, field, 4, keyed=keyed)
        self.assertTrue((out[sq][:, :3] == 0).all(), "the band's keyed squares stay black")
        self.assertTrue(np.array_equal(out[..., 3], px[..., 3]))

    def test_a_detail_is_the_original_enlarged(self):
        px = np.zeros((128, 256, 4), np.uint8); px[..., 3] = 255; px[..., :3] = 24
        px[52:68, 12:36, :3] = (120, 80, 40)                         # the model's blob with a dark rim where the lamp is
        yy, xx = np.mgrid[0:128, 0:256]
        d = np.maximum(np.abs(yy - 59.5) - 3.5, np.abs(xx - 23.5) - 7.5)   # distance outside the lamp's 4x box (rows 56..64, cols 16..32)
        field = np.clip(128 - d / 4 / 2 * 127, 0, 255).astype(np.uint8)
        out = ut.restore_detail(px, field, self.orig)
        self.assertTrue((out[57:63, 18:30, :3] == (240, 160, 0)).all(), "inside the outline the original's lamp colour")
        self.assertTrue((out[100:, 100:, :3] == 24).all(), "the rest is kept")

if __name__ == "__main__":
    unittest.main()
