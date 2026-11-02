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

## Withdrawn or answered (not offered as fixes)

- "I26 turns 3-5 % more": a harness artefact; in lockstep at 1/24 s I26 matches the kit to 0.1 %.
- "AI flees later on the kit because of a kit fix": at engine 20 % both settings flee alike; the real gap was the turn speed.
- #114 tanker, #113 bus texture, #85 cockpit shadow, #101 pad surface (confirmed in the retail game), #102 regen hold (confirmed), #72/#121 helicopter reverse spin, #111 far creepers, #137 placements: the original's own behaviour; answer with the spec.
- Chase camera pitch (visual pair V5), hpLesser on a building, the jammer sound, the night LOBJ lights, the heli-dies-on-contact row: the spec does not settle them yet.

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
