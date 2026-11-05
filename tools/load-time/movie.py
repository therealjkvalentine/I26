#!/usr/bin/env python3
"""The briefing's pace and the start's times per run, from loadprofile.py's results (docs/perf/loadtime-plan.md, round 2):
  python movie.py OUT [TAG_PREFIX...]
One row a run: the tag, the mission, to the mission, the movie's start and end, the card after it (the mission - the movie's
end), the frames shown / dropped / late, the worst gap between two frames shown, the audio generator's underrun frames after
the first frame, the longest load step run under the movie.
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import summarise as sm

out = sys.argv[1]
prefixes = sys.argv[2:]
print("| run | mission | to the mission s | movie start s | movie over s | card after the movie s | frames shown | dropped | late | worst gap ms | audio underrun frames | longest step under the movie ms |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|")
for r in sm.load(out):
    if prefixes and not any(r["tag"].startswith(p) for p in prefixes): continue
    x = r["res"]
    if not x: print(f"| {r['tag']} | {r['mission']} | FAILED |"); continue
    def s(k):
        v = x.get(k, "-")
        return f"{int(v) / 1000:.2f}" if v.lstrip("-").isdigit() else "-"
    after = f"{(int(x['total_ms']) - int(x['movie_over_ms'])) / 1000:.2f}" if x.get("movie_over_ms", "-").isdigit() else "-"
    print(f"| {r['tag']} | {r['mission']} | {s('total_ms')} | {s('movie_ms')} | {s('movie_over_ms')} | {after} | {x.get('movie_shown', '-')} | {x.get('movie_dropped', '-')} | "
          f"{x.get('movie_late', '-')} | {x.get('movie_worst_ms', '-')} | {x.get('audio_skips', '-')} | {x.get('attach_worst_step_ms', '-')} |")
