# Frame costs of the modern look's features (play tip 4cac47e)

What each feature of the port costs in a frame, measured one at a time against a baseline with everything on, at
3440x1440 and 1920x1080, on the integration tip `play` = 4cac47e (pass 14 was being merged into `play` while this ran;
every number here is from 4cac47e plus the measuring hooks of branch `perf/frame-cost-survey`, nothing of pass 14). The
plan that follows from it is [plan.md](plan.md).

## How it was measured

- **Machine.** GTX 1080 Ti (driver 560.94) with its power limit at 180 W (the card held 1 800-1 900 MHz under load, so
  the cap did not bite much), a 3440x1440 display, Windows 10, Godot 4.7.2 .NET, Forward+ (Vulkan). The machine was
  shared with other lanes' builds and captures throughout (see Noise).
- **Scene.** The T01 start, `--mission t01 --drive --seed 1 --skip-movies --skip-cutscenes`, the player's car standing with
  its engine running while the script's cars play the opening (the barn, the water tower, the plants and the cliffs in
  view; the mirror shows the road behind), cockpit view (F1, the 3D dash, the mirror in Full). A chase-view set, a firing
  set (`--hold-fire`, the machine guns), a smoke set (`I76_SMOKE=0.4`: every car held at 40 % health so the damage smoke
  runs) and the loading runs are separate tables.
- **Window.** 8 s after the mission counted as loaded (the first tick done; the effects rehearsal behind the card is over
  by then) the profiler measures every frame for 10 s, then quits. Vsync off, Max FPS off, `--gpu-profile` on (Godot's
  per-pass GPU timestamps), audio muted. 3440x1440 is the game's borderless fullscreen at the desktop size; 1920x1080 is
  its Resolution row's centred window.
- **Instrument.** The game's own `FrameProfiler` (`I76_FRAME_PROFILE=1`), extended on this branch with a timed window
  (`I76_FRAME_PROFILE_SKIP_MS` / `_RUN_MS`), a per-frame CSV (`_CSV`), a screenshot of the last frame (`_SHOT`) and the
  VRAM monitors in its series. Per frame: the GPU time of each pass (shadows, depth pre-pass, SSAO, opaque, sky, MSAA
  resolve, transparent, post, 2D) and of each viewport (the mirror is its own), `cpu.wall` (the time between frames),
  `cpu.nodes` (every node's `_Process`), `cpu.render` (the renderer's culling and command recording), `cpu.sim`, draw calls,
  primitives, texture and total video memory. A run's value is the median over its ~10 s of frames; a configuration's value
  is the median over its three runs. Noise is reported as the max-min spread over the three runs.
- **Isolation.** Each run in a scratch user folder (`APPDATA` overridden; the texture packs, the bake cache and the shader
  cache junctioned in from the live one, nothing written to the player's data), `shell.cfg` written from the baseline plus
  the one change, the engine started fresh for every run. Before a run the harness waited for the GPU to be under 10 %
  with no other Godot process, sampled other Godot processes every 2 s during the run and discarded and reran any run that
  saw one, and from 15:00 left 20 s idle after every run for the other lanes' idle checks.
- **Baseline ("everything on").** The modern look; Terrain Detail High, Shadow Detail High (shadow map and SSAO),
  Anti-Aliasing 4x, Vegetation High at the 60 % default density, draw distance 20 km, Hide Map Edge Cliffs On, HD
  textures 4x (menu art too), Hood Finish Gloss, lighting calibration On, death variety On, brush impacts On, MG tracers
  bolts, brake lamps held, mirror range Full, the 3D dash, external instruments On, car interiors On, palette grade On,
  crash blur On, LFE Level 100 % (the bass-shaker synth running, output Stereo), effects at 100 %, sim rate 24 Hz.
- **Toggles.** The `[port]` keys and Graphic Detail bytes of `shell.cfg` where a key exists; `--1997` for the look; the
  code's `I76_*` tuning hooks where there is no key (`I76_SSAO=0`, `I76_SMOKE_LAYER=0`, `I76_HIDE=...`); two hooks added
  on this branch for features that had no switch: `I76_SMOOTH_KEY=0` (the signed-distance outlines) and the mirror
  follow-ups `I76_MIRROR_RATE=2` / `I76_MIRROR_SSAO=0`. Anisotropy is a project setting
  (`textures/default_filters/anisotropic_filtering_level`), changed by hand between runs for its row.
- **Harness.** `tools/frame-cost/` on this branch: `bench.py` (one configuration), `matrix.py` (the sets, reps interleaved,
  a `fill` mode that reruns only what lacks a clean run), `loadtime.py`, `contamination.py` (marks runs another GPU user
  overlapped, from a GPU telemetry log and from the run's own numbers against its siblings), `report.py` and `assemble.py`
  (these tables). Each run leaves `NAME.RES.N.{log,csv,png}` in the output folder and a row in `results.tsv` with its start
  and end time, the GPU's state before it, the other GPU users seen and the profile's result line.

## The baseline frame

<<TABLE:base>>

The frame is GPU-bound at 3440x1440 (wall 17.4 ms against 16.9 ms of GPU) and about balanced at 1920x1080. The 1997 look
of the same scene is 2.3 / 1.6 ms GPU and 710 draw calls; the chase view (F2) of the same start is 21.7 ms at 3440x1440
(no dash hiding the ground, more terrain in view). The mirror viewport (gpu.vp0) is a fifth of the GPU time; the depth
pre-pass is as large as it is because SSAO makes it write normals (see the Features table and plan.md, item 1). Hiding
the scene's parts outright (`I76_HIDE`, the table further down) puts the terrain at 5.6 of the 10.7 ms at 1080p, the
vegetation at 1.0, the horizon ring at 0.65 and the placements at 0.35.

## Features, sorted by frame cost

Each row is the baseline minus the configuration with that one feature off or turned down (positive = what the feature
costs when on). GPU ms is the whole frame's GPU time; "CPU main-thread" is `cpu.nodes + cpu.render`; "Frame (wall)" is
the time between frames (vsync off, so the slower of CPU and GPU plus the present). Draw calls and VRAM are deltas too.
Rows under about 0.3 ms GPU are within the survey's noise (below) and should be read as "no measurable cost".

<<TABLE:features>>

**The top eight**, by what they cost in the frame (GPU, 1440p / 1080p):

1. Ambient occlusion (the SSAO that Shadow Detail High turns on): 5.0 / 3.1 ms, almost all of it the depth pre-pass
   having to run every material for normals (the SSAO pass itself is 0.5 / 0.2 ms).
2. The rear-view mirror (a second scene render every frame): 2.9 / 3.4 ms and 630 draw calls; its Full range alone 0.9 /
   1.3 ms over the original's 100 m. Its follow-ups (below) recover 2.1-2.4 ms of it.
3. Anti-aliasing 4x: 2.1 / 1.3 ms over Off, 0.5 / 0.5 ms over 2x, and 366 / 151 MB of VRAM.
4. Sun shadows (the shadow map, SSAO aside): 1.3 / 1.1 ms and ~900 draw calls; 2 splits instead of 4 save 0.45 ms of it,
   a 600 m reach 0.2 ms.
5. Terrain Detail High (the 1.25 m level): 1.0 / 0.9 ms. (The terrain as a whole is half the frame; its shader, not its
   anisotropy: 16x, 8x and 4x measure the same to 0.01 ms.)
6. Vegetation High: 0.7 / 0.9 ms and 838 draw calls (0.1 / 0.4 ms over Low); 100 % density costs 0.8 / 0.0 ms over the
   60 % default.
7. Draw distance 20 km: 0.0 / 0.5 ms but ~800 draw calls (the horizon ring alone 0.65 ms at 1080p).
8. HD textures 4x: no frame time at either resolution, but 723 MB of VRAM over the originals and about 3 s of a T01 load
   (1.3 s over 2x), in the next table.

And one negative cost worth knowing: the 3D dash saves 2.0 / 0.7 ms against the 2D dash, because it is opaque geometry
drawn first and the terrain behind it fails the depth test.

Everything else is within noise: smooth keyed edges (6-20 MB of VRAM, no frame time), brush impacts, lighting
calibration, hood finish, death variety (costs only at a death), the smoke depth layer (0.0 ms with every car smoking:
below), map edge, tracers (0.00 ms while firing both machine guns: below), the bass-shaker synth, palette grade, car
interiors, the paved roads' 16x anisotropy.

## HD texture packs: 4x, 3x, 2x, off

What a player gets for each pack size: the load time of a mission (the loading card appearing to the loading card gone,
warm caches: a first run of each pack warmed the disk cache and was dropped, then three runs, the median; the briefing
plays after the card on this tip, never over the load, so this is the load alone; the pack's disk size and file count
beside it), the VRAM the textures take in T01, and the frame time at each resolution. On disk the packs are BC7 .dds
with mipmaps, the cockpit art at full scale in every pack, which is why 2x is more than a quarter of 4x.

<<TABLE:hd>>

In a player's words: <<HD_WORDS>>

## The other scenes and the follow-ups

The mirror's follow-ups update the mirror on alternate frames, which makes a run's frames bimodal; the medians in those
two tables lean one way or the other, so the plan quotes frame means for them: at 3440x1440 the mean frame is 15.4 ms with
`I76_MIRROR_RATE=2` (-1.6 against the baseline's 16.9), 16.0 with `I76_MIRROR_SSAO=0` (-0.9), 14.8 with both (-2.1), 14.6
with both and the original's 100 m range (-2.4), 14.0 with no mirror (-2.9); at 1920x1080: 8.6 (-2.1), 9.3 (-1.4), 8.1
(-2.6), 7.8 (-2.9), 7.3 (-3.4). The mirror viewport's own time goes from 3.5 ms to 2.4 ms without SSAO and to 1.7 ms with
the 100 m range as well.

<<TABLE:sets>>

## Noise

<<TABLE:noise>>

The GPU numbers are the reliable ones: the per-pass timestamps do not see other processes' CPU load, and the
configurations' GPU spreads are a few tenths of a millisecond at 1440p. The CPU numbers are not: the machine ran other
lanes' builds and captures the whole time (CPU load 50-75 % when sampled), and `cpu.wall` and `cpu.nodes` at 1920x1080
spread by 6-13 ms between runs of the same configuration. Read a CPU delta only when it is larger than the spread beside
it, and prefer the 1440p column. The GPU clock sat at 1 800-1 900 MHz under the 180 W cap for the runs that recorded it
(`gpu_mhz` in the result line), so the cap did not move the numbers between runs. `--gpu-profile` itself costs a little
(timestamp queries and a printed block every few hundred frames) and is on for every row alike.

## Rows left out and rerun

Runs during which the sampler saw another GPU user (the death-capture lane's short captures, 45 of them between 14:02
and 14:40 and more after 17:00; other lanes' Godot runs earlier; from 15:30 a CUDA upscaler, which the sampler counts
since then), runs with no result, and runs the telemetry log or their own numbers give away (`contamination.py`: a GPU
telemetry sample over 20 % within 25 s of the run at a moment no run of ours was on the GPU, or a GPU time 0.8 ms or more
over the configuration's other runs; the whole 1920x1080 third rep between 14:35 and 15:25 fell to this, when a CUDA job
the Godot-only sampler could not see shared the GPU) were left out of every median above and the configuration run again
until it had three clean runs. Every configuration in the tables has three. The rows left out (end time, configuration,
resolution, rep, scene, why):

<<TABLE:reruns>>
