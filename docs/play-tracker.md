# Patch stack tracker

The list behind the fork's `main` branch (called `play` until pass 19): every change this fork carries ahead of upstream, what it changes, the evidence
it rests on, and the order to offer it upstream as pull requests. Lives on `main` only (on `play-meta` until pass 19); never on
a topic branch. `tools/play-branches.txt` is the machine-readable merge order; `tools/sync-play.ps1` merges the new
branches onto `play`; upstream's master is merged into `play` by hand when it moves.

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
| C | fix/target-tags-bold | the garage's (C), (V), (R) part legends: the letter rastered emboldened (1.3) and a little larger in its ring, modern look only; the ring and the 1997 look unchanged | ux (owner's note); captures of FIELD SALVAGE before and after; no unit test (a text-server raster weight) | `_scratch/r3-visual-tweaks/` | merged (ebadeb6, pass 8) |
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
| I23 | fix/hd-cockpit-panels-2 | modern look: the ammunition counters' digits drawn a pixel left, on the housing's drums (Cockpit.DigitShift); the mirror's frame and glass cut along smooth edges (MirrorEdge: the art's masks blurred, cut at half over a screen pixel), 3D and 2D, with or without a pack; the 1997 look keeps the original's pixels | ux (owner's playtest); local: Game MirrorEdgeTests 5 | `_scratch/r3-hd-cockpit-2/` | merged |
| I24 | feat/dymo-weapon-labels | modern look: the weapon and special plates (and an empty specials slot's EMPTY) lettered in the Impact Label Reversed font (shipped with its free-font licence, noted in THIRD-PARTY-NOTICES), the weapons and specials panels worked at twice the original's pixels at least (CockpitPanels.LabelScale); the 1997 look the painted plates | ux; local: Game DymoLabelsTests 17 | `_scratch/r3-dymo/` | merged |
| I25 | fix/host-form-polish | the host form's arena sketch on a torn scrap of paper, a tip on each rule, the arena list's scroll bar | ux; local: Game HostFormLayoutTests 3, Formats ArenaSketchTests | `_scratch/r3-host-form/shots/` | merged |
| I26 | fix/melee-list-arenas-only | Instant Melee's and the host form's MELEE lists leave out an addon level whose script is byte for byte a story mission's (MissionTitles.IsCoopCopy); the CO-OP list keeps the missions | original (the melee lobby lists every addon level); local: Formats MissionTitlesTests | `_scratch/r3-mission-lists/` | merged |
| I27 | fix/scene-names | trip missions listed as "Scene N: name" by what happens in them (MissionTitles.SceneNames); the executable's unshown titles no longer read; A01 "Training" | original (shows "Scene N." only); local: Formats MissionTitlesTests, Game SceneBookmarksTests | `_scratch/r3-mission-lists/` | merged |
| I28 | fix/map-edge-on-off | Map Edge is Off (the walls as they are) or On (drawn down at their foot); Haze and Fade Out gone, a saved 0-3 read as Off/On | ux (owner's notes); local: Game MapEdgeTests | `_scratch/r3-edge-and-marks/shots/` | merged |
| I29 | fix/marker-marks-the-port-value | the pads' `*` marks the port's values, not the original's (ShellSettings.PortMark) | owner's decision; local: Game SettingsWithoutARowTests | `_scratch/r3-edge-and-marks/shots/` | merged; made uniform by I36 |
| I30 | feat/map-edge-thin-barriers | Map Edge On also hides a thin fence between two drivable parts of a map (EdgeWalls), drawn at the plate's level; the collision unchanged | ux (the owner will try it); local: Formats EdgeWallsTests 6 | `_scratch/r3-edge-and-marks/shots/t05-*.png` | merged |
| I31 | feat/hood-shine-adjustable | Hood Finish Matte / Satin (default) / Gloss on both pads, the cars' paint too (PaintFinish; `[port] hood_finish`, an old hood_matte read) | ux; local: Sim PaintFinishTests | `_scratch/r3-visual-tweaks/` | merged |
| I32 | fix/impact-marks-subtler | ground strikes a third fainter; `[port] effects_size`, `effects_brightness`, `effects_opacity` (25-150 %, the modern look's weapon effects; EffectsLook) | ux; local: Sim EffectsLookTests | `_scratch/r3-visual-tweaks/` | merged; the playtest machines have 80 / 70 / 70 |
| I33 | fix/smoke-depth | the half-size smoke layer takes the nearest surface under each of its pixels (no fringe over a car's or hill's outline) (#95); I76_SMOKE_DEPTH_VIEW shows the depth it uses | issue #95; local: Game SmokeDepthTests 5 | `_scratch/r3-smoke-depth/` | merged; a Mac launcher with the diagnostic |
| I34 | fix/net-esc-labels | the network Esc menu: Leave Match, Exit I76, the questions saying which | ux; local: Net EscapeMenuSelfDestructTests 6 | `_scratch/r3-mp-esc/` | merged |
| I35 | feat/pistol-keys | Pistol Left / Pistol Right (Control Configuration and the pad screens): held in the cockpit, the head turns to that window and the .45 fires once the arm is up; in CarInput, protocol 25 | ux; local: Sim HandgunSessionTests (first shot 0.498 s after the key at 20 Hz), Formats PistolKeysTests, Net WireTests | `_scratch/r3-pistol/shots/` | merged; the owner's input.map and gamekeys.cfg on both machines |
| I36 | fix/round3-integration-4 | the integrator's pass-7 fix: the `*` on every value the original does not have, on every row that offers the original's (below); the shell's Graphic Detail values end short of the scroll bar | owner's decision; local: Game SettingsWithoutARowTests, Sim PaintFinish / ScreenWobble | `_scratch/r3-integration-4/shots/shell-gd-{modern,1997}.png` | merged |
| I37 | fix/dev-panel-scale | the Developer panels (mission and main menu) scale with the window: 2x from 1080 rows, 3x at 4K, following a resize (DevScale) | ux (owner's note); local: Game DevScaleTests; captures at factor 1 and a forced 2 | `_scratch/r3-devscale/` | merged (9884461) |
| I38 | feat/graphics-rows-to-config | HD Textures and HD Menu Art leave Graphic Detail: `[port] hd_textures` / `hd_menu_art`, on by default (modern look), toggled on the Developer panel; a saved Off kept | owner's taste (keep menus simple); local: Game HdSettingsKeysTests, HdTexturesSettingTests | `_scratch/r3-dev-settings/` | merged (7ae64f9) |
| I39 | feat/dev-panel-settings-and-signals | the Developer panel's second column: Settings (every key of playing.md's "Settings without a row" as a stepping button, saved at once, applied live where it can) and Signals (the last 3 s of force feedback, rumble and telemetry, can stay over the game) | local: Game DevSettingsSignalsTests 7 (the key list held equal to playing.md's table) | `_scratch/r3-dev-settings/` | merged (e7fa4a4) |
| I40 | feat/upscale-colour-match | tools/upscale-textures.py brings the model's blurred picture back to the original's colour in Oklab, the model's detail kept; `--from` applies it to an old pack, `--no-colour-match`, `--self-check` | self-check 5/5 (error 4.23 -> 0.40); the lab pack's car panel 2.06 -> 1.07, dash 2.66 -> 1.84; 6,510 textures repacked in 25 min | `_scratch/r3-upscale-v1/` | merged (c638aeb); the playtest machines keep their pack (regenerate to see it) |
| I41 | fix/smoke-depth-metal | with MSAA the modern smoke layer reads the multisampled depth itself (no smoke through a hill, car or turret on a Mac) (#95) | issue #95; on an M-series Mac (Metal, MSAA 4x, Render Scale 50 %) before/after; local: Game SmokeDepthTests 9 | `_scratch/r3-smoke-mac/` | merged (f6c143a) |
| I42 | fix/dymo-labels-brighter | the modern cockpit's label tape letters pure white (255 lit, 238 unlit), a narrower antialiased edge, the emboss shadow darker | ux (owner's note); local: Game DymoLabelsTests 19 | `_scratch/r3-visual-tweaks/` | merged (72392d9) |
| I43 | feat/vegetation-density | `[port] vegetation_density` (10-150 %, 60 by default) thins the modern look's scattered plants to a subset; the map's placed objects and the 1997 look untouched | ux (owner: Low too dense); local: Formats VegetationTests (914 / 559 / 284 shrubs at 100 / 60 / 30 %); m01 plants placed 2886 / 1728 / 866 | - | merged (2dbf091) |
| I44 | fix/ai-fire-cadence-as-original | docs only: a deviations row for the attack-tactic chooser: the 1997 CD build's is a stub that always picks dirt_gunner (T14's cop circles out of sight, a few rounds a minute), the 1998 build GOG ships draws from the weighted lists (road_side, steady fire); I26 follows the 1998 build | 86Box, retail 1.0, T14, n 7; headless T14 seeds 1-3 | - | merged (158f1b6); the AI follows GOG's build (version policy) |
| I45 | fix/zero-armour-damage-as-original | test only: a round through a side whose armour is gone goes whole to the components, as measured | original (T14 damage tap); local: Sim DamageTests (new test passes) | - | merged (93b72d7) |
| I46 | fix/wreck-smoke-range | the modern look's scenery-wreck fire and smoke start and draw out to 600 m, as the original's model sprites (were cut well short) | original (the 1997 look's reach); T05's wrecks at 450 / 250 / 80 m both looks; local: Game SmokeWindowTests | - | merged (7603218) |
| I47 | fix/death-orbit-drawn | the death orbit camera interpolated between ticks (no 8-degree jumps every second or third frame on a fast display) | owner's report (Mac); M01 at 60 fps, frame-to-frame change even (13.5-15.9 against 16.5/2.8/3.2...); local: Sim DeathOrbitTests 2 new | - | merged (a92d4bb) |
| I48 | fix/round3-integration-5 | the integrator's pass-8 fix: vegetation_density on the Developer panel's Settings (10/30/60/100/150, live), its playing.md row and shell.cfg line after the effects keys; the README's Windows tex-world path (a tab had replaced a backslash-t); two changelog full stops | local: Game 275/275 (DevSettingsSignalsTests' table test failed on play before it) | - | merged (4984d2c) |
| I49 | feat/tactile-lfe | a bass shaker's signal of the port's own on the LFE channel (TactileSynth, BassShaker): the engine, the road by surface and speed, a thump per shot, hit and explosion, 15-90 Hz under a limiter; nothing on a stereo output | ux (the original has no such signal); local: Formats TactileSynthTests 7 | - | merged (2abfb5d) |
| I50 | feat/audio-output-modes | Audio Control's Output (Stereo, Phones, 2.1\*, 5.1\*, 7.1\*) and LFE Level rows; 2.1 mixes the shaker signal and a bass boost under the crossover into stereo; `[port] tactile_eq` (a hz:dB correction curve) and `crossover` (40-160 Hz, 90); explosions felt out to 2 km; an old `tactile=lfe` read as 5.1 | ux; local: Formats TactileSynthTests 14, Game 246 | - | merged (8ea721a); the playtest machines have output 2.1 and the owner's curve |
| I51 | fix/map-edge-roads-and-1997 | Map Edge On in the 1997 look too; the plants follow the drawn-down ground wherever the player is; a thin barrier with a road at its foot stays (the mountain over T05's mountain road) | ux (owner's notes); local: Formats EdgeWallsTests 7; T05 heat maps before/after | - | merged (79ad994) |
| I52 | fix/map-edge-placed-objects | On never draws down ground the mission puts something on (T05's helicopters stood 111 m up); a pad keeps its height, an object on a wall keeps the cells about it | local: Formats EdgeWallsTests 12 (T05, T11, S06: nothing placed or on a path over hidden ground) | - | merged (26aad4e) |
| I53 | feat/modern-lighting-calibration | the modern look's lighting calibrated by day against the 1997 look's (exposure, ground and object tints solved per mission, the sky halfway); `[port] lighting_calibration` on\* / off, an earlier `look_colour_match` read; contains feat/modern-look-colour-match (b20aef9, the ground-only first step, not merged separately) | local: Formats LightingCalibrationTests 8, Game SettingsWithoutARowTests (key migration 7); captures T01, T05, M01, T10 | branch's captures | merged (4d447a4) |
| I54 | feat/build-version-rules | user://build-rules.cfg (written by the game, commented with each build's value) picks the 1997 CD or the 1998 build's turret slew and AI tactic choice, the 1998 (GOG) build's by default; structural session rules, no snapshot or protocol bump | original (the two builds measured); local: Sim TurretSlewTests, TacticChooserBuildTests, Game BuildRulesTests 7 | - | merged (e2cf5d9) |
| I55 | fix/round3-integration-6 | the integrator's pass-9 fix: lighting_calibration (off / on\*) and crossover (40\*/60\*/80\*/90/120\*/160\* Hz) on the Developer panel's Settings, live; tactile_eq a curve set by hand only (DevSettings.HandSetOnly, the table test allows it); playing.md marks lighting_calibration's on | local: Game 289/289 (the table test fails on play without it) | - | merged (c992a4f) |
| I56 | feat/dev-fps-overlay | `[port] dev_fps` (false / true\*, no row; the Developer panel's Settings, live) draws the frame rate in the window's top left corner, `FPS 64`: the average over the last second, updated twice a second; both looks | ux; local: Game FrameStatsTests, the Settings table test | - | merged (f76de3d) |
| I57 | fix/net-melee-music-tracks | a melee, Instant or network, asking for a shell's track (I76's M09: 15, the menu's) starts on the next mission track; Nitro's default sequence passes over its menu (10) and credits (4) tracks; `I76_MUSIC_SEQUENCE=retail/gog` and `music_tracks=all` unchanged; the stale duplicate of the music deviations row removed | WRLD tracks read from every mission of both installs; local: Formats MissionMusicTests | - | merged (3000ef8) |
| I58 | feat/build-id-in-lobby | the host form shows this build's id, the roster the host's (red when it differs), and a joiner on another build is let in with a one-line notice to everyone; the id from the SDK's source revision or, in a git archive, an export-subst placeholder; `I76_BUILD_ID`; protocol 26 (join request, accept, session list) | ux (family games on several machines); local: Net BuildIdTests 6, WireTests | - | merged (d6f0a7c) |
| I59 | fix/round3-integration-7 | the integrator's pass-10 fix: the build-id notice's changelog example and BuildIdTests take a character's name (Skeeter) for the joining player instead of a real first name | local: Net BuildId 6/6 | - | merged (0ceb444) |
| I60 | feat/graphics-submenus | Graphic Detail keeps the original's six rows on its sheet; the port's rows on four pages, Display, World, Car & Cockpit and Effects, on the main menu's sheet and the Esc pad alike (GraphicsPages, one table); nothing scrolls; Done/Cancel across pages from one snapshot, Reset per page. Rebased: no Update Check row (upstream's Check for Updates stays on Play Options' second page) | local: Game GraphicsPagesTests | `_scratch/r3-gfx-sub/shots` | merged (02d1181) |
| I61 | fix/lfe-level-stronger | the bass shaker half as strong again at the same LFE Level, the level applied inside the shaker's limiter | local: Formats TactileSynthTests | - | merged (fd2d2b6) |
| I62 | feat/lfe-2 | Output Stereo, Phones, 5.1, 7.1 and LFE Level 0 (off, the default) turning the shaker signal on; `[port] lfe_engine/road/hits/shots/explosions` 0-200 % (Developer panel sliders); road rumble from the car's speed and its springs; movies' high shelf and the game's shelves (`game_eq`, `movie_eq`, set by hand) | local: Formats TactileSynth, SoundEq; Game 293 | - | merged (125f6bf) |
| I63 | feat/smooth-keyed-edges | modern look: keyed textures cut along smooth outlines traced from the key (signed distance fields from KeyEdge, made on workers), signs, fences, plants, the cockpit's art, the mirror and compass; contains fix/compass-smooth-edges (eb71edf, not merged separately) | local: Formats KeyEdgeTests 6, 4210 keyed textures; captures T01 | branch's captures | merged (591d76f) |
| I64 | fix/known-test-failures | the four known Sim failures answered: T14 checkpoint test on the 1997 tactic rule, the co-op side ram while overtaking not the human's, T01Follow's driver keeps off Taurus at 30 Hz, the Nitro caltrop's silent hit | local: probes on a second machine; Sim 992/1021 there | - | merged (8b3ae05) |
| I65 | fix/vehicle-lights-strength | modern look: head, brake and tail lamps weaker at night and well down by day (LampStrength) | local: Game LampStrength 3; captures | - | merged (e154725) |
| I66 | fix/palette-grade-no-dither | Palette Grade moves the modern look's colours toward the mission's palette smoothly, no per-pixel dither | local: Formats SoftwarePalette 24 | - | merged (ff55868) |
| I67 | fix/dymo-letter-weight | the modern cockpit's weapon labels lettered heavier, the embossed shadow no longer eating the strokes | local: Game DymoLabels 21 | - | merged (fdf3130) |
| I68 | fix/paved-roads-sharp | modern look: paved roads carry the asphalt scan's grain and wear near the camera; textures at 16x anisotropy | local: Game PavedRoadDetail 2; captures | - | merged (16fa62f) |
| I69 | feat/lighting-original-colours | modern look in the original's colours: the frame through the 3D card's curve, the sky's texture colour per point, grey sky light, the ground solve on the flats; Matte the default Hood Finish (rebased onto upstream's all-over clear coat: Matte takes it off every car, the global starts at Matte, a hubcap's coat only under Satin and Gloss) | local: Formats LightingCalibration, Sim PaintFinish; captures against the 1997 look, 9 sets | branch's captures | merged (ff37f7a) |
| I70 | fix/map-edge-row-name | the Map Edge row is Hide Map Edge Cliffs (the World page, both menus) | local: capture of the sheet | - | merged (f995e90) |
| I71 | fix/map-edge-roadside | Hide Map Edge Cliffs keeps a thin barrier within 80 m of a road (fades in to 120 m) | local: Formats EdgeWallsTests 13 | - | merged (d2e5809) |
| I72 | fix/round3-integration-8 | the integrator's pass-11 fix: the last page is Effects (the Esc pad's title art), the Developer panel's bass-shaker trace fed by the shaker's peak with its header and dev-hooks line saying so (was "no LFE output on this build"), the Settings test names the lfe_* sliders and tactile_eq / game_eq / movie_eq / lighting_calibration, two Hood Finish comments say Matte is the default | local: Game 328/328 | - | merged (f130273) |
| I73 | fix/ai-dropper-gate-as-original | AI cars with mines, oil, Blox or fire droppers no longer drop under a car chasing them straight on above about 28 m/s, as the build GOG sells | local: Sim AiDropAndMortarGateTests | - | merged (ff44787) |
| I74 | fix/hd-menu-port-edits | the HD menu art's port edits | local | - | merged (c594240) |
| I75 | feat/hd-pack-scale | the HD pack's scale | local | - | merged (0c0f928) |
| I76 | feat/death-variety | modern look: a car's death follows what killed it; most deaths open with a calmer blast, burning wheels drawn per death; snapshot 25, protocol 27 | local: Game DeathVarietyTests, Sim DeathCauseTests | - | cherry-picked (9b80ce0, b091233) |
| I77 | feat/brush-impacts | modern look: the scattered plants answer rounds, cars, blasts and fire; `[port] brush_impacts` | local: Formats BrushFieldTests | - | cherry-picked (93cc458) |
| I78 | fix/hardpoint-textures | modern look: the 3D cockpit's weapons panel, steering wheel and arms are cut by their own pixels rather than the first frame's outline (the housings, ammo plates, hands and pistol were cut away; the specials panel was never affected and keeps its smooth edges) | playtest ("hardpoint textures don't appear"); captures `_scratch/r3-hardpoint-tex/`; local: Game 336/336 | `_scratch/r3-hardpoint-tex/` | ready (5727c81, 5e04625) |
| I79 | fix/hd-pack-3x | an HD upscale at any whole multiple of the original is served, not only a power of two (the 3x pack fell back to the originals for every car and world texture, 282 of them on M01); the log notes the first upscale served at each scale and a --screenshot run prints a count per scale | found while on I78; local: Formats HdTextureTests 9 (one new) | `_scratch/r3-hd-pack-3x/` | ready (b8162bb) |
| I80 | fix/dymo-labels-2 | modern cockpit weapon labels: the letter-weight fix's synthetic bold removed so the tape's emboss reads (face 232/214 with lit and shaded bevel edges), letter gap 30 % tighter (mean gap 234 -> 164 font units), the ammo counters' ink darkened (41,41,20 -> 14,14,7) | playtest ("the flood of white occludes the emboss", "spacing 30 % less", "counter black darker"); local: Game 317/317 (3 new) | `_scratch/r3-dymo-2/` | ready (8997727) |
| I81 | fix/dev-panel-2 | Developer panel at half its size (scale from window height / 1440) and 85 % opaque; the Teleport row's Go/Keep/Forget were covered by the second column (the status text widened the first after layout): status wraps, columns re-placed; Show FPS row on the Display page and the Esc pad bound to `dev_fps`, the counter top-right over menus and missions; I76_MOUSE click hook | playtest ("dev mode too big", "teleport doesn't work", "display fps in the graphics menu"); local: Game 319/319 | `_scratch/r3-dev-panel-2/shots/` | ready (79d2738, 7c04539) |
| I82 | feat/lfe-3 | bass shaker: per-surface bands with distance-paced lumps and grit (off-road 30-34 Hz, gravel road 66 Hz + 76 Hz grit, pavement a narrow 56 Hz hum, planks 46 Hz; 60 ms crossfade), road/off-road contrast stretched (sand ~15 dB above asphalt), a per-wheel 28 Hz knock on crossing a road or bridge edge scaled by speed (front axle then rear); `[port] lfe_offroad`, `lfe_bumps`; dev readout line, I76_LFE_LOG; tools/TactileRender writes the synth to WAV; docs/lfe-surfaces.md | playtest; research with sources; local: Formats TactileSynth 23/23, Sim WheelCrossings 5/5 | `_scratch/r3-lfe-3/` (46 WAVs) | ready (5014d29, 64e18c0) |
| I83 | fix/map-edge-slopes | Hide Map Edge Cliffs hid every unreachable slope (36-60 degrees) joined to the edge; now only ground whose way in crosses a step steeper than 60 degrees (8.66 m per 5 m sample) is hidden, drawn down to the cliff's foot, never below its old height; patches under 25 samples kept; I76_MAP_EDGE_SLOPES=0 for the old rule. T05 restores 53 % of the ground it flattened, M06 77 % | playtest ("it hides slopes we want to keep"); local: Formats EdgeWallsTests 18/18 (1 new) | `_scratch/r3-map-edge-6/edge-slopes.png` | ready (16adac5) |
| I84 | feat/movie-while-loading | modern look: the briefing plays while MissionBuilder.Build runs on a thread (nodes outside the tree; prop material maps held and flushed after; frames capped at 240 fps during the build), then the card for the rest of the load; `[port] briefing_while_loading` (default on; 1997 look, retries, --skip-movies, co-op and the editor keep the original order); saves 5-8 s per briefed mission. HD movies: `user://movies/<stem>.ogv` (Theora + Vorbis, any fps, same duration) replaces the SMK in the modern look, same skip keys and stall watch; I76_MOVIE_SHOT, I76_MOVIE_LOG | playtest ("couldn't the movie play during loading?"); measured t01/t07/t08/m01; local: Game 342/342 (BriefingOverlap, MovieReplacement tests) | `_scratch/r3-movie-load/` | ready (1cfac51, 82a12f3, 6fb5e20) |
| I85 | remove/map-edge-cliffs | Hide Map Edge Cliffs removed: EdgeWalls.cs and its tests, MapEdge.cs, the shader include and the draw-down code in five shaders, four shader globals, the horizon ring's hidden-state heights, the setting and both menus' row, seven I76_MAP_EDGE* hooks, the docs rows; a withdrawn note in deviations.md and a Removed line in the CHANGELOG; old shell.cfg files with `map_edge` still load (24 files, +47/-1340) | the user, after playtesting the slope fix: the walls and the slopes up to them are the maps' intended shape; removed for simplicity | `_scratch/r3-map-edge-remove/shots/` | ready (726b2e8) |
| I86 | feat/hood-gloss-level | Hood Finish -> Hood Gloss, a 0-100 level in steps of 5 on the sheet and the Esc pad (any integer from the file and the dev slider); 0/50/100 render as the old Matte/Satin/Gloss (captured: within 2/255), levels between blend the two nearest presets' roughness, specular, clearcoat, coat roughness, metallic cap and hubcap coat; shader global `i76_hood_gloss` (float) replaces `i76_paint_finish`; `hood_finish` and `hood_matte` read for migration, not written | playtest ("a finer grained adjustment for hood gloss"); local: Game HoodGlossTests, Sim PaintFinishTests 24/24 | `_scratch/r3-hood-gloss/shots/hood-gloss-steps.png` | ready (e518ee3) |
| I87 | feat/lfe-4 | bass shaker: hits scaled by damage on a log curve (a landing, a ram and a head-on no longer alike), a 23 Hz cabin ring after hits over 20 damage on the original's cockpit-jolt decay (1.47 s), the edge knock redone (40 Hz body + 80 Hz octave, 350 ms, 5-14 dB over the sand rumble), seeded variation on every surface (level ~2 dB, pitch ~4 %, one lump in 14 bigger), every source in its own band after measuring the game's sounds below 200 Hz (docs/lfe-bands.md); `lfe_cracks` and `lfe_strip` dev-only at 0; headroom plan (ram 0 dBFS, axle knock -6, wheel knock -12, roads -8 or under); tools/TactileRender sweeps and `eq_from_measurement.py`; measurement WAVs for the user's force readings | playtest; sweeps `_scratch/r3-lfe-3/sweeps*/`, bands `bands/`, WAVs `wavs2/`, `wavs3/`, `measure/`; local: Formats TactileSynth 30/30, Sim WheelCrossings 6/6 | `_scratch/r3-lfe-3/` | ready (0285d4e, 8b5d7cf, 3035cb7) |
| I88 | feat/graphics-pages-3 | Graphic Detail's sheet in the original's order and words (Screen Resolution, Monitor Brightness, Visibility Range (new: Far = the original's 600 m, Max* = 20 km; the original's Near/Med./Far read from the exe's grxdet1 table), Terrain Detail, Shadow Detail, Ext. Instrum. Disp., Rear Mirror) then three pages, Look / Display / Camera, on the sheet and the Esc pad (the pad gains Visibility Range, HD Textures, Car Interiors, Camera Return, View Zoom Reset); one HD Textures row Off/4x/3x/2x listing the sizes whose folders exist (Off/On* with a pack but no size folders); Display's Reset owns Anti-Aliasing, the sheet's Reset the sheet's rows (the pad's leaves Screen Resolution alone); long labels push their value past the column; Car Interiors On* and Anti-Aliasing 8x* marked; no key renamed | the proposal `_scratch/r3-gfx-options/proposal.md`, approved by the user; local: Game 337/337 (page lists, line positions, Reset scopes, Visibility Range, HD Textures values) | `_scratch/r3-gfx-pages-3/pages-before-after.png` | ready (6c6ea9e) |
| I89 | fix/briefing-overlap-title | with the briefing over the build the loading card (loadscr.pcx, "SOMEWHERE IN THE SOUTHWEST...") was fading in for ~0.75 s before the movie; now black until the movie's first frame, then the card with the original's 0.32 s ramp for the rest of the load; `BriefingOverlap.CardBeforeBriefing`; the original order (card, briefing, mission) unchanged; nothing else runs before the movie | playtest ("Somewhere in the Southwest plays before the video"); traces `_scratch/r3-movie-load/title-*`; local: Game 350/350 | `_scratch/r3-movie-load/title-after-sheet.jpg` | ready (0f88089) |
| I90 | fix/counter-digits-font | modern look: the ammunition counters' digits drawn from Roboto Bold 3.016 (SIL OFL 1.1, embedded, licence shipped) at the panel's resolution on the strip's drum face with its separators, 1 texel anti-aliasing (`CounterDigits.cs`); a reverse-emboss inset built and off; the 1997 look keeps the znbe strip; font match against ten faces on the sheet | playtest ("the edges of the numbers in the ammometers are fuzzy; font-match and replace"); local: Game 334/334 (3 new) | `_scratch/r3-counter-digits/counter-digits.png` | ready (0b276b2) |
| I91 | fix/shell-layout-2 | Save/Load Bookmark's Delete moved from the scroll arrow's line to the bottom button line left of Load/Save (the Nitro screen's spot); the host form's arena scrap 20 base px (1/32 of the 640 picture) further left, its dimensions under the drawing and centred, stopping 3 px short of the AREA OF PLAY border on wide arenas; checked on 11 arenas, 1024x768 / 1280x720 / 3440x1440, HD art on and off, 1997 look | playtest; local: Game 349/349 (BookmarkDeletePlaceTests rewritten) | `_scratch/r3-shell-fixes-2/shots/` | ready (e22f8dc) |
| I92 | feat/death-variety-2 | two-stage deaths in the modern look: the failed part's event (hood out burning, a corner burst, sparks, the first pop, a crunch), a 200-500 ms beat, exactly one tank blast, a cause-specific drawn throw (down-and-back, slide, launch with a roll, sag, none for a driver); the original's 10 s effect roll (~25 x1_cars1) drawn as flares instead of 3 m blasts (the six-second fireball, and why brakes looked alike); variety-off's double blast fixed; the catastrophic award (1 in 8 + specials, never a driver) alone gets the 7.5 m tank, three burning panels, every wheel burning, a 20 s fire; driver smoke at a quarter rate; burning wheels 1.2 -> 0.8 per death; every number in `DeathVariety.cs`; I76_SHOT_TICKS, I76_QUIT_TICK, I76_DEATH_SEED hooks. Drawn throw only: the simulated wreck still takes the original's kick, so collisions can sit metres from the picture | the user's critique of the first clips and design brief; `_scratch/r3-death-2/design.md`, clips `deaths2-compare.mp4`, counts over 25 scripted deaths; local: Game 327/327, Sim DeathCause/Snapshot 33 | `_scratch/r3-death-2/` | ready for playtest (4b17cb8, b6b286b) |
| I93 | feat/hd-pack-alpha | the pack tool's `--families`: each cockpit family through its own colour model (radar HDcube3; panels, gear, compass, mirror UltraSharpV2; damage, weapons, hands HDcube3; notepad HDcube4Compact; gauges, needles, misc the pack's own colour with the alpha step), the transparency upscaled with 4xHDcube-Alpha and cut at one half (`--alpha` for every keyed texture), `--family-table FILE.json`, `--models-dir`, `--gpu-share`, `--self-check` 6/6; pack.json and ATTRIBUTION.txt list every model and licence. The packs rebuilt with it (393 files each) are installed on the desktop, the lab sources, the laptop and James's Mac | the user's picks from the dash sheets (`_scratch/r3-dash-upscale/sheets/`, `dash-applied.png`) | `_scratch/r3-dash-upscale/` | ready (79864e3) |

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

Pass 7 (2026-11-02 night): the thirteen branches above merged by sync-play (incremental, `-NoRebase`; rerere replayed the
resolutions of a scratch pass of the same merges in the same order, the trees identical), then fix/round3-integration-4.
Play stays on 0.11.0 (5192e29); upstream's move is a separate decision.
- Cockpit.cs (dymo over hd-cockpit-panels-2): the Faithful setter keeps both: the repaint flags, and the labels' panel
  scale (LabelScale 1 in the 1997 look, 2 in the modern one) with ArtSwapped on a flip; the weapons row stamps the label
  (or the painted plate in the 1997 look) and moves the digits by DigitNudge, in both panels.
- MissionTitles.cs / MissionTitlesTests.cs (scene-names over melee-list-arenas-only): IsCoopCopy and its script hash kept,
  the executable's title reader gone for SceneNames; both branches' tests kept.
- Overlays.cs / EscapeMenu.cs (hood-shine over map-edge-on-off): Hood Finish's three steps on both pads with Esc row 18
  (HD Textures 19), the Resets put Map Edge to Off (the on-off branch's name) and Hood Finish to its default.
- Main.cs / Shell.cs (impact-marks over hood-shine and the marker flip): ApplyHoodFinish and ModernFx.ApplyLook both; the
  marker branch's Melee A.I. names with the effects properties after them; playing.md keeps the marker branch's "Settings
  without a row" table (port values marked) with the three effects rows after melee_ai, unmarked (no original's value).
- CHANGELOG.md: union at each merge, the network Esc lines in the labels branch's words (Leave Match, Exit I76), the HD
  Textures line in the marker branch's (On\*), the host form line in the polish branch's; protocol 25 at the top.
  docs/deviations.md: the network Esc row in the labels branch's words, "Settings without a row" in the marker branch's.
- A scratch-pass slip: the first record of the marker merge's CHANGELOG resolution dropped the HD Textures line; its
  rerere entry was rewritten to the corrected file and the merge replayed identical before sync-play ran.
- fix/round3-integration-4 (the owner's decision, the `*` uniform): every row that offers the original's value marks each
  value the original does not have, on both pads: Field of View (all but 90), Force Feedback Pad Enhanced\* and Wheel\*
  (the original drove a force-feedback stick, never a wheel), Melee A.I., Map Edge On\*, HD Textures On\*, HD Menu Art
  On\*, Hood Finish Satin\* and Gloss\* (Matte the original's flat paint), Render Scale and Rumble Strength all but 100 %,
  1997 Mode Off\*, Crash Blur Off\*, Vegetation Low\* and High\*, Mouse Camera's speeds, 3D Garage On\*, Head Tracking
  Analog\* and Digital\*. Left unmarked, no original's value: Palette Grade, Car Interiors, Camera Return, View Zoom Reset,
  Max FPS, VSync, Mouse In Window, Log, Renderer, Resolution, Update Check, Simulation Rate, Wheel Strength, Head Track
  Sens., Map Downloads, Anti-Aliasing (the 1997 look ignores it). Crash Blur's and Hood Finish's shell.cfg words come from
  tables of their own (CrashBlur's round-trip test caught "off\*" reaching the file). The shell's Graphic Detail values end
  at x 461, short of the bar at 464 ("Monitor\*" ran into it); the Esc pad already moved them.
- The playtest machines' user folders: `[port] effects_size=80`, `effects_brightness=70`, `effects_opacity=70` added
  (nothing else changed; shell.cfg.before-pass7 beside it); the owner's input.map and gamekeys.cfg from the pistol lane
  (gears on [ ], Pistol Left / Right on , and .; the port's [ and ] keys moved to - and =), an existing file kept as
  .before-pistol. The look rows: the reference install's input.map has the arrows' Up = pilot_glance_up and Down =
  pilot_glance_down; the lab's and the laptop's install maps have them swapped (Up = glance down, "as the owner plays it on
  the Mac", 2026-10-21); the owner map installed follows the reference (Up = glance up). The Mac's install map has the
  reference's way too (Up = glance up), so on the Mac the owner map changes no look row.

Versions after pass 7: snapshot 24, protocol 25 (feat/pistol-keys: a 17th button bit in the input queue's mask; WireTests'
TheProtocolIs25), shell.cfg 13.

Test status, pass 7 (fix/round3-integration-4 at 895e22a, the tree of `play` 26db81f but the manifest): desktop
(I76_INSTALL and I76_NITRO_INSTALL set) Game 246/246, Sim 982/1015, Net 424/440; outside the desktop baseline only the
known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the
Nitro install set); Net's failures all in the baseline. Laptop: Sim 981/1013, Net 425/440, Game 246/246, Formats 915/949
(30 skipped), Editor 216/216; outside the laptop's baseline the three known Sim ones. No new failure.

Deployed: `play` 26db81f to the laptop (VERSION.txt "play 26db81f deployed 2026-11-03T00:57Z"; smoke T01 --drive
renders, net 25, the 2x pack read), and to the Mac (VERSION.txt "play 26db81f deployed 2026-11-03T01:30Z", built and imported there;
no game run), with a launcher "I26 play (smoke diagnostic)" that sets I76_SMOKE_DEPTH_VIEW=1. Neither user folder had an
input.map or gamekeys.cfg before, so no .before-pistol copy was needed.

Pass 8 (2026-11-03): the twelve branches above (rows I37-I47 and fix/target-tags-bold in C) merged by sync-play
(incremental, `-NoRebase`, play kept on 0.11.0; rerere replayed the resolutions of a scratch pass of the same merges in
the same order, the trees identical), then fix/round3-integration-5. fix/wreck-smoke-range and fix/death-orbit-drawn
came in during the pass and went into a second dry run before anything was pushed. Left for the next pass (in progress):
feat/tactile-lfe, feat/audio-output-modes, feat/modern-look-colour-match, feat/modern-lighting-calibration,
fix/map-edge-roads-and-1997, fix/map-edge-placed-objects, feat/build-version-rules.
- CHANGELOG.md: fix/target-tags-bold (base 0.11.0) adds its own Unreleased Fixed part; its line goes at the end of
  play's Fixed list. Elsewhere union (the Developer panel, HD keys, colour match and vegetation lines under New; labels,
  wreck smoke and death orbit under Fixed).
- README.md (colour match over graphics-rows-to-config): the colour-match paragraph kept, ending with the HD key's words
  (`hd_textures=false` or the Developer panel's Settings) since the row is gone.
- fix/round3-integration-5: vegetation_density in DevSettings (the table test holds the panel to playing.md), the
  README's Windows path, two full stops.

Versions after pass 8: snapshot 24, protocol 25, shell.cfg 13, unchanged (vegetation_density is written only when not
60; the HD keys only when off).

Test status, pass 8 (fix/round3-integration-5 at 4984d2c, the tree of `play` 0b0790c but the manifest): desktop
(I76_INSTALL and I76_NITRO_INSTALL set) Game 275/275, Sim 985/1018, Net 419/440; outside the desktop baseline only the
known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the
Nitro install set) and three Net network tests (AnetMelee's NAT game, Discovery's address book, the locked game's
lobby) that ran while the laptop deploy and the push loaded the machine; run alone they pass 3/3, and on the laptop.
Laptop: Sim 984/1016, Net 425/440, Game 275/275, Formats 923/958 (30 skipped), Editor 216/216; outside the laptop's
baseline the three known Sim ones. No new failure.

Deployed: `play` 0b0790c to the laptop (VERSION.txt "play 0b0790c deployed 2026-11-03T11:38Z"; smoke T01 --drive renders,
net 25, the 2x pack read, the plants thinner; `_scratch/deploy/smoke8-laptop.png`) and to the Mac (VERSION.txt "play
0b0790c deployed 2026-11-03T11:37Z", built and imported there on the first try; no game run). No user-folder changes.
The laptop's run-gui.ps1 is the older -Cmd one; the -CmdFile one ran from runs/run-gui2.ps1.

Pass 9 (2026-11-03): rows I49-I54 merged by sync-play (incremental, `-NoRebase`, play kept on 0.11.0 though upstream
master has moved 25 commits; rerere replayed the resolutions of a scratch pass of the same merges in the same order, the
trees identical but the manifest), then fix/round3-integration-6. feat/modern-look-colour-match is not listed: it is an
ancestor of feat/modern-lighting-calibration and came in with it. pr/smoke-depth (the upstream pull request's branch on
current master) is not in the stack. Every branch based on play (93e6e67, or 0b0790c for the build rules), merged once.
- CHANGELOG.md: union under New (shaker, Audio Control output, lighting calibration) and Fixed (the two Map Edge lines
  together), except that feat/audio-output-modes rewrote tactile-lfe's own line: its wording kept, the old one dropped.
- docs/playing.md: the table's `tactile` row (tactile-lfe) dropped, since audio-output-modes replaced it with Output
  (an old `tactile=lfe` still reads as 5.1); `tactile_eq` and `crossover` added after the HD rows; `lighting_calibration`
  after `vegetation_density`. The "Output and LFE Level" paragraph goes before "The port's values", whose text is play's
  (audio-output-modes carried the older one that still listed HD Textures).
- fix/round3-integration-6: lighting_calibration and crossover on the Developer panel's Settings, tactile_eq set by hand
  only (a curve, not a few values; DevSettings.HandSetOnly, and dev-hooks.md's Settings row says so). output and
  lfe_level have Audio Control rows, so they are not in the table; turret_slew and ai_tactics live in build-rules.cfg,
  which the game writes, not under shell.cfg's [port].
- The desktop's disk filled during the scratch merges (another session's staging); one merge commit was retried, nothing
  else was lost.

Versions after pass 9: snapshot 24, protocol 25, shell.cfg 13, unchanged (the build rules are structural session rules).

Test status, pass 9 (`play` d791df7): desktop (I76_INSTALL and I76_NITRO_INSTALL set) Game 289/289, Sim 990/1023, Net
424/440; outside the desktop baseline only the known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam,
T01FollowTests at 30 Hz, CaltropTests with the Nitro install set). Laptop: Sim 989/1021, Net 425/440, Game 289/289,
Formats 951/986 (30 skipped), Editor 216/216; outside the laptop's baseline the three known Sim ones. No new failure.

Deployed: `play` d791df7 to the laptop (VERSION.txt "play d791df7 deployed 2026-11-03T12:27Z"; smoke T01 --drive renders
with output 2.1 set, net 25, the 2x pack read, the lighting calibration solved; `_scratch/deploy/smoke9-laptop.png`) and to
the Mac (VERSION.txt "play d791df7 deployed 2026-11-03T12:27Z", built and imported there on the first try; no game run).
Both user folders' shell.cfg backed up as shell.cfg.before-pass9 and given, under [port], `output="2.1"` and the owner's
`tactile_eq` curve (the shaker rig's measured response, boosts capped at +4 dB); no other key touched; build-rules.cfg
left to the game.

Pass 10 (2026-11-03): rows I56-I58 merged by sync-play (incremental, `-NoRebase`, play kept on 0.11.0), then
fix/round3-integration-7. All three based on play 913a5dc. Only CHANGELOG.md conflicted (build-id against dev-fps under
New: union). The docs/playing.md table (dev_fps after crossover), the Developer panel's Settings list, Protocol.cs and
WireTests (26) and the music deviations row merged by git with no hand fix.
- The integration branch was first made on a scratch merge chain; merged into play it conflicted with play's own
  build-id merge and the union brought the old changelog line back. It was rebuilt on play's own merges (28fcfdd, not
  yet pushed), so it merges cleanly; the scratch commits were never pushed.
- feat/build-id-in-lobby's own commit message keeps the first name the branch used as its example (a topic commit, not
  rewritten); the tree no longer has it.

Versions after pass 10: snapshot 24, protocol 26 (feat/build-id-in-lobby; WireTests' TheProtocolIs26), shell.cfg 13
(dev_fps written only when on).

Test status, pass 10 (`play` 29226fa): desktop (I76_INSTALL and I76_NITRO_INSTALL set) Game 293/293, Sim 990/1023, Net
431/446; outside the desktop baseline only the known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam,
T01FollowTests at 30 Hz, CaltropTests with the Nitro install set). Laptop: Sim 989/1021, Net 431/446, Game 293/293,
Formats 954/988 (30 skipped), Editor 216/216; outside the laptop's baseline the three known Sim ones. No new failure.

Deployed by deploy-family.sh, one run: `play` 29226fa to the laptop (VERSION.txt "play 29226fa deployed
2026-11-03T18:35Z"; smoke T01 --drive renders, net 26; `_scratch/deploy/smoke10-laptop.png`), the owner's Mac
("...T18:35Z", built and imported there), and both family Macs ("...T18:36Z" each, woken by Wake-on-LAN, the build sent
in md5-checked chunks, built and imported there, their repo's play fast-forwarded to 29226fa). No game run on a Mac; no
user-folder changes.

Open after round 3:
- The Mac's head look (the owner's note): open; not reproduced here. A per-machine input.map is suspected. Needs the
  Mac's input.map and a repro.
- Map walls: Original (Plain*) stays the default; Hide is a choice, not the default.
- CheckpointTests (T14) fails because I26's AI follows the GOG (1998) build: its attack-tactic chooser draws from the
  weighted lists, so T14's lead cop takes road_side and patrols 160-190 m off in clear view, firing steadily, and with
  the turret aim fix its rounds hit Taurus; the checkpoint race no longer plays out as the test pinned it. The 1997 CD
  build's chooser always returns dirt_gunner and its cop circles mostly out of sight (fix/ai-fire-cadence-as-original's
  deviations row, measured n 7). Faithful to the build players own, so left failing and listed below; the test needs a
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

## Upstream rebuild 2026-11-03

`play` moved from 0.11.0 (5192e29) onto upstream's master at 32988ae (25 commits, still 0.11.0 plus Unreleased; upstream's
snapshot 15 and protocol 21 unchanged: no format or wire change).

What upstream changed (and what it meant for the stack):
- Our PR #144 landed as upstream's own commit (rcodesign, DER entitlements): fix/macos-entitlements-der dropped.
- #141 and #142 (01228e7): the player's car settled before the first tick, the carried menu track `ProcessMode.Always`,
  the mission track read behind the card. fix/load-card-audio-hiccup dropped (the same line). fix/wheel-suspension-prime
  kept: it primes every placed car's wheel history (AI, respawns, co-op seats), upstream settles only single-player cars.
- Police sirens loop by the GAS0 mode, on any car, and a police honk turns the siren on (2ddb2d4): fix/siren-gpw-loop dropped.
- Update check (356eddb, #138): the same `[port] update_check` key, on by default, a Check for Updates row on Play
  Options' second page and a link under the version: feat/update-check dropped. Our Graphic Detail row is gone (the rows
  under it move up one); upstream's row sits on Play Options' second page at y 300, under the port's rows.
- Car materials (2815842, 30eef70): the per-part finish table is gone, every car clear-coated paint with only the windows
  as glass. fix/hd-pack-glass-and-key keeps its delta (the windows and paint analyses read the original texture);
  feat/hood-shine-adjustable's Matte/Satin/Gloss now acts on that all-over paint.
- The horizon (6 commits): the 1997 panels by the original rule alone (no far-edge measuring), the modern ring sunk from
  `HorizonEdge.Ring` (`sink_low`). perf/horizon-early-z keeps the shader as an include with upstream's cone in it; the
  map-edge branches' shader edits go into the include.
- The map editor's playtest switch, stash and session store (`SessionStore`): TextureFactory keeps upstream's store for
  the originals always and for made textures when no HD pack is present (an HD pack's textures are repainted in place by
  a look flip, so they are made per factory as before).
- Bots use ramps (JumpLinks, MeleeBotJumps): its damage recorder takes the port's Rough(v, surface, dt)
  (fix/rough-terrain-damage-surface amended).
- Co-op clients' colliders follow gates and burnt guns; memory freed between missions (smoke table, force feedback,
  heap trim); F12 and music in the editor: no overlap beyond context.

Dropped from the manifest (absorbed): fix/siren-gpw-loop, fix/macos-entitlements-der, fix/load-card-audio-hiccup,
feat/update-check. Added: fix/upstream-rebuild-integration (stacked on play, last).

How it was rebuilt: every branch rebased by its own commits (master-based onto upstream, stacked-on-a-topic onto that
topic's new tip, stacked-on-play onto the play being built), merged in old play's own merge order (its first-parent
history; the manifest's order met different conflict shapes, so rerere could not replay), the 11 branches held in the
other session's worktrees merged as they stand. sync-play's `-Rebuild` cannot do this as it is: it rebases a branch
stacked on play onto upstream with all of old play's commits under it. The hand resolutions (54, besides rule-based
ones: the changelog/docs union, version pins only, upstream's Check for Updates moved, and a stacked branch's file taken
as old play's pre/post-merge pair three-way merged onto the new play) are, in short:
- Play Options' second page: Head Tracking 185/208, Rumble Strength 231, Melee A.I. 254, Wheel Strength 277, Check for
  Updates (upstream) 300; the fold's and the round-3 branches' Update Check pieces left out.
- Graphic Detail: old numbering kept through the rebuild (slot 16, our Update Check, empty), closed up in
  fix/upstream-rebuild-integration (Hood Finish 16, Render Scale 17, Map Edge 18).
- VehicleSave: TurnSpeed and Handicap saved, upstream's HornLoops structural; MeleeSave: HandicapChanged and upstream's
  Landed and ramp state; SnapshotHeader: the ignition and auto-handicap bytes in old play's order.
- Main.cs: the sky scroll's extra step without the far-edge code upstream removed; SetHorizonEye through `_horizon.Set`
  with upstream's ring values; the own song before upstream's pre-read track; the skip latch beside the editor timers.
- DropShadows: upstream's once-a-run shaders with the cached parameter names; MainDev: the mission picker and the
  loadout; TextureFactory: as above, KeyColour from the original; the old siren's code gone with the branch.
- Old play's hand fixes outside any branch (CheatMarker.Mark, ForceFeedback.PadStrength, the Load Bookmark Build with
  Direct, DevicePresets' shift, the ignition switches' launch header, the AI-fill and sight-line test set-ups, the
  Rough stub, the handicap protocol numbers in multiplayer.md, snapshot 24): three ride in the stacked branches that were
  built on them, the rest are in fix/upstream-rebuild-integration with the changelog, deviations and dev hooks rebuilt
  as old play's plus upstream's rows (the union merges had repeated rows).
Checked: every file upstream did not touch equals old play's except the dropped siren branch's (AudioBank, WeaponTests);
the files it did touch were compared with old play three-way merged with upstream, the differences being the above.

Versions on `play` after the rebuild: snapshot 24, protocol 26 (both above upstream's 15 / 21; WireTests pins 26 / 24),
shell.cfg 13; `[port] update_check` now defaults to on (upstream's), written only when off.

Fixes found by building after the rebuild (each in its branch, amended, pushed, merged into play again with play's side
for the rest): fix/rough-terrain-damage-surface (upstream's JumpLinks.Recorder implements Rough(v, surface, dt)) and
perf/horizon-early-z (the ring's session cache keeps the chunks' Rim flag; upstream's HorizonEdgeTests source check
reads the include and the eye set through HorizonTerrain.Set).

Test status, upstream rebuild (`play` 7f21cc6), laptop: Sim 1001/1033, Net 450/467, Game 300/300, Formats 962/997 (30
skipped), Editor 222/222. Against upstream master's own run on the laptop (32988ae: Sim 698/729, Net 404/421, Game
67/67, Formats 753/786, Editor 222/222; 41 failures, of which upstream's two new BotJumpTests are outside the laptop's
0.11.0 baseline): play adds the three known Sim ones (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam,
T01FollowTests at 30 Hz) and VegetationTests.T02Sward's timing line (3.7 s against 1.5 s under load; in the laptop's
0.11.0 baseline), and passes AiStuckTests...TurnsOnIt, which fails on upstream. New because of upstream: BotJumpTests'
two (fail on upstream alone on this machine). New because of us: none.

Desktop (`play` 7f21cc6; I76_INSTALL and I76_NITRO_INSTALL set; an emulator lane running): game/ built; Game 300/300,
Sim 1002/1035, Net 442/467, Formats 976/997 (16 skipped), Editor 222/222. Outside the desktop's 0.11.0 baseline: the
known four (CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the
Nitro install); tests that fail on upstream master too (BotJumpTests' two, AnetPunchTests' symmetric NAT, AiPathTests'
Seminole bridge, BrakeTests' melee stop, FlameDamageTests, FrameRateTraceTests' two, MortarRangeTests' WP mortar);
upstream's new CoopNetTests.AGateTheScriptSlidesMovesTheClientsColliderToo, which fails on upstream master alone on this
desktop too (the host's gate does not finish its slide within the wait; it passes on the laptop); and load flakes that
pass when run again or on the laptop (EightPlayersShareAStoryMission, HostMigrationTests' bots) or are network timing
(PredictionTests' laggy link, AnetMeleeTests' NAT roster; both pass on the laptop). New because of us: none.

Not deployed in this pass (the coordinator deploys after folding in the day's pending branches).

## Pass 11 (2026-11-03)

Thirteen round-3 branches built on the old play (d0a9384) were rebased onto the rebuilt play (52b62c5) by their own
commits (`git rebase --onto 52b62c5 d0a9384`, the stacked ones onto their base's new tip), force-pushed to `play`, listed
as manifest section K and merged by sync-play's incremental path (`-NoRebase`: without it the script would rebase the
eleven other-session branches that are no longer checked out in their worktrees onto upstream and merge them a second
time), dry run first; rerere replayed the resolutions of a scratch pass of the same merges in the same order (the trees
identical). Then fix/round3-integration-8.

Rebase resolutions (in the branches):
- feat/graphics-submenus: Overlays.cs takes the branch's pages over the rebuilt flat list; its Update Check row goes
  (GraphicsRow, the Display page, the copy and the tests; the changelog and the deviations row say Display is Resolution,
  VSync, Max FPS, Render Scale, Renderer, Log), upstream's Check for Updates staying on Play Options' second page.
- feat/smooth-keyed-edges: TextureFactory keeps upstream's `Make` (the session store) and adds the branch's KeyEdge
  methods after it. fix/compass-smooth-edges moved to the rebased first commit (eb71edf) and is not listed.
- fix/vehicle-lights-strength: CarNode keeps upstream's weakly keyed GlowMats (`Add`) with the branch's day_floor.
- feat/lighting-original-colours: deviations rows merged word by word (upstream's Horizon panels, the branch's Lighting,
  the Sky row with both edits). Paint: upstream clear-coats every car all over and the Hood Finish block already acts on
  that paint, so Matte (the branch's new default) takes the coat off every car and Satin and Gloss keep it; the shaders'
  global i76_paint_finish starts at Matte (project.godot had Satin) and a wheel face's hubcap drops its coat under Matte.
- fix/map-edge-row-name: the flat list's Map Edge row (18 after the close-up) renamed.
Merge resolution (rerere): fix/map-edge-row-name over feat/graphics-submenus in Overlays.cs and EscapeMenu.cs keeps the
pages and names the World page's row Hide Map Edge Cliffs on both menus.

fix/round3-integration-8: the last page's title is Effects (the Esc pad prints it over the title art, where "Effects &
Textures" ran past it), on both menus, the changelog and the deviations row; the Developer panel's Signals plot the bass
shaker's peak (BassShaker hands each block's peak to DevSignals, zero while nothing plays) under a header saying so,
where it said "no LFE output on this build" and plotted zero; docs/dev-hooks.md the same; DevSettingsSignalsTests names
the five lfe_* keys (sliders), tactile_eq, game_eq and movie_eq (hand-set only) and lighting_calibration; two comments
said Satin was the default Hood Finish.

Versions unchanged: snapshot 24, protocol 26, shell.cfg 13 (no pass-11 branch bumps either).

Test status, pass 11 (`play` d70996c): desktop (I76_INSTALL and I76_NITRO_INSTALL set, an emulator lane running) game/
built; Game 328/328, Sim 1006/1035, Net 444/467. Sim: every failure is on the desktop baseline list; the known four
(CheckpointTests T14, CoopFormationTests...IsNotThatHumansRam, T01FollowTests at 30 Hz, CaltropTests with the Nitro
install) now pass (fix/known-test-failures). Net outside the baseline: upstream's BotJumpTests' two, and four that pass
when run again alone (AnetMeleeTests' two NAT tests, BotCarsTests.TheNewHostKeepsTheSameUnarmedBotCars,
CoopNetTests.TheSuccessorsReplayOfAMissionIsTheHostsGame t01; 7/7 alone): load. Laptop: Sim 1004/1033, Net 449/467,
Game 328/328, Formats 985/1020 (30 skipped), Editor 222/222; outside the laptop's baseline only upstream's BotJumpTests'
two and BotTests.ThreeBotsPlayAMinuteOfMeleeOverALossyLink (the laptop-load flake, in the desktop baseline). The three
known Sim ones are gone there too (Sim 1001 -> 1004 passed). New because of us: none.

## Pass 12 (2026-11-03)

Five branches added as manifest section L. fix/ai-dropper-gate-as-original, fix/hd-menu-port-edits and feat/hd-pack-scale
were merged with --no-ff (CHANGELOG.md conflicts only, both sides kept). feat/death-variety and feat/brush-impacts were
built on the old play (d0a9384): a merge would drag the old history in (the first attempt conflicted in 66 files, add/add
on everything the rebuild re-created) and was aborted; their own commits after d0a9384 (two and one) were cherry-picked.
Before the next sync-play run these two must be rebased onto play (`git rebase --onto <play tip> d0a9384`) or the script
will merge the old history.

Resolutions: CHANGELOG.md, both sides; docs/deviations.md keeps the branch's Car deaths and Brush impacts rows beside
play's current Police light bar and Roads rows (the branch's older copies dropped); docs/playing.md adds brush_impacts
above the lighting_calibration row; Shell.cs ShellSetting has DeathVariety and BrushImpacts (properties, read, write)
beside LightingCalibration/LightingFilmic; WreckFire keeps death-variety's delayed `SetBurn(kindle, heat)` and adds
brush-impacts' `BrushImpacts.Heat` call.

Versions: snapshot 24 -> 25 (death-variety only), protocol 26 -> 27 (death-variety only); WireTests pins 25 and 27.
Only one branch bumps either, so nothing to reconcile; the snapshot guard passes.

Test status, pass 12: desktop, I76_INSTALL set. Editor 222/222, Game 336/336, Formats 1001/1036 (30 skipped, 5 failures),
Net 440/467 (27), Sim 1020/1049 (29). Sim's 29 and all the Net and Formats failures but seven are the same tests that
fail on the previous play tip (df066fa, run on its own build): the desktop baseline. The seven outside it (VegetationTests
T02Sward..., five network tests and VisibilityTests.EveryoneSeesEveryoneAfterANewRound) passed when rerun alone (Vegetation T02Sward, the migration, prediction and visibility ones, the hidden-cars Anet one; the punch pair and SixBots failed in a
first rerun under load, then passed alone: 2/2) and the same tests pass alone on df066fa: load. New because of us: none.

## Pass 13 (2026-11-04)

Two branches added as manifest section M, both made on play a3b949f (pass 12) and merged with --no-ff: fix/hardpoint-textures
(I78) and fix/hd-pack-3x (I79). Before the run feat/death-variety and feat/brush-impacts were pointed at their cherry-picks on
play (b091233 and 93cc458) so the script sees them as merged instead of merging their old-base history (the pass-12 note).

Test status, pass 13 (`play` c2237c7): desktop, I76_INSTALL set, the movie render on the GPU. Game 336/336, Formats 1002/1037
(30 skipped, 5 failures in the first run, 4 when rerun to list them: GameDiscovery ServerPlacesFindTheGameNextToTheExecutable,
GogLibrary Drive_letters_map..., InstallAndMigration App_folder_is_the_one_holding_the_mac_bundle, TerrainSurface
ParallelBuildAndCachedCopy...; the fifth is the order-dependent WeaponTests.EveryWeaponSlotHasItsHardpoint, which passes alone),
all in the desktop baseline. New because of us: none. Deployed c2237c7 to the laptop, the three Macs.

## Pass 14 (2026-11-04)

Five branches added as manifest section N (rows I80-I84), all made on play 4cac47e and merged with --no-ff. Checked but not
changed: the T05 jump's fall (gravity 9.8 m/s^2 and no airborne drag, as the spec; clearing needs 45-47 m/s at the lip, matched
at 120 mph; the "fell too fast" feel is most likely the nose pitching down ~90 degrees over the flight below ~110 mph, which
the spec does not cover). Proposal only, no code: the graphics options reorganisation (`_scratch/r3-gfx-options/proposal.md`).

Resolution: feat/lfe-3 conflicted with fix/dev-panel-2 in docs/playing.md (the `[port]` table's tail): both kept, the
lfe_offroad and lfe_bumps rows above dev-panel-2's dev_fps row (rerere recorded). DevSettings.cs auto-merged.

Test status, pass 14 (`play` 83408bb): desktop, I76_INSTALL set, a performance survey sharing the GPU. Game: no failures;
Formats: 5 failures (GameDiscovery, GogLibrary, InstallAndMigration, TerrainSurface ParallelBuild, Vegetation T02Sward), all in
the desktop baseline. New because of us: none. Deployed b8e7233 (83408bb plus the play-meta merge and feat/lfe-3's sweep-harness commit 8ee9178, which the second sync
picked up: the knock level to 0.6) to the laptop, the three Macs.

## Pass 15 (2026-11-04)

Two branches as manifest section O (rows I85-I86), both made on play after pass 14 (83408bb) and merged with --no-ff: the map
edge cliffs option removed, Hood Gloss as a level. The old map-edge branches stay in sections F-N as history; a -Rebuild would
merge them and then this removal, in order.

Resolution: feat/hood-gloss-level conflicted with remove/map-edge-cliffs in CHANGELOG.md, docs/deviations.md, docs/playing.md,
GraphicsPages.cs and GraphicsPagesTests.cs (the same World/Car rows): hood gloss's side taken with the map-edge row, value and
test line dropped (rerere recorded).

Test status, pass 15 (`play` ca59d29): desktop, I76_INSTALL set. Game 349, no failures; Formats 1030, 5 failures (GameDiscovery,
GogLibrary, InstallAndMigration, TerrainSurface ParallelBuild, Vegetation T02Sward), all in the desktop baseline. New because
of us: none. Deployed ca59d29 to the laptop, the three Macs.

## Pass 16 (2026-11-04)

Two branches as manifest section P (rows I87-I88), both made on play after pass 15 and merged with --no-ff: the shaker's
second and third rounds (feat/lfe-3 stays as merged; its lane continued on feat/lfe-4) and the three-page Graphic Detail.

Resolution: feat/graphics-pages-3 conflicted with feat/lfe-4 in DevSettings.cs (the panel's key list: lfe-4 added the
lfe_cracks/lfe_strip sliders where pages-3 moved dev_fps up among the keys that have a row): both kept (rerere recorded).

Test status, pass 16 (`play` da88d22): desktop, I76_INSTALL set. Game 356, no failures; Formats 1037, 5 failures (GameDiscovery,
GogLibrary, InstallAndMigration, TerrainSurface ParallelBuild, Vegetation T02Sward), all in the desktop baseline. New because
of us: none. Deployed da88d22 to the laptop, the three Macs.

## Pass 17 (2026-11-04)

Four branches as manifest section Q (rows I89-I92), merged with --no-ff: the card after the briefing, font-drawn counter
digits, the two shell layout moves, and the two-stage deaths (for playtest; the drawn-throw trade-off is in its row).

Resolution: feat/death-variety-2 conflicted with fix/briefing-overlap-title in docs/playing.md (the `[port]` table's death_variety
and briefing_while_loading rows, adjacent): the new deaths text with the title fix's loading-card wording (rerere recorded).

Test status, pass 17 (`play` 3f58a55): desktop, I76_INSTALL set. Game 363, no failures; Formats 1037, 5 failures (GameDiscovery,
GogLibrary, InstallAndMigration, TerrainSurface ParallelBuild, Vegetation T02Sward), all in the desktop baseline. New because
of us: none. Deployed 3f58a55 to the laptop, the three Macs.

## Pass 18 (2026-11-04)

One branch as manifest section R (row I93): the pack tool's family table and alpha step (tool and docs only; the rebuilt
packs are user data, already installed).

Merged by hand after the sync (`play` af49c28): CHANGELOG.md both sides; docs/playing.md's HD textures paragraph takes the
HD Textures row's text (pages-3) with the pack tool's family-table phrase (hd-pack-alpha). The merge commit was pushed with the
paragraph's conflict markers still in it (the resolution script bailed and the commit ran anyway); f8022fc fixes the paragraph.
No deploy: tool and docs only.

## Integration notes — pass 19 reconcile (2026-11-05)

Upstream master 8edea2a (39 commits over play's base 32988ae) merged into `play` (f8022fc) as one `--no-ff` merge on
branch `play-reconcile`, resolved by hand (rerere recorded every file). Nothing pushed; `play` itself unchanged.

**Absorbed by upstream, dropped from the stack** (upstream's code taken, ours removed):
- fix/difficulty-damage → be3d718. `ShellSettings.ApplyTo` and `Combat.NetworkGame` (which counts `RemoteSim`) are
  upstream's; our duplicate `NetworkGame` is gone; `PlayOptionsCombat.Apply` now calls `ApplyTo` and adds only Unlim.
  Ordnance; `PlayOptionsCombatTests` removed (upstream's `PlayOptionsSimTests`); `DifficultyDamageTests` kept (handgun
  and flame cases upstream's tests do not have).
- fix/heated-seats-ammo → a99303d (`Specials.AtLoad`). `Armory.HeatedSeats` and its SpecialsTests case removed.
- fix/editor-tool-column-scroll → e1926cc. `EditorStyle.ColumnHeight`, our ScrollContainer and `EditorToolColumnTests` removed.
- fix/macos-mic-opened-on-first-cb → 0f2ebef + 692c9c8. CbVoice, project.godot (`enable_input=false`) and the CB doc
  paragraph are upstream's; the CHANGELOG's macOS-microphone bullet removed.

**Superseded by upstream** (upstream does the job; ours removed):
- feat/host-form-redesign, fix/host-form-polish, and fix/shell-layout-2's arena-scrap move → 56373bd (the base host form
  drawn by the port as a ruled sheet). Gone: our options box (`HostBox`, `BakeBox`, `CoopHot`, `HostBoxHotspots`), the arena
  sketch scrap, the rule tips, the arena list's bar. Kept on top of upstream's sheet: Ignition Lock (id 39) and Handicaps
  (43) in the left table's spare rows, and on the co-op form Co-op Scaling, Co-op Damage, Rolling Start (40-42); the build
  line (`DrawBuild`) over the drawn sheet and the Nitro form. fix/shell-layout-2's bookmark Delete move stays.
- perf/horizon-early-z → 8625cce + 798c651 (judgment call). Upstream's outside ground rewrote HorizonTerrain and the ring's
  shader include; its near chunks take early depth rejection, but the ring itself still writes DEPTH on every path. Taken:
  upstream's horizon_terrain.gdshader/.gdshaderinc, HorizonTerrain.cs and HorizonEdgeTests; horizon_rim.gdshader removed.
  Re-applied: our `haze_gain` (the modern-colours branch) in the include, a `HorizonTerrain.Set` helper (the material only),
  and ApplySky gives `OutsideTerrain.Modern` the ring's material again so the near chunks get the new haze gain.
  Alternative not taken: port the main/rim split onto upstream's `HORIZON_RING` path (it measured 2.8 ms of 10.4 on T01 at
  3440x1440); a perf lane can redo it on upstream's include.

**Combined (ours kept on top of upstream's structure)**:
- Play Options: upstream's scrolling list (46924ca, d68c2ed), its eleven rows first, then ours (Unlim. Ordnance, Head
  Tracking, Head Track Sens., Rumble Strength, Melee A.I., Wheel Strength); Force Feedback is our value row (Off / Pad /
  Pad Enhanced / Wheel). ScrollRowsTests' order test lists the seventeen.
- Graphic Detail: our pages (feat/graphics-pages-3) kept; the mouse rows (Mouse Camera, Camera Return, View Zoom Reset,
  Mouse In Window) removed from the Camera page, the enum, `GraphicsPages.Copy`, the shell sheet and the Esc pad, since
  upstream's Mouse Options (7c3dbe8) has them. The Camera page now holds Field of View alone (alternative: fold it into
  Display and drop the page; not done, to keep the graphics-defaults lane's merge small).
- fix/mouse-in-window-menus (#147): `CursorPolicy` keeps the menus' Confined mode alongside upstream's cockpit capture;
  Main's call keeps the network standings' pointer and upstream's Cockpit Mouse Look.
- Control Configuration: upstream's Mouse Options entry sits one row lower, under our Pistol Left / Right (`MouseRow`).
- ChaseCamera: upstream's `ChaseOrbit`; our `DeathOrbit` (renamed `_death`) and `ViewFov.Main`.
- Cockpit reticle: ours (Field of View) with upstream's non-null `tex`. SmokeLayer: ours (the cached uniform set).
- MouseLook: upstream's sensitivity API with our `*` marks on the legacy names.
- Salvage: ours (a superset of 9be3ffc: specials at 1, and the tier table's stop).
- VehicleFactory / MissionSetup: upstream's `specials:` parameter and `ModelSphere`; our `HeliStep.Setup(..., network)`.
- MeleeSave / JumpLinks.cs: upstream's revert of the bots' ramp jumps (f8db1bb) taken, JumpLinks deleted (ours only had the
  `Rough(v, surface, dt)` signature); the CHANGELOG's "Bots use ramps" bullet removed. Nothing of ours stacks on f2ac22c.
- CombatView's line mesh goes through `MeshArrays.AddSurfaceArrays` (upstream 4acee3e's stress test).

**Formats**: protocol 28 = upstream's 22 (Chase Crosshair online, roster names) on our 27. `JoinAccept`: ours (ignition,
handicaps, build) then the crosshair byte (read when present). The session answer's tail: ignition, build, crosshair,
arena name, then (after the tail) each driver's make and model. A play client and an upstream server refuse each other by
version (`protocol 28; this server speaks 22`). Snapshot stays 25 (upstream did not bump its 15). Re-pinned: WireTests
(`TheProtocolIs28`, the tail cuts), DiscoveryTests, PasswordTests, BuildIdTests.

**Conflict files and how each went**: CHANGELOG.md (ours, plus the reconcile line, protocol 28, stale bullets edited);
docs/dev-hooks.md, docs/playing.md (both, word-merged); docs/deviations.md (both; Graphic Detail and Play Options rows
rewritten for the merge; Specials upstream's); docs/multiplayer.md (host-form paragraphs upstream's, our rows added; CB
microphone upstream's; protocol 28 noted); InstallLocator.cs, MainNet.cs, ShellFrame.cs, Shell.cs (both); Main.cs
(combined: ApplyPlayOptions, cursor, Far + sky scroll; the horizon setup upstream's); ChaseCamera.cs (combined);
MapEditorUi.cs, CbVoice.cs, project.godot (upstream); ControlsScreen.cs (combined); MeleeScreen.cs, NetForms.cs (upstream's
drawn host + ours: roster CONNECT TO SERVER 38 in mode 8, ignition/handicap/co-op handlers, the crosshair toggle);
Overlays.cs (Play Options combined, Graphic Detail ours less the mouse rows); Cockpit.cs, SmokeLayer.cs (ours);
EscapeMenu.cs (ours less the mouse rows); horizon_terrain.gdshader/.gdshaderinc, HorizonEdgeTests.cs (upstream);
Discovery.cs, InputQueue.cs, Protocol.cs, MeleeHost.cs, tools/I76.Server/Program.cs (combined); MissionSetup.cs, MeleeSave.cs
(combined); JumpLinks.cs (deleted); Salvage.cs (ours); VehicleFactory.cs (upstream); CursorPolicy.cs, MouseLook.cs
(combined); Combat.cs (upstream's difficulty, our impact helpers); Combat.Droppers.cs (ours on the shared factor);
HostFormLayoutTests.cs (upstream's plus our ids; spare left rows 4 → 3); DiscoveryTests, PasswordTests, WireTests (re-pinned).

**A future rebuild must redo**: the Play Options rows of feat/head-tracking, rumble strength, feat/bot-skill-from-difficulty,
the ordnance and wheel-strength branches as rows of upstream's list (they were page-2 placements); the host-rule branches
(ignition lock, handicaps, co-op scaling/damage/rolling start) as HostFormLayout rows; feat/graphics-submenus /
graphics-pages-3 without the mouse rows; fix/mouse-in-window-menus against upstream's CursorPolicy; the death orbit
(fix/death-camera-orbit-rate) on upstream's ChaseCamera; feat/net-* protocol numbers counted from upstream's 22 (play's 28);
`HorizonTerrain.Set` and `haze_gain` on upstream's include; CombatView's `MeshArrays` call. Manifest edits:
`_scratch/r3-reconcile/play-branches.patch` (not yet on play-meta).

**Tests** on ff27ef1, desktop, I76_INSTALL set, against f8022fc built and run the same way: Sim 1154 passed / 29 failed, the same 29 names as f8022fc's (1041 / 29); Game 390 / 1, the one RoadPieceTypeTests.EveryStockPieceIsStillPainted (upstream's new test counts 392 stock pieces; this install's addon folder holds co-op pack levels, 639; not in f8022fc); Net 454 / 18 against f8022fc's 447 / 20: gone BotJumpTests x2 (reverted upstream) and AnetPunchTests x2 (fixed upstream 4e9a234), new CoopNetTests.EightPlayersShareAStoryMission (passes alone) and PredictionTests' laggy link at 50 ms (Load=Sensitive, fails alone on f8022fc too); Formats 1011 / 4, the desktop's four path and parallel-build failures, as f8022fc. Without I76_INSTALL: Game 363 / 0, Sim 1187 / 0, Net 472 / 0.

**Smoke run** (Godot 4.7.2, 1280x960, `_scratch/r3-reconcile/*.jpg`): the shell's Graphic Detail sheet and its Look, Display and Camera pages, Mouse Options, Control Configuration, Play Options (top and scrolled to the end), the base host form (drawn sheet with Ignition Lock and Handicaps) and the Nitro host form, each in both looks; T01 starts; T05 and M01 at the map's edge (the ground runs on past it in both looks; `I76_CAP_CAM` did not move the cockpit view, so these are from the car). No exception in any log (only the fresh worktree's missing boot_splash import). The Hide Map Edge Cliffs row was already gone (remove/map-edge-cliffs); upstream's outside ground is the answer. Control Configuration's Mouse Options row sits on the last ruled line, just over the button bar.

## Pass 23 (2026-11-06)

Eight branches as manifest sections AK-AR: menus with tooltips, Advanced and Controls (AK), menu art at 4x with retyped
lettering (AL), terrain-shadow probe hooks (AM: the T05 band is the ridge's real shadow, no player-visible change), firing
frame rate (AN), dash shudder (AO), night lighting (AP), cockpit art round 5 (AQ), road signs and gas pumps (AR, pass 23b).
Held for the owner: revert/explosions-to-upstream (the modern flipbooks were missing from game/vfx on every machine until
this pass; main with them, the death_variety default and effects_* scaling explosions are the open questions).

Resolution: fix/world-art-signs-pumps conflicted with fix/cockpit-art-round5 in tools/upscale-textures.py and
tools/test_upscale_context.py (--cells and --recut, both kept). One fix on main: a tooltip for dash-shudder's sway_snap row,
which the menus branch's tooltip test caught.

Test status, pass 23 (`main` 1b6f7dbd, f2b22514 after AR), desktop, no install paths set, `--filter "Load!=Sensitive"`: Game
552/0 (554/0 after AR; 23 install skips), Sim 1203/0, Editor 230/0, Formats 926/0 (124 skips), Net 464/0 earlier in the pass.
Deployed f2b22514 to the laptop, James's Mac and Michael's Mac (Andrew's Mac drops its connection mid-build: still on pass 22's
build, with pass 23's packs); 397 changed HD pack files synced (tex-menu, tex-world-4x, tex-world-8x where present).
Laptop smoke T01 1280x960: loads and drives on the 4x pack.

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
