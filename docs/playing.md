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

**Update check.** At every start I26 asks GitHub (one request, nothing about you or your machine is sent) whether a newer release exists and, if so, shows "version X available" under the version in the corner of the chooser and the main menu; click it to open the release page. Options > Play Options (second page) > Check for Updates turns it off, and then nothing is requested. A build run from source never asks.

**Saves of the original game.** Load Bookmark has an Import Saves button: it lists the bookmarks the original game
saved in its folder (`savegame.dir` and the `saveNNN.cmp` files; on Windows also their copy in
`%LOCALAPPDATA%\VirtualStore` when the game sits under Program Files), as the original's Load board lists them, and
Import turns each into an I26 bookmark with the same name and scene. The car comes with it: the body, paint, armour
and chassis, every part fitted, in the van, in the repair order or left in Field Salvage, each in the condition it
was saved in. A part is fitted only where the original's garage would show it fitted, and weapons past the
garage's eleventh go to Field Salvage, as the original does on loading. Importing again skips the bookmarks
already imported. The original's files are only read.

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
**Field of view.** Graphic Detail's **Field of View** row (Original, 80, 100, 110) sets how wide the main 3D views look, in degrees across a 4:3 view (the original's is 90, and a wider window shows more to the sides at any choice); it applies at once to the outside views and the cockpit's 3D view. The binoculars, the mirror and the radar keep their own framing.
**Hood finish.** In the modern look the cockpit's hood is clear-coated paint and mirrors the sky; Graphic Detail's **Hood Finish** row, Matte, takes the clear coat and the sheen off it (live; the 1997 look is not affected).

### Controls

Controls follow the original's keys, as set in Options > Control Configuration (which edits keys, the mouse
and a pad).

| Action | Default |
|---|---|
| Drive | W/A/S/D or keypad 8/4/2/6 |
| Shift gears / reverse | `,` and `.` / X |
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

A printable sheet of your own bindings: `dotnet run --project tools/I76.Cli -- keysheet [input.map] [out.html]` (without a file, the input.map
the game saved, else the install's; open the page in a browser and print it).

Shift or Ctrl held cancels a command, as in the original. Port keys: C poetry (in network games, the CB radio's push-to-talk), Ctrl+F3 debug text, Ctrl+F7
Modern Physics, Ctrl+F5 debug fly camera (right mouse looks, WASD/QE fly, Shift for speed), Ctrl+F9 wins the trip
mission on the spot (debug), Ctrl+F2 the Developer panel when it is turned on (docs/dev-hooks.md).

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
**Rumble.** A pad's rumble follows the game's own force-feedback effects (Play Options' Force Feedback row). On Play Options' second
page the Rumble row steps Original (those effects only, the default), Enhanced (and short pulses for a nitrous kick, the handbrake,
gear changes, the starter, each kind of weapon's shot and a light engine growl) and Off; Rumble Strength sets 25 to 150 % on
every level. Both apply when a mission starts.

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
