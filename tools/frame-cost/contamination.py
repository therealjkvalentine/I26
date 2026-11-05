#!/usr/bin/env python3
"""Marks runs that another GPU user overlapped: (a) the GPU telemetry log (every 20 s) shows utilisation over 20 % at a moment no run of ours
was on the GPU, within 25 s of the run; (b) the run's GPU total is 0.8 ms or more above the median of the configuration's other runs at the
same resolution and scene. Rewrites runs/contaminated.tsv (keeping hand-written lines) and prints what it marked."""
import os, sys, datetime as dt, statistics as st, collections
here = os.path.dirname(os.path.abspath(__file__))
TELE = os.environ.get("FRAME_COST_TELEMETRY", "")   # a log of `date time power, limit, temp, VRAM, clock, util %` lines every 20 s (nvidia-smi); optional
sys.path.insert(0, here)
import bench
rows = []
for l in open(os.path.join(bench.OUT, "results.tsv"), encoding="utf-8"):
    f = l.rstrip("\n").split("\t")
    if len(f) < 13 or f[1].startswith("proto") or f[12] == "FAILED": continue
    end = dt.datetime.fromisoformat(f[0]); start = end - dt.timedelta(seconds=float(f[9]))
    d = dict(kv.split("=") for kv in f[12].split())
    rows.append(dict(ts=f[0], name=f[1], res=f[2], rep=f[3], scene=f[4], others=f[11], start=start, end=end, gpu=float(d["gpu.total"])))
mine = [(r["start"], r["end"]) for r in rows]
def ours_at(t): return any(s - dt.timedelta(seconds=2) <= t <= e + dt.timedelta(seconds=6) for s, e in mine)   # a sample just after our run still shows our load
foreign = []
for l in (open(TELE, encoding="utf-8", errors="replace") if TELE and os.path.exists(TELE) else []):
    p = l.split()
    try:
        t = dt.datetime.fromisoformat(p[0] + "T" + p[1]); util = int(p[-2])
    except Exception: continue
    if util > 20 and not ours_at(t): foreign.append(t)
marked = {}
for r in rows:
    if r["others"] != "0": continue
    near = [t for t in foreign if r["start"] - dt.timedelta(seconds=25) <= t <= r["end"] + dt.timedelta(seconds=25)]
    if near: marked[(r["ts"], r["name"], r["res"], r["rep"])] = f"GPU telemetry shows foreign GPU use ({len(near)} sample(s) over 20 % within 25 s of the run)"
by = collections.defaultdict(list)
for r in rows:
    if r["others"] == "0" and (r["ts"], r["name"], r["res"], r["rep"]) not in marked: by[(r["name"], r["res"], r["scene"])].append(r)
for k, rs in by.items():
    if len(rs) < 3: continue
    for r in rs:
        others = [x["gpu"] for x in rs if x is not r]
        if r["gpu"] - st.median(others) >= 0.8:
            marked[(r["ts"], r["name"], r["res"], r["rep"])] = f"GPU total {r['gpu']:.2f} ms is {r['gpu'] - st.median(others):+.2f} over the configuration's other runs"
path = os.path.join(bench.OUT, "contaminated.tsv")
keep = [l for l in open(path, encoding="utf-8")] if os.path.exists(path) else []
keep = [l for l in keep if "GPU telemetry" not in l and "configuration's other runs" not in l]
with open(path, "w", encoding="utf-8") as f:
    f.writelines(keep)
    for (ts, name, res, rep), why in sorted(marked.items()): f.write(f"{ts}\t{name}\t{res}\t{rep}\t{why}\n")
print(len(foreign), "foreign telemetry samples;", len(marked), "runs marked")
for (ts, name, res, rep), why in sorted(marked.items()): print(ts, name, res, rep, "|", why[:70])
