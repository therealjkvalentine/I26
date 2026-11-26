"""Tests of tools/hd-pack.py and tools/hd_verify.py (python -m unittest tools/test_hd_pack.py): the recipes' shape, the steps'
textures and arguments, the keys, finish, verify's classes and release's archives. Made-up pictures only; no model, no game."""
import argparse, importlib.util, json, os, sys, tempfile, unittest, zipfile
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
_spec = importlib.util.spec_from_file_location("hd_pack", os.path.join(HERE, "hd-pack.py"))
hp = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(hp)
import hd_verify

try:
    import etcpak
except ImportError:
    etcpak = None

def args(**kw):
    a = dict(work="w", export=None, masters_from=None, out="o", models_dir="m", device="cpu", only=None, dry_run=True, install="i",
             game=None, version=None, jobs=0)
    a.update(kw); return argparse.Namespace(**a)

class Recipes(unittest.TestCase):
    def test_every_step_names_a_known_tool_its_lists_and_pinned_models(self):
        pins = json.load(open(os.path.join(hp.RECIPES, "models.json")))["models"]
        for name in ("world-4x", "world-8x"):
            r = hp.load_recipe(name)
            ids = [s["id"] for s in r["steps"]]
            self.assertEqual(len(ids), len(set(ids)), name)
            self.assertEqual(ids[-1], "finish")
            for st in r["steps"]:
                tool = st.get("tool")
                self.assertTrue(tool in ("export", "encode", "finish", "i76") or os.path.exists(os.path.join(HERE, tool)), (name, st["id"]))
                for m in st.get("models", []): self.assertIn(m, pins)
                for g in st.get("only", "").split(","):
                    if g.strip().startswith("@"): self.assertTrue(os.path.exists(os.path.join(hp.RECIPES, g.strip()[1:])), g)
                for r_ in st.get("reads", []): self.assertIn(r_, ids[:ids.index(st["id"])], (name, st["id"], "reads a step before it"))
            for m in r["used_for"]: self.assertIn(m, pins)

    def test_the_8x_shares_the_4x_export_masters_and_masks(self):
        four = {s["id"]: s for s in hp.load_recipe("world-4x")["steps"]}
        eight = {s["id"]: s for s in hp.load_recipe("world-8x")["steps"]}
        for sid in ("export", "masters-whole", "masters-tiled", "masks", "cells-layout"):
            self.assertEqual({k: v for k, v in eight[sid].items() if k != "shared_from"}, four[sid])

    def test_the_lists_hold_the_releases_counts(self):
        self.assertEqual(len(hp.globs_list("@lists/seams.txt")), 115)
        self.assertEqual(len(hp.globs_list("@lists/cutouts.txt")), 69)

class Steps(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory(); exp = os.path.join(self.d.name, "export"); os.makedirs(exp)
        for n in ("aa11ftl1", "aa11ftr1", "bb_1sign", "zswls101", "zsye"): open(os.path.join(exp, n + ".png"), "wb").close()
        self.exp = exp
    def tearDown(self): self.d.cleanup()

    def test_only_narrows_each_steps_own_textures(self):
        b = hp.Build(args(export=self.exp, only="aa11*,zswls*"), {"pack": "tex-world-4x", "steps": []})
        names, only = b.selection({"id": "x", "only": "zswls*,zhl45*"})
        self.assertEqual((names, only), (["zswls101"], "zswls101"))
        self.assertEqual(b.selection({"id": "y"})[0], ["aa11ftl1", "aa11ftr1", "zswls101"])
        self.assertEqual(b.selection({"id": "z", "only": "zsye"})[0], [])
        b2 = hp.Build(args(export=self.exp), {"pack": "tex-world-4x", "steps": []})
        self.assertEqual(b2.selection({"id": "x", "only": "zswls*"}), (None, "zswls*"))

    def test_placeholders_and_names_as_arguments(self):
        b = hp.Build(args(export=self.exp), {"pack": "tex-world-4x", "steps": []})
        masks = os.path.join(self.d.name, "masks"); os.makedirs(masks)
        for n in ("aa11ftl1.pgm", "aa11ftl1.detail.pgm", "x.txt"): open(os.path.join(masks, n), "wb").close()
        st = {"id": "k", "only": "aa11*", "args": ["key-edge", "{work}/f", "4", "{names:only}", "--masks", masks, "{pgm:" + masks + "}"]}
        out = b.subst(st, None, "aa11*")
        self.assertEqual(out[3:5], ["aa11ftl1", "aa11ftr1"])
        self.assertEqual(out[-2:], ["aa11ftl1", "aa11ftl1.detail"])
        self.assertTrue(out[1].endswith("/f") and os.path.isabs(out[1]))

    def test_a_steps_key_follows_what_it_reads(self):
        b = hp.Build(args(export=self.exp), {"pack": "tex-world-4x", "steps": []})
        st = {"id": "s", "tool": "upscale-textures.py", "args": ["a"], "reads": ["r"]}
        b.keys["r"] = "one"; k1 = b.key(st)
        b.keys["r"] = "two"; k2 = b.key(st)
        self.assertNotEqual(k1, k2)
        self.assertNotEqual(k1, b.key(dict(st, args=["b"])))
        self.assertEqual(k2, b.key(dict(st, note="words")), "the note is not part of the key")

class Menu(unittest.TestCase):
    def test_menu_runs_menu_pack_build_with_the_common_options_and_the_world_export(self):
        import contextlib, io
        with tempfile.TemporaryDirectory() as d:
            ex = os.path.join(d, "world-export"); os.makedirs(ex)
            open(os.path.join(ex, "bottom-up.txt"), "w").close()
            a = args(work=os.path.join(d, "w"), out=os.path.join(d, "o"), export=ex, only="shellbg30.webp", list_steps=False)
            r = json.load(open(os.path.join(hp.RECIPES, "menu.json"), encoding="utf-8"))
            out = io.StringIO()
            with contextlib.redirect_stdout(out): hp.delegate("menu", r, a)
            cmd = out.getvalue()
            self.assertIn("menu-pack.py build --install i", cmd)
            for want in ("--work " + os.path.join(d, "w", "menu"), "--out " + os.path.join(d, "o"), "--device cpu", "--models-dir m",
                         "--world-export " + ex, "--only shellbg30.webp"):
                self.assertIn(want, cmd)

class Finish(unittest.TestCase):
    def test_finish_writes_the_packs_papers(self):
        with tempfile.TemporaryDirectory() as d:
            open(os.path.join(d, "aa.dds"), "wb").write(b"x")
            with open(os.path.join(d, "pack.json"), "w") as f:
                json.dump({"text_redraws": [{"texture": "aa", "spec": "s", "fonts": ["Anton"]}], "text_fonts": [{"font": "Anton", "by": "b", "license": "OFL", "source": "u"}]}, f)
            r = hp.load_recipe("world-4x")
            hp.finish(d, r, "2026.01.02", [{"id": "base"}], "cpu", [{"texture": "fg", "spec": "p", "note": ""}])
            pj = json.load(open(os.path.join(d, "pack.json"), encoding="utf-8"))
            self.assertEqual(pj["version"], "2026.01.02"); self.assertEqual(len(pj["models"]), 6)
            self.assertTrue(pj["licence"].startswith("non-commercial")); self.assertEqual(pj["text_redraws"][0]["texture"], "aa")
            self.assertEqual(pj["fixes"][0]["texture"], "fg"); self.assertIn("numpy", pj["built_with"])
            at = open(os.path.join(d, "ATTRIBUTION.txt"), encoding="utf-8").read()
            self.assertIn("4x-UltraSharpV2 by Kim2091", at); self.assertIn("- Anton: b, OFL, u", at)
            sums = open(os.path.join(d, "FILES.sha256")).read().splitlines()
            self.assertEqual([l.split("  ")[1] for l in sums], ["ATTRIBUTION.txt", "pack.json", "aa.dds"])

@unittest.skipIf(etcpak is None, "etcpak not installed")
class Verify(unittest.TestCase):
    def test_the_classes(self):
        import dds_mips
        rng = np.random.default_rng(2)
        px = np.dstack([rng.integers(0, 256, (32, 32, 3)), np.full((32, 32), 255)]).astype(np.uint8)
        with tempfile.TemporaryDirectory() as d:
            a = os.path.join(d, "a.dds"); dds_mips.write_dds(a, px, etcpak); rel = open(a, "rb").read()
            self.assertEqual(hd_verify.compare(("a", a, rel))[1], "identical")
            b = os.path.join(d, "b.dds")
            with open(b, "wb") as f: f.write(rel[:-16] + bytes(16))      # the last (1x1) level other
            self.assertEqual(hd_verify.compare(("b", b, rel))[1], "top identical")
            near = px.copy(); near[0, 0, 0] ^= 1; c = os.path.join(d, "c.dds"); dds_mips.write_dds(c, near, etcpak)
            self.assertIn(hd_verify.compare(("c", c, rel))[1], ("same", "top identical", "identical"))
            far = 255 - px; far[..., 3] = 255; e = os.path.join(d, "e.dds"); dds_mips.write_dds(e, far, etcpak)
            self.assertEqual(hd_verify.compare(("e", e, rel))[1], "different")

class Release(unittest.TestCase):
    def test_the_same_pack_gives_the_same_archive(self):
        with tempfile.TemporaryDirectory() as d:
            pack = os.path.join(d, "tex-world-4x"); os.makedirs(os.path.join(pack, ".text-base"))
            for n, b in (("aa.dds", b"x" * 100), ("pack.json", b"{}"), ("ATTRIBUTION.txt", b"c"), (".text-base/aa.dds", b"y")):
                open(os.path.join(pack, n), "wb").write(b)
            out = []
            for k in range(2):
                o = os.path.join(d, f"r{k}"); hd_verify.release_main([o, pack, "--version", "2026.01.02"])
                out.append(open(os.path.join(o, "I26-HD-Textures-4x-2026.01.02.zip"), "rb").read())
            self.assertEqual(out[0], out[1])
            names = zipfile.ZipFile(os.path.join(d, "r0", "I26-HD-Textures-4x-2026.01.02.zip")).namelist()
            self.assertEqual(names, ["tex-world-4x/README.txt", "tex-world-4x/ATTRIBUTION.txt", "tex-world-4x/pack.json", "tex-world-4x/aa.dds", "tex-world-4x/FILES.sha256"])

if __name__ == "__main__":
    unittest.main()
