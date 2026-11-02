#!/usr/bin/env python3
"""Add I26 (the Linux build) to Steam as a non-Steam game on a Steam Deck or any Linux Steam.

    python3 add-to-steam.py /path/to/i26-0.11.0/i26.x86_64 [--install /path/to/Interstate76] [--dry-run]

What it does, all under the Steam user's folder:
  - adds (or, when one by this name is there, moves it to this build) a shortcut in config/shortcuts.vdf;
  - writes a plain library tile (a flat colour with the name, generated here; no game art) to config/grid;
  - copies controller_neptune_i26.vdf (next to this script) into Steam's controller templates and maps the
    shortcut's name to it, so Game Mode offers it as the layout (to be confirmed on a Deck).
Run it with Steam shut down (Steam rewrites shortcuts.vdf when it exits). Running it again changes nothing
but the same entry. The old shortcuts.vdf is kept as shortcuts.vdf.i26bak. Python 3 standard library only.
"""
import argparse
import os
import shutil
import struct
import subprocess
import sys
import zlib

NAME = "I26"
COLOUR = (0x1f, 0x2a, 0x44)      # tile background
INK = (0xf0, 0xe6, 0xc8)         # tile text
HERE = os.path.dirname(os.path.abspath(__file__))
LAYOUT = "controller_neptune_i26.vdf"

# ---- binary KeyValues (shortcuts.vdf): 0 = map, 1 = string, 2 = int32, 8 = end of map ----


def parse(b, i=0):
    items = []
    while True:
        t = b[i]
        i += 1
        if t == 8:
            return items, i
        j = b.index(0, i)
        key = b[i:j].decode("utf-8", "replace")
        i = j + 1
        if t == 0:
            v, i = parse(b, i)
        elif t == 1:
            j = b.index(0, i)
            v = b[i:j].decode("utf-8", "replace")
            i = j + 1
        elif t == 2:
            v = struct.unpack_from("<i", b, i)[0]
            i += 4
        else:
            raise ValueError("unknown value type %d at %d" % (t, i))
        items.append((t, key, v))


def dump(items):
    out = bytearray()
    for t, key, v in items:
        out.append(t)
        out += key.encode() + b"\0"
        if t == 0:
            out += dump(v)
        elif t == 1:
            out += v.encode() + b"\0"
        else:
            out += struct.pack("<i", v)
    out.append(8)
    return bytes(out)


def get(items, key):
    for _, k, v in items:
        if k.lower() == key.lower():
            return v
    return None


def app_id(exe, name):
    """The id Steam derives for a shortcut: CRC-32 of the quoted exe and the name, top bit set.
    Returns (unsigned, as the signed int32 the file stores)."""
    top = zlib.crc32(('"%s"%s' % (exe, name)).encode()) | 0x80000000
    return top, top - (1 << 32)


# ---- the tile: a flat colour and the name in a 5x7 bitmap font ----

FONT = {   # 5 columns, 7 rows, one string of 5 bits per row
    "I": "01110 00100 00100 00100 00100 00100 01110", "2": "01110 10001 00001 00110 01000 10000 11111",
    "6": "00110 01000 10000 11110 10001 10001 01110", "A": "01110 10001 10001 11111 10001 10001 10001",
    "B": "11110 10001 10001 11110 10001 10001 11110", "C": "01110 10001 10000 10000 10000 10001 01110",
    "D": "11110 10001 10001 10001 10001 10001 11110", "E": "11111 10000 10000 11110 10000 10000 11111",
    "F": "11111 10000 10000 11110 10000 10000 10000", "G": "01110 10001 10000 10111 10001 10001 01111",
    "H": "10001 10001 10001 11111 10001 10001 10001", "J": "00111 00010 00010 00010 00010 10010 01100",
    "K": "10001 10010 10100 11000 10100 10010 10001", "L": "10000 10000 10000 10000 10000 10000 11111",
    "M": "10001 11011 10101 10101 10001 10001 10001", "N": "10001 11001 10101 10011 10001 10001 10001",
    "O": "01110 10001 10001 10001 10001 10001 01110", "P": "11110 10001 10001 11110 10000 10000 10000",
    "Q": "01110 10001 10001 10101 10010 01101 00000", "R": "11110 10001 10001 11110 10100 10010 10001",
    "S": "01111 10000 10000 01110 00001 00001 11110", "T": "11111 00100 00100 00100 00100 00100 00100",
    "U": "10001 10001 10001 10001 10001 10001 01110", "V": "10001 10001 10001 10001 10001 01010 00100",
    "W": "10001 10001 10001 10101 10101 11011 10001", "X": "10001 10001 01010 00100 01010 10001 10001",
    "Y": "10001 10001 01010 00100 00100 00100 00100", "Z": "11111 00001 00010 00100 01000 10000 11111",
    "0": "01110 10001 10011 10101 11001 10001 01110", "1": "00100 01100 00100 00100 00100 00100 01110",
    "3": "11110 00001 00001 01110 00001 00001 11110", "4": "10010 10010 10010 11111 00010 00010 00010",
    "5": "11111 10000 11110 00001 00001 10001 01110", "7": "11111 00001 00010 00100 01000 01000 01000",
    "8": "01110 10001 10001 01110 10001 10001 01110", "9": "01110 10001 10001 01111 00001 00010 01100",
    " ": "00000 00000 00000 00000 00000 00000 00000", "-": "00000 00000 00000 11111 00000 00000 00000",
}


def png(w, h, rows):
    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))
    raw = b"".join(b"\0" + r for r in rows)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def tile(w, h, text):
    """The name centred, as large as fits in about 70% of the width and 40% of the height."""
    text = "".join(c for c in text.upper() if c in FONT) or "I26"
    scale = max(1, min(int(w * 0.7) // (6 * len(text) - 1), int(h * 0.4) // 7))
    tw, th = (6 * len(text) - 1) * scale, 7 * scale
    x0, y0 = (w - tw) // 2, (h - th) // 2
    bg, fg = bytes(COLOUR), bytes(INK)
    glyphs = [FONT[c].split() for c in text]
    rows = []
    for y in range(h):
        line = bytearray(bg * w)
        if y0 <= y < y0 + th:
            ry = (y - y0) // scale
            for n, g in enumerate(glyphs):
                for rx, bit in enumerate(g[ry]):
                    if bit == "1":
                        for sx in range(scale):
                            x = x0 + (6 * n + rx) * scale + sx
                            line[3 * x:3 * x + 3] = fg
        rows.append(bytes(line))
    return png(w, h, rows)


# ---- Steam folders ----


def steam_root(arg):
    for p in ([arg] if arg else []) + ["~/.steam/steam", "~/.local/share/Steam"]:
        p = os.path.realpath(os.path.expanduser(p))
        if os.path.isdir(os.path.join(p, "userdata")):
            return p
    sys.exit("No Steam folder with userdata/ found; pass --steam-dir.")


def steam_user(root, arg):
    if arg:
        return arg
    base = os.path.join(root, "userdata")
    users = [d for d in os.listdir(base) if d.isdigit() and d != "0"]
    if not users:
        sys.exit("No Steam user under %s; sign in to Steam once, or pass --userid." % base)

    def last(d):
        p = os.path.join(base, d, "config", "localconfig.vdf")
        return os.path.getmtime(p) if os.path.exists(p) else 0
    return max(users, key=last)


def steam_running():
    try:
        return subprocess.run(["pgrep", "-x", "steam"], capture_output=True).returncode == 0
    except OSError:
        return False


def install_layout(root, user, name):
    layout = os.path.join(HERE, LAYOUT)
    tdir = os.path.join(root, "controller_base", "templates")
    if not os.path.exists(layout) or not os.path.isdir(tdir):
        print("Could not install the controller layout; copy %s into Steam's controller_base/templates by hand." % LAYOUT)
        return
    shutil.copy2(layout, os.path.join(tdir, LAYOUT))
    cdir = os.path.join(root, "steamapps", "common", "Steam Controller Configs", user, "config")
    os.makedirs(cdir, exist_ok=True)
    cs = os.path.join(cdir, "configset_controller_neptune.vdf")
    key = name.lower()
    body = open(cs, encoding="utf-8").read().rstrip() if os.path.exists(cs) else '"controller_config"\n{'
    if '"%s"' % key not in body.lower():
        if os.path.exists(cs):
            shutil.copy2(cs, cs + ".i26bak")
        block = '\t"%s"\n\t{\n\t\t"template"\t\t"%s"\n\t}\n' % (key, LAYOUT)
        with open(cs, "w", encoding="utf-8") as f:
            f.write(body.rstrip("}").rstrip() + "\n" + block + "}\n")
    print("Controller layout installed; if Game Mode does not offer it, choose 'I26 - Steam Deck' under Templates.")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("exe", help="the Linux build's i26.x86_64")
    ap.add_argument("--install", help="the game folder (the one holding I76.ZFS); leave out if I26 finds it by itself")
    ap.add_argument("--name", default=NAME, help="library name (default %(default)s)")
    ap.add_argument("--steam-dir")
    ap.add_argument("--userid")
    ap.add_argument("--no-layout", action="store_true", help="do not install the controller layout")
    ap.add_argument("--dry-run", action="store_true", help="say what would change, write nothing")
    ap.add_argument("--force", action="store_true", help="go on although Steam is running")
    a = ap.parse_args()

    exe = os.path.abspath(a.exe)
    if not os.path.isfile(exe):
        sys.exit("Not a file: %s" % exe)
    root = steam_root(a.steam_dir)
    user = steam_user(root, a.userid)
    cfg = os.path.join(root, "userdata", user, "config")
    path = os.path.join(cfg, "shortcuts.vdf")
    opts = '-- --install "%s"' % os.path.abspath(a.install) if a.install else ""
    top, signed = app_id(exe, a.name)

    items = [(0, "shortcuts", [])]
    if os.path.exists(path):
        with open(path, "rb") as f:
            raw = f.read()
        items, _ = parse(raw)
        if dump(items) != raw:
            sys.exit("shortcuts.vdf does not read back unchanged; not touching it.")
    sc = get(items, "shortcuts")
    if sc is None:
        sc = []
        items.append((0, "shortcuts", sc))

    entry = [(2, "appid", signed), (1, "AppName", a.name), (1, "Exe", '"%s"' % exe),
             (1, "StartDir", '"%s"' % os.path.dirname(exe)),
             (1, "icon", os.path.join(cfg, "grid", "%d_icon.png" % top)), (1, "ShortcutPath", ""),
             (1, "LaunchOptions", opts), (2, "IsHidden", 0), (2, "AllowDesktopConfig", 1), (2, "AllowOverlay", 1),
             (2, "OpenVR", 0), (2, "Devkit", 0), (1, "DevkitGameID", ""), (2, "DevkitOverrideAppID", 0),
             (2, "LastPlayTime", 0), (1, "FlatpakAppID", ""), (0, "tags", [])]
    at = None
    for t, k, v in sc:
        if t == 0 and get(v, "AppName") == a.name:
            at = k
    if at is not None:
        old = get([x for x in sc if x[1] == at][0][2], "LastPlayTime")
        entry = [(t, k, old if k == "LastPlayTime" and old is not None else v) for t, k, v in entry]
        sc[:] = [(t, k, entry if k == at else v) for t, k, v in sc]
    else:
        used = {k for _, k, _ in sc}
        n = 0
        while str(n) in used:
            n += 1
        sc.append((0, str(n), entry))
    verb = ("Would update" if at is not None else "Would add") if a.dry_run else ("Updated" if at is not None else "Added")
    print("%s shortcut %r: %s %s (app id %d)" % (verb, a.name, exe, opts, top))
    if a.dry_run:
        return
    if steam_running() and not a.force:
        sys.exit("Steam is running; quit it first (it rewrites shortcuts.vdf on exit), or use --force.")

    os.makedirs(cfg, exist_ok=True)
    if os.path.exists(path):
        shutil.copy2(path, path + ".i26bak")
    with open(path, "wb") as f:
        f.write(dump(items))

    grid = os.path.join(cfg, "grid")
    os.makedirs(grid, exist_ok=True)
    for fname, w, h in (("%dp.png", 600, 900), ("%d.png", 460, 215), ("%d_hero.png", 1920, 620), ("%d_icon.png", 256, 256)):
        with open(os.path.join(grid, fname % top), "wb") as f:
            f.write(tile(w, h, a.name))
    print("Library tile written to %s" % grid)
    if not a.no_layout:
        install_layout(root, user, a.name)
    print("Done (Steam user %s). Start Steam again." % user)


if __name__ == "__main__":
    main()
