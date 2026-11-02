# Patch stack tracker

The private list behind the `play` branch: every change this fork carries ahead of upstream, what it changes, the evidence
it rests on, and the order to offer it upstream as pull requests. Lives on `play-meta` (merged into `play`) only; never on
a topic branch. `tools/play-branches.txt` is the machine-readable merge order; `tools/sync-play.ps1` rebuilds `play`.

Evidence classes: **original** = measured in the 1997 retail game (emulated Pentium) or read exactly from its own data;
**kit** = measured on the original executable under the patch kit (a proxy; keys named where they matter); **spec** =
code read against the project's own notes of the original, not run; **local** = an artefact of the port, checked in the
port itself; **ux** = the original has no such behaviour to match. PR state: none / open #n / merged / withdrawn.

Ranking: bugs players reported upstream first; then measured fidelity gaps with original-game evidence; then gaps with
kit or spec evidence; then features (each behind an option with a deviations row); the simulation clock last, raised as
an option when the rest has landed. One or two PRs open at a time; the next on the same area waits for a reply.

## A. Reported upstream

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 1 | fix/wheel-suspension-prime | a placed car's wheels start from the ground under them: no drop and bob at mission start, respawn, melee join (#141) | original: stationary car travel exactly 0 from the first substep in T01/T08/T16; kit n=2 within 2.5 cm; I26 stock dips 8 cm, bounces 16 cm, ~0.7 s | none |
| 2 | fix/siren-gpw-loop | a police siren loops as its sound file's play mode says; the on-edge is counted when the voice starts (#48, #51) | original data: 26 stock sound files are loop mode; kit event trace: T10's one-shot horn file plays once, so the file's mode is the rule; the ".45 kill plays the siren" half is the original's own behaviour (answer, no fix) | none |
| 3 | fix/script-camera-cuts | a cut to another placement of the same camera op is a cut, not a blended move (#140) | local: headless t01 1 / t04 6 blended cuts -> 0; live 1997-look capture 3 frames -> 1 | none |
| 4 | fix/mission-music-wrap | mission music goes 2..14 again after 14 (#128) | original: the exe's own CD requests n=2: 3..14 then 2..14 | none |
| 5 | fix/smoke-sky-flicker | 1997 damage smoke no longer flickers over the sky in the cockpit; modern clumping on game time | local: 61 frame pairs, sky change 26.3 % -> 0.31 % (= no smoke); owner report (Mac) | none |
| 6 | fix/macos-entitlements-der | macOS: re-sign with rcodesign; bundle check wants Apple's DER entitlements | original n/a; checked on the Mac: shipped 0.11.0 fails spawn (163), branch launches | open #144 |
| 7 | fix/macos-mic-prompt-on-first-cb | docs: the microphone permission is asked at start on macOS | local (owner report) | none; upgrade to a code fix (open the input lazily) todo |
| 8 | todo | #124 six-wheelers' middle pair gets suspension (the sim never steps it) | spec: the original runs the suspension step on the middle pair with transfer 0 | none |
| 9 | todo | #106 right-click in Control Configuration dead in a session (needs a live repro) | local | none |
| 10 | todo | #95 smoke seen through terrain; #118 bridge deck underside; #142 audio hiccup after the loading card; #143 first garage load; #116 first-explosion stutter | local; each needs a repro first | none |

## B. Measured against the original (retail 1.0 on an emulated Pentium 166)

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 11 | fix/taurus-load-cornering | Taurus's car takes load-based cornering as the original sets it at load | kit: t02 race with the player parked 94.8 s; I26 100.5 s; with the flag 95.3 s; AI replay on the kit's world throttle 0.40 -> 0.63 vs 0.65 | none |
| 12 | fix/difficulty-damage | Difficulty Level reaches the combat: player rounds x2.0 Wimp / x0.75 Badass offline, x1 online | kit: damage per round Champ 25.3, Badass 18.0 (n=2); spec factors 2.0/1.0/0.75; I26 25 at every setting | none |
| 13 | (lane) fix/ai-turn-speed-at-spawn | the AI's turn speed is computed at spawn (divisor at its default) and on wheel damage, not from the live car | original: 13 AI cars 12.52198 m/s, player 13.71714, zero spread over 2 boots; I26 13.09 / 14.36 | none |
| 14 | (lane) fix/ai-avoid-scripted-lead-car | T02's opening: the scripted player car no longer backs away from Taurus ahead | original: camera released 13.9-14.0 s after the second frame (2 boots); kit 13.96; I26 20.17 | none |
| 15 | (lane) fix/fsm-timer-rounds-to-nearest | script timers round the start time to the nearest second | original exe bytes exact; kit 6 runs match rounding, truncation wrong in 5 of 6 | none |
| 16 | (lane) fix/rocket-catchup-eases-by-frame | a burst's rockets all leave at the frame's eased speed | original: 13 rockets, 2 boots, residual <= 0.003 m/s | none |
| 17 | (lane) fix/brake-stop-keeps-accel-one-tick | the nose settles the tick after a braking stop, not on it | original: 34 stops, 2 boots: 0.53 +- 0.13 deg the next frame | none |
| 18 | (lane) fix/mirror-fov-and-far-clip | rear mirror 45 deg across, 100 m far clip (both looks) | original: 43 +- 1.5 deg measured, nothing past ~100 m; static exact | none |
| 19 | (lane) fix/sky-scroll-counts-mirror-pass | 1997 look: clouds drift twice as fast with the full cockpit mirror | original: 2.000 sky calls a frame with the mirror on, 1.000 off (4 boots) | none |
| 20 | (lane) fix/engine-loop-pitch-formula | engine loop pitch follows the original's frequency formula | original: formula read exactly from the retail exe; I26 a placeholder about 7 semitones low at idle | none |
| 21 | (lane) fix/ordnance-live-cap | at most 160 live rounds per kind, 40 online for droppers, oldest evicted | kit (same bytes as retail): cap field 160/40, live plateau 41 online; I26 190 | none |
| 22 | (lane) fix/ai-avoid-probe-left-track | the avoidance no-go probe reads the left track | spec, a vanilla trace exists | none |

## C. Kit or spec evidence (code read against the original's rules)

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 23 | fix/1997-tracers | 1997 look: MG tracers a one-pixel line over the front 65 % of the step, cannon the whole step, no cap | kit: same-pose pairs match the branch's look | none |
| 24 | fix/modern-target-health-bar | modern look: the target bar has the original's 30 x sqrt(hp) geometry | spec | none |
| 25 | fix/bookmark-cancel-returns | Save Bookmark's Cancel returns to the screen that opened it | ux (the original's flow is not recorded) | none |
| 26 | (lane) fix/map-voice-line-on-sfx-bus | the map's line follows the SFX slider | kit event trace n=3 | none |
| 27 | (lane) fix/radar-sweep-and-pings | 30-frame sweep, LED/growl to 600 m, ping per sweep | exe bytes / spec | none |
| 28 | (lane) fix/1997-road-light-no-depth-fade | 1997 roads lit without the depth fade | spec | none |
| 29 | (lane) weapon fire loop: fix/round-start-offset-only-with-spread, fix/trigger-repress-click, fix/unlimited-ammo-ai-keeps-firing, fix/burst-rearm-after-batched-shot | details of the fire timer and round spawn | code vs the original's code | none |
| 30 | (lane) damage and ordnance rules: fix/ai-ezk-mortar-fires, fix/rounds-hit-signs-and-posts, fix/ai-sight-line-and-structures, fix/explosion-template-by-key, fix/hplesser-structure-part-pool, fix/splash-damage-no-isshot, fix/turret-slew-online-and-elevation, fix/zero-length-hit-zone, fix/rough-terrain-damage-surface | | spec | none |
| 31 | (lane) sound: fix/cb-queue-with-voice-off, fix/gpw-volume-field, fix/silent-dropper-hit-sounds, fix/sound-cull-from-player-car, fix/sfx-voice-slider-curve | | spec; eimarx measured | none |
| 32 | (lane) fix/ai-back-away-terrain-height, fix/dash-mirror-slot-position, fix/overview-zoom-per-tick, fix/hood-view-shows-own-car, fix/script-camera-arrived-flag, fix/brake-lamp-hold-row, fix/1997-road-texture-sampling | | spec / kit pairs | none |
| 33 | todo | physics and collision: ramp rails drive-on, sphere-part colliders, AI predictor clearance, blown-tyre pull gate, fatal landing ends the substep, breakables per part, network helicopter lift, contact order statics last, class 8/9 placements grounded | spec | none |
| 34 | todo | AI droppers and mortar: the target predictor's +0.09 s and arc; AI mortar release | spec | none |
| 35 | todo | missions and trip: cheats marker on trip wins, default car without a shell car, salvage specials amount, setUserRadar, start_engine pulse; small audit items (isShot two frames, teleport terrain y, tactic_skid modulus) | spec | none |
| 36 | todo | network: own shots of a jammable weapon not drawn ahead (MP-B1), ping shown as a low percentile (MP-B2), the shots counter at respawn (MP-B3), end-of-match brake (MP-F2), remote health in three steps (MP-F3, deviations line), co-op bonus (deviations line) | local measured / spec | none |

## D. Features (each off by default behind an option, with a deviations row)

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 37 | (lane) feat/head-tracking-opentrack | head look from opentrack's UDP output; glance overrides; 150 ms ease-back | ux | none |
| 38 | (lane) feat/original-save-reader, feat/import-original-saves-ui | import the original game's saves as bookmarks and a trip car | original data (byte-exact format) | none |
| 39 | todo | wheel force feedback on Windows (DirectInput sink for the original's effects; strength and pavement scale) | ux | none |
| 40 | todo | pad rumble: enhanced events and strength | ux | none |
| 41 | todo | multiplayer additive: sticky scoreboard with kick (#107), ban list (#117), return to the server picker (#122), host rule "ignition stays on" (#108), co-op scaling / damage factor / joiner matches speed, bot skill from Difficulty | ux / requests | none |
| 42 | todo | options: field of view row, resolution row (#81), render scale and 8x MSAA, update check off by default (#138), matte hood, modern tracers, original cursors | ux / requests | none |
| 43 | todo | a bookmark for every mission; German voices overlay; extra music shuffle; telemetry UDP out; input record/replay; --skip-cutscenes / --mute; key sheet; Deck layout | ux | none |
| 44 | feat/dev-menu | a Developer panel off for players (--dev, I76_DEV=1): teleport, repair, God mode, store/restore, step | ux | none |

## E. Last: the clock

| order | branch | what it changes | evidence | PR |
|---|---|---|---|---|
| 45 | feat/sim-rate-24 | Simulation Rate offers 24 Hz (85/2048 s), the original's step under a 20 fps cap on Windows NT | kit / Windows capture: frames 46.9 / 2 x 31.25 ms, about 24 steps/s; the bounce tempo gap 28 vs 24 substeps/s | none (raise as an option, obliquely, after the rest) |

## Withdrawn (not offered)

- "I26 turns 3-5 % more": a harness artefact (the brake phase's throttle left on); in lockstep at 1/24 s I26 matches the kit to 0.1 %.
- "AI flees later on the kit because of a kit fix": at engine 20 % both settings flee alike; the real gap is the turn speed (row 13).
- #114 tanker, #113 bus texture, #85 cockpit shadow, #101 pad surface, #102 regen hold, #72/#121 helicopter reverse spin, #111 far creepers: the original's own behaviour (answer with the spec; an option only if asked).
