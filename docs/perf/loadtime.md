# Mission load times (play tip f8022fc, branch perf/load-time)

What a mission start costs, phase by phase and thread by thread, measured with the game's own `LoadProfiler`
(`I76_LOAD_PROFILE=1`) in T01, M01 and T05, with the 4x texture pack and with the install's originals, warm and cold
caches; how long the retail game shows its loading card; the menu side; and the same starts after this branch's changes.
The plan that follows from it is [loadtime-plan.md](loadtime-plan.md). The method follows the frame-cost survey's
`docs/perf/costs.md` (branch `perf/frame-cost-survey`): isolation, medians of three, the spread, contaminated runs marked.

## Headline

- **The retail card**: up 5.0 s (T01) and 6.5 s (T05) on the emulated box-spec P166, its first frame to the briefing's 5.7
  and 7.4 s; it is up for the load, not timed. Ours now holds 6.5 s before a briefing.
- **Where the seconds go** (warm, no briefing): 0.4 s before the card, the build 1.6-4.2 s, the attach on the main thread
  5.8-10.3 s (the cars 0.9-3.7 s, the effects rehearsal 1.1-2.0 s, the look 1.3-1.5 s, the sky and calibration 0.4-1.0 s).
  T01 11.0 / 12.5 s (originals / 4x), M01 7.8 / 10.8 s, T05 10.8 / 15.0 s. Cold caches add 0.6-6.0 s, mostly the road
  layers' bake.
- **After this branch** (interleaved): without a briefing 0.3-0.6 s sooner; the briefing skipped at once 1.2 s (T01) and
  4.2 s (T05) sooner with the card now first (the card after it 6.8 s instead of 14.3 / 17.4 s); the attach after a
  briefing the build finished under 1.8-2.6 s shorter (the cars' parts and the calibration off the main thread). Watched
  to its end, a briefing now ends about 6.5 s later than in the old movie-first order (the card comes first).
- **Shared between missions**: about 2 s of a start (pipelines, the shadow atlas, first-run code); the rest is the
  mission's own. The click to the card's first frame is 0.1 s.

## How it was measured

- **Machine.** GTX 1080 Ti at a 180 W power cap, Windows 10, 8 logical cores, Godot 4.7.2 .NET (Forward+, Vulkan), a
  1920x1080 window. The machine was shared with other lanes all morning: their builds, test runs and a resident Python
  process kept the CPU at 70-100 % before and during most runs (the CPU load is in every row of `results.tsv`). This is
  the survey's largest source of noise; the before/after comparisons are therefore **interleaved** (the two builds run one
  after the other for every mission and rep) rather than taken in separate batches.
- **Builds.** "before": the play tip f8022fc plus the profiler only (94e0c64, worktree `r3-loadtime-base`); "after": this
  branch. Both are fresh worktrees without an imported `.godot/` (the boot splash's PNG is missing in both; nothing else
  the mission start loads is an imported resource).
- **Scene.** `--mission t01|t05 --drive --seed 1 --skip-cutscenes --skip-movies`, and `--melee --mission m01 --drive
  --seed 1 --skip-movies`: a start without the briefing (the card until the mission is ready), the frame-cost survey's
  everything-on settings (Terrain High, Shadow High, MSAA 4x, Vegetation High, 20 km, the 4x pack or `hd_textures=false`).
  The briefing's sequence is measured separately (below), with `I76_MOVIE_SKIP_MS` to take the skip path.
- **Instrument.** `LoadProfiler` (`game/Scripts/Boot/LoadProfiler.cs`): every `MissionBuilder.Trace` stamp closes a phase on
  the thread that stamps it, with the textures made in it (original or upscale, MB, ms) and the renderer's pipeline
  compilations (main thread); events for the card's first frame, the movie's start and end, the build's end and the
  mission (the card gone). The clock's zero is the scene's start (a command-line mission) or the shell's click. One CSV a
  run and a `load-profile-result:` line; `I76_LOAD_PROFILE_QUIT=1` ends the run 3 s into the mission.
- **Isolation.** Each run a fresh engine in a scratch user folder (`APPDATA` overridden): `shell.cfg` written for the
  configuration, the `tex-world-*` and `tex-menu` folders junctioned from the live user folder (nothing copied),
  **warm**: one folder kept between runs with copies of the live bake cache (`cache/`, 366 MB), shader cache and Vulkan
  pipeline cache, and a rep 0 of every configuration run first and dropped; **cold**: a new folder for every run, with no
  bake cache, shader cache or pipeline cache. The Windows file cache was not dropped (no reboot): the install's ZFS and the
  pack's files are as warm as the runs before left them, so "cold" is the game's own caches cold, not the disk's.
- **Rules.** Before every run the GPU under 10 % and no other Godot process (`nvidia-smi`, `tasklist`), the shared GPU lock
  held for batches of under 20 minutes; during the run other Godot, dotnet, python and 86Box processes and the CPU load
  sampled every 2 s. A run another Godot process overlapped is rerun; a rep more than 25 % off its siblings' median while
  the CPU load was over 50 % is marked contaminated (`*`) and left out of the median. Medians of the clean reps 1-3; the
  spread is max-min.
- **Harness.** `tools/load-time/`: `loadprofile.py` (one configuration or an interleaved A/B, reps, packs, caches),
  `summarise.py` (the tables), `split.py` (the parts), `menu.py` (the menu side). Outputs under
  `_scratch/r3-loadtime/` (`survey/`, `ab/`, `check/`, `menu/`, `retail/`).

## The retail card

The retail game shows `addon\loadscr.pcx` ("SOMEWHERE IN THE SOUTHWEST...") while it loads the mission, then plays the
briefing (re/subsystems/mission.md: the load, then the intro movie at 0x40371c). Its time is its load's on the machine; the
box-spec machine is the lab's emulated P166.

- **Rig.** `i76-uncap-lab/emu` par runner (`tools/par/parrun.py`, headless 86Box 6.0 on a private desktop): Pentium 166,
  32 MB, ASUS P/I-P55T2P4, S3 Trio64, Windows 95 RTM, the retail 1.0 `I76.EXE` (1997-03-15) from the CD install on the
  emulated IDE disk, CD 2 in the drive, the software renderer at the detail the base disk's `I76PLYR.DEF` holds (the lab's
  par base, `w95pb-par-t01sb16-emulane.img`; `PARRUN_TIME_SYNC=disabled`: on today's date Windows 95 otherwise stops at a
  daylight-saving dialog). The window recorded at 10 fps (`video.py`), the guest's clock (GetTickCount) logged every
  second over COM1 with the host's.
- **Scripts.** `card-t01-retail.txt` (main menu, TRIP, NEW TRIP: straight into T01) and `card-t05-retail.txt` (LOAD
  BOOKMARK "T05 start (emu)", ACCEPT SALVAGE, the repair form's DONE); no input during the load.
- **Reading.** The card's on and off from the video's mean luma (black 16.4; the card 21.6; the menu 40; the movie 80-130),
  converted to guest time through the heartbeats around it (the VM ran at 0.87 of real time in T01, 0.60 in T05: other
  lanes' load; guest time is what a real P166 would show, the emulated disk included).

| Mission | Click to the card's first frame | Card up (ramp in, held, ramp out) | Black after it | Card's first frame to the movie's | Click to the movie |
|---|---|---|---|---|---|
| T01 (NEW TRIP) | 0.0 s | **5.0 s** (0.8 + 3.7 + 0.5) | 0.7 s | **5.7 s** | 5.7 s |
| T05 (bookmark, DONE) | 0.7 s (the form closing) | **6.5 s** | 0.9 s | **7.4 s** | 8.2 s |

The card is not timed: it is up while the mission loads, so its time is the load's and differs by mission. The port's
card now holds **6.5 s from its first frame to the briefing's** (`BriefingOverlap.RetailCardSeconds`, the mean of the two),
its 0.32 s ramps included. Captures: `_scratch/r3-loadtime/retail/retail-t01-p166.mp4` (4.6 MB),
`retail-t05-p166.mp4` (5.2 MB), `t01-card-sheet.jpg` and `t05-card-sheet.jpg` (the frames at 2 and 1 a second); runs
`emu/results/par/20261105-054139--loadcard-t01-1` and `20261105-054612--loadcard-t05-1`. One run each: the guest's
timing is deterministic up to its disk and CD emulation, and the two runs' heartbeats agree with the real-time ratio.

## The mission start before this branch (warm caches, no briefing)

Click-to-mission is the scene's start to the card gone (the mission's first frame follows it); every phase of these runs is
on the main thread (before this branch only a briefing built on a thread). Medians of reps 1-3 (`survey/`, tag `before`):

| Mission | Pack | To the mission s | spread s | card's first frame ms | textures (upscales) | MB | pipelines | reps |
|---|---|---|---|---|---|---|---|---|
| T01 | 4x | 12.49 | 0.24 | 301 | 563 (561) | 109 | 170 | 2 (1 contaminated: 16.2 s at 88 % CPU) |
| T01 | originals | 11.01 | 1.72 | 351 | 563 (0) | 27 | 170 | 3 |
| M01 | 4x | 10.79 | 2.75 | 315 | 211 (209) | 43 | 180 | 3 |
| M01 | originals | 7.81 | 2.92 | 422 | 211 (0) | 11 | 180 | 3 |
| T05 | 4x | 14.97 | 1.60 | 314 | 744 (742) | 115 | 154 | 3 |
| T05 | originals | 10.77 | 0.48 | 287 | 744 (0) | 29 | 154 | 3 |

The pack costs T01 1.5 s, M01 3.0 s and T05 4.2 s here (the frame-cost survey measured T01 8.7 vs 11.6 s from the card's
"up" stamp, on an older tip and a quieter machine). Its share: the decodes and uploads of 209-742 upscales, the ground's
PBR materials (1.4-1.8 s, absent without the pack) and the cars' and the rehearsal's bigger textures.

### Where the seconds go (seconds, medians)

| build | mission | pack | cache | before card | waited | build | cars (build) | attach | sky + lighting calibration | look (shadows, draw distance, vegetation, quality) | spawn: sim world, combat view | spawn: cars' nodes | spawn: damage view, prewarms | first tick, vegetation and trees prewarmed | effects rehearsal | attach: other | reps |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| before | t01 | 4x | warm | 0.41 | 0.00 | 4.19 | 0.00 | 8.28 | 0.74 | 1.38 | 0.62 | 2.40 | 0.53 | 0.50 | 2.00 | 0.31 | 3 |
| before | t01 | off | warm | 0.45 | 0.00 | 2.45 | 0.00 | 8.22 | 0.95 | 1.41 | 0.84 | 1.60 | 0.57 | 0.56 | 1.89 | 0.30 | 3 |
| before | m01 | 4x | warm | 0.42 | 0.00 | 2.96 | 0.00 | 6.98 | 0.61 | 1.34 | 0.67 | 1.88 | 0.39 | 0.19 | 1.52 | 0.32 | 3 |
| before | m01 | off | warm | 0.49 | 0.00 | 1.55 | 0.00 | 5.76 | 0.53 | 1.54 | 0.73 | 0.92 | 0.38 | 0.23 | 1.14 | 0.31 | 3 |
| before | t05 | 4x | warm | 0.41 | 0.00 | 4.21 | 0.00 | 10.26 | 1.02 | 1.38 | 0.85 | 3.67 | 0.71 | 0.32 | 1.80 | 0.31 | 3 |
| before | t05 | off | warm | 0.39 | 0.00 | 2.45 | 0.00 | 7.93 | 0.87 | 1.33 | 0.70 | 2.55 | 0.70 | 0.30 | 1.27 | 0.32 | 3 |


"build" is `MissionBuilder.Build`; "attach" is the main thread's work after it, split into its groups (`split.py`). The
attach is **two thirds** of every start. Its largest items:

- **The cars' nodes and parts** (`spawn: cars made`, 0.9-3.7 s): `CarPartsCache.Get` per car (the model assembled, its
  meshes, 137-304 textures) and the node per car. T05 has the most cars.
- **The effects rehearsal** (1.1-2.0 s): 150-180 pipeline compilations and 131-195 textures (27-48 MB with the pack),
  behind the card so the first explosion, death and fire do not hitch.
- **The look** (1.3-1.5 s): the shadow atlas and filter, the draw distance's horizon ring, the vegetation's scatter and
  meshes, the modern quality's environment.
- **The sky and the lighting calibration** (0.5-1.0 s, the calibration's arithmetic 0.3-0.7 s of it).
- **The sim's world and the combat view** (0.6-0.9 s), **the damage view and its prewarms** (0.4-0.7 s), **the first tick
  and the vegetation's prewarm** (0.2-0.6 s), **the card's fade out** (0.3 s).

The build's own largest items with the pack: the ground materials (1.4-1.8 s, cached in the bake cache, packed on load),
the terrain's chunks (0.3-0.7 s), the placements' textures (`done`, 0.4-0.8 s), the road bake (0.3 s). Before the card:
the card's picture read (0.1-0.3 s) and its 0.32 s ramp.

### On a thread, without a briefing (the first version of this branch, measured and dropped)

Built on its thread behind the card with nothing to overlap (no briefing), the start was slower, interleaved against the
play tip (`ab-threaded-nobriefing/`, 2 reps): T01 13.06 vs 12.39 s, T05 15.10 vs 13.36 s, and M01 12.37 vs 8.53 s (its
build done at 2.9 s, then held to the card's 6.5 s, a rule since dropped). The build itself took 5.2-5.8 s on the thread
against 3.9-4.0 s on the main thread: every renderer call that returns a value (a mesh, a material, a texture created)
waits for the main loop's next frame. The attach got shorter by what moved off it (T01 8.0 -> 6.5 s, T05 9.1 -> 6.5 s:
the cars' parts and the calibration). So a start without a briefing builds on the main thread again; the threaded build is
for the briefing, which hides it.

### The build's frame cap (T01, 4x, no briefing, built on the thread, one run each, `check/`)

| Cap behind the card | terrain: chunks added | build done | to the mission |
|---|---|---|---|
| 240 fps (the briefing path's cap before) | 4.62 s | 10.1 s | 17.0 s |
| 1000 fps (now) | 1.26 s | 6.7 s | 13.4 s |
| uncapped | 1.08 s | 6.1 s | 13.3 s |

## Before and after, without a briefing (interleaved, warm, 4x pack)

`ab/`: the play tip with the profiler (`base`) and this branch (`after`) one after the other for every mission and rep,
3 reps, no warm-up needed (the warm folder was warm from the survey). A start without a briefing builds on the main thread
in both; what differs is the lighting calibration solved beside the load.

| Mission | base s | after s | after - base | spread base / after | sky + calibration base / after | attach base / after |
|---|---|---|---|---|---|---|
| T01 | 11.89 | 11.27 | **-0.62** | 0.37 / 0.17 | 0.73 / 0.28 | 7.65 / 7.33 |
| M01 | 8.53 | 8.27 | **-0.26** | 0.33 / 0.39 | 0.44 / 0.26 | 5.96 / 5.76 |
| T05 | 13.36 | 12.96 | **-0.40** | 0.33 / 0.06 | 0.91 / 0.28 | 9.00 / 8.60 |

The calibration's 0.2-0.6 s leaves the critical path; the rest of the attach is unchanged within the noise (the look's
1.3-1.4 s, the cars' 1.5-3.3 s, the rehearsal's 1.4-1.7 s).

## Textures by family (warm, 4x pack; the same counts with the originals at a quarter of the MB or less)

Counted where `TextureFactory` makes a texture for the GPU, by the phase that asked for it. "ms" is the time inside the
factory for those textures: tens of milliseconds, because the pack's decodes run before, in parallel
(`TextureFactory.Prefetch`), and the upload is the renderer's. Textures are not where the seconds go; the work around them
is (the meshes, the nodes, the ground materials, the pipelines).

| Family (phase) | T01 | M01 | T05 |
|---|---|---|---|
| World: ground, sky, horizon (`ground, sky, horizon`, `sky`) | 10, 2 MB | 10, 2 MB | 10, 2 MB |
| World: placements (`scenery spawn`, `done`) | 168, 29 MB | 16, 2 MB | 122, 15 MB |
| Cars: every car's parts (`spawn: cars made`; after: `cars prefetched` on the build's thread + the player's own 16) | 137, 21 MB | 44, 10 MB | 304, 38 MB |
| Cars: the player's damage looks and the wrecks (`car prewarmed`, `wrecks prewarmed`) | 53, 9 MB | 10, 2 MB | 119, 21 MB |
| Effects rehearsal: explosions, fire, smoke, the death stand-in (`effects rehearsal`) | 195, 48 MB | 131, 27 MB | 189, 39 MB |
| Total (upscales of them) | 563 (561), 109 MB | 211 (209), 43 MB | 744 (742), 115 MB |

The cockpit's own art (the dash, the gauges, the panels: the pack's cockpit family) is read by the cockpit outside the
factory and is not in these counts; its time is inside `spawn: cars made` (the player's car's setup).

## With a briefing: the card's sequence, before and after (interleaved, warm, 4x pack)

`abb/`, 2 reps each, the briefing played and skipped by `I76_MOVIE_SKIP_MS` (3 s: a player who skips at once; 20 s: one
who watches longer than the build). Before: the play tip's `briefing_while_loading=on` (black, the movie, the card; the
build on a thread at below-normal priority and a 240 fps cap). After: `card_first` (the card 6.5 s, the movie, the card
again; the build at normal priority behind the card, 1000 fps, the cars' parts on its thread, the calibration beside).
Times from the scene's start; medians of 2 (each pair within 0.2 s).

| Mission, skip at | build | card's first frame | movie start | movie over | build done | to the mission | card after the movie | attach on the main thread (20 s rows: the CPU load) |
|---|---|---|---|---|---|---|---|---|
| T01, 3 s | before | - | 0.67 | 3.55 | 9.23 | **17.85** | 14.30 | 8.29 |
| T01, 3 s | after | 0.44 | 6.91 | 9.88 | 6.35 | **16.66** | 6.78 | 6.46 |
| T05, 3 s | before | - | 0.55 | 3.47 | 11.43 | **20.84** | 17.37 | 9.08 |
| T05, 3 s | after | 0.43 | 6.90 | 9.87 | 8.50 | **16.66** | 6.79 | 6.47 |
| T01, 20 s | before | - | 0.57 / 0.92 | 20.47 / 20.77 | 8.46 / 9.88 | 28.36 / 31.47 | 7.89 / 10.70 | CPU 47 / 88 % |
| T01, 20 s | after | 0.46 / 0.80 | 6.93 / 7.24 | 26.89 / 27.18 | 6.29 / 7.84 | 36.15 / 39.77 | 9.26 / 12.59 | CPU 80 / 99 % |
| T05, 20 s | before | - | 0.55 / 0.99 | 20.47 / 20.82 | 10.75 / 12.82 | 34.86 / 33.43 | 14.39 / 12.61 | CPU 75 / 90 % |
| T05, 20 s | after | 0.55 / 0.57 | 7.02 / 6.98 | 26.97 / 26.95 | 9.55 / 10.12 | 35.57 / 34.79 | 8.60 / 7.84 | CPU 64 / 91 % |

The 20 s rows are the two reps side by side (rep 1 / rep 2), not medians: the other lanes had the CPU at 64-99 % through
them and the attach after the movie varied by 3 s between reps, so read them as contaminated. With that said: T05's card
after the movie is 8.6 / 7.8 s after against 14.4 / 12.6 s before; T01's is 9.3 / 12.6 s after against 7.9 / 10.7 s
before, longer than the same build's 6.8 s after a 3 s skip (its `spawn: cars made` 2.3 s against 1.3 s, the rehearsal
2.2 s against 1.6 s, at 80-99 % CPU). Watching the whole briefing, the mission comes 6.5 s later than before (the card
first) less what the attach saves. Not settled on this machine; to rerun on a quiet one.

What the player sees after: the card from 0.43 s to 6.9 s (its 0.32 s ramps included: 6.5 s first frame to the movie's,
as the retail game), the briefing, and the card again for 6.8 s, the attach. The build is done before the briefing
starts in T01 and 1.6 s into it in T05. Skipping at once still reaches the mission 1.2 s (T01) and 4.2 s (T05) sooner
than before, although the card now comes first: the build is faster (normal priority, 1000 fps, 6.4 vs 9.2 s in T01) and
the attach 1.8-2.6 s shorter (the cars' parts made on the build's thread, the calibration beside the load).

## A second mission in the same run: what is shared (warm, after, no briefing)

`chain/`: T01 then T05 in one process (`I76_LOAD_PROFILE_NEXT=t05`, the scene reloaded as the shell's launch does), 3 reps
(rep 2 at 92-96 % CPU marked and left out). T05 as the second load against T05 first in a run (the A/B's `after`, 4x; the
survey's `before` for the originals):

| T05 | first load s | second load s | saved | build | attach | effects rehearsal | its pipelines | look | combat view + sim world |
|---|---|---|---|---|---|---|---|---|---|
| 4x, first | 12.96 | | | 3.95 | 8.60 | 1.70 | 150-154 | 1.38 | 0.66 |
| 4x, second | | 11.09 | **1.9** | 3.51 | 7.72 | 1.31 | 20 | 1.06 | 0.34 |
| originals, first (before) | 10.77 | | | 2.45 | 7.93 | 1.27 | 154 | 1.33 | 0.70 |
| originals, second | | 8.02 | **2.4** (0.6 of it the calibration) | 2.12 | 5.70 | 0.89 | 19 | 1.01 | 0.31 |

Shared, made once a run: the pipelines (the rehearsal's 150 compilations become 20; 0.4 s), the renderer's shadow atlas
and the look's global state (0.3 s), the combat view's and the sim's first-run code (0.3 s), part of the ground
materials' staging (the build 0.3-0.4 s shorter). Not shared: the cars (`spawn: cars made` 3.7 s either way: every car's
parts and textures are the mission's, its palette's), the terrain, the placements, the rehearsal's 189 textures (made again
for the mission's palette). So about 2 s of a 11-13 s start is shared; the rest is the mission's own.

## The menu side (warm, 4x pack, the after build; `menu/`, 2 reps each, `tools/load-time/menu.py`)

| What | rep 1 | rep 2 | Note |
|---|---|---|---|
| The engine's start to the main menu's first frame (`--skip-movies`: no intro movies) | 2.26 s | 1.87 s | all six starts of the batch: median 1.84 s, 1.67-2.26 s |
| The 3D garage's first opening, after the menu's warm-up (opened 600 frames in) | 0.37 s | 0.19 s | the warm-up itself took 1.09 / 0.86 s of the menu's idle frames |
| The 3D garage's first opening, before the warm-up (opened at frame 2) | 0.91 s | 0.84 s | the fix/first-garage-load-prewarm precedent: ~0.6 s saved by doing it at menu idle |
| A mission from the shell: the click to the scene's start | 8 ms | - | the shell's scene freed, `Main` again |
| ... the click to the card's first frame | **0.10 s** | **0.11 s** | the card made in 58 ms |
| ... the click to the mission (T01, no briefing) | 11.85 s | 10.69 s | as from the command line |

The click to the card is a tenth of a second, so starting the build at the click instead of at the scene's start would
gain under 0.1 s; the build starts before the card is drawn as it is.

## Cold caches (the play tip, no briefing; `cold/`, a fresh user folder every run, 3 reps)

| Mission | Pack | Cold s | spread s | Warm s (the survey) | Cold - warm | build cold / warm | attach cold / warm | road bake done (cold) |
|---|---|---|---|---|---|---|---|---|
| T01 | 4x | 17.78 | 2.14 | 12.49 | +5.3 | 8.37 / 4.19 | 8.86 / 8.28 | 4.96 s |
| T01 | originals | 12.33 | 0.60 | 11.01 | +1.3 | 4.56 / 2.45 | 7.47 / 8.22 | 2.38 s |
| M01 | 4x | 11.80 | 2.09 | 10.79 | +1.0 | 3.53 / 2.96 | 7.68 / 6.98 | 0.95 s |
| M01 | originals | 8.38 | 0.09 | 7.81 | +0.6 | 1.89 / 1.55 | 5.94 / 5.76 | 0.57 s |
| T05 | 4x | 20.95 | 1.16 | 14.97 | +6.0 | 9.93 / 4.21 | 10.57 / 10.26 | 6.24 s |
| T05 | originals | 15.34 | 0.61 | 10.77 | +4.6 | 5.80 / 2.45 | 8.98 / 7.93 | 3.35 s |

Cold costs the **bake cache** above all: the road layers are baked again (`road bake done`: the layers' mipmaps and
compression, 2.4-6.2 s; warm 0.3 s), the modern terrain's surface filled again. The shader and pipeline caches cold cost
the attach up to 1 s (T01's originals measured shorter cold, within the batches' noise): the rehearsal 1.3-2.3 s against
1.1-2.0 warm, the look's shadow settings 1.0-1.2 s against 0.5-0.6.
The ground materials are not slower cold (1.1-1.4 s): with no cache they are packed from the imported images either way.
A player's first start of each mission is the cold row; the bake cache keeps every later one warm.

## Noise

The machine was busy with other lanes throughout (CPU 70-100 % before many runs, see each `results.tsv`'s `cpu_load`):
spreads of 0.1-0.4 s in the interleaved A/B batches, 1-3 s in the separate survey batches, and the 20 s briefing rows
contaminated outright. Differences under ~0.5 s outside the interleaved tables are within the noise. A rep more than 25 %
off its siblings' median at over 50 % CPU is marked (one in the survey: T01 4x warm rep 1, 16.2 s). Not measured: the
Windows file cache cold (it needs a reboot), the 2x and 3x packs (the frame-cost survey has their load times), the other
missions, the 1997 look's start (unchanged: main-thread build, the original's order).

