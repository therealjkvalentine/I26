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
| 3 | Machine-gun flash cadence | each shot's flash and sound on the display frame where its timer put it (`I76_MG_EVEN`) | a new 10 fps three-frame sprite on each tick that fires (83 / 125 ms at 24 Hz); overlapping, they read as a flash every second shot | flash re-light every 206 ms (robust sd 16) | small to medium | **Done in round 2** (the round-1 attempt was withdrawn): each gun's one sprite re-lit at the shot's place in its tick, its same frames played within the gun's interval: re-light every 106 ms, robust sd 9. It reads as a crisp flash per shot rather than the overlapping glow, a change of impression the user should judge (sheet 6); `[port] even_flash` |
| 4 | Supersampling | full screen: drawn at 2x per axis (6880x2880 on 3440x1440); none in a window | Render Scale 100 % | texture shimmer, sub-pixel detail | small to medium, full screen only | none needed: Render Scale 200 % does it (a 1997-look frame is 1.6-2.4 ms of GPU at 1080p); worth a word in the help line |
| 5 | Distance | 2300 m, terrain LOD x24 with morphing, a fog to the horizon colour from 0.75 of the clip | 600 m with the horizon panels (Far) or 20 km drawn crisp to the map's end (Max\*) | at T05's start the kit's road ends in the horizon's mountains, I26 Max\* in the map's edge wall | small for feel, visible on long views | **Done in round 2**: Visibility Range 2.3 km\* (`draw_distance=2`), the engine's depth fog from 1725 m in the panels' foot colour; GPU 1.42 / 1.61 / 1.86 ms at Far / 2.3 km / Max\* (1997 look, 1920x1080), 180 fps at each |
| 6 | Frame rate and pacing | 110-118 fps, 1 % low 63-76, uneven on a 179 Hz display | 179 fps, sd 1.0 ms, 1 % low 83 | **I26 ahead** | none in i76-e's favour | none |
| 7 | Rear-view mirror | every second frame (60 a second), to 1000 m, a 256x64 8-bit texture | every second frame (about 90 a second at 179), 1024x256, to Rear Mirror Range | same rule; I26 sharper | none | none |
| 8 | Cameras: chase, head look, head sway, field of view | the original's code at the display rate: chase distance and heading ease 2·dt, head look 4·dt, acceleration sway 2·dt; 90 at 4:3 widened to 106 at 16:9 | the same formulas on the render clock; Monitor = 106 at 16:9 | *same* | none | none |
| 9 | Drawing between ticks | every moving object and the projectiles; smoke extrapolated | cars, wheels, turrets and rounds; the 1997 smoke per tick at the tick's fraction | *same* where it shows (a tick behind) | none | none |
| 10 | Gamma, filtering, colour depth | 32-bit, the Glide gamma ramp not applied, bilinear without mip chains (distance shimmer) | the 3D card's gamma curve, bilinear with mipmaps and anisotropy | I26 a little brighter, steadier in the distance | none | none: *I26 closer* to the 3D card |

Not compared: sound cue timing (both set the engine's pitch from its rpm once a tick and start one-shot reports on the
tick; i76-e was muted in every run), a jump, a hit taken and a car's death (T01's first minute has none of them and the
runs were kept short on a shared machine), and a melee (i76-e starts one only through its menus).

## What changed in the 1997 look

| Change | Key | Before | After | Reference |
|---|---|---|---|---|
| Early Tick: a key change runs the next tick at once (`TickPacer`, `CarNode.KeepDrawn`) | `[port] early_tick` (on) | throttle from rest, key to first change: 50 ms median (39-67, n 9) | 22 ms (17-28, n 10) | i76-e 17 ms (11-17, n 9) |
| Anti-aliasing: the 1997 look takes the Anti-Aliasing row's MSAA | `[port] aa_1997` (on) | stepped polygon edges (the pillars, the horizon, the road lines) | smooth, as i76-e's | 179.0 / 178.1 fps, 1 % low 83 / 82 fps: no measurable cost |

Neither changes the simulation: the ticks, their length, their inputs and the physics are the same, and both are off in
the modern look (which keeps its own Anti-Aliasing as before) and, for Early Tick, online, under Modern Physics and with an
input replay. The remaining distance to i76-e in the throttle test, about one 5.6 ms display frame, is within the method's
resolution. The rows above that need a change and are not made here: supersampling (a setting, Render Scale 200 %) and
the far fade to the horizon colour (row 5).

## Round 2

**The distance.** Visibility Range's third value, 2.3 km\* (`[port] draw_distance=2`): the 2300 m clip and, in the 1997 look,
the ground and everything on it fading linearly from 1725 m into the horizon panels' foot colour (the mean of their bottom four
rows, the colour the kit's own fog uses), the sky left alone. At T05's start, the one view both games share exactly, the
road through the canyon now ends in the panels' mountains, as in the kit; at Max\* the map's edge wall closes it. Frame cost
(T05, driving 30 s, 1997 look with 4x MSAA, 1920x1080, median GPU per frame / draw calls): Far 1.42 ms / 185, 2.3 km 1.61 ms
/ 488, Max\* 1.86 ms / 658; all three hold the display's 180 fps.

**The machine-gun flash.** Each gun's flash is one sprite lit again where the gun's timer put the shot within its tick, playing
the same three frames within the gun's interval (30 a second for a 10/s gun). Chase view, a 10/s gun held, 180 Hz capture of
the gun's region: before, a re-light every 206 ms (robust sd 16 ms, 33; plain sd 14.8), the overlapping 10 fps sprites showing
every second shot; after, every 106 ms (robust sd 9 ms, 65; plain sd 15.1 with one missed re-light, 7.5 without it). The
spread halves at the gun's own cadence; relative to the interval it is the same (8 %). The look changes from an overlapping
glow with dips to a crisp flash per shot.

**Early Tick, by eye and by numbers.** A per-frame log of every car's drawn position (`I76_DRAW_LOG`; M01, three computer
cars, A and D tapped every 0.8 s at full throttle, 27 early ticks): the change in drawn velocity from one frame to the next on
the frames at an early tick, per car, median 1.77-1.80 m/s, p99 1.8-4.5, largest 5.4, against 1.74-1.80, 3.6 and 7.1 on all
other frames: an early tick moves nothing on screen by more than an ordinary tick boundary does. No computer car came nearer
than 74 m in the run, so the slow-motion clips (180 Hz captures played 6x slower) show the player's car only.

**Anti-aliasing on keyed textures.** T05 and M01, the same view with `aa_1997` off and on, 2x crops: polygon edges (pillars,
walls, the arena's rim) are smoothed, the keyed cut-outs (the arena's fence boards) stay stepped as they were, with no fringe or
seam at their edges. The 1997 look has no plants and no sign stood near either start, so those were not seen.

**Sound** (both unmuted; key pressed with SendInput and the default output recorded by WASAPI loopback on one clock, each 5 ms
block stamped; the figure is key to the sound reaching the system's mix, so the device's own latency is not in it). Gun report
(Enter, 10 presses, the mean energy above 1 kHz first 6 dB over the 300 ms before): I26 before 102 ms, after 86 ms; i76-e 186
ms. Engine pitch (W tapped 300 ms from rest): I26's spectral centroid leaves its idle by 1.5 sd at a median 79 ms before, 60
after (noisy: a 300 ms tap moves the rpm little); i76-e's shows no detectable change within the tap. So the sound does not
explain i76-e feeling snappier: I26's reports come about 100 ms sooner. (The loopback lost data 98 times in 20 s while i76-e
played and 16 times for I26; the per-block stamps keep the times right across the gaps.)

## Round 3: control latency

The question after rounds 1 and 2: what else shortens the time from a key to the screen and to the speakers? Each candidate was
measured as before (a script presses the key with `SendInput` at the instant it lights a marker, a 180 Hz desktop capture finds the
first frame that answers; T01, `--drive`, the 1997 look, Early Tick on, windowed 1920x1080), with two additions: `I76_DRAW_LOG` now
stamps each frame with the performance counter the script's key times use, and logs the keys as that frame read them and the drawn
headings, so the key-to-read time and the drawn car's answer are measured to the frame without the capture's 5.6 ms steps; and
`I76_AUDIO_LOG` stamps the gun loop's start the same way. Throttle from rest, 11 or 12 presses a run, each run's first press left out;
several runs per configuration at different minutes, pooled, because one configuration's run medians spread from 11 to 33 ms between
runs (the desktop's composition state, which the game does not control): a single run cannot separate anything under about 10 ms.
Three other lanes shared the machine; no run was marked contaminated (GPU idle and no other game process at each start), but a
Windows Security Alert from another lane's test host stood over part of the captured band in every run, the same in all. The game came up paused (its Esc menu open, cause not found)
about 52 s into T01 in every run, so each run was kept inside that.

| Candidate | Key to photon before (median, IQR, presses) | After | Pacing (frames drawn: fps, sd, 1 % low) | Verdict | Key |
|---|---|---|---|---|---|
| Swap: VSync on, queued (FIFO), Godot's stock 3 images, frame queue 2 | **22.2 ms**, 13.9-27.8, n 67 (six runs: 11.1-33.3) | (the reference) | 171-180 fps, sd 0.7-2.0 ms, 1 % low 56-109 | | |
| `frame_queue_size` 1 | 22.2 | 22.2 ms, 13.9-30.6, n 11 | 162 fps in that run | **no effect**: Godot 4.7.2 keeps two frames whatever is asked (`RenderingDevice.GetFrameDelay()` reads 2) | not shipped |
| `swapchain_image_count` 2 | 22.2 | 16.7 ms, 11.1-26.4, n 22 (runs 13.9, 22.2); with queue 1: 11.1, 8.3-16.7, n 11 | 169-179 fps, sd 0.7-1.5, 1 % low 78-110 | inside the reference's run-to-run spread: not shown to pay | not shipped |
| VSync off (Max FPS 240) | 22.2 | 11.1 ms, 0.0-19.4, n 11 | 177 fps, sd 1.8, 1 % low 76 | as fast as mailbox, but tears and paces worse | the existing VSync row |
| VSync adaptive | 22.2 | 27.8 ms, 22.2-33.3, n 11 | 179 fps, sd 0.9, 1 % low 98 | the same as FIFO while no frame is late | not shipped |
| **Swap: mailbox** (the newest finished frame at each refresh, no tearing) | 22.2 | **11.1 ms**, 5.6-11.1, n 56 (five runs, all 8.3-11.1) | 236-248 fps, sd 1.0-1.2, 1 % low 106-118 | **pays, in every run**: about 11 ms, the queued display frames. It costs power where the game outruns the display: GPU 149 W median against 83 W in the 1997 look (GTX 1080 Ti at its 180 W cap). In the modern look, GPU-bound at 145-154 fps, no gain (38.9 ms, 27.8-44.4, n 9, against FIFO's 30.6, 27.8-38.9, n 10) and no cost (162 W against 165) | **shipped, on**: VSync On swaps by mailbox (Godot falls back to FIFO where the driver has none); live | `[port] low_latency_present` |
| Input sampling point | key to the frame that reads it: median 3.6 ms (run medians 2.2-5.2), worst 6-12 ms | — | — | already the latest point Godot offers: the window's messages are pumped at the top of each frame and `SimDriver` (process priority −1) reads the live key state (`Input.IsPhysicalKeyPressed`) and decides the early tick in the same call, so the wait is half a 5.6 ms frame on average. `Input.UseAccumulatedInput` merges mouse motion only, never keys. The remaining 3 ms on average could only be had by reading the keyboard again in mid-frame, which Godot does not do | nothing to ship |
| Draw Ahead: the player's car drawn at the newest tick carried forward (both looks) | throttle from rest 22.2 ms; steering, the drawn heading's answer after the key (draw log, 20 presses a run): 0.2° at 42 ms, 0.5° at 68-74, 1° at 95-107 (two runs) | throttle 22.2 ms, 22.2-27.8, n 22 (the first change there is the cockpit's needle, not the car's pose); steering 0.2° at **16 ms**, 0.5° at **35**, 1° at **69** | 179 fps, unchanged | **pays for steering, about 30 ms** (a tick less of drawing behind). Its corrections at the ticks, M01 chase view with a hard turn: the drawn velocity's frame-to-frame kink at the frames a tick runs, median 3.2 m/s, p99 24, largest 30, against 1.5, 9.6 and 17 interpolated (a p99 step about 8 cm larger); the slow-motion clips are for the eye | **shipped, off** until judged | `[port] draw_ahead` |
| Camera smoothing | — | — | — | the original's rules (below): proposed, not changed | — |
| Sound: the gun report (Enter, the Piranha's machine gun, key to the loopback) | **88.8 ms**, 81.8-102.7, n 12 | 5 ms buffer: 77.5 (73.8-84.0); physics 240: 80.2 (78.3-85.6); **both: 73.4**, 66.7-74.9 | 179-180 fps; no dropout in 2 x 25 s with the LFE synth on, nor in 45 s with the shipped defaults (mailbox swap, 5 ms, 240 steps: 64.2 ms, 58.6-69.0, n 24) | **pays, about 15 ms** | `[port] low_latency_audio` |

**Shipped on by default**: the mailbox swap (`low_latency_present`) and the short sound path (`low_latency_audio`: Godot's audio
output latency 5 ms, which WASAPI gives as 4.7 against 10.0 for the stock 15, and Godot's physics step at 240 a second). **Behind an
off key**: Draw Ahead (`draw_ahead`), which departs from the original's drawing (docs/deviations.md) and wants a look at its
corrections. Early Tick (`early_tick`) remains the 1997 look's only: the modern look waits for the tick as before (Main.cs), which is
most of the modern look's 30.6 ms; giving it Early Tick too would be the next step there.

**The camera.** What follows the car with a delay, all on the render clock as the original's (round 1, row 8); none changed here:

| Smoothing | Rule | Time constant | Measured |
|---|---|---|---|
| Chase view (F2) heading | slerped to the car's flattened heading at 2·dt a frame | 0.5 s (first order) | M01, a hard turn: the camera's heading trails the car's by a median 10.6° while it turns; the delay that best lines the two headings up is 290 ms |
| Chase pitch, yaw and distance (the track keys) | eased at 2·dt | 0.5 s | — (answers the camera keys only) |
| Chase position | none: the collider centre of the car as drawn | 0 | — |
| Mouse orbit (Mouse Camera), wheel zoom, return behind | 18·dt; 1 − e^(−9·dt); 3.5/s after 4.5 s idle | 56 ms; 111 ms; 0.29 s | — |
| Cockpit head look (glances, the stick's head) | 4·dt (the mouse's head 18·dt) | 0.25 s (56 ms) | — |
| Head sway | three heights eased at 2·dt to the sim's acceleration (which steps a tick) | 0.5 s | — |
| The driver's lean (modern body) | a spring, 70/s², damping 10/s | ω 8.4 rad/s, ζ 0.6 | — |
| Binocular glance | 2·dt | 0.5 s | — |

The chase heading is the one that reads as lag on turn-in: half a second of first-order easing puts the car visibly across the
screen in a hard turn. It is the original's rule (docs/deviations.md), so it stays; a proposal for a port key, not made here: the
heading eased at 4·dt (0.25 s), or followed by a critically damped spring of the same settling time, which halves the angle the
camera trails by and keeps the swing. The cockpit's view turns with the car rigidly; its sway is a feel effect of 0.5 s that does not
move the view's heading.

**The sound: where the 89 ms go** (the gun report, stock settings; the key and the loopback on one clock):

| Stage | Time | How known |
|---|---|---|
| key to the frame that reads it | 4.4 ms (median) | draw log |
| the early tick fires the gun, `CombatView` starts the voice | 0.7 ms (key to `Play` 5.1) | the audio log's stamp |
| a positional voice (`AudioStreamPlayer3D`) starts only at Godot's next physics step | 0-16.7 ms at 60 a second (8.3 on average); 0-4.2 at 240 | Godot's source (the start is in `NOTIFICATION_INTERNAL_PHYSICS_PROCESS`); 240 a second measured 8.6 ms sooner |
| Godot's output buffer | 10.0 ms (stock: 15 asked, WASAPI gives 10); 4.7 at 5 | `AudioServer.GetOutputLatency()`; 5 measured 11.3 ms sooner |
| Windows' mixer and the loopback recorder | about 5 ms | a test tone played through WASAPI and recorded the same way: 5.1 ms median |
| the sample's own build-up | the report reaches its body 25-35 ms in (wmmgun: 2.5 ms of silence, a quiet lead, the bang from 30 ms) | the sample's envelope; the detector (6 dB over the noise above 1 kHz) fires somewhere on that rise |
| the rest (Godot's mix steps, the device period) | about 20-30 ms | by difference |

The tick costs nothing here any more (Early Tick fires the gun in the frame that reads the key), and 15 ms of the rest are now
taken. The sample's lead-in is the original's sound and stays.

**The display, and the floor.** The desktop's display is an AW3425DWM, 3440x1440 at 180 Hz: a refresh every 5.56 ms, scanned top to
bottom, so the captured band (54-84 % of the screen's height) lights 3.0-4.7 ms after its refresh begins. The marker that times the
press goes through the same compositor and the same scanout, so every figure above is the game's own delay over a window that redraws
at once; the absolute key-to-photon time adds the compositor's frame (one refresh, 5.6 ms), the scanout to that line, the panel's
response, and a USB keyboard's polling (1-8 ms) before Windows has the key at all. With the mailbox swap the game answers 11 ms after
a window that redraws at once: two display frames, one to draw the frame after the key is read (a 1997-look frame takes about 4 ms)
and one waiting for the refresh. The method's resolution is one capture frame (5.6 ms); the floor of this measure is about one frame.

Recordings (the lane's scratch folder, not the repository): slow-motion clips of a hard turn in M01's arena with Draw Ahead off and on
(`da-off-m01-slowmo.mp4`, `da-on-m01-slowmo.mp4`, 180 Hz played 6x slower, about 1 MB each).
