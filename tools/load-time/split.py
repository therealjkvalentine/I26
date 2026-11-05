#!/usr/bin/env python3
"""Where a mission start's seconds go, by part (docs/perf/loadtime.md's "Where the seconds go"): the medians over the clean reps
of each tag / mission / pack / cache of
  before card  - the scene's start to the card up (the card made and faded in; the build's start on its thread)
  build        - MissionBuilder.Build (terrain, roads, placements, their textures), main or mission-build thread
  cars (build) - the cars' parts made on the build's thread (the after builds)
  attach       - the main thread after the build: the world into the tree, sky and lighting calibration, the look, the cars'
                 nodes, the first tick, the prewarms, the effects rehearsal, the card's fade out
  waited       - the main thread's time with nothing of its own to do (the card held, the build going on)
and the main thread's share of the attach, by group.
  python split.py OUT [--tags a,b]
"""
import argparse, collections, csv, os, statistics, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import summarise as sm

GROUPS = [
    ("sky + lighting calibration", ("world in tree", "horizon built", "sky materials", "lighting calibration", "sky")),
    ("look (shadows, draw distance, vegetation, quality)", ("look:", "veg:", "trees shown", "drop shadows", "mission loaded", "look set")),
    ("spawn: sim world, combat view", ("spawn: sim world", "spawn: session", "spawn: settings", "spawn: combat view")),
    ("spawn: cars' nodes", ("spawn: cars made",)),
    ("spawn: damage view, prewarms", ("spawn: damage view", "spawn: car prewarmed", "spawn: wrecks prewarmed", "car spawned", "damage view prewarmed")),
    ("first tick, vegetation and trees prewarmed", ("first tick done", "vegetation prewarmed", "trees prewarmed", "car, first tick")),
    ("effects rehearsal", ("effects rehearsal",)),
]
BUILD = ("build start", "ground, sky, horizon", "roads flattened", "road textures", "road pieces", "road bake", "road bake done", "ground materials",
         "terrain build", "terrain: 1997 arrays", "terrain: chunks added", "terrain: modern prepared", "terrain built", "roads build", "scenery spawn", "done")


def parts(path):
    out = collections.Counter()
    seen_tree = False
    for r in csv.DictReader(open(path, encoding="utf-8")):
        if r["thread"] == "event": continue
        ms, ph, th = int(r["ms"]), r["phase"], r["thread"]
        if ph == "world in tree": seen_tree = True
        if ph in BUILD: out["build"] += ms; continue
        if ph.startswith("cars prefetched"): out["cars (build)"] += ms; continue
        if th != "main": continue
        if not seen_tree:
            if ph.startswith("build: done") or ph.startswith("build: behind") or ph.startswith("briefing") or ph.startswith("loading card up, after"): out["waited"] += ms
            else: out["before card"] += ms
            continue
        for g, keys in GROUPS:
            if any(ph.startswith(k) for k in keys): out[g] += ms; break
        else: out["attach: other"] += ms
    out["attach"] = sum(v for k, v in out.items() if k in [g for g, _ in GROUPS] or k == "attach: other")
    return out


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("out"); ap.add_argument("--tags", default="")
    a = ap.parse_args()
    rows = [r for r in sm.load(a.out) if r["res"] and r["rep"] >= 1 and r["others"].startswith("godot+0") and r["csv"] and os.path.exists(r["csv"])]
    if a.tags: rows = [r for r in rows if r["tag"] in a.tags.split(",")]
    groups = collections.OrderedDict()
    for r in rows: groups.setdefault((r["tag"], r["mission"], r["pack"], r["cache"]), []).append(parts(r["csv"]))
    keys = ["before card", "waited", "build", "cars (build)", "attach"] + [g for g, _ in GROUPS] + ["attach: other"]
    print("| tag | mission | pack | cache | " + " | ".join(keys) + " | reps |")
    print("|" + "---|" * (len(keys) + 5))
    for k, ps in groups.items():
        med = [statistics.median([p.get(x, 0) for p in ps]) / 1000 for x in keys]
        print(f"| {' | '.join(k)} | " + " | ".join(f"{v:.2f}" for v in med) + f" | {len(ps)} |")


if __name__ == "__main__":
    main()
