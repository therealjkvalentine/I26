# Frame-cost plan: the modern look's features, ranked by what they cost and what can be saved

Companion to [costs.md](costs.md), which holds the measurements (branch `perf/frame-cost-survey`, measured on the play tip
4cac47e, before pass 14; a GTX 1080 Ti at a 180 W power cap, Godot 4.7.2 Forward+). Numbers below are that survey's
medians: GPU ms at 3440x1440 / 1920x1080 in the T01 start, cockpit view, everything on. The baseline frame is 16.9 / 10.7 ms
GPU, 2 600 draw calls, 1.7 / 1.4 GB of textures; the 1997 look of the same scene is 2.3 / 1.6 ms. The chase view of the
same start is 21.7 ms at 3440x1440: no dash to hide the ground behind it, and more terrain in view.

Each item: the engine mechanism, what our code does, the change, the expected saving, the risk to faithfulness (the
original's behaviour, docs/deviations.md), and how to measure it (the configuration names are `tools/frame-cost/matrix.py`'s;
costs.md has the tables).

## Where the frame goes

Terrain is half of it: hiding the terrain node takes 5.6 ms off the 10.7 ms frame at 1080p (its depth pre-pass 2.5 ms, its
opaque pass 2.8 ms, 660 draws), vegetation 1.0 ms, the horizon ring 0.65 ms, the placements 0.35 ms. The depth pre-pass is
as large as it is because SSAO makes it write normals, so the terrain's full material runs twice. The mirror viewport is a
fifth of the GPU time at both resolutions (3.5 / 3.4 ms) because it is a second scene with the same draw calls, not because
of its pixels. On the CPU the frame is 2.7 ms of nodes and 2.9 ms of render-command recording at 1440p, and most of the
frame's 2 600 draw calls are terrain chunks and vegetation MultiMeshes drawn into the main view, the mirror and four shadow
splits.

## Ranked changes

### 1. Ambient occlusion: its own row, off by default (5.0 / 3.1 ms, 29 % of the frame)

- Mechanism. SSAO is a half-resolution compute pass (0.46 / 0.24 ms by itself), but turning it on makes Forward+ render the
  depth pre-pass with a normal-roughness target, so every opaque material runs its fragment shader in the pre-pass as well
  as in the opaque pass. The terrain shader (terrain_modern.gdshaderinc: 12 samplers, up to ~25 anisotropic reads a pixel)
  covers most of the screen; its pre-pass goes from 0.7 to 3.4 ms at 1080p and from 0.8 to 5.5 ms at 1440p (`ssao_off`).
- Ours. `ApplyModernQuality`: Shadow Detail High = shadow map + SSAO, Low = shadow map alone. The pad calls the row Shadow
  Detail, so a player turning shadows up pays for AO without knowing.
- Change. An Ambient Occlusion row of its own (Off / On) on Graphic Detail's World page, Off by default; Shadow Detail High
  keeps only the longer shadow reach. (The first-start default `DefaultGraphics[0x6]` was Low then, High in round 5 and Low again since round 6, docs/perf/costs.md; the survey's
  baseline took High as "everything on".)
- Saving. 5.0 ms at 1440p, 3.1 ms at 1080p (16.9 -> 12.0; 10.7 -> 7.6). In the chase view 5.9 ms.
- Faithfulness. None: the original has no ambient occlusion. The modern look loses the contact darkening under cars and
  shrubs by default; it stays one row away.
- Measure. `ssao_off` and `shadow_low` against `base`; gpu.depth is the tell.

### 2. The rear-view mirror: the original's cadence and reach (2.1-2.4 of its 2.9 / 3.4 ms, measured)

- Mechanism. A SubViewport is a whole second scene render: culling, depth pre-pass (with normals when SSAO is on), opaque,
  sky, its share of the shadow passes. Its cost scales with draw calls more than with pixels: the mirror is 1024x256 (5 % of
  the main view's pixels) but adds 630 of the frame's 2 600 draw calls and 1.2 ms of pre-pass at 1440p.
- Ours. `Cockpit.ApplyMirrorMode/Mirror`: UpdateMode.Always in the modern look, far clip 20 km with `mirror_range=full`
  (the modern look's default), the world's environment (SSAO included), cull mask all but the cockpit and smoke layers.
  The 1997 look updates it every second frame at 256x64 with a 100 m far clip, as the original did.
- Change, measured with this branch's hooks (costs.md, "The mirror's follow-ups"; frame means, as alternate frames make the
  medians bimodal): (a) update every second frame in the modern look too (`I76_MIRROR_RATE=2`: one expression in `Mirror()`,
  `_mirrorTick` already exists): 1.6 / 2.1 ms; (b) its own Environment, the world's duplicated with SsaoEnabled = false
  (`I76_MIRROR_SSAO=0`; nobody sees AO in a 160-pixel-tall picture): 0.9 / 1.4 ms, the mirror viewport's own time 3.5 ->
  2.4 ms; (a)+(b): 2.1 / 2.6 ms; (a)+(b)+the original's 100 m range: 2.4 / 2.9 ms, against 2.9 / 3.4 ms for no mirror at all.
  Then (c) a cull mask without the horizon ring and the far vegetation tier for the rest.
- Faithfulness. (a) and the 100 m range move toward the original (every second frame; 100 m). The original's reach is already
  offered as `mirror_range=original`.
- Measure. gpu.vp0 (the mirror viewport's GPU time), draws and frame means in the cockpit scene: `mirror_off` and
  `mirror_original` are the bounds, `mirror_rate2`, `mirror_nossao`, `mirror_both`, `mirror_both_orig` the steps.

### 3. The terrain shader by distance (1-2 ms; the terrain is 5.6 of 10.7 ms at 1080p)

- Mechanism. Fragment cost = reads x pixels. The modern terrain does triplanar untiled material blending (`sample_untiled`:
  two array reads of albedo and two of normal per projection, three projections where the slope asks), the ground texture
  three ways, the road layer, the marks' field, the asphalt grain (five reads near paved roads), the sward, the cavity map,
  all through `textureGrad`. Beyond a few hundred metres none of it resolves: a 1.25 m texel is under a pixel past ~300 m at
  1440p. Anisotropy is not the cost: 16x, 8x and 4x measure the same frame to 0.01 ms (`aniso_8x`, `aniso_4x`), so the paved
  roads' 16x stays.
- Ours. One shader, every branch keyed on material weights, not on distance; Terrain Detail High adds the 1.25 m geometry
  level within 100 m (1.0 / 0.9 ms, mostly the extra vertices in the pre-pass and the shadow splits).
- Change. Three distance tiers in `fragment()` on `dist`: near (as now) to 250 m; middle (one planar read of albedo and
  normal, no untiled second read, no strata projections, no asphalt grain) to 700 m; far (albedo at the broad normal, no
  normal map, no marks) beyond. The mirror and the shadow passes take the far tier. The cross-fade hides in the LOD's morph
  bands (MorphStart = 40/360/730 m).
- Saving. 1 to 2 ms at 1440p (the terrain's opaque share is ~4-5 ms; the far half of the screen in pixels is most of it);
  with SSAO on, the same again in the pre-pass; more in the chase view (opaque 13 ms there).
- Faithfulness. None: the 1997 look has its own shader. A visible change only if the tier boundary shows.
- Measure. `hide_terrain` (the whole terrain's cost), `terrain_low`, the per-pixel scaling between the two resolutions.

### 4. Shadows: splits, reach and casters (0.5-1 ms of 1.3 / 1.1 ms; ~900 draw calls)

- Mechanism. A directional light with 4 PSSM splits renders the casters up to four times; every object within the max
  distance that casts is drawn into each split it touches. Draw calls, not pixels, are the cost: shadows add ~900 of the
  frame's 2 600 draws at the T01 start (`shadow_off` against `shadow_low`).
- Ours. Max distance 1500 m, 4 splits, blur 1, soft-low, atlas 4096 (`SetMode1997`); every terrain chunk, placement and
  near shrub tier casts.
- Change. 2 splits (`Parallel2Splits`): measured 0.45 ms and 80 draws at 1080p (`shadow_splits_2`: the shadow pass 0.78 ->
  0.52 ms, the rest in culling); max distance 600 m: 0.18 ms and 140 draws (`shadow_dist_600`); placements and vegetation
  casting only within 400 m (a `CastShadow` flip by distance, as the plants already do at 60 m) for the rest of the draws.
- Saving. 0.6-0.9 ms and 300-500 draws.
- Faithfulness. None (the original has projected drop shadows only, kept in the 1997 look). Far shadows soften with 2 splits;
  check a mesa's shadow at 600-800 m before and after.
- Measure. gpu.shadows, draws and cpu.render in the `shadowtune` set.

### 5. MSAA 4x -> 2x as the recommendation for 1440p+; keep 4x available (0.5 / 0.5 ms; 2.1 / 1.3 ms vs Off)

- Mechanism. 4x MSAA multiplies the depth pre-pass and resolve work on the alpha-tested foliage (every covered sample runs
  the alpha test) and the G-buffer size (366 MB of VRAM at 1440p, `msaa_off`).
- Ours. `Graphics[0x8]` Anti-Aliasing Off / 2x / 4x, default 4x (`DefaultGraphics`).
- Change. No code: the recommendation for a 1440p+ screen on a 1080 Ti-class card is 2x; a first start on a GPU with under
  6 GB could default to 2x (`RenderBackend` knows the adapter). Keep 4x for the look.
- Saving. 0.5 / 0.5 ms and 210 / 75 MB at 2x; 2.1 / 1.3 ms and 366 / 151 MB at Off.
- Faithfulness. None (the original had none; the 1997 look runs without).
- Measure. `msaa_2x`, `msaa_off`.

### 6. Vegetation: fewer nodes, the far tier by visibility range (0.3-0.5 ms CPU, 0.3 ms GPU, 500 draws)

- Mechanism. Each MultiMeshInstance3D is a draw per pass (pre-pass, opaque, the splits it casts into, the mirror). 12 000
  plants at the T01 start are ~700 nodes (`I76_VEG_STATS`: 38 chunks, 700 nodes, 95 % of the plants in the far tier);
  `veg_off` removes 838 draws for 0.7 / 0.9 ms GPU, `hide_veg` 1.0 ms at 1080p; 100 % density costs 0.8 ms at 1440p over
  the 60 % default.
- Ours. One MultiMesh per species variant per tier per chunk (Vegetation.cs `Nodes[3]`), tiers cross-faded by a dither with
  two `discard`s in the fragment shader on top of the alpha scissor.
- Change. (a) One MultiMesh per species per tier per chunk, the variant chosen per instance (the variants share the atlas;
  one mesh per species built from its variants with a vertex-shader `INSTANCE_CUSTOM` mask selecting the variant's
  triangles) -- about a sixth of the nodes; (b) `VisibilityRangeEnd` on the far tier's nodes at the fade distance so the
  engine culls them without a `_Process` step; (c) the far tier without the dither discards outside the fade bands (a
  scissor-only shader variant for tier 2) so it keeps early-Z.
- Saving. 500 of the 838 draws, 0.3-0.5 ms of cpu.render, ~0.3 ms GPU; more in the mirror and the shadow splits.
- Faithfulness. None (a deviation already; docs/modern-vegetation.md).
- Measure. `veg_off`, `veg_low`, `hide_veg`, draws and cpu.render; `I76_VEG_STATS` for the node count.

### 7. Draw distance 20 km: the far chunks (800 draws, 0.0 / 0.5 ms)

- Mechanism. Every chunk of a 20 km view is a draw in every pass; the coarsest LOD (5 m -> 8 quads a chunk edge) still
  costs the draw.
- Ours. `draw_distance=1` (the modern look's default) draws the whole map and the horizon ring; `0` the original's 600 m with
  the sky fog, which saves 807 draws for 0.0-0.5 ms GPU (and `hide_horizon` 0.65 ms for the ring at 1080p).
- Change. Merge level-4 chunks four to one (one mesh per 2x2 chunks beyond 2400 m, built with the coarsest level) so the far
  field is a quarter of the draws; `VisibilityRange` fades at the horizon ring's foot.
- Saving. ~500 draws, 0.3 ms GPU, a few tenths of a ms of cpu.render.
- Faithfulness. None: the view's reach stays; the original's 600 m is still `draw_distance=0`.
- Measure. `draw_dist_0`, `hide_horizon`, draws and cpu.render.

### 8. HD texture packs: 4x for the cockpit, 2x for the world (VRAM and load, not frame time)

- Mechanism. BC7 .dds with mipmaps, uploaded whole; Godot 4.7 has no texture streaming (mip streaming arrives as an opt-in
  import type in 4.8, not this project's engine). The GPU samples the mip that fits the texel density, so a 4x texture costs
  no more per frame than a 2x one once past the first few metres: the survey finds no frame difference between 4x, 3x, 2x
  and off at either resolution (all within 0.3 ms, the noise).
- Ours. `hd_texture_size` picks one pack for everything: 4x = +723 MB of textures over the originals in T01 (1.72 GB at
  1440p), 3x +685 MB, 2x +598 MB (the packs carry the cockpit art at full scale and the same mip chains, so 2x is not a
  quarter of 4x). Load, warm caches: T01 11.6 s with 4x, 10.8 s with 3x, 10.3 s with 2x, 8.7 s with the originals; M01 8.5 /
  8.1 / 7.5 / 5.9 s (costs.md, "HD texture packs").
- Change. A size per category: the cockpit's `z*` art at 4x (the dash is looked at from 60 cm), cars 2x (a 1997 car texture
  fills 200 px at most), world 2x; and a cap by VRAM: under 4 GB drop the top mip of every world texture at upload
  (`Image.ShrinkX2` before the ImageTexture is made).
- Saving. 400-500 MB of VRAM and about a second of a mission's load; nothing in the frame.
- Faithfulness. The 1997 look never draws the packs; none.
- Measure. vram_tex_mb across `base` / `hd_3x` / `hd_2x` / `hd_off`; `tools/frame-cost/loadtime.py`.

### 9. Keep the 3D dash as the occluder it is (a saving of 2.0 / 0.7 ms already in the baseline)

The 3D cockpit covers a third of the screen with opaque geometry drawn first, so the terrain behind it fails the depth test
(`dash_2d` costs 2.0 / 0.7 ms more than the 3D dash, in the opaque pass). The chase view has no such occluder and is 4.8 ms
dearer at 1440p. Nothing to change; a note for the 2D dash's users (Render Scale is their lever) and for any future overlay:
draw opaque cockpit parts as 3D geometry, not as a transparent 2D layer.

## Everything else measured: within the noise

Smooth keyed edges (one R8 field per keyed texture: 6-20 MB VRAM, no frame cost: the field read is one bilinear sample
against a texture already in cache), brush impacts (a 128x128 field upload when dirty, no measurable cost standing; its
particles are capped), lighting calibration (a 0.7 s solve at load, then uniforms), the hood finish (a uniform branch; Gloss
adds Godot's clearcoat lobe on car paint only), death variety (nothing until a death; then the stock death's particles plus a
few bursts and the wreck fire, which the modern look has with the setting off too), map edge (a vertex-shader branch), the
tracers as bolts (0.00 ms against the line while firing both machine guns: the fire table), the smoke depth layer (0.0 ms
with every car smoking at 40 % health: the smoke table; the smoke itself 0.1 ms GPU and 0.3 ms of nodes), the bass-shaker
synth (main thread, within the ~0.3 ms the toggles can resolve), palette grade, car interiors, the 16x anisotropy of the
paved roads (0.01 ms against 8x and 4x). Each is under 0.3 ms GPU at either resolution, which is the survey's resolution for
a single feature (costs.md, Noise).

## A frame-time budget for future features

The budget. With everything on at 3440x1440 the frame is 16.9 ms GPU on a 1080 Ti at 180 W: at the edge of 16.7 ms (60 fps)
in the cockpit and well over it in the chase view (21.7 ms). Items 1-2 of this plan bring the cockpit to about 10 ms; that is
the frame to protect. Per new feature, measured in the standing T01 cockpit scene with this survey's harness:

| What | Allowed, idle (nothing happening that the feature answers) | Allowed, active (firing, a death, smoke, a crash) |
|---|---|---|
| GPU time at 3440x1440 | 0.25 ms | 1.0 ms, for the seconds it is active |
| Main-thread time (nodes + render) at 3440x1440 | 0.10 ms | 0.5 ms |
| Draw calls | 20 | 100 |
| VRAM | 50 MB | -- |
| Mission load | 0.5 s on T01 with the 4x pack | -- |

A feature that needs more buys it: the lane names what it turns down elsewhere (this plan's list), or the feature goes
behind a row that defaults off. Anything in a SubViewport, a compositor effect or a per-frame texture upload is measured
with its worst case on screen, not idle.

What a lane reports (in its PR text, from `tools/frame-cost/bench.py` on Windows or the Linux `tools/frame-profile.sh`): the
medians of three runs, feature on vs off, at 3440x1440 and 1920x1080, in the T01 cockpit scene (and its own active scene):
GPU total, gpu.depth and gpu.opaque, cpu.nodes + cpu.render, draw calls, VRAM textures, with the max-min spread over the
three runs beside each delta; a run flagged by the contamination check (another GPU user during the run, or a GPU time an
outlier against its siblings) is rerun, not averaged. A delta inside the spread is reported as "within noise", not as
zero. For a change that lives in one pass, the pass's number is the one that must move; for anything touching the terrain
or the vegetation, `hide_terrain` / `hide_veg` give the ceiling. A feature that updates a viewport on alternate frames or
otherwise varies frame to frame reports frame means as well as medians.

Resolution scaling is the sanity check: a pixel-bound cost roughly halves from 1440p ultrawide to 1080p (2.4x fewer pixels);
a draw-bound cost does not (the mirror: 2.9 vs 3.4 ms). A feature whose cost does not shrink with resolution is adding
draws or CPU work and should be looked at as such.

## T05 at a playtest's settings: 60 fps, and what 120 fps needs (round 5)

A playtest of T05 in the modern look at 3440x1440 ran at about 42 fps. Measured here on the same card (GTX 1080 Ti at 180 W,
a 179 Hz display) with the playtester's settings: the modern look, Terrain and Shadow Detail High, Anti-Aliasing 4x, Vegetation
Medium, Visibility Range Max, the mirror's full reach, the world's textures at 4x and the cars' at 8x, Ambient Occlusion off,
VSync off for the measurement (`FRAME_COST_PROFILE=owner`). The run is `tools/frame-cost/bench.py --scene t05`: 170 s of T05's
busy parts, the car driven along the roads (`I76_TEST_ROUTE`) and moved between them at set mission seconds (`I76_SPAWN_AT`),
firing at the nearest car with Unlim. Ammo, Armor and Chas. Rein. so it lives through every fight. Sections: the start and the
first car's attack on the road east (0-45 s), the mountain pass with the two cars that wait on it (45-105 s), the four-car
group north of the junction (105-145 s), Skeeter at Fisher's Field (145-170 s). One run per row; two runs of one build differ by
about 3 % in the median.

Until this round bench.py wrote shell.cfg as version 13, which since version 14 raises Terrain Detail Low and Shadow Detail Low to
High as it loads: a `terrain_low` or `shadow_low` run on a build with that step (this round's) measured High. It writes version
14 now; the earlier surveys predate the step.

The harness's `--gpu-profile` (the pass times) prints a table once a second, and that print is a stutter of its own (a 10 ms
frame every second, in the 1 % lows). The before/after table is measured without it (`FRAME_COST_GPU_PROFILE=0`); the pass
times below come from runs with it.

### Before and after

| T05, 3440x1440 | before: median fps | 1 % low | frames over 16.7 ms | after: median fps | 1 % low | frames over 16.7 ms |
|---|---|---|---|---|---|---|
| whole route | 62.5 | 25.0 | 43.5 % | 100.3 | 33.8 | 4.4 % |
| start, first car | 61.0 | 22.9 | 47.4 % | 96.0 | 34.5 | 3.2 % |
| mountain pass | 56.5 | 25.0 | 62.1 % | 106.9 | 33.4 | 6.5 % |
| the group | 59.7 | 26.6 | 50.9 % | 95.4 | 36.5 | 3.9 % |
| Fisher's Field | 102.5 | 30.1 | 6.4 % | 103.2 | 32.0 | 2.2 % |
| worst 5 s | 47 (the pass, 95-100 s) | | | 83 (the pass, 90-95 s) | | |

The 1 % low is the mean frame rate of the slowest 1 % of frames. Two game instances sharing the card, as in the playtest,
share the card and the main thread's cores with it; the 42 fps seen is within what that does to a build that ran at 57-62 fps
alone.

### Where the frame went

The GPU was not the limit: the main view took 8.0 ms and the mirror 1.8 ms every second frame. The main thread's nodes took
11-15 ms in the fights against 4 ms at Fisher's Field, where nobody is near. A sampling trace of the main thread
(dotnet-trace, 70 s of the first two sections) put 56 % of its managed time in the cockpit's radar, and the finalizer thread
was busy the whole time releasing Godot images:

- The radar. With a target held the scope is drawn every frame (the sweep's 0.2 s step applies only without one): the frame
  duplicated, the range plate and the LED each stamped through a managed copy of the whole picture (`Image.GetData` and
  `SetData`), the blips filled, the picture uploaded. With the dash's 4x art that is several megabytes of short-lived arrays a
  frame, and a full (generation 2) collection every 2-3 s: 65 of them in the run. Now the scope's content (frame, range plate,
  LED, each blip's square and colour) is compared with what was last drawn, and only a change makes a new picture, in one
  buffer and one image kept from frame to frame, the frames' texels read once. Full collections in the run: 65 -> 2.
- The smoke's lamps (`SmokeLayer.GatherLights`): a search of the whole scene tree (thousands of plant, ground and scenery
  nodes) for lights every sixth frame, 7 % of the managed time. Now one search, then the tree's node-added and node-removed
  signals keep the list.
- The drop shadows' scenery list was gathered in one frame every second; that walk is now spread over frames at 0.5 ms each,
  and the scenery meshes' bounds are kept instead of asked of the engine for every caster every frame. Not measured apart.

After the fixes the main thread's nodes take 3.0 ms, the renderer's CPU side 2.0 ms, the physics steps ~0.17 ms each. What is
left in managed time: the drop shadows' per-frame caster loop (17 %), the cockpit (16 %, the radar 7 % of it when a blip
moves), the bass shaker's synthesis (12 %, with a 14-band `tactile_eq`; it runs per sample on the main thread), the combat
view (8 %). A second trace of the mountain pass's fight (75-105 s) finds the same parts: the radar 13 % there (blips move
every few frames at close range), the drop shadows 13 % (most of it asking each car part whether it is visible), the shaker 12 %.

### The new defaults

- Physics at 240/s (`low_latency_audio`): Godot moves every positional sound in its physics step, about 0.17 ms a step. Before
  the fixes, when the main thread was the limit, 240/s cost about 1 ms a frame (54.9 and 57.1 fps against 58.6 and 60.9 at
  60/s); after them, with the GPU the limit, nothing measurable (100.3 against 100.0). At 120 fps it is two steps a frame,
  0.35 ms.
- The mailbox present (`low_latency_present`, VSync on): 100.4 fps against 100.3 with VSync off; no cost here.

### What 120 fps costs

8.3 ms a frame. After the fixes, the playtester's settings at 3440x1440 on this card:

| Part | ms a frame | at 120 fps |
|---|---|---|
| GPU, main view (opaque 5.6, sky 0.7, depth 0.6, shadows 0.6, transparent 0.6, MSAA resolve 0.4, tonemap 0.2) | 8.0-8.9 | over budget alone |
| GPU, mirror (every second frame) | 1.8 (0.9 a frame) | |
| main thread: nodes | 3.0 | fits |
| main thread: the renderer's CPU side (1600 draws) | 2.0 | fits |
| physics steps (240/s) | 0.17 a step | 0.35 |
| the simulation (24 Hz) | 0.35 | fits |

The GPU is the limit: about 9.5 ms a frame with the mirror, so 100-105 fps. The main thread would allow ~150 fps on a typical
frame, but its spikes (effects starting in a fight, new pipelines, the terrain's level changes after a jump) are 20-40 ms and
are what the 1 % low is made of.

### The optimisations that would get there

1. The opaque pass (5.6 ms): the terrain shader's near tier is most of it at 3440x1440. The tiers (item 3) could start sooner
   (near tier to 150 m rather than 250 m); expected 0.5-1 ms, not measured.
2. The mirror (0.9 ms a frame): its far clip to 2 km with `mirror_range=full` instead of 20 km, the far chunks out of its cull
   mask (item 2c); expected 0.3-0.5 ms, not measured.
3. Anti-Aliasing 2x (the default since round 2; this playtest kept the earlier 4x): 0.6 ms.
4. The main thread's spikes: the first explosion and death of each kind already rehearse behind the loading card; the rest
   are the effects' pipelines compiled in a fight (`hitch:` lines name them) and the terrain's level rebuild after a teleport.
   Each wants its own trace; none is a steady cost.
5. The radar's redraw when a blip moves (still a whole-frame copy and upload each time a blip moves a pixel; 7-13 % of the main thread's
   managed time in a fight): the blips and the LED as a small layer of their own over the static frame would make it a few
   hundred bytes.

### Settings fallback ladder

Each step keeps the ones before it, measured on the route (one run each, ±3 %; with the pass times, so with the once-a-second
print: compare within the table). Step 1's gain is inside the noise on this route, though it takes 0.6 ms off the GPU:

| Step | What drops | median fps | GPU ms (main view) | What it costs to the eye |
|---|---|---|---|---|
| 0 | the playtester's settings, after the fixes | 98.3 | 8.91 | |
| 1 | Anti-Aliasing 4x -> 2x | 98.3, 101.2, 102.1 (three runs) | 8.28 | stair-steps on thin edges (fences, poles, wires) a little more visible |
| 2 | Shadow Detail High -> Low (two splits to 600 m) | 107.8 | 8.08 | distant mesas' and buildings' shadows past 600 m gone; near shadows softer |
| 3 | Terrain Detail High -> Low (no 1.25 m level near the car) | 110.0 | 7.52 | the ground's shape within 100 m a little coarser |
| 4 | Render Scale 100 -> 85 % (FSR 1.0 up) | 120.4 | 6.75 (FSR's pass 0.5 of it) | the ground's fine detail and thin edges a little soft |
| 5 | Vegetation Medium -> Off | 131.5 | 6.57 | no scattered brush and grass (the map's own trees stay); also 0.5 ms of nodes, 0.4 ms of the renderer's CPU, 270 draws |

The order is the least visible saving per millisecond first. Ambient Occlusion (off here; 5 ms at this size, item 1) goes
before all of them for anyone who turned it on. Visibility Range Far (600 m, the original's) and the 1997 look are the floor
below the ladder: each changes what the game looks like rather than how finely, so they are the player's choice, not a fallback.

### A target frame rate

`[port] target_fps` (off by default, docs/playing.md; `Boot/FrameTarget.cs`) is the ladder's step 4 taken for the player, live: every 30 frames it takes the medians of the main view's GPU time and the frame time; when the target is missed and the
GPU took more than 85 % of the frame, the 3D view drops one Render Scale step (85, 75, then 67 %, upscaled by FSR as the row's
steps are); when the next step up would fit inside 80 % of the frame (its GPU time scaled by its pixels) three windows in a row,
it goes back up, never past the player's own Render Scale. A frame the main thread holds is left alone. On the route with
`target_fps=120` and the playtester's settings: median 116.8 fps (113-123 by section), the main view's GPU 6.9 ms against 8.9;
its 1 % low (24.7, with the harness's once-a-second print) is the main thread's spikes, which no scale reaches. The cost to the eye is FSR's: the ground's fine detail
and thin edges a little soft at 75-85 %. It is not the default because a changing sharpness is a deviation a player should ask for.

## The dash: every element updates only itself (round 6)

The cockpit's panels are textures drawn on the CPU (CockpitPanels: the original's panel image, sprites from the sheets stamped
at the layout's anchors) and uploaded to the GPU. Until this round an upload was always the whole texture, and with the dash's
8x art (the default on a 6 GB card) a panel is 2048x1024, 8 MB: every roll of an ammunition counter sent the weapons panel's
8 MB (the firing fix of round 5 had made the roll redraw only the digits on the CPU, but the upload stayed whole), a gear change
2 MB, every half degree of heading the compass's 1 MB, every needle step of the 2D speedometer and tachometer 4 MB (also under
the 3D dash, which never shows them), and every step of the radar's sweep 8 MB.

### The audit (888af090, the dash at 8x)

| Element (texture, size at 8x) | What redraws it | Redrawn on the CPU | Uploaded | How often | Managed allocations per update |
|---|---|---|---|---|---|
| Radar (zrad frames, 2048x1024) | the sweep's 0.2 s step; with a target held, any blip, the range plate or the LED changing | the whole picture (frame copied, stamps, blips) | whole, 8 MB | 5/s sweeping; with a target held up to every frame | none (the round-5 fix), but each of the 62 frames' texels read once into a kept 8 MB array |
| Weapons panel and ammo counters (zbk_, 2048x1024) | a counter digit rolling (each shot, at the tick rate); a plate lit or a diode band changing | the digits' windows and the separator bar; the whole panel for a plate or diode | whole, 8 MB, plus an 8 MB copy into an Image | up to 24/s while firing; a weapon switch | `Take(5).ToList()` every frame; a key string per digit drawn |
| Specials panel (zbks_, 2048x1024) | a charge counter rolling, a plate or diode | the whole panel | whole, 8 MB | a special used | an array of the three specials every frame |
| Damage panel, Mr Damage (zsy_, 2048x1024) | any part changing band | the whole panel (put back, 15 stamps) | whole, 8 MB | a few times a fight | a list and two arrays every frame, changed or not |
| Gear strip PRND21 (zgear101, 2048x256) | a gear change | the whole strip | whole, 2 MB | a shift | none |
| Compass (zcm_, 512x512) | heading moving half a degree | the window | whole, 1 MB | every frame while turning | none |
| 2D speedometer and tachometer (zspeedo6, ztach6, 1024x1024) | the needle's frame changing | the whole dial | whole, 4 MB each | several a second while the speed or the revs change, also under the 3D dash, where they are not drawn | a sprite name string per step |
| Equipment lights (the plates' lit state, the diodes, the radar LED) | part of the weapons, specials and radar pictures above | as their panel | as their panel | as their panel | as their panel |
| 3D needles, steering wheel, arms | the needle's angle; the wheel's or an arm's frame | nothing (a transform; a texture swap) | nothing | every frame; each frame change | each swap searched the whole material cache by name |
| Messages (the CB line) | a line on the air | a Label | Godot's text | the text set every frame while a line plays | a string every frame |
| PinCockpit (the dash held in the eye's frame) | every frame in the car | nothing | instance parameters | every frame | four parameter names made a part a frame, a child array a node |

### What changed

- `LiveTexture`: on a RenderingDevice (Forward+, Mobile) each panel and the radar is a texture of its own that the GPU can copy
  into. A changed rectangle goes up through a small staging texture (a power of two up to 256 texels a side, then a multiple of
  256; kept) and is copied into place by the GPU, byte for byte; a picture that changed over three quarters of its staging size goes up
  whole. The Compatibility renderer has no texture copy: there it uploads whole, as before.
- `CockpitPanels`: every draw notes the rectangle it wrote; Commit uploads those. `Region` redraws one element: the original image
  put back in its rectangle and the panel's draws replayed clipped to it (every draw is a stamp or copy that writes its own texels
  from its own source, so that gives the whole redraw's texels there). A redraw that keeps most of a panel (a plate lit, a diode,
  the specials) is drawn whole on the CPU as before and uploads what differs from the panel the GPU holds.
- Per element: a counter roll uploads its digit's window (64 x 80 texels, 32 KB through the staging texture) and the separator
  bar is restamped only inside that window; a gear change the arrow's old and new places; a damage band the part's plate; the
  compass its window; a needle step its square; the radar the rectangles where the new frame differs from the last (in bands of
  32 rows, so the sweep line is a staircase, not its bounding box), the blips' old and new squares and the range plate or LED if they
  changed. The 2D speedometer and tachometer are drawn only where they are shown (the 2D dash, the external instruments).
- Allocations: the damage, weapons and specials panels read the car into kept arrays; the counter cells are found by a value key;
  the material swaps of the wheel and arms use a per-texture list; PinCockpit's parameter names are made once; the CB label is set
  when the line changes.

### The ammunition counters: text or blit

A Label (or Label3D) in the counters' font cannot be what the 3D dash shows: the counters are part of the weapons panel's texture
on the cockpit model, so text would need a SubViewport rendered into the panel (a whole render target a frame, far dearer than the
blit) or quads over the model, which miss the drum's shading, the housing's fade at the window's edges (sampled from the panel
round each window) and the panel's own filtering, so they could not match today's pixels. Glyph quads from a digit atlas sampled
by the panel's shader would differ at the window's edges, where filtering reads the atlas's neighbouring cell instead of the
housing. The blit is already a glyph atlas: each drum cell (figure, style, edge, housing colour, scale) is made once and kept; a
roll copies one 64 x 80 cell on the CPU (20 KB) and sends that cell's 32 KB staging texture to the GPU, a few microseconds. Kept
as a blit; the drum and its feathered edge are unchanged.

### Before and after

The perf bench (`tools/frame-cost/bench.py`, `FRAME_COST_PROFILE=owner`, 3440x1440, the dash's and the cars' textures at 8x,
`I76_DASH_STATS=1`), one run of each build. On T05 (`--scene t05`) and T01 (`--scene fire`) the firing used real ammunition (Play Options' Armor and Chas.
Rein. on, Unlim. Ammo off), so the counters roll through every fight. The cockpit's cost is its update on the main thread (the
panels drawn and committed, the radar, the head and wheel); upload is the texture bytes sent to the GPU; the garbage collections
are the whole process's.

| | cockpit ms a frame (mean) | worst frame ms | texture upload MB/s | uploads/s | cockpit managed KB/s | GC gen0 / gen1 / gen2 a minute |
|---|---|---|---|---|---|---|
| T05 whole route, before | 1.55 | 23.8 | 159 | 28.5 | 3405 | 135 / 93 / 0.4 |
| T05 whole route, after | 0.36 | 13.5 | 25 | 141 | 3224 | 130 / 93 / 0.7 |
| T05 mountain pass (fights), before | 1.85 | 22.0 | 188 | 34.5 | 3976 | 101 / 86 / 1.0 |
| T05 mountain pass, after | 0.38 | 13.5 | 28 | 167 | 3393 | 101 / 87 / 0 |
| T01, the car standing, the guns held firing (51 s, until the car was destroyed), before | 0.76 | 13.4 | 122 | 17.1 | 4859 | 80 / 33 / 0 |
| T01 firing, after | 0.26 | 12.2 | 27 | 170 | 4190 | 93 / 9 / 0 |

The frame (bench result lines, T05 whole route): median frame 11.6 -> 10.3 ms, its 95th percentile 24.0 -> 16.5 ms, the main
thread's nodes 4.35 -> 3.42 ms; one run each, with the machine's other load different between them (70 % and 52 % CPU), so the frame
numbers carry that noise; the cockpit's own numbers do not depend on it. Uploads are more but smaller (a digit's window each).
Most of what is left of the upload is the radar's sweep: each step's frame differs from the last over the sweep's wedge and its fading trail. The cockpit's managed
allocations left are the radar's frames read once each (8 MB a frame at 8x, the first time each is shown: most of the KB/s above
in a run's first minute) and about 2 KB a frame in the head's input lookups (Controls.Down); the garbage collections are the
process's, the cockpit not the cause of them.

Pixel identity: every panel's CPU picture hashed at every change, before against after, the same tick for tick in both looks
(T05 from the start to 108 s with the gun firing: 1046 changes of the weapons panel, 787 of the compass, 27 of the damage panel); every upload read back from the GPU against the CPU's
picture (`I76_DASH_STATS=verify`): 2905 uploads, no texel different; screenshots at 3440x1440 (16 frames at 29 s, 9 at 108 s, the
same ticks; for the comparison both builds had the housings' and the effects' random numbers seeded and `I76_VEG_PLANTS=0`, a local change
not in the branch): the 1997 look zero differing pixels, the modern look zero on the dash, the differences all in the world's hit
effects and smoke at the target (which two runs of the before build also differ in, 66 pixels at 29 s).
