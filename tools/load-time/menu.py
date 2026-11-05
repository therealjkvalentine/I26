#!/usr/bin/env python3
"""The menu side of the load-time survey (docs/perf/loadtime.md): the shell's start-up to the main menu's first frame, the 3D
garage's first opening (after the main menu's warm-up, and without it) and a mission launched from the shell (the click to
the card's first frame and to the mission), through LoadProfiler's lines. Same scratch user folder and rules as loadprofile.py.
  python menu.py OUT REPS [--cache warm|cold] [--pack 4x|off]
"""
import argparse, os, re, subprocess, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import loadprofile as lp

CASES = {
    # the garage opened 600 frames after the menu (the warm-up done) and at frame 2 (before its 45-frame delay)
    "garage-warmed": ({"I76_SHELL_INPUT": "garage:x@600"}, 25),
    "garage-cold": ({"I76_SHELL_INPUT": "garage:x@2"}, 20),
    # a mission from the shell: the launch hook hands T01 over as NEW TRIP's forms would, 300 frames after the menu
    "launch-t01": ({"I76_SHELL_INPUT": "launch:trip,t01,vppt01.vcf@300", "I76_LOAD_PROFILE_QUIT": "1"}, 90),
    # the main menu left standing 10 s: the menu warm-up's own line (its frames' longest, over 33 ms)
    "menu-idle": ({}, 12),
    # starts from the menu after its warm-up (frame 400): a melee (no briefing) and T01 with its briefing skipped after 3 s
    "launch-m01-idle": ({"I76_SHELL_INPUT": "launch:melee,m01,vppirna1.vcf@400", "I76_LOAD_PROFILE_QUIT": "1"}, 90),
    "launch-t01-brief": ({"I76_SHELL_INPUT": "launch:trip,t01,vppt01.vcf@400", "I76_LOAD_PROFILE_QUIT": "1", "I76_MOVIE_SKIP_MS": "3000", "+movies": "1"}, 100),
}


def run(out, case, rep, cache, pack, cfg=None, tag=""):
    env_over, secs = CASES[case]
    env_over = dict(env_over); movies = env_over.pop("+movies", None)
    root = os.path.join(out, "appdata-menu-" + cache)
    if cache == "cold":
        import shutil; shutil.rmtree(root, ignore_errors=True)
    d = lp.make_appdata(root, cache == "warm")
    over = dict(lp.PACKS[pack]); over.update(cfg or {})
    lp.write_cfg(d, over)
    env = dict(os.environ); env.update({"APPDATA": root, "I76_LOAD_PROFILE": "1"}); env.update(env_over)
    os.makedirs(os.path.join(out, "runs"), exist_ok=True)
    log = os.path.join(out, "runs", f"menu{tag}.{case}.{cache}.{pack}.{rep}.log")
    if "I76_LOAD_PROFILE_QUIT" in env_over: env["I76_LOAD_PROFILE_CSV"] = log.replace(".log", ".csv")
    u, p = lp.wait_idle(); load0 = lp.cpu_load()
    cmd = [lp.GODOT, "--path", lp.GAME, "--windowed", "--", "--install", lp.INSTALL, "--mute"] + ([] if movies else ["--skip-movies"])
    with open(log, "w", encoding="utf-8", errors="replace") as f:
        proc = subprocess.Popen(cmd, stdout=f, stderr=subprocess.STDOUT, env=env)
        try: proc.wait(timeout=secs)
        except subprocess.TimeoutExpired:
            subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True)
    text = open(log, encoding="utf-8", errors="replace").read()
    menu = re.search(r"main menu's first frame at (\d+) ms", text)
    garage = re.search(r"garage open to its first frame (\d+) ms", text)
    warm = re.search(r"garage warmed up in (\d+) ms", text)
    res = re.search(r"load-profile-result: (.*)", text)
    mw = re.search(r"menu warm-up: (.*)", text)
    row = [tag or "-", case, cache, pack, str(rep), str(u), str(load0), str(p), menu.group(1) if menu else "-", garage.group(1) if garage else "-",
           warm.group(1) if warm else "-", mw.group(1) if mw else "-", res.group(1)[:330] if res else "-"]
    open(os.path.join(out, "menu.tsv"), "a", encoding="utf-8").write("\t".join(row) + "\n")
    print("\t".join(row), flush=True)
    time.sleep(8)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("out"); ap.add_argument("reps", type=int)
    ap.add_argument("--cache", default="warm"); ap.add_argument("--pack", default="4x"); ap.add_argument("--cases", default=",".join(CASES))
    ap.add_argument("--ab-warmup", action="store_true", help="each case with [port] menu_warmup on and off, interleaved")
    a = ap.parse_args()
    for rep in range(1, a.reps + 1):
        for case in a.cases.split(","):
            if a.ab_warmup:
                run(a.out, case, rep, a.cache, a.pack, {"menu_warmup": '"on"'}, ".mwon")
                run(a.out, case, rep, a.cache, a.pack, {"menu_warmup": '"off"'}, ".mwoff")
            else: run(a.out, case, rep, a.cache, a.pack)
