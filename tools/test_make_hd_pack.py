"""Tests of tools/make-hd-pack.py's argument handling and the commands it builds (python -m unittest tools/test_make_hd_pack.py,
or pytest tools/). Nothing is run: made-up program files in a temporary folder."""
import importlib.util, os, sys, tempfile, unittest

HERE = os.path.dirname(os.path.abspath(__file__))
_spec = importlib.util.spec_from_file_location("make_hd_pack", os.path.join(HERE, "make-hd-pack.py"))
mp = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(mp)

def touch(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "w").close()
    return path

class FindGameTests(unittest.TestCase):
    def test_file_folder_and_app(self):
        with tempfile.TemporaryDirectory() as d:
            exe = touch(os.path.join(d, "win", "i26.exe"))
            self.assertEqual(mp.find_game(exe), exe)
            self.assertEqual(os.path.normcase(mp.find_game(os.path.join(d, "win"))), os.path.normcase(exe))
            lin = touch(os.path.join(d, "lin", "i26.x86_64"))
            self.assertEqual(mp.find_game(os.path.join(d, "lin")), lin)
            mac = touch(os.path.join(d, "mac", "I26.app", "Contents", "MacOS", "I26"))
            self.assertEqual(mp.find_game(os.path.join(d, "mac", "I26.app")), mac)
            self.assertEqual(mp.find_game(os.path.join(d, "mac")), mac)
            self.assertIsNone(mp.find_game(os.path.join(d, "nowhere")))

class ParseTests(unittest.TestCase):
    def fails(self, argv):
        with self.assertRaises(SystemExit) as e, open(os.devnull, "w") as null:
            err, sys.stderr = sys.stderr, null
            try: mp.parse(argv)
            finally: sys.stderr = err
        self.assertEqual(e.exception.code, 2)

    def test_game_needed_only_without_an_export(self):
        with tempfile.TemporaryDirectory() as d:
            work = os.path.join(d, "work")
            self.fails(["--work", work])
            self.fails(["--work", work, "--game", os.path.join(d, "missing")])
            touch(os.path.join(work, "export", "bottom-up.txt"))
            self.assertIsNone(mp.parse(["--work", work]).game)

    def test_install_must_be_a_folder(self):
        with tempfile.TemporaryDirectory() as d:
            exe = touch(os.path.join(d, "i26.exe"))
            self.fails(["--game", exe, "--install", os.path.join(d, "no-such-install")])

    def test_device_choices(self):
        self.fails(["--dry-run", "--device", "mps"])

    def test_dry_run_needs_nothing(self):
        a = mp.parse(["--dry-run"])
        self.assertTrue(os.path.isabs(a.export))

class CommandTests(unittest.TestCase):
    def test_three_steps_with_their_options(self):
        with tempfile.TemporaryDirectory() as d:
            exe, inst = touch(os.path.join(d, "i26.exe")), os.path.join(d, "install")
            os.makedirs(inst)
            a = mp.parse(["--game", exe, "--install", inst, "--work", os.path.join(d, "w"), "--out", os.path.join(d, "pack"),
                          "--device", "cpu", "--only", "pp11*"])
            steps = [c for _, c in mp.commands(a)]
            self.assertEqual(len(steps), 3)
            game, up, rd = steps
            self.assertEqual(game[0], exe)
            self.assertEqual(game[game.index("--") + 1:], ["--install", inst, "--export-textures", a.export])
            self.assertIn("--windowed", game[:game.index("--")])
            self.assertTrue(up[1].endswith("upscale-textures.py") and up[2] == a.export)
            self.assertEqual(up[3:], ["--out", os.path.join(d, "pack"), "--device", "cpu", "--only", "pp11*"])
            self.assertTrue(rd[1].endswith("text-redraws.py") and rd[2] == a.export)
            self.assertEqual(rd[3:], ["--pack", os.path.join(d, "pack")])   # its --only takes one glob: it skips what the pack lacks

    def test_dry_run_names_the_program_in_a_folder(self):
        with tempfile.TemporaryDirectory() as d:
            exe = touch(os.path.join(d, "i26-play-26", "i26.x86_64"))
            a = mp.parse(["--dry-run", "--game", os.path.join(d, "i26-play-26"), "--work", os.path.join(d, "w")])
            self.assertEqual(mp.commands(a)[0][1][0], exe)
            self.assertEqual(mp.parse(["--dry-run", "--game", os.path.join(d, "none")]).game, os.path.join(d, "none"))

    def test_an_existing_export_is_not_made_again(self):
        with tempfile.TemporaryDirectory() as d:
            touch(os.path.join(d, "export", "bottom-up.txt"))
            steps = mp.commands(mp.parse(["--work", d]))
            self.assertEqual(len(steps), 2)
            self.assertNotIn("--out", steps[0][1])          # the game's own folder by default
            self.assertNotIn("--install", steps[0][1])

if __name__ == "__main__":
    unittest.main()
