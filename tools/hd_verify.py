"""tools/hd-pack.py's verify and release commands.

    python3 tools/hd-pack.py verify PACK --against RELEASE.zip [--report FILE] [--jobs N]
    python3 tools/hd-pack.py release OUT_DIR --version V PACK... [--notes FILE] [--readme TEMPLATE]

verify compares a pack folder with a released archive of the same pack, texture by texture: "identical" (the same file),
"top identical" (the same top level, bytes for bytes; only the smaller levels differ: the release's chains were made again
later from the top levels), else the decoded top levels' PSNR over colour and the largest difference: "same" at 45 dB or
more, "look" from 35 to 45, "different" under 35, or "size" where the pictures' sizes differ; then what is "missing" (in
the release, not the pack) and "extra". It prints a summary by the recipe step that last wrote each texture (from the pack's
pack.json "recipe"), the 20 worst files and writes the whole list as a tab-separated report. Exit 0 always: it measures.

release zips each pack folder for sharing: OUT_DIR/<the recipe's zip name>-V.zip holding <pack>/README.txt (from the template,
tools/hd-recipes/README.template.txt, its {version}, {archives} and {notes} filled), ATTRIBUTION.txt, pack.json, the
textures, FILES.sha256 (made again with the README), and OUT_DIR/SHA256SUMS-V. Entries in a fixed order with the version's
date as their time, .dds deflated at level 1 and text at 9, so the same pack gives the same archive. Dot folders
(.text-base/, .recut-prev/) are left out.
"""
import argparse, hashlib, io, json, os, sys, time, zipfile
from multiprocessing import Pool

HERE = os.path.dirname(os.path.abspath(__file__))
RECIPES = os.path.join(HERE, "hd-recipes")
sys.path.insert(0, HERE)

def sha(b): return hashlib.sha256(b).hexdigest()

def top_level(data):
    """A .dds's (or a PNG's) top level as an RGBA array, and its top level's bytes (None for a PNG)."""
    import numpy as np, dds_mips
    from PIL import Image
    if data[:4] != b"DDS ":
        return np.array(Image.open(io.BytesIO(data)).convert("RGBA")), None
    hd = dds_mips.read_header(data); w, h = hd["w"], hd["h"]
    n0 = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * 16
    im = Image.open(io.BytesIO(dds_mips.dds_header(w, h, 1, n0) + data[hd["offset"]:hd["offset"] + n0])); im.load()
    return np.array(im.convert("RGBA"))[:h, :w], data[hd["offset"]:hd["offset"] + n0]

def compare(job):
    """(name, class, psnr, max difference, share of texels differing) of a pack file against the release's."""
    import numpy as np
    name, mine_path, rel = job
    with open(mine_path, "rb") as f: mine = f.read()
    if mine == rel: return name, "identical", None, 0, 0.0
    a, at = top_level(rel); b, bt = top_level(mine)
    if at is not None and at == bt: return name, "top identical", None, 0, 0.0
    if a.shape != b.shape: return name, "size", None, None, None
    d = np.abs(a.astype(np.int32) - b.astype(np.int32))
    mse = float((d[..., :3].astype(np.float64) ** 2).mean())
    p = 99.0 if mse == 0 else 10 * np.log10(255.0 ** 2 / mse)
    cls = "same" if p >= 45 else "look" if p >= 35 else "different"
    return name, cls, round(float(p), 2), int(d.max()), round(float((d.max(axis=2) > 0).mean()), 5)

def step_of(pack_json):
    """stem -> the id of the recipe step that last wrote it, from the recipe pack.json names (best effort: its "only" globs,
    and the lettering's and fixes' specs)."""
    import fnmatch
    rec = (pack_json.get("recipe") or {}).get("file", "")
    name = os.path.splitext(os.path.basename(rec))[0]
    if not name or not os.path.exists(os.path.join(RECIPES, name + ".json")): return lambda stem: "?"
    import importlib.util
    spec = importlib.util.spec_from_file_location("hdpack", os.path.join(HERE, "hd-pack.py")); hp = importlib.util.module_from_spec(spec); spec.loader.exec_module(hp)
    steps = [s for s in hp.load_recipe(name)["steps"] if s.get("install") or s.get("writes") == "pack" and s.get("tool") not in ("finish",)]
    sp = importlib.util.spec_from_file_location("tr", os.path.join(HERE, "text-redraws.py")); tr = importlib.util.module_from_spec(sp); sp.loader.exec_module(tr)
    lettered = {t for _, t, _ in tr.load_specs()}; fixed = {t for _, t, _ in tr.load_specs(os.path.join(tr.SPECS, "fixes"))}
    def who(stem):
        last = "?"
        for s in steps:
            if s["id"] == "lettering":
                if stem in lettered: last = s["id"]
                continue
            if s["id"] == "fixes" or s["id"] == "pump-counters":
                if stem in fixed: last = s["id"]
                continue
            g = hp.globs_list(s.get("only", "*")); k = hp.globs_list(s.get("skip", "")) if s.get("skip") else []
            if any(fnmatch.fnmatch(stem, x) for x in g) and not any(fnmatch.fnmatch(stem, x) for x in k): last = s["id"]
        return last
    return who

def main(argv):
    ap = argparse.ArgumentParser(prog="hd-pack.py verify", description="compare a pack folder with a released archive")
    ap.add_argument("pack"); ap.add_argument("--against", required=True, help="the release's .zip of the same pack")
    ap.add_argument("--report", help="the tab-separated list of every file (default PACK/../verify-<pack>.tsv)")
    ap.add_argument("--jobs", type=int, default=os.cpu_count())
    a = ap.parse_args(argv)
    pack = os.path.abspath(a.pack); z = zipfile.ZipFile(a.against)
    root = os.path.basename(pack.rstrip("/\\")) + "/"
    rel = {i.filename[len(root):]: i for i in z.infolist() if i.filename.startswith(root) and i.filename.lower().endswith((".dds", ".png", ".webp"))}
    mine = {}
    for r, dirs, files in os.walk(pack):
        dirs[:] = [d for d in dirs if not d.startswith(".")]
        for f in files:
            if f.lower().endswith((".dds", ".png", ".webp")): mine[os.path.relpath(os.path.join(r, f), pack).replace("\\", "/")] = os.path.join(r, f)
    missing = sorted(set(rel) - set(mine)); extra = sorted(set(mine) - set(rel))
    jobs = ((n, mine[n], z.read(rel[n])) for n in sorted(set(rel) & set(mine)))
    t0 = time.time(); rows = []
    with Pool(a.jobs) as pool:
        for i, row in enumerate(pool.imap_unordered(compare, jobs, chunksize=16), 1):
            rows.append(row)
            if i % 500 == 0: print(f"\r{i} compared", end="", flush=True)
    pj = json.load(open(os.path.join(pack, "pack.json"), encoding="utf-8")) if os.path.exists(os.path.join(pack, "pack.json")) else {}
    who = step_of(pj)
    classes = ("identical", "top identical", "same", "look", "different", "size")
    count = {c: 0 for c in classes}; by_step = {}
    for n, c, p, m, share in rows:
        count[c] += 1; st = who(os.path.splitext(os.path.basename(n))[0]); by_step.setdefault(st, {k: 0 for k in classes})[c] += 1
    print(f"\r{pack} against {os.path.basename(a.against)}: {len(rows)} compared in {time.time() - t0:.0f} s")
    print("  " + ", ".join(f"{c} {count[c]}" for c in classes) + f", missing {len(missing)}, extra {len(extra)}")
    print("  by the step that last wrote the texture:")
    for st in sorted(by_step, key=lambda s: -sum(by_step[s].values())):
        print(f"    {st:<16} " + ", ".join(f"{c} {v}" for c, v in by_step[st].items() if v))
    worst = sorted((r for r in rows if r[2] is not None), key=lambda r: r[2])[:20]
    if worst:
        print("  the 20 worst (PSNR dB, largest difference, share of texels differing):")
        for n, c, p, m, share in worst: print(f"    {n:<24} {p:6.2f} {m:4d} {share:.4f}  {who(os.path.splitext(os.path.basename(n))[0])}")
    if missing: print(f"  missing: {' '.join(missing[:40])}{' ...' if len(missing) > 40 else ''}")
    if extra: print(f"  extra: {' '.join(extra[:40])}{' ...' if len(extra) > 40 else ''}")
    report = a.report or os.path.join(os.path.dirname(pack), f"verify-{os.path.basename(pack)}.tsv")
    with open(report, "w", encoding="utf-8", newline="\n") as f:
        f.write("file\tclass\tpsnr\tmax\tshare\tstep\n")
        for n, c, p, m, share in sorted(rows):
            f.write(f"{n}\t{c}\t{'' if p is None else p}\t{'' if m is None else m}\t{'' if share is None else share}\t{who(os.path.splitext(os.path.basename(n))[0])}\n")
        for n in missing: f.write(f"{n}\tmissing\t\t\t\t\n")
        for n in extra: f.write(f"{n}\textra\t\t\t\t\n")
    print(f"  report: {report}")
    return 0

def release_main(argv):
    ap = argparse.ArgumentParser(prog="hd-pack.py release", description="zip pack folders for sharing")
    ap.add_argument("out"); ap.add_argument("packs", nargs="+", help="pack folders (tex-world-4x, tex-world-8x, ...)")
    ap.add_argument("--version", required=True, help="the release's version, YYYY.MM.DD")
    ap.add_argument("--notes", help="a text file of what changed, put into the README's {notes}")
    ap.add_argument("--readme", default=os.path.join(RECIPES, "README.template.txt"), help="the README template")
    a = ap.parse_args(argv)
    import importlib.util
    spec = importlib.util.spec_from_file_location("hdpack", os.path.join(HERE, "hd-pack.py")); hp = importlib.util.module_from_spec(spec); spec.loader.exec_module(hp)
    names = {}
    for f in os.listdir(RECIPES):
        if f.endswith(".json") and f != "models.json" and not f.startswith("masks"):
            r = json.load(open(os.path.join(RECIPES, f), encoding="utf-8"))
            if r.get("pack") and r.get("zip"): names[r["pack"]] = r["zip"]
    packs = [os.path.abspath(p) for p in a.packs]
    archives = [(p, f"{names.get(os.path.basename(p), os.path.basename(p))}-{a.version}.zip") for p in packs]
    table = "\n".join(f"  {zn:<52} {sum(1 for f in hp.pack_files(p) if f.lower().endswith(('.dds', '.png', '.webp'))):>6} files   {os.path.basename(p)}"
                      for p, zn in archives)
    notes = open(a.notes, encoding="utf-8").read().rstrip() if a.notes else "  (none given)"
    readme = open(a.readme, encoding="utf-8").read().replace("{version}", a.version).replace("{archives}", table).replace("{notes}", notes)
    readme = readme.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8")
    y, m, d = (int(x) for x in a.version.split(".")[:3])
    stamp = (y, m, d, 12, 0, 0)
    os.makedirs(a.out, exist_ok=True); sums = []
    for p, zn in archives:
        with open(os.path.join(p, "README.txt"), "wb") as f: f.write(readme)
        hp.write_sums(p)
        files = hp.pack_files(p); root = os.path.basename(p)
        first = [f for f in ("README.txt", "ATTRIBUTION.txt", "pack.json") if f in files]
        order = first + [f for f in files if f not in first and f != "FILES.sha256"] + ["FILES.sha256"]
        dst = os.path.join(a.out, zn)
        with zipfile.ZipFile(dst + ".part", "w") as z:
            for f in order:
                text = not f.lower().endswith((".dds", ".png", ".webp"))
                info = zipfile.ZipInfo(f"{root}/{f}", stamp); info.external_attr = 0o644 << 16
                if f.lower().endswith((".png", ".webp")): info.compress_type = zipfile.ZIP_STORED; level = None
                else: info.compress_type = zipfile.ZIP_DEFLATED; level = 9 if text else 1
                with open(os.path.join(p, f), "rb") as src: z.writestr(info, src.read(), compresslevel=level)
        os.replace(dst + ".part", dst)
        h = hashlib.sha256()
        with open(dst, "rb") as f:
            for b in iter(lambda: f.read(1 << 22), b""): h.update(b)
        sums.append(f"{h.hexdigest()}  {zn}"); print(f"{dst}: {os.path.getsize(dst):,} bytes, {len(order)} entries")
    with open(os.path.join(a.out, f"SHA256SUMS-{a.version}"), "w", encoding="utf-8", newline="\n") as f: f.write("\n".join(sums) + "\n")
    print(f"{os.path.join(a.out, f'SHA256SUMS-{a.version}')}")
    return 0
