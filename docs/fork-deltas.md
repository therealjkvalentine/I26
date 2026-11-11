# What this fork changes

This is the play fork of I26: a patch stack on upstream's `master`, one topic branch per change, merged by hand onto `main`
(the integration branch, public on this fork) in the order `tools/play-branches.txt` lists them (sections A to BF, 288
branches), with upstream's master last merged at 8edea2a7. This page is the stack by area as of 2026-11-08, `main` at
08a86979; the HD packs the modern look can draw (the menu art, the 4x world pack, the 8x pack of the cars and cockpit) are
made by the tools here from your own install and are separate downloads, not in the repository (public links to come).

Each line is one change in a player's words, the option that controls it where there is one (a Graphic Detail or Play
Options row, a `[port]` key in shell.cfg, or an `I76_*` hook from docs/dev-hooks.md), and the manifest section(s) in
brackets, where the branch names are. A fidelity fix (I26 made to match the original) has no option: it is the default.
Every deliberate difference has a row in docs/deviations.md. Network games use protocol 28 and save snapshot 25: this
build does not play with upstream's builds or 0.11.0.

Each section's before/after pictures are on the changes page, built by `tools/changes-page/build.py` and published from
the `gh-pages` branch (see tools/changes-page/README.md).

## Looks and lighting

- The modern look's lighting is calibrated per mission against the 1997 look's: the ground shows the original's colour and brightness, the frame goes through the display's own curve instead of Filmic, the cars keep the original's paint colours (`[port] lighting_calibration`, on; `off` or `filmic`). [I, K]
- Night missions are dark as the original's: the ground by moonlight alone, the road a darker band under it, the sky at a quarter, so the headlamps matter (`I76_NIGHT_MOON/GROUND/ROAD/SKY` to compare). The sky's clouds are read through a smooth spline, no 80 px squares. [AP, BB]
- Headlamps, brake and tail lamps weaker at night and well down by day; Palette Grade moves colours smoothly, no dither; paved roads carry the asphalt grain near the camera. [K]
- Keyed textures (signs, fences, plants, the cockpit's mirror and compass) are cut along smooth outlines instead of a pixel staircase; road signs by the game's own outline, back-culled. [K, AR]
- The smoke layer takes the nearest surface under each pixel, so no fringe over a car or hill in front of it, with MSAA too (#95). [I]
- Rounds striking the ground a third fainter; `[port] effects_size`, `effects_brightness`, `effects_opacity` scale the weapons' effects. [I, AY]
- Scattered grass and brush thinned to 60 % (`[port] vegetation_density`); the brush answers rounds, cars, blasts and fire (`[port] brush_impacts`). [I, L]
- Scenery wrecks burn and smoke out to 600 m as in the 1997 look; the camera round a wrecked car turns smoothly on a fast display. [I]
- Modern machine-gun rounds are short bright bolts, not a ten-metre beam; the target health bar at the original's size and place. [C, D]
- 1997 look brought to the patch kit's picture: Anti-Aliasing applies (`[port] aa_1997`), Early Tick answers the keys within a tick (`early_tick`), Visibility Range 2.3 km, muzzle flashes keep time with the gun (`even_flash`), tracers one pixel per 480 rows, effect sprites upright (#105), roads without the depth fade and sharp into the distance. [A, C, I, W]
- The draw distance follows the look with no row (`[port] draw_distance`); the sky drifts one step a tick by default (`sky_drift=original` for the original's double step). [B, D, H, I]

## Menus and shell

- Graphic Detail is the original's rows in the original's order plus an Advanced page (1997 Mode, 1997 Palette Grade, World Textures 1997/4x, Car & Dash Textures 1997/4x/8x); every row on the shell, the Esc pads, the host forms and the Developer panel has a tooltip after half a second and the costly rows a help line. [H, I, K, P, S, AK]
- New rows: Resolution (#81), Render Scale 50 to 200 %, Field of View (Monitor the default, 90 the original's), Hood Gloss 0 to 100, Ambient Occlusion (off), Cabins Behind Glass, Show FPS; Options > Controls holds force feedback, rumble, wheel strength, mouse and the key map. [D, I, N, O, S, AK]
- Graphic Detail's last line shows the frame rate: measured in a mission, estimated on the main menu for the rows as set (`[port] menu_fps`). [AX]
- On every row that offers the original's value, each value the original does not have is marked `*`; choices a player would rarely change are `[port]` keys with the right default, listed in docs/playing.md "Settings without a row". [I]
- HD menu art (a separate pack): the menu pages, forms and part sprites at 4x, the M screen's maps upscaled, the forms' ruled lines cleaned, printed lettering retyped in Arial, the title logo redrawn as flat vector art from the original; Godot's own text (the Developer panel, the loading line, the build line) in Arial too. [L, AA, AL, AU, AZ, BC, BD]
- Bookmarks: Delete on Load Bookmark and Save Bookmark with a yes/no; Cancel goes back to the screen it came from. [C, D, I, Q]
- Story missions listed as "Scene N: name"; the melee lists leave out co-op packs' copies of the story missions. [I]
- The 3D garage keeps the menu track, as CONFIGURE CHASSIS does (`[port] garage_radio=on` for the boombox); melee and network games never play the menu's track. [I]
- A mission with a briefing loads as upstream's 0.12 shows it (the card a moment, the briefing while the world builds, PLEASE STAND BY after a skip or its end, the mission by itself when ready), but the rest of the load waits for the briefing's picture to stand still (no stutter) and PLEASE STAND BY shows faint over the playing briefing while the world builds (`[port] briefing_while_loading`: `true`, `false`). [N, Q, V, DG]
- Mouse In Window holds in the menus too (#147); the first visit to the 3D garage opens as fast as the later ones (#143); the first explosion no longer stutters (#116). [A]
- `--skip-cutscenes` and `--mute` for unattended runs; a voice overlay folder (`--voices LANG`) and a `music-extra` folder for your own songs. [D]

## Cockpit and dash

- An 8x pack of the cars and the cockpit over the 4x: the loader serves each texture from the largest size that has it; a first start picks 8x where the card has 6 GB (`[port] hd_dash_size`, `hd_car_size`, 4 or 8; the Car & Dash Textures row). [Y, AF, AJ]
- Each dash panel and the radar is its own texture and uploads only the rectangle that changed, so firing no longer pulls the frame rate down (`I76_DASH_STATS=1`). [AN, AW]
- The dash holds still on screen (it shook up to a pixel a frame far from the map's origin), and the head sway eases into its target (`[port] sway_snap=true` restores the original's snap). [AO]
- The steering wheel and the driver's arms are drawn filtered from the pack's frames and the frames recut along the key (`I76_FRAMES_POINT=1` for the old read). [BA]
- The cockpit's art is read through mip levels with anisotropic filtering, so the dash's lettering, needles and edges no longer crawl as it sways; the 1997 look is untouched. [BF]
- The weapon and special labels are embossed label-tape lettering, the ammunition counters Roboto Bold digits on shaded drums that fade at the window's edges (`I76_COUNTER_DRUM`, `I76_COUNTER_FEATHER`). [I, K, N, Q, AA, AF, AH]
- The 3D gauges look like the original's: black dials, sharp lettering, needles turned off-centre; the dash smooth when looked down at. [A, C]
- Under an HD pack the mirror frame, radar marks, panels, compass and the dream's grey are drawn right; the weapons panel, wheel and arms cut by their own pixels; the damage panel's sprite cells upscaled alone. [I, K, M, AQ]
- The 2D dash's mirror at the original's place; its gunsight on the gun line at every Field of View; `]` toggles the original's ghost cockpit. [C, D, H]

## Cars and world art

- `tools/upscale-textures.py` makes the HD packs from your own export: colour matched back to the original in Oklab, BC7 `.dds` with mipmaps, a model per cockpit family with an alpha step for keyed art, neighbour-aware upscales for seams (`i76 tex-seams`), sprite cells alone, cut-outs recut by the game's outline (`i76 key-edge`), `--scale 8`; `tools/text-redraws.py` sets the smallest painted lettering again from free fonts; `tools/menu-maps.py`, `menu-rules.py` and `flat-art.py` make the menu pack. None of it ships. [D, I, L, R, T, Y, Z, AA, AQ, AR, AZ, BD]
- The modern look draws a pack from the user folder at any whole multiple, swapped at once by the HD Textures rows; the 1997 look keeps the originals (`[port] world_textures`, `car_textures`). [D, I, M, Y, AK]
- Under a pack car windows stay glass and the cars keep their finish; a mission with a pack loads nearly as fast as without one. [I]
- Road signs and gas pumps redone in the 4x pack; the rolling wheels keep their painted light still instead of carrying a shadow round. [AR, AV]
- Hood Gloss covers the cars' paint as well as the hood, 5 the default (0 the original's flat paint). [D, I, O, AK]
- Fences and tyre walls stand on their line, centred between posts (#137); the garage's (C), (V), (R) legends bold. [A, C]

## Simulation fidelity fixes

- A placed car's wheels start from the ground: no drop and bob at a start, respawn or join (#141); six-wheelers ride their middle wheels (#124). [A]
- Cut-scene cuts show no in-between frame (#140); any camera call clears the arrived flag; mission music goes back to track 2 after 14 (#128); cockpit smoke no longer flickers over the sky. [A]
- Measured in the retail game: Taurus's car takes load-based cornering; AI cars turn at the original's 12.5 m/s; an AI no longer reverses behind a scripted lead car; script timers round to the nearest second; a salvo's rockets leave at one speed; a braking car keeps its nose down one tick; the mirror shows 45 degrees to 100 m; engines idle at the original's pitch; live rounds and drops capped as the original caps them (the oldest goes); an AI watches its left side for no-go ground. [B]
- Weapons: rounds start at the muzzle without spread; the dry click on a re-press; AI keeps firing with Unlim. Ammo; the EZK Mortar fires and leads; rounds hit signs and posts; AI and emplacements hold fire behind buildings; a splash is not a shot; turrets slew at full speed online and raise to the stop; a turret on a moving car aims from the barrel. [C, I]
- AI: backing away stops on a 3 m ground change; the obstacle chooser tries left first then full lock; targets led along their turn; droppers and mortars gated as the original's; an attacker cap of 0 lets one come; friendly cars held out of the line of fire; no drops under a pursuer closing straight on; a turret's lead delay. [C, G, L]
- Physics and places: a shoved car bounces off the wall; emplacements stand on the ground; ramp rails are solid but not drivable; sphere parts and breakables per part; a flat tyre no longer tugs a scripted drive; a fatal landing ends the step; a network helicopter lifts at once; the Nitro P18 plane can be hit. [C]
- Scripts and the trip: cheats mark the trip as the original does; a mission from the shell puts you in the Piranha; field salvage takes a wreck's specials; setUserRadar, teleportOffset and the start-engine pulse as the original; isShot holds two frames; end of match brakes every car. [C]
- Sound: CB lines not queued with Voice at 0; the map's voice line on the SFX level; silent slick, Blox and fire-drop hits; sounds culled from the player's car; the Effects and Voice sliders on the original's curve; the radar's sweep, pings and growl as the original's. [C]
- `user://build-rules.cfg` picks the 1998 build's turret slew and AI tactic choice (the default) or the 1997 CD's. [I]
- Rows for the original's rules: Unlim. Ordnance (no live cap, single player), Melee A.I. (the bots follow Difficulty, or Easy / Hard), Simulation Rate 24 Hz (the original's step under a 20 fps cap). [D, E, I]

## Multiplayer and co-op

- A damaged weapon shows only the shots the server fires; the shots counter survives a respawn; the ping reads the round trip under packet loss; a right click on the Esc menu's key map waits for a key (#106). [A]
- Tab locks the standings open with a pointer and the host's boot by double-click; `/ban NAME` by name and address; Connect to Server under the game list (#122). [D, H]
- Host rules: Ignition Lock, Handicaps -3 to +3, Auto handicaps from the standings (dealt x0.2 to x5, taken the inverse), bots' skill from Difficulty. [D, I]
- Co-op: EXTRA ENEMIES (+1 per player by default), DAMAGE TAKEN x0.5 to x1.5 (the fraction carried, so a flame still burns), Rolling Start (a joiner comes in at a teammate's speed; `[port] coop_rolling_start`); the co-op form shows only what a story mission uses, with a help line per row. [D, H, AI]
- The host form keeps the fork's look (the options box, the arena scrap, rule tips) with upstream's labelled rules table. [X, AF]
- The network forms show each game's build; a joiner on another build is let in with a notice. [I]
- The network Esc menu has Self Destruct (new car) and says Leave Match / Exit I76; an arena asking for the menu's track starts on the next mission track. [I]
- Pistol Left / Pistol Right keys (the .45 fires as soon as the arm is up). [I]
- A melee bot's route in one open cell no longer crashes the server; the game list keeps a missing map's lookup for 5 s instead of re-reading the folders every redraw. [AD]

## Performance

- The frame-cost survey (docs/perf/costs.md) and the defaults it led to: Ambient Occlusion off, Shadow Detail Low (600 m), MSAA 2x, the mirror on alternate frames with the original's reach, terrain shader distance tiers. [S, AT]
- T05 at 3440x1440 median 62.5 to 100.3 fps: the radar redrawn only on change (full collections 65 to 2 a run), the smoke keeps a light list, the drop-shadow scan spread over frames; `[port] target_fps` lowers Render Scale to 67 % at most while GPU-bound. [AS]
- Render Scale below 100 % with FSR 1.0 for an older GPU. [D]
- Load time (docs/perf/loadtime.md): the world built on a thread from the scene's start, the cars' parts, cockpit art and lighting calibration off the main thread, the HD pack uploaded as stored. [I, V]
- Drop-shadow casters found once and shadow nodes told only what changed (less garbage a frame). [C2]
- Control latency: with VSync the newest finished frame is shown (`[port] low_latency_present`), a 5 ms audio buffer and 240/s physics step (`low_latency_audio`), the player's car drawn at the newest tick carried forward (`draw_ahead`, off); docs/1997-parity.md has the measurements. [W, AG]

## Sound, shaker and force feedback

- A bass shaker signal of the port's own on the LFE channel: engine, road by surface and speed, a knock at road edges, shots, damage-scaled hits with a cabin ring, explosions (Audio Control's Output and LFE Level; `[port] lfe_*`, `tactile_eq`, `crossover`; docs/lfe-bands.md). [I, K, N, P]
- The movies' top octave shelved so cut-scenes are not brighter than the game; the effects' tone adjustable (`[port] movie_eq`, `game_eq`). [K]
- Force feedback on a DirectInput wheel or stick on Windows with the original's effects (Controls' Force Feedback Wheel, Wheel Strength); Pad Enhanced adds pulses for the nitrous, handbrake, gears, starter and shots (Rumble Strength). [D]
- `[port] movie_ignition=on`: the modern look's ignition plays the briefing movie's engine start-up, cut from your own install at run time. [AC]

## Controls and input

- A pad button as a shift layer, tap / hold on one button, look-back bindings (fire shoots the rear gun), a wheel and a stick set up together, the T300RS and CH Fighterstick recognised by name (the Controller and Joystick screens). [D, H]
- Head tracking from opentrack's UDP output (Play Options > Head Tracking Analog / Digital). [D]
- Held keys no longer stick after the OS swallows a modifier's release. [I]
- Steam Deck: a Steam Input layout and add-to-Steam script in `tools/deck`; `i76 keysheet` prints your bindings as one page. [D]
- `I76_INPUT_RECORD` / `I76_INPUT` record and replay a mission tick-exact; `I76_TELEMETRY_UDP` sends the car's motion to seats and dashboards at 60 packets a second. [D, H]

## Developer tools and settings

- A Developer panel, off for players (`[port] developer=true`, `--dev`; Ctrl+` or Ctrl+F2, and a row at the bottom of the Esc Options Menu): teleport to landmarks, repair, God mode, store and restore the sim, freeze and step, a Scene picker for any mission in any car, a live Loadout, a Settings column stepping every `[port]` key without a row, a Signals column for force feedback, rumble, telemetry and the shaker; on the main menu Import Saves (the original game's bookmarks) and All Scenes; scales with the window. [D, I, N, AF]
- The cheat mark: Play Options' Unlim. Ammo, Armor and Chas. Rein. make a cheater for the session as in the original; a panel action that changes the running attempt (a teleport, repair, God mode, swap) marks that attempt only, cleared at the next fresh start. [C, BE]
- Show FPS (`[port] dev_fps`), an `I76_QUIET_WINDOW=1` start for test runs behind your windows, `I76_LOAD_PROFILE`, the shadow probe hooks `I76_SHADOW_*` and `I76_DEBUG_DRAW=pssm`; every hook in docs/dev-hooks.md. [I, N, U, V, AM]
- Benches and surveys: `tools/frame-cost`, `tools/load-time`, `bench.py` (docs/perf/plan.md, the 120 fps ladder); `tools/TactileRender` renders the shaker signal to WAV. [P, S, V, AS]
- The test suites on a machine with the install: wall-clock tests carry `Load=Sensitive`, the known failures answered, path- and platform-honest Formats tests (docs/play-setup.md "Running the tests"). [K, AE]
- The pages for upstream: docs/upstream-handoff.md and the first ten pull requests drafted; docs/play-tracker.md ranks every change with its evidence; docs/1997-parity.md measures the 1997 look against the patch kit. [AB, W]

## Movies

- HD movies: a `movies/<name>.ogv` in the user folder plays in place of the install's SMK in the modern look, at the briefing's place while the world builds. The fork's own renders are not shipped; the support is. [N]
- The movies' sound matched to the game's and the ignition cut from the briefing: above. [K, AC]

## Withdrawn

- The fork's car deaths and explosions (DeathVariety: deaths by cause in two stages, a cause-specific sign, `[port] death_variety`) are withdrawn: the modern look's deaths, explosions, fire and smoke are upstream's again. The work stays on its branches. [L, Q, AF, AY]
- Hide Map Edge Cliffs (a week of hiding the boundary walls) was removed: the walls and the slopes up to them are the maps' intended shape. [D, I, K, N, O]
- Dropped because upstream did the same: the siren loop, macOS entitlements and microphone, the load-card audio hiccup, the editor's tool column, Difficulty's damage, Heated Seats' ammunition, the update check, the horizon depth pre-pass, the first host-form redesign (comments in the manifest say which upstream commit). [A, B, C, C2, D, I]

## How to try it

- The fork's `main` branch (this repository) builds as DEVELOPMENT.md says; the build line in the game's corner says which build you have: `play-N (commit)` is a deployed build of pass N from `main`, `dev` a local build, `ci-N` upstream's tester build.
- Settings are in the user folder's shell.cfg; its `[port]` section holds the fork's keys (docs/playing.md, "Settings without a row"). A shell.cfg from upstream's build is migrated forward, never overwritten.
- Every change here is backed by its evidence, a before | after sheet, clip or test kept with the lane that made it (docs/play-tracker.md names it per branch), and a branch offered upstream carries its sheet in the pull request's body, never in the repository.
- docs/playing.md has the details for players: how the game finds your install, the HD textures, controls, custom maps and status.
