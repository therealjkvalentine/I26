#!/usr/bin/env python3
"""Medians over reps per (config, res, scene) from runs/results.tsv, with the delta against 'base' of the same res and scene.
  python summarise.py [RES] [SCENE] [--md]
"""
import sys, os, statistics as st, collections
here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, here)
import bench
runs_dir = bench.OUT
want_res = next((a for a in sys.argv[1:] if "x" in a and a[0].isdigit()), None)
want_scene = next((a for a in sys.argv[1:] if a in ("cockpit", "chase", "fire", "drive")), None)
md = "--md" in sys.argv
rows = collections.defaultdict(list)
bad = set()
if os.path.exists(os.path.join(runs_dir, "contaminated.tsv")):
    for l in open(os.path.join(runs_dir, "contaminated.tsv"), encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        if len(f) >= 4: bad.add(tuple(f[:4]))
for line in open(os.path.join(runs_dir, "results.tsv"), encoding="utf-8"):
    f = line.rstrip("\n").split("\t")
    if len(f) < 13: continue
    ts, name, res, rep, scene, u, m, p, t, wall, code, others, resl = f[:13]
    if resl == "FAILED" or others not in ("0",) or name.startswith("proto") or (ts, name, res, rep) in bad: continue
    if want_res and res != want_res: continue
    if want_scene and scene != want_scene: continue
    d = {k: float(v) for k, v in (kv.split("=") for kv in resl.split()) if k != "frames"}
    d["frames"] = float(dict(kv.split("=") for kv in resl.split())["frames"])
    rows[(res, scene, name)].append(d)
keys = ["cpu.wall", "cpu.nodes", "cpu.render", "gpu.total", "gpu.viewport", "gpu.depth", "gpu.opaque", "gpu.shadows", "gpu.ssao", "gpu.transparent", "draws", "prims_k", "vram_tex_mb", "vram_video_mb"]
def med(rs, k): return st.median(r.get(k, float("nan")) for r in rs)
def spread(rs, k):
    v = [r.get(k, float("nan")) for r in rs]
    return (max(v) - min(v)) if len(v) > 1 else 0.0
groups = collections.defaultdict(list)
for (res, scene, name), rs in rows.items(): groups[(res, scene)].append((name, rs))
for (res, scene), items in sorted(groups.items()):
    base = next((rs for n, rs in items if n == "base"), None)
    print(f"\n## {res} {scene}  (n = reps; delta = config minus base; spread = max-min over reps)")
    hdr = ["config", "n", "cpu.wall", "d", "cpu.nodes", "d", "gpu.total", "d", "gpu.depth", "gpu.opaque", "gpu.shadows", "gpu.ssao", "draws", "d", "vram_tex", "d", "spread wall/gpu"]
    if md: print("| " + " | ".join(hdr) + " |"); print("|" + "---|" * len(hdr))
    else: print("\t".join(hdr))
    def D(rs, k): return (med(rs, k) - med(base, k)) if base else float("nan")
    out = []
    for name, rs in items:
        out.append((name, len(rs), med(rs, "cpu.wall"), D(rs, "cpu.wall"), med(rs, "cpu.nodes"), D(rs, "cpu.nodes"), med(rs, "gpu.total"), D(rs, "gpu.total"),
                    med(rs, "gpu.depth"), med(rs, "gpu.opaque"), med(rs, "gpu.shadows"), med(rs, "gpu.ssao"), med(rs, "draws"), D(rs, "draws"), med(rs, "vram_tex_mb"), D(rs, "vram_tex_mb"),
                    f"{spread(rs, 'cpu.wall'):.1f}/{spread(rs, 'gpu.total'):.1f}"))
    out.sort(key=lambda r: (r[0] != "base", r[7]))
    for r in out:
        cells = [r[0], str(r[1])] + [f"{x:+.2f}" if i in (3, 5, 7) else f"{x:.0f}" if i in (12, 13, 14, 15) else f"{x:.2f}" for i, x in enumerate(r[2:16], 2)] + [r[16]]
        cells[12] = f"{r[12]:.0f}"; cells[13] = f"{r[13]:+.0f}"; cells[15] = f"{r[15]:+.0f}"
        print(("| " + " | ".join(cells) + " |") if md else "\t".join(cells))
