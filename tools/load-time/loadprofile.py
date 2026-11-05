#!/usr/bin/env python3
"""Load-time survey runner (Windows): a mission start through the game's LoadProfiler (I76_LOAD_PROFILE=1; docs/perf/loadtime.md
says how the survey used it), one configuration at a time, reps interleaved, in a scratch user folder.

  python loadprofile.py OUT REPS [--missions t01,m01,t05] [--packs 4x,off] [--caches warm,cold] [--tag before] [--briefing]
                        [--shell] [--env VAR=VAL ...] [--ab TAG=GAMEDIR,...]

Needs GODOT (the Godot .NET console executable), I76_INSTALL, a built game/ (dotnet build) of this worktree.
- warm: one user folder kept between runs (OUT/appdata-warm), its bake cache, shader cache and pipeline cache copied once from the
  live user folder (%APPDATA%/I26: read, never written) and kept warm by the runs; a first run of each mission and pack (rep 0)
  warms it and is kept apart.
- cold: a fresh user folder for every run: no bake cache, no shader cache, no pipeline cache. The Windows file cache is not dropped
  (no reboot): the pack's and the install's files are as warm as the previous runs left them.
Both: shell.cfg written from BASE (the frame-cost survey's everything-on baseline) plus the pack, the tex-world-Nx and tex-menu
folders junctioned in from the live folder (nothing copied). Before a run: the GPU under 10 % with no other Godot process (the
frame-cost survey's rule); during it, other Godot/dotnet/python/86Box processes and the CPU load are sampled every 2 s and kept
with the row (a run another Godot overlapped is marked contaminated and done again).
Rows go to OUT/results.tsv; each run leaves OUT/runs/NAME.{log,csv}.
--briefing: the briefing plays (no --skip-movies; I76_MOVIE_SKIP_AFTER may cut it); --shell: the start goes through the shell
(I76_SHELL_INPUT launch:...), so the click, the shell's start-up and the scene reload are in the profile.
"""
import argparse, datetime, os, re, shutil, statistics, subprocess, sys, threading, time

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.normpath(os.path.join(HERE, "..", "..", "game"))
GODOT = os.environ.get("GODOT", "godot")
INSTALL = os.environ.get("I76_INSTALL", "")
LIVE = os.path.join(os.environ["APPDATA"], "I26")

BASE_GRAPHICS = [5, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 3, 2, 1, 1]
BASE = {
    "vegetation": 2, "vegetation_density": 60, "draw_distance": 1, "map_edge": '"on"', "checkpoints": "false",
    "force_feedback": "true", "lfe_level": 100, "sim_rate": 24, "palette_grade": "true", "crash_blur": '"on"',
    "mouse_camera": 2, "max_fps": 0, "vsync": "false", "developer": "true", "hd_menu_art": "true", "hd_textures": "true",
    "hd_texture_size": 4, "effects_size": 100, "effects_brightness": 100, "effects_opacity": 100, "mirror_range": '"full"',
    "brake_lamps": '"held"', "mg_tracers": '"bolts"', "hood_finish": '"gloss"', "death_variety": '"on"', "brush_impacts": '"on"',
    "lighting_calibration": '"on"', "dash_3d": "true", "car_interiors": "true", "dev_fps": "false", "resolution": '"1920x1080"',
}
PACKS = {"4x": {"hd_textures": "true", "hd_texture_size": 4}, "off": {"hd_textures": "false"}}
MISSIONS = {
    "t01": ["--mission", "t01", "--drive", "--seed", "1", "--skip-cutscenes"],
    "t05": ["--mission", "t05", "--drive", "--seed", "1", "--skip-cutscenes"],
    "m01": ["--melee", "--mission", "m01", "--drive", "--seed", "1"],
}
LINKS = ["tex-world", "tex-world-2x", "tex-world-3x", "tex-world-4x", "tex-menu"]


def gpu_util():
    try:
        return int(subprocess.check_output(["nvidia-smi", "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits"], text=True).split()[0])
    except Exception:
        return -1


def procs():
    """Other processes that share the machine: Godot, dotnet, python, 86Box, ffmpeg (counts by name)."""
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True).stdout
    n = {}
    for l in out.splitlines():
        name = l.split(",")[0].strip('"').lower()
        for k in ("godot", "dotnet", "python", "86box", "ffmpeg", "msbuild", "vbcscompiler"):
            if name.startswith(k): n[k] = n.get(k, 0) + 1
    return n


def cpu_load():
    try:
        o = subprocess.check_output(["wmic", "cpu", "get", "loadpercentage", "/value"], text=True, stderr=subprocess.DEVNULL)
        return int(re.search(r"=(\d+)", o).group(1))
    except Exception:
        return -1


def wait_idle():
    t0 = time.time()
    while True:
        u, p = gpu_util(), procs()
        if 0 <= u < 10 and p.get("godot", 0) == 0: return u, p
        if time.time() - t0 > 1800: return u, p
        time.sleep(5)


def junction(link, target):
    if os.path.exists(link): return
    subprocess.run(["cmd", "/c", "mklink", "/J", link, target], capture_output=True)


def make_appdata(root, warm):
    d = os.path.join(root, "I26")
    os.makedirs(d, exist_ok=True)
    for name in LINKS:
        if os.path.isdir(os.path.join(LIVE, name)): junction(os.path.join(d, name), os.path.join(LIVE, name))
    for f in ("build-rules.cfg", "input.map", "input.rules", "gamekeys.cfg", "joystick.cfg"):
        if os.path.isfile(os.path.join(LIVE, f)) and not os.path.exists(os.path.join(d, f)): shutil.copy2(os.path.join(LIVE, f), d)
    if warm:
        for name in ("cache", "shader_cache", "vulkan"):
            if not os.path.exists(os.path.join(d, name)) and os.path.isdir(os.path.join(LIVE, name)):
                shutil.copytree(os.path.join(LIVE, name), os.path.join(d, name))
    return d


def write_cfg(d, over):
    port = dict(BASE); port.update(over)
    lines = ["[shell]", "", "version=13", "graphics=PackedByteArray(" + ", ".join(map(str, BASE_GRAPHICS)) + ")",
             "audio=PackedByteArray(8, 8, 8, 8)", "play=0", "difficulty=0", "", "[port]", ""] + [f"{k}={v}" for k, v in port.items()]
    open(os.path.join(d, "shell.cfg"), "w", encoding="utf-8").write("\n".join(lines) + "\n")


def run_once(out, name, mission, pack, cache, rep, tag, briefing, shell, env_over, cfg_over, game=GAME):
    runs = os.path.join(out, "runs"); os.makedirs(runs, exist_ok=True)
    stem = os.path.join(runs, name)
    if cache == "cold":
        root = os.path.join(out, "appdata-cold")
        shutil.rmtree(root, ignore_errors=True)   # (the junctions go with it, not their targets: rmtree does not follow them)
    else:
        root = os.path.join(out, "appdata-warm")
    d = make_appdata(root, cache == "warm")
    over = dict(PACKS[pack]); over.update(cfg_over)
    write_cfg(d, over)
    args = list(MISSIONS[mission])
    if not briefing: args.append("--skip-movies")
    env = dict(os.environ)
    env.update({"APPDATA": root, "I76_LOAD_PROFILE": "1", "I76_LOAD_PROFILE_QUIT": "1", "I76_LOAD_PROFILE_CSV": stem + ".csv"})
    if shell:
        # the start through the shell: the menu, then the mission handed over as the forms would (the click is the clock's zero)
        kind = "melee" if mission.startswith("m") else "trip"
        args = []
        env["I76_SHELL_INPUT"] = f"launch:{kind},{mission},vppirna1.vcf@90"
        if not briefing: args.append("--skip-movies")
    env.update(env_over)
    u, p = wait_idle()
    load0 = cpu_load()
    t0 = time.time()
    seen = {"godot": 0, "dotnet": 0, "python": 0, "86box": 0}; loads = []; stop = threading.Event()

    def sample():
        while not stop.wait(2):
            q = procs()
            for k in seen: seen[k] = max(seen[k], q.get(k, 0))
            loads.append(cpu_load())
    th = threading.Thread(target=sample, daemon=True); th.start()
    cmd = [GODOT, "--path", game, "--", "--install", INSTALL, "--mute"] + args
    with open(stem + ".log", "w", encoding="utf-8", errors="replace") as log:
        log.write("# " + " ".join(cmd) + "\n# env: " + " ".join(f"{k}={v}" for k, v in sorted(env_over.items())) + f" APPDATA={root}\n")
        log.flush()
        try:
            code = subprocess.run(cmd, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=150).returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
            subprocess.run(["taskkill", "/IM", os.path.basename(GODOT), "/F"], capture_output=True)
    stop.set(); th.join()
    res = ""
    for l in open(stem + ".log", encoding="utf-8", errors="replace"):
        if l.startswith("load-profile-result:"): res = l.strip()[len("load-profile-result:"):].strip()
    others = max(0, seen["godot"] - 2)   # ours are two processes: the console wrapper and the engine
    row = [datetime.datetime.now().isoformat(timespec="seconds"), tag, mission, pack, cache, str(rep), str(u), str(load0),
           f"{statistics.median(loads):.0f}" if loads else "-", f"godot+{others} dotnet{seen['dotnet']} python{seen['python']} 86box{seen['86box']}",
           f"{time.time() - t0:.0f}", str(code), res or "FAILED"]
    with open(os.path.join(out, "results.tsv"), "a", encoding="utf-8") as f: f.write("\t".join(row) + "\n")
    print("\t".join(row[1:6] + [row[9], row[12][:150]]), flush=True)
    time.sleep(10)   # a gap for the other lanes' idle checks
    return res, others


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out"); ap.add_argument("reps", type=int)
    ap.add_argument("--missions", default="t01,m01,t05"); ap.add_argument("--packs", default="4x,off"); ap.add_argument("--caches", default="warm,cold")
    ap.add_argument("--tag", default="run"); ap.add_argument("--briefing", action="store_true"); ap.add_argument("--shell", action="store_true")
    ap.add_argument("--env", nargs="*", default=[]); ap.add_argument("--cfg", nargs="*", default=[])
    ap.add_argument("--no-warmup", action="store_true")
    ap.add_argument("--ab", default="", help="TAG=GAMEDIR,TAG=GAMEDIR: builds compared run for run (interleaved), each its own tag")
    ap.add_argument("--budget-min", type=float, default=18.0, help="stop after this many minutes (the GPU lock's batch)")
    a = ap.parse_args()
    env = dict(kv.split("=", 1) for kv in a.env); cfg = dict(kv.split("=", 1) for kv in a.cfg)
    t_start = time.time()
    first = 0 if not a.no_warmup else 1
    for rep in range(first, a.reps + 1):
        for cache in a.caches.split(","):
            if rep == 0 and cache == "cold": continue   # a cold run needs no warm-up
            for mission in a.missions.split(","):
                for pack in a.packs.split(","):
                    if (time.time() - t_start) / 60 > a.budget_min: print("budget over", flush=True); return
                    builds = [kv.split("=", 1) for kv in a.ab.split(",")] if a.ab else [(a.tag, GAME)]
                    for tag, game in builds:
                        name = f"{tag}.{mission}.{pack}.{cache}.{rep}"
                        for attempt in range(2):
                            res, others = run_once(a.out, name + ("" if attempt == 0 else "b"), mission, pack, cache, rep, tag, a.briefing, a.shell, env, cfg, game)
                            if res and others == 0: break


if __name__ == "__main__":
    main()
