"""Tests of tools/dds_mips.py and the .dds writers of tools/upscale-textures.py and tools/text-redraws.py (python -m unittest
tools/test_dds_mips.py, or pytest tools/). Made-up pictures only; skipped without etcpak (pip install etcpak)."""
import importlib.util, os, sys, tempfile, unittest
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dds_mips

def load(name, file):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, file))
    m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m); return m

try:
    import etcpak
except ImportError:
    etcpak = None

def banded(w=128, h=64):
    """An orange panel with two keyed stripes (alpha 0, the colour bled under them orange) and a black and white letter-like block:
    the shape of the bus's side band."""
    px = np.zeros((h, w, 4), np.uint8); px[..., :3] = (238, 121, 1); px[..., 3] = 255
    px[20:24, :, 3] = 0; px[28:30, :, 3] = 0
    px[40:52, 10:50, :3] = 0; px[44:48, 20:40, :3] = 255
    return px

def raw(path):
    with open(path, "rb") as f: return f.read()

@unittest.skipIf(etcpak is None, "etcpak not installed")
class WrittenChainTests(unittest.TestCase):
    def written(self, writer):
        d = tempfile.mkdtemp(); p = os.path.join(d, "t.dds")
        writer(p, banded(), etcpak)
        return dds_mips.read_levels(p)

    def check_chain(self, levels, hd):
        self.assertEqual(hd["mips"], 8)                                   # 128x64 down to 1x1
        self.assertEqual([l.shape[:2] for l in levels], [(64 >> i or 1, 128 >> i) for i in range(8)])
        self.assertEqual(hd["trailing"], 0)
        chain = dds_mips.mip_chain(banded())
        for k in range(1, len(levels)):                                   # each level the picture filtered down (BC7's error only)
            d = np.abs(levels[k].astype(int) - chain[k].astype(int))
            self.assertLess(d.mean(), 3, k)
            if min(levels[k].shape[:2]) >= 16: self.assertLess(d.max(), 12, k)
        e = dds_mips.level1_error(levels)
        self.assertLess(float((e > 48).mean()), 0.001)
        # the keyed stripes keep the colour bled under them in level 1 (an alpha-weighted resize made them black)
        stripe = levels[1][10:12, :, :]
        self.assertTrue((stripe[..., 3] < 8).all())
        self.assertGreater(int(stripe[..., 0].min()), 200)

    def test_dds_mips_writer_level1_is_the_filtered_top(self):
        self.check_chain(*self.written(dds_mips.write_dds))

    def test_upscale_textures_writes_the_full_filtered_chain(self):
        self.check_chain(*self.written(load("upscale_textures", "upscale-textures.py").write_dds))

    def test_text_redraws_writes_the_full_filtered_chain(self):
        self.check_chain(*self.written(load("text_redraws", "text-redraws.py").write_dds))

    def test_check_flags_an_alpha_weighted_chain_and_regenerate_mends_it_keeping_the_top(self):
        from PIL import Image
        d = tempfile.mkdtemp(); p = os.path.join(d, "old.dds")
        top = banded(); levels = [Image.fromarray(top, "RGBA")]
        while levels[-1].size != (1, 1):                                  # the old writers' chain: Pillow's RGBA resize
            im = levels[-1]; levels.append(im.resize((max(1, im.size[0] // 2), max(1, im.size[1] // 2)), Image.BOX))
        blocks = [dds_mips._bc7(np.array(l), etcpak) for l in levels]
        with open(p, "wb") as f: f.write(dds_mips.dds_header(128, 64, len(blocks), len(blocks[0])) + b"".join(blocks))
        self.assertTrue(dds_mips.check(p)[0])
        top_bytes = raw(p)[148:148 + len(blocks[0])]
        link = os.path.join(d, "link.dds"); os.link(p, link)
        dds_mips.regenerate(p, etcpak)
        self.assertFalse(dds_mips.check(p)[0])
        self.assertEqual(raw(p)[148:148 + len(blocks[0])], top_bytes)  # the top level kept byte for byte
        self.assertTrue(dds_mips.check(link)[0])                          # a hard link keeps the old file

    def test_check_flags_a_stale_chain_from_another_picture(self):
        d = tempfile.mkdtemp(); p = os.path.join(d, "stale.dds")
        old = banded(); new = old.copy(); new[20:30, :, 3] = 255; new[40:52, :, :3] = (238, 121, 1)
        a = dds_mips.encode_dds(old, etcpak); b = dds_mips.encode_dds(new, etcpak)
        n0 = 128 * 64                                                     # BC7: a byte a texel
        with open(p, "wb") as f: f.write(b[:148 + n0] + a[148 + n0:])     # the new top over the old picture's chain
        self.assertTrue(dds_mips.check(p)[0])
        dds_mips.regenerate(p, etcpak)
        self.assertFalse(dds_mips.check(p)[0])

    def test_bc7_error_alone_on_a_fine_high_contrast_texture_is_not_stale(self):
        """Fine black and white texels: BC7 alone puts level 1 far from the filtered top on many texels; a chain made from the
        top level (regenerate) passes, the same top over another picture's chain does not."""
        px = np.zeros((64, 128, 4), np.uint8); px[..., 3] = 255
        px[..., :3] = np.random.default_rng(1).choice([0, 255], size=(64, 128, 3)).astype(np.uint8)
        d = tempfile.mkdtemp(); p = os.path.join(d, "fine.dds")
        dds_mips.write_dds(p, px, etcpak); dds_mips.regenerate(p, etcpak)
        stale, share, black, _ = dds_mips.check(p)
        self.assertGreater(share, 0.002); self.assertEqual(black, 0.0); self.assertFalse(stale)
        other = dds_mips.encode_dds(banded(), etcpak); n0 = 128 * 64
        with open(p, "rb") as f: data = f.read()
        with open(p, "wb") as f: f.write(data[:148 + n0] + other[148 + n0:])
        self.assertTrue(dds_mips.check(p)[0])

class ChainTests(unittest.TestCase):
    def test_chain_is_linear_light_and_not_alpha_weighted(self):
        px = np.zeros((2, 2, 4), np.uint8); px[0, :, :3] = 255; px[1, :, :3] = 0; px[..., 3] = 255; px[0, 0, 3] = 0
        l1 = dds_mips.mip_chain(px)[1][0, 0]
        self.assertEqual(int(l1[0]), 188)                                 # half of white in linear light, sRGB 188 (not 128)
        self.assertEqual(int(l1[3]), 191)                                 # alpha averaged as it is

    def test_odd_sizes_go_down_to_one_texel(self):
        chain = dds_mips.mip_chain(np.full((76, 181, 4), 200, np.uint8))
        self.assertEqual(chain[1].shape[:2], (38, 90)); self.assertEqual(chain[-1].shape[:2], (1, 1))
        self.assertTrue((chain[-1] == 200).all())

if __name__ == "__main__":
    unittest.main()
