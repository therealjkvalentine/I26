# The 1997 look against i76-everywhere

i76-everywhere (i76-e) is a patch kit that runs the original 1997 executable on today's machines: 120 frames a second
through a Glide wrapper, wide screens, a 2.3 km draw distance. Players who know both say its picture "feels a little
snappier and more fun" than this port's 1997 look. This note finds, with measurements, what differs between the two,
how much each difference plausibly contributes, and what the 1997 look can take from it. It covers the 1997 look only;
the modern look is not the subject.

Both games were run on one machine (i7-7700K, GTX 1080 Ti, a 3440x1440 179 Hz display), windowed at 1920x1080, in T01
with the mission's own car (the Picard Piranha), keyboard driving (W/A/S/D, Enter to fire):

- **I26**: the play branch, the 1997 look (`--1997`), Simulation Rate 24 (85/2048 s ticks), VSync on, Max FPS
  unlimited, Field of View Monitor (73.74 degrees high: 106 across 16:9), Visibility Range Max (20 km), the player's
  shell.cfg otherwise; the scratch user folder held only that file.
- **i76-e**: the kit's daily-driver preset (`OWNER-2300-ALL`, the patch kit's `tick19-42` timing): the whole simulation
  on a 41.667 ms tick (24 a second) drawn interpolated at 120 frames a second, `I76_SIM_TICK_INPUT=1`, far clip and
  roads 2300 m with a fog to the horizon colour from 0.75 of it, terrain LOD x24, mirror every second frame to 1000 m,
  FOV 90 at 4:3 widened Hor+ (106 across 16:9), 4x MSAA (its 2x supersampling is not used in a window), FFB and
  head tracking off.

The field of view, the tick (41.5 ms against 41.7 ms) and the car are therefore the same; the draw distance cannot be
(I26 offers 600 m or 20 km).

## What was measured, and how

All runs windowed at 1920x1080 on the desktop's display, one game at a time with nothing else on the GPU, each under two
minutes, I26's sound muted and i76-e's muted by its own switch (the kit's rule for automated runs), so sound timing was
not measured. I26 started with `--drive` (the player's car at T01's start, the mission script not holding it: T01's
opening shot cannot be skipped from the command line in I26) and i76-e with `--mission t01`, its intro skipped with Esc,
so the two drove the same road from the same place with the same car body (I26's with its default loadout).

**Frame pacing** (each game's own per-frame log, about 30 s of driving with steering taps; i76-e `I76_FRAME_LOG`, I26
`I76_FRAME_PROFILE_CSV`'s `cpu.wall`):

| | frames/s | median ms | sd ms | p99 ms | 1 % low (mean of the slowest 1 %) |
|---|---|---|---|---|---|
| I26 1997 look, before | 179.0 | 5.55 | 1.08 | 8.65 | 83 fps |
| I26 1997 look, after (MSAA 4x on) | 178.1 | 5.56 | 1.03 | 8.77 | 82 fps |
| i76-e (three runs) | 110-118 | 8.33-8.51 | 0.9-1.8 | 12.5-14.5 | 63-76 fps |

i76-e caps at 120 and presents into a 179 Hz desktop, so each of its frames is on screen for one, two or three
refreshes (a 180 Hz desktop capture counted 1462 / 1282 / 258 intervals of 5.6 / 11.1 / 16.7 ms in 27 s). I26 runs at
the display's rate with vsync. **Pacing is not where i76-e is ahead; the 1997 look is the steadier of the two.**

**Input to screen.** A script pressed the key with `SendInput` (scan codes) at the instant it turned a 24 px topmost
marker square white; ffmpeg's Desktop Duplication grab (`ddagrab`) recorded the marker and a band of the game's window at
180 frames a second (5.6 ms resolution), and the latency is the number of captured frames from the marker's first white
frame to the band's first change beyond the noise before the press (mean + 6 sd of the 38 frames before). The marker goes
through the same compositor as the game, so the figure is the game's own delay relative to a window that redraws at once
(input to photon less about one composition), measured the same way for both games. Throttle from rest (W held 300 ms,
then S to stop, 10 presses; the tachometer needle, the head sway and the first motion are what change):

| | median | interquartile | n |
|---|---|---|---|
| I26 1997 look, before | **50 ms** | 39-67 | 9 |
| I26 1997 look, after (Early Tick) | **22 ms** | 17-28 | 10 |
| i76-e (`I76_SIM_TICK_INPUT=1`) | **17 ms** | 11-17 | 9 |

Steering at speed (A and D in turn, 300 ms each, 24 presses, the horizon band's sideways shift averaged over the presses)
was too noisy to separate the games: the car's yaw builds over 150-300 ms from the keyboard's sqrt(t/3) steering in both,
and the averaged curves cross 0.25 px at 170-200 ms in either. The wait the throttle shows is the same wait for any key.

**The tick and the drawing between ticks** (the code and the kit's records): both step the whole game 24 times a second
(I26 85/2048 s = 41.5 ms, i76-e 41.667 ms), and both draw every car between its last two ticks, so in both the picture is
up to one tick behind the newest state. The difference was the wait: I26 read the keys only when a tick fell due, so a
press waited 0-41.5 ms (21 on average) before the simulation saw it; i76-e runs the tick at once when a key changes,
borrowed from the next, and draws from the pose on screen to the new tick (the kit measured 4.2 ms median from the key to
the first changed drawn frame at 120 fps, against half a tick without).

**The original** (the kit's lab: the retail CD build on an emulated P166 in 86Box, and the GOG build's 20 fps cap on
Windows): the frame was the step, 26.6 ms (37.5 fps) in a melee arena on the emulated retail machine, 31-36 fps in
T04, T05 and T13, and 46.9 / 31.2 ms steps (about 24 a second) under GOG's cap; a key read at the top of a frame moved the
car in that frame. A held steering key reaches 0.9 of lock in 2.41 s; 0-10 / 20 / 30 m/s took about 1.05 / 2.75 / 5.55 s.
So the original answered a key within one of its frames (27-50 ms, plus the display), I26 before (50 ms to the first
change) answered like the original at its slowest, and i76-e and I26 after answer faster than the 1997 machine did,
without changing the step.

Captures (the lane's scratch folder, not the repository): the same eight moments in both games before and after,
the anti-aliasing at 1:1, and 30 fps review videos of each run.

## The differences, ranked

"Contribution" is a judgement of how much of "snappier, more fun" each explains; "gap" is the measured or documented
difference. Rows marked *same* or *I26 closer* need nothing.

| # | Difference | i76-e | I26 1997 look (before) | Gap | Contribution | Change for the 1997 look: cost, risk |
|---|---|---|---|---|---|---|
| 1 | Waiting for the tick | a key change runs the next tick now (borrowed), drawn from the pose on screen | the keys wait for the tick (0-41.5 ms) | 33 ms median, throttle from rest (50 against 17) | **large**: every press, every turn-in | **Done**: Early Tick (`TickPacer`), `[port] early_tick`. Small (a pacer class, a key-change test, a kept drawn pose per car). Risk low: the ticks and their inputs are unchanged; rounds and effects drawn between ticks can move on up to a tick at a press |
| 2 | Polygon edges | 4x MSAA (and 2x supersampling full screen) | no multisampling | pillars, horizon and road lines crawl in I26 | **medium**: the clean look in motion | **Done**: the 1997 look takes the Anti-Aliasing row, `[port] aa_1997`. A few lines; no measurable cost (179 fps either way). Risk: keyed texture cut-outs stay stepped (MSAA smooths geometry, not texels) |
| 3 | Machine-gun cadence | each shot's flash and sound on the display frame where its timer put it (`I76_MG_EVEN`): 100 ms apart | the flash on the tick: 83 and 125 ms apart at 24 Hz (the looping report was already steady) | the flash interval's 21 ms swing to under 10 | **small to medium**: firing is half the game | **Done**: `ShotFired.Lead` (presentation only) and the 1997 look's flash and one-shot report at it, `[port] even_shots`. Small; risk low (a flash at most a tick later than before) |
| 4 | Supersampling | full screen: drawn at 2x per axis (6880x2880 on 3440x1440); none in a window | Render Scale 100 % | texture shimmer, sub-pixel detail | small to medium, full screen only | none needed: Render Scale 200 % does it (a 1997-look frame is 1.6-2.4 ms of GPU at 1080p); worth a word in the help line |
| 5 | Distance | 2300 m, terrain LOD x24 with morphing, a fog to the horizon colour from 0.75 of the clip | 600 m with the horizon panels (Far) or 20 km drawn crisp to the map's end (Max\*), no fade | i76-e's far ground melts into the horizon; I26 shows more, with an edge | small for feel, visible in the horizon shots | proposed: a fade to the horizon colour over the last quarter of the draw distance in the 1997 terrain and object shaders at Max\*, or a 2.3 km setting. Medium cost, low risk |
| 6 | Frame rate and pacing | 110-118 fps, 1 % low 63-76, uneven on a 179 Hz display | 179 fps, sd 1.0 ms, 1 % low 83 | **I26 ahead** | none in i76-e's favour | none |
| 7 | Rear-view mirror | every second frame (60 a second), to 1000 m, a 256x64 8-bit texture | every second frame (about 90 a second at 179), 1024x256, to Rear Mirror Range | same rule; I26 sharper | none | none |
| 8 | Cameras: chase, head look, head sway, field of view | the original's code at the display rate: chase distance and heading ease 2·dt, head look 4·dt, acceleration sway 2·dt; 90 at 4:3 widened to 106 at 16:9 | the same formulas on the render clock; Monitor = 106 at 16:9 | *same* | none | none |
| 9 | Drawing between ticks | every moving object and the projectiles; smoke extrapolated | cars, wheels, turrets and rounds; the 1997 smoke per tick at the tick's fraction | *same* where it shows (a tick behind) | none | none |
| 10 | Gamma, filtering, colour depth | 32-bit, the Glide gamma ramp not applied, bilinear without mip chains (distance shimmer) | the 3D card's gamma curve, bilinear with mipmaps and anisotropy | I26 a little brighter, steadier in the distance | none | none: *I26 closer* to the 3D card |

Not compared: sound cue timing (both set the engine's pitch from its rpm once a tick and start one-shot reports on the
tick; i76-e was muted in every run), a jump, a hit taken and a car's death (T01's first minute has none of them and the
runs were kept short on a shared machine), and a melee (i76-e starts one only through its menus).
