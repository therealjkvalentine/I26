<img width="550" height="36" alt="SOMEWHERE IN THE SOUTHWEST..." src="https://github.com/user-attachments/assets/fe88d0e6-128e-4a78-b573-138f98486eab" />

&nbsp;

This is a reimplementation of the classic game Interstate 76 in Godot engine.

[![I26 - Interstate '76 remastered (video)](https://img.youtube.com/vi/ph6Zrzbycx0/maxresdefault.jpg)](https://www.youtube.com/watch?v=ph6Zrzbycx0)

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/G4Q128JK2B)

# Features

* Modernized graphics
* 1997 mode - toggle the original graphics any time with F12
* Single player campaign, scenarios and auto melee
* Multiplayer with modern netcode
* Online coop mode for campaign and scenarios
* Easy to use 3D map editor
* 3D garage with respraying

## Optional
* Auto-reverse
* Mission checkpoints
* Botanically accurate vegetation
* Support for upscaled textures
* CB radio in multiplayer
* Car interiors

# Installation
Install the [GoG](https://www.gog.com/en/game/interstate76) version (and Nitro pack) from GoG Galaxy, and the game should find them automatically. If not, copy the Interstate 76 directory into the `i26` directory named `i76`, and/or the Nitro pack into the directory named `nitro`.

Alternatively, place the `setup_interstate_76_*.exe` offline backup installers into the `i26` directory and they will be unpacked automatically. If you're logged in to [gog.com](gog.com), the download links are: [Interstate 76](https://www.gog.com/downloads/interstate_76/en1installer1), [Nitro Pack](https://www.gog.com/downloads/interstate_76_nitro_pack/en1installer1)

On Linux, ensure Microsoft core fonts are installed for Impact and Arial.

# HD textures (optional)

The modern look draws an upscaled texture in place of the install's when a pack is in the user folder's `tex-world-4x/`
(`%APPDATA%\I26\tex-world-4x` on Windows, `~/.local/share/I26/tex-world-4x` on Linux, `~/Library/Application Support/I26/tex-world-4x`
on macOS), one file a texture named after it (`pp11ftf1.dds`). None ships with I26 and no game art is in this repository: you
make the pack on your own machine from your own install. **[docs/hd-textures.md](docs/hd-textures.md) says how, step by step.**
The quick route needs this fork's build (a `play-NN` zip from the [releases page](https://github.com/therealjkvalentine/I26/releases):
upstream's builds cannot export textures yet), Python 3.10+, this repository, and under two hours with an NVIDIA graphics card
(most of a day on a CPU). On Windows, in PowerShell in the repository's folder:

    py -m venv .venv
    .venv\Scripts\python -m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu126
    .venv\Scripts\python -m pip install spandrel numpy pillow etcpak
    .venv\Scripts\python tools\make-hd-pack.py --game PATH\TO\i26-play-NN --install "PATH\TO\Interstate 76"

(On Linux and macOS: `python3 -m venv .venv`, `.venv/bin/pip install torch spandrel numpy pillow etcpak`, then
`.venv/bin/python tools/make-hd-pack.py --game ... --install ...`.) It exports the textures through the game
(`--export-textures`), runs `tools/upscale-textures.py` over them (the model 4xTextures_GTAV_rgt-s_dither by Philip Hofmann,
CC-BY-4.0, downloaded once; colour matched back to the original; BC7 `.dds` with mipmaps; resumes if stopped) and then
`tools/text-redraws.py` (the smallest painted lettering set again from free SIL OFL fonts), printing each command as it goes.
The doc also gives the full recipe of the packs shared in testing: the cockpit by family, the alpha step, seams, the 8x pack of
the cockpit and the cars (`tex-world-8x/`, 2.9 GB, no frame-time cost) and the menu art's tools, with every model's source and
licence (two are non-commercial: a pack made with them is for your own use).

Graphic Detail's Advanced page then has **World Textures** (1997 or 4x) and **Car & Dash Textures** (1997, 4x, or 8x with an 8x
pack) rows ([docs/playing.md](docs/playing.md), HD textures); 1997 on both (shell.cfg's `[port] world_textures=1997` and
`car_textures=1997`), or the 1997 look (F12), draws the originals.

# Source code
I26 is free software under the [GNU GPL v3](LICENSE). To build it, run the tests or contribute, see [DEVELOPMENT.md](DEVELOPMENT.md); [docs/playing.md](docs/playing.md) has the details for players (how the game finds your install, settings and saves, controls, custom maps, system requirements and status).
This is the play fork: [docs/fork-deltas.md](docs/fork-deltas.md) lists what it changes against upstream, by area.

I26 contains no game data. It is an unofficial fan project and it needs your own legal copy of Interstate 76.

# Credits
Thanks to the Interstate 76 community for testing, ideas, and supporting this project.