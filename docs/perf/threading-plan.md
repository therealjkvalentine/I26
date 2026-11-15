# Threading plan: taking work off the main thread

The owner's question: "how could we rewrite things to be more threadable to speed up the processor bottleneck, cause we seem to
hit that a fair bit". This is the plan, not the rewrite: where the main thread's time goes, what can leave it, in what order, and
what each step should buy. Earlier measurements are in [costs.md](costs.md) and [plan.md](plan.md); this note adds the per-section
profile of 2026-11-15 and a trial of the engine's render thread.

Everything here was measured on the development desktop (a 4-core i7-7700K, a GTX 1080 Ti held at 180 W, Windows 10, Godot 4.7.2
.NET, Forward+), at 3440x1440 with the owner's own settings (the modern look, the world's textures 4x, the cars' and dash's 8x,
Render Scale 75 %, the mirrors on), uncapped, in the cockpit. The machine's other load is given beside every number, because it
moves them more than any setting does.

## The short version

**Where the time goes.** A frame on this desktop, quiet, in a busy scene, is 7.5 to 9.6 ms (104 to 133 fps); with one of the
four cores busy elsewhere, as when a lane runs, 10 to 14 ms (70 to 100 fps). The main thread spends the quiet frame like this:

| Part of the frame | ms a frame | Whose code |
|---|---|---|
| Our scripts (every node's per-frame step: the cars placed, the cockpit, effects, plants, terrain, sound) | 1.9 - 2.4 (3.1 with eight cars fighting) | ours |
| of which the simulation's tick itself | 0.07 - 0.12 (0.22 with eight cars) | ours (`src/I76.Sim`) |
| The engine drawing the frame (culling, building and recording 1,500-2,600 draws for the main view, the shadow map and the mirrors, and sending them) | 5.3 - 6.7 | Godot, on our main thread |
| Deferred calls, physics steps, pacing | 0.4 - 0.5 | Godot |

Three things follow, and they are not what we expected when the question was asked:

1. **The simulation is not the bottleneck.** A tick is a third to a half of a millisecond (under one with eight cars), and it
   runs on one frame in four or five. Putting it on its own thread would save 0.1 to 0.2 ms a frame. It is the hardest change
   on the list and nearly the least valuable.
2. **The largest single item is the engine's own drawing work, which Godot does on the main thread by default.** Godot has a
   switch that moves all of it to a second thread. It is marked experimental. Tried here as the game stands it did not crash,
   but it was slower in the cockpit and only 4-7 % faster in the chase view, because two things of ours fight it (the
   cockpit's live textures and the per-frame timing queries: section 1, "Fresh runs"). On paper it turns "scripts plus draw"
   into "the longer of the two", the largest gain on this list; it has to be made valid and measured again before anyone
   counts on it.
3. **Most of what hurts today is not architecture.** The 44-54 fps the owner sees in heavy scenes is this desktop's four cores
   shared with the lanes' builds and captures: the same scene, the same build, the same morning ran at 104 fps with the machine
   quiet, 87 fps with one other busy process and 67 fps with the processor full. A many-core machine removes that by itself. And
   the slow frames that make play feel uneven (the 1 % low, 50-60 fps while the average is over 100) are single bursts on the
   main thread: the radar's picture uploaded when its sweep steps, a garbage collection, a terrain or plant chunk arriving, one
   slow tick. Those are shed by tidier single-threaded code and by moving each burst to a worker, not by restructuring the game.

**What can move, and the order.** Cheapest high-value first; each is one mergeable lane. The fps are for T05's start on this
desktop, quiet, 104 fps today, as the processor alone would allow (the 1080 Ti caps this scene at about 130-140 fps whatever the
processor does).

| # | Step | Kind | fps before -> after (this desktop) | Effort |
|---|---|---|---|---|
| 1 | Shed the bursts: no garbage in the per-frame paths, a larger young-generation budget, the radar's upload by the part that changed, JIT warm-up behind the loading card, smaller per-frame budgets for arriving chunks | waste, one thread | average 104 -> 106; **1 % low 57 -> 75-85** | S each |
| 2 | The engine's render thread (`thread_model` = Separate) | threads (the engine's) | 104 -> up to 140-150 **if it holds** (unproven: as it stands, +4-7 % with worse pacing) | S to try, M to ship |
| 3 | Fewer draws: the mirrors' and the shadow map's lists, terrain chunks merged by distance, plants batched | waste, one thread | 104 -> 120 alone; lifts step 2's ceiling | M |
| 4 | Scripts on a diet: per-node scripts folded into managers, work skipped when nothing changed, the plants' tier split and the smoke's sort on workers | both | 120 -> 133 alone | M |
| 5 | The bass shaker's synthesis on a worker thread (**done**, 2.4) | threads (ours) | no change in the average; removes a 1-1.6 ms burst on one frame in ten | S |
| 6 | The cockpit's panels and radar composed off the main thread or on the card | threads (ours) | removes the 3.5-10 ms radar bursts that step 1 only shrinks | M |
| 7 | The simulation on its own thread, with a snapshot handed to the presentation | threads (ours) | +1 to +3 fps; tick bursts no longer stall a frame; a network client's re-simulation leaves the main thread | L |

**Where that lands.** On this desktop, quiet, steps 1-4 take heavy scenes from 100-130 fps to the card's ceiling (130-150 fps at
these settings) with 1 % lows near 80-90. On a Core Ultra 9 285K or a current X3D chip, whose single core is about twice this
one's (an estimate from public single-thread figures, not measured here), today's code would already allow about 180-220 fps in
these scenes with nothing else running, provided the graphics card can draw a frame in 5.5 ms; the steps above are then what
holds the 1 % low near the 180 Hz refresh rather than at half of it, and what keeps the main thread out of the way when the card
is fast enough for more. Step 7 is worth doing only after the others, and mostly for network play.

## 1. Where the main thread's time goes

### The instrument and the runs

`I76_FRAME_SECTIONS` (branch `perf/frame-spikes`, not yet on main) writes a row a frame: the wall time, the span of every node's
process step, each section inside it (the sim and its tick, the cockpit with its radar, each effect view, the plants, the
terrain, the voices, the shaker, ...), the deferred calls, the engine's draw (from `RenderingServer.FramePreDraw` to
`FramePostDraw`), the renderer's own measure of the main viewport's CPU and GPU time, the garbage collections and the managed
bytes allocated. The scenes are the bench's: T01's start, T05's start, and M01 as a melee with four computer cars, the guns held
and the target locked (five cars fighting); each 12 s, from 8 s after the load.

Taken from the `perf/frame-spikes` lane's runs of main `b77f5517` plus the hook on 2026-11-15, 08:16-08:20, the machine's other
processes using 4 % of it (the quietest runs this desktop has given). One run a scene; two runs of one build differ by about 3 %.

| Scene (quiet: others 4 %) | fps (mean) | frame ms (mean / p95 / p99) | 1 % low |
|---|---|---|---|
| T01 start | 121 | 8.3 / 10.8 / 14.0 | 58 |
| T05 start | 104 | 9.6 / 12.9 / 15.3 | 57 |
| Melee, five cars firing | 133 | 7.5 / 9.0 / 10.6 | 80 |

### Per subsystem, ms a frame (means)

| | T01 | T05 | Melee, 5 cars |
|---|---|---|---|
| **The sim tick** (`SimSession.Tick`; 24 Hz, so on one frame in four or five; 0.33 - 0.51 ms a tick) | 0.07 | 0.12 | 0.09 |
| **Our scripts, the rest:** | | | |
| the sim's host: input, the cars and their parts placed (interpolated), engine pitch, tyre sounds | 0.29 | 0.57 | 0.26 |
| the cockpit: panels, radar, mirror, head | 0.29 | 0.31 | 0.22 |
| effects: rounds and flashes, damage, smoke, sprites and lights, skid marks, brush impacts, tumbleweeds | 0.42 | 0.33 | 0.59 |
| the world: terrain levels, plants, the outside ground, scenery, trees | 0.31 | 0.44 | 0.10 |
| drop shadows, lamps, the cab's clip | 0.04 | 0.04 | 0.05 |
| sound: each voice's volume and cull; the bass shaker's synthesis and the wheel's forces | 0.14 | 0.17 | 0.14 |
| the HUD, overlays, cameras, UI, recorders | 0.14 | 0.17 | 0.15 |
| process steps not inside a section (small nodes, the engine's call into each script) | 0.26 | 0.28 | 0.26 |
| **Scripts and tick together** (`nodes`) | **1.96** | **2.42** | **1.85** |
| Deferred calls before the draw, and from the draw's end to the next frame (physics steps at 240/s, pacing) | 0.36 | 0.43 | 0.39 |
| **The engine's draw on the main thread** | **5.94** | **6.72** | **5.26** |
| of which the renderer's own count for the main viewport (culling, the render list, recording) and the frame's set-up | 2.29 | 2.86 | 1.62 |
| The card's time for the main viewport alone (the mirrors' are on top, about 1 ms a frame averaged) | 5.39 | 6.09 | 5.77 |
| Draws a frame | ~2,600 | ~2,000 | ~1,500 |

Reading it:

- **Our scripts are a quarter of the frame, and no part of them is large.** The largest sections are half a millisecond. The old
  heavy items are gone from the average: the drop shadows' casters (11-17 % of the main thread in round 5) are 0.04 ms, the bass
  shaker (2 ms a frame when Debug code was unoptimised) is 0.06-0.12 ms, the cockpit 0.2-0.3 ms.
- **The tick is 5 % of our scripts** (7 % with eight cars: 0.22 ms a frame, below). At a 60 Hz Simulation Rate with eight cars
  it would be about 0.5 ms a frame.
- **The draw is more than half the frame**, and only part of it is the renderer's counted work on the main view. The rest is the
  mirrors' and the shadow map's own culling and recording, the 2D pass and the submission. It is processor work, not waiting
  for the card: the fresh runs below drew the eight-car melee with the card's share cut from 7.4 to 1.8 ms and the frame did not
  shorten.

### The slow frames

The same runs' percentiles (T05; the melee is alike): the scripts' span is 2.1 ms at the median, 4.6 ms at p95, 6.4 ms at p99 and
14 ms at worst. The `perf/frame-spikes` lane attributed every frame with more than 7 ms of script time, across the three scenes:

| Cause | Share of the slow frames | Size |
|---|---|---|
| The radar's sweep step: the composed screen uploaded whole (4.6-6 MB with the 8x dash) | about 55 % | 3.5-10 ms, about five times a second |
| A young-generation garbage collection (gen 0 or gen 1; always stops every thread) | about 30 % | 3-8 ms, about one frame in a hundred |
| One slow sim tick | a few | 3-9 ms, once 23 and once 44 ms |
| The brush-impact field, a terrain level, a plant chunk's tier split | the rest | 2.5-8 ms, twice 21-27 ms |

and, below that threshold, the bass shaker fills its buffer in bursts: nothing on most frames, 1.0-1.6 ms on one frame in ten to
twenty.

Garbage: the main thread allocated 35-83 kB a frame; the sim's ticks were a third of it (105 kB a tick in T05), which the lane's
second commit takes to 14 kB a tick. A run with the young generation's budget raised to 64 MB (`DOTNET_GCgen0size`) had one
collection in its 12 s instead of nine to twenty.

### What the machine's other work does

The same build and scene, the same morning (T05's start):

| Other processes' share of the machine | fps | scripts ms | draw ms |
|---|---|---|---|
| 4 % | 104 | 2.4 | 6.7 |
| 17 % (one busy build) | 87 | 3.0 | 7.9 |
| 19 %, the processor at 100 % when the run began | 67 | 4.3 | 10.1 |

and from round 7's measurement of the owner's own profile (T06 by night, [plan.md](plan.md), "Debug code and the owner's frame
rate"): 77 fps with the machine at about 70 %, 56 fps at 98 %, scripts / renderer / sim 4.7 / 3.4 / 0.9 ms against 6.7 / 5.1 /
1.1 ms. Four cores with three of them busy leave the main thread waiting for a core; that, more than any subsystem, is the
"processor bottleneck" in the owner's sessions. Nothing in this plan fixes it on this desktop except running fewer lanes while
playing; sixteen or more cores fix it outright.

### Fresh runs for this plan

Two short batches on 2026-11-15 under the GPU lock (11:43-11:50 and 12:16-12:22), on the same build (main `b77f5517` plus the hook),
the same settings and window. The machine was **not quiet**: another lane's job held one of the four cores throughout (other
processes 19-23 % of the machine), and tests started during the later runs (36-60 %). Read them against each other and against
the loaded rows above, not against the quiet table.

| Scene (others' share of the machine) | fps | frame ms | scripts | of which the tick (per tick) | draw | renderer's count | card, main view |
|---|---|---|---|---|---|---|---|
| Melee, eight cars firing (M01, seven computer cars), 3440x1440 (20 %) | 102 | 9.8 | 3.14 | 0.22 (0.93) | 6.03 | 2.24 | 7.40 |
| the same at 1280x536 (39 %) | 100 | 10.0 | 3.76 | 0.27 (1.12) | 5.53 | 2.70 | 1.84 |
| T05 start (19 %) | 75 | 13.3 | 3.56 | 0.23 (0.72) | 9.20 | 4.18 | 7.67 |
| T06 start, by night (23 %) | 71 | 14.1 | 4.25 | 0.28 (0.82) | 9.02 | 4.65 | 7.70 |

- **The eight-car melee is held by the processor, not the card.** At 1280x536 the card's time for the main view falls from 7.4
  to 1.8 ms and the frame stays at 10 ms: scripts 3.1-3.8 ms and the engine's draw 5.5-6.0 ms, one after the other on one
  thread. So the draw row is processor work.
- **With eight cars the effects are the largest script section** (1.18 ms: the rounds, flashes and sprites of eight cars'
  guns), then the sim's host (0.45 ms) and the tick (0.22 ms a frame; 0.9 ms a tick, on a loaded machine).
- **T06 by night:** scripts 4.25 ms, the draw 9.0 ms, the tick 0.28 ms, with one to two cores busy elsewhere. It and T05's row
  are what the owner's sessions look like with a single lane running: 70-75 fps.

**The render thread, tried as it is** (`--render-thread separate`, same scenes, alternated with the rows above):

| Scene | fps without -> with | What happened |
|---|---|---|
| Melee, eight cars, 3440x1440 | 102 -> 77 | 1 % low 46 -> 9 |
| the same at 1280x536 | 100 -> 68 | |
| T05 start | 75 -> 33 | |
| T06 start | 71 -> 31 | others 60 % during the second run |

It ran every time without a crash (four runs of 32-38 s, particles and a mission load in each), and it was slower, for two
reasons that are ours, both printed by the engine:

- **The cockpit's live textures call the RenderingDevice from the main thread** (`LiveTexture`: `texture_update`,
  `texture_copy` and the mip kernel's compute list). With a render thread the engine refuses each call ("This function
  (texture_update) can only be called from the render thread"): about 16,000 errors with their backtraces in 12 s, the panels
  not updated, the cockpit's section 0.3 -> 3.5-17 ms and the main thread's garbage 41 -> 400-1,600 kB a frame from the error
  text alone.
- **Reading the renderer's timings every frame forces the two threads together** ("Call to
  viewport_get_measured_render_time_gpu causing RenderingServer synchronizations on every frame. This significantly affects
  performance"): the measuring hook does it, and so do the game's own frame-rate record (`PlayFpsRecorder`) and, when it is
  on, the target frame rate.

So the cockpit runs gave no figure for what the render thread buys; they gave the list of what must change first (2.1).

**The same trial in the chase view, without the measuring hook** (the eight-car melee, 1280x536 so the card does not limit, and
3440x1440; the game's `I76_FPS_LOG` lines, 28 s each, three pairs alternated; two of the four cores busy elsewhere):

| Melee, eight cars, chase view | fps without | fps with the render thread | frame-time deviation | worst frame every 2 s |
|---|---|---|---|---|
| 1280x536, pair 1 | 148 | 154 | 1.5 -> 5.4 ms | 19 -> 34 ms |
| 1280x536, pair 2 | 154 | 159 | 1.4 -> 5.5 ms | 20 -> 36 ms |
| 3440x1440 | 148 | 159 | 1.3 -> 5.2 ms | 21 -> 35 ms |

4-7 % faster on average and much less even. The two faults were still in it: the cockpit's live textures are updated in the
chase view too (37,000 engine errors a run, each printing a backtrace), and the game's own frame-rate record reads the
renderer's timing every frame. So this is a floor for the render thread, not its measure. No crash in seven runs in all.

One thing these runs show by the way: the same fight is 148-154 fps in the chase view and 100-102 in the cockpit on a
comparably loaded machine. The cockpit's extra views (the rear and door mirrors' scenes, the dash) are about 3 ms of the main
thread's frame, which is step 3's first target.

The share of the machine in these tables is of its eight logical processors, as the earlier lanes counted it: 20 % is about
one and a half of the four cores.

## 2. What can move, candidate by candidate

The fps figures assume T05's start on this desktop, quiet (9.6 ms, 104 fps), the card not limiting, and savings that add; on the
1080 Ti the card ends the gains at about 7-7.5 ms a frame (130-140 fps).

### 2.1 The renderer's processor side: Godot 4.7's threading as it is

Verified against the 4.7 documentation and source on 2026-11-15 (pages under `https://docs.godotengine.org/en/4.7/`):

- **`rendering/driver/threads/thread_model`** (ProjectSettings, `classes/class_projectsettings.html`). Values in 4.7's source
  (`core/os/os.h`): 0 unsafe (not supported in Godot 4), 1 single-safe (the default, and what the game runs), 2 separate thread.
  The command line's `--render-thread separate` selects it for one run. The documentation marks it experimental: "This setting
  has several known bugs which can lead to crashing, especially when using particles or resizing the window. Not recommended for
  use in production at this stage", and "Rendering on a thread may improve performance, but synchronizing to the main thread can
  cause a bit more jitter". **Certain.**
- **What Separate moves.** In `servers/rendering/rendering_server_default.cpp` every RenderingServer call from the main thread
  becomes a queued command, and the frame's draw itself (culling, the render lists, the RenderingDevice's recording and
  submission) runs on a render thread. That is the whole of this note's "engine's draw" row. **From the source; certain for
  4.7.**
- **What is already threaded without it.** Scene culling runs as WorkerThreadPool group tasks once a scenario has 1,000
  instances (`rendering/limits/spatial_indexer/threaded_cull_minimum_instances`; `servers/rendering/renderer_scene_cull.cpp`).
  The Forward+ renderer's render lists and draw recording are single-threaded in 4.7 (no pool use in
  `forward_clustered/render_forward_clustered.cpp`). Whether shadow passes, particles or skeletons use the pool is **not
  documented and was not confirmed.**
- **The scene tree** (`tutorials/performance/thread_safe_apis.html`): "Interacting with the active scene tree is not
  thread-safe"; other threads hand work over with `call_deferred`. Building nodes outside the tree on one thread and adding them
  on the main thread is allowed (what `MissionBuilder` and the plants' chunks do). **Certain.**
- **The servers** (same page): "Accessing servers from threads is supported. However, for the rendering and physics servers,
  thread-safe operation must be enabled in the project settings first", which for rendering is the Separate model again; and
  "avoid calling functions involving direct interaction with the GPU on other threads, such as creating new textures or modifying
  and retrieving image data". So with the default model, a worker may compute a buffer but the main thread must hand it to the
  RenderingServer. **Certain.**
- **Resources:** "Modifying a unique resource from multiple threads is not supported"; one thread loads and modifies. **Certain.**
- **Thread groups** (`Node.process_thread_group`, `classes/class_node.html`): a subtree's process steps run on a pool thread,
  and "accessing most functions in nodes outside the thread group is forbidden". Our per-frame nodes all reach outside
  themselves (the sim's state, the camera, the floating origin), so this is not a fit. Whether C# scripts work in sub-thread
  groups is **not stated; not tried.**
- **WorkerThreadPool** (`classes/class_workerthreadpool.html`): `add_task`, `add_group_task`, every task waited for; as many
  threads as logical cores by default. For C# the manual says only that "it may be easier to use the threading classes they
  support" (`tutorials/performance/using_multiple_threads.html`). The game already uses .NET's `Task.Run` and `Parallel.For`
  throughout (plants, terrain, radar frames, the loaders); this plan keeps that. The two pools do not know of each other, so on
  four cores they can crowd the main thread; on sixteen it does not matter.
- **Draw calls** (`tutorials/performance/optimizing_3d_performance.html`): Forward+ merges `MeshInstance3D` nodes that share a
  mesh and an opaque or alpha-tested material into one instanced draw by itself. `MultiMesh` draws thousands of instances in one
  call but is visible all or none (`classes/class_multimesh.html`), hence one per area; its buffer can be built on workers and set
  in one call (`RenderingServer.multimesh_set_buffer`). **Certain.**

.NET, for a Godot 4.7 project (.NET 8):

- **Garbage collection.** The workstation collector's background (concurrent) mode is on by default and helps only full
  collections; young-generation collections always stop every managed thread, the main one included, for the 3-8 ms measured.
  Server GC gives throughput for more threads and memory and longer young pauses on a client; not for the game. What works is
  allocating less on the main thread and a larger young budget (`DOTNET_GCgen0size`, measured above).
  `GCSettings.LatencyMode = SustainedLowLatency` only postpones blocking full collections, of which a mission has almost none.
- **Run-time settings.** Godot's documentation says nothing of `runtimeconfig` knobs. In 4.7's source
  (`modules/mono/mono_gd/gd_mono.cpp`) a game run from the editor binary starts the runtime from Godot's own
  `GodotPlugins.runtimeconfig.json`, so a project's settings **probably do not apply to a run from source**; environment
  variables (`DOTNET_...`) do, as the measured run shows. An export hosts the project's own assembly. **Needs a trial per
  setting, in an export and from source.**
- **The JIT.** Tiered compilation with dynamic PGO is .NET 8's default: code runs slow the first thirty times and is rebuilt in
  the background. First use of a path in a fight is a hitch. `Boot/JitWarm.cs` (prepare every method on spare cores) exists and
  **nothing calls it**. ReadyToRun (code compiled ahead, in the export) would remove most first-use JIT; Godot's export has no
  option for it (its .NET options are scripts, symbols and embedding), so whether a `PublishReadyToRun` property in the project
  is honoured **needs a trial**. NativeAOT: 4.7's source has a path for it; it is not a documented desktop export route and the
  game's reflection (the save-state audit) would need work. Not planned.

**The candidate: run with the render thread.** What it is: the 5.3-6.7 ms draw row. How: `thread_model` 2 (or the flag, for the
trial). What state it shares and how that is made safe:

- `LiveTexture` (the cockpit's panels, updated in every view) calls the RenderingDevice directly from the main thread; with a
  render thread the engine refuses those calls (measured), so they must be handed over (`RenderingServer.CallOnRenderThread`,
  with the bytes copied or the buffer's ownership passed). `SmokeLayer`'s compositor callback already runs on the render side
  and is already locked against the main thread's `Submit`; no error came from it in the trial.
- 36 places read a texture back (`GetImage` and the like): each becomes a wait for the render thread. The ones in play (captures
  and checks apart) must go or move behind the loading card.
- Nine classes use `FramePreDraw` / `FramePostDraw` for timing and one-frame-later logic; with a render thread these fire on the
  main thread at the sync point and no longer bracket the drawing.
- `PlayFpsRecorder`, `FrameTarget`, `GpuTimer` and the profilers ask the renderer for the viewport's measured time every frame;
  each ask is a full synchronisation of the two threads (the engine warns of it). They must sample rarely or use the wall clock.
- GPU particles (fire, wreck fire, some effects) are what the documentation's warning names. The loading card, the look change
  and the mission restart free and create thousands of resources at once, which is where a command queue's ordering bugs show.
- The frame profiler's and the frame-rate estimate's numbers change meaning (the draw is no longer in the main thread's frame).

Risk: high as a default (the engine says so), low as an opt-in `[port]` switch read at start. Effort: S for the trial, M to make
the game's own render-side calls correct, then a long soak. Expected: the frame becomes the larger of the main thread's part
(scripts + sync, about 3 ms) and the render thread's (5.3-6.7 ms), instead of their sum: for the eight-car melee in the cockpit
9.8 ms -> about 6 ms (102 -> 150-165 fps), for T05's start 9.6 -> about 7 ms (104 -> 140-150, where the 1080 Ti takes over).
**That is arithmetic, not a measurement**: the trial as the game stands gave +4-7 % with the faults above still in it, and
worse frame pacing. The first job of the lane is to remove the two faults and measure again; if the gain is then under about
20 %, stop there and leave the switch out.

### 2.2 Fewer draw calls (no threads needed)

Now: 1,500-2,600 draws a frame; the renderer's counted time is 1.6 ms at 1,500 and 2.3-2.9 ms at 2,000-2,600, and the mirrors'
and shadow map's shares sit in the draw row's remainder. From [costs.md](costs.md): the terrain 660 draws (one a chunk, plus
shadow-only casters), the plants 800 (a MultiMesh per chunk, species variant and tier), the sun's shadow map 900 (four splits
drawing the casters again), the rear mirror 630 (a second scene), the 20 km draw distance 800. Measured as wholes: the settings
sweep of 2026-11-14 (quiet, T05's start) gained 13 % with the door mirrors off and 24 % with every mirror off; the eight-car
melee here ran at 148-154 fps in the chase view against 100-102 in the cockpit. The views are the largest part.

How:

- The mirrors and the shadow map draw lists they do not need: cull masks and per-object ranges so that small scenery and far
  chunks are not in them (the code sets no visibility range anywhere).
- The terrain's far levels as fewer, larger meshes (a far chunk is its own draw however few its triangles).
- The plants: one MultiMesh a chunk and tier for all species (a texture array or atlas instead of a material a species), built
  on the worker that already makes the chunk.
- Static scenery placed once: left as it is (Forward+ already instances repeats).

Shared state: none new; all of it is built at load or on the existing chunk workers and attached on the main thread as now.
Risk: low to medium (the picture must not change: pixel-identical captures in both looks, per mission). Effort: M, in three
separable parts. Expected: 30-40 % fewer draws, 1.0-1.5 ms off the draw on whichever thread runs it; 104 -> about 120 fps alone.

### 2.3 Our scripts on a diet

Now: 1.9-2.4 ms in about 80 classes with a per-frame step, many of them one node per voice, per effect, per strip.

- Fold per-instance nodes into one manager each (one call from the engine into C# instead of dozens; the "not in a section" 0.26
  ms is mostly the calls themselves).
- Skip work whose inputs did not change: a car at rest need not have its parts placed again; the brush-impact field is decayed
  and uploaded as a 128x128 picture every frame, and could carry a time stamp a cell and be uploaded only when something is hit.
- The plants' tier re-split (per plant, main thread) and the smoke's sort and buffer fill: compute on a worker into a buffer,
  hand it over with one `multimesh_set_buffer` call.
- Rounds in flight: one MultiMesh instead of a pooled node a round.
- The per-frame budgets for arriving work (terrain meshes "three or 4 ms", eight plant chunks) were sized for 60 fps; at 180 fps
  a 4 ms budget is most of a frame. Budget by the frame target (about 1 ms).

Shared state: worker-built buffers are owned by the worker until handed over (a queue, as the plants' chunks do now). Risk: low.
Effort: M, many small changes. Expected: 2.4 -> about 1.6 ms; 120 -> 133 fps after 2.2, alone.

### 2.4 The bass shaker's synthesis off the main thread

Now: `BassShaker._Process` fills whatever the generator has room for (`TactileSynth.Fill`: per sample two band-passes, ten
equaliser bands, a high-pass, a low-pass, a limiter, the live grains and thumps); 0.06-0.12 ms a frame on average, in bursts of
1.0-1.6 ms. (Measured with the Dummy audio driver; with a real device the samples a second are the same.)

Godot's side (`classes/class_audiostreamgenerator.html`, `class_audiostreamgeneratorplayback.html`,
`class_audiostreamplayback.html`, `tutorials/audio/sync_with_audio.html`): audio is mixed on its own thread; a generator "expects
a script to generate audio data" and is polled with `get_frames_available` and filled with `push_buffer`; overriding the mixer's
own callback (`_mix`) "is not useful ... in GDScript or C#. Only GDExtension can take advantage of it". Pushing from a thread
other than the main one is **not documented**; the playback's ring buffer in the source has one reader and no lock, so one writer
thread would probably work, unsupported.

How, staying inside what is documented: a .NET worker runs the synth a few milliseconds ahead into a single-producer,
single-consumer ring of floats; the main thread's step shrinks to copying what the generator has room for and one `PushBuffer`
(microseconds). The synth's inputs are already gathered once a tick in `BassShaker.Tick` (engine speed, surface, wheel travel,
shots, explosions, hits): they become one small parameter block swapped atomically and a queue of events. Latency: the generator
already buffers 50 ms; the ring's lead (5-10 ms) is taken out of that buffer's fill, so the feel does not move. Risk: low (an
underrun is silence in a shaker, and the old path stays behind a switch). Effort: S. Expected: the burst goes; the average gains
0.1 ms. The CB voice chat's generator can use the same ring.

**Done** (branch `perf/shaker-off-main-thread`). `TactileStream` (I76.Formats, no Godot) is the worker: the main thread writes
a tick's inputs into one of 32 reused `TactileEvents` slots (a single-producer, single-consumer ring: nothing allocated) and
says whether the game is held; the worker steps each slot, fills, and pushes blocks of 256 frames to keep 50 ms queued. The
settings go over whole (the LFE Level's gain, tactile_eq, crossover, lfe_*), applied before the next sample; LFE Level 0 opens
nothing. The synth's voices are pooled, so it allocates nothing once warm. The worker pushes into the generator itself rather
than through a ring the main thread copies from: in Godot 4.7.2's source (`servers/audio/effects/audio_stream_generator.cpp`,
`core/templates/ring_buffer.h`) `push_buffer` and `get_frames_available` take no lock and touch only the playback's ring
buffer, whose one reader is the mixer; the main thread's push was already that unsynchronised hand-over, and the worker is the
only writer while it runs (stopped and joined before the player is freed: a scene reload, a change of output). The engine's
"Thread-safe APIs" page says nothing of audio; its rule is one thread modifying a resource at a time, which this keeps.
`I76_SHAKER_THREAD=0` keeps the old main-thread path.

Godot rounds a generator's length up to a power of two of frames: the 50 ms asked for is 4096 frames, 85 ms at 48 kHz (93 at
44.1), and the old path kept it full, so a tick's step was felt 80-90 ms later plus the output's own latency. The worker keeps
50 ms queued: about 35-40 ms sooner. The Dummy driver drains 4096 frames at a time and then sleeps 93 ms
(`audio_driver_dummy.cpp`), more than the ring holds, so it reports an underrun on every drain whichever path fills it: its
underrun counts and delays mean nothing; the real driver's are below.

Measured (`I76_SHAKER_TIMES`, the shaker's own main-thread time a frame: its process step and the ticks' gathering; LFE 100 %,
the owner's tactile_eq; 3440x1440, uncapped; the before and after runs interleaved; the machine shared with other lanes):

| Scene, Dummy driver (others' share of the machine, before / after) | main thread ms a frame, mean / p99 / max: before | after | fps before / after |
|---|---|---|---|
| T05 start (55 % / 19 %) | 0.113 / 0.77 / 2.77 | 0.004 / 0.020 / 0.029 | 77 / 98 |
| T05 start (17 % / 19 %) | 0.068 / 0.65 / 0.82 | 0.007 / 0.028 / 0.072 | 102 / 77 |
| Melee, five cars firing (26 % / 40 %) | 0.066 / 0.80 / 2.68 | 0.0035 / 0.015 / 0.10 | 126 / 119 |

Before, the frames with a fill (about eleven a second with this driver) took 0.55-0.8 ms of shaker time, up to 2.8; after, no
frame's exceeds 0.1 ms and the p99 is 0.015-0.028 ms (the 0.02 ms target is met at p99, not yet on every frame). The fps
columns follow the machine's load, not the change: the average saving is 0.06-0.11 ms a frame, about 1 % (104 -> 105 fps at
T05's quiet 9.6 ms frame; 200 -> 203 at a 5 ms one), and the slow frames lose their 0.6-0.8 ms. The worker's own passes take
0.33-0.45 ms each on its thread. The real driver's delay and underruns are still to be measured (the owner's hold, 2026-11-15).

The signal: a 20 s scripted drive (every source, the owner's tactile_eq) rendered through main's synth in place and through the
worker under a real-time mixer is byte-identical; the tests hold it to that for an 8 s drive, and check that a worker held for
200 ms leaves exact silence and resumes on the next sample.

Engine voices are not synthesised by us: each is an `AudioStreamPlayer3D` the engine mixes on its audio thread already; our part
is each voice's volume and cull a frame (0.03-0.06 ms), which 2.3's manager covers.

### 2.5 The cockpit, the radar and the dash

Now: 0.2-0.3 ms a frame on average after round 6's per-element updates, but the radar's sweep step composes the screen and
uploads it whole about five times a second, 3.5-10 ms each time with the 8x dash: over half of all slow frames.

How, in rising effort:

- Upload only the rectangle that changed (the sweep's wedge and trail), as the panels do. Single thread; the lane
  `perf/frame-spikes` has this in hand.
- Compose on a worker: the panels' pictures are plain arrays; a worker draws the next sweep step ahead (the radar's frames are
  already decoded ahead on one), the main thread uploads a finished buffer. The upload itself stays on the main thread (or the
  render thread with 2.1).
- Do the sweep on the card: the 62 frames as a texture array, the blips, plate and LED as a small layer over it, the screen
  composed by the dash's shader. Then nothing is uploaded when the sweep steps. The picture must stay texel-identical (the
  round-6 and round-7 checks: every upload read back).

Shared state: the composed buffer (owned by the worker until swapped); the blips' positions come from the sim's state on the main
thread and are passed in. Risk: medium for the shader route (two looks, three texture sizes). Effort: S / M / M. Expected: no
change in the average; the 1 % low rises the most of any single change.

### 2.6 Effects and particles

Now: 0.33-0.59 ms a frame, mostly already batched: smoke, rocket trails and chemical clouds are MultiMeshes drawn by one
compositor pass; fire is GPU particles; tumbleweeds and clutter are MultiMeshes. Still per node: a `MeshInstance3D` a round,
flashes and sprites, up to 48 skid strips each rebuilding its mesh when it grows, the brush-impact field's per-frame upload. The
smoke's sort and fill run on the main thread.

How: as 2.3 (rounds as a MultiMesh, the brush field by time stamp, the smoke's sort on a worker and one buffer call). Vegetation
fade and knock-down belong in the shader as per-instance data of the plants' MultiMeshes (a time stamp an instance), so that a
knocked bush costs one buffer write when it is hit and nothing after; how they are stored today was not read for this plan.
Risk: low. Effort: part of step 4. Expected: 0.2-0.3 ms (more with eight cars firing, where the effects are 1.2 ms).

### 2.7 Culling, LOD and streaming in scripts

Already off the main thread: terrain level meshes, plant chunks, the outside ground, the horizon, radar frames, the mission's
build. On the main thread still: choosing levels (0.2 ms on T05), attaching finished work under a budget, the plants' tier
re-split. The drop shadows' caster walk is solved (0.04 ms). Nothing here needs a new thread; 2.3's budgets and worker moves
cover it. Engine-side per-object visibility ranges would let the engine's (already threaded) culling drop far scenery instead of
our scripts or nobody.

### 2.8 The sim tick on its own thread

Now: 0.07-0.12 ms a frame (0.33-0.51 ms a tick at 24 Hz), 0.22-0.28 ms with eight cars or on a loaded machine, with rare ticks
of 3-9 ms and worse. The sim is engine-free and
deterministic, and a whole `SimSession` already ticks off the main thread in the listen server (`NetMeleeServer`, under one
lock), so the sim itself is ready. The presentation is not: only the cars' poses are copied a tick (`CarNode.Snapshot`);
everything else reads the live objects during the frame (`CarNode.Place` reads flags, health, throttle, the armoury and engine
speed; the cockpit, radar, HUD, combat and damage views read `Combat`, `Damage` and their event lists; the shaker and the wheel
read the player's car), and the tick calls back into Godot code from inside (`Damaging`, car contacts, `FrameStarting` /
`FrameDone`, the script host's sounds and cameras).

What a hand-off needs:

- **A tick's view, built at the end of each tick on the sim's thread:** for every car the pose (as now) and the fields the
  presentation draws from; the rounds in flight; the tick's events (combat, damage, impacts, landings, honks, script sounds and
  camera orders) as lists owned by the view. Two views are kept (previous and current) for interpolation; the main thread swaps
  to a finished one at the top of a frame and never touches the live world. This is the double buffer; it is the save-state's
  field list in spirit, and the same guard test idea applies (a new sim field the presentation reads must be in the view).
- **Callbacks become events** in the view. Anything that must stay synchronous with the tick (the mission script's own logic) is
  already inside the sim.
- **Commands go the other way** as they do now: the keys sampled by the main thread are the tick's `CarInput`.

Input latency, kept exact:

- The pacer stays on the main thread. A tick is requested with the input sampled at that instant, as now; the sim thread runs
  it; the main thread goes on with work that does not need the result (cameras, terrain, plants, sound), then takes the view
  before placing the cars, waiting if it is not ready. A normal tick is done long before. The frame that requested the tick
  draws from it, as today: not a frame later.
- **Early Tick** (a key change runs the next tick at once, drawn from the pose on screen) is the same request made early; the
  same join.
- **Draw Ahead** (the player's car drawn at the newest tick carried forward) reads the newest view and the pacer's remainder;
  unchanged.
- A tick that overruns (the rare 5-40 ms one) can be given a bounded wait (say 1 ms): the frame is then drawn from the last two
  views with the cars held at the newer one, and the tick's result appears a frame later. That trades a whole-frame hitch for a
  one-frame hold of the cars, only on those ticks. It is a deviation from "the frame waits for its tick" and wants a look before
  it is the default.

Determinism, replays, net play: a tick is a function of the state and that tick's inputs; which thread runs it changes neither.
The input trace, `StateHash` and the reference traces are untouched, and the determinism tests are this step's gate. A network
client's prediction and reconcile (rewind to the acknowledged state, re-step the pending inputs; several ticks in one frame after
a late packet) move to the sim thread with the session, which is where this step pays most: that burst leaves the main thread.
The host's and the server's sessions already run off it.

Risk: medium to high (every reader of live state has to move to the view; a missed one is a data race that shows as a rare wrong
frame, not a crash). Effort: L. Expected: 0.1-0.2 ms a frame at 24 Hz, 0.3-0.5 ms at 60 Hz; tick bursts and a client's
re-simulation off the main thread.

### 2.9 Inside the tick: parallel-for over cars, substeps, rounds

Not independent enough, and too small to pay:

- **One random stream.** `CrtRand` is shared the way the original shares `rand()`, and it is drawn conditionally inside each
  car's step (rough-ground damage, the steering's jitter), in damage, in the weapons and in the bots. The order of the draws is
  the simulation; the reference traces replay it. Cars stepped in parallel would have to draw in list order anyway.
- **Order-dependent passes.** The contact pass writes each car's record in pair order, "the last one written wins" (a wall beats
  a car); rounds damage cars as they fly, in list order; events are appended in order.
- **Size.** The whole tick is 0.33-0.51 ms for five to ten cars. A parallel-for's wake-up and join cost a tenth of that on a good
  day and more than all of it on a busy four-core machine.

The parts that are pure (the AI's steering target, each car's ground queries) are microseconds each. This would be worth
revisiting only for a mode with fifty or more cars; the form would be "compute in parallel, apply in list order", with the random
draws left in the apply phase.

### 2.10 What should not be threaded

- **The sim's rules** (2.9): order is the specification.
- **Anything that touches the active scene tree**: placing nodes, visibility, shader parameters on live materials. Workers build
  buffers and detached nodes; the main thread attaches and applies.
- **Texture creation and upload, readbacks** on workers, with the default thread model: the documentation forbids it and an
  export has deadlocked on it before (AGENTS.md, Pitfalls).
- **Input sampling and the tick pacer**: on the main thread, at the top of the frame, so latency stays where it was measured.
- **The physics steps** (240 a second for sound latency, 0.13-0.25 ms a frame): Godot's own setting to run physics on a thread
  restricts the API and the game uses the physics server only for the optional modern physics. Leave.
- **Per-frame node steps through Godot's thread groups**: every one of ours reads state outside its own subtree.

## 3. The order

Each step is one lane with its own branch and its test of success. "No change to what the player sees" means pixel-identical
captures at fixed ticks in both looks for steps 1, 3, 4 and 6, and the determinism tests (the Sim and Net suites, the CLI runs of
T01 / T03 / T05 / T10 / M01 line for line) for steps 1 and 7. Scenes: T01's start, T05's start and its mountain-pass fight
(`bench.py --scene t05`), T06 by night, M01 with four and with seven computer cars; each with the machine quiet and the load
written down.

1. **Shed the bursts** (S each, single thread). Merge `perf/frame-spikes` (the hook; the sim's garbage; the radar's partial
   upload). Then: the remaining per-frame allocations by section (the hook lists them); the young-generation budget set where
   the game starts the runtime, in an export and from source; `JitWarm` called behind the loading card; arrival budgets by frame
   target. Success: T05's 1 % low from 57 to 75 or better with the average unchanged or better; collections under two a minute
   in play; no frame over 16.7 ms in the melee window.
2. **The render thread, as a trial then an opt-in** (S, then M). First, behind a development switch, hand `LiveTexture`'s
   calls to the render thread and stop the per-frame timing queries; then this note's measurement again on a quiet machine
   over the full scene list with `--render-thread separate`, crashes and visual faults logged. If the gain is there: the rest
   of 2.1's list, a `[port]` switch (off by default), a soak in play. Success: the eight-car melee in the cockpit from 100 to
   140 fps or better with the machine quiet and the frame-time deviation no worse than
   without it; no engine error in the log; a two-hour soak (missions, restarts, look changes, window resizes, alt-tab) without a
   crash or a wrong picture. Anything less and the switch stays out of the build.
3. **Fewer draws** (M, three parts: the mirrors' and shadow map's lists; far terrain; plants). Success: 30 % fewer draws on T01
   and T05, the draw's time down 1 ms or more, zero differing pixels.
4. **Scripts on a diet** (M). Success: the scripts' span on T05 from 2.4 to 1.7 ms or less, p99 under 4 ms.
5. **The shaker on a worker** (S; **done**, 2.4). Success: its main-thread time under 0.02 ms on every frame; the output's waveform equal to
   the old path's for a recorded tick sequence; no underruns in a 10-minute run.
6. **The radar and panels off the main thread or on the card** (M). Success: no frame's cockpit time over 1 ms; every panel
   texel-identical (`I76_DASH_STATS=verify`).
7. **The sim thread** (L), only if network play or a 60 Hz rate with many cars shows tick time on the main thread worth
   removing. Success: determinism suites green; key-to-photon latency (the `I76_DRAW_LOG` method of docs/1997-parity.md) within
   1 ms of before for throttle and steering, Early Tick and Draw Ahead on and off; a client's reconcile no longer in the main
   thread's frame.

**The rough total.**

| | T05 start today | after 1, 3, 4 (one thread) | with 2 as well, if it holds |
|---|---|---|---|
| This desktop, quiet: main-thread frame | 9.6 ms | about 7.3 ms | main about 2.5 ms, render thread about 5 ms |
| fps the processor allows | 104 | about 135 | about 190 |
| fps seen (the 1080 Ti needs about 7-7.5 ms) | 104 | 130-135 | 130-140 |
| 1 % low | 57 | 80-90 | 80-90 |
| A 285K or X3D (single core about 2x; estimate): fps the processor allows | about 200 | about 270 | 350 or more |
| 1 % low there, if the bursts scale the same | about 110 | 160-180 | 160-180 |

The second-to-last row is an estimate from the ratio of public single-thread benchmark figures between a 7700K and those
processors (1.8 to 2.2 times), applied to today's measured main-thread frame; games with large scenes often gain more from an
X3D's cache than that ratio says, and less when memory-bound. It assumes a graphics card that draws these scenes in 5.5 ms at
3440x1440, about three times the 1080 Ti here; with a slower card the card is the limit and none of this shows. The melee's and
T01's frames are 15-20 % shorter than T05's throughout.

## 4. Architecture or waste?

Mostly waste and circumstance, measured:

- **Circumstance first.** With the machine quiet the heavy scenes already run at 104-133 fps; the owner's 44-54 is the same code
  on four cores shared with other work. The new machine fixes that with no change to the game.
- **Then per-frame waste a single thread can shed.** The slow frames are four or five named bursts (the radar's upload, young
  collections, a chunk arriving under a 4 ms budget, a first-time JIT, the shaker's fill). None needs a new architecture; each is
  an S or M change, and together they are the difference between a 1 % low at half the average and one near it. Draw calls the
  mirrors and the shadow map do not need, and per-node scripts, are waste of the same kind: 1-2.5 ms a frame.
- **One real architectural limit, and it is the engine's, not ours:** Godot draws on the main thread by default, and that is over
  half the frame. Its own switch moves it; the game is not ready for the switch (the trial), the engine calls it unstable, and
  its gain is unmeasured until the game's faults against it are fixed. So it is a trial with a go or no-go, then an option after
  a soak, or it waits for the engine to drop the experimental mark.
- **Our own architecture (the sim on the main thread) costs 0.1 ms a frame.** The split the code already has (an engine-free,
  deterministic sim; a presentation that draws it) is what would make a sim thread possible, and it is worth keeping clean for
  that day, but threading it is the last step, not the first.

So the honest order is: quiet machine or more cores; shed the bursts; fewer draws; thinner scripts; then the two thread moves
that pay (the engine's render thread, the shaker and radar workers); the sim thread last.

## What is uncertain

- The split of the draw row between processor work and waiting for the card: settled for the eight-car melee (processor work:
  the frame did not shorten at 1280x536), on a loaded machine;
  not repeated for T05 and T06 (the batch's time ran out).
- The render thread's stability in this game over hours; the documentation's warning stands until soaked.
- T06 by night and an eight-car melee on a quiet machine: measured here only with one to two cores busy elsewhere (70-102 fps);
  the quiet figures are
  for T01, T05 and the five-car melee. T05's mountain-pass fight was not re-profiled by section (round 5's and round 6's whole-route
  figures stand: scripts 3.0-3.4 ms, the renderer 2.0 ms).
- What the render thread buys once the game's own faults against it are fixed: the largest unknown in the plan.
- The bass shaker's cost was measured with the Dummy audio driver and the sound muted (the worker's numbers in 2.4 add the
  real driver, muted).
- The new machine's figures are scaled, not measured, and assume a card three times this one.
- Whether the project's run-time settings (the collector's budget, ReadyToRun) are honoured from source and in an export.
- Pushing audio from a worker thread directly: undocumented; the shaker relies on it, on the evidence of the engine's source
  (2.4). C# scripts in Godot's thread groups: undocumented, not tried, not relied on.
