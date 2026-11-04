#!/usr/bin/env python3
"""The survey: every configuration against the baseline, reps interleaved (rep 1 of each, then rep 2, ...).
  python matrix.py SET RES REPS [SCENE]     SET: all | quick | a name list separated by commas
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bench

# name -> (cfg overrides, graphics byte overrides, env overrides, extra args, look1997)
C = {
    "base":            ({}, {}, {}, [], False),
    "look1997":        ({}, {}, {}, [], True),
    "sdf_off":         ({}, {}, {"I76_SMOOTH_KEY": "0"}, [], False),
    "brush_off":       ({"brush_impacts": '"off"'}, {}, {}, [], False),
    "lighting_off":    ({"lighting_calibration": '"off"'}, {}, {}, [], False),
    "hood_matte":      ({"hood_finish": '"matte"'}, {}, {}, [], False),
    "death_off":       ({"death_variety": '"off"'}, {}, {}, [], False),
    "hd_3x":           ({"hd_texture_size": 3}, {}, {}, [], False),
    "hd_2x":           ({"hd_texture_size": 2}, {}, {}, [], False),
    "hd_off":          ({"hd_textures": "false"}, {}, {}, [], False),
    "veg_low":         ({"vegetation": 1}, {}, {}, [], False),
    "veg_off":         ({"vegetation": 0}, {}, {}, [], False),
    "veg_density_100": ({"vegetation_density": 100}, {}, {}, [], False),
    "smoke_layer_off": ({}, {}, {"I76_SMOKE_LAYER": "0"}, [], False),
    "map_edge_off":    ({"map_edge": '"off"'}, {}, {}, [], False),
    "tracers_line":    ({"mg_tracers": '"original"'}, {}, {}, [], False),
    "dash_2d":         ({"dash_3d": "false"}, {}, {}, [], False),
    "mirror_off":      ({}, {"0xd": 0}, {}, [], False),
    "mirror_original": ({"mirror_range": '"original"'}, {}, {}, [], False),
    "instruments_off": ({}, {"0xe": 0}, {}, [], False),
    "lfe_off":         ({"lfe_level": 0}, {}, {}, [], False),
    "shadow_low":      ({}, {"0x6": 1}, {}, [], False),
    "shadow_off":      ({}, {"0x6": 0}, {}, [], False),
    "ssao_off":        ({}, {}, {"I76_SSAO": "0"}, [], False),
    "terrain_low":     ({}, {"0x2": 0}, {}, [], False),
    "msaa_off":        ({}, {"0x8": 0}, {}, [], False),
    "msaa_2x":         ({}, {"0x8": 1}, {}, [], False),
    "draw_dist_0":     ({"draw_distance": 0}, {}, {}, [], False),
    "palette_grade_off": ({"palette_grade": "false"}, {}, {}, [], False),
    "interiors_off":   ({"car_interiors": "false"}, {}, {}, [], False),
    "smoke":           ({}, {}, {"I76_SMOKE": "0.4"}, [], False),
    "smoke_layer_off_smoke": ({}, {}, {"I76_SMOKE": "0.4", "I76_SMOKE_LAYER": "0"}, [], False),
    "shadow_dist_600": ({}, {}, {"I76_SHADOW_DIST": "600"}, [], False),
    "shadow_splits_2": ({}, {}, {"I76_SHADOW_SPLITS": "2"}, [], False),
    # the scene's parts hidden outright (FrameProfiler's I76_HIDE), to attribute the passes rather than cost a feature
    "hide_veg":        ({}, {}, {"I76_HIDE": "Vegetation"}, [], False),
    "hide_terrain":    ({}, {}, {"I76_HIDE": "Terrain"}, [], False),
    "hide_horizon":    ({}, {}, {"I76_HIDE": "HorizonTerrain"}, [], False),
    "hide_placements": ({}, {}, {"I76_HIDE": "Placements"}, [], False),
    "diag":            ({}, {}, {"I76_VEG_STATS": "1", "I76_TERRAIN_STATS": "1", "I76_BRUSH_DEBUG": "1", "I76_VRAM": "5"}, [], False),
    "aniso_check":     ({}, {}, {}, [], False),
    "aniso_8x":        ({}, {}, {}, [], False),   # project.godot anisotropic_filtering_level=3 (queue3.sh edits it)
    "aniso_4x":        ({}, {}, {}, [], False),   # level 2
    # the mirror follow-ups (hooks on this branch)
    "mirror_rate2":    ({}, {}, {"I76_MIRROR_RATE": "2"}, [], False),
    "mirror_nossao":   ({}, {}, {"I76_MIRROR_SSAO": "0"}, [], False),
    "mirror_both":     ({}, {}, {"I76_MIRROR_RATE": "2", "I76_MIRROR_SSAO": "0"}, [], False),
    "mirror_both_orig": ({"mirror_range": '"original"'}, {}, {"I76_MIRROR_RATE": "2", "I76_MIRROR_SSAO": "0"}, [], False),
}
SETS = {
    "all": [k for k in C if k not in ("smoke", "smoke_layer_off_smoke", "shadow_dist_600", "shadow_splits_2")],
    "quick": ["base", "look1997", "hd_off", "veg_off", "shadow_low", "mirror_off", "sdf_off", "terrain_low"],
    "smoke": ["base", "smoke", "smoke_layer_off_smoke"],
    "shadowtune": ["base", "shadow_dist_600", "shadow_splits_2", "shadow_low"],
    "mirror": ["base", "mirror_rate2", "mirror_nossao", "mirror_both", "mirror_both_orig"],
    "parts": ["base", "hide_veg", "hide_terrain", "hide_horizon", "hide_placements"],
    "fire": ["base", "tracers_line"],
    "chase": ["base", "hd_off", "veg_off", "sdf_off", "shadow_low", "terrain_low", "look1997", "map_edge_off"],
}
def clean_rows():
    """(name, res, rep, scene) of every row with a result and no other Godot seen, less the rows listed in runs/contaminated.tsv."""
    bad = set()
    cpath = os.path.join(bench.OUT, "contaminated.tsv")
    if os.path.exists(cpath):
        for l in open(cpath, encoding="utf-8"):
            f = l.rstrip("\n").split("\t")
            if len(f) >= 4: bad.add((f[0], f[1], f[2], f[3]))
    ok = set()
    rpath = os.path.join(bench.OUT, "results.tsv")
    if os.path.exists(rpath):
        for l in open(rpath, encoding="utf-8"):
            f = l.rstrip("\n").split("\t")
            if len(f) >= 13 and f[12] != "FAILED" and f[11] == "0" and (f[0], f[1], f[2], f[3]) not in bad:
                ok.add((f[1], f[2], f[3], f[4]))
    return ok

fill = sys.argv[1] == "fill"
if fill: sys.argv.pop(1)
sel, res, reps = sys.argv[1], sys.argv[2], int(sys.argv[3])
scene = sys.argv[4] if len(sys.argv) > 4 else "cockpit"
names = SETS.get(sel) or sel.split(",")
skip_ms, run_ms = 8000, 10000
done = clean_rows() if fill else set()
for rep in range(1, reps + 1):
    for n in names:
        if (n, res, str(rep), scene) in done: continue
        cfg, gr, env, extra, look = C[n]
        for attempt in range(3):
            line, others = bench.run_once(n, res, rep, scene, cfg, gr, env, extra, skip_ms, run_ms, look, 300)
            if line and others == 0: break
