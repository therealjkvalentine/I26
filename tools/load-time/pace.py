#!/usr/bin/env python3
"""A briefing's picture pace against what the load did under it (docs/perf/loadtime.md, "The briefing's pace"):
  python pace.py RUN.pace.csv [RUN.profile.csv] [--all]

RUN.pace.csv is the game's I76_MOVIE_PACE log (one row a frame of the game while a Smacker movie plays), RUN.profile.csv its
I76_LOAD_PROFILE_CSV (the load's phases by thread). Only the frames while the mission loads under the picture count (the
`loading` column), unless --all. Prints the movie's frames, those that came up over 1.5 frame times late (MoviePace's rule: due
on the steadiest schedule of the last 0.5 s), the game's longest frame, the picture against the sound, and one line for each of
the game's frames over 1.5 movie frames (100 ms) with what happened in it: collections and their pause, pipelines compiled,
video memory taken by textures and buffers (the uploads), the load's last stamp and the build thread's phase then.
"""
import csv, sys

FRAME = 1 / 15.0
LATE, WINDOW = 1.5, 0.5


def late_frames(shown):
    """shown: [(t_s, index)] increasing index. Returns (late count, worst lateness s), as MoviePace.Late."""
    late, worst, start = 0, 0.0, 0
    for s, (at, idx) in enumerate(shown):
        while start < s - 1 and shown[start][0] < at - WINDOW:
            start += 1
        anchor = min(t - i * FRAME for t, i in shown[start:s + 1])
        first = idx if s == 0 else shown[s - 1][1] + 1
        for k in range(first, idx + 1):
            by = at - (anchor + k * FRAME)
            worst = max(worst, by)
            if by > LATE * FRAME:
                late += 1
    return late, worst


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    every = "--all" in sys.argv
    rows = list(csv.DictReader(open(args[0], newline="")))
    if not every:
        rows = [r for r in rows if r["loading"] == "1"]
    if not rows:
        print("no frames"); return
    phases = []
    first_frame_ms = None
    if len(args) > 1:
        for p in csv.DictReader(open(args[1], newline="")):
            if p["thread"] == "event": continue
            if p["phase"].startswith("movie: first frame") and first_frame_ms is None:
                first_frame_ms = int(p["end_ms"])
            phases.append((int(p["start_ms"]), int(p["end_ms"]), p["thread"], p["phase"]))

    def build_phase(t_ms):
        if first_frame_ms is None: return ""
        at = first_frame_ms + t_ms
        names = [f"{ph} ({e - s} ms)" for s, e, th, ph in phases if th == "mission-build" and s <= at <= e]
        return names[0] if names else "-"

    shown, last = [], -1
    for r in rows:
        i = int(r["shown"])
        if i > last:
            shown.append((float(r["t_ms"]) / 1000, i)); last = i
    late, worst = late_frames(shown)
    frames = shown[-1][1] - shown[0][1] + 1 if shown else 0
    span = (float(rows[-1]["t_ms"]) - float(rows[0]["t_ms"])) / 1000
    longest = max(float(r["frame_ms"]) for r in rows[1:]) if len(rows) > 1 else 0
    offs = [float(r["offset_ms"]) for r in rows if r["offset_ms"] not in ("NaN", "nan", "")]
    gc = lambda r, k: int(r[k])
    print(f"{args[0]}: {span:.1f} s, {len(rows)} game frames ({len(rows) / max(span, 1e-9):.0f} a second), {frames} movie frames, "
          f"{late} over {LATE} frame times late (latest {worst * 1000:.0f} ms), longest game frame {longest:.0f} ms; "
          f"picture - sound {min(offs, default=0):.0f} to {max(offs, default=0):.0f} ms; "
          f"collections gen0 {gc(rows[-1], 'gc0') - gc(rows[0], 'gc0')} gen1 {gc(rows[-1], 'gc1') - gc(rows[0], 'gc1')} gen2 {gc(rows[-1], 'gc2') - gc(rows[0], 'gc2')}, "
          f"pause {float(rows[-1]['gc_pause_ms']) - float(rows[0]['gc_pause_ms']):.0f} ms")
    print(f"RESULT frames={frames} late15={late} worst_late_ms={worst * 1000:.0f} worst_frame_ms={longest:.0f}")
    for a, b in zip(rows, rows[1:]):
        ms = float(b["frame_ms"])
        if ms <= LATE * FRAME * 1000: continue
        d = lambda k, f=float: f(b[k]) - f(a[k])
        print(f"  {float(b['t_ms']) / 1000:7.2f} s  {ms:5.0f} ms  gc {int(d('gc0', int))}/{int(d('gc1', int))}/{int(d('gc2', int))} pause {d('gc_pause_ms'):4.0f} ms  "
              f"draw {float(b.get('draw_ms') or 0):4.0f} ms  pipelines {int(d('pipelines', int))}  tex +{d('tex_mb'):.1f} MB  buf +{d('buf_mb'):.1f} MB  stamp '{b['stage']}'  build: {build_phase(float(b['t_ms']))}")


if __name__ == "__main__":
    main()
