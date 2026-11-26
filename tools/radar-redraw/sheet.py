"""The contact sheet of tools/radar-redraw.py: the label sets against the original and the current upscale, both styles, a
sweep and a locked frame, zoomed crops of the label area and the scope's centre, and strips of consecutive sweep frames.
Everything is shown the way the game shows it (rows flipped). Written to OUT_DIR as sheet-radar.png, its parts and index.html."""
import html, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

PANEL_W = 180          # original px of the frame that hold the radar (the rest is the colour key)
SWEEP_FRAME, LOCKED_FRAME = 4, 26
STRIP = list(range(0, 8))
BG = (24, 24, 28); FG = (235, 235, 235); DIM = (160, 160, 170)


def _font(rr, px):
    return ImageFont.truetype(os.path.join(rr.REPO_FONTS, "Arimo", "Arimo[wght].ttf"), px)


def game_view(a):
    """RGBA -> RGB in the game's view (rows flipped), the key shown dark."""
    a = a[::-1].astype(float); al = a[..., 3:4] / 255
    return Image.fromarray(np.clip(a[..., :3] * al + np.array(BG) * (1 - al), 0, 255).astype(np.uint8))


def label_cell(img, text, font, sub=None):
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, img.width, 30 if sub is None else 52), fill=(0, 0, 0))
    d.text((8, 4), text, font=font, fill=FG)
    if sub: d.text((8, 30), sub, font=_font_small, fill=DIM)
    return img


_font_small = None


def sheet(rr, export_dir, upscale_dir, out_dir, spec):
    global _font_small
    os.makedirs(out_dir, exist_ok=True)
    f_big, f_mid = _font(rr, 26), _font(rr, 20); _font_small = _font(rr, 17)
    frames = rr.load_frames(export_dir, spec)
    g, med = rr.measure(frames, spec, export_dir)
    S = 8
    sets = spec["label_sets"]
    off = spec["frames"]["sweep"] % spec["frames"]["count"]
    panel_base = rr.upscale_frame(upscale_dir, off, S)
    redraws = {(ls["name"], st): rr.Redraw(g, spec, st, ls, S, panel_base) for ls in sets for st in ("A", "B")}

    def orig(kind, i):
        a = frames[spec["frames"][kind] % i]; return np.repeat(np.repeat(a, S, 0), S, 1)

    def up(kind, i):
        return rr.upscale_frame(upscale_dir, spec["frames"][kind] % i, S)

    def red(name, st, kind, i):
        return redraws[(name, st)].frame(up(kind, i), kind, i)

    parts = []
    # ---- the two frames: rows = label sets, columns = original / current / A / B
    cw, ch = PANEL_W * 4, 128 * 4
    for kind, i in (("sweep", SWEEP_FRAME), ("locked", LOCKED_FRAME)):
        stem = spec["frames"][kind] % i
        lw = 260; head = 60
        W = lw + 4 * cw; H = head + len(sets) * ch
        im = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(im)
        d.text((10, 12), f"{stem} ({'sweep' if kind == 'sweep' else 'locked on a target'}), as the game shows it; each cell 4x the original's pixels",
               font=f_big, fill=FG)
        cols = ["original (nearest)", "current 8x upscale", "redraw A (faithful)", "redraw B (phosphor)"]
        o_img = game_view(orig(kind, i)).crop((0, 0, PANEL_W * S, 128 * S)).resize((cw, ch), Image.LANCZOS)
        u_img = game_view(up(kind, i)).crop((0, 0, PANEL_W * S, 128 * S)).resize((cw, ch), Image.LANCZOS)
        for r, ls in enumerate(sets):
            y = head + r * ch
            d.text((10, y + 10), ls["name"], font=f_big, fill=FG)
            words = [t["text"] for t in ls.get("texts", [])] or ["(no words)"]
            yy = y + 48
            for w in words: d.text((10, yy), w, font=f_mid, fill=DIM); yy += 26
            for c in range(4):
                if c == 0: cell = o_img.copy()
                elif c == 1: cell = u_img.copy()
                else: cell = game_view(red(ls["name"], "AB"[c - 2], kind, i)).crop((0, 0, PANEL_W * S, 128 * S)).resize((cw, ch), Image.LANCZOS)
                label_cell(cell, f"{cols[c]} | {ls['name'] if c >= 2 else 'same in every row'}", f_mid)
                im.paste(cell, (lw + c * cw, y))
        name = f"part-{kind}.png"; im.save(os.path.join(out_dir, name)); parts.append((name, f"{stem}: label sets by column"))

    # ---- zoomed crops, full 8x: the label area and the scope's centre
    x0, y0, x1, y1 = 10, 6, 48, 96                      # the left panel, game view, original px
    crops = [("original (nearest)", game_view(orig("sweep", SWEEP_FRAME))), ("current 8x", game_view(up("sweep", SWEEP_FRAME)))]
    for ls in sets: crops.append((f"A: {ls['name']}", game_view(red(ls["name"], "A", "sweep", SWEEP_FRAME))))
    cwz, chz = (x1 - x0) * S, (y1 - y0) * S
    im = Image.new("RGB", (len(crops) * (cwz + 12) + 12, chz + 80), BG); d = ImageDraw.Draw(im)
    d.text((12, 10), "the left panel at full 8x (game view): marks, knob pointers and lettering", font=f_big, fill=FG)
    for k, (t, a) in enumerate(crops):
        cell = a.crop((x0 * S, y0 * S, x1 * S, y1 * S)); im.paste(cell, (12 + k * (cwz + 12), 70))
        d.text((12 + k * (cwz + 12), 46), t, font=f_mid, fill=DIM)
    name = "part-labels-zoom.png"; im.save(os.path.join(out_dir, name)); parts.append((name, "label area, full 8x"))

    gc = g["grid"]["centre"]; R = 18
    cx, cy = gc[0], 128 - gc[1]
    box = (int((cx - R) * S), int((cy - R) * S), int((cx + R) * S), int((cy + R) * S))
    crops = [("original (nearest)", game_view(orig("sweep", SWEEP_FRAME))), ("current 8x", game_view(up("sweep", SWEEP_FRAME))),
             ("A", game_view(red(sets[0]["name"], "A", "sweep", SWEEP_FRAME))), ("B", game_view(red(sets[0]["name"], "B", "sweep", SWEEP_FRAME))),
             ("original locked", game_view(orig("locked", LOCKED_FRAME))), ("current locked", game_view(up("locked", LOCKED_FRAME))),
             ("A locked", game_view(red(sets[0]["name"], "A", "locked", LOCKED_FRAME))), ("B locked", game_view(red(sets[0]["name"], "B", "locked", LOCKED_FRAME)))]
    cz = 2 * R * S
    im = Image.new("RGB", (4 * (cz + 12) + 12, 2 * (cz + 40) + 60), BG); d = ImageDraw.Draw(im)
    d.text((12, 10), "the scope's centre at full 8x (game view): pip, rings, spokes, beam and cone", font=f_big, fill=FG)
    for k, (t, a) in enumerate(crops):
        x = 12 + (k % 4) * (cz + 12); y = 60 + (k // 4) * (cz + 40)
        im.paste(a.crop(box), (x, y + 28)); d.text((x, y), t, font=f_mid, fill=DIM)
    name = "part-centre-zoom.png"; im.save(os.path.join(out_dir, name)); parts.append((name, "scope centre, full 8x"))

    # ---- motion: consecutive sweep frames
    fc = g["face"]["centre"]; fr = g["face"]["radius"] + 2
    fcy = 128 - fc[1]
    sb = (int((fc[0] - fr) * S), int((fcy - fr) * S), int((fc[0] + fr) * S), int((fcy + fr) * S))
    cs = 300
    rows = [("original", lambda i: game_view(orig("sweep", i))), ("current 8x", lambda i: game_view(up("sweep", i))),
            ("A", lambda i: game_view(red(sets[0]["name"], "A", "sweep", i))), ("B", lambda i: game_view(red(sets[0]["name"], "B", "sweep", i)))]
    im = Image.new("RGB", (160 + len(STRIP) * (cs + 8), 60 + len(rows) * (cs + 8)), BG); d = ImageDraw.Draw(im)
    d.text((12, 10), f"sweep frames {STRIP[0]}..{STRIP[-1]} in order (0.2 s apart in the game, clockwise)", font=f_big, fill=FG)
    for r, (t, fn) in enumerate(rows):
        y = 60 + r * (cs + 8); d.text((12, y + cs // 2 - 12), t, font=f_big, fill=FG)
        for k, i in enumerate(STRIP):
            cell = fn(i).crop(sb).resize((cs, cs), Image.LANCZOS)
            ImageDraw.Draw(cell).text((6, 4), f"f{i:03d}", font=f_mid, fill=FG)
            im.paste(cell, (160 + k * (cs + 8), y))
    name = "part-motion.png"; im.save(os.path.join(out_dir, name)); parts.append((name, "motion strip"))

    # ---- the whole sheet: the parts stacked
    imgs = [Image.open(os.path.join(out_dir, n)) for n, _ in parts]
    W = max(i.width for i in imgs); H = sum(i.height for i in imgs) + 20 * len(imgs)
    sh = Image.new("RGB", (W, H), BG); y = 0
    for i in imgs: sh.paste(i, (0, y)); y += i.height + 20
    sh.save(os.path.join(out_dir, "sheet-radar.png"))
    with open(os.path.join(out_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write("<!doctype html><meta charset=utf-8><title>Radar redraw</title><style>body{background:#18181c;color:#eee;font:15px sans-serif;margin:16px}"
                "img{display:block;max-width:none;margin:8px 0 28px}h2{font-weight:600}</style>\n<h1>Cockpit radar redraw: contact sheet</h1>\n")
        f.write("<p>Shown as the game shows the frames (rows flipped). Label sets: " +
                "; ".join(html.escape(f"{ls['name']}: {', '.join(t['text'] for t in ls.get('texts', [])) or 'no words'} ({ls.get('note', '')})") for ls in sets) + "</p>\n")
        for n, t in parts: f.write(f"<h2>{html.escape(t)}</h2><img src='{n}' alt='{html.escape(t)}'>\n")
    print("sheet:", os.path.join(out_dir, "sheet-radar.png"))
    return 0
