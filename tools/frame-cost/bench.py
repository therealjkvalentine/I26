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
(nothing is written to the player's data). shell.cfg is written from BASE plus --cfg before each run. Before a run it
waits for the GPU to be under 10 % with no other Godot or compute process, samples other GPU users every 2 s during the run
(the row's `others` column: rerun a row that is not 0), and leaves GAP_S idle after each run for other lanes' idle checks.
"""
import argparse, os, re, subprocess, sys, time, datetime, shutil

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.environ.get("FRAME_COST_OUT") or os.path.join(os.environ.get("TEMP", HERE), "frame-cost")
GODOT = os.environ.get("GODOT", "godot")
GAME = os.path.normpath(os.path.join(HERE, "..", "..", "game"))
INSTALL = os.environ.get("I76_INSTALL", "")
APPDATA = os.environ.get("FRAME_COST_APPDATA") or os.path.join(OUT, "appdata")
CFG = os.path.join(APPDATA, "I26", "shell.cfg")
GAP_S = 20   # idle seconds left after each run for the other lanes' idle checks

# Everything on: the modern look with every port feature at its costliest offered value.
BASE_GRAPHICS = [5, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 3, 2, 1, 1]   # +0x2 Terrain High, +0x6 Shadow High (SSAO), +0x8 MSAA 4x
BASE = {
    "vegetation": 2, "vegetation_density": 60, "draw_distance": 1, "map_edge": '"on"', "checkpoints": "false",
    "force_feedback": "true", "lfe_level": 100, "sim_rate": 24, "palette_grade": "true", "crash_blur": '"on"',
    "mouse_camera": 2, "max_fps": 0, "vsync": "false", "developer": "true", "hd_menu_art": "true", "hd_textures": "true",
    "hd_texture_size": 4, "effects_size": 100, "effects_brightness": 100, "effects_opacity": 100, "mirror_range": '"full"',
    "brake_lamps": '"held"', "mg_tracers": '"bolts"', "hood_finish": '"gloss"', "death_variety": '"on"', "brush_impacts": '"on"',
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
PROFILE = os.environ.get("FRAME_COST_PROFILE", "full")   # full (everything on) | defaults | old_defaults
SCENES = {
    # the T01 start, the player's car standing with its engine running while the script's cars play the opening; seed 1
    "cockpit": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes"],
    "chase": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--chase"],
    "fire": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-fire"],
    "drive": ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes", "--hold-throttle", "--chase"],
}

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
        found += ["compute:" + m.group(1) for m in re.finditer(r"^\|\s+\d+\s+\S+\s+\S+\s+(\d+)\s+C\s+", smi, re.M)]
    except Exception:
        pass
    return found

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
    for k, v in graphics_over.items():
        g[int(k, 0)] = int(v)
    port = dict(DEFAULTS if profile in ("defaults", "old_defaults") else BASE); port.update({k: v for k, v in over.items() if k != "__profile"})
    lines = ["[shell]", "", "version=13", "graphics=PackedByteArray(" + ", ".join(map(str, g)) + ")", "audio=PackedByteArray(8, 8, 8, 8)", "play=0", "difficulty=0", "", "[port]", ""]
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
    write_cfg(cfg_over, graphics_over)
    engine = [GODOT, "--path", GAME, "--gpu-profile"]
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
    env.update(env_over)
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
            proc = subprocess.run(engine + ["--"] + game, stdout=log, stderr=subprocess.STDOUT, env=env, timeout=timeout)
            code = proc.returncode
        except subprocess.TimeoutExpired:
            code = "timeout"
            subprocess.run(["taskkill", "/IM", os.path.basename(GODOT), "/F"], capture_output=True)
    stop.set(); th.join()
    wall = time.time() - t0
    res_line = ""
    with open(stem + ".log", encoding="utf-8", errors="replace") as f:
        for line in f:
            if line.startswith("frame-profile-result:"):
                res_line = line.strip()[len("frame-profile-result:"):].strip()
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
    return res_line, others_max[0]

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
            if res_line and others == 0: break

if __name__ == "__main__":
    main()
