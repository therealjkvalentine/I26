#!/usr/bin/env python3
"""The load-time survey's tables from loadprofile.py's output (docs/perf/loadtime.md).
  python summarise.py OUT [--tags before,after] [--top 8] [--phases]
Per tag, mission, pack and cache: the median over the clean reps (rep >= 1, no other Godot process) of the click-to-mission
time, the card's first frame, the build's end and the main thread's busy time, with the max-min spread; then the top phases
(each phase's median ms over the reps, its thread, its textures and pipelines). A rep whose total is more than 25 % off its
siblings' median while the CPU load was over 50 % is marked contaminated (*) and left out of the median.
"""
import argparse, collections, csv, os, re, statistics


def parse_res(s):
    return dict(kv.split("=", 1) for kv in s.split() if "=" in kv)


def load(out):
    rows = []
    for l in open(os.path.join(out, "results.tsv"), encoding="utf-8"):
        f = l.rstrip("\n").split("\t")
        if len(f) < 13: continue
        when, tag, mission, pack, cache, rep, gpu, load0, loadmed, others, wall, code, res = f[:13]
        r = parse_res(res) if res != "FAILED" else {}
        rows.append(dict(when=when, tag=tag, mission=mission, pack=pack, cache=cache, rep=int(rep), cpu=int(loadmed) if loadmed.lstrip("-").isdigit() else -1,
                         others=others, res=r, csv=r.get("csv")))
    return rows


def phases(path):
    out = collections.OrderedDict()
    if not path or not os.path.exists(path): return out
    for r in csv.DictReader(open(path, encoding="utf-8")):
        if r["thread"] == "event": continue
        key = (r["phase"], r["thread"])
        o = out.setdefault(key, dict(ms=0, tex=0, hd=0, mb=0.0, pipes=0))
        o["ms"] += int(r["ms"]); o["tex"] += int(r["tex_orig"]) + int(r["tex_hd"]); o["hd"] += int(r["tex_hd"])
        o["mb"] += float(r["mb_orig"]) + float(r["mb_hd"]); o["pipes"] += int(r["pipelines"])
    return out


def num(r, k):
    v = r["res"].get(k, "-")
    return int(v) if v.lstrip("-").isdigit() else None


def main():
    ap = argparse.ArgumentParser(); ap.add_argument("out"); ap.add_argument("--tags", default=""); ap.add_argument("--top", type=int, default=8)
    ap.add_argument("--phases", action="store_true")
    a = ap.parse_args()
    rows = [r for r in load(a.out) if r["res"] and r["rep"] >= 1 and r["others"].startswith("godot+0")]
    if a.tags: rows = [r for r in rows if r["tag"] in a.tags.split(",")]
    groups = collections.OrderedDict()
    for r in rows: groups.setdefault((r["tag"], r["mission"], r["pack"], r["cache"]), []).append(r)
    print("| tag | mission | pack | cache | to mission s | spread s | card's first frame ms | build done s | main thread busy s | textures (HD) | MB | pipelines | reps |")
    print("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for key, rs in groups.items():
        tot = [num(r, "total_ms") for r in rs if num(r, "total_ms")]
        med = statistics.median(tot)
        clean = [r for r in rs if not (abs(num(r, "total_ms") - med) > 0.25 * med and r["cpu"] > 50)]
        marks = len(rs) - len(clean)
        tot = [num(r, "total_ms") for r in clean]
        def m(k, scale=1000.0):
            v = [num(r, k) for r in clean if num(r, k) is not None]
            return f"{statistics.median(v) / scale:.2f}" if v else "-"
        built = m("built_ms")
        print(f"| {' | '.join(key)} | {statistics.median(tot) / 1000:.2f} | {(max(tot) - min(tot)) / 1000:.2f} | {m('card_ms', 1)} | {built} | {m('main_ms')} | "
              f"{m('textures', 1)} ({m('hd', 1)}) | {m('tex_mb', 1)} | {m('pipelines', 1)} | {len(clean)}{'*' * marks} |")
    if not a.phases: return
    for key, rs in groups.items():
        per = [phases(r["csv"]) for r in rs]
        names = collections.OrderedDict()
        for p in per:
            for k in p: names[k] = True
        agg = []
        for k in names:
            ms = [p[k]["ms"] if k in p else 0 for p in per]
            first = next(p[k] for p in per if k in p)
            agg.append((statistics.median(ms), max(ms) - min(ms), k, first))
        agg.sort(key=lambda x: -x[0])
        print(f"\n**{' / '.join(key)}** (median of {len(rs)}): " + "; ".join(
            f"{k[0]} ({k[1]}) {med:.0f} ms ±{spr / 2:.0f}" + (f", {f['tex']} tex ({f['hd']} HD) {f['mb']:.0f} MB" if f['tex'] else "") + (f", {f['pipes']} pipelines" if f['pipes'] else "")
            for med, spr, k, f in agg[:a.top]))


if __name__ == "__main__":
    main()
