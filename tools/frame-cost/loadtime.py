#!/usr/bin/env python3
"""Mission load time per HD pack size: the loading card up to the loading card gone (MissionBuilder's I76_BUILD_TRACE stamps),
warm caches (a first run of each is a warm-up and dropped), T01 and M01, reps interleaved.
  python loadtime.py REPS [RES]
Appends rows to runs/loadtime.tsv: pack, mission, rep, card_up_ms, card_gone_ms, load_s, build_line, vram_tex_mb (from the profile line).
"""
import sys, os, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bench

PACKS = {"4x": {"hd_textures": "true", "hd_texture_size": 4}, "3x": {"hd_textures": "true", "hd_texture_size": 3},
         "2x": {"hd_textures": "true", "hd_texture_size": 2}, "off": {"hd_textures": "false"}}
bench.SCENES["load_t01"] = ["--mission", "t01", "--drive", "--seed", "1", "--skip-movies", "--skip-cutscenes"]
bench.SCENES["load_m01"] = ["--melee", "--mission", "m01", "--drive", "--seed", "1", "--skip-movies"]
reps = int(sys.argv[1]); res = sys.argv[2] if len(sys.argv) > 2 else "1920x1080"
out = os.path.join(bench.OUT, "loadtime.tsv")
for rep in range(0, reps + 1):   # rep 0 warms the caches and is kept apart
    for mission in ("t01", "m01"):
        for pack, cfg in PACKS.items():
            name = f"load_{pack}"
            scene = f"load_{mission}"
            line, others = bench.run_once(name, res, rep, scene, cfg, {}, {"I76_BUILD_TRACE": "1"}, [], 1000, 2000, False, 400)
            log = os.path.join(bench.OUT, f"{name}.{res}.{scene}.{rep}.log")
            up = gone = None; build = ""
            for l in open(log, encoding="utf-8", errors="replace"):
                if m := re.search(r"build trace: loading card up @(\d+) ms", l): up = int(m.group(1))
                if m := re.search(r"build trace: loading card gone @(\d+) ms", l): gone = int(m.group(1))
                if re.match(r"[A-Z]\d\d: \d+ ms", l): build = l.strip()
            vram = re.search(r"vram_tex_mb=([\d.]+)", line or "")
            row = [pack, mission, str(rep), str(up), str(gone), f"{(gone - up) / 1000:.2f}" if up and gone else "", build, vram.group(1) if vram else "", str(others)]
            with open(out, "a", encoding="utf-8") as f: f.write("\t".join(row) + "\n")
            print("\t".join(row), flush=True)
