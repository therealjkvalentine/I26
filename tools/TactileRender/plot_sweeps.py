"""Plots the bass-shaker sweeps (tools/TactileRender --sweep DIR) and in-game LFE logs (I76_LFE_LOG) as PNGs.

    python tools/TactileRender/plot_sweeps.py DIR [--game LOG SPEED ...] [--before BEFORE_DIR]

Needs matplotlib and numpy. Writes DIR/*.png and prints the numbers the verdicts quote.
"""
import csv, math, re, sys, os
from collections import defaultdict
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

COL = {"asphalt": "#555555", "dirt road": "#c06000", "planks": "#8a6d3b", "dirt": "#2a7ab0", "sand": "#d4a017", "rough": "#7a3fb0",
       "parking lot": "#888888", "dirt junction": "#e08040", "air": "#cccccc", "none": "#eeeeee"}


def rows(path):
    with open(path, newline="") as f:
        return list(csv.DictReader(f))


def fl(r, k):
    return float(r[k])


def plot_filters(d):
    r = rows(os.path.join(d, "a_filters.csv"))
    hz = [fl(x, "hz") for x in r]
    fig, ax = plt.subplots(1, 2, figsize=(13, 4.8))
    worst = {}
    for c, col in (("60", "#2a7ab0"), ("90", "#c06000"), ("120", "#2a9a40")):
        m = [fl(x, f"measured_{c}") for x in r]
        i = [fl(x, f"intended_{c}") for x in r]
        ax[0].semilogx(hz, i, color=col, lw=1, ls="--")
        ax[0].semilogx(hz, m, color=col, lw=2, alpha=0.8, label=f"crossover {c} Hz: measured (solid), intended (dashed)")
        worst[c] = max(abs(a - b) for a, b, f in zip(m, i, hz) if 15 <= f <= 160)
    ax[0].set(title="(a) the chain's gain: 15 Hz high-pass x crossover low-pass", xlabel="Hz", ylabel="dB", ylim=(-24, 2))
    ax[0].legend(fontsize=8); ax[0].grid(True, which="both", alpha=0.3)
    m = [fl(x, "eq_tone_measured") for x in r]
    i = [fl(x, "eq_tone_intended") for x in r]
    ax[1].semilogx(hz, i, color="k", ls="--", lw=1, label="intended: EQ curve x filters")
    ax[1].semilogx(hz, m, color="#c06000", lw=2, alpha=0.8, label="measured: a tone through the synth")
    b = rows(os.path.join(d, "a_eq_bed.csv"))
    ax[1].semilogx([fl(x, "hz") for x in b], [fl(x, "bed_ratio_db") for x in b], "o", color="#2a7ab0", label="road bed: with EQ over without (1/3 oct)")
    ax[1].semilogx([fl(x, "hz") for x in b], [fl(x, "curve_db") for x in b], "x", color="k", label="road bed: the curve at the centre")
    ax[1].set(title='(a) tactile_eq "20:3.6, 35:-3.5, 60:0", crossover 90', xlabel="Hz", ylabel="dB", ylim=(-14, 6))
    ax[1].legend(fontsize=8); ax[1].grid(True, which="both", alpha=0.3)
    fig.tight_layout(); fig.savefig(os.path.join(d, "a_filters.png"), dpi=110); plt.close(fig)
    eqtone = max(abs(a - b) for a, b, f in zip(m, i, hz) if 15 <= f <= 160)
    eqbed = max(abs(fl(x, "bed_ratio_db") - fl(x, "curve_db")) for x in b)
    print(f"(a) worst filter error 15-160 Hz: " + ", ".join(f"{k} Hz {v:.2f} dB" for k, v in worst.items()) + f"; EQ tone {eqtone:.2f} dB; EQ bed {eqbed:.2f} dB")


def plot_surfaces(d):
    r = rows(os.path.join(d, "b_surfaces.csv"))
    by = defaultdict(list)
    for x in r:
        by[x["surface"]].append(x)
    fig, ax = plt.subplots(1, 3, figsize=(16, 4.8))
    for s, xs in by.items():
        v = [fl(x, "mps") for x in xs]
        c = COL[s]
        ax[0].plot(v, [fl(x, "centroid_hz") for x in xs], "-o", color=c, label=f"{s} centroid")
        ax[0].plot(v, [fl(x, "dominant_hz") for x in xs], ":", color=c, alpha=0.8)
        ax[0].fill_between(v, [fl(x, "band_lo_hz") for x in xs], [fl(x, "band_hi_hz") for x in xs], color=c, alpha=0.08)
        ax[1].plot(v, [fl(x, "rms_dbfs") for x in xs], "-o", color=c, label=s)
    ax[0].set(title="(b) pitch: centroid (solid), dominant (dotted), -3 dB band (shaded)", xlabel="m/s", ylabel="Hz", ylim=(15, 90))
    ax[0].legend(fontsize=7, ncol=2); ax[0].grid(alpha=0.3)
    ax[1].set(title="(b) RMS level at LFE 100 %, springs still", xlabel="m/s", ylabel="dBFS"); ax[1].legend(fontsize=8); ax[1].grid(alpha=0.3)
    v = [fl(x, "mps") for x in by["asphalt"]]
    gap = [fl(b, "rms_dbfs") - fl(a, "rms_dbfs") for a, b in zip(by["asphalt"], by["sand"])]
    gapd = [fl(b, "rms_dbfs") - fl(a, "rms_dbfs") for a, b in zip(by["asphalt"], by["dirt"])]
    pitch = [fl(g, "centroid_hz") - max(fl(o, "centroid_hz") for o in (a, b, c))
             for g, a, b, c in zip(by["dirt road"], by["dirt"], by["sand"], by["rough"])]
    ax[2].plot(v, gap, "-o", color=COL["sand"], label="sand over asphalt (dB)")
    ax[2].plot(v, gapd, "-o", color=COL["dirt"], label="dirt over asphalt (dB)")
    ax[2].axhline(14.7, color=COL["sand"], ls="--", lw=1, label="design: sand bed 14.7 dB over asphalt")
    ax[2].plot(v, pitch, "-s", color=COL["dirt road"], label="dirt road centroid over the highest off-road (Hz)")
    ax[2].set(title="(b) the design targets against speed", xlabel="m/s"); ax[2].legend(fontsize=8); ax[2].grid(alpha=0.3)
    fig.tight_layout(); fig.savefig(os.path.join(d, "b_surfaces.png"), dpi=110); plt.close(fig)
    quiet = all(min(by, key=lambda s: fl(by[s][k], "rms_dbfs")) == "asphalt" for k in range(len(v)))
    print(f"(b) sand-asphalt gap {min(gap):.1f} to {max(gap):.1f} dB; dirt-asphalt {min(gapd):.1f} to {max(gapd):.1f}; "
          f"dirt road centroid over off-road {min(pitch):.1f} to {max(pitch):.1f} Hz; asphalt quietest at every speed: {quiet}")
    # spectra at 15 and 30 m/s
    p = rows(os.path.join(d, "b_psd.csv"))
    fig, ax = plt.subplots(1, 2, figsize=(13, 4.5))
    for k, mps in enumerate(("15", "30")):
        for x in p:
            if x["mps"] != mps:
                continue
            f = [int(c) for c in x if c not in ("surface", "mps")]
            db = np.array([float(x[str(c)]) for c in f])
            db = np.convolve(db, np.ones(5) / 5, "same")
            ax[k].plot(f, db, color=COL[x["surface"]], label=x["surface"])
        ax[k].set(title=f"(b) spectra at {mps} m/s (5 Hz smoothed)", xlabel="Hz", ylabel="dB", xlim=(10, 150)); ax[k].legend(fontsize=8); ax[k].grid(alpha=0.3)
    fig.tight_layout(); fig.savefig(os.path.join(d, "b_spectra.png"), dpi=110); plt.close(fig)


def plot_knocks(d, before):
    fig, ax = plt.subplots(1, 2, figsize=(13, 4.5))
    sets = [("after (BumpLevel 0.6)", rows(os.path.join(d, "c_knocks.csv")), "-o")]
    if before:
        sets.insert(0, ("before (BumpLevel 0.35)", rows(os.path.join(before, "c_knocks.csv")), "--s"))
    for name, r, st in sets:
        v = [fl(x, "mps") for x in r]
        ax[0].plot(v, [fl(x, "peak_dbfs") for x in r], st, color="#c06000", label=f"{name}: one wheel's peak")
        ax[0].plot(v, [fl(x, "axle_peak_dbfs") for x in r], st, color="#7a3fb0", label=f"{name}: an axle's peak")
        ax[1].plot(v, [fl(x, "knock_over_sand_db") for x in r], st, color=COL["sand"], label=f"{name}: over the sand bed")
        ax[1].plot(v, [fl(x, "knock_over_asphalt_db") for x in r], st, color=COL["asphalt"], label=f"{name}: over the asphalt bed")
    r = sets[-1][1]
    ax[0].plot([fl(x, "mps") for x in r], [fl(x, "expected_peak_dbfs") for x in r], ":", color="k", label="nominal (the thump's start level)")
    ax[0].set(title=f"(c) the crossing knock: peak (to -40 dB in {fl(r[0], 'to_minus40db_ms'):.0f} ms, -20 dB in {fl(r[0], 'to_minus20db_ms'):.0f} ms)", xlabel="m/s", ylabel="dBFS")
    ax[0].legend(fontsize=7); ax[0].grid(alpha=0.3)
    ax[1].axhline(0, color="k", lw=0.8)
    ax[1].set(title="(c) knock peak over the bed's sine-equivalent peak", xlabel="m/s", ylabel="dB"); ax[1].legend(fontsize=7); ax[1].grid(alpha=0.3)
    fig.tight_layout(); fig.savefig(os.path.join(d, "c_knocks.png"), dpi=110); plt.close(fig)
    print("(c) " + "; ".join(f"{x['mps']} m/s peak {fl(x, 'peak_dbfs'):.1f} dBFS (nominal {fl(x, 'expected_peak_dbfs'):.1f}), "
                             f"-40 dB at {fl(x, 'to_minus40db_ms'):.0f} ms, over sand {fl(x, 'knock_over_sand_db'):+.1f} dB" for x in r))


def plot_limiter(d):
    r = rows(os.path.join(d, "d_limiter.csv"))
    o = rows(os.path.join(d, "d_occupancy.csv"))
    fig, ax = plt.subplots(1, 2, figsize=(13, 4.8))
    i = np.array([fl(x, "input_dbfs") for x in r])
    ax[0].plot(i, [fl(x, "output_dbfs") for x in r], color="#c06000", lw=2, label="measured: a 40 Hz sine's peak")
    ax[0].plot(i, i, "k:", label="no limiter")
    ax[0].plot(i, 20 * np.log10(0.85 * np.tanh(10 ** (i / 20) / 0.85)), "k--", lw=1, label="intended: 0.85 tanh(x/0.85)")
    ax[0].axhline(20 * math.log10(0.85), color="grey", lw=0.8)
    knee = open(os.path.join(d, "d_knee.txt")).read().strip()
    kdb = float(re.search(r"input of (-?[\d.]+)", knee).group(1))
    ax[0].axvline(kdb, color="#2a7ab0", ls="--", lw=1, label=f"1 dB of reduction at {kdb:.1f} dBFS in")
    plan = os.path.join(d, "plan.csv")
    if os.path.exists(plan):
        # where each source peaks (tools/TactileRender --plan, LFE 100 %): on the curve, by group
        marks = {"impact": ("o", "#c0392b"), "explosion": ("D", "#e67e22"), "knock": ("s", "#7a3fb0"), "shot": ("^", "#2a9a40"), "road": ("v", "#2a7ab0"), "engine": ("x", "k")}
        seen = set()
        for x in rows(plan):
            if x["tuning"] != "after":
                continue
            pk = fl(x, "peak_dbfs")
            m, c = marks.get(x["group"], ("+", "k"))
            ax[0].plot([pk], [20 * math.log10(0.85 * math.tanh(10 ** (pk / 20) / 0.85))], m, color=c, ms=6,
                       label=f"{x['group']} peaks (--plan)" if x["group"] not in seen else None)
            seen.add(x["group"])
    ax[0].set(title="(d) the limiter: input peak against output peak", xlabel="input dBFS", ylabel="output dBFS", ylim=(-30, 1))
    ax[0].legend(fontsize=8); ax[0].grid(alpha=0.3)
    labels, vals, cols = [], [], []
    for x in o:
        labels.append(f"{x['surface']} {x['lfe_percent']}% {x['springs']}")
        vals.append(100 * fl(x, "share_over_1db"))
        cols.append(COL[x["surface"]])
    y = np.arange(len(labels))
    ax[1].barh(y, vals, color=cols)
    ax[1].set_yticks(y, labels, fontsize=7); ax[1].invert_yaxis()
    ax[1].set(title="(d) 45 m/s: share of time in 1 dB or more of limiting", xlabel="% of samples")
    ax[1].grid(axis="x", alpha=0.3)
    fig.tight_layout(); fig.savefig(os.path.join(d, "d_limiter.png"), dpi=110); plt.close(fig)
    werr = max(abs(fl(x, "output_dbfs") - 20 * math.log10(0.85 * math.tanh(10 ** (fl(x, "input_dbfs") / 20) / 0.85))) for x in r)
    print(f"(d) {knee}; worst error against tanh {werr:.2f} dB; " + "; ".join(
        f"{x['surface']} {x['lfe_percent']}% {x['springs']} {100 * fl(x, 'share_over_1db'):.0f}% (mean {fl(x, 'mean_reduction_db'):.1f} dB)" for x in o))


LINE = re.compile(r"lfe: t ([\d.]+), x (-?[\d.]+), z (-?[\d.]+), speed ([\d.]+) m/s, LFE road: ([a-z ]+), bed (\d+) Hz at (-?[\d.]+|off)(?: dB)?, wheels (\S+)(?: shoulder)?, bumps (\d+)(?:, lumps \d+)?, rms (-?[\d.]+) dBFS, peak (-?[\d.]+) dBFS")


def plot_game(d, logs):
    surf = {x["surface"]: x for x in []}
    off = defaultdict(dict)
    for x in rows(os.path.join(d, "b_surfaces.csv")):
        off[x["surface"]][int(x["mps"])] = x
    fig, ax = plt.subplots(len(logs), 1, figsize=(15, 4.6 * len(logs)), squeeze=False)
    for k, (log, target) in enumerate(logs):
        t, sp, name, bed, lvl, rms, bumps = [], [], [], [], [], [], []
        for line in open(log, encoding="utf-8", errors="replace"):
            m = LINE.search(line)
            if not m:
                continue
            t.append(float(m.group(1))); sp.append(float(m.group(4))); name.append(m.group(5).strip())
            bed.append(int(m.group(6))); lvl.append(-60 if m.group(7) == "off" else float(m.group(7)))
            rms.append(max(float(m.group(10)), -60)); bumps.append(int(m.group(9)))
        t = np.array(t); a = ax[k][0]
        # the surface under the car, as shaded spans
        i = 0
        while i < len(t):
            j = i
            while j < len(t) and name[j] == name[i]:
                j += 1
            a.axvspan(t[i], t[min(j, len(t) - 1)], color=COL.get(name[i], "#eeeeee"), alpha=0.18, lw=0)
            if t[min(j, len(t) - 1)] - t[i] > 1.5:
                a.text((t[i] + t[min(j, len(t) - 1)]) / 2, -57, name[i], ha="center", fontsize=8)
            i = j
        a.plot(t, rms, color="k", lw=0.6, alpha=0.6, label="game: RMS over 50 ms (dBFS)")
        a.plot(t, lvl, color="#c06000", lw=1.2, label="game: road bed level, synth (dB, before the limiter)")
        # the offline sweep's RMS for the surface at the target speed (springs still)
        ref = [float(off[n][target]["rms_dbfs"]) if n in off and target in off[n] else np.nan for n in name]
        a.plot(t, ref, color="#2a7ab0", lw=2, label=f"offline sweep: the surface at {target} m/s, springs still (RMS dBFS)")
        knocks = [t[q] for q in range(1, len(t)) if bumps[q] > bumps[q - 1]]
        for q in knocks:
            a.axvline(q, color="#7a3fb0", lw=0.8, alpha=0.7)
        a2 = a.twinx()
        a2.plot(t, bed, color="#2a9a40", lw=1.5, drawstyle="steps-post", label="game: bed band (Hz)")
        a2.plot(t, sp, color="#2a9a40", lw=0.8, ls=":", label="speed (m/s)")
        a2.set_ylabel("Hz / m/s"); a2.set_ylim(0, 80)
        a.set(title=f"In-game drive, cruise {target} m/s ({os.path.basename(log)}): purple lines are crossing knocks", xlabel="s", ylabel="dB", ylim=(-60, 0))
        h1, l1 = a.get_legend_handles_labels(); h2, l2 = a2.get_legend_handles_labels()
        a.legend(h1 + h2, l1 + l2, fontsize=7, loc="upper left", ncol=2); a.grid(alpha=0.3)
        # per surface, at speed within 10 % of the target: the median RMS against the offline value
        stats = defaultdict(list)
        for q in range(len(t)):
            if abs(sp[q] - target) < 0.1 * target:
                stats[name[q]].append((rms[q], lvl[q]))
        out = []
        for n, v in stats.items():
            if n in off and target in off[n] and len(v) > 20:
                med = float(np.median([x[0] for x in v])); low = float(np.percentile([x[0] for x in v], 10))
                out.append(f"{n}: median {med:.1f}, 10th pct {low:.1f} dBFS vs offline {float(off[n][target]['rms_dbfs']):.1f} (n {len(v)})")
        print(f"game {target} m/s: knocks {len(knocks)}; " + "; ".join(out))
    fig.tight_layout(); fig.savefig(os.path.join(d, "e_game_drive.png"), dpi=110); plt.close(fig)


def main():
    a = sys.argv[1:]
    d = a[0]
    before = None
    logs = []
    i = 1
    while i < len(a):
        if a[i] == "--before":
            before = a[i + 1]; i += 2
        elif a[i] == "--game":
            logs.append((a[i + 1], int(a[i + 2]))); i += 3
        else:
            i += 1
    plot_filters(d)
    plot_surfaces(d)
    plot_knocks(d, before)
    plot_limiter(d)
    if logs:
        plot_game(d, logs)


if __name__ == "__main__":
    main()
