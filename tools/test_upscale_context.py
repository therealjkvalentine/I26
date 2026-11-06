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

if __name__ == "__main__":
    unittest.main()
