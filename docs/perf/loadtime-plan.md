# Load-time plan: where a mission start's seconds go, what was moved, what comes next, and speculative loading

Companion to [loadtime.md](loadtime.md), which holds the measurements (branch `perf/load-time` from the play tip f8022fc;
Godot 4.7.2 .NET, Forward+, a GTX 1080 Ti at 180 W, 8 logical cores shared with other lanes). Numbers here are that
survey's medians, warm caches unless said. "Before" is the play tip with the profiler only (94e0c64); "after" is this branch.
The model is the frame-cost survey's `docs/perf/plan.md` (branch `perf/frame-cost-survey`): each item says the mechanism,
what our code does, the change, the saving, the risk, and how to measure it.

## Where the seconds go

A mission start is three parts (loadtime.md, "Where the seconds go"):

1. **Before the card** (0.3-0.5 s): the scene's start, the card made (its picture decoded) and faded in.
2. **The build** (`MissionBuilder.Build`; 1.5-4.2 s on the main thread before, 6-7 s on its own thread after): the
   mission file, terrain (the 1997 arrays, every chunk's mesh, the modern look's surface and coarse meshes), the road
   bake, the ground's PBR materials (with a pack: 1.4-2.1 s, read from the bake cache and packed), the placements and
   their textures. Mission-specific but for part of the ground materials' staging (a second load's build is 0.3-0.4 s
   shorter) and the pipelines its materials need.
3. **The attach** (6-10 s, always on the main thread): the world into the tree, the sky and the lighting calibration,
   the look (shadow settings, draw distance, vegetation), the sim's world, the combat view, the cars' nodes (and before
   this branch their parts), the damage view and its prewarms, the first tick, the vegetation and trees prewarmed, the
   effects rehearsal (an explosion, a death, fire and smoke made far below the world for 4 frames, so their pipelines are
   compiled behind the card: 150-180 pipeline compilations and 130-195 textures), the card's fade out.

The attach is two thirds of the load and cannot leave the main thread: it puts nodes in the tree, sets the renderer's
state and draws the rehearsal's frames. Before this branch the build was on the main thread too except under a briefing.

The HD pack adds 1.4-4.2 s (t01 12.5 vs 11.0 s, t05 15.0 vs 10.8 s): its textures' decode and upload (561-742 upscales,
109-115 MB) spread over the build, the cars and the rehearsal, and the ground materials it brings.

## What runs where, and why

- Main thread, serial, by necessity: everything that adds nodes to the tree or changes the renderer's global state
  (environment, shadow atlas, sky), the sim's setup (it reads the world's nodes for the ground), the views' first frames.
- Main thread, serial, by habit: the lighting calibration's arithmetic (now beside the load), the cars' parts (now on the
  build's thread), the car nodes' construction (could be split in parts), the effects rehearsal's texture decodes
  (pure CPU, could be prefetched), the vegetation's scatter.
- Build thread: the whole build, made outside the tree. Its renderer calls that return a value (a mesh, a material, a
  texture created) are queued to the main loop and wait for its next iteration, so the main loop's frame rate sets the
  build's pace: 240 fps cost t01 3.3 s of waiting on its terrain chunks alone (1000 fps: 1.3 s).
- Workers already used: the road layers' raster, the ground materials' staging, the distant terrain, the pack's decodes
  (`TextureFactory.Prefetch`, `Parallel.For`), the cavity maps.

## Done on this branch (measured in loadtime.md, "Before and after" and "With a briefing")

Net, interleaved against the play tip, warm, 4x: without a briefing T01 11.89 -> 11.27 s, M01 8.53 -> 8.27 s, T05 13.36 ->
12.96 s; with the briefing skipped after 3 s T01 17.85 -> 16.66 s and T05 20.84 -> 16.66 s although the card now comes
first, and the card after the briefing 14.3 / 17.4 s -> 6.8 s. Watching a long briefing to its end, the mission comes the
card's 6.5 s later than with the old movie-first order, less the attach's 1.8-2.6 s (the 20 s rows were contaminated: see
loadtime.md).

### 1. Under a briefing, the build on its own thread from the scene's start, before the card is drawn

- Mechanism. `Main.StartBuildJob` starts `MissionBuilder.Build` on the `mission-build` thread before the loading card is
  made; `BuildBehindCard` steps `CardSequence` a frame at a time (card for the retail time, the movie, the card again).
- Saving. Under a briefing (card_first) the build is hidden: it is done 6-7 s after the scene starts, about when the 6.5 s
  card is over, so the briefing's end starts the attach at once. Without a briefing there is nothing to hide it under, and
  on a thread it was 1-2 s slower than on the main thread (the renderer's queued calls): measured, then dropped
  (loadtime.md, "On a thread, without a briefing"); such a start builds on the main thread behind the card as before.
- Risk. The build made off the main thread was already the briefing path's (feat/movie-while-loading). The 1997 look,
  retries, co-op and the editor keep the main-thread build and the original's order.

### 2. The build's frame cap 240 -> 1000 fps

- t01's terrain chunks 4.6 -> 1.3 s, the build 9.8 -> 6.4 s (uncapped: 1.1 s and 5.8 s). The cost is the GPU drawing a
  static card or a movie at up to 1000 fps for the build's seconds; on a laptop that is power and heat for 6-10 s.

### 3. The cars' parts on the build's thread (`Main.PrefetchCars`)

- Every placement's .vcf and the car asked for go through `CarPartsCache.Get` after the world: the assembled model, the
  body, weapon, turret, lamp and wheel meshes and their textures (121 upscales in t01). t01's "cars made" on the main
  thread 2.4 -> 1.3 s, T05's 3.3 -> 1.3 s; the prefetch costs 1.0-2.4 s on the build's thread, hidden under the card and
  the briefing. Only on the threaded (briefing) path. A placement the spawn does not use (a car the script never brings
  in) holds its parts for the mission.

### 4. The lighting calibration's arithmetic beside the load

- `SolveLightingCalibration(background: true)` takes its inputs on the main thread (the ground texture read back and cut to
  8x8, the sky's colours, the sun) and solves on a worker; `FinishLightingCalibration` applies it before the effects
  rehearsal, the world held still if it is not done (it always was in the survey). The sky and calibration group 0.44-0.91
  -> 0.26-0.28 s on the main thread; the whole start 0.26-0.62 s shorter without a briefing.

### 5. The build thread's priority: normal behind the card, below normal under the movie

- Behind the card nothing else of ours needs the CPU; under the movie the decoder does. On this shared machine the
  below-normal build lost whole seconds to other lanes' processes.

## Next, ranked (by saving over risk; item 9 is the largest saving and the largest change)

### 6. The effects rehearsal's pipelines and textures, once a run (1.5-2.0 s a mission; the largest attach item)

- Mechanism. The rehearsal makes ~165 pipelines and decodes ~195 textures (48 MB with the pack) on the main thread every
  mission. The pipelines outlive the scene (the renderer's cache in the process; the shader cache on disk only speeds
  their creation), the textures do not (a TextureFactory a mission, its palette). The editor's playtest already skips
  a second rehearsal in a run (`_warmedLooks`).
- Change. (a) Rehearse at the main menu's idle, as `GarageWarmup` does for the garage (a stage under the menu's opaque
  frame, a few frames): the first mission's pipelines are then made before the click. (b) Skip the mission's rehearsal
  for what a previous mission in the run already compiled, as the playtest does; (c) prefetch the rehearsal's textures on
  the build's thread (`TextureFactory.Prefetch` of the effects' members).
- Saving. The second-load runs (loadtime.md, "A second mission in the same run") bound it: the rehearsal 1.7 -> 1.3 s
  with its pipelines made (150 -> 20 compilations), about 0.4 s a mission; (c), the textures prefetched, the rest of it.
- Risk. A pipeline not compiled shows as a first-explosion hitch, the thing the rehearsal exists for: measure with
  `I76_HITCH_MS` and the first explosion of t01.

### 7. The car nodes and the damage view's prewarms in slices (1.3 s and 0.5-0.7 s)

- `MakeCarNode` per car (Init, weapons, wheels, lamps: node work) and `DamageView.Prewarm/PrewarmWrecks` (the wrecks'
  meshes: CPU work that could join the build's thread as the parts did). Saving 0.5-1 s; low risk.

### 8. The look's renderer settings (0.5-0.6 s, 0.3 s less on a second load in a run) and the drop shadows (0.3 s)

- `SetMode1997` sets the shadow atlas, the filter quality and the draw distance (the horizon ring) every mission; the
  atlas reallocation is the larger part. Keep the last values across the scene reload (they are the renderer's global
  state): ~0.3-0.5 s. Low risk.

### 9. The attach under the briefing (6.8 s after the movie today, T01 and T05 with the pack)

- The rest of the load could run while the movie plays, its world hidden under the movie's layer. The movie is drawn on
  the main thread from a decoder thread's frames, so every main-thread step longer than a frame (the world into the tree,
  the spawn, the rehearsal) would stall its picture; the audio runs on. Only worth it with the attach cut into steps of a
  few milliseconds (as the modern terrain's deferred build is, `ModernSteps`). Saving: the whole card-again wait (6.8 s)
  when the briefing is watched to its end. Risk: movie stutter, and the mission's first tick happening under the movie.
  The largest remaining saving; also the largest change.

### 10. Cold caches

- Cold (no bake cache, no shader or pipeline cache) costs 0.6-6.0 s over warm (loadtime.md's cold table), mostly the
  road layers' bake (2.4-6.2 s cold with the pack's road textures, 0.3 s from the cache), then the terrain surface's fill
  and every pipeline compiled from scratch (up to 1 s). The first start of each mission pays it once. Levers: find where
  the bake's cold seconds go (`I76_BUILD_TRACE` splits it into the raster, the mipmaps and the compression; not done
  here) and spread that part over the workers as the raster already is, and a pipeline cache warmed at the menu (item 6a).

## Round 2: the rest of the load under the briefing, and the menu's warm-up

Measured on `perf/load-time` merged with play 187e255, windowed (1920x1080 before the merge, 1280x960 after), warm, the 4x
pack, 1-2 reps a configuration under the lock-fairness rule (batches under 15 minutes); the CPU was at 60-100 % from
other lanes in most runs, so the times are indicative and the movie counts are the result. `tools/load-time/movie.py`
prints the per-run table; outputs `_scratch/r3-loadtime/r2/` (before the merge), `r2f/` (after), `r2fmenu/`.

### 9 (done in part): AttachSteps, and why it stays after the briefing

- The load after the build is now a list of steps (`Main.AttachSteps`: the world into the tree, the look, `SpawnCarSteps` a car
  a step, `SetupPlayerCarSteps`, the first tick, the prewarms, `WarmEffectsSteps`), run straight through as before by default.
  `I76_ATTACH_UNDER_MOVIE=1` runs them under the briefing at `I76_ATTACH_BUDGET_MS` a frame (default 30) with the world held
  still. With it, the mission follows the briefing at once: T01 and T05 watched 25 s, the card after the briefing **0.01 s**
  (7-9 s without); skipped at 3 s, 2.7 s (T01) and 4.7-5.3 s (T05) instead of 6.6-8.2 s.
- But the briefing stutters: of 375 frames in 25 s, 285-298 were shown, 15-17 dropped outright, 13-14 late (over two
  frames apart), the worst gap 1.05-1.23 s, and the audio ran dry for 281-422 sample frames (13-19 ms). The per-frame budget
  does not help: 22 steps are over 50 ms on their own (the player's dashboard and cockpit panels 1.1-1.2 s, the rehearsal's
  first drawn frame 0.75-0.78 s with ~54 pipeline compilations, the combat view 0.48-0.58 s, the explosions' flipbooks 0.38-0.43 s,
  the drop shadows 0.36 s, the vegetation's prewarm 0.35 s, ...), and a step runs whole. <!-- SWEEP -->
- The user's requirement is a briefing with no dropped or late frame and no audio gap, so it stays off: the rest of the
  load runs after the briefing behind the card, as in round 1. To turn it on, every step must fit the movie's frame: the
  cockpit's panels and the dashboard on the build's thread (their textures and meshes are CPU work), the rehearsal's
  pipelines made before the briefing (the menu's warm-up, below, and the rest of the rehearsal's materials), the combat
  view's and the vegetation's work cut into pieces. That is days of work, not a budget.
- The default path (the build on its thread under the briefing, the rest after it), checked for the requirement: T01, T05
  and T08 watched 25 s (T08's briefing ends at 17.4 s), 2 reps each: **0 dropped and 0 late frames** in 5 of 6 runs; T08
  rep 1 had 1 dropped and 1 late frame (a 146 ms gap) with the CPU at 85 % from other lanes; the audio generator reported 2
  underrun sample frames (0.09 ms) after the first frame in 4 of 6 runs, likely the generator's start (the first push follows the
  first frame; not verified). The Nitro Pack has no mission briefings (its smk/ holds only the intro and the credits), so
  there is no Nitro briefing to check.
- Found on the way: play's debug-build check `MainThread.Check` (4acee3e, for the map editor's workers) threw from the
  mission-build thread, so every briefed start in a debug build failed its build after the merge (release builds have no
  check). The build's thread is now `MainThread.Allowed` (cc993a9).

### The menu's warm-up (`[port] menu_warmup`, on)

- `MenuWarmup` (Shell/MenuWarmup.cs): a second after the main menu's first frame and after the garage's warm-up, the modern
  effects (`ModernFx`: the explosion, fire and smoke flipbooks into its static texture cache, their materials' pipelines,
  their code run once) on an unseen stage far below the menu, a part a frame, stopped when the menu is left.
- The menu's frames while it runs: in one piece (before the merge), 152-173 ms over 5 frames, the longest frame 100-111 ms,
  2 over 33 ms: a visible hitch. A part a frame (after): <!-- MENUFRAME -->
- The saving (before the merge, 2 reps, interleaved on/off, the launch hook 400 frames into the menu): a melee (M01, no
  briefing) 9.06 / 12.43 s with it against 12.69 / 13.16 s without (0.7-3.6 s, noisy); T01 from the menu with its briefing
  skipped at 3 s, 17.84 / 18.43 against 17.95 / 18.26 s (no saving: the rehearsal is after the briefing either way, and
  what it saves is ~0.4 s of pipelines). <!-- MENUSAVE -->

### Before and after (round 1 + round 2, the 4x pack)

| Start | Play tip (f8022fc, interleaved, round 1) | This branch before the merge | This branch after the merge (1280x960, contaminated CPU) |
|---|---|---|---|
| T01, no briefing | 11.89 s | 11.27 s | 11.86 / 12.38 s |
| M01, no briefing | 8.53 s | 8.27 s | 8.57 / 8.67 s |
| T05, no briefing | 13.36 s | 12.96 s | 13.57 / 13.62 s |
| T01, briefing skipped at 3 s | 17.85 s (card after it 14.3 s) | 16.49 / 18.00 s (card after 6.6 / 8.0 s); with the attach under it 13.87 / 13.56 s (2.7 s, stuttering) | not rerun (the lock budget) |
| T05, briefing skipped at 3 s | 20.84 s (17.4 s) | 16.66 / 18.35 s (6.8 / 8.2 s); under it 16.16 / 14.99 s (5.3 / 4.7 s) | not rerun |
| T01, briefing watched (25 s) | movie-first: ~28-31 s (card after 7.9-10.7 s, contaminated) | 38.82 / 39.40 s (card after 6.9 / 7.5 s); under it 31.92 / 31.96 s (0.01 s, stuttering) | 40.67 / 40.11 s (card after 8.6 / 8.0 s), clean |
| T05, briefing watched (25 s) | movie-first: ~33-35 s (card after 12.6-14.4 s) | 39.43 / 41.39 s (7.5 / 9.4 s); under it 32.30 / 31.92 s (0.01 s) | 40.55 / 39.19 s (8.6 / 7.2 s), clean |
| T08, briefing to its end (17.4 s) | - | - | 31.91 / 35.91 s (card after 7.4 / 11.4 s) |

The watched rows include the card's 6.5 s first (the user's card_first); the old movie-first order started the briefing at
0.6 s.

## Speculative loading from the menus

The user's four ideas: (a) load the player's last choices in the background at menu idle; (b) load along the click path
as it is revealed (mission list, mission, garage, go); (c) start on hover instead of click; (d) start when the mouse is
heading that way.

### What there is to win on this machine

- What a speculative load could make ahead is the build (`MissionBuilder.Build` and, on this branch, the cars' parts):
  mission-specific, 1.6-4.2 s of work on the main thread (T01 4.2 / 2.5 s with the pack / the originals, M01 3.0 / 1.6 s,
  T05 4.2 / 2.5 s), 5-7 s on a thread. The attach (5.8-10.3 s before this branch, 1.5-2.6 s less after it) needs the
  mission's scene and cannot be made in the menu: the shell's scene is freed on the click (`ReloadCurrentScene`) and the
  world's nodes, the sim and the views belong to the mission's.
- With `card_first` and a briefing (every trip mission but the training), the build is already hidden: it is done within
  the 6.5 s card or soon into the movie. A speculative build saves nothing there unless the briefing is skipped in its
  first seconds.
- Without a briefing (melee, the training, `--skip-movies`) the build is on the critical path: a finished speculative
  build would save its 1.6-4.2 s of a 7.8-15 s start, about a fifth.
- Shared between missions, made once a run, measured as a second load in the same run (loadtime.md, "A second
  mission"): about 2 s of T05's 11-13 s: the pipelines (the rehearsal's 150 compilations become 20: 0.4 s), the
  renderer's shadow atlas and the look's global state (0.3 s), the combat view's and the sim's first-run code (0.3 s),
  part of the ground materials' staging (0.3-0.4 s). Mission-specific: the terrain, the roads' bake, the placements and their textures (the mission's palette makes
  even a shared model's textures the mission's), the cars' parts (their materials too).
- What a guess holds: a built world before its attach is the mission's terrain meshes, road layers, placements and their
  textures; after a load the process holds 2.0 GB (M01) to 3.4 GB (T05) with the 4x pack and 0.7-1.7 GB of textures in
  video memory, most of it the world's. Godot uploads a texture when it is created, so a pending build holds its video
  memory too, beside the menu's. A wrong guess is a build thrown away (one core for 2-7 s, the pack's decodes on every
  core in bursts) and its memory back only at the next collection and heap trim.

### Engine and code constraints

- Godot's threaded resource loading (`ResourceLoader.LoadThreadedRequest`) is for `.tres`/`.tscn` resources; the game's
  world is built by our own code from the install's ZFS archive, so it is our thread, not Godot's loader, that would run
  ahead. That code is already safe off the main thread when nothing else uses the same builder (the briefing path proved
  it); the ZFS reader (`AssetVfs`) is per load, the pack reader per thread for a folder pack, single-threaded for a zip.
- Every texture and mesh the build makes is uploaded through the renderer when created: a calls queued from a thread wait
  for the main loop's next frame, so a speculative build in the menu runs at the menu's frame rate (the shell caps at the
  display's refresh: 60-144 Hz), several times slower than behind the card, unless the menu also uncaps its frames.
- The builder's materials are per mission (the mission's palette); its `MaterialLibrary` holds the props' maps until the
  main thread flushes them; a speculative build must be adopted whole by the next scene (a static handed across
  `ReloadCurrentScene`, as `Stash` and `EditorSession` already do) or dropped.
- The scene reload frees the shell's nodes; nothing of the menu is kept, so (b)'s garage step would also have to keep the
  garage's stage alive across the reload to reuse it.

### Power, heat and disk on a laptop

- A build is one core busy for 6-7 s plus bursts on all cores for the pack's decodes, and the GPU uploading textures: on a
  laptop at the menu that is fan noise and battery for a mission the player may not start. (c) and (d) multiply the
  wrong guesses: a pointer passing over the list would start and cancel builds. The pack's files (3.7 GB for all sizes) are
  read from disk; on a cold file cache that is the disk's time too, competing with the menu music's streaming.

### How games do it, plainly

- Warming shared assets at menu time is common: shader and pipeline caches compiled at the title screen or in the
  background (many recent PC releases compile shaders at the first start or in the main menu), common UI and character
  assets kept resident across levels, a level's streaming started once the player has committed (a "loading" screen that
  is really a commit point).
- Per-choice prefetch on hover is rare in games; it is a web and desktop technique (link prefetch on hover). Games that
  begin loading before the final confirm usually do it at a commit point that is almost certain: after the mission is
  picked and while a briefing or a loadout screen is up. I know of no measured study of hover prefetch in games and will
  not cite one.

### Recommendation

1. No per-choice prefetch for briefed missions (a-d): with the card first and the briefing, the build it could save is
   already hidden; what the player waits for after the briefing is the attach, which only the mission's scene can do.
2. For starts without a briefing (melee above all), (b) at its commit point pays modestly: the melee form's arena and car
   are decided before ENTER ARENA, and a build started when the form opens (or when the arena changes) would save the
   build's 1.6-3.0 s of M01's 7.8-10.8 s. Hover (c) and heading (d) add wrong guesses for at most a second's head start
   over (b), on a machine where a build is seconds of work: not worth it. (a), the last choice at menu idle, is (b)
   without the commit: a build per menu visit for a player who may not play the same arena.
3. Warm what every mission shares, once per run, at menu idle (item 6a): the pipelines and the ground materials; the
   second-load numbers in loadtime.md say what a mission start finds already made.
4. Measure and do item 9 (the attach under the briefing) before any speculation: it is the 6.8 s the player waits after
   the briefing today.
5. Not prototyped on this branch: (b) for melee needs the threaded build adopted across the scene reload (a static as
   `Stash` and `EditorSession` hold theirs, keyed by the mission, the car, the look and the pack size, dropped on any
   mismatch) and the menu's frames uncapped while it builds (a queued renderer call waits a menu frame); its saving is
   bounded above by the build's share measured here.
