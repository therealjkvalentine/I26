#!/usr/bin/env python3
"""Checks the HD packs' mip chains: for each .dds, level 1 against the top level box-filtered (tools/dds_mips.py), and with --fix
makes the chain of every file that fails again from its top level (the top level kept byte for byte).

    python tools/dds-mips.py PACK [PACK ...] [--from LIST] [--fix] [--before DIR] [--list FILE] [--jobs N] [--only GLOB]

PACK: a pack folder (tex-world-4x, tex-world-8x); its dot-folders (.text-base, .recut-prev, ...: earlier files kept by the tools)
are not served and not read. --before DIR: each file is kept in DIR/<pack folder name>/ (a hard link, or a copy) before it is rewritten (once). --list FILE:
the failing files' paths, one a line. A file is rewritten through a .part file and a replace, so hard links to it keep the old file.

The writers of tools/upscale-textures.py and tools/text-redraws.py made the chain with Pillow's RGBA resize, which weights colour
by alpha: every keyed texel's colour was black from level 1 down while the top level carries the colour bled under the key."""
import argparse, fnmatch, os, shutil, sys, time
from concurrent.futures import ProcessPoolExecutor
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dds_mips

def files_of(pack, only):
    return sorted(os.path.join(pack, f) for f in os.listdir(pack)
                  if f.lower().endswith(".dds") and fnmatch.fnmatch(f.lower(), only.lower()) and os.path.isfile(os.path.join(pack, f)))

def one(path):
    try:
        stale, share, black, hd = dds_mips.check(path)
        return path, stale, share, black, hd["mips"], hd["dxgi"], None
    except Exception as e:
        return path, False, 0.0, 0.0, 0, None, f"{type(e).__name__}: {e}"

def fix_one(path):
    try:
        import etcpak
        dds_mips.regenerate(path, etcpak); return path, None
    except Exception as e:
        return path, f"{type(e).__name__}: {e}"

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("packs", nargs="*")
    ap.add_argument("--from", dest="from_list", help="check (and fix) the .dds files listed in this file (an earlier --list) instead of whole packs")
    ap.add_argument("--fix", action="store_true", help="make the failing files' chains again from their top levels")
    ap.add_argument("--before", help="copy each file to BEFORE/<pack name>/ before it is rewritten")
    ap.add_argument("--list", help="write the failing files' paths here")
    ap.add_argument("--only", default="*", help="file name glob")
    ap.add_argument("--jobs", type=int, default=max(1, (os.cpu_count() or 2) - 1))
    a = ap.parse_args()
    t0 = time.time(); bad = []; errs = 0; n = 0
    groups = [(pack, files_of(pack, a.only)) for pack in a.packs]
    if a.from_list:
        with open(a.from_list, encoding="utf-8") as f: groups.append((a.from_list, [l.strip() for l in f if l.strip()]))
    if not groups: ap.error("a pack folder or --from LIST is needed")
    for pack, files in groups:
        n += len(files); nb = 0
        with ProcessPoolExecutor(a.jobs) as ex:
            for path, stale, share, black, mips, dxgi, err in ex.map(one, files, chunksize=8):
                if err: errs += 1; print(f"  error {path}: {err}"); continue
                if stale: bad.append(path); nb += 1
        print(f"{pack}: {len(files)} files, {nb} with a stale or wrong mip chain ({time.time() - t0:.0f} s)")
    print(f"{len(bad)} of {n} files fail" + (f", {errs} unreadable" if errs else ""))
    if a.list:
        with open(a.list, "w", encoding="utf-8") as f: f.writelines(p + "\n" for p in bad)
    if a.fix and bad:
        if a.before:                                           # every old file kept before any is rewritten
            for path in bad:
                d = os.path.join(a.before, os.path.basename(os.path.dirname(os.path.abspath(path)))); os.makedirs(d, exist_ok=True)
                b = os.path.join(d, os.path.basename(path))
                if not os.path.exists(b):
                    try: os.link(path, b)                      # the old file kept by a hard link (the rewrite replaces the name)
                    except OSError: shutil.copy2(path, b)
        done = 0
        with ProcessPoolExecutor(a.jobs) as ex:
            for path, err in ex.map(fix_one, bad, chunksize=4):
                done += 1
                if err: errs += 1; print(f"  error {path}: {err}")
                if done % 200 == 0: print(f"  {done} of {len(bad)} made again ({time.time() - t0:.0f} s)")
        print(f"{len(bad)} chains made again" + (f", {errs} errors" if errs else ""))

if __name__ == "__main__":
    main()
