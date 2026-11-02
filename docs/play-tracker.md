# Patch stack tracker

The private list behind the `play` branch: every change this fork carries ahead of upstream, what it changes, the evidence
it rests on, and the order to offer it upstream as pull requests. Lives on `play-meta` (merged into `play`) only; never on
a topic branch. `tools/play-branches.txt` is the machine-readable merge order; `tools/sync-play.ps1` rebuilds `play`
(incremental by default, `-Rebuild` after upstream moves).

Evidence classes: **original** = measured in the 1997 retail game (emulated Pentium 166) or read exactly from its own
data; **kit** = measured on the original executable under the owner's patch kit (a proxy; its keys named where they
matter); **spec** = code read against the project's own notes of the original, not run; **local** = an artefact of the
port, checked in the port itself; **ux** = the original has no such behaviour to match (a feature; behind an option with a
deviations row). PR state: none / open #n / merged / withdrawn.

Ranking: bugs players reported upstream first; then measured fidelity gaps with original-game evidence; then gaps with
kit or spec evidence; then features; the simulation clock last, raised as an option when the rest has landed. One or two
PRs open at a time; the next on the same area waits for a reply. Branches made by other sessions (the first fourteen,
in the sibling worktree folder) keep their own commit messages; the rest carry a "Checked:" paragraph.

## A. Reported upstream

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 1 | fix/wheel-suspension-prime | a placed car's wheels start from the ground under them: no drop and bob at mission start, respawn, melee join (#141) | original: stationary car travel exactly 0 from the first substep in T01/T08/T16; kit n=2 within 2.5 cm | none |
| 2 | fix/siren-gpw-loop | a police siren loops as its sound file's play mode says (#48, #51) | original data: 26 stock files are loop mode; kit event trace confirms the one-shot case; the ".45 kill plays the siren" half is the original's own behaviour (answer, no fix) | none |
| 3 | fix/script-camera-cuts | a cut to another placement of the same camera op is a cut, not a blended move (#140) | local: headless t01 1 / t04 6 blended cuts -> 0; live 1997-look capture | none |
| 4 | fix/script-camera-arrived-flag | camIsArrived is cleared by every placing camera call | spec (no stock effect) | with 3 |
| 5 | fix/mission-music-wrap | mission music goes 2..14 again after 14 (#128) | original: the exe's own CD requests n=2 | none |
| 6 | fix/smoke-sky-flicker | 1997 damage smoke no longer flickers over the sky in the cockpit; modern clumping on game time | local: sky change 26.3 % -> 0.31 % over 61 frame pairs (owner report) | none |
| 7 | fix/macos-entitlements-der | macOS: re-sign with rcodesign; bundle check wants Apple's DER entitlements | checked on a Mac: shipped 0.11.0 fails to spawn, the branch launches | open #144 |
| 8 | fix/macos-mic-opened-on-first-cb | the microphone is opened at the first CB press on macOS, so the permission prompt comes then (supersedes the docs-only fix/macos-mic-prompt-on-first-cb, not merged) | local (owner report); unverified on a Mac | none |
| 9 | fix/middle-wheel-suspension | six-wheelers' middle pair gets its own suspension (#124; the tank has no middle pair, its sinking is the body's pose) | spec | none |
| 10 | fix/predicted-shots-of-jammable-weapon | online, a weapon under 70 % no longer draws shots that are then taken back | local measured: 62 % of such shots were taken back | none |
| 11 | fix/own-shots-counter-at-respawn | the debug shots counter no longer jumps at a respawn | local | with 10 |
| 12 | fix/net-ping-low-percentile | the shown ping is the lowest of the recent samples, not the transport's inflated one | local measured: 60 ms link at 10 % loss showed 155-624 ms | none |
| 13 | fix/escape-menu-right-click-reconfigure | right-click rebinding works in a network game's Esc menu (#106) | local: the fly camera grabbed the press; live repro | none |
| 14 | fix/first-garage-load-prewarm | the 3D garage is pre-warmed from the idle menu (#143) | local measured: first entry 1.2 s -> 0.25 s | none |
| 15 | fix/load-card-audio-hiccup | the carried menu music plays through the loading card's pause (#142) | local measured: 0.7 s of silence -> 0 (n=3) | none |
| 16 | fix/first-explosion-stutter | the first building blast's sprite look is made behind the loading card (#116) | local measured: 122-134 ms -> 21-42 ms | none |
| 17 | feat/regen-pad-hold-readout | the net HUD shows the regen pad's 30 s hold (#102; the rule is the original's, confirmed in the retail exe) | original (rule) + ux (readout) | none |
| - | todo | #95 smoke through terrain and #118 bridge underside did not reproduce; #84 hood deform, #133 AMD Vulkan, #99/#105/#112/#123 renderer items untouched | - | - |

## B. Measured against the original (retail 1.0 on an emulated Pentium 166)

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 18 | fix/taurus-load-cornering | Taurus's car takes load-based cornering as the original sets it at load | kit: t02 race 94.8 s vs I26 100.5 s, with the flag 95.3 s | none |
| 19 | fix/difficulty-damage | Difficulty Level reaches the combat: player rounds x2.0 Wimp / x0.75 Badass offline, x1 online | kit: damage per round Champ 25.3, Badass 18.0 (n=2); spec factors | none |
| 20 | fix/ai-turn-speed-at-spawn | the AI's turn speed is computed at spawn with the default divisor and on wheel damage, not from the live car | original: 13 AI cars 12.52198 m/s, zero spread over 2 boots; I26 13.09 / 14.36 | none |
| 21 | fix/ai-avoid-scripted-lead-car | the blocked-ahead probe sweeps the car's origin against the other cars' boxes: T02's opening no longer backs away from Taurus | original: camera released 13.9-14.0 s (2 boots); I26 20.17 -> 13.65 s | none |
| 22 | fix/fsm-timer-rounds-to-nearest | script timers round the start time to the nearest second | original exe bytes exact; kit 6 runs | none |
| 23 | fix/rocket-catchup-eases-by-frame | a burst's rockets all leave at the frame's eased speed | original: 13 rockets, 2 boots, residual <= 0.003 m/s | none |
| 24 | fix/brake-stop-keeps-accel-one-tick | the nose settles the tick after a braking stop, not on it | original: 34 stops, 2 boots | none |
| 25 | fix/mirror-fov-and-far-clip | rear mirror 45 deg across, 100 m far clip (both looks) | original: 43 +- 1.5 deg measured, nothing past ~100 m; static exact | none |
| 26 | fix/sky-scroll-counts-mirror-pass | 1997 look: clouds drift twice as fast with the full cockpit mirror | original: 2.000 sky calls a frame with the mirror on, 1.000 off (4 boots) | none |
| 27 | fix/engine-loop-pitch-formula | engine loop pitch follows the original's frequency formula | original: formula read exactly from the retail exe; I26 a placeholder ~7 semitones low at idle | none |
| 28 | fix/ordnance-live-cap | at most 160 live rounds per kind, 40 online for droppers, oldest evicted | kit (same bytes as retail): cap fields, live plateau 41 online | none |
| 29 | fix/ai-avoid-probe-left-track | the avoidance no-go probe reads the left track | spec; a vanilla trace exists | none |

## C. Kit or spec evidence (code read against the original's rules)

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 30 | fix/1997-tracers | 1997 look: MG tracers a one-pixel line over the front 65 % of the step, cannon the whole step, no cap | kit: same-pose pairs match | none |
| 31 | fix/modern-target-health-bar | modern look: the target bar has the original's 30 x sqrt(hp) geometry | spec | none |
| 32 | fix/bookmark-cancel-returns | Save Bookmark's Cancel returns to the screen that opened it | ux (the original's flow is not recorded) | none |
| 33 | fix/map-voice-line-on-sfx-bus | the map's line follows the SFX slider | kit event trace n=3 | none |
| 34 | fix/radar-sweep-and-pings | 30-frame sweep, LED/growl to 600 m, ping per sweep | exe bytes / spec | none |
| 35-36 | fix/1997-road-light-no-depth-fade, fix/1997-road-texture-sampling | 1997 roads lit without the depth fade; bilinear without mips | spec; kit pairs | none |
| 37 | fix/dash-mirror-slot-position | the 2D dash mirror in the original's slot, cropped | kit pairs | none |
| 38 | fix/overview-zoom-per-tick | F10 zoom steps per tick | spec | none |
| 39 | fix/hood-view-shows-own-car | F6 hides the player's own car as the original does | spec | none |
| 40 | fix/brake-lamp-hold-row | the 150 ms brake-lamp hold is modern-look only; row amended | spec | none |
| 41-44 | fix/round-start-offset-only-with-spread, fix/trigger-repress-click, fix/unlimited-ammo-ai-keeps-firing, fix/burst-rearm-after-batched-shot (test only) | fire-timer and round-spawn details | code vs the original's code | none |
| 45 | fix/ai-ezk-mortar-fires | the EZK mortar gets its AI fire gate | spec | none |
| 46 | fix/rounds-hit-signs-and-posts | rounds stop at signs and posts; a direct explosive knocks them off | spec | none |
| 47 | fix/ai-sight-line-and-structures | the AI's line of fire runs centre to centre at +1.5 m and through structures | spec | none |
| 48-51 | fix/splash-damage-no-isshot, fix/turret-slew-online-and-elevation, fix/zero-length-hit-zone, fix/rough-terrain-damage-surface | damage and turret details | spec | none |
| 52-56 | fix/cb-queue-with-voice-off, fix/gpw-volume-field, fix/silent-dropper-hit-sounds, fix/sound-cull-from-player-car, fix/sfx-voice-slider-curve | sound rules | spec; eimarx measured | none |
| 57-58 | fix/ai-back-away-terrain-height, fix/ai-avoid-chooser-order | back_away reads the terrain; the chooser's order and fallbacks | spec | none |
| 59-61 | fix/ai-target-predictor-arc, fix/ai-dropper-and-mortar-gates (stacked), fix/ezk-mortar-uses-predictor (stacked) | the target predictor's rows, gear term and +0.09 s; droppers and mortars use it | spec (the right-turn case is the original's own) | none |
| 62-63 | fix/ai-attacker-slot-cap-zero, fix/ai-friendly-fire-scan | slot and friendly-fire details | spec | none |
| 64-65 | fix/explosion-template-by-key, fix/heated-seats-ammo | first registration of an effect key wins; Heated Seats x1.1 ammo | spec (T05 pump 85 m, S07 stations 25 m) | none |
| 66 | fix/death-camera-orbit-rate | death and end-of-match orbit 8 deg per tick | spec (end orbit); death by analogy | none |
| 67-77 | fix/contact-order-statics-last, fix/emplacement-placements-grounded, fix/ramp-rails-not-drive-on, fix/sphere-part-colliders, fix/modern-physics-sphere-parts (stacked), fix/breakables-per-part-spheres, fix/ai-predictor-clearance, fix/blown-tyre-pull-scripted-drive, fix/fatal-landing-ends-substep, fix/network-heli-lift-at-init | physics and collision rules | spec (T11 gun -1.97 m measured on the install) | none |
| 78-86 | fix/trip-cheat-marker, fix/mission-default-car, fix/salvage-specials-amount, fix/set-user-radar, fix/start-engine-pulse (test only), fix/is-shot-two-frames, fix/teleport-path-node-terrain-y, fix/tactic-skid-modulus, fix/movie-skip-deviation-row | mission, trip and script rules | spec | none |
| 87-88 | fix/end-of-match-brake, fix/network-deviation-rows | the winner's 10 s brake; two deviation rows | spec (exe read) | none |

## D. Features (each off by default behind an option, with a deviations row)

| order | branch | what it changes | PR |
|---|---|---|---|
| 89 | feat/head-tracking-opentrack | head look from opentrack's UDP output; glance overrides; 150 ms ease-back | none |
| 90-91 | feat/original-save-reader, feat/import-original-saves-ui (stacked) | import the original game's saves as bookmarks and a trip car (18 real saves checked) | none |
| 92 | feat/bookmark-per-mission | "All Scenes": a bookmark per mission with its stock car | none |
| 93 | feat/wheel-force-feedback-windows | the original's effects on a DirectInput wheel or stick (untested on hardware) | none |
| 94 | feat/rumble-strength-and-events | Rumble Original / Enhanced / Off and a strength | none |
| 95-98 | feat/net-scoreboard-sticky-with-kick, feat/net-ban-list, feat/net-return-to-server-list, feat/host-rule-ignition-stays-on (protocol 22) | #107, #117, #122, #108 | none |
| 99-102 | feat/coop-enemy-scaling, feat/coop-damage-factor, feat/coop-joiner-matches-speed, feat/bot-skill-from-difficulty | co-op host options; melee AI from Difficulty | none |
| 103-108 | feat/field-of-view-row, feat/resolution-row (#81), feat/update-check (#138), feat/hood-matte-option, fix/modern-mg-tracers-look, feat/render-scale-row | Graphic Detail rows and the modern tracer look | none |
| 109-110 | fix/1997-look-draw-distance-default, feat/ghost-cockpit-toggle | Original clip under the 1997 look on a fresh install; the `]` ghost cockpit | none |
| 111-113 | feat/skip-cutscenes-and-mute-switches, feat/voices-overlay-folder, feat/extra-music-folder | unattended switches; a voice overlay folder; the player's own music | none |
| 114 | feat/steam-deck-support | playing.md section, a Steam Input layout, add-to-steam script | none |
| 115-119 | feat/pad-shift-layer, feat/tap-or-hold-binding, feat/look-back-context-bindings, feat/device-presets-by-name, feat/second-device-together | pad shift layer; tap/hold; look-back context; wheel and stick presets; two devices | none |
| 120-122 | feat/input-record-and-replay, feat/telemetry-udp-out, feat/controls-key-sheet | tick-exact input replay; telemetry packets (own, OutSim, OutGauge); a printable key sheet | none |
| 123 | feat/dev-menu | a Developer panel off for players | none |

## E. Last: the clock

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 124 | feat/sim-rate-24 | Simulation Rate offers 24 Hz (85/2048 s), the original's step under a 20 fps cap on Windows NT | kit / Windows capture: about 24 steps/s; the bounce tempo gap 28 vs 24 substeps/s | none (an option, raised last) |

## F. Round 2 (2026-11-02)

Merged into `play` on 2026-11-02 in this order (the first five by sync-play, the rest by hand). PR order is the order of
offering upstream; the stacked branches (on `play`) go up only after what they stack on.

| order | branch | what it changes | evidence | PR order |
|---|---|---|---|---|
| R1 | fix/editor-tool-column-scroll | the map editor's tool column scrolls when it is taller than the window (#146) | local: Game tests (6 new), editor captures | after A |
| R2 | fix/gate-fence-grounding | fences and tyre walls stand centred on their line between the posts (#137) | local: Formats tests, captures | after A |
| R3 | fix/1997-effect-sprites-upright | 1997 look: effect cards stand upright with their foot at the origin (#105) | local: Formats tests (EffectQuad, FireballCard) | after A |
| R4 | perf/horizon-early-z | the modern look's mountain ring is shaded only where it shows (depth pre-pass, rim pass split into horizon_terrain.gdshaderinc) | frame profiles: a fifth to a third of the GPU time back (GTX 1080 Ti, GTX 1650 Ti) | C2 |
| R5 | feat/delete-bookmarks | Load Bookmark has Delete (yes/no notice) | local: Game tests (2 new), captures | D |
| R6 | fix/mouse-in-window-menus | Keep Mouse In Window holds the pointer in the menus too (#147) | local (issue report) | after A |
| R7 | feat/net-handicaps | host rule Handicaps: a level -3..+3 per player scales their damage (x0.55..x1.6); listing flag, accept byte, scoreboard VarU | local: HandicapTests (Net), HandicapDamageTests (Sim) | D, after the ignition rule |
| R8 | perf/render-scale-down | Render Scale 50/67/75/85 % with FSR 1.0 (amended: no mip bias of our own, the engine adds log2(scale) itself) | frame profiles: T01 10.5 -> 7.6 ms at 75 % (GTX 1080 Ti), 14.1 -> 9.7 (GTX 1650 Ti) | stacked on play, after the render scale row |
| R9 | feat/map-edge-treatment | Graphic Detail Map Edge: Original / Haze / Fade Out for the modern look's boundary walls | local: MapEdgeTests; t05 Fade Out capture after the merge, no rim sliver | D |
| R10 | feat/dev-menu-mission-picker | Developer panel: Scene row (any mission, any car), started at once | local | stacked on play, after feat/dev-menu |
| R11 | feat/dev-menu-landmarks | Developer panel Teleport: generated landmarks and kept places | local | stacked on play, after R10 |
| R12 | feat/dev-menu-loadout | Developer panel Loadout: swap a mount's weapon live, Refill (snapshot: each weapon's file in the car's make-up) | local: DevLoadoutTests | stacked on play, after R11 |
| R13 | feat/original-behavior-pad | Original Recipe pad: 12 rows (Physics, Live Ordnance, Melee A.I., Draw Distance, Field of View, Rear Mirror Range, Sky Drift, Brake Lamps, MG Tracers, Death Orbit, Music Tracks, Rumble). Amended before merging: the AI Droppers row, `[port] ai_droppers`, `Combat.StraightDropGate`, its test and deviations row removed | local: OriginalRecipe 11/11, Sim AiDrop/OrdnanceCap/SnapshotGuard 19/19 | stacked on play, last of D |
| R14 | fix/ai-dropper-gate-straight-line | the AI's dropper gate predicts a pursuer on a straight line at its arrival time again: dropped on at any speed | original: measured 2026-11-02 (retail 1.0, emulated Pentium, 2 boots) | stacked on play; C |
| R15 | fix/turret-lead-predictor-delay | a turret leads through the target predictor: closest-approach time at the aim speed + 0.09 s, along the turning circle | original: measured 2026-11-02 (retail 1.0, emulated Pentium, 2 boots) | stacked on play; C |
| R16 | fix/cockpit-art-bilinear | the 3D cockpit's z* art sampled bilinear on the 3D card and in the modern look (Software unchanged): the dash looked down at is no longer blocks | local: CockpitSamplingTests 3 (owner report) | after A |
| R17 | feat/net-auto-handicap | Handicaps Off / Manual / Auto: Auto sets each player's level from the standing (a level per 1.5 net kills over the table's average, -5..+5, one step a tick, a dead band), said in the feed; the accept carries the mode as a byte | local: AutoHandicapTests (Sim), HandicapTests and AutoHandicapSimTests (Net); melee-sim over ten seeds: spread 12.3 -> 7.7 kills | stacked on feat/net-handicaps (merged against it) |
| R18 | fix/look-choice-rows | one LookChoice helper; a look-following choice is stored only when its value changes; shell.cfg version 12 drops the draw_distance Far 0.11.0 wrote on every save | local (design review) | stacked on play |
| R19 | fix/coop-damage-remainder | Co-op Damage below x1 carries the fraction per car and channel, so small hits (flame) still add up | local (design review) | stacked on play |
| R20 | fix/pad-rules-shift-tap-latch | pad rules: the shift wins over tap/hold, a 200 ms tap, the handbrake latch | local (design review) | stacked on play |
| R21 | fix/esc-aa-row-names | the Esc menu's Anti-Aliasing row lists 8x as the main menu's does | local (design review) | stacked on play |
| R22 | fix/scoreboard-host-guards | a double-click on the locked board asks before booting; /ban refuses unknown names; picks by slot order | local (design review) | stacked on play |
| R23 | fix/input-trace-header-and-flush | input recordings start with a header (build, install, options) and flush every row; developer actions marked | local (design review) | stacked on play |
| R24 | fix/telemetry-rate-and-time | telemetry capped at 60 packets a second, with monotonic time | local (design review) | stacked on play |
| R25 | fix/2d-reticle-follows-fov | the 2D dash's gunsight is projected through the chosen Field of View | local (design review) | stacked on play |

Versions on `play` (pass 2): snapshot 24 (21 the host's handicap on each car, appended after the AI turn speed; 22 each
weapon's file in a car's make-up; 23 Auto handicap, `MeleeRules.AutoHandicaps` after the ignition lock and
`MeleePlayer.Form`; 24 Co-op Damage's carry per car and channel), protocol 24 (22 the ignition lock, 23 Handicaps, 24
the Handicaps mode: the accept ends with the ignition byte, then the mode byte 0/1/2; the listing's flags byte has 8 for
on and 16 for Auto). WireTests pins 24 / 24 (`TheProtocolIs24`); CHANGELOG's network line and docs/multiplayer.md
follow.

Hand fixes in round 2 (outside conflict hunks or beyond either side):
- Bookmarks.cs: one LoadBookmarkScreen.Build (feat/delete-bookmarks added a second): Import Saves keeps the place left of
  Load, Delete goes after All Scenes on the top line (left of Load in Nitro, which has neither).
- feat/net-handicaps: host hot id 43 for Hcp (40-42 are play's co-op Scaling, Damage and Rolling Start), in Hot and
  both Live lists; Discovery/MeleeHost/JoinAccept carry IgnitionStaysOn and Handicaps together; FlameTouch applies
  Difficulty's factor and the handicap; server --help and the console's command list keep both; VehicleSave writes
  TurnSpeed then Handicap (every older field in place).
- feat/map-edge-treatment: Map Edge is Graphic Detail row 21 in the shell sheet (16-20 taken), row 16 in the Esc pad;
  Reset/Cancel use play's `int? DrawDistanceChoice`; its haze lines went into horizon_terrain.gdshaderinc (after
  perf/horizon-early-z's split) outside the HORIZON_RIM_PASS block, so both passes get them.
- Stacked branches (perf/render-scale-down, the three Developer panel branches, the pad) were built on an earlier
  `play` (7c3988f, same tree as 6ff52d7): merged with that commit as the merge base (`git merge-recursive 7c3988f --
  HEAD <branch>`, MERGE_HEAD set), so only their own commit is merged; their history brings the earlier play's
  commits along.
- feat/net-auto-handicap (pass 2): merged against feat/net-handicaps (54dd5cc) as its base. Host hot id 43 steps Hcp
  through Off/Manual/Auto (co-op: Off/Manual); the co-op rows keep CO-OP DAMAGE beside HANDICAPS:. The handicaps doc
  comments and docs/multiplayer.md say 23 for the rule and 24 for the mode. AutoHandicapSimTests' pinned numbers
  re-pinned to play's game (the rule's assertions unchanged and passing; the bots' game differs on play by the other
  sim fixes): off kills 4,1,1,0,0,0; auto kills 5,2,1,0,0,0, levels -3,0,0,1,0,1, moves 1:-1 1:-2 3:+1 1:-3 5:+1.
- docs/deviations.md (in the merge of fix/look-choice-rows): the stale second "View distance" row (Far the default) and
  the stale second "Vehicle lamps" row (the port's 0.15 s always) removed; the current rows stay.
- Note, not fixed (a follow-up lane): the Esc Graphic Detail pad's Done and Cancel write the resolved draw distance,
  which pins DrawDistanceChoice on any visit.

Test status, 2026-11-02, `play` with round 2: desktop Game 167/167, Editor 216/216, Net --filter Wire|Handicap|Discovery
51/52 (RestrictionTests.ARefusedJoinGetsNoSeatOverTheWire, in the desktop baseline). Laptop (play at 7fadc78, before
fix/cockpit-art-bilinear, which touches only game/): Sim 936/967, Net 411/429; 38 failures, 4 not in the laptop
baseline: the two known ones (CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz) and two network-timing
tests, BotTests.ThreeBotsPlayAMinuteOfMeleeOverALossyLink and PredictionTests.ThePredictedCarAgreesWithTheServersOverALaggyLink,
which pass on the desktop on a re-run (flakes under the laptop's load, not re-pinned).

Test status, pass 2 (`play` at 5c03c59, round 2 plus auto handicap and section H): desktop Game 183/183, Editor 216/216,
Net --filter Wire|Handicap|Discovery 54/55 (RestrictionTests.ARefusedJoinGetsNoSeatOverTheWire, desktop baseline).
Laptop: Sim 947/978, Net 419/434; 36 failures, 2 not in the laptop baseline, the two known ones
(CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz); the two network-timing tests of pass 1 passed.

## I. Round 3 (2026-11-02 afternoon): the playtest's answers

Merged into `play` on 2026-11-02 by sync-play (incremental; rerere replayed the hand resolutions recorded in a scratch
pass of the same merges, in the same order). The four master-based branches sit in C2, C and D of the manifest; the eight
stacked ones and the integration branch are section I, after H.

| order | branch | what it changes | evidence | screenshots | status |
|---|---|---|---|---|---|
| C2 | perf/interop-garbage | the cars' drop-shadow casters are found once, a shadow's node is told only what changed, the smoke layer keeps its uniform set (frame profiler: gc0_per_100) | frame profiles: process step T01 2.08 -> 1.77 ms, T05 3.04 -> 2.38 (GTX 1650 Ti), T01 3.85 -> 2.84 (GTX 1080 Ti); finalizer thread 6.3 -> 3.0 % of samples; captures bit-identical | - | merged |
| C | fix/cockpit-gauge-dials-too-dark | the 3D cockpit's dials black under their lettering (an opaque face draws its keyed texels in index 255's colour, not the bleed), the cockpit art read at its full-size level, nearest; the needles in the palette colour the original's flat-face lookup gives (entry 160 on T01), lit, turned off-centre with the reading side 0.69 of the length; its earlier "Cockpit gauge needles" deviations row gone | original (86Box, retail 1.0, software renderer, 640x480, T01 by day, n 1 boot): every needle pixel a shade of entry 160; the pivot 0.70/0.71 (speedometer) and 0.66/0.70 (tachometer); the speedometer face's pixels darker than 32/255 68 % original, 33 % before, 61 % (1997) / 64 % (modern) now; local: Game CockpitNeedleTests 4 | `_scratch/r3-needle-2/` (captures beside the emulator's) | merged (6a01da1, pass 4) |
| D | feat/hd-world-textures | the modern look draws a player's upscaled texture from user://tex-world/ (or a pack zip's tex-world/) in a texture's place, 2x/4x/8x; the 1997 look keeps the originals; nothing ships | local: Formats HdTextureTests, Game CockpitPanelsBlitTests | `_scratch/r3-upscale-v1/shots/{front,cockpit}-{original,hd}-1280x960.png` | merged |
| D | feat/hd-texture-tool | `--export-textures DIR` and tools/upscale-textures.py (the 4xTextures_GTAV_rgt-s_dither model, CC-BY-4.0, named in the pack) make such a pack from the player's own install | local: 6,563 PNGs exported from a base install; the lab's pack stays outside the repository | as above | merged |
| I1 | fix/turret-aim-moving-carrier | a turret aims at the predictor's point from the barrel; nothing taken off for the carrier's own travel; the AI gun gate and friendly scan test the round as it flies here | original (measured 2026-11-02, see below): rounds never inherit the carrier's velocity and the original's turrets hit from a moving carrier; local: TurretLeadTests 188/200 hits at 20 m/s (was 1) | `_scratch/r3-sim-fixes/shots/turret-chase-{modern,1997}.png` | merged; CheckpointTests (T14) moves, see known failures |
| I2 | feat/original-recipe-folded | the Original Recipe pad folded away: Unlim. Ordnance a Play Options row, Rumble into Force Feedback (Off / Pad* / Pad Enhanced / Wheel), Draw Distance no row anywhere, the rest `[port]` keys with a "Settings without a row" table in docs/playing.md; Sky Drift steady by default (deviations row updated: we differ on purpose); the original's value marked `*` (ShellSettings.OriginalMark); settings version 13 | ux (the owner's notes: keep the menus simple); local: SettingsWithoutARowTests | `_scratch/r3-menus-fold/shots/{esc,shell}-*.png` | merged |
| I3 | fix/graphic-detail-scrollbar | Graphic Detail scrolls as one list (the original's six rows and the port's) on the pad's fourteen lines; the bar at the right edge after the values | local (owner report) | `_scratch/r3-menus-polish/shots/scrollbar/*.png` | merged |
| I4 | feat/field-of-view-in-graphic-detail | Field of View on the Esc pad too; Monitor (the original's height, the width from the window) the default, 60..110 exact degrees across the window, 90* the original's; `[port] fov` in degrees across the window | ux; local: ViewFovTests | `_scratch/r3-menus-polish/shots/fov/*.png` | merged |
| I5 | feat/developer-menu-entry | `developer=true` in shell.cfg (developer_tools read too); the Developer entry at the bottom of the Esc Options Menu; Ctrl+` as well as Ctrl+F2 (Mac keyboards, the Godot editor) | local: DeveloperFlagTests | `_scratch/r3-menus-polish/shots/dev-entry/*.png` | merged |
| I6 | fix/delete-bookmark-from-parts-form-1997 | Save Bookmark (the salvage form's and garage's sheet) has Delete too | local (owner report): BookmarkDeletePlaceTests | `_scratch/r3-fixes-shell/shots/01-save-bookmark-delete-from-salvage-{modern,1997}.png` | merged |
| I7 | fix/1997-tracers-two-pixels | 1997 look: the MG tracer line one pixel per 480 rows of the 3D view (two at 1080p, four at most), a screen-space quad | original: a one-pixel line at 640x480; local: TracerLengthTests (owner report: a hair's breadth) | `_scratch/r3-fixes-shell/shots/04-tracers-1997-{1280x960,1920x1080}.png` | merged |
| I8 | feat/map-edge-hide | Map Edge gains Hide: the walls and the plateau behind them drawn down at the plain of their foot (EdgeWalls in Formats, the terrain's vertex shader, the ring from the plain); Original (Plain*) stays the default | ux; local: EdgeWallsTests; measured over the 114 stock and Nitro maps (terrace steps 5-40 m, plateaus 400 m deep) | `_scratch/r3-map-walls/shots/{m01,t05start,t05se}-{original,haze,hide}.png` | merged |
| I9 | fix/round3-integration | the integrator's hand fixes beyond the conflict hunks (listed below) | local: Game tests 206/206 | `_scratch/r3-integration/shots/esc-graphic-detail-end-{modern,1997}.png` | merged |
| I10 | feat/hd-textures-row | Graphic Detail's HD Textures row (Esc pad and the main menu's sheet, shown when a pack is found): Off\* the game's own world, car and cockpit textures, On the pack's upscales, live; HD Menu Art stays its own row | ux; local: HdTexturesSettingTests | `_scratch/r3-hd-row/shots/{esc,shell}-gd-{modern,1997}.png`, `t01-{cockpit,chase}-{on,off}.png` | merged |
| I11 | feat/saves-tools-to-dev-panel | Load Bookmark is the original's sheet plus Delete; Import Saves and All Scenes move to a Developer panel on the main menu (with `developer=true`) | ux (the owner's notes: keep the menus simple); local: BookmarkDeletePlaceTests | `_scratch/r3-saves-declutter/shots/0{1..5}-*.png` | merged |
| I12 | fix/round3-integration-2 | the integrator's hand fixes of pass 4 (below): the cockpit sampler after the dials, the network handguns row, the .45 and Q in playing.md | local: Game CockpitSamplingTests | - | merged |
| I13 | fix/held-keys-stale-modifier | a modifier the engine still holds is let go when a later key or mouse event's flags say it is up (Controls.Observe), so held keys no longer act on the press only after macOS keeps a release (Ctrl+F2 to the menu bar); scripted keys carry the script's modifiers | local: Formats StaleModifiersTests 3; on Windows a lost Control release crept at 0.5 throttle before, 47.9 by frame 420 after; not observed on a Mac | - | merged |
| I14 | fix/shell-music-no-title-tracks | the 3D garage keeps the menu track, as the original's CONFIGURE CHASSIS does; the boombox only with `[port] garage_radio=on`, mission tracks only (I76 2-14, Nitro all but 10 and 4) | spec (the shell loops its menu track on every screen); local: Formats ShellMusicTests 4 | - | merged |
| I15 | feat/net-esc-self-destruct | the network Esc menu's row 2 is Self Destruct (new car), the original's Replay there (Ctrl+Del's flag), asked yes/no, greyed while dead or once decided; Leave / Exit questions say what they do | spec; local: Net EscapeMenuSelfDestructTests 5 | `_scratch/r3-mp-esc/shots/` | merged |
| I16 | feat/net-auto-handicap-2 | auto handicap: a level per net kill over the table's average (band 1); dealt x0.75..x0.2 at -1..-5, +n the inverse to x5; taken 1/factor held to x1/3..x3; rams by the rammer's factor, the .45's 5 points by both | ux (the port's rule); melee-sim m05 six bots, seeds 1-24: leader margin 8.3 (off) / 3.3 (old auto) / 2.0 (new); local: Sim AutoHandicapTests, HandicapDamageTests, Net HandicapTests, AutoHandicapSimTests re-pinned | - | merged; AutoHandicapSimTests passes again |
| I17 | fix/hd-pack-glass-and-key | under an HD pack the window, material-table, paint-mask and material-map analyses read the original's texels (an entry's Base) and sample their maps by UV: glass sees through again, no cyan rim | local: Formats CarMaterialTableTests (5939 glass texels on the 128x128 grid, 340 at 512x512); M01 side captures with and without the pack | `_scratch/r3-pack-fixes/` | merged |
| I18 | perf/hd-pack-load | packs are 2x BC7 .dds (the cockpit's z* art 4x) uploaded as stored, decompressed only where the CPU paints; a model's upscales read and decoded together on all cores; tools/upscale-textures.py writes DDS (etcpak), `--from` repacks an older pack | local: T01 mission build 9.0 s no pack, 9.8 s with the 2x BC7 pack (was 13.4-18.0 s with the 4x PNG pack); the pack 588 MB on disk (1.29 GB as 4x PNG); Formats HdTextureTests | `_scratch/r3-pack-fixes/` | merged |
| I19 | fix/hd-cockpit-panels | under an HD pack: the mirror's surround dropped within 0.1 of its corner colour (no cream box); the radar's blips, contact LED and range lamp at the frame's own scale; TextureFactory.Swapped after SetHd reloads the panels, radar frames, wheel and arms at the new scale (the HD Textures row and the look flip live, the 1997 look always the originals); the tool's `--pixel` scales the labels, ammunition digits and mirror frames 4x without the model, in their own colours (zdue now in the pack) | owner's playtest (five cockpit faults with the 2x BC7 pack); local: Game RadarBlipTests 3, Formats HdTexture 5; the radar sweep "slow and backwards" not reproduced (same 30 frames, 0.2 s step, clockwise, with and without the pack) | `_scratch/r3-hd-cockpit/shots/` | merged; the lab pack's eight regenerated files copied to both playtest machines |
| I20 | fix/hd-dream-grey | the dream (T04) grey again with a pack: the entries the mission's palette greys and t01.act colours are found once (HdTexture.Greyed) and an upscale over them takes the mission's grey by luminance, blended between the original's greyed texels (HdTexture.Recolour); road and cars keep their colour, as in the original | spec (t04.act differs from t01.act in 32 entries, the ground and sky ramps); local: Formats HdTextureGreyTests 3; T04 greys 30 entries, T12 7, M15 16, P10 17, T06/T07 2 | `_scratch/r3-hd-cockpit/` (T04 with and without the pack, 1997) | merged |
| I21 | feat/host-form-redesign | the network host form's options one box under NO. DRIVERS in the form's paper and lines (check boxes for the on/off rules, ruled blanks for Max Score, Turrets, Ignition, Rate, Hcp, Pass); an arena sketch from above with its starts, count and size (ArenaSketch in Formats), a band of high ground hatched round the floor | ux; local: Formats ArenaSketchTests 11, Game HostFormLayoutTests 2 | `_scratch/r3-host-form/shots/` | merged (stacked on fd1ad8e; no hunk against pass 5's network branches beyond the changelog) |
| I22 | fix/round3-integration-3 | the integrator's pass-6 fix: DamageModel's HandicapTaken and IncomingFactor each under their own summary (pass 5's note) | - | - | merged |

Measured corrections (the lab's 86Box rig, retail 1.0 on an emulated Pentium 166, 2026-11-02):
- Rounds never inherit the carrier's velocity: the player's forward .50 cal at 18-26 m/s leaves at 149.99 m/s along
  the barrel (n 114, excess median -0.02 m/s; 168-176 if inherited), the velocity field written to 20 m/s with the pose
  held changes nothing (n 56), an AI car's forward 200 m/s gun at 12-17 m/s reads 199.96-200.05 and its rearward gun
  199.5-200.04 (n 21 / 19, 4 boots), rockets +3.6-3.9 m/s whatever the speed (the speed field's half-step ease, no
  carrier term). Spawn stays as it is.
- The original's turrets hit from a moving carrier: the player's .50 cal turret 19/19 at rest and 37/37 driving past at
  21-23 m/s (miss 0.1-0.7 m, median 0.2; an aim that took the carrier's velocity off would miss by 5 m), T11's
  helicopters 195/229 (85 %, median 48 m/s) and 43/45 (96 %), a T14 police car 15/24 at 2.6-17 m/s (n 24, the target
  jostled). So fix/turret-aim-moving-carrier is faithful, not an improvement. Not covered: the lead on a moving target
  from a moving carrier (second order).
- Trip mission 5's cliff (the owner's note): no bug. The fall rule is faithful: a drop of over 35 m kills, as the
  original's does.

Pass 4 (2026-11-02 evening): the dials branch's new tip and the two stacked branches merged by sync-play (incremental;
rerere replayed the resolutions of a scratch pass of the same merges, the trees identical), then fix/round3-integration-2.
- The dash finding: the dial faces of the z* dash art are index 255, the colour key, but the dash's faces do not honour
  the key; the original draws those texels in index 255's colour (black on T01). The loader's bleed under keyed texels
  (for filtering) had greyed them, and a mipmapped read greyed the lettering too. The dials branch draws the keyed texels
  of an opaque cockpit face in the key's colour and reads the art at its full-size level, nearest. Keyed opaque faces
  outside the cockpit still show the bleed (the 1997 look's world objects may share the cause): open.
- Hand resolution, dials over fix/cockpit-art-bilinear and the HD textures (conflict hunks, rerere keeps them):
  object.gdshaderinc keeps the bilinear branch's name (albedo_cockpit) and edge clamp with the dials branch's sampling,
  `filter_nearest_mipmap` and `textureLod(albedo_cockpit, tuv, 0.0)` in every look (Software included: the dials were
  measured on the original's software renderer); MaterialLibrary sets albedo_cockpit and the dials' key_fill;
  TextureFactory's served entry (original or upscale) carries KeyColour from the original's first keyed texel (a
  helper, since the HD branch builds entries through Prepare); CHANGELOG drops the bilinear line and the old needle line
  for the dials' one. The bilinear branch's scope was the cockpit sampler alone, so nothing of its smoothing remains:
  the owner's "slanted blocks" report (dash looked down at on a large window) is answered by the measurement, to be
  looked at again in the next playtest.
- fix/round3-integration-2: CockpitSamplingTests pin the point sampling; the unused i76_point_clamped macro goes; the
  In-car view row says full-size and point-sampled with the key's colour on the dials; the Network melee in game row
  gives every human's car its .45s, as docs/multiplayer.md does, whose stale "later phases" handgun line goes;
  docs/playing.md says what Q picks (15 degrees of the nose, radar range, else cleared) and how the .45 finishes a
  crippled car (a side glance held 0.5 s, then fire; under a third straight to a critical part).

Pass 5 (2026-11-02 night): the six branches above merged by sync-play (incremental; rerere replayed the resolutions of a
scratch pass of the same merges in the same order, the trees identical). No code conflict: the HD branches sit on
feat/hd-textures-row already and the two network branches on fix/round3-integration-2, so HdTextures.cs, TextureFactory,
MaterialLibrary, NetHud, EscapeMenu and Handicaps merged without a hunk.
- CHANGELOG.md: union at each merge (garage music, the two network lines under New, the two HD lines under Fixed);
  perf/hd-pack-load's own copy of the glass line kept once.
- docs/deviations.md: the network Esc row ("Escape menu in a network game") put before "Settings without a row", whose
  text is the garage-music branch's (garage_radio in the key list), not the Esc branch's older copy of it.
- Seen, not changed: in feat/net-auto-handicap-2 the HandicapTaken helper's summary sits under IncomingFactor's, which
  is left without its own (a doc-comment slip in DamageModel.cs, no behaviour); for the next integration branch.
- Self Destruct counts a death like Ctrl+Del, so it moves the auto handicap's net kills as the key does.
- HD pack numbers (T01 --drive --chase, seed 7, mission build from the hitch line, a shared loaded machine): 9.0 s without
  a pack, 9.8 s with the 2x BC7 pack; the pack 588 MB on disk.

Versions after pass 5: snapshot 24, protocol 24, shell.cfg 13, unchanged (garage_radio is written only when on);
WireTests' TheProtocolIs24 holds.

Test status, pass 5 (`play` at d1b9c17, before the play-meta merge): desktop (I76_INSTALL and I76_NITRO_INSTALL set)
Game 214/214, Sim 959/992, Net 424/439; outside the desktop baseline only the known four (CheckpointTests T14,
CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the Nitro install set). Laptop: Sim
958/990, Net 424/439, Game 214/214, Formats 889/924 (30 skipped), Editor 216/216; outside the laptop's baseline the three
known Sim ones. AutoHandicapSimTests leaves the known list (re-pinned by feat/net-auto-handicap-2). No new failure.

Pass 6 (2026-11-02 night): the three branches above and fix/round3-integration-3 merged by sync-play (incremental; the
trees identical to a scratch pass of the same merges). Upstream moved meanwhile (origin/master 32988ae, ten commits past
0.11.0: map editor, memory, car materials, macOS signing); play stays on 5192e29 until a `-Rebuild`, and the two stack
branches not checked out in a worktree (fix/round3-integration, fix/hd-pack-glass-and-key) did not rebase onto it and
were merged as they stand (no ref moved).
- TextureFactory.cs: fix/hd-cockpit-panels' `Swapped` event and fix/hd-dream-grey's `PackPalette` (with `_greyed`) added
  at the same place: both kept, Swapped first (a conflict hunk rerere keeps). Both work on the served HdPic, so a greyed
  upscale is what the panels reload after a swap.
- CHANGELOG.md: union at each merge (the HD cockpit and dream lines under Fixed, the host form under New after the
  network Esc and auto handicap lines).
- feat/host-form-redesign (base fd1ad8e): its files and pass 5's network and shell changes do not overlap outside
  CHANGELOG.md, docs/deviations.md and docs/multiplayer.md, which merged by git; the Hcp blank steps the same Off / Manual /
  Auto mode the auto handicap reads.
- The installed HD pack on both playtest machines: the eight files fix/hd-cockpit-panels' tool regenerates (zdse,
  zdse320, zdue (new), zdue320, zmiri101, zmiro101, znbe, znbe320) copied over the user folder's tex-world.

Versions after pass 6: snapshot 24, protocol 24, shell.cfg 13, unchanged (no pass-6 branch bumps any).

Test status, pass 6 (`play` at b0b771b, before the play-meta merge): desktop (I76_INSTALL and I76_NITRO_INSTALL set)
Game 220/220, Sim 959/992, Net 421/439; outside the desktop baseline only the known four (CheckpointTests T14,
CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the Nitro install set); Net's three
more failures than pass 5 are all in the desktop baseline (network flakes under load). Laptop: Sim 958/990, Net 424/439,
Game 220/220, Formats 903/938 (30 skipped), Editor 216/216; outside the laptop's baseline the three known Sim ones. No
new failure.

Open after round 3:
- The Mac's head look (the owner's note): open; not reproduced here. A per-machine input.map is suspected. Needs the
  Mac's input.map and a repro.
- Map walls: Original (Plain*) stays the default; Hide is a choice, not the default.
- CheckpointTests (T14) fails with the turret aim fix: the cops' turrets now hit Taurus and the checkpoint race no longer
  plays out as the test pinned it. A consequence of a faithful fix: left failing and listed below; the test needs a
  different mission or car (a follow-up lane).

Hand fixes in round 3 (each a conflict-hunk resolution rerere keeps, unless said to be in fix/round3-integration):
- CHANGELOG.md: the union kept lines the fold replaced (the draw-distance-after-a-visit fix, the Original Recipe pad) at
  every later merge; fix/round3-integration takes them out and puts the Unreleased section back to one New and one
  Fixed part (the two master-based fixes were under New).
- docs/deviations.md: the union duplicated rows the fold had reworded (Play Options rows, Instant Melee's computer cars,
  Escape menu and mission-end menu, In-car view lighting): the fold's wording kept, the dev entry's Hotkeys row and the
  texture, needle and tracer rows added; the Map boundary walls row is the Hide branch's text with Plain\* as the default.
- EscapeMenu.cs: fix/graphic-detail-scrollbar (built before the fold) re-added the Recipe pad and the Draw Distance row:
  ours (the fold) in all three hunks, its FirstRow / VisibleRows, Shown and bar drawing merged by git. Field of View is
  Esc row 11 after Vegetation (Mouse Camera to Map Edge move down one; no Draw Distance row), ResetGraphics puts it to
  Monitor and leaves DrawDistanceChoice alone; the Developer entry's row is computed from the pad's last entry (theirs);
  Map Edge's row takes MapEdge.Names.Length with the comment naming Plain as the original's.
- Overlays.cs: Field of View keeps the fold's slot 16 with the FOV lane's comment (Update Check 17, Hood Finish 18,
  Render Scale 19, Map Edge 20).
- MapEdge.cs / MapEdgeTests.cs: `Original, Haze, FadeOut, Hide` with Names `Plain*`, Haze, Fade Out, Hide; Parse(3) is
  Hide, Parse(4) Original.
- tests/I76.Game.Tests/OriginalRecipeTests.cs: the fold renamed it to SettingsWithoutARowTests.cs while the FOV lane
  edited the old file (its "Original first" exemption); the modify/delete conflict leaves the old copy in the tree and
  sync-play adds it, so fix/round3-integration deletes it (it tests a pad that no longer exists).
- fix/round3-integration besides: "90 (1997)" is "90*" in ViewFov.Names, the deviations row, playing.md and the
  changelog, and SettingsWithoutARowTests' marker test covers ViewFov.Names; `developer` in the "Settings without a row"
  table; the Esc pad's Graphic Detail gets Render Scale (row 16, before Map Edge, live, Reset to 100 %) as the shell's
  sheet has it; the sheet's Map Edge comment names Hide.
- The merge order the script took differs from the manifest's for the master-based ones (dials before interop-garbage):
  harmless, both touch only the changelog in common.

Versions on `play` after round 3: snapshot 24, protocol 24, unchanged (no round-3 branch bumps either); WireTests pins
24 / 24 (`TheProtocolIs24`), SnapshotHeader.CurrentVersion 24, Protocol.Version 24. shell.cfg version 13 (the fold).

Test status, round 3 (`play` at a96407c, the tree the final play-meta merge carries): desktop (I76_INSTALL and
I76_NITRO_INSTALL set) Game 206/206, Sim 952/985, Formats 894/915 (16 skipped), Net 416/434; against the desktop
baseline the new failures are CheckpointTests.ARetryFromTheObjectivesCheckpointIsTheStateItWasTakenIn (T14: the cops'
turrets now hit Taurus, see above), the two known ones (CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30
Hz), AutoHandicapSimTests.SixBotsOfMixedSkillForFiveMinutesWithTheRuleOffAndOn (Net: its kills and levels were pinned
to play's bot game in pass 2; the bots' turrets hit now, so auto kills read 1,2,0,2,0,... against the pinned 5,2,1,0,0,0;
a consequence of the faithful fix, to re-pin once the rule's assertions are checked on the new game), and
CaltropTests.ACarOverADroppedCaltropBlowsThatTyre, which is not round 3's: it fails whenever I76_NITRO_INSTALL is set
(the Nitro layer's caltrop ordnance names a "null" impact effect, so the impact carries no sound) on play before round 3
too, and passes without it; the desktop baseline was run without that variable. Laptop (play at a96407c): Sim 951/983,
Net 417/434, Game 206/206, Formats 881/915 (30 skipped); outside the laptop's baseline the three Sim ones above, the
AutoHandicapSim test, and BotTests.ThreeBotsPlayAMinuteOfMeleeOverALossyLink (the laptop-load flake of pass 1, in the
desktop baseline, passing on the desktop).

Test status, pass 4 (`play` at f83e4a0): desktop (I76_INSTALL and I76_NITRO_INSTALL set) Game 214/214, Sim 952/985;
outside the desktop baseline only the known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam,
T01FollowTests at 30 Hz, CaltropTests with the Nitro install set). Laptop: Sim 951/983, Net 417/434, Game 214/214,
Formats 881/915 (30 skipped), Editor 216/216; outside the laptop's baseline the three Sim ones and
AutoHandicapSimTests' six bots, all known. No new failure. Versions unchanged: snapshot 24, protocol 24, shell.cfg 13
(no pass-4 branch bumps any; hd_textures, when absent, follows hd_menu_art).

## Withdrawn or answered (not offered as fixes)

- "I26 turns 3-5 % more": a harness artefact; in lockstep at 1/24 s I26 matches the kit to 0.1 %.
- "AI flees later on the kit because of a kit fix": at engine 20 % both settings flee alike; the real gap was the turn speed.
- #114 tanker, #113 bus texture, #85 cockpit shadow, #101 pad surface (confirmed in the retail game), #102 regen hold (confirmed), #72/#121 helicopter reverse spin, #111 far creepers, #137 placements: the original's own behaviour; answer with the spec.
- Chase camera pitch (visual pair V5), hpLesser on a building, the jammer sound, the night LOBJ lights, the heli-dies-on-contact row: the spec does not settle them yet.
- Round 2 sweeps (2026-11-02): #132 #120 #131 #126 #115 #71 #119 #123 #69 #127 #19 are fixed upstream already (closable);
  #112 #84 #85 #57 are the original's own behaviour (answer with the spec); #136: the overpass road servers need TLS 1.3,
  which Windows 10 cannot do (no fix here); #133 AMD/Vulkan 1997 look: not reproducible here, suggest D3D12 as the default
  on AMD; #130 #118 #99 need a repro.

## Integration notes (play only)

- Snapshot format: the topics bump CurrentVersion from 15 each (turn speed, user radar, teleport ground, the three co-op
  options, the ignition rule); on `play` it is 20 with every addition saved; WireTests pins 20 and protocol 22.
- Hand fixes made on `play` outside conflict hunks (redo after a `-Rebuild`): one Load Bookmark `Build` for the import and
  All Scenes buttons, both using the renamed `Direct` flag; the Rough(v, surface, dt) stub in FatalLandingTests;
  `ForceFeedback.PadStrength`; `CheatMarker.Mark()` for the dev menu; Graphic Detail rows renumbered 17-20; co-op host
  hot ids 40-42; the AiSightLine test's cars on another team.
- Known test moves documented by their branches: T01 diner dice and T01Follow (timer rounding, brake stop), T14 checkpoint
  re-seeded (turn speed, friendly-fire scan), T06 creepers wait rule (T02 probe). Pre-existing Windows failures: the
  snapshot corpus stack overflow in the test host, symlink and path tests, network flakes under load.
- Test status, 2026-11-02, `play` with all 123 branches, two machines, against origin/master's own failure lists (64
  names on the desktop, 42 on the laptop): Editor 216/216 and Game 145/145 on both; Formats 5 (desktop) / 4 (laptop)
  failures, all pre-existing; Sim 32 / 31 failures and Net 15 failures (laptop), all pre-existing except two:
  CoopFormationTests.AnAiDrivingIntoAHumanIsNotThatHumansRam (with fix/taurus-load-cornering the rear-end contact is
  credited to the lead car: an attribution question for the original, not re-pinned) and T01FollowTests at 30 Hz (the
  test's driver gets stuck 7 m behind Taurus after the timer rounding moved the scene; no number to re-pin). The snapshot
  corpus tests overflow the Windows test host's stack on master too.
