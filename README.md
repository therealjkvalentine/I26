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

The modern look draws an upscaled texture in place of the install's when one is found in the user folder's `tex-world/`
(`%APPDATA%I26	ex-world` on Windows, `~/.local/share/I26/tex-world` on Linux, `~/Library/Application Support/I26/tex-world`
on macOS), named after the texture (`pp11ftf1.png`) at 2x, 4x or 8x. None ships with I26, and no game art is in this
repository: `tools/upscale-textures.py` makes them on your own machine from your own install. Export the textures
(`i26 -- --install DIR --export-textures SRC_DIR`, which quits when done), install Python 3.10+ with
`pip install torch spandrel numpy pillow etcpak`, and run `python3 tools/upscale-textures.py SRC_DIR`. It downloads the model
4xTextures_GTAV_rgt-s_dither (Philip Hofmann, CC-BY-4.0) once, takes about half a second a texture on a graphics card
and several seconds on a CPU, resumes if stopped, and writes an `ATTRIBUTION.txt` beside the pictures. World and car
textures come out at twice the original's size and the cockpit's art at four times, as compressed `.dds` files the game
loads about as fast as its own; `--from OLD_PACK` repacks a pack made by an earlier version without running the model again. The 1997 look
keeps the originals; Graphic Detail's HD Textures row (shown when a pack is found) switches the upscales off and on.

# Source code
I26 is free software under the [GNU GPL v3](LICENSE). To build it, run the tests or contribute, see [DEVELOPMENT.md](DEVELOPMENT.md); [docs/playing.md](docs/playing.md) has the details for players (how the game finds your install, settings and saves, controls, custom maps, system requirements and status).

I26 contains no game data. It is an unofficial fan project and it needs your own legal copy of Interstate 76.

# Credits
Thanks to the Interstate 76 community for testing, ideas, and supporting this project.