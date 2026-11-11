#!/usr/bin/env python3
"""Builds the changes page: every section of tools/play-branches.txt merged into the fork, with its before/after pictures.

The pictures are not in the repository (no game art or captures are committed). They live in an evidence folder outside
it, one folder per lane (r<N>-<name>/...), and commit messages name them in their Evidence lines. This script finds them,
shrinks them (WebP for pictures, H.264 at half size for clips) into the output folder next to index.html, and writes a
self-contained static site that works from any sub-path (relative links only), for example the gh-pages branch.

    python3 tools/changes-page/build.py --evidence DIR --out DIR [--ref main]

    --evidence  the evidence folder (or env I26_EVIDENCE_ROOT)
    --out       the site folder to write: index.html, media/, .nojekyll (or env I26_CHANGES_OUT)
    --cache     where the file index and shrunk media are cached (default: <evidence>/.changes-page-cache)
    --deny      an optional file of extra regular expressions (one a line) that must not appear in the built text
                (or env I26_CHANGES_DENY); kept outside the repository, for words that must not be committed either
    --rescan    rebuild the cached index of the evidence folder

Needs Pillow (with WebP) and ffmpeg on PATH. Reads git only; no network.
"""
import argparse, datetime, hashlib, html, json, os, re, shutil, subprocess, sys
from urllib.parse import quote

HERE = os.path.dirname(os.path.abspath(__file__))
IMG_EXT = (".png", ".jpg", ".jpeg")
VID_EXT = (".mp4", ".webm")
MAX_SIDE, MAX_PIXELS, WEBP_Q = 4096, 9_000_000, 80
VID_WIDTH, VID_CRF = 1720, 33
MAXPER, MAXVID, CL_MIN = 8, 3, 1.9

# Text that must never reach the public page: local paths, private addresses, mail addresses, internal folders.
PRIVATE = [r"[A-Za-z]:\\", r"/Users/", r"/home/", r"\\\\[A-Za-z]", r"_scratch", r"\b192\.168\.", r"\b10\.\d+\.\d+\.\d+\b",
           r"\b100\.\d+\.\d+\.\d+\b", r"\b172\.(1[6-9]|2\d|3[01])\.", r"[\w.+-]+@[\w-]+\.[\w.]+"]

A = None  # parsed arguments


def git(*a):
    r = subprocess.run(["git", "-C", A.repo] + list(a), capture_output=True, text=True, encoding="utf-8", errors="replace")
    return r.stdout


# ---------- manifest and git ----------
def parse_manifest():
    secs, cur = [], None
    for ln in git("show", A.ref + ":tools/play-branches.txt").splitlines():
        m = re.match(r"^# ([A-Z]{1,2}[0-9]*)\. (.*)$", ln)
        if m:
            cur = {"id": m.group(1), "header": m.group(2), "branches": []}
            secs.append(cur)
            continue
        if ln.startswith("#") or not ln.strip():
            continue
        b = ln.split("#")[0].strip()
        if b and cur:
            cur["branches"].append(b)
    return secs


def merge_map():
    d = {}
    for ln in git("log", A.ref, "--merges", "--format=%H%x09%ad%x09%s", "--date=short").splitlines():
        h, dt, s = ln.split("\t", 2)
        m = re.match(r"^Merge (?:branch )?'?([^' ]+)'?", s)
        if m and m.group(1) not in d:
            d[m.group(1)] = (h, dt)
    return d


def gather(secs):
    mg = merge_map()
    for s in secs:
        s["items"] = []
        for b in s["branches"]:
            it = {"branch": b}
            if b in mg:
                h, dt = mg[b]
                it["date"] = dt
                raw = git("log", h + "^1.." + h + "^2", "--format=%s%n%b%x00")
            else:
                it["date"] = git("log", "-1", "--format=%ad", "--date=short", b).strip()
                raw = git("log", "-n", "6", b, "--format=%s%n%b%x00")
            it["msgs"] = [m.strip() for m in raw.split("\x00") if m.strip()]
            s["items"].append(it)
        ds = sorted(i["date"] for i in s["items"] if i["date"])
        s["date"] = ds[len(ds) // 2] if ds else ""
        hd = re.search(r"\((\d{4}-\d{2}-\d{2})", s["header"])
        if hd:
            s["date"] = hd.group(1)


# ---------- changelog ----------
STOP = set("the and for with from that this are not its into when was has have than then they their them each every all any one two out off new now still only also more most over too but can may".split())


def toks(s):
    return {t for t in re.split(r"[^a-z0-9]+", s.lower()) if len(t) > 3 and t not in STOP}


def load_changelog():
    return [ln[2:].strip() for ln in git("show", A.ref + ":CHANGELOG.md").splitlines() if ln.startswith("- ")]


def best_changelog(cl, clt, texts):
    q = set()
    for t in texts:
        q |= toks(t)
    best, bs = None, 0
    for line, lt in zip(cl, clt):
        sc = len(q & lt) / (1 + 0.15 * len(lt)) if len(q & lt) >= 3 else 0
        if sc > bs:
            best, bs = line, sc
    return best, bs


def clip_sentences(t, n=2, cap=520):
    t = t.replace("`", "")
    parts = re.split(r"(?<=[.;])\s+(?=[A-Z\[])", t)
    out = ""
    for p in parts[:n]:
        if len(out) + len(p) > cap and out:
            break
        out += (" " if out else "") + p
    if len(out) > cap:
        out = out[:cap].rsplit(" ", 1)[0] + "..."
    return out


def scrub(t):
    """Drops what reads as working notes rather than a description: evidence paths, model and lane remarks."""
    t = re.sub(r"\s*\((?:[^()]*\b(?:lane|integrator) on [^()]*)\)", "", t)
    t = re.sub(r"\s*\(?\b(?:Evidence|sheet|clip):?\s*\S*_scratch\S*\)?", "", t)
    t = re.sub(r"\S*_scratch\S*", "", t)
    return re.sub(r"\s{2,}", " ", t).strip()


# ---------- evidence files ----------
def load_index():
    """Picture and clip files per lane folder (r<N>-<name>), cached; --rescan rebuilds it."""
    p = os.path.join(A.cache, "fileindex.json")
    if os.path.exists(p) and not A.rescan:
        return json.load(open(p, encoding="utf-8"))
    raw = {}
    for lane in sorted(os.listdir(A.evidence)):
        lp = os.path.join(A.evidence, lane)
        if not os.path.isdir(lp) or not re.match(r"r\d+-", lane):
            continue
        L = []
        for r, ds, fs in os.walk(lp):
            ds[:] = [d for d in ds if d not in ("appdata", "bench", "tools", "user", "ref")]
            for f in fs:
                if f.lower().endswith(IMG_EXT + VID_EXT):
                    L.append(os.path.relpath(os.path.join(r, f), A.evidence).replace("\\", "/"))
        raw[lane] = L
    json.dump(raw, open(p, "w", encoding="utf-8"))
    return raw


REF_RX = re.compile(r"[A-Za-z0-9_:\\/.$~-]+\.(?:png|jpe?g|mp4|webm)\b", re.I)


def resolve(tok, lanes, idx):
    tok = tok.replace("\\", "/")
    m = re.search(r"_scratch/(.+)$", tok)
    if m:
        if os.path.exists(os.path.join(A.evidence, m.group(1))):
            return m.group(1)
        tok = m.group(1)
    if tok.startswith("/") or ":" in tok:
        return None
    if re.match(r"r\d+-", tok) and os.path.exists(os.path.join(A.evidence, tok)):
        return tok
    cands = [f for lane in lanes for f in idx.get(lane, []) if f == lane + "/" + tok or f.endswith("/" + tok)]
    return cands[0] if len(set(cands)) == 1 else None


def evidence_files(sec, ov, idx):
    lanes = list(ov.get("lane_hint", {}).get(sec["id"], []))
    msgs = [m for it in sec["items"] for m in it["msgs"]]
    for m in msgs:
        lanes += re.findall(r"_scratch[\\/](r\d+-[\w-]+)", m)
    lanes = list(dict.fromkeys(lanes))
    found = []
    for m in msgs:
        for ln in m.splitlines():
            if not re.search(r"Evidence|_scratch|sheet|\.png|\.jpg|\.mp4", ln):
                continue
            for tok in REF_RX.findall(ln):
                r = resolve(tok, lanes, idx)
                if r and r not in found:
                    found.append(r)
    ex = set(ov.get("exclude", {}).get(sec["id"], []))
    return [f for f in found if os.path.basename(f) not in ex]


# ---------- media ----------
def media_name(rel, ext):
    return hashlib.sha1(rel.encode()).hexdigest()[:12] + ext


def fresh(dst, src):
    return os.path.exists(dst) and os.path.getmtime(dst) >= os.path.getmtime(src)


def make_image(rel):
    from PIL import Image
    src = os.path.join(A.evidence, rel)
    name = media_name(rel, ".webp")
    cached = os.path.join(A.cache, "media", name)
    if not fresh(cached, src):
        im = Image.open(src)
        if im.mode in ("RGBA", "LA", "P"):
            im = im.convert("RGBA")
            bg = Image.new("RGB", im.size, (0, 0, 0))
            bg.paste(im, mask=im.split()[3])
            im = bg
        else:
            im = im.convert("RGB")
        f = min(1.0, MAX_SIDE / max(im.size), (MAX_PIXELS / (im.width * im.height)) ** 0.5)
        if f < 1.0:
            im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.LANCZOS)
        im.save(cached, "WEBP", quality=WEBP_Q, method=6)
    shutil.copy2(cached, os.path.join(A.out, "media", name))
    return name


def make_video(rel):
    src = os.path.join(A.evidence, rel)
    name = media_name(rel, ".mp4")
    cached = os.path.join(A.cache, "media", name)
    if not fresh(cached, src):
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", src, "-an", "-c:v", "libx264", "-preset", "slow",
                        "-crf", str(VID_CRF), "-vf", "scale='min(%d,iw)':-2" % VID_WIDTH, "-pix_fmt", "yuv420p",
                        "-movflags", "+faststart", cached], check=True)
    shutil.copy2(cached, os.path.join(A.out, "media", name))
    return name


# ---------- html ----------
E = html.escape
CSS = """
:root{--bg:#f6f5f1;--fg:#1d1d1b;--mut:#6b6a63;--card:#fff;--line:#d9d6cc;--acc:#8a2b2b;--chip:#ece9df}
@media (prefers-color-scheme:dark){:root{--bg:#16171a;--fg:#e8e6df;--mut:#9a988f;--card:#202226;--line:#34363b;--acc:#e08a7a;--chip:#2a2c31}}
*{box-sizing:border-box}html{-webkit-text-size-adjust:100%}body{margin:0;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,Segoe UI,Arial,sans-serif}
main{max-width:1500px;margin:0 auto;padding:16px}a{color:var(--acc)}
h1{font-size:1.6rem;margin:.2em 0}h2{font-size:1.15rem;margin:1.6em 0 .4em;padding-bottom:.2em;border-bottom:1px solid var(--line);color:var(--acc)}
.bar{position:sticky;top:0;background:var(--bg);padding:10px 0;z-index:5;border-bottom:1px solid var(--line);display:flex;flex-wrap:wrap;gap:8px;align-items:center}
.bar input{flex:1 1 220px;min-width:0;padding:8px 10px;font:inherit;background:var(--card);color:var(--fg);border:1px solid var(--line);border-radius:6px}
button{font:inherit;padding:7px 12px;background:var(--chip);color:var(--fg);border:1px solid var(--line);border-radius:6px;cursor:pointer}
.meta{color:var(--mut);font-size:.9rem}.intro{max-width:110ch}
details{background:var(--card);border:1px solid var(--line);border-radius:8px;margin:6px 0}
summary{cursor:pointer;padding:9px 12px;display:flex;flex-wrap:wrap;gap:4px 10px;align-items:baseline}
summary .id{font-weight:700;color:var(--acc);min-width:2.4em}summary .t{flex:1 1 260px;min-width:0}summary .d{color:var(--mut);font-size:.85rem}
details.none{background:transparent;opacity:.72}details.none summary .t{color:var(--mut)}
.tag{font-size:.72rem;padding:1px 7px;border-radius:9px;background:var(--chip);color:var(--mut)}
.body{padding:2px 14px 14px}.body p{margin:.4em 0 .8em;max-width:110ch;overflow-wrap:anywhere}
.set{font-size:.88rem;color:var(--mut)}code{background:var(--chip);padding:0 4px;border-radius:4px;font-size:.88em;overflow-wrap:anywhere}
.imgs{display:grid;grid-template-columns:repeat(auto-fill,minmax(min(100%,520px),1fr));gap:12px}
figure{margin:0;min-width:0}figure img{width:100%;height:auto;display:block;border:1px solid var(--line);border-radius:4px;background:#000}
figcaption{font-size:.8rem;color:var(--mut);overflow-wrap:anywhere}video{width:100%;max-height:70vh;background:#000;border-radius:4px}
ul.br{margin:.2em 0 .8em;padding-left:1.2em;font-size:.9rem}ul.br li{margin:2px 0;overflow-wrap:anywhere}
.hidden{display:none}footer{margin:2em 0 1em;color:var(--mut);font-size:.85rem;max-width:110ch}
@media (max-width:600px){main{padding:10px}.body{padding:2px 8px 10px}h1{font-size:1.3rem}}
@media (min-width:2400px){main{max-width:3000px}body{font-size:18px}.imgs{grid-template-columns:repeat(auto-fill,minmax(620px,1fr))}}
"""
JS = """
(function(){var q=document.getElementById('q'),ds=[].slice.call(document.querySelectorAll('details.chg')),
hs=[].slice.call(document.querySelectorAll('h2.grp')),cnt=document.getElementById('cnt');
function f(){var t=q.value.toLowerCase().trim(),n=0;ds.forEach(function(d){var m=!t||d.getAttribute('data-t').indexOf(t)>=0;d.classList.toggle('hidden',!m);if(m)n++});
hs.forEach(function(h){var e=h.nextElementSibling,any=false;while(e&&e.tagName!=='H2'){if(e.classList.contains('chg')&&!e.classList.contains('hidden'))any=true;e=e.nextElementSibling}h.classList.toggle('hidden',!any)});
cnt.textContent=n+' of '+ds.length+' changes shown'}
q.addEventListener('input',f);
document.getElementById('ex').onclick=function(){ds.forEach(function(d){if(!d.classList.contains('hidden'))d.open=true})};
document.getElementById('co').onclick=function(){ds.forEach(function(d){d.open=false})};
if(location.hash){var d=document.getElementById(location.hash.slice(1));if(d&&d.tagName==='DETAILS')d.open=true}f()})();
"""


def caption(rel, given, ov):
    cap = given or re.sub(r"[-_]+", " ", os.path.splitext(os.path.basename(rel))[0]).strip()
    if rel in ov.get("original_game", {}):
        cap += " (includes the original 1997 game for comparison)"
    return cap


def fig_img(rel, cap):
    n = make_image(rel)
    return ('<figure><a href="media/%s" title="open at full size"><img src="media/%s" loading="lazy" alt="%s"></a>'
            "<figcaption>%s</figcaption></figure>" % (n, n, E(cap, quote=True), E(cap)))


def fig_vid(rel, cap):
    n = make_video(rel)
    return '<figure><video controls preload="none" playsinline src="media/%s"></video><figcaption>%s (clip)</figcaption></figure>' % (n, E(cap))


def clean_title(h):
    t = re.sub(r"\s*\((?:\d{4}-\d\d[^()]*|lane on[^()]*|integrator[^()]*)\)", "", h)
    t = re.sub(r"\s*;.*$", "", t)
    t = re.sub(r"\s+:", ":", t)
    t = re.sub(r"^(?:pass \d+ |round \d+ )", "", t.strip()).strip(" .:")
    return t[:1].upper() + t[1:]


def settings_of(text):
    s = []
    for m in re.findall(r"\[port\]\s*([a-z0-9_]+)", text):
        if m not in s:
            s.append(m)
    return s


def build():
    ov = json.load(open(os.path.join(HERE, "overrides.json"), encoding="utf-8"))
    os.makedirs(os.path.join(A.cache, "media"), exist_ok=True)
    if os.path.isdir(os.path.join(A.out, "media")):
        shutil.rmtree(os.path.join(A.out, "media"))
    os.makedirs(os.path.join(A.out, "media"))
    secs = parse_manifest()
    gather(secs)
    idx = load_index()
    cl = load_changelog()
    clt = [toks(x) for x in cl]
    sha = git("rev-parse", "--short=8", A.ref).strip()
    built = datetime.date.today().isoformat()
    excluded = ov.get("public_exclude", {})
    stats = {"img": 0, "vid": 0, "excluded": [], "used": []}
    blocks = []
    for s in secs:
        sid = s["id"]
        subs = [scrub(it["msgs"][0].split("\n")[0]) for it in s["items"] if it["msgs"]]
        title = ov["titles"].get(sid) or clean_title(s["header"])
        multi = len(s["items"]) > 3
        desc = ov.get("desc", {}).get(sid, "")
        best = None
        if not multi and not desc:
            best, sc = best_changelog(cl, clt, subs[:3] + [s["header"]])
            if best and sc >= CL_MIN:
                desc = clip_sentences(best)
            else:
                best = None
            if not desc:
                good = [x for x in subs if len(x) >= 40 and not re.match(r"(WIP|Merge|Docs?:|docs:)", x)] or subs
                if good:
                    desc = clip_sentences(good[0].rstrip(".:") + ".")
        elif not desc:
            desc = "%d changes merged together; each is listed below." % len(s["items"])
        desc = scrub(desc)
        setting = settings_of(" ".join([best or ""] + [m for it in s["items"] for m in it["msgs"]][:3])) if not multi else []
        files = [(f, None) for f in evidence_files(s, ov, idx)]
        for f, c in ov.get("files", {}).get(sid, []):
            if f not in [x[0] for x in files]:
                files.append((f, c))
        files = [x for x in files if os.path.exists(os.path.join(A.evidence, x[0]))]
        for x in [x for x in files if x[0] in excluded]:
            stats["excluded"].append((sid, x[0]))
        files = [x for x in files if x[0] not in excluded]
        ims = sorted([x for x in files if x[0].lower().endswith(IMG_EXT)],
                     key=lambda x: 0 if re.search(r"sheet|review|before.?after|compar", os.path.basename(x[0]), re.I) else 1)[:MAXPER]
        vds = [x for x in files if x[0].lower().endswith(VID_EXT)][:MAXVID]
        parts = ['<div class="body">']
        if desc:
            parts.append("<p>%s</p>" % E(desc))
        if setting:
            parts.append('<p class="set">Setting: %s</p>' % ", ".join("<code>[port] %s</code>" % E(x) for x in setting[:4]))
        if len(s["items"]) > 1:
            parts.append('<ul class="br">' + "".join(
                "<li><code>%s</code> %s</li>" % (E(it["branch"]), E(scrub(it["msgs"][0].split("\n")[0] if it["msgs"] else "")[:170]))
                for it in s["items"]) + "</ul>")
        elif s["items"]:
            parts.append('<p class="set">Branch: <code>%s</code></p>' % E(s["items"][0]["branch"]))
        figs = [fig_img(f, caption(f, c, ov)) for f, c in ims] + [fig_vid(f, caption(f, c, ov)) for f, c in vds]
        if figs:
            parts.append('<div class="imgs">' + "".join(figs) + "</div>")
        if not ims:
            parts.append('<p class="set">No screenshot for this change (code, tests or documents).</p>')
        stats["used"] += [[sid, f] for f, _ in ims + vds]
        stats["img"] += len(ims)
        stats["vid"] += len(vds)
        parts.append("</div>")
        has = bool(ims)
        dt = "%s %s %s %s" % (sid, title, desc, " ".join(s["branches"]))
        h = ('<details class="chg%s" id="%s" data-t="%s"><summary><span class="id">%s</span><span class="t">%s%s</span><span class="d">%s</span></summary>%s</details>'
             % ("" if has else " none", E(sid), E(dt.lower(), quote=True), E(sid), E(title),
                "" if has else ' <span class="tag">no screenshot</span>', E(s["date"]), "".join(parts)))
        blocks.append((s["date"], len(blocks), h, has, sid))
    body, last = [], None
    for b in sorted(blocks, key=lambda b: (b[0], b[1]), reverse=True):
        if b[0] != last:
            body.append('<h2 class="grp">%s</h2>' % E(b[0] or "undated"))
            last = b[0]
        body.append(b[2])
    nimg = sum(1 for b in blocks if b[3])
    page = ('<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
            '<title>I26 fork changes</title><meta name="description" content="Every change merged into the I26 play fork, with before and after pictures.">'
            '<style>' + CSS + '</style></head><body><main>'
            '<h1>What the I26 play fork changes</h1>'
            '<p class="intro">I26 is a reimplementation of Interstate \'76 (1997) in Godot. This fork carries a stack of fixes and '
            'options on top of it; each section below is one part of that stack as merged into the fork\'s <code>main</code>, '
            'newest first, with the before and after pictures that were taken when it was made. Open a section to see it; '
            'click a picture to open it at full size. Some sheets set the original 1997 game beside I26 for comparison.</p>'
            '<p class="meta">Built %s from main %s. %d sections (A to %s): %d with pictures, %d without.</p>'
            % (built, sha, len(blocks), blocks[-1][4], nimg, len(blocks) - nimg)
            + '<div class="bar"><input id="q" type="search" placeholder="Filter (title, section letter, branch)" aria-label="filter">'
              '<button id="ex">Expand all</button><button id="co">Collapse all</button><span class="meta" id="cnt"></span></div>'
            + "".join(body)
            + '<footer>Interstate \'76 and its art belong to their owners; the game\'s pictures here are captures and crops '
              'shown to compare the reimplementation with the original. I26 needs a legally owned copy of the game to run.</footer>'
            + "<script>" + JS + "</script></main></body></html>")
    open(os.path.join(A.out, "index.html"), "w", encoding="utf-8", newline="\n").write(page)
    open(os.path.join(A.out, ".nojekyll"), "w").close()
    # which evidence file each published media file came from, for auditing; kept in the cache, never published
    json.dump(stats["used"], open(os.path.join(A.cache, "media-map.json"), "w", encoding="utf-8"), indent=1)
    deny = list(PRIVATE)
    if A.deny and os.path.exists(A.deny):
        deny += [ln.strip() for ln in open(A.deny, encoding="utf-8") if ln.strip() and not ln.startswith("#")]
    text = re.sub(r'(src|href)="media/[0-9a-f]{12}\.(webp|mp4)"', "", page)
    hits = [(p, m.group(0)) for p in deny for m in re.finditer(p, text, re.I if p not in PRIVATE else 0)]
    size = sum(os.path.getsize(os.path.join(A.out, "media", f)) for f in os.listdir(os.path.join(A.out, "media")))
    print("sections %d, with pictures %d, images %d, clips %d, excluded %d, media %.1f MB"
          % (len(blocks), nimg, stats["img"], stats["vid"], len(stats["excluded"]), size / 1e6))
    if hits:
        print("PRIVATE TEXT FOUND, do not publish:", file=sys.stderr)
        for p, m in hits[:40]:
            print("  %s -> %r" % (p, m), file=sys.stderr)
        sys.exit(2)


def main():
    global A
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--evidence", default=os.environ.get("I26_EVIDENCE_ROOT"))
    ap.add_argument("--out", default=os.environ.get("I26_CHANGES_OUT"))
    ap.add_argument("--cache")
    ap.add_argument("--deny", default=os.environ.get("I26_CHANGES_DENY"))
    ap.add_argument("--repo", default=os.path.abspath(os.path.join(HERE, "..", "..")))
    ap.add_argument("--ref", default="main")
    ap.add_argument("--rescan", action="store_true")
    A = ap.parse_args()
    if not A.evidence or not A.out:
        ap.error("--evidence and --out (or I26_EVIDENCE_ROOT and I26_CHANGES_OUT) are required")
    A.evidence, A.out = os.path.abspath(A.evidence), os.path.abspath(A.out)
    A.cache = os.path.abspath(A.cache or os.path.join(A.evidence, ".changes-page-cache"))
    os.makedirs(A.out, exist_ok=True)
    os.makedirs(A.cache, exist_ok=True)
    build()


if __name__ == "__main__":
    main()
