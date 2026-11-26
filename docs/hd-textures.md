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
   `i26 --windowed --resolution 640x480 --audio-driver Dummy -- --install DIR --export-textures i26-hd-work/export`
2. Upscale them: `python tools/upscale-textures.py i26-hd-work/export`. The first run downloads the model
   **4xTextures_GTAV_rgt-s_dither** by Philip Hofmann (CC-BY-4.0, 136 MB, from
   `https://github.com/Phhofm/models/releases/download/4xTextures_GTAV_rgt-s_dither/4xTextures_GTAV_rgt-s_dither.pth`)
   into `~/.cache/i26/models/`. The output is colour matched back to the original (the model shifts broad colours) and
   written as compressed `.dds` files with an `ATTRIBUTION.txt` and a `pack.json`.
3. Set the smallest painted lettering again from free fonts: `python tools/text-redraws.py i26-hd-work/export` (the
   speedometer's figures, licence plates, badges, signs; CPU only; the model's own files are kept in the pack's
   `.text-base/`, `--restore` puts them back).
4. Optional, as the shared packs have it: the cockpit radar drawn again over the pack's frames (`pip install scipy` first):
   `python tools/radar-redraw.py pack i26-hd-work/export PACK`, PACK the pack's folder
   ([The cockpit radar redrawn](#the-cockpit-radar-redrawn-step-13-of-the-recipe)).
5. Optional, likewise: the damage panel's wires drawn again and a light wear on it and the weapon counters:
   `python tools/hud-redraw.py pack i26-hd-work/export PACK`
   ([The damage panel and counters redrawn](#the-damage-panel-and-counters-redrawn-step-14-of-the-recipe)).

The pack goes to the game's own folder: `%APPDATA%\I26\tex-world-4x` on Windows, `~/.local/share/I26/tex-world-4x` on
Linux, `~/Library/Application Support/I26/tex-world-4x` on macOS. `--out DIR` puts it elsewhere (then copy or move that
folder there yourself). `--only "pp11*,cb_1bd*"` does a few textures first, to see that everything works.

### Check that it worked

Start the game in the modern look (the default; F12 switches looks). **Options > Graphic Detail** (its World and Car & Cockpit pages) now has
**World Textures** and **Car & Dash Textures** rows reading **4x**: they only appear when a pack is found. With the
game started with `-- --log`, its log (`logs/godot.log` in the user folder) has lines like
`HD textures: .../I26/tex-world-4x/ (N files); world tex-world-4x, cars and dash tex-world-4x` at the start and
`HD textures served: 4x N` after a mission has loaded (`HD textures: first 4x upscale served (...)` before it).

### Switch it off

Set both rows to **1997**, or press F12 for the 1997 look (which always draws the originals), or rename the
`tex-world-4x` folder. Deleting the folder removes the pack for good (make it again with the same commands).

## The full recipe the shared packs use

The quick route runs one model over everything. The packs shared in our testing (release 2026.11.23) took more steps, each
written down below in the order they were really run. Nothing in them was painted by hand: every step is a command or a
small spec in this repository, and the models are pinned by their SHA-256 in `tools/hd-recipes/models.json`, one list for
the world and the menu packs (the tools refuse a model file of the same name with another hash). What was manual is which
textures each step covers: those choices
are the name lists in `tools/hd-recipes/lists/`.

**In one command.** `tools/hd-pack.py` runs the steps below from their recipes, `tools/hd-recipes/world-4x.json` and
`world-8x.json` (the menu pack's `menu.json` has its own runner, `tools/menu-pack.py build`, which `hd-pack.py menu` runs
with the same options: see [The HD menu art](#the-hd-menu-art-tex-menu)):

    python tools/hd-pack.py world-4x --install "C:\GOG Games\Interstate 76" --work i26-hd-work --out i26-hd-packs
    python tools/hd-pack.py all --install DIR --work i26-hd-work --out i26-hd-packs      the 4x, the 8x and the menu pack

It exports the textures with `i76 export-textures` (or `--game PATH` for an I26 build's `--export-textures`, or `--export
DIR` for an export made earlier), downloads any model it lacks into `--models-dir` and refuses one whose hash differs,
makes the 8x masters on the graphics card (about 30 minutes on an RTX 5090; `--masters-from DIR` takes masters made
earlier instead), and writes the pack into `--out` (never the game's own folder: copy it there yourself), with `pack.json`
naming the recipe, its commit, the tools', models' and libraries' versions and the graphics card, `ATTRIBUTION.txt` and
`FILES.sha256`. A second run skips every step whose inputs (the tool, the step, the models, the lists and specs it names,
the export) have not changed. `--list-steps` and `--dry-run` show what it would do.

To change one texture, edit its spec or list and rebuild it alone: `python tools/hd-pack.py world-4x --install DIR --only
hb_1bdf1` runs every step that names it, for it only, in the pack already built. `python tools/hd-pack.py verify
i26-hd-packs/tex-world-4x --against I26-HD-Textures-4x-2026.11.23.zip` compares a pack with a release, file by file
(identical, the same top level, or the decoded pictures' PSNR), by the step that last wrote each, and writes the list;
`python tools/hd-pack.py release OUT --version 2026.12.01 i26-hd-packs/tex-world-4x i26-hd-packs/tex-world-8x` zips the
packs as the releases are (`--notes FILE` for the README's "what changed").

**Extra things the full recipe needs**

- `pip install scipy` (`--cells`, `--paint-masks`, `--paint-cut`, `menu-rules.py`, `radar-redraw.py`), and `potracer resvg-py` for `flat-art.py`.
- `pip install opencv-python-headless` for the barn doors' braces only (the release's inpaint; see step 12).
- The developer tools' command line, `i76` below (`tex-seams`, `key-edge`, `cat`, `bg`): it is not in the release zips. Install
  the .NET 8 SDK and run it from the repository as `dotnet run --project tools/I76.Cli -- --install DIR COMMAND ...`.
- The models below, downloaded by hand into `--models-dir` (default `~/.cache/i26/models`; a missing one stops the run and
  names its source). Two are **non-commercial**: a pack made with them is for your own use, not for sale or a paid
  bundle. `pack.json` and `ATTRIBUTION.txt` list every model a pack used.

| Model file | By | Licence | Where from | Used for |
| --- | --- | --- | --- | --- |
| `4xTextures_GTAV_rgt-s_dither.pth` | Philip Hofmann | CC-BY-4.0 | downloaded by the tool | everything (the quick route), the masters' first pass |
| `2xHDCube_Compact.pth` | Venomalia | CC-BY-4.0 | [HDcube releases, Compact](https://github.com/Venomalia/HDcube/releases/download/Compact/2xHDCube_Compact.pth) | the masters' second pass, `--then` (seams) and `--x2-model` (8x) |
| `4x-UltraSharpV2.safetensors` | Kim2091 | CC-BY-NC-SA-4.0, **non-commercial** | [Hugging Face](https://huggingface.co/Kim2091/UltraSharpV2/resolve/main/4x-UltraSharpV2.safetensors) | the dash panels, gear strip, compass, mirror |
| `4xHDcube3_500k.pth` | Venomalia | CC-BY-NC-SA-4.0, **non-commercial** | [HDcube 3.0](https://github.com/Venomalia/HDcube/releases/download/3.0/4xHDcube3_500k.pth) | the radar, damage panel, weapon panels, hands, gas pumps |
| `4xHDCube4Compact.pth` | Venomalia | HDcube licence (no rights claimed in outputs) | [HDcube 4.0 zip](https://github.com/Venomalia/HDcube/releases/download/4.0/4xHDcube4Compact.zip) | the notepads |
| `4xHDcube-Alpha.pth` | Venomalia | HDcube licence | the same 4.0 zip, `Models/HDcube/` | the alpha step (dials, needles, reticle) |

**The steps of the 4x pack**, in the order they were run (`SRC` is the export, `M` the models folder, `PACK` the pack
folder; a step that redoes textures the pack already has writes to an empty `--out` whose files then replace the pack's,
because the tool skips a texture already in `--out`):

1. **Export** as in the quick route (palette `t01.act`): 6,563 PNGs and `bottom-up.txt`.
2. **The 8x masters**: every texture up to 640 texels through 4xTextures_GTAV, then 2xHDCube_Compact, kept at 8x as PNGs, not
   colour matched (about 5 GB; a work folder, never shipped). The release ran the first model over the whole picture for the
   textures up to 512 texels and in 128-texel tiles for the 53 larger ones, the second in 256-texel tiles, all in full
   precision:
   `upscale-textures.py SRC --masters-out MASTERS --model M/4xTextures_GTAV_rgt-s_dither.pth --then 2xHDCube_Compact.pth --models-dir M --tile 0 --then-tile 256 --precision fp32 --max-size 512`,
   then the same with `--tile 128 --max-size 640`.
3. **The base**: `upscale-textures.py SRC --from MASTERS --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 4 --max-size 640 --out PACK`
   (each master colour matched to the original, brought down to 4x, BC7).
4. **The cockpit by family**: `upscale-textures.py SRC --families --models-dir M --from MASTERS --only "z*" --max-size 1100 --scale 4`
   (1100, not 640: the weapons' side strip `zdue` is 186x1035 and the notepads 640x480). Each family of the cockpit's art
   through the model in the table above, and the dials, needles, reticle and binocular mask through the alpha step (their
   cut-out outline upscaled by 4xHDcube-Alpha: straight needles, round numerals).
5. **Seams**: `i76 --install DIR tex-seams adjacency.json`, then `upscale-textures.py SRC --neighbours adjacency.json --model
   M/4xTextures_GTAV_rgt-s_dither.pth --then 2xHDCube_Compact.pth --models-dir M --only @tools/hd-recipes/lists/seams.txt`:
   where two textures meet in the game (a hood against its fender, road pieces, the horizon), each is upscaled with its
   neighbour's texels round it, so the two agree at the edge. 135 of the worst seams were tried and the 115 whose seam
   scores got better kept (the list); these go through `--then` inside the tool, not through the masters.
6. **Cut-out edges** of the road signs and other cut-out scenery: `i76 --install DIR key-edge FIELDS 4 NAME...` for the names in
   `lists/cutouts.txt`, then `upscale-textures.py SRC --recut FIELDS --out PACK --only @tools/hd-recipes/lists/cutouts.txt`.
7. **The gas pumps**: the seven pump textures through 4xHDcube3_500k (the `pumps` family), colour matched; on the four fronts
   the counter window is then the original's texels enlarged without a model (a `from-original` fix, step 10's tool). This
   replaced three of the seam list's textures (`fg_1pmt1`, `fn_1pmf1`, `fs_1pmf1`).
8. **The damage panel's sprite sheets a cell at a time**: `i76 --install DIR cat vpit_1.elt > vpit_1.elt`, then
   `upscale-textures.py SRC --families --cells vpit_1.elt --panels PACK --only "zsye,zsye320" --max-size 640` (the release ran
   `zsye` on the CPU).
9. **The steering wheel's and the arms' frames**: `key-edge` as in step 6 for `zswls*,zhl45*,zhr45*,zsl45*,zsr45*` (270),
   then `--recut FIELDS --recut-edge 0.9`.
10. **Lettering and the other fixes**: `text-redraws.py SRC --pack PACK`: the 113 lettered textures set again from free fonts
   (the specs in `tools/text-redraws/`; specs accepted since the release, such as the road signs of
   [Fixing a lettered texture](#fixing-a-lettered-texture), letter more, so a rebuild differs from the release on those),
   and the three fixes that are specs of the same tool: the pumps' counters
   (`from-original`), the barn doors' braces (`bands`: `hb_1bdf1`, `hj_1bdf1`, drawn as straight bands fitted to the
   original) and the factory forecourt's border (`border-recolour`: `ca_1lt_6`, a colour slip in the original's outer texels).
   Run it after every step that rewrote a lettered texture, after deleting the pack's `.text-base/` copies of those.
11. **Painted two-tone edges and small lamps** (the police cruiser, the Clydesdale, the school bus, T09's Dover), after the
   lettering: `upscale-textures.py SRC --paint-masks MASKS --mask-rules tools/hd-recipes/masks-cars.json` (the masks kept:
   the two-tone rule found glass against paint on the Clydesdale's and the bus's windows, so only the cruiser keeps its
   two-tone masks; the Dover's keep their largest piece and its red lamps are found by colour), `i76 --install DIR key-edge
   FIELDS 4 --masks MASKS STEM STEM.detail ...`, `upscale-textures.py SRC --paint-cut FIELDS --out PACK --only
   "cp11*,fd11*,xb11*,dl1?*"`.
12. **Mip chains**: the tools write each file's full chain from its top level. The release's files were made before that
   (their chains rewritten later by `dds-mips.py --fix`, the top levels kept), so a rebuild matches the release's top levels,
   not every smaller level. `python tools/dds-mips.py PACK --fix` repairs a pack made before November 2026.
13. **The radar** (after the release, 2026-11-26), last: `radar-redraw.py pack SRC PACK` draws the radar's 62 frames again
   over the pack's own (style B, the lock-range labels) and writes the range plate's and contact lamp's sheets from them,
   which go with them: [The cockpit radar redrawn](#the-cockpit-radar-redrawn-step-13-of-the-recipe). CPU only, about a
   minute.
14. **The damage panel and the counters** (2026-11-26), after the radar: `hud-redraw.py pack SRC PACK --distress medium`
   draws the damage panel's wires again and lays a medium wear on it, its lights and the counters' housings (the radar step
   lays the same wear on the radar, `--distress medium`, so the three units match):
   [The damage panel and counters redrawn](#the-damage-panel-and-counters-redrawn-step-14-of-the-recipe). CPU only, under
   a minute.

**The 8x pack of the cockpit and the cars** (`tex-world-8x/` beside the 4x; Graphic Detail's Car & Dash Textures row then
offers 8x; 2.9 GB, 2,579 textures, about 0.5 GB more video memory, no difference in frame time). It needs the 8x masters of
step 2 (`MASTERS` below). Then:

    python tools/upscale-textures.py SRC --families --models-dir M --from MASTERS --x2-model 2xHDCube_Compact.pth --masters-out FAMILY_MASTERS --only "z*" --skip "zmap*" --max-size 640 --scale 8
    python tools/upscale-textures.py SRC --from MASTERS --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --only @tools/hd-8x-cars.txt,zdue --covers "cockpit & cars"
    python tools/upscale-textures.py SRC --families --family-masters FAMILY_MASTERS --from MASTERS --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --max-size 640 --only "z*" --skip "zmap*" --covers "cockpit & cars"

then the 4x pack's steps 8 to 11 with `--scale 8` (and `key-edge FIELDS 8`): the damage panel's cells (with `--x2-model
2xHDCube_Compact.pth`), the frames, the lettering (61 of the specs have a texture in the 8x) and the cars' painted edges,
then steps 13 and 14, the radar and the damage panel, over the 8x's own files.
The first is the graphics card part (about 15 minutes on a GTX 1080 Ti), the second and third the packing on the CPU
(about half an hour on 8 cores). `--out` defaults to the user folder's `tex-world-8x/`; the mission maps (`zmap*`) stay at
4x. The car list `tools/hd-8x-cars.txt` comes from `i76 car-table survey`, with the 23 lit brake lamp textures
(`*bkx1`, `cp11bko1`) the survey does not reach added by name.

## The HD menu art (`tex-menu`)

The menus' own pictures at 4x (the pages and forms, the Esc pads, the pop-ups and blueprint sprites, the loading cards and
the mission maps) are a separate pack, `tex-menu/`, which stock I26 0.12.1 and later already read. It is made from your
install by one tool and a recipe, `tools/hd-recipes/menu.json`, which lists every step in the order the shared pack
(release 2026.11.23, "I26 4:3" 0.0.3) was made.

**What it needs, on top of the quick route's:** the .NET 8 SDK (for the export, `tools/I76.Cli`), `pip install scipy
opencv-python-headless scikit-image potracer resvg-py`, the models `4xTextures_GTAV_rgt-s_dither.pth` (the quick route's) and
`RealESRGAN_x4plus_anime_6B.pth` (by Xintao Wang et al., BSD-3-Clause, 18 MB: the fire photo inside the mission select
page's logo); the tool downloads each into `--models-dir` and checks its SHA-256 against `tools/hd-recipes/models.json`
(the recipe names the files, the pins are there), and **Windows' Arial** (`arial.ttf`, `arialbd.ttf`,
`ariali.ttf` in the system's Fonts folder): the pages' printed words are set again in it, as in the shared pack. Arial is
not free to share, so it is not in this repository; on Linux or macOS set `I26_ARIAL_DIR` to a folder with those three files
from a Windows copy you own, or the lettering step stops and says so. The mission maps also need the world export the quick
route makes (`i26-hd-work/export`).

In one command, beside the world packs (the export of the menus in `i26-hd-work\menu\export`, the world export the maps
need in `i26-hd-work\export`, made first when it is not there yet; the pack in `i26-hd-packs	ex-menu`):

```powershell
.venv\Scripts\python tools\hd-pack.py menu --install "C:\GOG Games\Interstate 76" --work i26-hd-work --out i26-hd-packs
```

or the two steps by hand:

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
`menu-pack.py build EXPORT --out DIR --only shellbg4.webp` (or `hd-pack.py menu ... --only shellbg4.webp`). The model's
output is kept in `DIR\work`, so a rebuild runs
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

### The Nitro Pack's pages (`nitro/`)

The Nitro Pack renumbered the shell database (`database.tvf`, 76 records against 83) and redrew some of it. The game reads
Nitro's own art from the menu pack's `nitro/` folder by Nitro's own numbers: `nitro/shellbg<record>.webp` with
`nitro/shellbg.json`, and `nitro/sprites/<bank>_<sprite>.webp` with `nitro/sprites/sprites.json`. A page Nitro left as the
base game's is shown from the base pack (no file is needed); every sprite on a Nitro screen comes from `nitro/sprites/`,
except the pop-ups of bank 30 that are the base game's bank 36, which the game takes from the base pack itself. The
recipe's `"nitro"` section makes that folder, with the same model, tiles, colour match and WebP settings as the base pack:

```powershell
.venv\Scripts\python tools\menu-pack.py export --nitro --install "C:\GOG Games\Interstate 76 Nitro Pack" i26-hd-work\nitro-export
.venv\Scripts\python tools\menu-pack.py build --nitro i26-hd-work\nitro-export --base-export i26-hd-work\menu-export --base-pack i26-hd-work\menu\tex-menu --out i26-hd-work\nitro
```

`--base-export` is the base game's export (above): a Nitro sprite whose picture (size and texels) is one of the base game's is
the base pack's finished file (`--base-pack`: a built `tex-menu` folder or a release zip such as
`i76-hd-menus-i26-4x3-2026.11.23.zip`), copied under its Nitro name, so the blueprints keep their traced lines. The rest goes
through the steps `menu-pack.py list-steps` shows under `nitro`: **base** (the model over the 14 pages Nitro redrew and the
sprites it added), **reuse**, **small-text**, **lineart**, **lettering** (`tools/text-redraws/menu/nitro/`: each spec keeps the
base page's runs where Nitro prints the same words in the same place, and adds Nitro's own), **rules** and **finish**. The
result is `i26-hd-work\nitro\tex-menu\nitro`: copy that `nitro` folder into the menu pack's folder
(`%APPDATA%\I26\tex-menu\nitro`, beside the `load` folder already there). Only one menu pack is read, so the folder has to
go inside it; a pack read from its zip needs the zip extracted to `tex-menu` first. The loading cards stay the base pack's
(`nitro/load/` is one folder for both games).

What is not covered: Nitro's main-menu logo (record 22) is the model's picture (its letters are textured, not flat art), and
the pop-ups' printed words are the model's as in the base pack. Widescreen pages are a pack of their own (next section).

### The widescreen menu pack (`tex-menu-wide`)

An optional pack for windows wider than 4:3: the **sides** of the menu pack's pages, so a page fills a 16:9 window (and, with
its ultrawide layer, a 21:9 one) instead of sitting between black bars. It holds no page: the game puts the 4:3 pack's own page
in the middle, untouched, and the strips beside it when it loads the page, so the hotspots, the words and everything the game
draws on a page stay exactly where they were (the sides are decoration only). Graphic Detail > Display has a **Menu Width** row
(Wide\*, the default, or 4:3) when the pack is installed; the 1997 look never uses it.

**Its layout.** `tex-menu-wide/wide.json` lists the layers (`16x9`: 3414x1920, 427 texels added on each side of the 2560x1920
page; `21x9`: 4588x1920, 587 more on each side of the 16:9 page) and, per record (`pages`, and the Nitro Pack's own under
`nitro`, by its numbers), each layer's strip file, `16x9/shellbg<N>.webp` (the left side and the right side side by side, 1920
tall), or `"pad": true` for a page whose edges are one colour (the main menu's logo on black, the credits, YOU ARE UNCONSCIOUS,
Tad's card on its brown: each row's outer four columns repeated out, no file), and the 4:3 page's edges as a fingerprint (the
mean colour of the outer four columns in sixteen bands). A window takes the narrowest layer that fills it (16:10 and 16:9 the
16:9 layer, 21:9 and wider the 21:9), and the pack's sides are used only over the page they were made for: a page of another
4:3 menu pack whose edges differ by more than 8 levels is shown at 4:3 (a rebuilt "I26 4:3" pack, a level or two off the
release, is widened). Strips rather than whole wide pages: the 16:9 layer is 7 MB where its pages whole would be about 70, and
the middle of a wide page can never be anything but the 4:3 pack's.

**How it is made.** Per page the recipe `tools/hd-recipes/menu-wide.json` says which of four ways (`class`):

- `model`: an image model drew the sides. The 4:3 page is put on a canvas of the model's own output ratio (Gemini 3 Pro Image's
  2K "16:9" is 2752x1536, 1.7917:1, not 16:9), the new parts a placeholder (`mirror`: the edge mirrored and blurred; `telea`: the
  edge's colours carried outward with no structure), and sent with a prompt that says plainly what the finished picture is and
  what is cut at each edge (for the forms: the same sheet of paper, wider, no lines, no boxes, no text; for the illustrated
  pages the objects cut by the edge named: on Joe's pads the toast and its plate, the saucer, the spoon). Two or three variants
  were made per page and one picked by eye. The model's picture (the **master**) is then aligned back onto the page (a template
  match, then an ECC affine: sub-pixel), warped once into the layer's frame, and joined by the seam treatment S4: a local optical
  flow between the model's drawing of the page and the page at each join, faded out across the side; the side taken to the
  art's own size and up again through the 4:3 pack's upscaler (4xTextures_GTAV_rgt-s_dither, the same tiles), so it has the
  pack's grain; then a Poisson membrane that makes the side meet the page's edge exactly, its low frequencies spreading far,
  its fine detail a few texels. The join is the page's own edge: not one texel of the page is redrawn.
  The 21:9 layer was made the same way from each page's finished 16:9 page (the model given the whole 16:9 page as the
  middle, its 21:9 canvas 2.357:1, so the page is made a little smaller with thin bands above and below to fill too, cropped
  after): compared on five pages with extending in two smaller steps and with one step from the 4:3 page, this one call gave
  no invented objects or streaks and keeps the 16:9 sides, which the 21:9 strips go outside.
- `borrow`: the page is printed on the same art as another at its edges (the forms are one paper scan; Joe's pads one counter),
  checked by pixels; it takes that page's sides, the membrane fitted again to its own edge (or the very same file when the two
  edges agree within 1.5 levels). Every form shares the sides of the form it matches best, so the sides do not change as you
  go from form to form.
- `pad`: as above. `skip`: no wide version (record 17, a 408x406 picture at the frame's top left, which no screen shows).

**What is not regenerable.** The model calls cannot be repeated byte for byte: the models take no seed and change under the
same name, so asking again gives another picture. The pack's release therefore keeps the masters (the model's own outputs,
with their SHA-256 in the recipe) as its source, and the recipe keeps everything else: per page the class, the model id as
called, the date, the canvas and the prompt exactly as sent, the variant picked and why, the affine that aligns the master
(found once, then pinned), and the seam, upscaler, membrane and fingerprint settings. From the masters and the 4:3 pack the
pack is rebuilt exactly:

```powershell
.venv\Scripts\python tools\menu-wide.py build --pack43 i76-hd-menus-i26-4x3-2026.11.23.zip --pack43 i26-hd-work\nitro\tex-menu --masters MASTERS --out i26-hd-work\wide
.venv\Scripts\python tools\menu-wide.py verify i26-hd-work\wide --pack43 i76-hd-menus-i26-4x3-2026.11.23.zip --pack43 i26-hd-work\nitro\tex-menu
```

(the second `--pack43` is the Nitro pages' folder when they are not inside the first). `verify` composes every wide page as the
game does and checks the 4:3 page pixel-identical in its middle. `menu-wide.py list` prints each page's class and pick.
`menu-wide.py regenerate shellbg5 --pack43 ... --masters MASTERS --variants 3` asks the model again with the recorded prompt
and canvas (your own `GEMINI_API_KEY` in the environment; about $0.14 an image at the time of writing) and writes the new
variants beside the masters; it changes nothing else. Point the page's `master` at the one you like, drop its `affine` and
`master_sha256`, and build. Needs `opencv-python-headless scipy` and, for the upscale, the menu pack's `torch spandrel` and
model. The pack's `pack.json` names the 4:3 pack it was made over, and `ATTRIBUTION.txt` the models (the sides carry Google's
SynthID watermark).

**Install.** Copy `i76-hd-menus-i26-wide-DATE.zip` as it is into the I26 user folder, beside the menu pack (do not extract it):
I26 reads its `tex-menu-wide/` folder in place, or the same folder extracted there. It needs the 4:3 menu pack (the "I26 4:3"
release, with its Nitro pages for the Nitro Pack's own forms); on its own it does nothing. Uninstall: delete the zip.

The older single tools still work on a pack you already have, in place, each keeping the files it replaces
(`.rules-prev/`, `.flat-prev/`, `.text-base/`) and putting them back with `--restore`: `menu-maps.py SRC`, `menu-rules.py
BG_DIR`, `flat-art.py BG_DIR` and `text-redraws.py BG_DIR --menu` (BG_DIR as `i76 --install DIR bg BG_DIR` writes the
records).

## Fixing a lettered texture

Each redrawn texture is a spec in `tools/text-redraws/` (coordinates in the original's texels, the words, a free font),
never art, so every pack made from the source letters the same way. `tools/find-lettered-textures.py` finds the
lettered textures and says which the upscale left soft or wrong; `tools/draft-text-redraws.py` drafts their specs in
batches, and you read the words and approve each one from a contact sheet. Everything it writes goes to a scratch folder
(`BATCH` below): it never touches a pack.

1. **Queue**: `python tools/draft-text-redraws.py queue --survey SURVEY.tsv --classes CLASSES.tsv --out QUEUE.tsv`.
   SURVEY.tsv is `find-lettered-textures.py SRC --out SURVEY.tsv --pack PACK --usage USAGE.tsv`'s table. CLASSES.tsv
   (stem, cls, text) is the review's verdict on each hit: SOFT or WRONG, with the words as the reviewer read them. The
   queue leaves out what a spec already letters (and any `--skip GLOB`). It is ordered by how often a texture is seen
   (missions using it, trip missions first), then by the pack's ink IoU. A car's dent stages and paints are one family.
2. **Gather**: `python tools/draft-text-redraws.py gather QUEUE.tsv SRC --pack PACK_8X --pack PACK_4X --batch BATCH`
   takes the next 30 or so textures: signs first, then some cars and the dash. For each it writes `BATCH/read/<texture>.png`:
   the original enlarged with its letter rows boxed and numbered, the pack beside it, and each row's crop.
   `look SRC STEM --box x0,y0,x1,y1 --pack PACK --out PNG` shows a region on a texel grid, to read small lettering or to
   draw a box the detector missed.
3. **Read the words** into `BATCH/words.json`, one entry per texture:
   `{"sl155x_1": {"rows": {"0": "SPEED", "1": "LIMIT"}}}`.
   A row you cannot read with confidence from the original is `"keep"` (the upscale stays), and so is any row not named.
   Never guess a word. `"add": [{"text": ..., "box": [x0, y0, x1, y1]}]` sets a row the detector missed; the box is
   tightened to the ink in it, or taken as given with `"exact": true`. `"font"` (and `"variation"`) fixes the face for
   the texture or for one row: Anton for every licence plate, for example. A row may also fix `"box"`, `"colour"`,
   `"turn"` and `"embolden"`.
4. **Fit**: `python tools/draft-text-redraws.py fit BATCH` chooses each row's face, weight and box, and uses one face for
   the texture where that costs each row little. The weight is fitted to the painted letters' (their ink, area for
   area), not thickened toward the upscale's. A sign family's approved face is a rule in the tool (`FACE_RULES`: the US
   shields in Archivo Narrow, the diamond warning signs in Arimo), so its later signs match. The ink is solid: the
   original's paint, or the pack's where the pack paints it darker. It is checked to be on the ground's side the
   original's is. It renders the spec with `text-redraws.py` over a copy of each pack's
   file and measures the result on the original's grid. PASS means no gross fault was found: the letters fall on the
   painted ones (blurred correlation 0.75 or more), the ink's box and colour are close, and nothing changed outside the
   rows. Anything else is FLAG, with the reasons. No measure taken against the original can tell a good redraw from the
   upscale: a model's output, filtered back down, is the original near enough. The measures therefore catch mistakes
   and the sheet decides.
5. **Review**: `python tools/draft-text-redraws.py sheet BATCH [--prev EARLIER_BATCH]` writes `BATCH/index.html` and `BATCH/sheet-NN.png`. Each
   card shows the original, the released pack and the redraw, with the words and the measures. Approve, reject or keep
   each one, then save `decisions.json`. For a second round, copy the families to rework into a new batch folder with
   the owner's note and what changed on each (`"owner"` and `"change"` in its `batch.json`); `--prev` then adds the
   earlier round's redraw beside the new one, with that note.
6. **Accept**: `python tools/draft-text-redraws.py accept BATCH decisions.json` copies the approved specs into
   `tools/text-redraws/` (`signs-`, `plates-` or `dash-<texture>.json`). Run `text-redraws.py SRC --pack PACK --only
   STEM` to letter a pack you have with them (or rebuild the texture through its recipe: `hd-pack.py world-4x ... --only
   STEM`), and `python -m unittest tools/test_text_redraws.py` to check them.

Small print nobody can read (a plate's state line, a garage's list of services) stays `"keep"`. A batch of 30 takes
about an hour of reading and a minute or two of fitting. Hand lettering, scripts and decorative wood
type are better kept than redrawn: no shipped face matches them, and the measures cannot tell. A new face follows
`tools/text-redraws/fonts/README.md`.

## The cockpit radar redrawn (step 13 of the recipe)

`tools/radar-redraw.py` draws the radar's 62 frames (`zradf000`-`030`, the sweep; `zradb000`-`030`, locked on a target)
again over a pack's own upscale of them, CPU only. Both world recipes run it last before `finish`, as their `radar` step:

    python tools/radar-redraw.py pack EXPORT PACK        # style B, the lock-range labels: the packs' choice, the defaults

It measures everything from your own export (`i76 export-textures`): the 60 frames' per-pixel median is the clean grid and
panel; the scope's three rings are circles fitted to its grid pixels (one centre), the four spokes lines through them, the
face a circle fitted to the green's edge, the small scope at the corner likewise; each sweep frame's beam is a fitted line
(all 30 meet at one pivot, frame f at 12f degrees plus a measured offset) and each lock cone's two edges are fitted. The
scope is drawn again as anti-aliased vectors at the pack's scale in the original's colours, registered to the original's
pixels, so the blips, the contact lamp and the range plate the game stamps at fixed pixels land where they did. Style B,
the packs', is a phosphor scope: a thin sharp beam (1.5 pixels, a bright core 0.7 wide, no halo) with a fading afterglow
behind it and a soft lock cone, the graticule printed crisp over all three (`grid_over`); style A (`--style A`) is the
original's line and cone without the afterglow barbs, over the graticule. The left panel's marks (the dotted arc, the
ticks, the knob pointers and the two small lettered rows) are erased from the upscale and drawn again as shapes, and the
label set's words are set in B612 Bold (SIL OFL 1.1, in `tools/text-redraws/fonts/B612/` with its licence; the pack's
`pack.json` and `ATTRIBUTION.txt` name it), printed: the ink darkens the panel's own shade to 0.3 of it (`ink_multiply`),
so the panel's shading shows through. The painted words are three pixels high and one pixel high: `fit-labels`
scores candidates against them and against a list of unrelated words, and none stands above chance, so the words are a
choice, not a reading. The game shows the frames with their rows flipped, so the spec's panel positions are in the game's
view and the lettering is set upright there.

The label set is `lock-range`: LOCK VOL between the knobs (the top knob as the lock tone's volume), and round the lower
knob 100, TGT and 600 lettered in place of the painted left tick, X and right tick (the marks are erased, not drawn
again: the words are the positions; the painted pointer rests on 600, the default range), RANGE under it. Every word is
one cap height, 2.4 original pixels (LOCK VOL was 2.6, the three numbers 1.8). 100, TGT and 600 are centred on their
marks, lifted 0.4 pixel out from the knob; the 600 is 0.8 pixel left of its tick so that it ends a pixel short of the
scope's bezel (the bezel's outer edge is at x 41.0 on those rows). The two faint marks left of the knob (a dash and a dot,
of no known meaning) are kept, cleaned. 100 m and 600 m are the game's two radar ranges (R flips them); TGT is a story for
the middle mark, not a state of R (the weapon camera in the radar screen, `RADAR_CAMERA_TOGGLE`, W, is a separate switch and
needs a target and a turret). The spec keeps other sets for comparison (`--labels`: `lock-range-m` with the unit,
`owner-50-500`, `var-range`, `gain-range`, `intensity`, `marks-only`). The knob's painted pointer never moves.

**The glass** (`--glass none|light|medium`, the spec's `glass`; `none` the default, so a pack is unchanged until a level is
chosen). The scope's glass drawn over the beam, the cone, the graticule and the pip, under the game's blips, lamp and plate: the
bezel's shadow (darkening from 0.7 of the face's radius to its rim), a soft sheen inside the rim at the top left as the player
sees it, with a faint glare inside it, and a little second-hand wear: one or two smudges (a soft uneven haze), fine bent
scratches catching the light, dust settled at the rim. All of it is made from the spec's seed in original pixels, so it is the
same at 4x and 8x and the same in every frame (it does not turn with the beam), and medium holds light's parts and adds more.
It is printed into all 62 frames and into the live sweep's grid layer (`zrads_grid.png`, one layer over the face), so the
stepped frames and Radar Sweep: Smooth show the same glass with no change to the game. `pack.json`'s `radar_redraw` names the
level.

The graticule (rings, spokes, the corner scope) is 0.7 original pixels wide with a one-texel anti-aliased edge (crisp:
`line_edge` null; rounds 2 and 3 used a quarter-pixel ramp).

**The edges.** The pack's upscale leaves the unit's wires wobbly and smeared, the scope's bezel ring uneven with a
stair-stepped silhouette at its foot, and the body's bottom corners notched. The step draws these again from the original's
own pixels (the spec's `edges`): each is a curve fitted to the original (the bezel the face's circle; the wires a centreline
through their pixels, ordered end to end, through a smoothing spline, the width their pixels over their length; the
corners a circle fitted to the silhouette's pixel edges), and each texel's colour is the original's colour regressed
along that curve (how far along it and how far across it: wide along, narrow across), so the shapes and shading are the
original's and the edges are round and straight again. The cable and the red and orange twisted pair are tubes of their
measured widths, each colour of the pair where the original has it; their zone (left of and below the body) is transparent
where the original is, the rest of it the original's dark shade. The bezel ring is drawn from inside the face's edge to just
past its dark outer line, further where the original is transparent beyond it (the silhouette); not at the top, where the
original's ring runs into the body's top edge, nor under the corner scope's ring. The corners are drawn as an outline only
(`mode` silhouette: their alpha, and colour only where the upscale was transparent), since a regressed bevel did not meet
the upscale's straight sides. The body's straight edges, its top, the handles, the corner scope's ring and the lamp's
housing stay the upscale's: they are clean there, and drawing them again would be guessing.

The game stamps two small sheets over every frame at the layout's anchors: `zrge` (the range plate over the corner scope:
`range_3` lit at 600 m, the default, `range_1` dark at 100 m) and `ztge` (the contact lamp: `radled_1` red while a car is
listed, `radled_2` green when none is within 600 m). A pack's upscale of those sheets on their own does not continue the
frame's upscale, so the stamp shows as a hard square at 4x and 8x; and the original's plate art (and its green lamp) lies
one pixel higher than the frame paints its own. The step therefore also writes `zrge` and `ztge`: each sprite is the
redrawn frame's texels inside a disc about the frame's own corner scope (or lamp), keyed outside it, the lit faces drawn
there from radial colour profiles measured on the original sprites; every frame carries the same texels inside the discs,
so a stamp changes nothing past its lit face. **The sheets and the frames ship together**: the sheets carry the frames'
texels, so a pack with the new frames and its old sheets (or the other way round) shows the square again. The game's
stamping code is untouched; the 1997 look and a game without a pack draw the originals.

What `pack` writes: the 62 frames and the two sheets in the pack's own format (a `.dds` pack gets BC7 `.dds` with the
full mip chain, the sheets stored bottom up as the export's `bottom-up.txt` says, the frames as they are; a PNG pack
PNGs), each file replaced, not written into (the recipe's stages are hard links). It keeps the frames it started from in
the pack's `.radar-base/` and starts from those when it runs again, so a second run writes the same files (`hd-pack.py
--only zradf007` takes that frame out of `.radar-base/` too, so the rebuilt upscale is the one redrawn). It adds
`radar_redraw` (the tool's and the spec's SHA-256, the style, the label set, the scale) and B612 to `text_fonts` in
`pack.json`, B612's line to `ATTRIBUTION.txt` (in the file's own style), and makes `FILES.sha256` again when the pack has
one. `--out DIR` writes the 67 files (the 64 and the sweep's three) with the amended `pack.json` and `ATTRIBUTION.txt` into DIR and leaves the pack as it
was: the way to redraw a pack you already have (yours, or one unzipped from a release) without building it again. A
pack whose `pack.json` names a radar redraw already and that has no `.radar-base/` (a pack the staged files of an earlier
round were copied into) is refused, since its frames are a redraw; `--base DIR` names a folder holding the upscale's own
frames (a backup made before installing) to start from.

**The live sweep's layers** (Graphic Detail's Radar Sweep: Smooth, docs/playing.md). With style B (`grid_over`) `pack` also
writes three files the game draws the scope from every frame instead of swapping the 30 sweep frames:

| File | What |
|---|---|
| `zrads_face` (the pack's format: `.dds` BC7 with mips, or `.png`) | the whole frame at the frames' size and rows, the radar on, with nothing on the scope's face but its green: no beam, afterglow, cone, graticule or pip. The panel, bezel, wires and the stamps' discs are the frames'. |
| `zrads_grid.png` (always PNG: its one-texel lines and their alpha exact) | the graticule (rings and spokes) and the centre pip over the scope's window only (`grid_box`), straight RGB with alpha their coverage, clipped to the face's circle |
| `zrads.json` | the fitted numbers, below |

`zrads.json` (`format` 1; lengths in original pixels, a texel's centre at `(j + 0.5) / scale`; angles in degrees in the
frames' file axes, `atan2(y, x)` with y down the stored rows, which the game shows flipped): `scale`, `size` (the frames'
texels), `layers` (`face`, `grid`, `off`: the radar-off face is the stepped set's `zradf030`; `grid_box`: x0, y0, x1, y1 of
the scope's window in texels), `face` (`centre`, `radius`, `colour`: the green; the face's edge is the coverage
`clamp(0.5 - (distance - radius) * scale, 0, 1)`), `pivot` (the beam's), `sweep` (`frames` 30, `step_deg` 12, `step_s` 0.2,
`deg_per_s` 60, `offset_deg` the fitted 1.078, clockwise on screen: frame f's beam lies at file angle `90 - 12 f +
offset_deg`, bearing `12 f - offset_deg`; the smooth beam takes f continuous, the frame shown plus the share of its step
gone), `beam` (`width` 1.5, `core_width` 0.7, `edge_colour`, `core_colour`, `length` the face's radius + 1, from the pivot),
`afterglow` (`span_deg` 150 behind the beam, `peak` 0.62, `decay_deg` 32, i.e. `decay_s` 0.533 at 60 degrees a second,
`colour`; faded in over the pivot's first pixels), `cone` (`half_angle` 5.94 and `apex_behind_pivot` 13.4 fitted, `fill`,
`soft` 2.2, `axis_colour`, `axis_alpha`; frame f's cone lies on bearing 12 f exactly). The drawing, in this order: the face;
over its circle the afterglow, then the beam's edge and core (or the cone with its axis); the grid layer over that; then the
game's blips, lamp and range plate. `compose_smooth` in the tool is that drawing in numpy, and the tests hold it to the
stepped frames: at a step's own angle the scope is the stepped frame of that step to within 2 levels off the face's one
anti-aliased rim texel. Style A (the graticule under the beam) writes no layers. Older packs have none, and the game keeps
the stepped frames for them.

**The radar for a pack you already have.** The packs released as 2026.11.23 predate this step. With the export of the
quick route (or `hd-pack.py`'s work folder) and Python with `numpy scipy pillow etcpak`, in PowerShell:

    .venv\Scripts\python tools\radar-redraw.py pack i26-hd-work\export "$env:APPDATA\I26\tex-world-8x" --out radar-8x
    .venv\Scripts\python tools\radar-redraw.py pack i26-hd-work\export "$env:APPDATA\I26\tex-world-4x" --out radar-4x

then, with the game closed, copy each folder's files over the pack's: `Copy-Item -Force radar-8x\* "$env:APPDATA\I26\tex-world-8x\"`
and the same for `radar-4x` (Linux: `cp radar-8x/* ~/.local/share/I26/tex-world-8x/`). Each takes about a minute. Without `--out` the pack is changed
in place (the frames it had kept in `.radar-base/`). For a release archive: unzip it, run `pack` on the unzipped
`tex-world-4x` (or `-8x`) in place, which also makes its `FILES.sha256` again, and zip it with `hd-pack.py release`.
`hd-pack.py verify` against the 2026.11.23 archives lists the 64 radar files as changed by the `radar` step (and the sweep's three as new).

The other commands, for working on the redraw:

    python tools/radar-redraw.py measure EXPORT [geometry.json]
    python tools/radar-redraw.py fit-labels EXPORT [fit.json]
    python tools/radar-redraw.py render EXPORT UPSCALE OUT [--scale 8] [--style A|B] [--labels NAME]   # PNGs, and radar-geometry.json
    python tools/radar-redraw.py sheet EXPORT UPSCALE OUT        # the contact sheet: OUT/index.html
    python -m unittest tools/test_radar_redraw.py

UPSCALE is a folder of a pack's frames (PNG or `.dds`, any whole multiple of 256 wide). For a look in the game without
touching your pack, put `render`'s output (or `pack --out`'s) in a folder's `world/` and start the game with `I76_TEX_DIR`
pointing at that folder: it is read first, over the installed pack.

## The damage panel and counters redrawn (step 14 of the recipe)

`tools/hud-redraw.py` works on five textures of a pack: `zsy_` (the damage panel, "Mr. Damage"), `zsye` (its lights: every
part's five states), `zwpe` (the weapon counters' five housings) and `zbk_`, `zbks_` (the weapons' and specials' rails the
housings hang on). CPU only; both world recipes run it after the radar, as their `hud` step:

    python tools/hud-redraw.py pack EXPORT PACK --distress medium     # the packs' level (the owner's pick)

**The wires.** The red and yellow wires hanging at the damage panel's sides are found in your export by their colours; each
colour's pieces between the connectors are fitted as centrelines (ordered end to end, a smoothing spline: the radar's
wire fit). A piece the other wire cuts is joined to the next one across the cut (ends within 6 pixels, only wire or air
between, never through a connector), so the red runs on unbroken under the yellow: every red run is drawn under every
yellow one (the owner's choice; the original paints the red over the yellow in two small places, about 4 pixels each, at
the right side's lower crossing and at the left side's foot). Each run is drawn as insulated wire of its measured width
(about 2 pixels): a matte cylinder lit from the top left in the original's dark and light shade of its colour, its width
and line wandering a little along it (7 % and a seventh of a pixel), slightly dulled and mottled with age, a soft broad
sheen along its top, grimed where it meets a connector or the panel, with a faint contact shadow under the yellow where it
crosses the red and under both on a connector or the panel (`wires.look` in the spec). The upscale's own wire texels round
them (red or yellow by hue) are taken out first: transparent where the original is, the body's or connector's colour
where it is not. The connectors, the body, its bevel and the screws are the upscale's (they are clean in it).

**The wear** (`tools/panel_distress.py`, `--distress 0|light|medium`, the spec's `distress`): grime gathered in the corners
(where edges across and along meet) and mottled, small chips of paint along the art's strong edges (not on the lettering or
the lights' lenses), a few fine hairline scratches (lighter, a fainter dark edge below; not across the wires or the lights),
and printed lettering a little uneven (a few per cent fainter in patches, a few pin-holes). The words, their places and the
shapes are never moved. Every random number comes from a fixed seed (the spec's 1976 and the unit's name) on a grid of 8
texels a pixel, brought to the pack's scale, so every run and every machine makes the same files, and a 4x and an 8x pack
the same pattern. `0` adds none (only the wires change; the other four files are the pack's own, not encoded again).

**What the game stamps is left as it is, and the wear never flickers.** The damage panel's layer is made once over the
panel as the game first shows it (every part stamped unlit at the layout's anchors, a row up where the game draws it so);
the panel gets it outside the stamps' places, and each light's five cells on the sheet get the part of the same layer that
lies under their place, so a light changing state changes only its lens, and the stamp's edge does not show. The tyres'
four places share one cell, as the two arms' do: those cells carry the first place's layer, and round the others the panel's
layer is blended into it over 4 pixels. Under the stamps the panel's texels are the pack's (the cells are opaque; the
game never shows them). On a housing the diode, the label plate and the four counter windows (where the game draws its
diode, its name plate and the drums) are the pack's texels exactly; one layer lies on all five housings (made over their
median, so the screws' slots, the only difference, do not steer it). The game's stamping code is untouched; the 1997 look
and a game without a pack draw the originals.

**The radar too.** `radar-redraw.py pack ... --distress medium` (or `render`) lays the same wear on the radar's 62 frames,
the smooth sweep's face and the stamps' sheets (one layer, made over the base frame; never on the scope's faces, which carry
the glass instead). The recipes' radar step does (`--glass medium --distress medium`), so the three units match;
`--distress` alone defaults to 0, which writes the files byte for byte as without it.

`pack` keeps the files it started from in the pack's `.hud-base/` (a rerun starts from those and writes the same files;
`hd-pack.py --only zsy_` takes them out with the texture) and names the redraw in `pack.json` (`hud_redraw`: the tool's,
the wear's and the spec's SHA-256, the level, the seed, the scale). `--out DIR` writes the five files and the amended
`pack.json` into DIR and leaves the pack as it is; a pack whose `pack.json` names a hud redraw already and has no
`.hud-base/` is refused (`--base DIR`: the upscale's own files). No font is used. The other commands:

    python tools/hud-redraw.py measure EXPORT [wires.json]          # the wires' pieces
    python tools/hud-redraw.py render EXPORT UPSCALE OUT [--scale 8] [--distress light]   # PNGs, the export's rows
    python tools/hud-redraw.py --self-check
    python -m unittest tools/test_hud_redraw.py

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
