#!/usr/bin/env python3
"""Frame-cost runner (Windows): one configuration of the game in a fixed scene, several times, through FrameProfiler's
timed window (I76_FRAME_PROFILE_SKIP_MS / _RUN_MS / _CSV / _SHOT; docs/perf/costs.md says how the survey used it).

  python bench.py NAME --res 1920x1080 [--reps 3] [--cfg key=value ...] [--graphics 0x6=1 ...] [--env VAR=VAL ...]
                       [--scene cockpit|chase|fire|drive|melee] [--1997] [--skip-ms 8000] [--run-ms 10000] [--args ...]

Needs: GODOT (the Godot .NET console executable; default `godot` on PATH), I76_INSTALL (the install), a built game/ (dotnet
build). Output goes under FRAME_COST_OUT (default: frame-cost/ in the user's temp folder): NAME.RES.N.{log,csv,png} and a row
in results.tsv (end time, config, res, rep, scene, the GPU's state before the run, wall s, exit code, other GPU users seen,
the profile's result line, start and end times). The game runs in an isolated user folder under the output folder (the
APPDATA override: Godot's user:// on Windows): make it once with FRAME_COST_APPDATA pointing at a folder holding I26/ with
junctions to the live user folder's tex-world-Nx/, tex-menu/, cache/ and copies of shader_cache/ and build-rules.cfg
(nothing is written to the player's data). The game starts in the quiet window (I76_QUIET_WINDOW=1 unless the caller's environment or
--env sets it; 0 is the old borderless fullscreen in front): a window of exactly --res behind the others, a desktop-sized one borderless
at the screen's corner, so a run never takes the screen or the keyboard from someone at the desktop. shell.cfg is written from BASE plus --cfg before each run. Before a run it
waits for the GPU to be under 10 % with no other Godot or compute process, samples other GPU users every 2 s during the run
(the row's `others` column: rerun a row that is not 0), and leaves GAP_S idle after each run for other lanes' idle checks.

FRAME_COST_PROFILE=owner is a playtest's settings (OWNER); FRAME_COST_PROFILE=file takes a player's own shell.cfg (FRAME_COST_SHELL_CFG)
as the base, VSync and Max FPS included; --scene t06 drives off from T06's start by night; --scene t05 drives T05's busy parts (T05_SECTIONS) for 170 s
(--skip-ms 2000 --run-ms 168000). FRAME_COST_GAME runs another checkout's game/ (a before build), FRAME_COST_GPU_PROFILE=0 leaves out
--gpu-profile (its table, printed once a second, is a stutter of its own: no gpu.* columns then), FRAME_COST_IGNORE_PIDS lists
compute processes that sit idle while their lane waits for the GPU lock. FRAME_COST_CPU_IDLE=N waits before a run until the machine's
processor load is N % or less (three samples 5 s apart: another lane's build takes the main thread's cores and the frame with them), and
FRAME_COST_CPU_MAX=N runs a row again (at most three times) whose median load during the run was over N %. A run whose player car is destroyed ends there (the game quits at the death, printing its partial
result): its row's result starts with DIED and it is not run again. FRAME_COST_PRIORITY=above
starts the game above normal priority, so the processes of the same priority other lanes leave running (encoders, builds) give way to its
main thread as they would to a game on a player's machine.
"""
import argparse, os, re, subprocess, sys, time, datetime, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("FRAME_COST_OUT") or os.path.join(os.environ.get("TEMP", HERE), "frame-cost")
GODOT = os.environ.get("GODOT", "godot")
GAME = os.environ.get("FRAME_COST_GAME") or os.path.normpath(os.path.join(HERE, "..", "..", "game"))   # FRAME_COST_GAME: another checkout's game/ (a before build)
INSTALL = os.environ.get("I76_INSTALL", "")
APPDATA = os.environ.get("FRAME_COST_APPDATA") or os.path.join(OUT, "appdata")
CFG = os.path.join(APPDATA, "I26", "shell.cfg")
QUIET = os.environ.get("I76_QUIET_WINDOW", "1")   # the game's window behind the others by default (docs/dev-hooks.md); 0: fullscreen in front
GAP_S = 20   # idle seconds left after each run for the other lanes' idle checks

# Everything on: the modern look with every port feature at its costliest offered value.
BASE_GRAPHICS = [5, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 3, 2, 1, 1]   # +0x2 Terrain High, +0x6 Shadow High (SSAO), +0x8 MSAA 4x
BASE = {
    "vegetation": 2, "vegetation_density": 60, "draw_distance": 1, "map_edge": '"on"', "checkpoints": "false",
    "force_feedback": "true", "lfe_level": 100, "sim_rate": 24, "palette_grade": "true", "crash_blur": '"on"',
    "mouse_camera": 2, "max_fps": 0, "vsync": "false", "developer": "true", "hd_menu_art": "true", "hd_textures": "true",
    "hd_texture_size": 4, "effects_size": 100, "effects_brightness": 100, "effects_opacity": 100, "mirror_range": '"full"',
    "brake_lamps": '"held"', "mg_tracers": '"bolts"', "hood_finish": '"gloss"', "brush_impacts": '"on"',
    "lighting_calibration": '"on"', "dash_3d": "true", "car_interiors": "true", "dev_fps": "false",
    "ambient_occlusion": "true",   # its own row since perf/graphics-defaults (Shadow Detail High's before)
}
# A first start's settings (the `defaults` profile): no [port] key but the measuring ones, the Graphic Detail block as a fresh one
# writes it. DEFAULT_GRAPHICS is this branch's (Anti-Aliasing 2x); OLD_DEFAULT_GRAPHICS the block before it (4x).
DEFAULT_GRAPHICS = [5, 1, 0, 1, 1, 1, 1, 2, 1, 1, 1, 1, 3, 2, 1, 1]
OLD_DEFAULT_GRAPHICS = [5, 1, 0, 1, 1, 1, 1, 2, 2, 1, 1, 1, 3, 2, 1, 1]
DEFAULTS = {"vsync": "false", "max_fps": 0, "hd_menu_art": "true", "hd_textures": "true", "hd_texture_size": 4}
# The modern look as it drew before perf/graphics-defaults, through this branch's hooks: the mirror every frame through the world's
# environment (SSAO and all), the terrain shader's one tier, four shadow splits to 1500 m whatever Shadow Detail says, and the
# distant ring as upstream's one pass writing depth (I76_HORIZON_SPLIT=0; play 74d7ab5 has it).
OLD_RENDER_ENV = {"I76_HORIZON_SPLIT": "0", "I76_MIRROR_RATE": "1", "I76_MIRROR_SSAO": "1", "I76_TERRAIN_TIERS": "0", "I76_SHADOW_SPLITS": "4", "I76_SHADOW_DIST": "1500"}
# The owner's playtest settings (docs/perf/plan.md, "T05 at 60 fps"): the modern look, Terrain and Shadow High, MSAA 4x, the world's
# textures 4x, the cars' 8x, vegetation Medium, the 20 km view, the mirror's full reach; no Ambient Occlusion (its own row, off).
OWNER = {
    "vegetation": 1, "draw_distance": 1, "checkpoints": "false", "force_feedback": "false", "lfe_level": 200, "sim_rate": 24,
    "crash_blur": '"off"', "mouse_camera": 2, "max_fps": 0, "vsync": "false", "developer": "true", "dev_fps": "true",
    "hd_menu_art": "true", "hd_textures": "true", "hd_dash_size": 4, "hd_car_size": 8, "effects_size": 80,
    "effects_brightness": 70, "effects_opacity": 70, "mirror_range": '"full"', "brake_lamps": '"original"', "mg_tracers": '"original"',
}
PROFILE = os.environ.get("FRAME_COST_PROFILE", "full")   # full (everything on) | defaults | old_defaults | owner | file
# FRAME_COST_PROFILE=file: a player's own shell.cfg (FRAME_COST_SHELL_CFG) as the base, its Graphic Detail block and [port] keys as
# written (VSync and Max FPS too: pass --cfg vsync=false to measure past the refresh); --cfg and --graphics change it as for the others
SHELL_CFG = os.environ.get("FRAME_COST_SHELL_CFG", "")

def read_shell_cfg(path):
    graphics, port, section = None, {}, ""
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line.startswith("["): section = line; continue
            if "=" not in line: continue
            k, v = line.split("=", 1)
            if section == "[shell]" and k == "graphics": graphics = [int(x) for x in re.findall(r"\d+", v)]
            elif section == "[port]": port[k] = v
    return graphics, port
SCENES = {
    # the T01 start, the player's car standing with its engine running while the script's cars play the opening; seed 1
    "cockpit": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes"],
    "chase": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--chase"],
    "fire": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-fire"],
    "drive": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-throttle", "--chase"],
    # T05's busy parts in one run (T05_SECTIONS), the car driven along the roads by I76_TEST_ROUTE, moved between them by
    # I76_SPAWN_AT's timed stops, firing at the nearest car; Play Options' Unlim. Ammo, Armor and Chas. Rein. keep it alive
    "t05": ["--mission", "t05", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-fire"],
    # T06 by night from its start, the cockpit, the car driven off down the road (the headlamps, the night terrain shader)
    "t06": ["--mission", "t06", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-throttle"],
}
# T05's sections: (name, mission second it starts, the teleport there or None, the road points driven at 15 m/s). The start and
# the first car on the road east (the first attacker comes at ~26 s), the mountain pass west over the ridge (two cars hide on it),
# the four-car group north of the junction, Skeeter at Fisher's Field.
T05_SECTIONS = [
    ("start", 0, None, "8083,44520 8240,44520 8411,44520 8586,44520 8756,44520 8910,44520 9050,44520 9186,44520 9305,44541 9417,44588 9525,44643 9633,44699 9744,44750 9858,44793 9975,44828"),
    ("pass", 45, "8945,44070,-90", "8905,44070 8860,44062 8825,44039 8800,44005 8791,43965 8786,43924 8777,43883 8753,43850 8715,43840 8677,43855 8653,43889 8638,43927 8623,43966 8606,44004 8577,44032 8538,44029 8515,43997 8481,43975 8442,43983 8411,44009 8377,44032 8338,44031 8318,43996 8304,43958 8268,43946 8240,43973 8230,44012 8223,44053 8204,44089 8166,44099 8132,44080 8115,44043 8106,44004"),
    ("group", 105, "10470,45063,0", "10470,45109 10470,45202 10470,45294 10461,45417 10433,45493 10397,45566 10373,45643 10362,45724 10362,45806 10371,45894 10380,45990 10530,45965 10690,45930"),
    ("field", 145, "6455,43700,180", "6449,43687 6446,43647 6445,43606 6447,43565 6449,43525 6452,43484 6455,43444 6455,43405 6455,43365 6455,43325 6455,43285 6455,43245"),
]
T05_END_S = 170
SCENE_ENV = {
    "t05": {
        "I76_SPAWN_AT": ";".join(f"{at}@{t}" for _, t, at, _ in T05_SECTIONS if at),
        "I76_TEST_ROUTE": "15;" + " ".join(pts for _, _, _, pts in T05_SECTIONS),
        "I76_AUTO_TARGET": "1",
    },
}
SCENE_PLAY = {"t05": 0x1C}   # [shell] play: Unlim. Ammo (0x04), Armor (0x08), Chas. Rein. (0x10)

def gpu_state():
    try:
        out = subprocess.check_output(["nvidia-smi", "--query-gpu=utilization.gpu,memory.used,power.draw,temperature.gpu", "--format=csv,noheader,nounits"], text=True)
        u, m, p, t = [x.strip() for x in out.strip().split(",")]
        return int(u), int(m), float(p), int(t)
    except Exception:
        return -1, -1, -1.0, -1

def other_godot():
    """Other GPU users: Godot processes (by name) and pure-compute apps (nvidia-smi's type C: CUDA upscalers, encoders), as 'name:pid'."""
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq Godot*"], capture_output=True, text=True).stdout
    found = ["godot:" + l.split()[1] for l in out.splitlines() if l.lower().startswith("godot")]
    try:
        import re
        smi = subprocess.check_output(["nvidia-smi"], text=True, stderr=subprocess.DEVNULL)
        # FRAME_COST_IGNORE_PIDS: compute processes known to sit idle while another lane holds the GPU lock (their context, no work)
        idle = set(os.environ.get("FRAME_COST_IGNORE_PIDS", "").split(","))
        found += ["compute:" + m.group(1) for m in re.finditer(r"^\|\s+\d+\s+\S+\s+\S+\s+(\d+)\s+C\s+", smi, re.M) if m.group(1) not in idle]
    except Exception:
        pass
    return found

def cpu_load():
    try:
        o = subprocess.check_output(["wmic", "cpu", "get", "loadpercentage", "/value"], text=True, stderr=subprocess.DEVNULL)
        return int(o.strip().split("=")[1])
    except Exception:
        return -1

def wait_cpu(limit=3600):
    """FRAME_COST_CPU_IDLE: the processor's load at or under it in three samples in a row, 5 s apart (or the limit passes)."""
    want = int(os.environ.get("FRAME_COST_CPU_IDLE", "0") or 0)
    if want <= 0: return
    t0 = time.time(); ok = 0; said = 0
    while ok < 3 and time.time() - t0 < limit:
        ok = ok + 1 if 0 <= cpu_load() <= want else 0
        if ok < 3: time.sleep(5)
        if time.time() - t0 > said + 120:
            said = time.time() - t0
            print(f"waiting: processor over {want} %, {said:.0f} s", flush=True)

def wait_idle(limit=4 * 3600):
    t0 = time.time(); said = 0
    while True:
        u, m, p, t = gpu_state()
        others = other_godot()
        if 0 <= u < 10 and not others:
            return u, m, p, t
        if time.time() - t0 > limit:
            print(f"warning: GPU busy ({u} %, {others}) after {limit} s; running anyway", flush=True)
            return u, m, p, t
        if time.time() - t0 > said + 120:
            said = time.time() - t0
            print(f"waiting: GPU {u} %, {len(others)} Godot process(es), {said:.0f} s", flush=True)
        time.sleep(5)

def write_cfg(over, graphics_over):
    profile = over.get("__profile", PROFILE)
    g = list({"defaults": DEFAULT_GRAPHICS, "old_defaults": OLD_DEFAULT_GRAPHICS}.get(profile, BASE_GRAPHICS))
    if profile == "file": g, file_port = read_shell_cfg(SHELL_CFG)
    for k, v in graphics_over.items():
        g[int(k, 0)] = int(v)
    port = dict(file_port if profile == "file" else DEFAULTS if profile in ("defaults", "old_defaults") else OWNER if profile == "owner" else BASE)
    port.update({k: v for k, v in over.items() if not k.startswith("__")})
    # version 14: an older file has Terrain Detail Low raised to High as it loads (ShellSettings.RaiseOldDetailDefaults; Shadow Detail is kept)
    lines = ["[shell]", "", "version=14", "graphics=PackedByteArray(" + ", ".join(map(str, g)) + ")", "audio=PackedByteArray(8, 8, 8, 8)", f"play={over.get('__play', 0)}", "difficulty=0", "", "[port]", ""]
    lines += [f"{k}={v}" for k, v in port.items()]
    with open(CFG, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")

def run_once(name, res, rep, scene, cfg_over, graphics_over, env_over, extra, skip_ms, run_ms, look1997, timeout):
    os.makedirs(OUT, exist_ok=True)
    stem = os.path.join(OUT, f"{name}.{res}.{scene}.{rep}")   # the scene in the name: the sets share configuration names
    write_cfg(cfg_over, graphics_over)
    # the window: the game's own Resolution setting (a centred window of that size; empty is the desktop's borderless fullscreen)
    cfg_over = dict(cfg_over)
    cfg_over.setdefault("resolution", '""' if res == "3440x1440" else f'"{res}"')
    if scene in SCENE_PLAY: cfg_over.setdefault("__play", SCENE_PLAY[scene])
    write_cfg(cfg_over, graphics_over)
    # --gpu-profile gives the passes' GPU times, and prints a table a second; FRAME_COST_GPU_PROFILE=0 leaves it out (no gpu.* columns)
    engine = [GODOT, "--path", GAME] + ([] if os.environ.get("FRAME_COST_GPU_PROFILE") == "0" else ["--gpu-profile"])
    # the quiet window (I76_QUIET_WINDOW, docs/dev-hooks.md), the default: a window of exactly RES behind the others, never the foreground
    # (a desktop-sized one is borderless at the screen's corner, drawn at full cost); I76_QUIET_WINDOW=0 for the old start in front
    quiet = env_over.get("I76_QUIET_WINDOW", QUIET)
    if quiet not in ("", "0"):
        engine += ["--windowed", "--resolution", res]
    game = ["--install", INSTALL, "--mute"] + SCENES[scene] + (["--1997"] if look1997 else []) + extra
    env = dict(os.environ)
    env["APPDATA"] = APPDATA
    env["I76_FRAME_PROFILE"] = "1"
    env["I76_FRAME_PROFILE_SKIP"] = "30"
    env["I76_FRAME_PROFILE_SKIP_MS"] = str(skip_ms)
    env["I76_FRAME_PROFILE_RUN_MS"] = str(run_ms)
    env["I76_FRAME_PROFILE_EVERY"] = "100000"
    env["I76_FRAME_PROFILE_CSV"] = stem + ".csv"
    env["I76_FRAME_PROFILE_SHOT"] = stem + ".png"
    env["I76_HITCH_MS"] = "100000"
    env["I76_QUIET_WINDOW"] = quiet
    env.update(SCENE_ENV.get(scene, {}))
    env.update(env_over)
    wait_cpu()
    u, m, p, t = wait_idle()
    t0 = time.time()
    # other lanes' Godot runs during ours: sampled every 2 s (ours are two processes: the console wrapper and the engine)
    import threading
    others_max = [0]; stop = threading.Event(); clocks = []; watts = []; cpu_loads = []
    def sample():
        while not stop.wait(2):
            n = len(other_godot()) - 2
            if n > others_max[0]: others_max[0] = n
            try:   # the GPU's clock and power while we run (the 180 W cap can pull the clock down as the card warms)
                o = subprocess.check_output(["nvidia-smi", "--query-gpu=clocks.sm,power.draw,utilization.gpu", "--format=csv,noheader,nounits"], text=True).strip().split(",")
                if int(o[2]) > 50: clocks.append(int(o[0])); watts.append(float(o[1]))
            except Exception:
                pass
            try:   # the machine's CPU load while we run (other lanes' builds and captures share it; our frame is ~1 core)
                o = subprocess.check_output(["wmic", "cpu", "get", "loadpercentage", "/value"], text=True, stderr=subprocess.DEVNULL)
                cpu_loads.append(int(o.strip().split("=")[1]))
            except Exception:
                pass
    th = threading.Thread(target=sample, daemon=True); th.start()
    with open(stem + ".log", "w", encoding="utf-8", errors="replace") as log:
        log.write("# " + " ".join(engine + ["--"] + game) + "\n# env: " + " ".join(f"{k}={v}" for k, v in sorted(env_over.items()))
                  + "\n# cfg: " + " ".join(f"{k}={v}" for k, v in sorted(cfg_over.items())) + " graphics " + " ".join(f"{k}={v}" for k, v in sorted(graphics_over.items())) + "\n")
        log.flush()
        try:
            flags = getattr(subprocess, "ABOVE_NORMAL_PRIORITY_CLASS", 0) if os.environ.get("FRAME_COST_PRIORITY") == "above" else 0
            proc = subprocess.run(engine + ["--"] + game, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=timeout, creationflags=flags)
            code = proc.returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
            subprocess.run(["taskkill", "/IM", os.path.basename(GODOT), "/F"], capture_output=True)
    stop.set(); th.join()
    wall = time.time() - t0
    res_line = ""; died = False
    with open(stem + ".log", encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("frame-profile-result:"):
                res_line = line.strip()[len("frame-profile-result:"):].strip()
            elif line.startswith("frame-profile-death:"):
                died = True
    # the player's car destroyed: the game ended the window there (FrameProfiler.PlayerDied); a partial result, not run again
    if died: res_line = "DIED " + res_line
    if res_line and clocks:
        import statistics
        res_line += f" gpu_mhz={statistics.median(clocks):.0f} gpu_w={statistics.median(watts):.0f}"
        if cpu_loads: res_line += f" cpu_load={statistics.median(cpu_loads):.0f}"
    # the row: end time, config, res, rep, scene, GPU util / MiB / W / C before the run, wall s, exit code, other Godot processes seen,
    # the profile's result line, then the run's start and end (local time, seconds)
    t_end = datetime.datetime.now()
    row = [t_end.isoformat(timespec="seconds"), name, res, str(rep), scene, str(u), str(m), f"{p:.0f}", str(t), f"{wall:.0f}", str(code), str(others_max[0]), res_line or "FAILED",
           datetime.datetime.fromtimestamp(t0).strftime("%H:%M:%S"), t_end.strftime("%H:%M:%S")]
    with open(os.path.join(OUT, "results.tsv"), "a", encoding="utf-8") as f:
        f.write("\t".join(row) + "\n")
    time.sleep(GAP_S)   # a clear gap after every run, so other lanes' idle checks can pass between ours
    print("\t".join(row[1:5] + [row[-1][:160]]), flush=True)
    return res_line, others_max[0] + (1 if cpu_max and cpu_loads and statistics_median(cpu_loads) > cpu_max else 0)

cpu_max = int(os.environ.get("FRAME_COST_CPU_MAX", "0") or 0)

def statistics_median(v):
    import statistics
    return statistics.median(v)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--res", default="1920x1080")
    ap.add_argument("--reps", type=int, default=1)
    ap.add_argument("--scene", default="cockpit", choices=sorted(SCENES))
    ap.add_argument("--cfg", nargs="*", default=[])
    ap.add_argument("--graphics", nargs="*", default=[], help="byte=value, e.g. 0x6=1")
    ap.add_argument("--env", nargs="*", default=[])
    ap.add_argument("--args", nargs="*", default=[])
    ap.add_argument("--1997", dest="look1997", action="store_true")
    ap.add_argument("--skip-ms", type=int, default=12000)
    ap.add_argument("--run-ms", type=int, default=15000)
    ap.add_argument("--timeout", type=int, default=240)
    a = ap.parse_args()
    cfg = dict(kv.split("=", 1) for kv in a.cfg)
    gr = dict(kv.split("=", 1) for kv in a.graphics)
    env = dict(kv.split("=", 1) for kv in a.env)
    for r in range(1, a.reps + 1):
        for attempt in range(3):   # a run another Godot shared the GPU with, or one with no result, is done again
            res_line, others = run_once(a.name, a.res, r, a.scene, cfg, gr, env, a.args, a.skip_ms, a.run_ms, a.look1997, a.timeout)
            if res_line and (others == 0 or res_line.startswith("DIED")): break
            if res_line.startswith("DIED"): break

if __name__ == "__main__":
    main()
