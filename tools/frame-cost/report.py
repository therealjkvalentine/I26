#!/usr/bin/env python3
"""costs.md's tables from runs/results.tsv and runs/loadtime.tsv (markdown on stdout)."""
import os, sys, statistics as st, collections
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import bench
runs_dir = bench.OUT
bad = set()
if os.path.exists(os.path.join(runs_dir, "contaminated.tsv")):
    for l in open(os.path.join(runs_dir, "contaminated.tsv"), encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        if len(f) >= 4: bad.add(tuple(f[:4]))
runs = collections.defaultdict(list)   # (res, scene, name) -> [dict]
reruns = []
for line in open(os.path.join(runs_dir, "results.tsv"), encoding="utf-8"):
    f = line.rstrip("\n").split("\t")
    if len(f) < 13 or f[1].startswith("proto"): continue
    ts, name, res, rep, scene, u, m, p, t, wall, code, others, resl = f[:13]
    if resl == "FAILED" or others != "0" or (ts, name, res, rep) in bad:
        reruns.append((ts, name, res, rep, scene, "other Godot seen" if others != "0" else "no result" if resl == "FAILED" else "listed in contaminated.tsv"))
        continue
    d = {k: float(v) for k, v in (kv.split("=") for kv in resl.split())}
    d["start"], d["end"] = (f[13], f[14]) if len(f) >= 15 else ("", "")
    runs[(res, scene, name)].append(d)

def med(rs, k): return st.median(r[k] for r in rs if k in r) if any(k in r for r in rs) else float("nan")
def spread(rs, k):
    v = [r[k] for r in rs if k in r]
    return max(v) - min(v) if len(v) > 1 else 0.0
def cpu(r): return r["cpu.nodes"] + r["cpu.render"]
for rs in runs.values():
    for r in rs: r["cpu.main"] = cpu(r)

# feature -> (config that turns it off / down, label of what was toggled, note)
FEATURES = [
    ("The modern look as a whole (vs the 1997 look)", "look1997", "`--1997`: the 1997 look, everything of the port's off", ""),
    ("Ambient occlusion (Shadow Detail High's SSAO)", "ssao_off", "I76_SSAO=0: shadows kept, SSAO off", "the depth pre-pass must write normals: the terrain's whole material runs twice"),
    ("Sun shadows (Shadow Detail Low vs Off)", "shadow_off", "Shadow Detail Off", "measured against shadow_low (SSAO already off); see the note"),
    ("Rear-view mirror (3D cockpit, Full)", "mirror_off", "Rear Mirror Off", "a second scene render at 1024x256 every frame"),
    ("Mirror range Full (vs the original's 100 m)", "mirror_original", "mirror_range=original", ""),
    ("Anti-aliasing MSAA 4x (vs Off)", "msaa_off", "Anti-Aliasing Off", ""),
    ("Anti-aliasing MSAA 4x (vs 2x)", "msaa_2x", "Anti-Aliasing 2x", ""),
    ("Terrain Detail High (the 1.25 m level)", "terrain_low", "Terrain Detail Low", ""),
    ("Vegetation High (vs Off)", "veg_off", "Vegetation Off", "60 % density"),
    ("Vegetation High (vs Low)", "veg_low", "Vegetation Low", ""),
    ("Vegetation density 100 % (vs the 60 % default)", "veg_density_100", "vegetation_density=100", "a cost, not a saving: 100 % minus 60 %"),
    ("Draw distance 20 km (vs the original's 600 m)", "draw_dist_0", "draw_distance=0", ""),
    ("3D dash (vs the 2D dash)", "dash_2d", "dash_3d=false", "negative: the 3D dash hides terrain pixels behind it"),
    ("External instruments (2D panels)", "instruments_off", "Ext. Instrum. Disp. Off", ""),
    ("HD textures 4x (vs the install's originals)", "hd_off", "hd_textures=false", "VRAM, not frame time"),
    ("HD textures 4x (vs 3x)", "hd_3x", "hd_texture_size=3", ""),
    ("HD textures 4x (vs 2x)", "hd_2x", "hd_texture_size=2", ""),
    ("Smooth keyed edges (signed-distance outlines)", "sdf_off", "I76_SMOOTH_KEY=0 (this branch's hook)", "one R8 field per keyed texture"),
    ("Brush impacts", "brush_off", "brush_impacts=off", "nothing was fighting in the scene"),
    ("Lighting calibration (original colours)", "lighting_off", "lighting_calibration=off", "a one-time solve at load; no per-frame work"),
    ("Hood finish Gloss (vs Matte)", "hood_matte", "hood_finish=matte", ""),
    ("Death variety", "death_off", "death_variety=off", "nothing died in the scene: costs only at a death"),
    ("Smoke depth layer", "smoke_layer_off", "I76_SMOKE_LAYER=0", "no smoke in the standing scene; see the smoke table"),
    ("Hide Map Edge Cliffs On", "map_edge_off", "map_edge=off", "a uniform branch in the terrain vertex shader"),
    ("MG tracers as bolts (vs the line)", "tracers_line", "mg_tracers=original", "no firing in the standing scene; see the fire table"),
    ("Bass-shaker synth (LFE 100 %)", "lfe_off", "lfe_level=0", "CPU only, main thread"),
    ("Palette grade", "palette_grade_off", "palette_grade=false", ""),
    ("Car interiors", "interiors_off", "car_interiors=false", ""),
]
RES = ["3440x1440", "1920x1080"]

def delta(res, scene, name, k, against="base"):
    a, b = runs.get((res, scene, against)), runs.get((res, scene, name))
    if not a or not b: return float("nan")
    return med(a, k) - med(b, k)

def fmt(x, d=2, sign=True):
    if x != x: return "n/a"
    return (f"{x:+.{d}f}" if sign else f"{x:.{d}f}")

def features_table(scene="cockpit"):
    rows = []
    for label, cfg, how, note in FEATURES:
        against = "shadow_low" if cfg == "shadow_off" else "base"
        sign = -1 if cfg == "veg_density_100" else 1   # that row costs the configuration over the baseline, not a feature of the baseline
        vals = {}
        for res in RES:
            for k in ("gpu.total", "cpu.main", "cpu.wall", "draws", "vram_tex_mb"):
                vals[(res, k)] = sign * delta(res, scene, cfg, k, against)
        n = [len(runs.get((res, scene, cfg), [])) for res in RES]
        rank = max(abs(vals[(r, "gpu.total")]) if vals[(r, "gpu.total")] == vals[(r, "gpu.total")] else 0 for r in RES)
        rows.append((rank, label, how, vals, n, note))
    rows.sort(key=lambda r: -r[0])
    out = ["| # | Feature (on, as in the baseline) | Toggled by | GPU ms 1440p | GPU ms 1080p | CPU main-thread ms 1440p | 1080p | Frame (wall) ms 1440p | 1080p | Draw calls 1440p | 1080p | VRAM MB 1440p | 1080p | reps | Note |",
           "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for i, (rank, label, how, v, n, note) in enumerate(rows, 1):
        out.append(f"| {i} | {label} | {how} | {fmt(v[('3440x1440','gpu.total')])} | {fmt(v[('1920x1080','gpu.total')])} | {fmt(v[('3440x1440','cpu.main')])} | {fmt(v[('1920x1080','cpu.main')])} | "
                   f"{fmt(v[('3440x1440','cpu.wall')])} | {fmt(v[('1920x1080','cpu.wall')])} | {fmt(v[('3440x1440','draws')],0)} | {fmt(v[('1920x1080','draws')],0)} | "
                   f"{fmt(v[('3440x1440','vram_tex_mb')],0)} | {fmt(v[('1920x1080','vram_tex_mb')],0)} | {n[0]}/{n[1]} | {note} |")
    return "\n".join(out)

def base_table(scene="cockpit"):
    out = ["| Resolution | Frame (wall) ms | GPU total ms | of which mirror viewport | depth pre-pass | opaque | shadows | SSAO pass | sky | transparent | CPU nodes ms | CPU render ms | Draw calls | Primitives (k) | VRAM textures MB | VRAM total MB | GPU MHz | reps |",
           "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for res in RES:
        rs = runs.get((res, scene, "base"), [])
        if not rs: continue
        m = lambda k: med(rs, k)
        out.append(f"| {res} | {m('cpu.wall'):.1f} | {m('gpu.total'):.1f} | {m('gpu.vp0'):.1f} | {m('gpu.depth'):.1f} | {m('gpu.opaque'):.1f} | {m('gpu.shadows'):.2f} | {m('gpu.ssao'):.2f} | {m('gpu.sky'):.2f} | {m('gpu.transparent'):.2f} | "
                   f"{m('cpu.nodes'):.1f} | {m('cpu.render'):.1f} | {m('draws'):.0f} | {m('prims_k'):.0f} | {m('vram_tex_mb'):.0f} | {m('vram_video_mb'):.0f} | {m('gpu_mhz') if m('gpu_mhz') == m('gpu_mhz') else float('nan'):.0f} | {len(rs)} |")
    return "\n".join(out)

def hd_table(scene="cockpit"):
    disk = {"4x": "1608 MB, 6563 .dds", "3x": "1220 MB, 6563 .dds", "2x": "923 MB, 6563 .dds", "off": "the install's own textures"}
    cfgs = [("4x", "base"), ("3x", "hd_3x"), ("2x", "hd_2x"), ("off", "hd_off")]
    out = ["| Pack | On disk | VRAM textures MB 1440p | 1080p | GPU ms 1440p | 1080p | Frame (wall) ms 1440p | 1080p | Load T01 s | Load M01 s |", "|---|---|---|---|---|---|---|---|---|---|"]
    lt = loadtimes()
    for pack, cfg in cfgs:
        cells = [pack, disk[pack]]
        for k, d in (("vram_tex_mb", 0), ("gpu.total", 2), ("cpu.wall", 2)):
            for res in RES:
                rs = runs.get((res, scene, cfg), [])
                cells.append(f"{med(rs, k):.{d}f}" if rs else "n/a")
        for mission in ("t01", "m01"):
            v = lt.get((pack, mission))
            cells.append(f"{st.median(v):.1f} (n={len(v)}, {min(v):.1f}-{max(v):.1f})" if v else "n/a")
        out.append("| " + " | ".join(cells) + " |")
    return "\n".join(out)

def loadtimes():
    lt = collections.defaultdict(list)
    p = os.path.join(runs_dir, "loadtime.tsv")
    if not os.path.exists(p): return lt
    for l in open(p, encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        if len(f) < 9 or f[2] == "0" or f[5] == "" or f[8] != "0": continue   # rep 0 warms the caches; a run with another Godot is out
        lt[(f[0], f[1])].append(float(f[5]))
    return lt

def set_table(scene, names, res, title):
    out = [f"**{title}** ({res}, {scene} scene, medians over reps, deltas against this set's base)", "", "| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |", "|---|---|---|---|---|---|---|---|---|---|---|---|"]
    base = runs.get((res, scene, "base"))
    for n in names:
        rs = runs.get((res, scene, n))
        if not rs: continue
        def cell(k, d=2):
            v = med(rs, k)
            return f"{v:.{d}f}" if n == "base" or not base else f"{v:.{d}f} ({v - med(base, k):+.{d}f})"
        out.append(f"| {n} | {len(rs)} | {cell('gpu.total')} | {cell('gpu.depth')} | {cell('gpu.opaque')} | {cell('gpu.shadows')} | {cell('gpu.transparent')} | {cell('cpu.nodes')} | {cell('cpu.render')} | {cell('cpu.wall')} | {cell('draws', 0)} | {cell('vram_tex_mb', 0)} |")
    return "\n".join(out)

def noise(scene="cockpit"):
    out = []
    for res in RES:
        sp = {k: [spread(rs, k) for (r, s, n), rs in runs.items() if r == res and s == scene and len(rs) >= 2] for k in ("gpu.total", "cpu.main", "cpu.wall", "draws")}
        if not sp["gpu.total"]: continue
        out.append(f"- {res}: over {len(sp['gpu.total'])} configurations with 2-3 reps, the max-min spread over reps is median {st.median(sp['gpu.total']):.2f} ms (p90 {sorted(sp['gpu.total'])[int(0.9 * (len(sp['gpu.total']) - 1))]:.2f}) for GPU total, "
                   f"{st.median(sp['cpu.main']):.2f} ms (p90 {sorted(sp['cpu.main'])[int(0.9 * (len(sp['cpu.main']) - 1))]:.2f}) for the main thread's work, "
                   f"{st.median(sp['cpu.wall']):.2f} ms (p90 {sorted(sp['cpu.wall'])[int(0.9 * (len(sp['cpu.wall']) - 1))]:.2f}) for the wall frame time, {st.median(sp['draws']):.0f} draw calls.")
    return "\n".join(out)

def rerun_list():
    return "\n".join(f"- {ts} {name} {res} rep {rep} ({scene}): {why}" for ts, name, res, rep, scene, why in reruns)

if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "all"
    if what in ("all", "base"): print("### Baseline\n\n" + base_table() + "\n")
    if what in ("all", "features"): print("### Features\n\n" + features_table() + "\n")
    if what in ("all", "hd"): print("### HD packs\n\n" + hd_table() + "\n")
    if what in ("all", "sets"):
        print(set_table("cockpit", ["base", "hide_terrain", "hide_veg", "hide_horizon", "hide_placements"], "1920x1080", "The scene's parts hidden") + "\n")
        print(set_table("cockpit", ["base", "shadow_low", "shadow_off", "ssao_off", "shadow_dist_600", "shadow_splits_2"], "1920x1080", "Shadows and SSAO") + "\n")
        print(set_table("cockpit", ["base", "smoke", "smoke_layer_off_smoke"], "1920x1080", "Damage smoke (every car held at 40 % health)") + "\n")
        print(set_table("fire", ["base", "tracers_line"], "1920x1080", "Firing the machine guns") + "\n")
        print(set_table("chase", ["base", "hd_off", "veg_off", "sdf_off", "shadow_low", "terrain_low", "look1997", "map_edge_off"], "3440x1440", "Chase view") + "\n")
    if what in ("all", "noise"): print("### Noise\n\n" + noise() + "\n")
    if what in ("all", "reruns"): print("### Rows left out and rerun\n\n" + rerun_list() + "\n")
