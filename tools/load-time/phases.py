#!/usr/bin/env python3
"""A mission start's main-thread seconds by part, one column per LoadProfiler CSV (docs/perf/loadtime.md, "A restart of the same
mission"): the card, the build's parts, the attach's, the textures made. The phases no group takes are printed to stderr.
  python phases.py RUN.csv RUN.t05.csv ...
"""
import csv, sys, collections
G = [
 ("card (made, ramps, gone)", lambda p: p.startswith("loading card") or p == "click"),
 ("textures, materials, meshes (kit)", lambda p: p.startswith("textures, materials")),
 ("build: ground, sky, horizon", lambda p: p in ("build start", "ground, sky, horizon")),
 ("build: roads (flatten, textures, bake)", lambda p: p.startswith("road")),
 ("build: ground materials (shared modern)", lambda p: p == "ground materials"),
 ("build: terrain (arrays, chunks, modern)", lambda p: p.startswith("terrain")),
 ("build: placements (scenery, their textures)", lambda p: p in ("scenery spawn", "done")),
 ("sky, horizon, lighting calibration", lambda p: p in ("world in tree", "horizon built", "sky materials", "sky") or p.startswith("lighting calibration") or p.startswith("sky")),
 ("look: shadows, draw distance, vegetation, quality", lambda p: p.startswith("look:") or p.startswith("veg:") or p in ("trees shown", "drop shadows", "mission loaded", "look set")),
 ("spawn: sim world, settings, combat view", lambda p: p.startswith("spawn: sim world") or p.startswith("spawn: session") or p.startswith("spawn: settings") or p.startswith("spawn: combat view")),
 ("spawn: cars (parts, meshes, nodes)", lambda p: p.startswith("spawn: car ") or p.startswith("cars prefetched")),
 ("spawn: player's cockpit (art, radar, panels)", lambda p: p.startswith("spawn: cockpit") or p.startswith("spawn: the player's car")),
 ("spawn: damage view, wrecks, prewarms", lambda p: p.startswith("spawn: damage") or "prewarmed" in p and p.startswith("spawn") or p in ("car spawned", "damage view prewarmed")),
 ("first tick, vegetation and trees prewarmed", lambda p: p in ("first tick done", "vegetation prewarmed", "trees prewarmed", "car, first tick")),
 ("effects rehearsal (first frames drawn)", lambda p: p.startswith("rehearsal") or p == "effects rehearsal"),
]
def parts(path):
    out = collections.OrderedDict((g, 0) for g, _ in G); out["other"] = 0; tex = [0, 0.0, 0]; total = None; other = collections.Counter()
    for r in csv.DictReader(open(path, encoding="utf-8")):
        if r["thread"] == "event":
            if r["phase"] == "event: mission": total = int(r["start_ms"])
            continue
        ms, ph = int(r["ms"]), r["phase"]
        tex[0] += int(r["tex_orig"]) + int(r["tex_hd"]); tex[1] += float(r["mb_orig"]) + float(r["mb_hd"]); tex[2] += int(r["tex_ms"])
        if r["thread"] != "main": continue
        for g, f in G:
            if f(ph): out[g] += ms; break
        else: out["other"] += ms; other[ph] += ms
    return out, tex, total, other
cols = [parts(p) for p in sys.argv[1:]]
names = [p.split("/")[-1].replace(".csv", "") for p in sys.argv[1:]]
print("| part (main thread, s) | " + " | ".join(names) + " |"); print("|---" * (len(names) + 1) + "|")
for g in list(cols[0][0].keys()):
    print(f"| {g} | " + " | ".join(f"{c[0][g]/1000:.2f}" for c in cols) + " |")
print("| **to the mission** | " + " | ".join(f"**{c[2]/1000:.2f}**" for c in cols) + " |")
print("| textures made (MB, ms in the factory) | " + " | ".join(f"{c[1][0]} ({c[1][1]:.0f} MB, {c[1][2]} ms)" for c in cols) + " |")
for n, c in zip(names, cols):
    if c[3]: print(n, "other:", ", ".join(f"{k} {v}" for k, v in c[3].most_common(6)), file=sys.stderr)
