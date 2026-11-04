"""The bass shaker's band chart: which band each shaker source owns (lfe_bands.csv) over where the game's own sounds have
their low-frequency energy (sfx_bands.csv), both from `tools/TactileRender --sfx INSTALL DIR`.

    python tools/TactileRender/plot_bands.py DIR

Writes DIR/bands.png. Needs matplotlib and numpy.
"""
import csv, os, sys
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.ticker

GROUP = {"road": "#2a7ab0", "engine": "#555555", "knock": "#7a3fb0", "hit": "#c0392b", "explosion": "#e67e22", "shot": "#2a9a40"}


def main():
    d = sys.argv[1]
    lfe = list(csv.DictReader(open(os.path.join(d, "lfe_bands.csv"), newline="")))
    sfx = list(csv.DictReader(open(os.path.join(d, "sfx_bands.csv"), newline="")))
    centres = [float(c) for c in sfx[0].keys() if c not in ("sound", "play_rate_hz", "below_200_db")]
    fig, (a, b) = plt.subplots(2, 1, figsize=(12, 13), gridspec_kw={"height_ratios": [1.25, 1]}, sharex=True)
    # the shaker's sources
    for i, r in enumerate(lfe):
        lo, hi = float(r["lo_hz"]), float(r["hi_hz"])
        c = GROUP[r["group"]]
        if hi / lo < 1.02:
            a.plot([lo], [i], "o", color=c, ms=8)
        else:
            a.barh(i, hi - lo, left=lo, height=0.6, color=c, alpha=0.35 if r["kind"] == "continuous" else 0.8)
        a.text(205, i, r["name"], va="center", fontsize=8)
    a.set_yticks([]); a.invert_yaxis()
    a.set_title("The shaker's sources: each one's band (noise beds at their -3 dB edges, pale: continuous; tones as dots)")
    for x in (25, 30, 36, 50):
        a.axvline(x, color="#ccc", lw=0.8, ls=":")
    a.grid(axis="x", which="both", alpha=0.3)
    # the game's sounds, dB under each sound's loudest band below 200 Hz
    m = np.array([[float(r[str(c) if str(c) in r else ("%g" % c)]) for c in centres] for r in sfx])
    edges = np.array([c / 2 ** (1 / 6) for c in centres] + [centres[-1] * 2 ** (1 / 6)])
    im = b.pcolormesh(edges, np.arange(len(sfx) + 1), np.clip(m, -24, 0), cmap="magma", shading="flat")
    b.set_yticks(np.arange(len(sfx)) + 0.5, [r["sound"] for r in sfx], fontsize=8); b.invert_yaxis()
    b.set_xscale("log"); b.set_xlim(14, 260)
    b.set_xticks([16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200], ["16", "20", "25", "31.5", "40", "50", "63", "80", "100", "125", "160", "200"])
    b.xaxis.set_minor_formatter(matplotlib.ticker.NullFormatter())
    b.set_xlabel("Hz")
    b.set_title("The game's own sounds below 200 Hz: each third-octave band in dB under the sound's loudest (engines at idle and 5000 rpm)")
    fig.colorbar(im, ax=b, label="dB", fraction=0.025)
    fig.tight_layout(); fig.savefig(os.path.join(d, "bands.png"), dpi=110); plt.close(fig)
    print(os.path.join(d, "bands.png"))


if __name__ == "__main__":
    main()
