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

| Resolution | Frame (wall) ms | GPU total ms | of which mirror viewport | depth pre-pass | opaque | shadows | SSAO pass | sky | transparent | CPU nodes ms | CPU render ms | Draw calls | Primitives (k) | VRAM textures MB | VRAM total MB | GPU MHz | reps |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 3440x1440 | 17.4 | 16.9 | 3.5 | 5.5 | 7.6 | 0.81 | 0.46 | 0.94 | 0.65 | 2.7 | 2.9 | 2645 | 3851 | 1719 | 1990 | 1797 | 3 |
| 1920x1080 | 11.1 | 10.7 | 3.4 | 3.4 | 4.6 | 0.78 | 0.24 | 0.69 | 0.42 | 2.2 | 2.6 | 2582 | 3786 | 1383 | 1618 | 1835 | 3 |

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

| # | Feature (on, as in the baseline) | Toggled by | GPU ms 1440p | GPU ms 1080p | CPU main-thread ms 1440p | 1080p | Frame (wall) ms 1440p | 1080p | Draw calls 1440p | 1080p | VRAM MB 1440p | 1080p | reps | Note |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | The modern look as a whole (vs the 1997 look) | `--1997`: the 1997 look, everything of the port's off | +14.63 | +9.14 | +2.77 | +2.83 | +12.76 | +7.67 | +1929 | +1876 | +1221 | +994 | 3/3 |  |
| 2 | Ambient occlusion (Shadow Detail High's SSAO) | I76_SSAO=0: shadows kept, SSAO off | +4.96 | +3.11 | +0.60 | +0.30 | +4.79 | +2.81 | -1 | -1 | +124 | +54 | 3/3 | the depth pre-pass must write normals: the terrain's whole material runs twice |
| 3 | Rear-view mirror (3D cockpit, Full) | Rear Mirror Off | +2.88 | +3.43 | -2.01 | -0.04 | +2.67 | +3.29 | +632 | +623 | +33 | +7 | 3/3 | a second scene render at 1024x256 every frame |
| 4 | Anti-aliasing MSAA 4x (vs Off) | Anti-Aliasing Off | +2.07 | +1.33 | -0.39 | +0.11 | +1.99 | +1.23 | -2 | -1 | +366 | +151 | 3/3 |  |
| 5 | 3D dash (vs the 2D dash) | dash_3d=false | -1.99 | -0.74 | -1.46 | +0.13 | -2.04 | -0.79 | +39 | +41 | -0 | +25 | 3/3 | negative: the 3D dash hides terrain pixels behind it |
| 6 | Sun shadows (Shadow Detail Low vs Off) | Shadow Detail Off | +1.34 | +1.05 | +0.30 | +0.73 | +1.27 | +1.23 | +934 | +889 | +57 | +32 | 3/3 | measured against shadow_low (SSAO already off); see the note |
| 7 | Mirror range Full (vs the original's 100 m) | mirror_range=original | +0.93 | +1.33 | -1.54 | +0.01 | +0.89 | +1.28 | +202 | +211 | +0 | +0 | 3/3 |  |
| 8 | Terrain Detail High (the 1.25 m level) | Terrain Detail Low | +1.03 | +0.91 | -0.89 | -0.48 | +0.87 | +0.83 | +0 | -2 | +25 | +25 | 3/3 |  |
| 9 | Vegetation High (vs Off) | Vegetation Off | +0.74 | +0.93 | -0.10 | +1.22 | +0.69 | +0.89 | +838 | +815 | +38 | +38 | 3/3 | 60 % density |
| 10 | Vegetation density 100 % (vs the 60 % default) | vegetation_density=100 | +0.83 | +0.02 | +1.23 | +0.08 | +0.88 | +0.38 | +102 | +105 | -25 | -0 | 3/3 | a cost, not a saving: 100 % minus 60 % |
| 11 | External instruments (2D panels) | Ext. Instrum. Disp. Off | -0.70 | +0.39 | -0.91 | -0.07 | -0.79 | -0.02 | +0 | -1 | +25 | +25 | 3/3 |  |
| 12 | Anti-aliasing MSAA 4x (vs 2x) | Anti-Aliasing 2x | +0.55 | +0.48 | -0.35 | -2.30 | +0.52 | -2.15 | -1 | +2 | +210 | +75 | 3/3 |  |
| 13 | Hood finish Gloss (vs Matte) | hood_finish=matte | -0.52 | -0.01 | -3.01 | +0.20 | -1.00 | +0.01 | +0 | +0 | +25 | +0 | 3/3 |  |
| 14 | Draw distance 20 km (vs the original's 600 m) | draw_distance=0 | +0.04 | +0.47 | +1.11 | -0.19 | +0.02 | +0.37 | +807 | +776 | +25 | +0 | 3/3 |  |
| 15 | MG tracers as bolts (vs the line) | mg_tracers=original | -0.45 | +0.06 | -0.66 | -1.08 | -0.55 | -0.20 | -1 | -1 | +0 | +25 | 3/3 | no firing in the standing scene; see the fire table |
| 16 | Palette grade | palette_grade=false | -0.09 | +0.45 | -0.41 | -3.24 | -0.17 | -3.50 | +1 | +1 | +27 | +12 | 3/3 |  |
| 17 | Vegetation High (vs Low) | Vegetation Low | +0.13 | +0.43 | -1.04 | +0.44 | +0.15 | +0.46 | +341 | +335 | +25 | +0 | 3/3 |  |
| 18 | Death variety | death_variety=off | -0.43 | +0.00 | -1.13 | +0.18 | -0.53 | +0.03 | -1 | -1 | +25 | +25 | 3/3 | nothing died in the scene: costs only at a death |
| 19 | Smoke depth layer | I76_SMOKE_LAYER=0 | -0.42 | +0.23 | -1.12 | -0.22 | -0.50 | -0.06 | +3 | +1 | +20 | +9 | 3/3 | no smoke in the standing scene; see the smoke table |
| 20 | Bass-shaker synth (LFE 100 %) | lfe_level=0 | -0.34 | +0.07 | -0.45 | +0.11 | -0.47 | -0.01 | -1 | -1 | +0 | +25 | 3/3 | CPU only, main thread |
| 21 | Car interiors | car_interiors=false | -0.33 | +0.30 | +0.08 | -2.47 | -0.34 | -1.45 | -1 | +1 | +0 | +25 | 3/3 |  |
| 22 | HD textures 4x (vs 2x) | hd_texture_size=2 | -0.30 | +0.05 | -1.63 | +0.08 | -0.50 | +0.07 | +2 | -1 | +125 | +100 | 3/3 |  |
| 23 | HD textures 4x (vs the install's originals) | hd_textures=false | +0.07 | +0.28 | -1.52 | +0.01 | -0.05 | +0.25 | +0 | +0 | +723 | +748 | 3/3 | VRAM, not frame time |
| 24 | Lighting calibration (original colours) | lighting_calibration=off | -0.20 | -0.01 | -1.36 | +0.18 | -0.88 | +0.02 | +1 | -0 | +0 | +26 | 3/3 | a one-time solve at load; no per-frame work |
| 25 | Hide Map Edge Cliffs On | map_edge=off | +0.05 | +0.16 | -1.61 | -0.31 | -0.08 | +0.01 | -2 | +0 | +25 | +0 | 3/3 | a uniform branch in the terrain vertex shader |
| 26 | HD textures 4x (vs 3x) | hd_texture_size=3 | -0.13 | +0.12 | -1.56 | +0.02 | -0.26 | +0.10 | -1 | -1 | +37 | +63 | 3/3 |  |
| 27 | Smooth keyed edges (signed-distance outlines) | I76_SMOOTH_KEY=0 (this branch's hook) | -0.10 | -0.01 | -2.13 | +0.11 | -0.26 | +0.00 | -1 | -2 | +6 | +6 | 3/3 | one R8 field per keyed texture |
| 28 | Brush impacts | brush_impacts=off | -0.09 | +0.06 | -4.17 | +0.03 | -1.23 | +0.01 | +0 | +1 | +0 | +25 | 3/3 | nothing was fighting in the scene |

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

| Pack | On disk | VRAM textures MB 1440p | 1080p | GPU ms 1440p | 1080p | Frame (wall) ms 1440p | 1080p | Load T01 s (card up to gone; of which the build) | Load M01 s |
|---|---|---|---|---|---|---|---|---|---|
| 4x | 1608 MB, 6563 .dds | 1719 | 1383 | 16.93 | 10.71 | 17.36 | 11.13 | 11.6 (11.6-11.8; build 3.9) | 8.5 (8.5-8.5; build 2.4) |
| 3x | 1220 MB, 6563 .dds | 1681 | 1320 | 17.06 | 10.59 | 17.62 | 11.03 | 10.8 (10.6-10.8; build 3.3) | 8.1 (8.0-8.2; build 1.9) |
| 2x | 923 MB, 6563 .dds | 1594 | 1283 | 17.23 | 10.66 | 17.86 | 11.06 | 10.3 (10.2-10.4; build 2.7) | 7.5 (7.5-7.6; build 1.6) |
| off | the install's own textures | 996 | 635 | 16.86 | 10.44 | 17.42 | 10.88 | 8.7 (8.6-8.8; build 2.4) | 5.9 (5.9-6.0; build 1.3) |

In a player's words: 4x over 2x costs +1.3 s of load on T01 and +1.0 s on M01 (4x loads T01 in 11.6 s, 2x in 10.3 s, the originals in 8.7 s), 125 MB more VRAM (723 MB over the originals), and -0.30 ms of GPU frame time at 3440x1440 and +0.05 ms at 1920x1080, which is inside the noise: the pack size is a VRAM and loading choice, not a frame-rate one. 3x sits between on every count.

## HD textures 8x (cockpit & cars) over 4x

The partial 8x pack (`tex-world-8x/`: the cockpit's own art but the mission maps, and every car texture with its dented
stages, 2,556 BC7 .dds, 2.86 GB on disk; the 4x serves the rest, HdPackFolders.Chain) against the 4x alone, branch
`feat/hd-pack-8x` on play 187e255, 2026-11-05, GTX 1080 Ti at 180 W, the same harness (`tools/frame-cost/bench.py`'s
`run_once`, everything-on profile, cockpit view, 8 s after the load then 8 s profiled; the load is the loading card up to
gone with `I76_BUILD_TRACE=1`). Per configuration a warm-up run (dropped) then three, reps interleaved 4x / 8x; the median,
the range in brackets. The machine was shared with other lanes' CPU work (the absolute load times are a second over the
4x row above; the 4x-to-8x step is what this measures).

| Mission, resolution | Pack | VRAM textures MB | VRAM total MB | Load s | GPU ms | Frame (wall) ms |
|---|---|---|---|---|---|---|
| T01, 3440x1440 | 4x | 1660 (1660-1660) | 1913 (1912-1913) | 12.94 (12.85-13.15) | 11.25 (11.20-11.40) | 12.62 (12.55-12.73) |
| | 8x over 4x | 2195 (2170-2195) | 2476 (2434-2480) | 15.18 (15.14-15.28) | 11.38 (11.29-11.46) | 12.65 (12.60-12.83) |
| | delta | **+536** | **+563** | **+2.2** | +0.13 | +0.03 |
| T05, 3440x1440 | 4x | 2039 (2039-2064) | 2331 (2330-2371) | 15.05 (14.99-16.02) | 14.33 (14.20-14.42) | 15.58 (15.51-15.76) |
| | 8x over 4x | 2649 (2624-2649) | 2972 (2932-2972) | 17.76 (16.87-18.24) | 14.33 (14.27-14.35) | 15.59 (15.54-15.69) |
| | delta | **+610** | **+641** | **+2.7** | -0.00 | +0.01 |
| T01, 1920x1080 | 4x | 1324 (1324-1349) | 1546 (1546-1592) | 11.73 (11.61-11.77) | 6.06 (6.04-6.13) | 8.22 (7.90-8.34) |
| | 8x over 4x | 1860 (1860-1860) | 2116 (2114-2117) | 13.69 (13.56-13.78) | 6.56 (6.24-6.89) | 8.10 (8.06-8.26) |
| | delta | **+536** | **+570** | **+2.0** | +0.50 | -0.12 |
| T05, 1920x1080 | 4x | 1703 (1703-1728) | 1968 (1963-2004) | 13.58 (13.57-14.73) | 7.82 (7.67-7.93) | 9.62 (9.51-14.97) |
| | 8x over 4x | 2313 (2313-2313) | 2606 (2606-2606) | 15.74 (15.70-15.81) | 7.52 (7.49-7.93) | 9.32 (9.30-9.35) |
| | delta | **+610** | **+638** | **+2.2** | -0.31 | -0.30 |

The 8x is a VRAM and loading choice like the other sizes: +0.54 GB of textures in T01 and +0.61 GB in T05 at either
resolution (the same textures), the game's whole use then 2.5 GB (T01) to 3.0 GB at 3440x1440; +2.0 to +2.7 s of load
no frame time (GPU deltas from -0.3 to +0.5 ms, inside or at the edge of
the runs' spread, the frame unchanged). The rest of the video memory (total minus textures: render targets, buffers) is
220-320 MB and grows ~30 MB with the 8x. So the 8x fits a 6 GB card with half of it spare (on a 4 GB card the 3 GB of T05 leaves
too little for the desktop and the driver), and a 16 GB Apple-silicon Mac; but an A/B of the same frames (the dash, the gauges,
the player's car at 3 m and close up, a car at 10 m, at 3440x1440 and 1920x1080) shows no difference, so the first start keeps
the 4x (HdTextures.FirstStartSize) and the 8x is only the row's, when its folder is there.

## The other scenes and the follow-ups

The mirror's follow-ups update the mirror on alternate frames, which makes a run's frames bimodal; the medians in those
two tables lean one way or the other, so the plan quotes frame means for them: at 3440x1440 the mean frame is 15.4 ms with
`I76_MIRROR_RATE=2` (-1.6 against the baseline's 16.9), 16.0 with `I76_MIRROR_SSAO=0` (-0.9), 14.8 with both (-2.1), 14.6
with both and the original's 100 m range (-2.4), 14.0 with no mirror (-2.9); at 1920x1080: 8.6 (-2.1), 9.3 (-1.4), 8.1
(-2.6), 7.8 (-2.9), 7.3 (-3.4). The mirror viewport's own time goes from 3.5 ms to 2.4 ms without SSAO and to 1.7 ms with
the 100 m range as well.

**The scene's parts hidden (I76_HIDE)** (1920x1080, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 10.71 | 3.41 | 4.58 | 0.78 | 0.42 | 2.24 | 2.64 | 11.13 | 2582 | 1383 |
| hide_terrain | 3 | 5.11 (-5.61) | 0.95 (-2.45) | 1.79 (-2.79) | 0.54 (-0.24) | 0.41 (-0.01) | 2.03 (-0.21) | 1.78 (-0.86) | 6.78 (-4.35) | 1920 (-662) | 1383 (+0) |
| hide_veg | 3 | 9.68 (-1.04) | 2.99 (-0.42) | 4.17 (-0.41) | 0.70 (-0.08) | 0.41 (-0.01) | 2.44 (+0.20) | 3.77 (+1.12) | 11.63 (+0.49) | 1766 (-816) | 1358 (-25) |
| hide_horizon | 3 | 10.06 (-0.65) | 3.27 (-0.14) | 4.18 (-0.40) | 0.78 (-0.01) | 0.41 (-0.00) | 2.15 (-0.09) | 2.49 (-0.16) | 10.58 (-0.55) | 2472 (-110) | 1358 (-25) |
| hide_placements | 3 | 10.37 (-0.35) | 3.34 (-0.07) | 4.41 (-0.18) | 0.78 (-0.01) | 0.42 (+0.01) | 2.21 (-0.03) | 2.23 (-0.41) | 11.17 (+0.04) | 2340 (-242) | 1383 (+0) |

**Shadows and SSAO** (1920x1080, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 10.71 | 3.41 | 4.58 | 0.78 | 0.42 | 2.24 | 2.64 | 11.13 | 2582 | 1383 |
| shadow_low | 3 | 7.52 (-3.20) | 0.71 (-2.70) | 4.41 (-0.17) | 0.76 (-0.02) | 0.40 (-0.02) | 2.08 (-0.16) | 2.49 (-0.15) | 8.48 (-2.65) | 2567 (-15) | 1329 (-54) |
| shadow_off | 3 | 6.47 (-4.25) | 0.73 (-2.68) | 4.23 (-0.35) | 0.00 (-0.78) | 0.30 (-0.12) | 1.77 (-0.48) | 2.08 (-0.56) | 7.26 (-3.88) | 1678 (-904) | 1297 (-86) |
| ssao_off | 3 | 7.60 (-3.11) | 0.71 (-2.69) | 4.45 (-0.13) | 0.77 (-0.02) | 0.41 (-0.01) | 2.08 (-0.16) | 2.50 (-0.14) | 8.32 (-2.81) | 2583 (+1) | 1329 (-54) |
| shadow_dist_600 | 3 | 10.54 (-0.18) | 3.38 (-0.02) | 4.48 (-0.10) | 0.78 (-0.01) | 0.42 (-0.00) | 2.30 (+0.06) | 2.76 (+0.11) | 10.95 (-0.18) | 2441 (-141) | 1383 (+0) |
| shadow_splits_2 | 3 | 10.27 (-0.45) | 3.36 (-0.04) | 4.47 (-0.11) | 0.52 (-0.26) | 0.42 (-0.00) | 2.24 (-0.00) | 2.71 (+0.07) | 10.68 (-0.46) | 2501 (-81) | 1383 (+0) |

**The mirror's follow-ups (this branch's hooks)** (1920x1080, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 10.71 | 3.41 | 4.58 | 0.78 | 0.42 | 2.24 | 2.64 | 11.13 | 2582 | 1383 |
| mirror_off | 3 | 7.29 (-3.43) | 2.15 (-1.25) | 3.17 (-1.41) | 0.41 (-0.37) | 0.38 (-0.04) | 2.04 (-0.21) | 2.89 (+0.24) | 7.84 (-3.29) | 1959 (-623) | 1376 (-7) |
| mirror_original | 3 | 9.38 (-1.33) | 2.65 (-0.75) | 3.90 (-0.68) | 0.80 (+0.02) | 0.41 (-0.01) | 2.20 (-0.04) | 2.70 (+0.05) | 9.86 (-1.28) | 2371 (-211) | 1383 (+0) |
| mirror_rate2 | 3 | 7.71 (-3.00) | 2.51 (-0.90) | 3.43 (-1.15) | 0.65 (-0.13) | 0.40 (-0.02) | 2.05 (-0.19) | 2.65 (+0.01) | 10.11 (-1.03) | 2167 (-415) | 1383 (+0) |
| mirror_nossao | 3 | 9.34 (-1.37) | 2.33 (-1.08) | 4.47 (-0.11) | 0.78 (-0.01) | 0.41 (-0.01) | 2.16 (-0.08) | 2.63 (-0.01) | 9.94 (-1.20) | 2582 (+0) | 1381 (-2) |
| mirror_both | 3 | 7.71 (-3.01) | 2.26 (-1.15) | 3.44 (-1.14) | 0.64 (-0.15) | 0.40 (-0.02) | 2.08 (-0.16) | 2.69 (+0.04) | 9.45 (-1.68) | 2168 (-414) | 1381 (-2) |
| mirror_both_orig | 3 | 7.68 (-3.03) | 2.24 (-1.17) | 3.37 (-1.22) | 0.67 (-0.11) | 0.40 (-0.01) | 2.06 (-0.19) | 2.72 (+0.07) | 8.82 (-2.31) | 2161 (-421) | 1381 (-2) |

**The mirror's follow-ups (this branch's hooks)** (3440x1440, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 16.93 | 5.49 | 7.61 | 0.81 | 0.65 | 2.66 | 2.93 | 17.36 | 2645 | 1719 |
| mirror_off | 3 | 14.05 (-2.88) | 4.30 (-1.19) | 6.54 (-1.07) | 0.43 (-0.38) | 0.60 (-0.05) | 3.16 (+0.51) | 4.43 (+1.50) | 14.69 (-2.67) | 2013 (-632) | 1686 (-33) |
| mirror_original | 3 | 16.00 (-0.93) | 4.86 (-0.62) | 7.09 (-0.52) | 0.81 (+0.00) | 0.64 (-0.01) | 3.33 (+0.68) | 3.79 (+0.86) | 16.48 (-0.89) | 2443 (-202) | 1719 (+0) |
| mirror_rate2 | 3 | 15.16 (-1.77) | 4.89 (-0.60) | 6.95 (-0.66) | 0.71 (-0.10) | 0.63 (-0.02) | 2.46 (-0.20) | 3.02 (+0.09) | 16.02 (-1.35) | 2219 (-426) | 1719 (+0) |
| mirror_nossao | 3 | 15.97 (-0.96) | 4.58 (-0.91) | 7.54 (-0.06) | 0.80 (-0.00) | 0.64 (-0.01) | 2.50 (-0.16) | 2.89 (-0.04) | 16.41 (-0.95) | 2646 (+1) | 1717 (-2) |
| mirror_both | 3 | 14.78 (-2.15) | 4.42 (-1.07) | 6.90 (-0.70) | 0.71 (-0.10) | 0.63 (-0.02) | 2.44 (-0.22) | 3.01 (+0.08) | 15.40 (-1.97) | 2222 (-423) | 1717 (-2) |
| mirror_both_orig | 3 | 14.58 (-2.35) | 4.41 (-1.08) | 6.66 (-0.95) | 0.72 (-0.09) | 0.64 (-0.01) | 2.46 (-0.19) | 3.05 (+0.12) | 15.06 (-2.31) | 2218 (-427) | 1691 (-28) |

**Anisotropic filtering (project.godot, by hand)** (3440x1440, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 16.93 | 5.49 | 7.61 | 0.81 | 0.65 | 2.66 | 2.93 | 17.36 | 2645 | 1719 |
| aniso_8x | 3 | 16.94 (+0.01) | 5.46 (-0.02) | 7.59 (-0.02) | 0.81 (+0.00) | 0.64 (-0.01) | 2.52 (-0.13) | 2.73 (-0.20) | 17.41 (+0.05) | 2646 (+1) | 1719 (+0) |
| aniso_4x | 3 | 16.94 (+0.01) | 5.48 (-0.01) | 7.58 (-0.03) | 0.82 (+0.02) | 0.64 (-0.01) | 2.66 (+0.00) | 2.96 (+0.03) | 17.40 (+0.04) | 2646 (+1) | 1719 (+0) |

**Damage smoke (every car held at 40 % health)** (1920x1080, cockpit scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 10.71 | 3.41 | 4.58 | 0.78 | 0.42 | 2.24 | 2.64 | 11.13 | 2582 | 1383 |
| smoke | 3 | 10.80 (+0.08) | 3.38 (-0.02) | 4.48 (-0.10) | 0.78 (-0.00) | 0.42 (+0.00) | 2.56 (+0.32) | 2.75 (+0.10) | 11.25 (+0.12) | 2584 (+2) | 1383 (+0) |
| smoke_layer_off_smoke | 3 | 10.69 (-0.03) | 3.39 (-0.02) | 4.46 (-0.12) | 0.78 (+0.00) | 0.59 (+0.17) | 2.52 (+0.27) | 2.55 (-0.10) | 11.15 (+0.02) | 2580 (-2) | 1348 (-35) |

**Firing the machine guns (--hold-fire)** (1920x1080, fire scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 10.64 | 3.35 | 4.58 | 0.77 | 0.46 | 2.65 | 2.63 | 11.19 | 2618 | 1358 |
| tracers_line | 3 | 10.64 (-0.00) | 3.35 (+0.00) | 4.57 (-0.01) | 0.77 (+0.00) | 0.46 (-0.00) | 2.58 (-0.07) | 2.58 (-0.05) | 11.17 (-0.02) | 2614 (-4) | 1383 (+25) |

**Chase view (F2)** (3440x1440, chase scene, medians over reps, deltas against this set's base)

| Config | reps | GPU total | depth | opaque | shadows | transparent | CPU nodes | CPU render | Frame (wall) | Draws | VRAM MB |
|---|---|---|---|---|---|---|---|---|---|---|---|
| base | 3 | 21.69 | 6.07 | 13.02 | 0.47 | 0.36 | 3.00 | 3.44 | 22.10 | 2421 | 1712 |
| hd_off | 3 | 21.34 (-0.35) | 5.92 (-0.15) | 12.74 (-0.29) | 0.48 (+0.01) | 0.36 (-0.00) | 2.73 (-0.27) | 3.46 (+0.02) | 21.76 (-0.34) | 2422 (+0) | 989 (-723) |
| veg_off | 3 | 20.67 (-1.02) | 5.74 (-0.33) | 12.39 (-0.63) | 0.43 (-0.04) | 0.35 (-0.02) | 2.02 (-0.98) | 2.36 (-1.08) | 21.10 (-1.00) | 1911 (-510) | 1699 (-12) |
| sdf_off | 3 | 22.16 (+0.47) | 6.04 (-0.03) | 13.42 (+0.40) | 0.46 (-0.01) | 0.35 (-0.01) | 2.96 (-0.04) | 3.64 (+0.20) | 22.55 (+0.45) | 2421 (+0) | 1680 (-32) |
| shadow_low | 3 | 15.85 (-5.84) | 0.62 (-5.45) | 13.07 (+0.04) | 0.44 (-0.03) | 0.35 (-0.02) | 2.93 (-0.07) | 3.50 (+0.05) | 16.35 (-5.75) | 2401 (-20) | 1565 (-147) |
| terrain_low | 3 | 19.94 (-1.75) | 5.38 (-0.69) | 12.00 (-1.02) | 0.45 (-0.02) | 0.35 (-0.01) | 2.84 (-0.16) | 3.37 (-0.08) | 20.36 (-1.74) | 2422 (+1) | 1712 (+0) |
| look1997 | 3 | 2.40 (-19.29) | 0.17 (-5.90) | 1.24 (-11.78) | 0.00 (-0.47) | 0.44 (+0.08) | 1.02 (-1.98) | 1.28 (-2.17) | 3.51 (-18.59) | 752 (-1669) | 497 (-1214) |
| map_edge_off | 3 | 21.70 (+0.01) | 6.05 (-0.02) | 13.01 (-0.02) | 0.46 (-0.01) | 0.36 (-0.01) | 2.96 (-0.04) | 3.47 (+0.02) | 22.16 (+0.05) | 2421 (+0) | 1686 (-25) |

## Noise

- 3440x1440: over 42 configurations with 2-3 reps, the max-min spread over reps is median 0.39 ms (p90 0.68) for GPU total, 1.63 ms (p90 3.77) for the main thread's work, 0.47 ms (p90 0.92) for the wall frame time, 3 draw calls.
- 1920x1080: over 46 configurations with 2-3 reps, the max-min spread over reps is median 0.37 ms (p90 0.57) for GPU total, 1.80 ms (p90 6.43) for the main thread's work, 1.57 ms (p90 8.75) for the wall frame time, 2 draw calls.

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

- 2026-11-04T12:15:11 base 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:19:26 base 1920x1080 rep 1 (chase): other Godot seen
- 2026-11-04T12:21:38 base 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:22:56 base 3440x1440 rep 1 (cockpit): listed in contaminated.tsv
- 2026-11-04T12:24:54 look1997 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:37:44 ssao_off 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:41:52 interiors_off 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:43:59 interiors_off 3440x1440 rep 1 (cockpit): other Godot seen
- 2026-11-04T12:45:22 interiors_off 3440x1440 rep 1 (cockpit): listed in contaminated.tsv
- 2026-11-04T12:46:00 base 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T12:46:44 base 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T12:47:41 base 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T12:50:28 sdf_off 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T12:51:01 sdf_off 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T12:51:36 sdf_off 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T13:01:09 lfe_off 3440x1440 rep 2 (cockpit): other Godot seen
- 2026-11-04T13:14:35 map_edge_off 3440x1440 rep 3 (cockpit): other Godot seen
- 2026-11-04T13:15:52 map_edge_off 3440x1440 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T13:24:02 base 1920x1080 rep 1 (cockpit): other Godot seen
- 2026-11-04T13:30:35 veg_density_100 1920x1080 rep 1 (cockpit): other Godot seen
- 2026-11-04T13:59:13 hide_terrain 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T13:59:47 hide_terrain 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:00:22 hide_terrain 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:01:12 hide_horizon 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:01:45 hide_horizon 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:05:59 hide_horizon 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:07:36 hide_placements 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:25:14 hide_placements 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:25:55 hide_placements 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:26:51 diag 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:30:03 diag 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:30:44 diag 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T14:32:38 look1997 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:36:17 sdf_off 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:37:16 sdf_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:38:03 brush_off 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:40:13 brush_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:40:55 lighting_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:43:40 hood_matte 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:44:32 death_off 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:45:28 death_off 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:46:47 death_off 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:48:03 hd_3x 1920x1080 rep 3 (cockpit): other Godot seen
- 2026-11-04T14:50:11 hd_2x 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:52:45 hd_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:53:45 veg_low 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:55:23 veg_density_100 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:56:03 smoke_layer_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:57:09 map_edge_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T14:59:52 mirror_original 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:00:34 instruments_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:01:33 lfe_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:02:19 shadow_low 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:02:57 shadow_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:04:03 ssao_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:05:05 terrain_low 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:06:13 msaa_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:10:17 hide_terrain 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:14:22 hide_terrain 1920x1080 rep 2 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:15:30 hide_horizon 1920x1080 rep 2 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:16:38 hide_placements 1920x1080 rep 2 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:17:58 diag 1920x1080 rep 2 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:19:11 brush_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:22:40 death_off 1920x1080 rep 3 (cockpit): listed in contaminated.tsv
- 2026-11-04T15:39:36 mirror_nossao 1920x1080 rep 1 (cockpit): other Godot seen
- 2026-11-04T15:43:08 hide_terrain 1920x1080 rep 2 (cockpit): other Godot seen
- 2026-11-04T17:40:23 base 3440x1440 rep 3 (chase): listed in contaminated.tsv
- 2026-11-04T17:42:06 veg_off 3440x1440 rep 3 (chase): listed in contaminated.tsv
