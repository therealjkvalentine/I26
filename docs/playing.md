# Playing I26

Details for players. The [README](../README.md) has the features and a short install guide.

## System requirements

- Windows 10 or later, or a 64-bit Linux (x86_64 or ARM64), with your own copy of Interstate '76 (and the Nitro Pack for its
  content), or macOS (one app for Apple silicon and Intel; signed ad hoc, not notarized by Apple, and so far
  tried on one Apple-silicon Mac with macOS 26, not on an Intel Mac).
- A GPU with a Vulkan 1.2 driver (or, on Windows, Direct3D 12) for the intended look. A machine with neither falls back to
  OpenGL (Godot's Compatibility renderer): the game runs, with no SSAO and some small differences; it says so once in the
  shell and in the log (see [docs/compatibility-renderer.md](compatibility-renderer.md)). If the picture is wrong on your
  machine, Graphic Detail's **Renderer** row switches between Vulkan, Direct3D 12 (Windows) and OpenGL at the next start.

## Installing and starting

1. Download the release zip for your system (`i26-<version>-windows.zip`, `-linux.zip`, `-linux-arm64.zip` or
   `-macos.zip`) from the GitHub releases page.
2. Run `i26.exe` (Windows) or `i26.x86_64` (Linux) from inside the unpacked `i26-<version>` folder, or open `I26.app`
   there (macOS). Nothing needs installing. On macOS, first clear the quarantine flag the browser put on the download, in
   Terminal: `xattr -dr com.apple.quarantine i26-<version>`. Otherwise macOS stops the app (it is not notarized), and an
   app opened while it still has the flag runs from a temporary copy elsewhere (App Translocation), where the `i76` and
   `nitro` folders beside it are not found. Give it the game in one of these ways; if
   it cannot find one it says so and lists the places it looked.
   - **Installed GOG game (including GOG Galaxy):** either copy your game folder into the I26 folder as `i76` (and
     `nitro` for the Nitro Pack), or unpack the I26 folder into your game's folder (only that game is found then).
   - **GOG offline installers:** put `setup_*.exe` next to `i26.exe` (or in the folder above it). I26 unpacks it into
     `i76` / `nitro` itself, once (about 465 MB for Interstate '76, 290 MB for the Nitro Pack; the launchers, most DLLs
     and GOG Galaxy files are left out; the game's own executable is kept, as I26 reads the computer drivers' tactics
     from it). You can also choose **Use a GOG installer...** on the "could not find" screen, or run
     `i26 -- --from-installer setup_interstate76_2.1.0.17.exe [--to DIR]`.
   - **Anywhere else:** a shortcut with `-- --install "C:\path\to\game"`.
   - **Updating I26:** move your `i76` and `nitro` folders into the new version's folder (or keep them in the local user
     folder, which every version checks).

Extraction goes into `i76` / `nitro` inside the I26 folder if that folder is writable, the subfolder is absent, empty or
I26's own earlier extraction, and there is space; otherwise into `i76` / `nitro` in the local user folder
(`~/.local/share/I26/` on Linux, `%LOCALAPPDATA%\I26\` on Windows). I26 never unpacks into a folder that holds
something else. Those folders hold any valid install, so a game folder copied in by hand works the same. Wine is not
needed. The small "GOG Galaxy" downloader `.exe` holds no game data and cannot be used; installers split into
`setup_...-1.bin` parts need the parts next to the `.exe`.

**Which game starts.** At start I26 collects the games it can run: installs (Interstate '76 and the Nitro Pack) and GOG
offline installers (`setup_interstate76_*.exe`, `setup_interstate76_nitro_pack_*.exe`) lying next to the executable, in
its parent folder or in the local user folder (installs: see above); a game counts once, its install preferred over its installer. One game starts
at once (an installer is unpacked first, with a progress bar). Both: a chooser shows the two games' main-menu logos side
by side (mouse, Left/Right and Enter, or the pad's D-pad and A; Esc quits); the last choice is preselected, and a game
that is still an installer is unpacked when you pick it (Cancel returns to the chooser). The main menu's Esc options have
**Switch Game** to come back to it (`--choose-game` on the command line). `--install`, `I76_INSTALL`, `--mission` and
network options skip the chooser.

The install is looked for in this order: `--install DIR`, the `I76_INSTALL` environment variable, the executable's
folder and its parent, `i76` and `nitro` inside the executable's folder, `i76` and `nitro` in the local user folder, then
GOG offline installers (in the executable's folder, its parent and the local user folder). I26 remembers no install
location between runs; an old `[install]` entry in `settings.cfg` is ignored.

**Settings and saves** live in your user folder, never in the game folder: `~/.local/share/I26/` on Linux,
`%APPDATA%\I26\` on Windows. Delete it to start from scratch. The first run copies
the settings and saves of the older "Interstate '76 (Godot)" folder if there is one.

**Bringing in saves.** Load Bookmark is the original's sheet plus a Delete button. Two tools for bringing bookmarks in are
on the Developer panel of the main menu instead (Ctrl+F2 or Ctrl+` there, with `developer=true` under `[port]` in
shell.cfg or `--dev`; Interstate '76 only, the Nitro Pack has no trip):

- **Import Saves** lists the bookmarks the original game saved in its folder (`savegame.dir` and the `saveNNN.cmp`
  files; on Windows also their copy in `%LOCALAPPDATA%\VirtualStore` when the game sits under Program Files), as the
  original's Load board lists them, and Import turns each into an I26 bookmark with the same name and scene. The car
  comes with it: the body, paint, armour and chassis, every part fitted, in the van, in the repair order or left in
  Field Salvage, each in the condition it was saved in. A part is fitted only where the original's garage would show it
  fitted, and weapons past the garage's eleventh go to Field Salvage, as the original does on loading. Importing again
  skips the bookmarks already imported. The original's files are only read.
- **All Scenes** makes a bookmark for every scene of the trip, "Scene N. <the mission's title>", each at the start of
  its scene in that mission's own stock car, so a fresh install can start any scene without a save. It asks first and
  never overwrites a bookmark of the same name.

Both end on Load Bookmark with the new bookmarks listed.

**Update check.** At every start I26 asks GitHub (one request, nothing about you or your machine is sent) whether a newer release exists and, if so, shows "version X available" under the version in the corner of the chooser and the main menu; click it to open the release page. Options > Play Options (second page) > Check for Updates turns it off, and then nothing is requested. A build run from source never asks.

**Log file.** Off by default, so it cannot grow. For a bug report, start the game once with `--log` (or the environment
variable `I76_LOG=1`) and attach `logs/godot.log` from the user folder (the last three runs' logs are kept as
`logs/godot<date>.log`); Graphic Detail's Log (restart) switch keeps it on for every start. Logs from older versions
stay where they are and can be deleted.

**Custom maps.** Install community maps as for the original game: unzip them into the game's own folders. For the
Nitro Pack, the map files (`.cbt` combat, `.rac` race, `.cf2`/`.cf3`/`.cf4` capture the flag, with their `.ter`) go
into `miss16` (and `miss8`), or into `addon`; for Interstate '76, a level's `.lvl` and `.ter` go into `addon`. Car
variants and parts (`.vcf`, `.wdf`) go into `addon`. They appear in the melee and network arena lists under their own
names, Nitro's by game type. In a network game every player needs the same map: a player whose copy is missing or
different is told so when joining. Maps made for the 1997 release (`.lvl` files without a horizon) and maps whose
terrain file is misnamed or short play too, and so do Interstate '76 `.lvl` maps dropped into the Nitro Pack's folders
(as combat arenas). A map left in a folder of its own inside `addon` (as Windows' "Extract All" does) is found too. A
map whose terrain file is missing is still listed, as in the original, but choosing it brings you back to the form
with a note naming the file. Interstate '82 maps (`.msa`)
are for another game and are not listed.

**Resolution.** Graphic Detail's **Resolution** row steps through Desktop (the game's borderless fullscreen at the desktop's size, as it starts) and the common window sizes that fit the screen; a size is a centred window, applied at once with no restart, and is kept for the next start. Alt+Enter still toggles fullscreen. A `--windowed`, `--fullscreen` or `--resolution` on the command line decides that start. Exclusive fullscreen is not offered.

**Show FPS.** Graphic Detail's Display page (the main menu's sheet and the Esc menu's pad) has a **Show FPS** row: On draws a small counter in the window's top right corner, over the menus and the game, `64 fps  15.6 ms`: the frame rate averaged over the last second and the frame time that is, updated twice a second. Off by default, applied at once and kept in shell.cfg as `[port] dev_fps` (the Developer panel's Settings sets the same key).

**Field of view.** Graphic Detail's **Field of View** row (on the Esc menu's pad and the main menu's) sets how wide the main 3D views look. **Monitor**, the default and the recommended choice, keeps the original's height (73.7 degrees) and takes the width from your window's shape: a 4:3 window shows exactly the original's 90 degrees across, a 16:9 window about 106, so a wide screen sees more to the sides than the original did. The numbers are exact degrees across the window whatever its shape, 60 to 110 in steps of 5; **90** is the original's width (the pads mark the values the original game does not have with `*`, so 90 is the one width unmarked), which in a wide window means less height than the original had. A choice applies at once to the outside views and the cockpit's 3D view; the binoculars, the mirror and the radar keep their own framing. In shell.cfg it is `[port] fov` in degrees across the window (absent for Monitor).

**HD textures.** With a texture pack in the user folder's `tex-world-4x/`, `tex-world-3x/` or `tex-world-2x/` (side by side, each with a pack.json whose `scale` says its size; or the older plain `tex-world/`; or an art pack zip's `tex-world/` folder; the README says how to make one from your own install), the modern look draws its upscaled world, car and cockpit textures (the dash, gauges and panels among them); the 1997 look always draws the originals. The switch has no row: `hd_textures` under [Settings without a row](#settings-without-a-row), or the Developer panel's Settings, where a change swaps the textures in place (the cockpit's damage panel keeps its scale until the next mission). `hd_texture_size` picks among the sizes present (from the next mission). The menu pictures' pack has its own key, `hd_menu_art`.

**HD movies.** An upscaled movie in the user folder's `movies/` (beside `tex-world-4x/`; on Windows `%APPDATA%\I26\movies\`) plays in the modern look in place of the install's Smacker file of the same name: `INT01F01.ogv` for `smk\INT01F01.SMK`, the stem in any case, the extension `.ogv`. It must be Ogg Theora video with Vorbis sound (what Godot plays; H.264 or MP4 does not play), any size (shown at its own aspect, black beside it), at any frame rate as long as it lasts as long as the original (the originals are 15 fps; 30 fps from interpolated frames plays as well), and its sound the original's track (22050 Hz mono), starting with the first frame. From upscaled frames and the original's sound (`i76 smk INT01F01.SMK --wav int01f01.wav` writes it):

```
ffmpeg -framerate 30 -i frames/%05d.png -i int01f01.wav -c:v libtheora -q:v 9 -pix_fmt yuv420p -c:a libvorbis -q:a 6 -ar 22050 -ac 1 -shortest INT01F01.ogv
```

(`-framerate` is the frames' own rate: 15 for one upscaled frame per original frame, 30 after interpolating to twice as many.) The 1997 look always plays the originals, and a file that does not open plays the original instead. The same keys skip it.

**Hood finish.** In the modern look the cars' paint, the cockpit's hood included, is clear-coated and mirrors the sky; Graphic Detail's **Hood Finish** row sets how much: Matte (the default) takes the coat and the sheen off, as the original's flat paint, so the cars show the original's colours; Satin\* softens the coat, Gloss\* is the full mirror (live; the 1997 look is not affected). An old `hood_matte=true` reads as Matte.

**Modern Physics.** Play Options' **Modern Physics** drives your car as a rigid body with suspension and tyres (Godot's Jolt engine) instead of the original's vehicle model; AI cars, weapons and damage are unchanged; not offered online. Off is the original's handling. See [docs/modern-physics.md](modern-physics.md).

**Unlim. Ordnance.** Play Options' second page, beside Unlim. Turrets: the original keeps at most 160 live rounds of a kind (40 online for mines, caltrops, Blox and fire drops) and removes the oldest past that; on, there is no cap, so a long oil or fire trail stays whole. Single-player games only: a network game keeps the original's cap for everyone.

**Output and LFE Level.** Audio Control (the main menu's and the Esc menu's) has two rows of the port's own. **LFE Level** (0 to 200 %, 0 the default) turns on a bass-shaker signal of the port's own and sets its level: the engine as a sine by rpm, the road as a rumble by surface (a paved road less than a dirt one) and the car's speed, engine or no engine, stronger with the load on the springs (a hard turn, a stop) and with their movement over bumps, a thump per shot, per hit and per explosion, all 15 to 90 Hz under a limiter; each of these sources has a level of its own in `shell.cfg` (`lfe_engine`, `lfe_road`, `lfe_hits`, `lfe_shots`, `lfe_explosions`, below) and a slider on the Developer panel (100 % is 1.5 times the synth's own level, 200 % three times; the signal has its own limiter after the level, so no setting clips it: a pile of thumps rounds off under it rather than leaning on the master limiter). At 0 nothing is added and every output plays as the original does. **Output** says where the signal goes: Stereo (the default) and Phones (the same: neither Godot nor the game has anything headphone-specific) mix it into both channels and boost the effects' own bass under the crossover (`crossover`, 90 Hz) by the level's share (100 %: twice, +6 dB well under the crossover; the boosted effects share the master limiter with the rest of the mix), for a sub or bass shaker fed from the stereo output, or whatever your rig does with the low end; **5.1**\* and **7.1**\* put the signal on the LFE channel alone, for a shaker on a receiver's or interface's sub output (the two are the same: the LFE of whatever surround layout the device has; with a stereo device nothing plays). The game cannot change the device's channels: the system's sound settings do. Explosions are felt by distance: full within 15 m, falling as 1/r (the air blast) to 120 m and as 1/√r (the ground's wave) beyond, lower and longer, to nothing at 2 km. In 5.1 the LFE also carries the game's own sounds: Godot sends every positional sound there at full level. A file from before these rows with `tactile = lfe` reads as 5.1, and one with Output 2.1 (an earlier name for the stereo mix) as Stereo; a file that chose 2.1, 5.1 or 7.1 without an LFE Level keeps 100 %, the default then. The Developer panel shows the output and the signal's peak.

**The port's values.** On the pads (the main menu's sheets and the Esc menu's), every row that offers the original game's value marks each value the original does not have with `*`; the unmarked value is the original's. Field of View: every value but 90 (Monitor\* included); Force Feedback: Pad Enhanced\* and Wheel\* (the original drove a force-feedback stick, never a wheel); Melee A.I.: Difficulty\*, Easy\*, Hard\*; Hide Map Edge Cliffs: On\*; Hood Finish: Satin\* and Gloss\* (Matte is the original's flat paint); Render Scale: every value but 100 %; 1997 Mode: Off\* (the modern look); Crash Blur: Off\*; Vegetation: Low\* and High\*; Mouse Camera: Slow\*, Normal\*, Fast\*; 3D Garage: On\*; Head Tracking: Analog\* and Digital\*; Rumble Strength: every value but 100 %. A row without a mark (Max FPS, VSync, Renderer, Simulation Rate, ...) has no original's value to tell apart.

### Settings without a row

A choice earns a row on a pad only where a player would plausibly change it. The rest are keys in `shell.cfg` (in your user
folder, see above), section `[port]`, set by hand with a text editor while the game is not running; a key that is absent
has its default, and a key the game writes itself is left as you set it. A value the original game does not have is marked with `*`; the first, unmarked, is the original's.

| Key | Values | Default | What it does |
|---|---|---|---|
| `draw_distance` | `0`, `1`\* | follows the look: `0` in the 1997 look, `1` in the modern one | How far the world is drawn: `0` the original's 600 m with the sky's horizon beyond (the modern look fades the ground into the sky there), `1` to 20 km, so a map's far edges can be seen. View only: the game's own ranges are unchanged. |
| `mirror_range` | `original`, `full`\* | follows the look: `original` in the 1997 look, `full` in the modern one | The rear-view mirror draws nothing past 100 m (the original's), or the whole view. |
| `brake_lamps` | `original`, `held`\* | follows the look: `original` in the 1997 look, `held` in the modern one | The brake lamps light while braking only, as the original's, or hold on 0.15 s after. |
| `mg_tracers` | `original`, `bolts`\* | follows the look: `original` in the 1997 look, `bolts` in the modern one | A machine gun's round in the modern look: the original's one-pixel line, or a glowing bolt. The 1997 look always draws the line. |
| `sky_drift` | `original`, `steady`\* | `steady` | The 1997 look's clouds. The original steps them twice a frame while the cockpit's rear-view mirror is in full mode, a side effect of the mirror pass, so they drift twice as fast with the mirror up; `steady` steps them once a tick whatever the mirror does. |
| `death_orbit` | `original`, `smooth`\* | `original` | The camera's orbit after your car is destroyed and at a network match's end: the original's 8 degrees a tick, or a steady 20 degrees a second. |
| `music_tracks` | `original`, `all`\* | `original` | Mission music plays the disc's tracks 2 to 14 in turn, as the original; `all` goes on into the title and ending tracks (15 to 17) before track 2. |
| `garage_radio` | `off`, `on`\* | `off` | The 3D garage: the menu track goes on, as in the original's CONFIGURE CHASSIS; `on` plays a boombox on the bench instead, a random mission track after another (I76's 2 to 14, the Nitro Pack's but its menu and credits tracks; never the title or ending). |
| `death_variety` | `off`, `on`\* | `on` | The modern look's car deaths: `on` gives each cause of death a look of its own (an engine's fireball out of the hood, a wheel's burst at its corner, a weapon's ammunition cooking off, a killed driver's car catching fire seconds later, ...) and burns 0 to 3 of the wheels a wreck throws, drawn per death, the same on every machine of a network game; `off` draws every death alike, as the original does. The 1997 look keeps the original's death. |
| `briefing_while_loading` | `off`, `on`\* | `on` | The modern look's mission start: `on` plays the briefing while the mission builds (the build on a thread of its own under the movie; the rest of the load, about 3 to 4 s, after it behind the loading card), so a briefing longer than the build starts the mission that much sooner; `off` builds behind the loading card first and plays the briefing after, as the original does. The 1997 look keeps the original's order. |
| `melee_ai` | `0`, `1`\*, `2`\*, `3`\* | `0` | Play Options' Melee A.I. (second page) as a number: the opening skill of an Instant Melee's computer cars, Normal (the original's), the Difficulty Level's, Easy, Hard. Single player only. |
| `effects_size` | `25` to `150` | `100` | The modern look's weapon effects, in percent of their drawn size: the rounds' streaks, the sparks and dust where they strike, muzzle flashes, rocket trails, smoke puffs (damage, wreck, chemical cloud) and the explosion and fireball flipbooks. The 1997 look draws the original's sprites and is not affected. |
| `effects_brightness` | `25` to `150` | `100` | The same effects' glow, in percent: the streaks, sparks, flashes, the fire in the explosions and under the wreck smoke, and the brief lights at muzzles, impacts and blasts. |
| `effects_opacity` | `25` to `150` | `100` | The same effects' opacity, in percent: lower lets more of the scene through the dust, smoke and fire. |
| `vegetation_density` | `10` to `150` | `60` | The modern look's scattered plants (grass, shrubs, cacti), in percent of what Graphic Detail's Vegetation level (Low or High) places: lower thins them to a subset of the same plants. The map's own objects (its trees, bushes and buildings) and the 1997 look are not affected. |
| `brush_impacts` | `off`, `on`\* | `on` | The modern look's scattered plants answer the fighting: rounds into a plant throw leaves and shake it, cars lay the plants they drive through down and they rise again over 15 s, blasts lay them down outward, and fire can set dry brush smouldering or burning for a few seconds. `off`: the plants only sway and part round a car, as before. The 1997 look is not affected. |
| `lighting_calibration` | `off`, `filmic`\*, `on`\* | `on` | The modern look's lighting calibrated by day against the 1997 look's: each mission's exposure and ground colour set so the ground shows the 1997 look's colour and brightness, the cars' and buildings' sky light and colour so they do too, the frame shown through the 3D card's own curve and the sky in its texture's own colours, day and night (`on`); the same calibration under the Filmic tonemap with the modern sky's own colours, as in 0.11 builds after the calibration (`filmic`); or the modern look's own lighting as it was (`off`). The earlier `look_colour_match` key is read when this one is absent. The 1997 look is not affected. |
| `ordnance_limit` | `original`, `unlimited`\* | `original` | Play Options' Unlim. Ordnance (second page). Single player only: a network game keeps the original's cap. |
| `modern_physics` | `false`, `true`\* | `false` | Play Options' Modern Physics. Not offered online. |
| `rumble` | `original`, `enhanced`\* | `original` | Force Feedback's Pad or Pad Enhanced\*; `force_feedback=false` is Off and `ffb_wheel=true` Wheel. |
| `hd_textures` | `false`, `true`\* | `true` | With a texture pack (see HD textures above), the modern look draws the pack's upscaled world, car and cockpit textures, the dash, gauges and panels among them; `false` draws the install's own. The 1997 look always draws the originals. Absent from a file from before the key, it takes `hd_menu_art`'s value. |
| `hd_texture_size` | `4x`, `3x`\*, `2x`\* | `4x` | Which texture pack the modern look draws when the user folder has more than one: `tex-world-4x/`, `tex-world-3x/` or `tex-world-2x/`. The chosen size's folder is read when it exists, else the plain `tex-world/`, else the nearest size present. Only sizes whose folder exists are stepped to, and with none the Developer panel's row is greyed. A change shows from the next mission. |
| `hd_menu_art` | `false`, `true`\* | `true` | With an HD Menu Pack, the modern look's menus draw its upscaled backgrounds, pop-ups and fonts; `false` the original pictures. The 1997 look always draws the originals. |
| `tactile_eq` | `hz:db` points, comma-separated | none | A correction curve for a bass shaker, applied to the shaker signal alone (LFE Level above 0): each point a frequency and a gain in dB (within ±12), interpolated in log frequency between points and held flat past the ends; each of the signal's tones is set by the curve at its own frequency, the road's rumble through a third-octave filter bank that follows the curve. For a rig that feels some frequencies more than others, the inverse of its response: `"20:3.7, 25:0, 35:-3.5, 50:0"` lifts 20 Hz and tames a 35 Hz resonance. |
| `game_eq` | `off`, two `hz:db` points\* | `150:1, 2500:-1`\* | The tone of the game's sound effects (not the music or the radio voices): the first point is a low shelf, its corner and its gain well under it; the second a high shelf, its corner and its gain well over it (half of each at its corner; each within ±12 dB). The effects are 8-bit at 11 kHz and the movies' sound twice that rate: by default a dB more bass and a dB less presence lean the effects toward the movies' balance. `off` plays them as recorded, as the original does. |
| `movie_eq` | `off`, two `hz:db` points\* | `250:0, 6000:-6`\* | The tone of the movies' sound, the same way. Their top octave (6 to 11 kHz), which the game's own sounds do not have, is 6 dB lower well over 6 kHz by default (3 dB at it), so the cutscenes are not brighter than the game around them; `off` plays them as recorded. |
| `crossover` | `40` to `160` (Hz) | `90` | The top of the shaker signal, and with the signal mixed into stereo the corner under which the effects' bass is boosted. Set it to your sub's or shaker amplifier's crossover. |
| `lfe_engine` | `0` to `200` (%) | `100` | The bass-shaker signal's share of the engine's sine (and the starter's shudder), in percent of what the synth makes (0 leaves it out), under the LFE Level and before the signal's limiter. The Developer panel's Settings has a slider for each, applied as you drag. |
| `lfe_road` | `0` to `200` (%) | `100` | The bass-shaker signal's share of the road's rumble, in percent of what the synth makes (0 leaves it out), under the LFE Level and before the signal's limiter. The Developer panel's Settings has a slider for each, applied as you drag. |
| `lfe_hits` | `0` to `200` (%) | `100` | The bass-shaker signal's share of the thumps of hits on your car, rams and crashes among them, in percent of what the synth makes (0 leaves it out), under the LFE Level and before the signal's limiter. The Developer panel's Settings has a slider for each, applied as you drag. |
| `lfe_shots` | `0` to `200` (%) | `100` | The bass-shaker signal's share of the thumps of your own shots, in percent of what the synth makes (0 leaves it out), under the LFE Level and before the signal's limiter. The Developer panel's Settings has a slider for each, applied as you drag. |
| `lfe_explosions` | `0` to `200` (%) | `100` | The bass-shaker signal's share of the explosions' rolls, near and far, in percent of what the synth makes (0 leaves it out), under the LFE Level and before the signal's limiter. The Developer panel's Settings has a slider for each, applied as you drag. |
| `dev_fps` | `false`, `true`\* | `false` | Graphic Detail's Show FPS row (Display page; see Show FPS above): the frame rate and frame time in the window's top right corner (`64 fps  15.6 ms`), the average over the last second, updated twice a second. Also the Developer panel's Settings. |
| `developer` | `false`, `true`\* | `false` | The Developer panel ([docs/dev-hooks.md](dev-hooks.md)): a Developer entry at the bottom of the Esc menu's Options Menu, and Ctrl+F2 or Ctrl+` in a mission, open it; on the main menu the same keys open its Saves tools (Import Saves, All Scenes). The older name `developer_tools` is read too. Single player only. |
| `turret_slew` (in `build-rules.cfg`) | `1998`, `1997` | `1998` | How fast a turret turns toward its target (car turrets, gun emplacements, helicopters; yours and the AI's). `1998`, the build GOG sells (Gold 1.2, i76.exe md5 9a232dcc; the Nitro Pack's the same): 1.7 rad/s (a helicopter's 6.8) times 0.75 + 0.25 of the turret's health, so a half-dead turret turns at 88 %. `1997`, the March 1997 CD release (retail 1.0, md5 6cc509ad): 1.5 rad/s (6.0) times the health itself, 12 % slower whole and half speed at half health. Read in both builds' code. Here, a car's 7.62 turret on a car circling it 25 m off at 25 m/s hits with 77 % / 75 % of its rounds (whole / half health) at `1998`, 75 % / 7 % at `1997`. Single player only. |
| `ai_tactics` (in `build-rules.cfg`) | `1998`, `1997` | `1998` | How an attacking AI car picks its tactic. `1998`: a draw from the tactics table weighted by the car's aggression, a candidate whose conditions fail struck. `1997`: always dirt_gunner (guns from off road), never a road tactic; measured in that build (T14, n = 7), the lead cop circles an idle player 10 to 90 m off, mostly out of sight, and fires 3 to 19 rounds in its first 20 s, then a few a minute, where under the 1998 draw it patrols the road in view and fires steadily. Single player only. |

The original shipped as several builds, and a few rules differ between them in a way a player feels. Those keys live in their own
file, `build-rules.cfg` beside `shell.cfg`, written at the first mission start with each key's facts in comments directly above
it (what each build does and how that is known); the game never rewrites it, only adds the block of a key it lacks. The default is
the 1998 build's, the one GOG sells; `1997` picks the March 1997 CD release's rule. Checked and the same in every build, so no key:
a turret's lead, the AI droppers' gate, ram credit, the radar's sweep, the cockpit needles, the flamer and the physics, the T05 jump
included. The player's damage factor is Play Options' Difficulty Level: the 1997 build has none, which is Champion; the 1998 build
deals x2.0 / x1.0 / x0.75 with your rounds and flame at Wimp / Champion / Badass, Wimp a fresh install's.

The four that follow the look are the original's in the 1997 look and the modern look's own otherwise until the key is in
the file; a key set by hand holds in both looks. Keys that change the simulation (`melee_ai`, `ordnance_limit`,
`modern_physics`, `turret_slew`, `ai_tactics`) and `developer` are for single-player games: a network game runs on the host's rules and keeps the original's; the drawing
keys are each player's own, online too. The hooks for a run (`I76_*`, [docs/dev-hooks.md](dev-hooks.md)) win over the file.

### Controls

Controls follow the original's keys, as set in Options > Control Configuration (which edits keys, the mouse
and a pad).

| Action | Default |
|---|---|
| Drive | W/A/S/D or keypad 8/4/2/6 |
| Shift gears / reverse | `,` and `.` / X |
| Pistol out of the left / right window | hold Pistol Left / Pistol Right (no key by default where `,` and `.` are the gears; bind them in Control Configuration) |
| Engine on or off / e-brake | I / Z |
| Fire / cycle weapon | Space / Enter |
| Hardpoints / specials | 1-5 / 6-8 |
| Pick targets | T, E, Q, Y |
| Radar / turret view in radar | R / F |
| Map / notepad | M / N |
| Glance, swing the chase camera | arrow keys |
| Zoom | PgUp / PgDn |
| Cockpit / chase / front / close / sides / front quarter / overhead | F1 / F2 / F3 / F6 / F7, F8 / F9 / F10 |
| Toggle in-car and last outside view | V (`--cockpit` starts in the car) |
| Look at target | Insert (keypad 0) |
| Rear-view mirror / outside instruments | `` ` `` / `[` |
| Pause / back to the menu | Pause / Ctrl+Q |
| Screenshot (to the user folder) | Ctrl+P |
| Self-destruct | Ctrl+Del or Ctrl+Alt+X |
| Put the car back on its wheels | U (can be turned off by a network host) |
| Standings (network games) | hold Tab |
| CB radio (network games) | hold C |
| Game menu | Esc |
| 1997 look on or off | F12 |

Targets and the .45: Q targets only a car within 15 degrees of the nose inside radar range, and clears the target when none is there; the driver's .45 out of the window is the finisher on a crippled car (hold a side glance, Left or Right, 0.5 s for the arm to come up, then fire; on a car under a third it goes straight to a critical part). The port's Pistol Left and Pistol Right keys do both at once: held in the cockpit, the head turns to that window and the .45 fires as soon as the arm is up (the same 0.5 s), three shots a second, never the hardpoints; let go, the head comes back. Their default keys are `,` and `.` where your map leaves those free. To move the outside instruments' `[` and the ghost cockpit's `]` (to put other commands there), `gamekeys.cfg` in the user folder takes `[keys]` `93="-"` and `12="="` (any key, as Control Configuration writes the game keys there).

A printable sheet of your own bindings: `dotnet run --project tools/I76.Cli -- keysheet [input.map] [out.html]` (without a file, the input.map
the game saved, else the install's; open the page in a browser and print it).

Shift or Ctrl held cancels a command, as in the original. Port keys: C poetry (in network games, the CB radio's push-to-talk), Ctrl+F3 debug text, Ctrl+F7
Modern Physics, Ctrl+F5 debug fly camera (right mouse looks, WASD/QE fly, Shift for speed), Ctrl+F9 wins the trip
mission on the spot (debug), Ctrl+F2 or Ctrl+` the Developer panel when it is turned on (`developer=true` under `[port]` in shell.cfg; docs/dev-hooks.md).

**Cheats.** The original's words, typed while holding Ctrl+Shift (single player): `getdown` stops the mission script
and sets everyone on you; get killed (or Ctrl+Del) and the mission counts as won 5 s later, the original's level
skip. `flflat`, `frflat`, `blflat` and `brflat` blow one of your tyres. `wiggleburger` turns a double-vision wobble
on or off. `freelance` and `thirdnostril` do nothing, in the original too. None of them marks you as a cheater.

**Game pads.** A pad is the original's joystick 1: the left stick steers, the right trigger accelerates and the
left brakes, the right stick swings the camera (and is a head look in the car), A fires, RB is the handbrake, B cycles, X and Y
target, the D-pad links, reverses and changes target. LB is a shift: held, A fires the rear hardpoint (3), B the dropper (4), X and Y
hardpoints 1 and 2, L3 and R3 specials 1 and 2, and the D-pad is binoculars (up), radar range (down) and the gears (left down, right up). Start opens the game menu and, in a network game, holding Back (View) shows the standings; bind Start or Back to a
command on the Controller screen and they do only that command instead. The pad also drives every menu
(D-pad or left stick to move, A to click, B and Start to go back); A, B or Start skips a movie, and B or Start a
skippable cut-scene, as Esc does. Everything is rebindable on the Controller
screen (Control Configuration > Calibrate).

**Head tracking.** [opentrack](https://github.com/opentrack/opentrack) (a webcam, a phone, a TrackIR-style clip) can turn
your head in the car. In opentrack set Output to **UDP over network**, its settings to **127.0.0.1** port **4242**, and press
Start; then set Play Options > More > Head Tracking to **Analog** (the view follows your head) or **Digital** (turning past a
threshold glances left, right or down at the dash, as the glance keys do). Head Track Sens. multiplies the angle (3: a 25° head
turn looks 75° round; the reach is the pad's free look, 80° each side). The glance keys and the pad's right stick win while held;
when opentrack stops the view eases back ahead. If the view turns the wrong way, invert that axis in opentrack's Options > Output.
Analog needs the 3D dash (F1). shell.cfg `[port]`: `head_track` (off / analog / digital), `head_track_sensitivity`,
`head_track_port` (4242), `head_track_bind` (127.0.0.1; 0.0.0.0 to take a tracker on another machine) and
`head_track_position` (true moves the eye with your head, up to 10 cm; off).

**Rumble.** A pad's rumble follows the game's own force-feedback effects: Play Options' Force Feedback row steps Off, Pad (those
effects only, the original's and the default), Pad Enhanced\* (and short pulses for a nitrous kick, the handbrake, gear changes,
the starter, each kind of weapon's shot and a light engine growl) and Wheel (Windows). On Play Options' second page Rumble
Strength sets 25 to 150 % on every level sent to the pad. Both apply when a mission starts, and from the Esc menu at once.

**Joysticks.** A flight stick, wheel or any other USB device that is not a game pad is found too (it is joystick 1 when
there is no game pad). Out of the box the stick steers sideways and accelerates (forward) or brakes (back), the trigger
fires, buttons 2 to 10 cycle, target, brake, link, map, horn, view and reverse, and the hat looks. Calibrate opens the
Joystick Configuration screen: move an axis or press a button and its row appears and lights, click it to choose what
it does; any axis can be the throttle, the steering or the camera, Invert turns it round, and the dead zone is set there
(a throttle lever or twist is just another axis). See [docs/joystick.md](joystick.md).

Without `--mission` the game plays the intro and opening credits (Esc, Space, Enter or a click skips), then opens
the original front end; `--skip-movies` leaves them out. Useful options (after `--` when run through Godot):
`--install DIR`, `--mission a01`, `--drive [car.vcf]`, `--melee`, `--1997`, `--cockpit`, `--windowed` (Godot's, before the `--`).
For unattended runs `--skip-cutscenes` skips a mission's skippable camera scenes as Esc would, and `--mute` turns the sound off.

**Another language's voices.** The port ships no game files, but it can read yours: copy the voice files of a dub you own (the 1997
German one from your own disc, say) into a folder `voices/de` in the user folder (`%LOCALAPPDATA%/I26` on Windows) and start with
`--voices de`, or give the folder itself (`--voices D:/mydub`). Those sound files play in place of the install's files of the same
name, and the mission scripts wait for their lengths. Without `--voices` nothing changes; `--voices en` is the same.

**Your own music.** Songs you own (.mp3 or .ogg) in a folder `music-extra` in the user folder (`%LOCALAPPDATA%/I26` on Windows),
or in the folder `--music-extra FOLDER` names, are mixed into the mission music: one song start in three plays one of them, in
shuffled order, in place of the disc track, and the disc's tracks go on after it. `I76_MUSIC_EXTRA_EVERY=N` (1 to 20) changes the
three; `--music-extra off` turns it off for a run. The menus' music is not touched. Nothing is shipped with the port.

### Steam Deck

I26 runs on a Steam Deck as the Linux build. What is written here comes from the notes of a Deck test of the same games and
the checklist for I26's own; whatever has not been run on a Deck with I26 yet says **to be confirmed**.

1. **Install.** Unpack `i26-<version>-linux.zip` in Desktop Mode (for example into `~/i26`) and give it the game as under
   *Installing and starting* above (a GOG installer next to `i26.x86_64`, or a game folder copied in). In Steam choose
   *Add a Non-Steam Game* and pick `i26.x86_64`; a game folder somewhere else goes into the launch options as
   `-- --install /path/to/Interstate76`. `tools/deck/add-to-steam.py` does the same from a terminal (with Steam shut down), makes a plain library tile
   and offers the controller layout below; see `tools/deck/README.txt`. No Proton: this is the native build (the Windows build under Proton
   also starts from a non-Steam entry with Proton forced, but is not what is described here).
2. **Pad.** In Game Mode Steam Input presents the Deck's controls as an Xbox-style pad (the test notes saw it as a virtual
   Xbox 360 pad). I26's default pad map should apply as written under *Game pads* above (left stick steers, triggers accelerate and brake, A and RB fire, B cycles, ...) and the
   Controller screen should rebind it; both are to be confirmed on a Deck. The layout in `tools/deck/` keeps the pad as an
   Xbox pad and adds hardpoints 1 to 5 on the left trackpad as a radial menu, the right trackpad as the menu pointer (press to click),
   and on the back grips L4 the emergency brake and R4, L5 and R5 Use Special 1, 2 and 3.
3. **Picture.** The Deck's panel is 1280x800. The game draws 16:9 (the project's 1280x720 base) and the front end keeps the
   original's 4:3 frame, so some bars are expected; to be confirmed on a Deck.
4. **Rumble.** The Deck's own controls are wired, so the rate limit that keeps a Bluetooth pad's rumble from stalling the game has little to do there. Whether Steam turns
   I26's rumble into the Deck's haptics is to be confirmed; Steam's rumble intensity is set in the game's controller settings.
   Do not switch Steam Input off for the entry, or the pad is not presented to the game.
5. **Text entry** (a bookmark's name, the multiplayer name): Steam's on-screen keyboard comes up with Steam + X. I26 does not open it by itself (to be confirmed).
6. **Docked to a TV.** In Game Mode Steam gives a game 1280x800 when docked unless the entry's *Properties > General > Game Resolution* is
   set to Native or to 1920x1080; with the default the picture is scaled onto a 16:9 TV with bars (observed with the original
   game under Steam's compositor; to be confirmed with I26). A TV that looks too dark: Graphic Detail's **Monitor Brightness** row. If the picture is
   wrong or the game does not start, Graphic Detail's **Renderer** row (Vulkan, OpenGL) takes effect at the next start;
   Vulkan is expected on the Deck (to be confirmed).
