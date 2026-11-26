"""Tests for tools/quiet_run.py's checks (the run itself needs Windows and the game): python -m unittest tools/test_quiet_run.py, or pytest tools/"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import unittest
import quiet_run as q

LIVE = os.path.abspath(os.path.join(os.sep, "users", "p", "AppData", "Roaming"))
TEMP = os.path.abspath(os.path.join(os.sep, "tmp"))


class QuietRunTests(unittest.TestCase):
    def test_dummy_audio_is_added_before_the_double_dash(self):
        assert q.fix_args(["--path", "game", "--", "--mission", "t01"]) == ["--audio-driver", "Dummy", "--path", "game", "--", "--mission", "t01"]
        assert q.fix_args(["--path", "game", "--audio-driver", "Dummy"]) == ["--path", "game", "--audio-driver", "Dummy"]
        assert q.fix_args(["--audio-driver=Dummy"]) == ["--audio-driver=Dummy"]

    def test_another_audio_driver_is_refused(self):
        with unittest.TestCase().assertRaises(q.Refused):
            q.fix_args(["--audio-driver", "WASAPI", "--path", "game"])

    def test_a_game_argument_is_not_the_engines(self):
        assert q.fix_args(["--path", "game", "--", "--audio-driver", "x"])[:2] == ["--audio-driver", "Dummy"]

    def test_the_scratch_user_folder(self):
        assert q.choose_appdata("/s/appdata", LIVE, LIVE, TEMP) == os.path.abspath("/s/appdata")
        assert q.choose_appdata(None, "/s/other", LIVE, TEMP) == os.path.abspath("/s/other")        # the caller's own scratch APPDATA
        assert q.choose_appdata(None, LIVE, LIVE, TEMP) == os.path.join(TEMP, "i26-quiet", "appdata")   # the live one is never used
        assert q.choose_appdata(None, None, LIVE, TEMP) == os.path.join(TEMP, "i26-quiet", "appdata")

    def test_the_live_folder_is_refused(self):
        with unittest.TestCase().assertRaises(q.Refused):
            q.choose_appdata(LIVE, None, LIVE, TEMP)
        with unittest.TestCase().assertRaises(q.Refused):
            q.choose_appdata(os.path.join(LIVE, "I26"), None, LIVE, TEMP)

    def test_the_quiet_window_is_always_on(self):
        assert q.fix_env({}, "/a")["I76_QUIET_WINDOW"] == "1"
        assert q.fix_env({"I76_QUIET_WINDOW": "0"}, "/a")["I76_QUIET_WINDOW"] == "1"
        assert q.fix_env({"I76_QUIET_WINDOW": "min"}, "/a")["I76_QUIET_WINDOW"] == "min"
        assert q.fix_env({}, "/a")["APPDATA"] == "/a"


if __name__ == "__main__":
    unittest.main()
