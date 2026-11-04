#!/usr/bin/env python3
"""costs.md from costs-template.md and report.py's tables; HD_WORDS from the HD numbers."""
import os, sys, io, statistics as st
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import report
t = io.open(sys.argv[2] if len(sys.argv) > 2 else os.path.join(report.here, "costs-template.md"), encoding="utf-8").read()
parts = {"base": report.base_table(), "features": report.features_table(), "hd": report.hd_table(), "noise": report.noise(), "reruns": report.rerun_list(),
         "sets": "\n\n".join([
             report.set_table("cockpit", ["base", "hide_terrain", "hide_veg", "hide_horizon", "hide_placements"], "1920x1080", "The scene's parts hidden (I76_HIDE)"),
             report.set_table("cockpit", ["base", "shadow_low", "shadow_off", "ssao_off", "shadow_dist_600", "shadow_splits_2"], "1920x1080", "Shadows and SSAO"),
             report.set_table("cockpit", ["base", "mirror_off", "mirror_original", "mirror_rate2", "mirror_nossao", "mirror_both", "mirror_both_orig"], "1920x1080", "The mirror's follow-ups (this branch's hooks)"),
             report.set_table("cockpit", ["base", "mirror_off", "mirror_original", "mirror_rate2", "mirror_nossao", "mirror_both", "mirror_both_orig"], "3440x1440", "The mirror's follow-ups (this branch's hooks)"),
             report.set_table("cockpit", ["base", "aniso_8x", "aniso_4x"], "3440x1440", "Anisotropic filtering (project.godot, by hand)"),
             report.set_table("cockpit", ["base", "smoke", "smoke_layer_off_smoke"], "1920x1080", "Damage smoke (every car held at 40 % health)"),
             report.set_table("fire", ["base", "tracers_line"], "1920x1080", "Firing the machine guns (--hold-fire)"),
             report.set_table("chase", ["base", "hd_off", "veg_off", "sdf_off", "shadow_low", "terrain_low", "look1997", "map_edge_off"], "3440x1440", "Chase view (F2)")])}
# the HD comparison in words
lt = report.loadtimes()
def m(res, cfg, k): rs = report.runs.get((res, "cockpit", cfg)); return report.med(rs, k) if rs else float("nan")
def L(pack, mission): v = lt.get((pack, mission)); return st.median(v) if v else float("nan")
words = (f"4x over 2x costs {L('4x','t01') - L('2x','t01'):+.1f} s of load on T01 and {L('4x','m01') - L('2x','m01'):+.1f} s on M01 "
         f"(4x loads T01 in {L('4x','t01'):.1f} s, 2x in {L('2x','t01'):.1f} s, the originals in {L('off','t01'):.1f} s), "
         f"{m('3440x1440','base','vram_tex_mb') - m('3440x1440','hd_2x','vram_tex_mb'):.0f} MB more VRAM ({m('3440x1440','base','vram_tex_mb') - m('3440x1440','hd_off','vram_tex_mb'):.0f} MB over the originals), "
         f"and {m('3440x1440','base','gpu.total') - m('3440x1440','hd_2x','gpu.total'):+.2f} ms of GPU frame time at 3440x1440 and {m('1920x1080','base','gpu.total') - m('1920x1080','hd_2x','gpu.total'):+.2f} ms at 1920x1080, "
         f"which is inside the noise: the pack size is a VRAM and loading choice, not a frame-rate one. 3x sits between on every count.")
parts["HD_WORDS"] = words
for k, v in parts.items(): t = t.replace(f"<<TABLE:{k}>>", v).replace(f"<<{k}>>", v)
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(report.here, "..", "..", "docs", "perf", "costs.md")
io.open(out, "w", encoding="utf-8", newline="\n").write(t)
print("wrote", out)
