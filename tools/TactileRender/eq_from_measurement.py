"""A bass shaker's tactile_eq from measured readings.

    python eq_from_measurement.py READINGS.csv [--unit db|linear] [--cap 12] [--from 16] [--to 100] [--out DIR]

READINGS.csv has a header and one row per frequency: `frequency_hz,measured_level`, the level as the meter reads it, in dB
(`--unit db`, the default) or linear (`--unit linear`: g, m/s^2, volts...; converted with 20*log10). Measure the flat
signals (tools/TactileRender --measure: sweep_flat.wav / steps_flat.wav, a constant -6 dBFS).

It prints the `tactile_eq = "..."` line for shell.cfg [port] (the format the game reads: "hz:db" points, comma-separated,
each within +-12 dB, interpolated in log frequency and held flat past the ends: src/I76.Formats/Input/TactileSynth.cs,
TactileEq), and writes it to DIR/tactile_eq.txt with a plot, DIR/eq_plot.svg (and DIR/eq_plot.png when matplotlib is there).

The curve: the readings in dB, put on the third-octave centres (the EQ's own band resolution: the game's road bed goes
through a third-octave bank) by interpolation in log frequency, smoothed over neighbouring centres (1/4, 1/2, 1/4), then
inverted so every centre from --from to --to Hz reaches the level of the strongest one: the strongest band stays at 0 dB and
the others are boosted, by at most --cap dB (12, the game's own limit). Outside --from..--to the end values are held, as the
game holds the curve past its ends. Needs only the standard library.
"""
import argparse, csv, math, os, sys

CENTRES = [10, 12.5, 16, 20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200]


def read(path, unit):
    pts = []
    with open(path, newline="", encoding="utf-8-sig") as f:
        for row in csv.reader(f):
            if not row or row[0].strip().startswith("#"):
                continue
            try:
                hz, v = float(row[0]), float(row[1])
            except (ValueError, IndexError):
                continue          # the header, or a stray line
            if hz <= 0:
                continue
            if unit == "linear":
                if v <= 0:
                    sys.exit(f"{path}: a linear reading must be above 0 ({hz} Hz: {v})")
                v = 20 * math.log10(v)
            pts.append((hz, v))
    if len(pts) < 2:
        sys.exit(f"{path}: need at least two readings")
    return sorted(pts)


def interp_log(pts, hz):
    """Linear in log frequency between points, held past the ends (as the game's TactileEq)."""
    if hz <= pts[0][0]:
        return pts[0][1]
    if hz >= pts[-1][0]:
        return pts[-1][1]
    for (f0, v0), (f1, v1) in zip(pts, pts[1:]):
        if f0 <= hz <= f1:
            u = math.log(hz / f0) / math.log(f1 / f0)
            return v0 + (v1 - v0) * u
    return pts[-1][1]


def make_eq(pts, cap, lo, hi):
    centres = [c for c in CENTRES if pts[0][0] / 1.12 <= c <= pts[-1][0] * 1.12] or CENTRES
    raw = [interp_log(pts, c) for c in centres]
    smooth = []
    for i in range(len(raw)):
        a, b = raw[max(i - 1, 0)], raw[min(i + 1, len(raw) - 1)]
        smooth.append(0.25 * a + 0.5 * raw[i] + 0.25 * b)
    inside = [s for c, s in zip(centres, smooth) if lo <= c <= hi] or smooth
    ref = max(inside)
    eq = []
    for c, s in zip(centres, smooth):
        cc = min(max(c, lo), hi)
        sv = interp_log(list(zip(centres, smooth)), cc)
        eq.append((c, max(0.0, min(cap, ref - sv))))
    return centres, raw, smooth, ref, eq


def fmt(eq):
    return ", ".join(f"{('%g' % c)}:{d:.1f}" for c, d in eq)


def svg_plot(path, pts, centres, smooth, ref, eq, cap):
    W, H, L, R, T, B = 760, 420, 60, 20, 30, 50
    fmin, fmax = 8, 250
    vals = [v - ref for _, v in pts] + [s - ref for s in smooth] + [d for _, d in eq] + [0, cap]
    ymin, ymax = math.floor(min(vals) / 6) * 6 - 3, math.ceil(max(vals) / 6) * 6 + 3

    def X(f): return L + (W - L - R) * math.log(f / fmin) / math.log(fmax / fmin)
    def Y(v): return T + (H - T - B) * (ymax - v) / (ymax - ymin)
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" font-family="sans-serif" font-size="11">',
           f'<rect width="{W}" height="{H}" fill="white"/>']
    for f in [10, 20, 31.5, 50, 80, 125, 200]:
        out.append(f'<line x1="{X(f):.1f}" y1="{T}" x2="{X(f):.1f}" y2="{H - B}" stroke="#ddd"/><text x="{X(f):.1f}" y="{H - B + 15}" text-anchor="middle">{f:g}</text>')
    v = ymin
    while v <= ymax:
        out.append(f'<line x1="{L}" y1="{Y(v):.1f}" x2="{W - R}" y2="{Y(v):.1f}" stroke="#eee"/><text x="{L - 6}" y="{Y(v) + 4:.1f}" text-anchor="end">{v:g}</text>')
        v += 3
    out.append(f'<text x="{(L + W - R) / 2}" y="{H - 12}" text-anchor="middle">Hz</text><text x="14" y="{T + 10}">dB</text>')

    def line(xy, colour, dash=""):
        d = " ".join(f"{X(f):.1f},{Y(v):.1f}" for f, v in xy)
        return f'<polyline points="{d}" fill="none" stroke="{colour}" stroke-width="2" {dash}/>'
    fs = [fmin * (fmax / fmin) ** (i / 200) for i in range(201)]
    eqc = lambda f: interp_log(eq, f)
    out.append(line([(f, interp_log(pts, f) - ref) for f in fs], "#2a7ab0"))
    out += [f'<circle cx="{X(f):.1f}" cy="{Y(v - ref):.1f}" r="3" fill="#2a7ab0"/>' for f, v in pts]
    out.append(line([(f, eqc(f)) for f in fs], "#c06000"))
    out.append(line([(f, interp_log(pts, f) - ref + eqc(f)) for f in fs], "#2a9a40"))
    out.append(line([(fmin, 0), (fmax, 0)], "#888", 'stroke-dasharray="4 3"'))
    for i, (t, c) in enumerate([("measured (0 dB: the strongest band)", "#2a7ab0"), ("tactile_eq", "#c06000"),
                                ("result: measured + EQ", "#2a9a40"), ("target: flat at the strongest band", "#888")]):
        out.append(f'<rect x="{L + 10}" y="{T + 6 + 16 * i}" width="14" height="3" fill="{c}"/><text x="{L + 30}" y="{T + 10 + 16 * i}">{t}</text>')
    out.append("</svg>")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(out))


def png_plot(path, pts, ref, eq):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        return False
    fs = [8 * (250 / 8) ** (i / 300) for i in range(301)]
    fig, ax = plt.subplots(figsize=(8, 4.5))
    ax.semilogx(fs, [interp_log(pts, f) - ref for f in fs], color="#2a7ab0", label="measured (0 dB: the strongest band)")
    ax.semilogx([f for f, _ in pts], [v - ref for _, v in pts], "o", color="#2a7ab0")
    ax.semilogx(fs, [interp_log(eq, f) for f in fs], color="#c06000", label="tactile_eq")
    ax.semilogx(fs, [interp_log(pts, f) - ref + interp_log(eq, f) for f in fs], color="#2a9a40", label="result: measured + EQ")
    ax.axhline(0, color="#888", ls="--", lw=1, label="target")
    ax.set(xlabel="Hz", ylabel="dB"); ax.grid(True, which="both", alpha=0.3); ax.legend(fontsize=8)
    fig.tight_layout(); fig.savefig(path, dpi=110); plt.close(fig)
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("csv")
    ap.add_argument("--unit", choices=["db", "linear"], default="db")
    ap.add_argument("--cap", type=float, default=12.0, help="the most boost, dB (the game holds each point within 12)")
    ap.add_argument("--from", dest="lo", type=float, default=16.0, help="the lowest frequency corrected (Hz); below it the value there is held")
    ap.add_argument("--to", dest="hi", type=float, default=100.0, help="the highest frequency corrected (Hz)")
    ap.add_argument("--out", default=None, help="where tactile_eq.txt and the plot go (default: next to the CSV)")
    a = ap.parse_args()
    pts = read(a.csv, a.unit)
    centres, raw, smooth, ref, eq = make_eq(pts, min(a.cap, 12.0), a.lo, a.hi)
    # the points from --from to --to only: the game holds the curve flat past its ends, as the values held there
    line = fmt([(c, d) for c, d in eq if a.lo <= c <= a.hi] or eq)
    out = a.out or os.path.dirname(os.path.abspath(a.csv))
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "tactile_eq.txt"), "w", encoding="utf-8") as f:
        f.write(f'tactile_eq = "{line}"\n')
    svg_plot(os.path.join(out, "eq_plot.svg"), pts, centres, smooth, ref, eq, a.cap)
    png = png_plot(os.path.join(out, "eq_plot.png"), pts, ref, eq)
    print("centre  measured  smoothed  eq")
    for c, r, s, (_, d) in zip(centres, raw, smooth, eq):
        print(f"{c:6g}  {r - ref:8.1f}  {s - ref:8.1f}  {d:+5.1f}")
    print()
    print("[port]")
    print(f'tactile_eq = "{line}"')
    print()
    print(f"written to {os.path.join(out, 'tactile_eq.txt')}; plot {os.path.join(out, 'eq_plot.svg')}" + (" and eq_plot.png" if png else ""))


if __name__ == "__main__":
    main()
