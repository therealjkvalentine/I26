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

The quick route runs one model over everything. The packs shared in our testing (release 2026.11.23) took more steps, each
written down below in the order they were really run. Nothing in them was painted by hand: every step is a command or a
small spec in this repository, and the models are pinned by their SHA-256 in `tools/hd-recipes/models.json` (the tools
refuse a model file of the same name with another hash). What was manual is which textures each step covers: those choices
are the name lists in `tools/hd-recipes/lists/`.

**In one command.** `tools/hd-pack.py` runs the steps below from their recipes, `tools/hd-recipes/world-4x.json` and
`world-8x.json` (the menu pack's `menu.json` has its own runner, which `hd-pack.py menu` calls):

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

- `pip install scipy` (`--cells`, `--paint-masks`, `--paint-cut`, `menu-rules.py`), and `potracer resvg-py` for `flat-art.py`.
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
   (the specs in `tools/text-redraws/`), and the three fixes that are specs of the same tool: the pumps' counters
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

**The 8x pack of the cockpit and the cars** (`tex-world-8x/` beside the 4x; Graphic Detail's Car & Dash Textures row then
offers 8x; 2.9 GB, 2,579 textures, about 0.5 GB more video memory, no difference in frame time). It needs the 8x masters of
step 2 (`MASTERS` below). Then:

    python tools/upscale-textures.py SRC --families --models-dir M --from MASTERS --x2-model 2xHDCube_Compact.pth --masters-out FAMILY_MASTERS --only "z*" --skip "zmap*" --max-size 640 --scale 8
    python tools/upscale-textures.py SRC --from MASTERS --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --only @tools/hd-8x-cars.txt,zdue --covers "cockpit & cars"
    python tools/upscale-textures.py SRC --families --family-masters FAMILY_MASTERS --from MASTERS --from-models 4xTextures_GTAV_rgt-s_dither.pth,2xHDCube_Compact.pth --scale 8 --max-size 640 --only "z*" --skip "zmap*" --covers "cockpit & cars"

then the 4x pack's steps 8 to 11 with `--scale 8` (and `key-edge FIELDS 8`): the damage panel's cells (with `--x2-model
2xHDCube_Compact.pth`), the frames, the lettering (61 of the specs have a texture in the 8x) and the cars' painted edges.
The first is the graphics card part (about 15 minutes on a GTX 1080 Ti), the second and third the packing on the CPU
(about half an hour on 8 cores). `--out` defaults to the user folder's `tex-world-8x/`; the mission maps (`zmap*`) stay at
4x. The car list `tools/hd-8x-cars.txt` comes from `i76 car-table survey`, with the 23 lit brake lamp textures
(`*bkx1`, `cp11bko1`) the survey does not reach added by name.

**The HD menu art** (`tex-menu/`: the menu pages, forms and pop-ups) is a separate pack, and **these tools do not make
its base**: the upscaled pages (`shellbg<N>.webp` with `shellbg.json`) were made outside this repository. Given such a
pack, these refine it (each needs the menu records as `i76 --install DIR bg BG_DIR` writes them):

- `python tools/menu-maps.py SRC` (the export): the mission maps (M) at 4x into `tex-menu/maps/` (GPU, a few seconds a
  map), their pen writing cleaned (`--writing plain` or `ink` for the alternatives).
- `python tools/menu-rules.py BG_DIR`: the forms' ruled lines without the model's pale band (CPU, seconds).
- `python tools/flat-art.py BG_DIR`: the title logo drawn again as vector art (CPU).
- `python tools/text-redraws.py BG_DIR --menu`: the pages' printed lettering set again (Arial Bold, the system's own copy).

Each keeps the files it replaces (`.rules-prev/`, `.flat-prev/`, `.text-base/`) and puts them back with `--restore`.

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
