#!/usr/bin/env python3
"""Build an HD texture pack from its recipe, the way the shared packs were made (docs/hd-textures.md, "The full recipe").

    python3 tools/hd-pack.py world-4x|world-8x|menu|all --install DIR --work DIR --out DIR [--game PATH | --export DIR]
                             [--models-dir DIR] [--device cuda|cpu] [--masters-from DIR] [--only GLOBS] [--version V]
                             [--list-steps] [--dry-run]
    python3 tools/hd-pack.py verify PACK --against RELEASE.zip [--report FILE]
    python3 tools/hd-pack.py release OUT_DIR --version V PACK... [--notes FILE]

A recipe (tools/hd-recipes/<pack>.json) is a list of steps; a step is one tool run: "tool" (a script in tools/, "i76" the
developer command line, or the runner's own "export", "encode" and "finish"), its "args" (placeholders below), "only" (the
textures it covers: globs or @lists/FILE, relative to tools/hd-recipes), "writes" (the work folder's stage it fills, or
"pack" for a step that changes the pack in place), "reads" (the steps whose output it uses), "device" (gpu: it gets
--device) and "models" (pinned in tools/hd-recipes/models.json; a missing one is downloaded, a wrong one refused). A step
{"id": ..., "from": "world-4x"} is that recipe's step, shared (the export, the masters, the masks). A recipe may instead
name its own "runner": [script, args...], run with --install, --work (WORK/<recipe>), --out, --device, --models-dir,
--only and --world-export (WORK/export, made first if missing, or --export): the menu pack, tools/menu-pack.py build.

A step marked "resume" (the masters) keeps its stage when its key changes and runs over it, the tool skipping what it
has: delete the stage to make it again. Placeholders: {export}, {masters} (WORK/masters, or --masters-from), {work}, {pack} (OUT/<the recipe's pack>), {models},
{device}, {tools}, {only} (the step's textures as one comma-separated argument: its own "only" narrowed by --only),
{names:only} (each of them an argument), {pgm:DIR} (the names of DIR's .pgm files, each an argument).

How a build goes: the pack folder is made again from empty each run. Steps that write a stage are skipped when their key
(the tool's and the step's text, the models', lists' and specs' hashes, the export's, and the keys of the steps they read)
is the one WORK/state.json holds for them; otherwise their stage is emptied and the step run again. Stages a step marks
"install" are then put into the pack in the recipe's order (hard links where the disk allows), and the steps that change
the pack run over it. --only STEMS rebuilds those textures in place: their files are taken out of each stage that holds
them and of the pack (with .text-base/ and .recut-prev/), and every step that names them runs again for them only.
finish writes pack.json (the recipe, its git commit, the tools', models' and libraries' versions, the GPU), ATTRIBUTION.txt
and FILES.sha256. Needs what the tools need (docs/hd-textures.md); the export needs --game (an I26 build with
--export-textures), --export (a folder one wrote), or the .NET SDK for `i76 export-textures`.
"""
import argparse, fnmatch, hashlib, json, os, platform, shutil, subprocess, sys, time, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
RECIPES = os.path.join(HERE, "hd-recipes")
REPO = os.path.dirname(HERE)
STATE = "state.json"

def sha256_file(path, h=None):
    h = h or hashlib.sha256()
    with open(path, "rb") as f:
        for b in iter(lambda: f.read(1 << 22), b""): h.update(b)
    return h

def sha256_tree(path):
    """A file's or a folder's hash (its files' names and contents, sorted)."""
    h = hashlib.sha256()
    if os.path.isfile(path): return sha256_file(path, h).hexdigest()
    for root, dirs, files in os.walk(path):
        dirs[:] = sorted(d for d in dirs if not d.startswith("."))
        for f in sorted(files):
            p = os.path.join(root, f); h.update(os.path.relpath(p, path).replace("\\", "/").encode()); sha256_file(p, h)
    return h.hexdigest()

def load_recipe(name):
    """The recipe's steps, those it takes "from" another recipe filled in from there."""
    with open(os.path.join(RECIPES, name + ".json"), encoding="utf-8") as f: r = json.load(f)
    steps = []
    for st in r.get("steps", []):
        if "from" in st:
            other = {s["id"]: s for s in load_recipe(st["from"])["steps"]}
            st = dict(other[st["id"]], shared_from=st["from"])
        steps.append(st)
    r["steps"] = steps
    return r

def globs_list(spec):
    """A step's "only": comma-separated globs, each may be @FILE (relative to tools/hd-recipes; globs one a line, # comments)."""
    out = []
    for g in (g.strip() for g in spec.split(",")):
        if g.startswith("@"):
            with open(os.path.join(RECIPES, g[1:]), encoding="utf-8") as f: out += [l.strip().lower() for l in f if l.strip() and not l.startswith("#")]
        elif g: out.append(g.lower())
    return out

def matches(stem, globs): return any(fnmatch.fnmatch(stem, g) for g in globs)

class Build:
    def __init__(self, a, recipe):
        self.a, self.r = a, recipe
        self.work = os.path.abspath(a.work)
        self.export = os.path.abspath(a.export) if a.export else os.path.join(self.work, "export")
        self.masters = os.path.abspath(a.masters_from) if a.masters_from else os.path.join(self.work, "masters")
        self.pack = os.path.join(os.path.abspath(a.out), recipe["pack"])
        self.models = os.path.abspath(a.models_dir)
        self.device = a.device
        self.state_path = os.path.join(self.work, STATE)
        self.state = json.load(open(self.state_path)) if os.path.exists(self.state_path) else {}
        self.keys, self.reran, self.log = {}, set(), []
        self.user_only = globs_list(a.only) if a.only else None
        self._stems = None

    # ---- names ----
    def stems(self):
        if self._stems is None:
            self._stems = sorted(n[:-4].lower() for n in os.listdir(self.export) if n.lower().endswith(".png")) if os.path.isdir(self.export) else []
        return self._stems

    def selection(self, st):
        """The step's textures (stems of the export) and its {only} argument: its own globs narrowed by --only."""
        own = globs_list(st.get("only", "*")); skip = globs_list(st.get("skip", "")) if st.get("skip") else []
        if self.user_only is None: return None, st.get("only", "*") if not st.get("only", "*").startswith("@") else ",".join(own)
        names = [s for s in self.stems() if matches(s, own) and matches(s, self.user_only) and not matches(s, skip)]
        return names, ",".join(names)

    # ---- arguments ----
    def subst(self, st, names, only):
        out = []
        for arg in st.get("args", []):
            if arg == "{names:only}":
                out += names if names is not None else [s for s in self.stems() if matches(s, globs_list(st.get("only", "*")))]
                continue
            if arg.startswith("{pgm:") and arg.endswith("}"):
                d = self.fill(arg[5:-1], only)
                out += sorted(f[:-4] for f in os.listdir(d) if f.endswith(".pgm")) if os.path.isdir(d) else []
                continue
            out.append(self.fill(arg, only))
        return out

    def fill(self, s, only):
        for k, v in (("{export}", self.export), ("{masters}", self.masters), ("{work}", self.work), ("{pack}", self.pack),
                     ("{models}", self.models), ("{device}", self.device), ("{tools}", HERE), ("{only}", only)):
            s = s.replace(k, v)
        return s

    # ---- keys ----
    def key(self, st):
        """A step's key: its text, its tool's hash, the models', the lists' and spec folders' it names, the export's, and
        the keys of the steps it reads."""
        h = hashlib.sha256(json.dumps({k: v for k, v in st.items() if k not in ("note", "shared_from")}, sort_keys=True).encode())
        tool = st.get("tool", "")
        if tool.endswith(".py"):
            h.update(sha256_file(os.path.join(HERE, tool)).digest())
            for helper in ("dds_mips.py",) + HELPERS.get(tool, ()): h.update(sha256_file(os.path.join(HERE, helper)).digest())
        pins = json.load(open(os.path.join(RECIPES, "models.json")))["models"]
        for m in st.get("models", []): h.update(pins[m]["sha256"].encode())
        for g in st.get("only", "").split(","):
            if g.strip().startswith("@"): h.update(sha256_file(os.path.join(RECIPES, g.strip()[1:])).digest())
        for arg in st.get("args", []):
            for word in ("{tools}/hd-recipes/", "{tools}/text-redraws"):
                if arg.startswith(word): h.update(sha256_tree(self.fill(arg, "")).encode())
        if st.get("tool") == "text-redraws.py" and "--specs" not in st.get("args", []): h.update(sha256_tree(os.path.join(HERE, "text-redraws")).encode())
        if st["id"] != "export": h.update(self.keys.get("export", "").encode())
        for r in st.get("reads", []): h.update(self.keys.get(r, "").encode())
        return h.hexdigest()

    # ---- running ----
    def run_cmd(self, argv, stdout=None, env=None):
        print("    " + subprocess.list2cmdline(argv), flush=True)
        if self.a.dry_run: return
        out = open(stdout, "wb") if stdout else None
        try: r = subprocess.run(argv, stdout=out, env=env)
        finally:
            if out: out.close()
        if r.returncode: sys.exit(f"step failed (exit {r.returncode}): {subprocess.list2cmdline(argv)}")

    def i76(self, args):
        return ["dotnet", "run", "--project", os.path.join(HERE, "I76.Cli"), "-c", "Release", "--", "--install", self.a.install] + args

    def stage_dir(self, st):
        return self.export if st["writes"] == "export" else self.masters if st["writes"] == "masters" else os.path.join(self.work, st["writes"])

    def models_ready(self, st):
        for m in st.get("models", []): ensure_model(m, self.models, self.a.dry_run)

    def step(self, st):
        sid, tool, writes = st["id"], st.get("tool"), st.get("writes")
        names, only = self.selection(st)
        if names is not None and not names and tool != "finish": return print(f"\n[{sid}] nothing of --only here")
        print(f"\n[{sid}] {st.get('note', '')}", flush=True)
        if writes == "export":
            have = os.path.exists(os.path.join(self.export, "bottom-up.txt"))
            if self.a.export or self.user_only is not None or (have and self.state.get(sid) == "done"):
                print(f"    the export in {self.export}")
            else:
                self.do_export(); self.state[sid] = "done"; self.save_state()
            self.keys[sid] = sha256_tree(self.export) if os.path.isdir(self.export) else ""
            return
        if writes == "masters" and self.a.masters_from:
            self.keys[sid] = "from " + self.masters; return print(f"    the masters from {self.masters}")
        k = self.key(st); self.keys[sid] = k
        in_place = writes == "pack"
        if not in_place:
            stage = self.stage_dir(st)
            earlier = [x["id"] for x in self.r["steps"][:self.r["steps"].index(st)] if x.get("writes") == writes]
            if self.user_only is None:
                stale = any(r in self.reran for r in st.get("reads", [])) or any(e in self.reran for e in earlier)
                if self.state.get(sid) == k and os.path.exists(stage) and not stale:
                    print("    up to date"); self.install(st, None); return
                if not earlier and os.path.exists(stage) and not st.get("resume") and not self.a.dry_run: shutil.rmtree(stage)
            elif not any(e in self.reran for e in earlier):
                # --only: the names out of the stage, unless a step before this one wrote them there in this run (the pumps'
                # counters work on the pumps step's pictures)
                for n in names: drop(stage, n, self.a.dry_run)
            if not self.a.dry_run: os.makedirs(stage, exist_ok=True)
            self.reran.add(sid)
        self.models_ready(st)
        t0 = time.time()
        if tool == "encode": self.do_encode(self.subst(st, names, only)[0], names)
        elif tool == "finish": self.do_finish()
        elif tool == "i76": self.run_cmd(self.i76(self.subst(st, names, only)), stdout=self.fill(st["stdout"], only) if st.get("stdout") else None)
        else:
            args = self.subst(st, names, only)
            if st.get("device") == "gpu" and "--device" not in args: args += ["--device", self.device]
            if self.a.jobs and tool == "upscale-textures.py": args += ["--jobs", str(self.a.jobs)]   # not in the key: the same output
            self.run_cmd([sys.executable, "-u", os.path.join(HERE, tool)] + args)
        self.log.append({"id": sid, "date": time.strftime("%Y-%m-%d"), "seconds": round(time.time() - t0, 1), "note": st.get("note", "")})
        if not in_place and not self.a.dry_run:
            if self.user_only is None: self.state[sid] = k; self.save_state()
            self.install(st, names)

    def install(self, st, names):
        """A stage marked "install" into the pack: its .dds (or .png) files, hard links where the disk allows."""
        if not st.get("install") or self.a.dry_run: return
        src = self.stage_dir(st); n = 0
        for f in sorted(os.listdir(src)):
            stem, ext = os.path.splitext(f)
            if ext not in (".dds", ".png") or (names is not None and stem not in names): continue
            dst = os.path.join(self.pack, f)
            for e in (".dds", ".png"):
                if os.path.exists(os.path.join(self.pack, stem + e)): os.remove(os.path.join(self.pack, stem + e))
            try: os.link(os.path.join(src, f), dst)
            except OSError: shutil.copy2(os.path.join(src, f), dst)
            n += 1
        print(f"    {n} files into {self.pack}")

    def do_export(self):
        os.makedirs(self.export, exist_ok=True)
        if self.a.game:
            env = dict(os.environ, I76_QUIET_WINDOW="1")
            self.run_cmd([self.a.game, "--windowed", "--resolution", "640x480", "--audio-driver", "Dummy", "--", "--install", self.a.install, "--export-textures", self.export], env=env)
        else:
            self.run_cmd(self.i76(["export-textures", self.export]))
        if not self.a.dry_run and not os.path.exists(os.path.join(self.export, "bottom-up.txt")): sys.exit(f"the export wrote nothing into {self.export}")
        self._stems = None

    def do_encode(self, src, names):
        """A stage's upright PNGs as BC7 .dds into the pack, in the stored row order (bottom-up.txt)."""
        print(f"    encode {src} -> {self.pack}")
        if self.a.dry_run: return
        import numpy as np, etcpak, dds_mips
        from PIL import Image
        bu = {l.strip().lower() for l in open(os.path.join(self.export, "bottom-up.txt")) if l.strip()}
        n = 0
        for f in sorted(os.listdir(src)):
            stem = f[:-4]
            if not f.endswith(".png") or (names is not None and stem not in names): continue
            px = np.array(Image.open(os.path.join(src, f)).convert("RGBA"))
            dst = os.path.join(self.pack, stem + ".dds")
            if os.path.exists(dst): os.remove(dst)
            dds_mips.write_dds(dst, px[::-1] if stem in bu else px, etcpak); n += 1
        print(f"    {n} encoded into {self.pack}")

    def save_state(self):
        with open(self.state_path + ".part", "w") as f: json.dump(self.state, f, indent=1)
        os.replace(self.state_path + ".part", self.state_path)

    def do_finish(self):
        if self.a.dry_run: return
        # the fixes a step made in a stage before it was encoded (the pumps' counters) are the pack's too
        extra = []
        for st in self.r["steps"]:
            p = os.path.join(self.stage_dir(st), "pack.json") if st.get("tool") == "text-redraws.py" and st.get("writes") not in ("pack", None) else None
            if p and os.path.exists(p): extra += json.load(open(p, encoding="utf-8")).get("fixes", [])
        finish(self.pack, self.r, self.a.version or time.strftime("%Y.%m.%d"), self.log, self.device, extra)

    def build(self):
        os.makedirs(self.work, exist_ok=True)
        if self.user_only is None and not self.a.dry_run:
            if os.path.exists(self.pack): shutil.rmtree(self.pack)
        os.makedirs(self.pack, exist_ok=True)
        if self.user_only is not None and not self.a.dry_run:
            for n in [s for s in self.stems() if matches(s, self.user_only)]: drop(self.pack, n, False)
        sys.path.insert(0, HERE)
        for st in self.r["steps"]: self.step(st)

# what a step's tool reads besides itself (a change to one runs the step again)
HELPERS = {"radar-redraw.py": ("panel_distress.py", "radar-redraw/radar.json"),
           "hud-redraw.py": ("radar-redraw.py", "panel_distress.py", "hud-redraw/hud.json")}

def drop(folder, stem, dry):
    """A texture's files out of a stage or pack (and its .text-base/, .recut-prev/, .radar-base/ and .hud-base/ copies)."""
    for d in (folder, os.path.join(folder, ".text-base"), os.path.join(folder, ".recut-prev"), os.path.join(folder, ".radar-base"),
              os.path.join(folder, ".hud-base")):
        if not os.path.isdir(d): continue
        for f in os.listdir(d):
            if f.split(".")[0] == stem and not dry: os.remove(os.path.join(d, f))

def ensure_model(name, models_dir, dry=False):
    """A pinned model in models_dir with its pinned hash: downloaded (out of its zip where it ships in one) when missing."""
    import urllib.request
    pins = json.load(open(os.path.join(RECIPES, "models.json")))
    m = pins["models"][name]; p = os.path.join(models_dir, name)
    if not os.path.exists(p):
        print(f"    model {name} missing: downloading {m['url']}")
        if dry: return
        os.makedirs(models_dir, exist_ok=True)
        if m.get("zip"):
            z = os.path.join(models_dir, m["zip"])
            if not os.path.exists(z):
                urllib.request.urlretrieve(m["url"], z + ".part"); os.replace(z + ".part", z)
            if sha256_file(z).hexdigest() != pins["archives"][m["zip"]]["sha256"]: sys.exit(f"{z}: not the pinned archive")
            with zipfile.ZipFile(z) as zf, open(p + ".part", "wb") as f: f.write(zf.read(m["member"]))
        else: urllib.request.urlretrieve(m["url"], p + ".part")
        os.replace(p + ".part", p)
    if not dry and sha256_file(p).hexdigest() != m["sha256"]: sys.exit(f"{p}: SHA-256 is not the one tools/hd-recipes/models.json pins: download it again")

def versions():
    """The libraries' versions, the GPU, Python and the system, for pack.json."""
    from importlib import metadata
    v = {}
    for lib in ("torch", "spandrel", "numpy", "pillow", "etcpak", "scipy", "opencv-python-headless", "opencv-python"):
        try: v[lib] = metadata.version(lib)
        except metadata.PackageNotFoundError: pass
    v["python"] = platform.python_version(); v["system"] = platform.platform()
    try:
        g = subprocess.run(["nvidia-smi", "--query-gpu=name,driver_version", "--format=csv,noheader"], capture_output=True, text=True)
        if g.returncode == 0: v["gpu"] = g.stdout.strip().splitlines()[0]
    except OSError: pass
    return v

def git_commit():
    try: return subprocess.run(["git", "-C", REPO, "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip() or None
    except OSError: return None

def finish(pack, r, version, log, device, extra_fixes=()):
    """pack.json from the recipe (keeping the tools' text_redraws, fixes, fonts, radar_redraw and hud_redraw), ATTRIBUTION.txt and FILES.sha256."""
    pins = json.load(open(os.path.join(RECIPES, "models.json")))["models"]
    pj = os.path.join(pack, "pack.json")
    tools_pj = json.load(open(pj, encoding="utf-8")) if os.path.exists(pj) else {}
    if extra_fixes:
        have = {f["texture"]: f for f in tools_pj.get("fixes", [])}; have.update({f["texture"]: f for f in extra_fixes})
        tools_pj["fixes"] = [have[k] for k in sorted(have)]
    used = [m for m in pins if m in r.get("used_for", {})]
    models = [{"file": m, "model": pins[m]["model"], "by": pins[m]["by"], "license": pins[m]["license"], "commercial": pins[m]["commercial"],
               "source": pins[m]["url"], "sha256": pins[m]["sha256"], "used_for": r["used_for"][m]} for m in used]
    nc = any(not m["commercial"] for m in models)
    import importlib.util
    spec = importlib.util.spec_from_file_location("ut", os.path.join(HERE, "upscale-textures.py")); ut = importlib.util.module_from_spec(spec); spec.loader.exec_module(ut)
    recipe_file = os.path.join(RECIPES, r["name"] + ".json")
    out = {"name": r["title"], "version": version, "date": time.strftime("%Y-%m-%d"), "scale": r["scale"], "format": "dds-bc7",
           **({"partial": True, "covers": r["covers"]} if r.get("covers") else {}),
           "full_scale": "z*", "pixel": "zdue*,zdse*,znbe*,zmir*", "colour_match": True,
           "model": "4xTextures_GTAV_rgt-s_dither",
           "model_license": "; ".join(f"{m['model']}: {m['license']}" for m in models),
           "attribution": " | ".join(f"{m['model']} by {m['by']}, {m['license']}, {m['source']} (used for: {m['used_for']})" for m in models),
           "models": models,
           "families": [{"family": f, "globs": g, "colour": c or "the masters (route 8b)", "alpha_step": bool(a)} for f, g, c, a in ut.COCKPIT],
           "recipe": {"file": "tools/hd-recipes/" + r["name"] + ".json", "git": git_commit(), "sha256": sha256_file(recipe_file).hexdigest()},
           "tools": {t: sha256_file(os.path.join(HERE, t)).hexdigest() for t in ("hd-pack.py", "upscale-textures.py", "text-redraws.py", "radar-redraw.py", "hud-redraw.py", "panel_distress.py", "dds_mips.py")},
           "built_with": dict(versions(), device=device),
           "steps": log,
           **{k: tools_pj[k] for k in ("text_redraws", "text_fonts", "fixes", "radar_redraw", "hud_redraw") if k in tools_pj},
           "licence": ("non-commercial; " if nc else "") + "derived from Interstate '76 art, requires an owned copy of the game",
           "links": {"upstream": "https://github.com/vahokif/I26", "recipe": "https://github.com/therealjkvalentine/I26/tree/main/tools/hd-recipes"}}
    with open(pj + ".part", "w", encoding="utf-8") as f: json.dump(out, f, indent=2, ensure_ascii=False); f.write("\n")
    os.replace(pj + ".part", pj)
    lines = [f"{r['title']} {version}: credits", "", "Upscaling models (their outputs are in this pack; the models are not):"]
    lines += [f"- {m['model']} by {m['by']}, {m['license']}, {m['source']} (used for: {m['used_for']})" for m in models]
    fonts = out.get("text_fonts", [])
    if fonts:
        lines += ["", "Fonts the redrawn lettering was set in (rendered into the pictures; no font file is included):"]
        lines += [f"- {f['font']}: {f['by']}, {f['license']}, {f['source']}" for f in fonts]
    if nc: lines += ["", "Because a model above is under a NonCommercial licence, this pack is offered NON-COMMERCIALLY only: free, never sold,",
                     "never behind a paywall, subscription or reward scheme."]
    lines += ["", "Source art: Interstate '76 (Activision, 1997). Its art and movies belong to their rights holders; this pack is derived",
              "from them and requires an owned copy of the game.", "",
              f"Made by tools/hd-pack.py {r['name']} from tools/hd-recipes/{r['name']}.json (see pack.json's \"recipe\")."]
    with open(os.path.join(pack, "ATTRIBUTION.txt"), "w", encoding="utf-8", newline="\n") as f: f.write("\n".join(lines) + "\n")
    write_sums(pack)
    print(f"    pack.json, ATTRIBUTION.txt, FILES.sha256 in {pack}")

def pack_files(pack):
    """The pack's files a release holds (no dot folders), relative paths with forward slashes, sorted."""
    out = []
    for root, dirs, files in os.walk(pack):
        dirs[:] = sorted(d for d in dirs if not d.startswith("."))
        out += [os.path.relpath(os.path.join(root, f), pack).replace("\\", "/") for f in files if not f.startswith(".") and not f.endswith(".part")]
    return sorted(out)

def write_sums(pack):
    """FILES.sha256: the hash of every file of the pack but itself (README.txt, ATTRIBUTION.txt and pack.json first, as the releases)."""
    files = [f for f in pack_files(pack) if f != "FILES.sha256"]
    first = [f for f in ("README.txt", "ATTRIBUTION.txt", "pack.json") if f in files]
    with open(os.path.join(pack, "FILES.sha256"), "w", encoding="utf-8", newline="\n") as f:
        for n in first + [x for x in files if x not in first]: f.write(f"{sha256_file(os.path.join(pack, n)).hexdigest()}  {n}\n")

def delegate(name, r, a):
    """A recipe with its own runner (the menu pack's tools/menu-pack.py build), run with the common options: the same
    --install, --models-dir, --device and --only, its work folder WORK/<name>, its pack OUT/tex-menu beside the others. The
    menu's mission maps are made from the world export, so that is made first in WORK/export as world-4x makes it (or
    --export names it). --list-steps prints the runner's steps."""
    run = r.get("runner") or [f"{name}-pack.py", "build"]
    script = os.path.join(HERE, run[0])
    if a.list_steps:
        subprocess.run([sys.executable, script, "list-steps"]); return
    world = Build(a, load_recipe("world-4x"))
    if not os.path.exists(os.path.join(world.export, "bottom-up.txt")):
        if a.export: sys.exit(f"--export {a.export}: no bottom-up.txt (not a texture export)")
        print(f"\n[export] the world export, the mission maps' input, in {world.export}"); world.do_export()
    argv = [sys.executable, script] + run[1:] + ["--install", a.install, "--work", os.path.join(os.path.abspath(a.work), name),
                                                 "--out", os.path.abspath(a.out), "--device", a.device, "--models-dir", a.models_dir,
                                                 "--world-export", world.export]
    if a.only: argv += ["--only", a.only]
    print("    " + subprocess.list2cmdline(argv))
    if not a.dry_run:
        rc = subprocess.run(argv).returncode
        if rc: sys.exit(rc)

def list_steps(r, b):
    for st in r["steps"]:
        names, only = b.selection(st)
        print(f"{st['id']:<16} {st.get('tool', ''):<20} -> {st.get('writes', ''):<18} {st.get('only', '*')[:40]:<40} {st.get('note', '')[:70]}")

def main(argv=None):
    if argv is None: argv = sys.argv[1:]
    if argv and argv[0] == "verify":
        import hd_verify; return hd_verify.main(argv[1:])
    if argv and argv[0] == "release":
        import hd_verify; return hd_verify.release_main(argv[1:])
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("pack", choices=("world-4x", "world-8x", "menu", "all"), help="the recipe (tools/hd-recipes/<pack>.json), or all three")
    ap.add_argument("--install", help="the Interstate '76 folder")
    ap.add_argument("--game", help="an I26 program with --export-textures, for the export (default: i76 export-textures)")
    ap.add_argument("--export", help="a folder --export-textures wrote, used as the export (read only)")
    ap.add_argument("--work", default="i26-hd-work", help="the work folder: the export, the masters and every step's stage (default ./i26-hd-work)")
    ap.add_argument("--out", default="i26-hd-packs", help="where the packs go, each in its own folder (default ./i26-hd-packs; not the game's folder)")
    ap.add_argument("--models-dir", default=os.path.join(os.path.expanduser("~/.cache"), "i26", "models"), help="the models (default ~/.cache/i26/models)")
    ap.add_argument("--device", default=None, help="cuda or cpu for the model steps (default cuda when torch has it)")
    ap.add_argument("--masters-from", help="a folder of 8x masters made earlier (the release's): the masters steps are skipped")
    ap.add_argument("--only", help="globs (comma-separated) of textures to rebuild in place through every step that names them")
    ap.add_argument("--jobs", type=int, default=0, help="processes for upscale-textures.py's steps without a model (default one a core)")
    ap.add_argument("--version", help="the packs' version (default today's date, YYYY.MM.DD)")
    ap.add_argument("--list-steps", action="store_true", help="print the recipe's steps and stop")
    ap.add_argument("--dry-run", action="store_true", help="print the commands and stop")
    a = ap.parse_args(argv)
    if a.device is None:
        try: import torch; a.device = "cuda" if torch.cuda.is_available() else "cpu"
        except ImportError: a.device = "cpu"
    if not a.list_steps and not a.install and not a.dry_run: ap.error("--install is needed")
    for name in (("world-4x", "world-8x", "menu") if a.pack == "all" else (a.pack,)):
        with open(os.path.join(RECIPES, name + ".json"), encoding="utf-8") as f: raw = json.load(f)
        if raw.get("runner") or name == "menu":
            print(f"== {name}: its own runner"); delegate(name, raw, a); continue
        r = load_recipe(name); b = Build(a, r)
        if a.list_steps: list_steps(r, b); continue
        print(f"== {name} -> {b.pack}")
        b.build()

if __name__ == "__main__":
    sys.exit(main())
