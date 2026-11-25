# Making the HD texture pack yourself

The modern look can draw every world, car and cockpit texture at four times the original's size from a pack in your user
folder. No pack ships with I26 and no game art is in this repository: the pack is made on your own machine, from your own
install, by an AI upscaler and a few scripts in `tools/`. This page says how, in two routes:

- **Quick**: one 4x pack, the free model only, a handful of commands. This is what most players want.
- **The full recipe**: what the packs shared in our testing were made with (more models, some of them non-commercial, and
  steps that need the developer tools). Optional; every step on top of the quick route is a refinement.

## What you need

1. **Interstate '76** (the GOG version, or any install folder with its files). The Nitro Pack is not needed for the pack.
2. **An I26 build that can export textures.** The `--export-textures` option is in this fork's builds, not yet in upstream's
   releases: download the newest `play-NN` zip for your system from the fork's
   [releases page](https://github.com/therealjkvalentine/I26/releases) and unzip it anywhere.
3. **This repository's tools**: `git clone https://github.com/therealjkvalentine/I26` (or the green **Code > Download ZIP**
   button on the fork's page, branch `main`, unzipped).
4. **Python 3.10 or newer** ([python.org](https://www.python.org/downloads/); on Windows tick "Add python.exe to PATH").
5. **Disk space**: about 5 GB for the Python packages with the graphics card build of PyTorch (0.7 GB for the CPU build),
   136 MB for the model, 40 MB for the export, about 1.4 GB for the finished 4x pack.
6. **An NVIDIA graphics card** with 4 GB of video memory or more (a run used 2.7 GB at most) for a run of under two
   hours. Without one it works on the CPU, overnight and then some (below).
   AMD and Intel cards are not used by the tool (it knows `cuda` and `cpu` only).

How long, measured on a GTX 1080 Ti (capped at 180 W) and a 4-core CPU, for the 6,509 textures the quick route does:

| Step | Graphics card | CPU only |
| --- | --- | --- |
| Install the Python packages | 5-10 min (2.5 GB download) | 2 min |
| Export (the game opens a small window and closes it) | 2 min | 2 min |
| Upscale | about 1 h 40 min | 15 to 20 hours |
| Lettering (`text-redraws.py`, 113 textures, CPU either way) | about 5 min | about 5 min |

The upscale resumes where it stopped: close the window or press Ctrl+C at any time and run the same command again.

## Quick: a 4x pack in four commands

### Windows (PowerShell)

Open PowerShell in the folder you cloned or unzipped the repository into (Shift + right-click in the folder, "Open
PowerShell window here"). Change the two paths in the last command to where your I26 and Interstate '76 are.

```powershell
py -m venv .venv
.venv\Scripts\python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu126
.venv\Scripts\python -m pip install spandrel numpy pillow etcpak
.venv\Scripts\python tools\make-hd-pack.py --game "C:\Games\i26-play-26" --install "C:\GOG Games\Interstate 76"
```

The second line is the graphics card build of PyTorch. **Do not skip it**: on Windows a plain `pip install torch` gives the
CPU-only build, and the upscale then takes the better part of a day instead of an hour. Without an NVIDIA card, leave
that line out and add `torch` to the third.

### Linux and macOS

```sh
python3 -m venv .venv
.venv/bin/pip install torch spandrel numpy pillow etcpak
.venv/bin/python tools/make-hd-pack.py --game ~/Games/i26-play-26 --install ~/"GOG Games/Interstate 76"
```

On Linux pip's `torch` already has the NVIDIA support. On a Mac the model runs on the CPU (the tool has no Apple GPU
path), so expect the CPU time above or more; point `--game` at `I26.app`.

### What `make-hd-pack.py` does

It prints each command before it runs it (`--dry-run` prints them and stops), so you can also type them yourself:

1. Export the install's textures as PNGs into `i26-hd-work/export` (the game starts in a small window, writes 6,563 PNGs
   and `bottom-up.txt`, and quits; it prints nothing):
   `i26 --windowed --resolution 640x480 -- --install DIR --export-textures i26-hd-work/export`
2. Upscale them: `python tools/upscale-textures.py i26-hd-work/export`. The first run downloads the model
   **4xTextures_GTAV_rgt-s_dither** by Philip Hofmann (CC-BY-4.0, 136 MB, from
   `https://github.com/Phhofm/models/releases/download/4xTextures_GTAV_rgt-s_dither/4xTextures_GTAV_rgt-s_dither.pth`)
   into `~/.cache/i26/models/`. The output is colour matched back to the original (the model shifts broad colours) and
   written as compressed `.dds` files with an `ATTRIBUTION.txt` and a `pack.json`.
3. Set the smallest painted lettering again from free fonts: `python tools/text-redraws.py i26-hd-work/export` (the
   speedometer's figures, licence plates, badges, signs; CPU only; the model's own files are kept in the pack's
   `.text-base/`, `--restore` puts them back).

The pack goes to the game's own folder: `%APPDATA%\I26\tex-world-4x` on Windows, `~/.local/share/I26/tex-world-4x` on
Linux, `~/Library/Application Support/I26/tex-world-4x` on macOS. `--out DIR` puts it elsewhere (then copy or move that
folder there yourself). `--only "pp11*,cb_1bd*"` does a few textures first, to see that everything works.

### Check that it worked

Start the game in the modern look (the default; F12 switches looks). **Options > Graphic Detail > Advanced** now has
**World Textures** and **Car & Dash Textures** rows reading **4x**: they only appear when a pack is found. With the
game started with `-- --log`, its log (`logs/godot.log` in the user folder) has lines like
`HD textures: .../I26/tex-world-4x/ (N files); world tex-world-4x, cars and dash tex-world-4x` at the start and
`HD textures served: 4x N` after a mission has loaded (`HD textures: first 4x upscale served (...)` before it).

### Switch it off

Set both rows to **1997**, or press F12 for the 1997 look (which always draws the originals), or rename the
`tex-world-4x` folder. Deleting the folder removes the pack for good (make it again with the same commands).

## The full recipe the shared packs use

The quick route runs one model over everything. The packs shared in our testing add the steps below. Each step is optional
and works on the pack the quick route made; several need things the quick route does not.

**Extra things the full recipe needs**

- `pip install scipy` (`--cells`, `--paint-masks`, `--paint-cut`, `menu-rules.py`), and `potracer resvg-py` for `flat-art.py`.
- The developer tools' command line, `i76` below (`tex-seams`, `key-edge`, `cat`, `bg`): it is not in the release zips. Install
  the .NET 8 SDK and run it from the repository as `dotnet run --project tools/I76.Cli -- --install DIR COMMAND ...`.
- The models below, downloaded by hand into `--models-dir` (default `~/.cache/i26/models`; a missing one stops the run and
  names its source). Two are **non-commercial**: a pack made with them is for your own use, not for sale or a paid
  bundle. `pack.json` and `ATTRIBUTION.txt` list every model a pack used.

| Model file | By | Licence | Where from | Used for |
| --- | --- | --- | --- | --- |
| `4xTextures_GTAV_rgt-s_dither.pth` | Philip Hofmann | CC-BY-4.0 | downloaded by the tool | everything (the quick route) |
| `2xHDCube_Compact.pth` | Venomalia | CC-BY-4.0 | [HDcube releases, Compact](https://github.com/Venomalia/HDcube/releases/download/Compact/2xHDCube_Compact.pth) | `--then` (the world and cars) and `--x2-model` (8x) |
| `4x-UltraSharpV2.safetensors` | Kim2091 | CC-BY-NC-SA-4.0, **non-commercial** | [Hugging Face](https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2.safetensors) | the dash panels, gear strip, compass, mirror |
| `4xHDcube3_500k.pth` | Venomalia | CC-BY-NC-SA-4.0, **non-commercial** | [HDcube 3.0](https://github.com/Venomalia/HDcube/releases/download/3.0/4xHDcube3_500k.pth) | the radar, damage panel, weapon panels, hands |
| `4xHDCube4Compact.pth` | Venomalia | HDcube licence (no rights claimed in outputs) | [HDcube 4.0 zip](https://github.com/Venomalia/HDcube/releases/download/4.0/4xHDcube4Compact.zip) | the notepads |
| `4xHDcube-Alpha.pth` | Venomalia | HDcube licence | the same 4.0 zip, its Alpha folder | the alpha step (dials, needles, reticle) |

**The steps**, in the order the shared 4x pack was made (`SRC` is the export, `PACK` the pack folder). The tool skips a
texture already in `--out`, so a step that redoes textures a pack already has (2 to 4) writes to an empty `--out` whose files
then replace the pack's; the shared pack's exact folders were not recorded, only each step's command and date in its
`pack.json`.

1. **World and cars through two models**: `upscale-textures.py SRC --then 2xHDCube_Compact.pth` (the model's 4x taken to
   8x by the second model, then brought down to 4x; sharper than the quick route's single model).
2. **Seams**: `i76 --install DIR tex-seams adjacency.json`, then `upscale-textures.py SRC --neighbours adjacency.json
   --then 2xHDCube_Compact.pth`: where two textures meet in the game (a hood against its fender, road pieces, the
   horizon), each is upscaled with its neighbour's texels round it, so the two agree at the edge.
3. **The cockpit by family**: `upscale-textures.py SRC --families --only "z*" --max-size 640`: each family of the
   cockpit's art through the model in the table above, and the dials, needles, reticle and binocular mask through the
   alpha step (their cut-out outline upscaled by 4xHDcube-Alpha: straight needles, round numerals).
4. **The damage panel's sprite sheets a cell at a time**: `i76 --install DIR cat vpit_1.elt > vpit_1.elt`, then
   `upscale-textures.py SRC --families --cells vpit_1.elt --only "zsye,zsye320"`.
5. **Cut-out edges** (road signs, the steering wheel and arms): `i76 --install DIR key-edge FIELDS 4 NAME...`, then
   `upscale-textures.py SRC --recut FIELDS --out PACK --only NAME...`.
6. **Painted two-tone edges and small lamps** (the police cruisers): `upscale-textures.py SRC --paint-masks MASKS --only
   "cp11*"`, `i76 --install DIR key-edge FIELDS 4 --masks MASKS STEM STEM.detail ...`, `upscale-textures.py SRC --paint-cut
   FIELDS --out PACK --only "cp11*"`.
7. **Lettering**: `text-redraws.py SRC` (as in the quick route; run it after any step that rewrote a lettered texture,
   after deleting the pack's `.text-base/` so the new files become the base).
8. **Mip chains of an old pack**: `python tools/dds-mips.py PACK --fix` repairs a pack made before November 2026 (its smaller
   levels had black under the key). A new pack does not need it.

**The 8x pack of the cockpit and the cars** (`tex-world-8x/` beside the 4x; Graphic Detail's Car & Dash Textures row then
offers 8x; 2.9 GB, 2,556 textures, about 0.5 GB more video memory, no difference in frame time). It needs the 4x pack and
the 8x masters, every texture through 4xTextures_GTAV then 2xHDCube_Compact kept at 8x as PNGs (`MASTERS_8X` below). Then:

    python tools/upscale-textures.py SRC --families --models-dir DIR --from MASTERS_8X --x2-model 2xHDCube_Compact.pth --masters-out FAMILY_MASTERS --only "z*" --skip "zmap*" --max-size 640 --scale 8
    python tools/upscale-textures.py SRC --from MASTERS_8X --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --only @tools/hd-8x-cars.txt,zdue --covers "cockpit & cars"
    python tools/upscale-textures.py SRC --families --family-masters FAMILY_MASTERS --from MASTERS_8X --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --max-size 640 --only "z*" --skip "zmap*" --covers "cockpit & cars"
    python tools/text-redraws.py SRC --pack PATH/TO/tex-world-8x

The first is the graphics card part (about 15 minutes on a GTX 1080 Ti), the second and third the packing on the CPU
(about half an hour on 8 cores). `--out` defaults to the user folder's `tex-world-8x/`; the mission maps (`zmap*`) stay at
4x. The masters themselves: the tool has no single switch that writes them; `upscale-textures.py SRC --then
2xHDCube_Compact.pth --scale 8 --format png --out MASTERS_8X` is the nearest (not checked against the shared pack's).

## The HD menu art (`tex-menu`)

The menus' own pictures at 4x (the pages and forms, the Esc pads, the pop-ups and blueprint sprites, the loading cards and
the mission maps) are a separate pack, `tex-menu/`, which stock I26 0.12.1 and later already read. It is made from your
install by one tool and a recipe, `tools/hd-recipes/menu.json`, which lists every step in the order the shared pack
(release 2026.11.23, "I26 4:3" 0.0.3) was made.

**What it needs, on top of the quick route's:** the .NET 8 SDK (for the export, `tools/I76.Cli`), `pip install scipy
opencv-python-headless scikit-image potracer resvg-py`, the models `4xTextures_GTAV_rgt-s_dither.pth` (the quick route's) and
`RealESRGAN_x4plus_anime_6B.pth` (by Xintao Wang et al., BSD-3-Clause, 18 MB: the fire photo inside the mission select
page's logo); the tool downloads each into `--models-dir` and checks its SHA-256, and **Windows' Arial** (`arial.ttf`, `arialbd.ttf`,
`ariali.ttf` in the system's Fonts folder): the pages' printed words are set again in it, as in the shared pack. Arial is
not free to share, so it is not in this repository; on Linux or macOS set `I26_ARIAL_DIR` to a folder with those three files
from a Windows copy you own, or the lettering step stops and says so. The mission maps also need the world export the quick
route makes (`i26-hd-work/export`).

```powershell
.venv\Scripts\python tools\menu-pack.py export --install "C:\GOG Games\Interstate 76" i26-hd-work\menu-export
.venv\Scripts\python tools\menu-pack.py build i26-hd-work\menu-export --out i26-hd-work\menu --world-export i26-hd-work\export
```

The first runs `i76 --install DIR menu-export OUT` (the shell's background records, the sprite banks 33-64 in the main
menu's palette, the seven Esc pads in t01's palette, the 22 loading cards, and font record 69's glyph boxes with the
install's Lee face for the circled key marks). The second writes `i26-hd-work\menu\tex-menu`; copy that folder into the
I26 user folder (`%APPDATA%\I26\tex-menu`), or zip its contents as `i76-hd-menus-NAME.zip` there. A folder in the user
folder is used before any menu zip. About 15 minutes with a graphics card (the model over 521 pictures, then the lettering
and clean-up passes on the CPU), and the maps a few minutes more.

The steps (`menu-pack.py list-steps`): **base** (the model over every picture, colour matched to the original as the
world pack is), **logo-photo** (page 14's logo through the second model, laid in inside the logo's colour mask; flat-art
later draws the logo again over it, so what stays is the fire photo seen through the logo's openings), **small-text** (the forms' small print from the original by bicubic 4x and an unsharp mask instead of the
model's), **lineart** (the blueprint sprites' 1-texel lines traced into smooth strokes), **lettering** (the pages' and
pads' printed words set again in Arial: the specs in `tools/text-redraws/menu/retype/`, and page 16's in
`tools/text-redraws/menu/`), **rules** (`menu-rules.py`: the forms' ruled lines), **flat-art** (`flat-art.py`: the title
logo as vector art), **fixes** (`tools/text-redraws/menu/fixes/`, in file order: the circled (C) (V) (R) key marks, the
inventory pages' title, the field salvage lines re-set), **maps** (`menu-maps.py`) and **finish** (`shellbg.json`,
`pads.json`, `sprites/sprites.json`, `pack.json`, `ATTRIBUTION.txt`).

**Fixing something:** the lettering is data. A misspelt or misplaced word on a page is a run in that page's spec (its
words and the rectangle round them in the original's 640x480 texels); edit it and rebuild just that page:
`menu-pack.py build EXPORT --out DIR --only shellbg4.webp`. The model's output is kept in `DIR\work`, so a rebuild runs
no model for a picture whose input is unchanged, and every step that names the page runs again from the model's picture.
`--only` takes globs of pack files (`sprites/36_*`, `6*.webp`, `nitro/load/*`). The spec ops are text-redraws.py's
(`erase`, `text`, ... above) plus the menu's: `retype`, `reset` (a line fitted to the original's print), `key-marks` and
`title`, described at the top of `tools/menu_lettering.py`.

**Checking a build against a release:** `menu-pack.py verify i26-hd-work\menu --against i76-hd-menus-i26-4x3-2026.11.23.zip
--report verify.txt` lists every file as identical, same (45 dB or more against the release's picture), look (35-45 dB),
different, missing or extra, with the worst twenty. The pack is not identical to the byte: the model's output differs in
the last bit between graphics cards and library versions, and lossy WebP re-encodes that; the recipe keeps the release's
lossy quality 95 so the comparison is like for like (`"encodings"` in the recipe; lossless is the option if you would
rather have a larger pack that does not depend on the encoder's version).

What is not covered: the Nitro Pack's menus (no Nitro menu pack has been made; the export runs on a Nitro install with
`I76_NATIVE=1`, but the lettering specs would be new work, page by page), and widescreen pages.

The older single tools still work on a pack you already have, in place, each keeping the files it replaces
(`.rules-prev/`, `.flat-prev/`, `.text-base/`) and putting them back with `--restore`: `menu-maps.py SRC`, `menu-rules.py
BG_DIR`, `flat-art.py BG_DIR` and `text-redraws.py BG_DIR --menu` (BG_DIR as `i76 --install DIR bg BG_DIR` writes the
records).

## Notes

- `upscale-textures.py --from OLD_PACK --out NEW_DIR` repacks a pack of 4x PNGs made earlier into the game's `.dds` without
  running the model again, colour matching it to your export on the way. `--no-colour-match` writes the model's colours as
  they come. `--scale 2` or `3` writes a smaller `tex-world-2x/` or `-3x/`, which the game reads only when shell.cfg's
  `hd_dash_size` / `hd_car_size` (or the Developer panel) names it. An older plain `tex-world/` folder is still read.
- On a second machine, copy the pack folder over (it is yours: your install, your machine), or make it again there. The
  lettering specs are in the repository, so `text-redraws.py` gives every machine the same lettering; `--pack DIR` points it
  at a pack outside the user folder.
- `python tools/upscale-textures.py --self-check` and `python tools/text-redraws.py --self-check` test the colour match and
  the lettering on made-up pictures, no game art and no model. The tools' tests: `python -m unittest discover -s tools -p
  "test_*.py"` (needs `scipy potracer resvg-py` too).
- Graphic Detail's rows and the folders the game reads (`tex-world-8x`, `-4x`, `-3x`, `-2x`, a plain `tex-world`, or a
  pack left zipped beside the program): [playing.md](playing.md), HD textures.
- What a pack costs in video memory and loading: [perf/costs.md](perf/costs.md).
