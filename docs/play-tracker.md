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
