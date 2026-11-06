# The `play` fork against upstream `master`

*A living page on the fork's integration branch, for upstream's lead: what this fork carries ahead of I26 `master`, one
topic branch per row, so you can decide per row whether to pull it, redo it your own way, or leave it. Brought up to
date 2026-11-05 at `play` 12ec3e5 against your `master` 8edea2a. The first ten pull requests are written out in
[upstream-handoff-first-prs.md](upstream-handoff-first-prs.md).*

## In one paragraph

`play` (published on this public fork as `main`) is your `master` at **8edea2a** (2026-11-04), merged in by hand on
2026-11-05, plus **247 topic branches** listed in `tools/play-branches.txt`, each one logical change; 238 of them are
offerable and nine are merge glue for `play` only. Against 8edea2a it touches **614 files, +53,142 / −2,309 lines**, of
which about 14,800 added lines (28 %) are tests. Since the first edition of this page (2026-11-04, against the older
base 32988ae): seven branches left the stack because your master does the job (section 2), eleven were added (manifest
sections S to AA, section 10 here), and every count and conflict below was taken again against 8edea2a. Everything is
behind an option or marked as a measured fidelity correction; nothing ships game data. Formats: network protocol
**28** (yours 22), save snapshot **25** (yours 15), `shell.cfg` version **13** (yours 11).

How the branches reach you: each topic branch is offered as its own pull request from this fork, in manifest order,
cut from your current `master` with its CHANGELOG bullet taken out (section 11). Open now: **#150** (the modern look's
smoke through terrain, #95). Earlier: #144 (macOS signing), which you landed as your own commit 32988ae. Nothing else
has been offered; the branch names below are the fork's, all published here.

## 1. How to read the tables

- **Branch**: one squashed commit unless the count says otherwise. The commit body says what the original does and
  how it was checked.
- **Evidence** (the fork's own classes): **original** = measured in the retail 1997 game on an emulated Pentium 166
  (86Box) or read from its data; **kit** = measured on the original executable under the fork's author's patch kit;
  **spec** = code read against the project's own notes of the original; **local** = a port artefact, checked in the
  port; **ux** = the original has no such behaviour (a feature, behind an option, with a `docs/deviations.md` row).
- **Size**: files, +lines, −lines of the branch as merged (tests, docs and changelog included).
- **On your master today**: what `git merge-tree` says merging the branch onto 8edea2a does (2026-11-05). `clean` =
  no conflict; `changelog` = a conflict in CHANGELOG.md only; otherwise the files that conflict (CHANGELOG.md not
  repeated). `stacked` = built on `play`, not on `master` (next point). The CHANGELOG bullet of every branch comes out
  when it is offered (section 11).
- **Stacked on play**: 120 of the 247 branches were built on top of `play` itself rather than on `master`, because
  they touch what earlier branches added (menus, the HD pack, the shaker, the Developer panel). They do not apply to
  `master` alone: take them after the branch they stack on, or treat a chain as one change (each theme in section 9
  names its chain and its net effect). The nine `fix/round3-integration*` and `fix/upstream-rebuild-integration` rows
  are merge glue for `play` only and are not offered.
- **Defaults**: anything marked *opinion* is the fork's author's taste for the family playtest builds. Take the mechanism,
  pick your own default.

## 2. Already absorbed or superseded by your master

Dropped from the stack; nothing to pull. *Absorbed* = your commit does the same thing; *superseded* = yours does the
job another way and the fork took yours.

| Ours (dropped) | Your commit | How |
|---|---|---|
| `fix/difficulty-damage` | be3d718 | absorbed: the same x2 / x1 / x0.75 rule, x1 online (`ShellSettings.ApplyTo`); the fork keeps only its extra handgun and flame cases as tests |
| `fix/heated-seats-ammo` | a99303d | absorbed: yours covers more (Dashboard Mary, Tire Covers, the Nitro specials) |
| `fix/editor-tool-column-scroll` | e1926cc | absorbed: the same issue (#146) |
| `fix/macos-mic-opened-on-first-cb` | 0f2ebef + 692c9c8 | absorbed: CB Microphone starts on None and the input stays off in the project until a device is chosen |
| `feat/host-form-redesign`, `fix/host-form-polish` (and `fix/shell-layout-2`'s move of the arena scrap) | 56373bd | superseded at the reconcile. The fork's author then preferred the fork's form, so `feat/host-form-master` (section 10) puts it back with your labelled rows and hotspot ids folded in |
| `perf/horizon-early-z` | 8625cce + 798c651 | superseded: your outside ground rewrote the distant ring. Its depth-pass split was re-ported onto your shader inside `perf/graphics-defaults` (section 10) |
| `fix/siren-gpw-loop`, `fix/macos-entitlements-der`, `fix/load-card-audio-hiccup`, `feat/update-check` | 2ddb2d4, 32988ae (#144), 01228e7 (#142), 356eddb (#138) | absorbed, dropped on 2026-11-03 |

Still in the stack but overlapping something of yours (ours would need redoing on yours, or yours decides):

| Your commit | Ours | What collides |
|---|---|---|
| 56373bd host form drawn larger on the ruled sheet | `feat/host-form-master` | ours replaces your HostForm.cs with the original's art, the options box, an arena scrap and a rules table carrying your rows (Chase Crosshair included); HostFormLayout and its tests adapted. A taste call: yours or ours |
| 46924ca / d68c2ed Play Options scrolls, second page gone | the master-based branches that add a Play Options row (Head Tracking, Rumble Strength, Melee A.I., Wheel Strength, Unlim. Ordnance) | on `play` these rows follow your eleven in your list; the branches themselves still place them on the old page 2 and need redoing as rows of your list |
| 7c3dbe8 Mouse Options screen | `fix/mouse-in-window-menus` (#147), `feat/graphics-submenus` / `feat/graphics-pages-3` | on `play` the mouse rows left Graphic Detail for your screen, and CursorPolicy keeps both the menus' Confined mode and your cockpit capture; the master-based #147 branch conflicts in Overlays.cs and CursorPolicy.cs |
| f8db1bb revert of the bots' ramp jumps (JumpLinks.cs deleted) | `fix/rough-terrain-damage-surface` | its only JumpLinks hunk was a signature change; it goes |
| 8edea2a helicopter collider sphere | `fix/network-heli-lift-at-init` | a MissionSetup.cs hunk; the fixes are independent |
| protocol 22 (Chase Crosshair online, roster names) | the protocol branches (ignition, handicaps, handicap mode, pistol keys, build id, death variety) | `play` is 28 = your 22's additions on the fork's 27; a subset is numbered again from your 22 |
| 6e5f8fc no Unreleased section in CHANGELOG | every branch | see section 11 |

## 3. Bug fixes for issues reported upstream (section A of the stack)

| Branch | What it changes | Evidence | Size | On your master today |
|---|---|---|---|---|
| `fix/wheel-suspension-prime` | a placed car's wheels start from the ground under them: no drop and bob at mission start, respawn, melee join, co-op seat (#141). Your 01228e7 settles the single-player car; this primes every placed car, AI included | original: stationary car travel exactly 0 from the first substep (T01/T08/T16); kit n=2 within 2.5 cm | 8, +264/−2 | clean |
| `fix/script-camera-cuts` | a cut to another placement of the same camera op is a cut, not a blended move (#140) | local: headless t01/t04 blended cuts 1 and 6 → 0 | 3, +64/−1 | clean |
| `fix/script-camera-arrived-flag` | every placing camera call clears camIsArrived | spec (no stock effect) | 3, +25 | changelog |
| `fix/mission-music-wrap` | mission music goes 2..14 again after 14; a mission's own track of 15+ plays once (#128) | original: the exe's CD requests, n=2 | 4, +18/−8 | clean |
| `fix/smoke-sky-flicker` | 1997 damage smoke no longer flickers over the sky in the cockpit; modern clumping on game time | local: sky change 26.3 % → 0.31 % over 61 frame pairs | 5, +77/−12 (2 commits) | clean |
| `fix/middle-wheel-suspension` | six-wheelers' middle pair gets its own suspension (#124; the tank has none) | spec | 7, +131/−12 | changelog |
| `fix/predicted-shots-of-jammable-weapon` | online, a weapon under 70 % no longer draws shots that are then taken back | local: 62 % of such shots were taken back | 5, +83/−4 | changelog |
| `fix/own-shots-counter-at-respawn` | the `I76_NET_DEBUG` shots counter no longer jumps at a respawn | local | 3, +48/−2 | changelog |
| `fix/net-ping-low-percentile` | the shown ping is the lowest of the recent samples | local: a 60 ms link at 10 % loss showed 155–624 ms | 8, +82/−5 | changelog |
| `fix/escape-menu-right-click-reconfigure` | right-click rebinding works in a network game's Esc menu (#106) | local: the fly camera grabbed the press | 3, +34/−2 | changelog |
| `fix/first-garage-load-prewarm` | the 3D garage is pre-warmed from the idle menu (#143) | local: first entry 1.2 s → 0.25 s | 6, +132/−29 | changelog |
| `fix/first-explosion-stutter` | the first building blast's sprite look is made behind the loading card (#116) | local: 122–134 ms → 21–42 ms | 5, +47/−2 | changelog |
| `feat/regen-pad-hold-readout` | the net HUD shows the regen pad's 30 s hold (#102; the rule is the original's, confirmed in the exe) | original (rule) + ux (readout) | 12, +122/−4 | dev-hooks.md, deviations.md (docs only) |
| `fix/mouse-in-window-menus` | Keep Mouse In Window honoured in the menus too (#147) | local | 8, +22/−7 | deviations.md, Overlays.cs, CursorPolicy.cs (your Mouse Options moved the row) |
| `fix/gate-fence-grounding` | fences and tyre walls centred on their line between posts (#137's gap part) | local: Formats tests, captures | 4, +83/−13 | changelog |
| `fix/1997-effect-sprites-upright` | 1997 look: effect cards upright, foot at the origin (#105) | local: EffectQuad, FireballCard tests | 5, +84/−17 | changelog |
| `fix/cockpit-art-bilinear` | the 3D cockpit's dash art sampled bilinear when looked down at | local (playtest report) | 7, +70/−14 | changelog; **superseded** by `fix/cockpit-gauge-dials-too-dark` (section 9), which measured the original and went back to point sampling at full size |

## 4. Fidelity corrections measured against the original (section B)

| Branch | What it changes | Evidence | Size | On your master today |
|---|---|---|---|---|
| `fix/taurus-load-cornering` | the car the mission names Taurus takes the original's load-based cornering whatever the play options; off the network a roofed Taurus is righted, not destroyed | kit: t02 race 94.8 s vs I26 100.5 s, with the flag 95.3 s | 4, +97 | clean |
| `fix/ai-turn-speed-at-spawn` | the AI's turn speed is computed at spawn with the default divisor and on wheel damage, not from the live car (snapshot field) | original: 13 AI cars 12.52198 m/s, zero spread over 2 boots; I26 13.09 / 14.36 | 13, +135/−24 | WireTests.cs (version pin) |
| `fix/ai-avoid-scripted-lead-car` | the blocked-ahead probe sweeps the car's origin against the other cars' boxes: T02's opening no longer backs away from Taurus | original: camera released 13.9–14.0 s; I26 20.17 → 13.65 s | 5, +164/−14 | changelog |
| `fix/fsm-timer-rounds-to-nearest` | script timers round the start time to the nearest second | original exe bytes; kit 6 runs | 3, +23/−1 | changelog |
| `fix/rocket-catchup-eases-by-frame` | a burst's rockets all leave at the frame's eased speed | original: 13 rockets, 2 boots, residual ≤ 0.003 m/s | 3, +44/−8 | changelog |
| `fix/brake-stop-keeps-accel-one-tick` | the nose settles the tick after a braking stop | original: 34 stops, 2 boots | 3, +50/−1 | changelog |
| `fix/mirror-fov-and-far-clip` | rear mirror 45° across, 100 m far clip (both looks; the modern look later makes the range a `[port]` key following the look) | original: 43 ± 1.5° measured, nothing past ~100 m | 4, +44/−1 | changelog |
| `fix/sky-scroll-counts-mirror-pass` | 1997 look: clouds drift twice as fast with the full mirror, as the original's second sky pass does (the stack later defaults `sky_drift=steady`, marked as a deliberate deviation) | original: 2.000 sky calls a frame with the mirror on, 1.000 off | 7, +52/−2 | Main.cs |
| `fix/engine-loop-pitch-formula` | engine loop pitch follows the original's frequency formula (idle ~7 semitones higher than the placeholder) | original: formula read from the exe | 5, +41/−4 | changelog |
| `fix/ordnance-live-cap` | at most 160 live rounds per kind, 40 online for droppers, oldest evicted; Play Options' Unlim. Ordnance lifts it offline | kit: cap fields, live plateau 41 online | 4, +158/−1 | changelog |
| `fix/ai-avoid-probe-left-track` | the avoidance no-go probe reads the left track | spec; a vanilla trace exists | 3, +58/−1 | changelog |
| `fix/ai-dropper-gate-straight-line` then `fix/ai-dropper-gate-as-original` | the AI's dropper gate predicts a pursuer on a straight line at its arrival time (measured 2026-11-02), and no drop under a pursuer closing straight on above ~28 m/s (the GOG build) | original, 2 boots | 4, +44/−20 and 4, +40/−42 | stacked |
| `fix/turret-lead-predictor-delay` | a turret leads through the target predictor: closest-approach time at the aim speed + 0.09 s, along the turning circle | original, 2 boots | 3, +111/−8 | stacked |
| `fix/turret-aim-moving-carrier` | a turret aims from the barrel with nothing taken off for the carrier's travel: rounds never inherit the carrier's velocity (n 114, excess median −0.02 m/s) and the original's turrets hit from a moving carrier (37/37 at 21–23 m/s) | original | 5, +82/−4 | stacked |
| `fix/cockpit-gauge-dials-too-dark` | the 3D cockpit's dial faces black under their lettering (keyed texels of an opaque face in the key's colour), the art at full size and point-sampled, needles in palette entry 160, turned off-centre (pivot 0.69) | original: software renderer captures, pixel statistics | 8, +129/−8 | stacked |
| `fix/zero-armour-damage-as-original` | test only: a round through a side whose armour is gone goes whole to the components | original (T14 damage tap) | 1, +34 | stacked |
| `fix/ai-fire-cadence-as-original` | docs only: the 1997 CD build's tactic chooser is a stub (always dirt_gunner); the 1998 GOG build draws from the weighted lists; I26 follows 1998 | original, T14 n 7 | 1, +1 | stacked |

## 5. Fidelity corrections from kit or spec evidence (section C)

| Branch | What it changes | Size | On your master today |
|---|---|---|---|
| `fix/1997-tracers` | 1997 look: MG tracers a one-pixel line over the front 65 % of the step, cannon the whole step, no 4 m cap | 4, +57/−7 | clean |
| `fix/1997-tracers-two-pixels` | the same line one pixel per 480 rows of the 3D view (two at 1080p, four at most) | 6, +73/−8 | stacked |
| `fix/modern-target-health-bar` | modern target bar: the original's 30 x 4 at 640x480 geometry, scaled | 6, +101/−17 | deviations.md |
| `fix/bookmark-cancel-returns` | Save Bookmark's Cancel returns to the screen that opened it | 4, +7/−6 | clean |
| `fix/map-voice-line-on-sfx-bus` | the map's spoken line follows the SFX slider | 2, +2/−1 | changelog |
| `fix/radar-sweep-and-pings` | 30-frame sweep in 6 s, LED/growl to 600 m at both ranges, a ping per pass | 4, +89/−9 | changelog |
| `fix/1997-road-light-no-depth-fade` | 1997 roads lit by the sun alone, no depth fade | 4, +40/−11 | changelog |
| `fix/1997-road-texture-sampling` | 1997 roads bilinear without mips (sharp into the distance) | 4, +41/−2 | changelog |
| `fix/dash-mirror-slot-position` | the 2D dash mirror in the original's slot, cropped by the frame | 5, +18/−11 | changelog |
| `fix/overview-zoom-per-tick` | F10 zoom steps per sim tick | 5, +32/−15 | deviations.md |
| `fix/hood-view-shows-own-car` | F6 hides the player's own car when looking back | 5, +46/−5 | changelog |
| `fix/brake-lamp-hold-row` | the 150 ms brake-lamp hold is modern-look only (`[port] brake_lamps`) | 5, +33/−6 | changelog |
| `fix/round-start-offset-only-with-spread` | rounds start at the muzzle on shots with no spread | 3, +80/−2 | changelog |
| `fix/trigger-repress-click` | re-pressing fire between shots plays the dry click | 3, +79 | changelog |
| `fix/unlimited-ammo-ai-keeps-firing` | with Unlim. Ammo an AI car with dry guns keeps firing | 3, +77/−2 | changelog |
| `fix/burst-rearm-after-batched-shot` | test only | 1, +84 | clean |
| `fix/ai-ezk-mortar-fires` | the EZK mortar gets its AI fire gate (it never fired) | 3, +112/−2 | changelog |
| `fix/rounds-hit-signs-and-posts` | rounds stop at signs and posts; an explosive knocks them off | 5, +144/−3 | changelog |
| `fix/ai-sight-line-and-structures` | the AI's line of fire runs centre to centre at +1.5 m and is blocked by structures | 4, +150/−4 | changelog |
| `fix/splash-damage-no-isshot` | a splash-only hit is not an attack for scripts and AI | 4, +48/−3 | changelog |
| `fix/turret-slew-online-and-elevation` | a damaged turret turns at full speed online; elevation to the stop for a target overhead; lead a fifth further | 3, +78/−5 | changelog |
| `fix/zero-length-hit-zone` | a zero-length hit zone | 2, +3/−1 | clean |
| `fix/rough-terrain-damage-surface` | rough-terrain damage by surface | 7, +68/−10 | JumpLinks.cs (your f8db1bb deleted it; that hunk goes) |
| `fix/cb-queue-with-voice-off` | CB lines not queued silently with Voice at 0 or 1 | 5, +28/−1 | changelog |
| `fix/gpw-volume-field` | the GPW volume field honoured (Moth trucks ~3 dB quieter) | 6, +78/−4 | changelog |
| `fix/silent-dropper-hit-sounds` | oil, gblox and fire-dropper hits silent again (impact sound only when the effect exists) | 4, +24/−10 | Combat.cs |
| `fix/sound-cull-from-player-car` | sounds culled by distance from the player's car, not the camera subject | 5, +5/−6 | changelog |
| `fix/sfx-voice-slider-curve` | Effects and Voice sliders on the original's 4 dB per halving curve | 5, +37/−5 | changelog |
| `fix/ai-back-away-terrain-height` | back_away reads the terrain height, not the car's | 3, +39/−3 | changelog |
| `fix/ai-avoid-chooser-order` | the avoidance chooser's order and fallbacks (left first, full lock when nothing clears) | 3, +126/−40 | changelog |
| `fix/ai-target-predictor-arc` | the target predictor's rows, gear term and +0.09 s (new `TargetPredictor.cs`) | 4, +121/−16 | changelog |
| `fix/ai-dropper-and-mortar-gates` | droppers and mortars use the predictor | 4, +114/−3 (2 commits) | changelog |
| `fix/ezk-mortar-uses-predictor` | the EZK mortar too | 3, +28/−3 | stacked |
| `fix/ai-attacker-slot-cap-zero` | an attacker cap of 0 lets one come at a time | 3, +54/−2 | changelog |
| `fix/ai-friendly-fire-scan` | friendly-fire scan: neutrals too, 4 s reach, ground distance | 3, +110/−7 | changelog |
| `fix/explosion-template-by-key` | first registration of an effect key wins (T05 pump 85 m, S07 stations 25 m) | 4, +120/−12 | changelog |
| `fix/death-camera-orbit-rate` | death and end-of-match orbit 8° per tick | 5, +81/−11 | ChaseCamera.cs |
| `fix/death-orbit-drawn` | that orbit interpolated between ticks (no 8° jumps on a fast display) | 6, +43/−6 | stacked |
| `fix/contact-order-statics-last` | a car shoved into a wall bounces off the wall | 3, +88/−7 | changelog |
| `fix/emplacement-placements-grounded` | gun emplacements stand on the ground, not at the file's height | 3, +45/−14 | changelog |
| `fix/ramp-rails-not-drive-on` | box parts of ramps and buildings solid but not drive-on | 3, +51/−3 | changelog |
| `fix/sphere-part-colliders` | multi-piece knock-offs collide per piece | 7, +220/−12 | changelog |
| `fix/modern-physics-sphere-parts` | the same under Modern Physics (P18's plane) | 2, +8/−1 (2 commits) | changelog |
| `fix/breakables-per-part-spheres` | breakables per-part spheres | 4, +101/−7 | changelog |
| `fix/ai-predictor-clearance` | mid-air AI reads the same ground height as the original | 3, +12/−6 | changelog |
| `fix/blown-tyre-pull-scripted-drive` | no flat-tyre pull while a cut-scene drives | 5, +76/−1 | changelog |
| `fix/fatal-landing-ends-substep` | a fatal landing ends the substep | 3, +89 | changelog |
| `fix/network-heli-lift-at-init` | a network helicopter rises at once | 4, +21/−3 | MissionSetup.cs |
| `fix/trip-cheat-marker` | shell cheats mark the trip as the original does (`CheatMarker.cs`) | 4, +53/−8 | changelog |
| `fix/mission-default-car` | a mission started without a car gets the Piranha | 3, +38/−1 | changelog |
| `fix/salvage-specials-amount` | field salvage takes specials as the original (tank/howitzer stop at the big gun) | 3, +85/−13 | Salvage.cs |
| `fix/set-user-radar` | setUserRadar acts (custom maps) | 10, +129/−8 | changelog |
| `fix/start-engine-pulse` | test only | 2, +66/−1 | clean |
| `fix/is-shot-two-frames` | isShot/isRammed hold two script frames | 4, +60/−1 | changelog |
| `fix/teleport-path-node-terrain-y` | teleportOffset's ground height from the path point | 8, +87/−4 | changelog |
| `fix/tactic-skid-modulus` | skid-turn chance 1 in 31, not 32 | 3, +33/−1 | changelog |
| `fix/movie-skip-deviation-row` | docs row | 2, +2/−1 | clean |
| `fix/end-of-match-brake` | the winner's 10 s brake with the wheel straight | 7, +102/−11 | changelog |
| `fix/network-deviation-rows` | two docs rows | 1, +2 | clean |
| `fix/target-tags-bold` | the garage's (C), (V), (R) legends bold and a little larger, modern look (*opinion*) | 2, +12/−3 | changelog |

## 6. Performance (pure, frames unchanged)

| Branch | What it changes | Measured | Size | On your master today |
|---|---|---|---|---|
| `perf/interop-garbage` | drop-shadow casters found once, shadow nodes told only what changed, the smoke layer keeps its uniform set | process step T01 2.08 → 1.77 ms, T05 3.04 → 2.38 (1650 Ti); finalizer thread 6.3 → 3.0 % of samples | 6, +115/−45 | SmokeLayer.cs |
| `perf/render-scale-down` | Render Scale 50/67/75/85 % with FSR 1.0 (stacked on the row) | T01 10.5 → 7.6 ms at 75 % (1080 Ti), 14.1 → 9.7 (1650 Ti) | 6, +39/−26 | stacked |
| `perf/hd-pack-load` | HD packs as BC7 `.dds` with mips uploaded as stored; a model's upscales decoded in parallel | T01 build 9.0 s no pack, 9.8 s with the 2x pack (was 13.4–18.0 s with 4x PNG) | 16, +312/−86 | stacked |

`perf/horizon-early-z` (the distant ring's depth pre-pass) was superseded by your outside ground and its split
re-ported onto your shader in `perf/graphics-defaults`. The frame-cost survey finished; it and the changes it led to
(`perf/frame-cost-survey`, `perf/graphics-defaults`) and the load-time work (`perf/load-time`) are in section 10.

## 7. Features, each behind an option (section D)

All off by default unless said; each has a `docs/deviations.md` row.

| Branch | What it adds | Size | On your master today |
|---|---|---|---|
| `feat/head-tracking-opentrack` | head look from opentrack's UDP output (127.0.0.1:4242); Analog / Digital; glance keys win | 8, +403/−8 | Overlays.cs (Play Options row) |
| `feat/original-save-reader` | reads the original game's save files (18 real saves checked) | 3, +439 | clean |
| `feat/import-original-saves-ui` | Import Saves: each original bookmark becomes an I26 bookmark with its car (later moved to the Developer panel) | 5, +349/−3 (2 commits) | changelog |
| `feat/bookmark-per-mission` | All Scenes: a bookmark per mission with its stock car | 5, +144 | changelog |
| `feat/delete-bookmarks` | Load Bookmark: Delete with a yes/no notice | 3, +82 | changelog |
| `feat/wheel-force-feedback-windows` | the original's effects on a DirectInput wheel or stick (Windows); Wheel Strength row | 14, +1395/−33 | Overlays.cs |
| `feat/rumble-strength-and-events` | Rumble Original / Enhanced / Off and a strength; Enhanced adds pulses for nitro, handbrake, gears, starter, shots | 9, +408/−6 | Main.cs, Overlays.cs |
| `feat/net-scoreboard-sticky-with-kick` | Tab locks the standings open; host boots by double-click (#107) | 13, +345/−11 | Main.cs, dev-hooks.md |
| `feat/net-ban-list` | `/ban NAME`, `/unban`, by name and address (#117) | 11, +278/−14 | changelog |
| `feat/net-return-to-server-list` | Connect to Server under the game list (#122) | 5, +45/−2 | deviations.md, MeleeScreen.cs |
| `feat/host-rule-ignition-stays-on` | host rule: nobody can switch the engine off to hide from turrets (#108); protocol field | 24, +183/−30 | the network forms and protocol (your 56373bd and protocol 22) |
| `feat/coop-enemy-scaling` | co-op: up to three copies of each enemy car, one per extra player | 24, +396/−29 | the network forms |
| `feat/coop-damage-factor` | co-op: humans take x0.5 / x0.75 / x1 / x1.5 | 19, +218/−9 | the network forms |
| `feat/coop-joiner-matches-speed` | co-op: a respawn or joiner comes in at a moving teammate's speed | 18, +213/−10 | the network forms |
| `feat/bot-skill-from-difficulty` | Melee A.I.: Instant Melee bots follow Difficulty, or Easy / Hard | 7, +121 | deviations.md, Overlays.cs |
| `feat/net-handicaps` | host rule Handicaps: a level per player scales the damage they deal; scoreboard shows it | 30, +555/−39 | the network forms and protocol |
| `feat/net-auto-handicap` + `feat/net-auto-handicap-2` | Auto: levels from the standings (a level per net kill over the average, −5..+5), dealt x0.2..x5 and taken the inverse capped x1/3..x3; melee-sim over 24 seeds: leader margin 8.3 → 2.0 kills | 28, +703/−87 and 11, +208/−120 | the network forms; the second is stacked |
| `feat/field-of-view-row` then `feat/field-of-view-in-graphic-detail` | Field of View: Monitor (the original's height, the width from the window) default, 60..110 exact degrees, 90 = the original; on both pads; `[port] fov` | 12, +99/−8 and 10, +243/−58 | ChaseCamera.cs, Overlays.cs; the second stacked |
| `fix/2d-reticle-follows-fov` | the 2D gunsight projected through the chosen FOV | 4, +45/−3 | stacked |
| `feat/resolution-row` | Graphic Detail: Desktop or a window of a common size (#81) | 8, +162/−3 | Overlays.cs, Shell.cs |
| `feat/hood-matte-option` → `feat/hood-shine-adjustable` → `feat/hood-gloss-level` | net: **Hood Gloss 0–100 in steps of 5** on both pads, 0 = the original's flat paint (default, *opinion*), 50/100 = the old Satin/Gloss, blends between; covers the cars' paint; shader global `i76_hood_gloss`; old `hood_finish`/`hood_matte` read | 9, +32/−2; 12, +83/−26; 17, +293/−72 | first Overlays.cs, Shell.cs; the rest stacked |
| `fix/modern-mg-tracers-look` | modern MG rounds as short bright bolts, not a 10 m beam | 4, +14/−5 | changelog |
| `feat/render-scale-row` | Render Scale 50..200 % (FSR 1.0 below 100); Anti-Aliasing gains 8x | 7, +95/−5 | deviations.md, Overlays.cs, Shell.cs |
| `fix/1997-look-draw-distance-default` → `fix/look-choice-rows` | the draw distance follows the look with no row (`[port] draw_distance`); one LookChoice helper; shell.cfg 12 drops the Far 0.11.0 wrote on every save | 8, +41/−14; 11, +82/−19 | deviations.md, Overlays.cs, EscapeMenu.cs; the second stacked |
| `feat/ghost-cockpit-toggle` | `]` toggles the original's ghost cockpit | 7, +30/−9 | deviations.md |
| `feat/skip-cutscenes-and-mute-switches` | `--skip-cutscenes`, `--mute` | 6, +73 | changelog |
| `feat/voices-overlay-folder` | `--voices LANG`: voice files from `voices/LANG` in the user folder override the install's | 8, +108 | deviations.md |
| `feat/extra-music-folder` | `music-extra` folder: the player's own songs one start in three | 8, +184/−2 | deviations.md |
| `feat/steam-deck-support` | playing.md section, a Steam Input layout, add-to-steam script (`tools/deck`; untested on a Deck) | 6, +782 | changelog |
| `feat/pad-shift-layer` | a pad button as a shift: a second command per button | 9, +413/−33 | changelog |
| `feat/tap-or-hold-binding` | tap vs hold on one pad button | 10, +395/−18 | changelog |
| `feat/look-back-context-bindings` | looking back: fire shoots the rear gun, R3 the dropper; stick back + handbrake toggles reverse | 7, +239/−1 | changelog |
| `fix/pad-rules-shift-tap-latch` | shift wins over tap/hold; 200 ms tap; handbrake latch | 9, +106/−36 | stacked |
| `feat/device-presets-by-name` | T300RS and CH Fighterstick layouts; any axis past half travel can give a command | 9, +485/−8 | changelog |
| `feat/second-device-together` | a wheel and a stick set up together | 7, +183/−25 | changelog |
| `feat/pistol-keys` | Pistol Left / Pistol Right commands (protocol 25) | 22, +244/−27 | stacked |
| `fix/held-keys-stale-modifier` | a modifier whose release the OS swallowed is let go at the next event (macOS Ctrl+F2) | 5, +97/−1 | stacked |
| `feat/input-record-and-replay` + `fix/input-trace-header-and-flush` | `I76_INPUT_RECORD` / `I76_INPUT` tick-exact replay, `I76_MOTION_CSV`; header with build and options, flush per row | 7, +401/−14; 7, +207/−28 | changelog; the second stacked |
| `feat/telemetry-udp-out` + `fix/telemetry-rate-and-time` | UDP telemetry, own format or LFS OutSim/OutGauge; capped at 60 packets/s, monotonic time | 8, +607; 6, +64/−12 | changelog; the second stacked |
| `feat/controls-key-sheet` | `i76 keysheet`: a printable HTML page of the bindings | 6, +218 | changelog |
| `feat/hd-world-textures` + `feat/hd-texture-tool` | the modern look draws a player's own upscaled texture from `user://tex-world*/` (2x/3x/4x/8x); `--export-textures`; `tools/upscale-textures.py` (model 4xTextures_GTAV_rgt-s_dither, CC-BY-4.0). Nothing ships | 17, +384/−82; 6, +157 | changelog |
| `feat/dev-menu` + `feat/dev-menu-mission-picker`, `-landmarks`, `-loadout`, `feat/developer-menu-entry`, `fix/dev-panel-scale`, `fix/dev-panel-2` | a Developer panel off for players (`--dev`, `developer=true`): teleport to landmarks, repair, God mode, store/restore the sim, Scene picker, live Loadout; scales with the window; half size and 85 % opaque | 13, +840/−28 and five stacked rows (+93, +432, +343, +52, +111, +112) | Main.cs; the rest stacked |
| `feat/dev-panel-settings-and-signals` | the panel's Settings column (every `[port]` key without a row, live) and Signals (force feedback, rumble, telemetry, shaker meter) | 13, +577/−52 | stacked |
| `feat/dev-fps-overlay` | `[port] dev_fps`, Show FPS row: the frame rate top left | 8, +161 | stacked |
| `feat/saves-tools-to-dev-panel` | Import Saves and All Scenes move to a main-menu Developer panel; Load Bookmark is the original's sheet plus Delete (*opinion*: keep the menus simple) | 10, +175/−60 | stacked |
| `feat/original-behavior-pad` → `feat/original-recipe-folded` → `fix/marker-marks-the-port-value` → `fix/round3-integration-4` | net: **no Original Recipe pad**; the original-vs-port choices are `[port]` keys with a "Settings without a row" table in docs/playing.md, and on every pad row that offers the original's value the port's values carry a `*` | 24, +454/−19; 25, +335/−354; 11, +63/−50 | stacked |

## 8. The clock (section E, raised last)

| Branch | What it changes | Evidence | Size | On your master today |
|---|---|---|---|---|
| `feat/sim-rate-24` | Simulation Rate offers 24 Hz (85/2048 s), the original's step under a 20 fps cap on Windows NT | kit / Windows capture ~24 steps/s; the bounce tempo gap 28 vs 24 substeps/s | 12, +71/−22 | tools/I76.Server/Program.cs |

## 9. Rounds 2 to 4 by theme (mostly stacked on play)

These came out of five days of family playtests. Each theme names its chain and the net effect, so you can take the
end state rather than replay the steps.

### Graphic Detail and the pads

Chain: `fix/graphic-detail-scrollbar` → `feat/graphics-submenus` → `feat/graphics-pages-3` (plus `fix/esc-aa-row-names`,
`fix/map-edge-row-name`, `feat/graphics-rows-to-config`, `feat/hd-textures-row`).
**Net effect**: the sheet shows the original's rows in the original's order and words (Screen Resolution, Monitor
Brightness, Visibility Range, Terrain Detail, Shadow Detail, Ext. Instrum. Disp., Rear Mirror), with Visibility Range
back as a row (Far = the original's 600 m, Max\* = 20 km, over `draw_distance`), then three pages, Look / Display /
Camera, the same on the Esc pad; one HD Textures row (Off / 4x / 3x / 2x by the folders present); Reset per page; the
Esc pad lists 8x anti-aliasing. Sizes: +43/−32, +488/−226, +502/−180. Your 46924ca (Play Options scrolls) and the
Mouse Options screen change the ground this stands on; expect to redo the row tables.

### HD texture packs (user data, nothing ships)

Chain: `feat/hd-world-textures`, `feat/hd-texture-tool` (section 7) → `fix/hd-pack-glass-and-key` → `perf/hd-pack-load`
→ `fix/hd-cockpit-panels` → `fix/hd-dream-grey` → `fix/hd-cockpit-panels-2` → `feat/upscale-colour-match` →
`fix/hd-menu-port-edits` → `feat/hd-pack-scale` → `fix/hd-pack-3x` → `feat/hd-pack-alpha`.
**Net effect**: `tools/upscale-textures.py` exports and upscales the player's own install into BC7 `.dds` packs at
2x/3x/4x (the cockpit 4x), colour-matched back to the original in Oklab, the window / paint / material analyses
reading the original texels (glass stays glass), the dream greyed as the mission's palette greys it, the cockpit's
panels redrawn at the pack's scale, the HD menu art's port edits applied to the upscale, any whole multiple served.
`--families` runs each cockpit family through a model of its own and an alpha step for keyed art; those extra models
(UltraSharpV2, HDcube3/4, HDcube-Alpha) are **non-commercial licences**, are not downloaded by the tool, and every
model used is listed in the pack's `pack.json` and `ATTRIBUTION.txt`. The default path (GTAV model, CC-BY-4.0) is
clean for redistribution. Sizes: +47/−10, +312/−86, +175/−37, +182/−9, +318/−27, +87/−6, +242/−25, +137/−21, +51/−10,
+185/−26. Measured: T01 build 9.0 s without a pack, 9.8 s with the 2x pack; the pack 588 MB on disk. Since the first
edition (section 10): `feat/font-redraws` / `-3` (the pack's small lettering set again from free fonts),
`feat/hd-pack-8x` (partial 8x packs; no visible gain), `fix/hd-pack-seams` (`i76 tex-seams`, neighbour-aware
upscales) and `tools/menu-maps.py` in `fix/counter-drums-and-hd-maps`; `perf/graphics-defaults` makes the 4x pack the
modern look's first-start choice when it is there.

### Cockpit art in the modern look

| Branch | What it changes | Evidence | Size |
|---|---|---|---|
| `fix/cockpit-gauge-dials-too-dark` | see section 4 | original | 8, +129/−8 |
| `fix/hardpoint-textures` | the weapons panel, wheel and arms cut by their own pixels, not the first frame's outline (housings, plates, hands and pistol were cut away) | playtest; captures | 3, +18/−6 |
| `feat/dymo-weapon-labels` → `fix/dymo-labels-brighter` → `fix/dymo-letter-weight` → `fix/dymo-labels-2` | weapon and special plates lettered in the Impact Label Reversed font (shipped with its free-font licence), embossed, 30 % tighter gap; the 1997 look keeps the painted plates (*opinion*) | ux | +640/−7, +34/−4, +105/−7, +119/−113 |
| `fix/counter-digits-font` | the ammunition counters' digits from Roboto Bold (OFL, embedded) on the drum face instead of upscaled pixels; a reverse-emboss inset built, off | playtest; font match against ten faces | 12, +306/−3 |
| `fix/counter-drums-and-hd-maps` | the counters shaded as drums with the figures cut in (section 10) | ux | 10, +590/−39 |
| `fix/hd-cockpit-panels-2` | the digits on the drums without the white column; mirror frame and glass cut along smooth edges | ux | 9, +318/−27 |
| `feat/smooth-keyed-edges` | keyed textures cut along smooth outlines (signed distance fields from the key, built on workers): signs, fences, plants, the cockpit, mirror, compass. 4,210 keyed textures | local | 14, +745/−61 |

### Modern-look rendering

| Branch | What it changes | Evidence | Size |
|---|---|---|---|
| `feat/modern-lighting-calibration` → `feat/lighting-original-colours` | the modern look's lighting solved per mission against the 1997 look's (exposure, ground and object tints, grey sky light, the frame through the 3D card's curve instead of Filmic, the sky's texture colour per point); `[port] lighting_calibration` on\* / filmic / off | local: LightingCalibrationTests, 9 capture sets | 11, +524/−2; 15, +346/−83 |
| `fix/vehicle-lights-strength` | lamps weaker at night, well down by day (`LampStrength.cs`) | local | 9, +107/−15 |
| `fix/palette-grade-no-dither` | Palette Grade moves colours smoothly, no dither | local | 7, +156/−11 |
| `fix/paved-roads-sharp` | asphalt grain near the camera; 16x anisotropy | local | 8, +116/−4 |
| `fix/smoke-depth` + `fix/smoke-depth-metal` | the half-size smoke layer takes the nearest depth under each pixel; with MSAA it reads the multisampled depth itself (#95, a Mac under Metal) | issue #95; before/after on an M-series Mac | 4, +116/−6; 3, +77/−23 |
| `fix/impact-marks-subtler` | ground strikes a third fainter (*opinion*); `[port] effects_size / effects_brightness / effects_opacity` 25–150 % | ux | 16, +203/−26 |
| `feat/vegetation-density` | `[port] vegetation_density` thins the scattered plants to a subset, 60 % by default (*opinion*) | 914 / 559 / 284 shrubs at 100 / 60 / 30 % | 8, +76/−2 |
| `feat/brush-impacts` | the brush answers rounds, cars, blasts and fire; `[port] brush_impacts` | local | cherry-picked |
| `fix/wreck-smoke-range` | scenery wrecks burn and smoke out to 600 m, as the 1997 look | original (the 1997 look's reach) | 6, +37/−10 |
| `feat/death-variety` → `feat/death-variety-2` | car deaths by cause in two stages: the failed part's event, a 200–500 ms beat, exactly one tank blast, a cause-specific drawn throw; the original's 10 s effect roll (~25 x1_cars1) drawn as flares; the catastrophic award (1 in 8 + specials) alone gets more; `[port] death_variety`; snapshot 25, protocol 27 (a per-death seed shared online). **Trade-off**: the throw is drawn only, the simulated wreck still takes the original's kick, so collisions can sit metres from the picture. Under playtest, not final | design doc and 88 s comparison clips | cherry-picked; 12, +581/−140 |

### Bass shaker and audio

Chain: `feat/tactile-lfe` → `feat/audio-output-modes` → `fix/lfe-level-stronger` → `feat/lfe-2` → `feat/lfe-3` → `feat/lfe-4`.
**Net effect**: a synthesised shaker signal of the port's own (engine, road by surface and speed with seeded variation,
a per-wheel knock at road and bridge edges, shots, damage-scaled hits with a 1.47 s cabin ring on the original's
cockpit-jolt decay, explosions out to 2 km), 15–90 Hz under a limiter, each source in its own band under the game's
own sounds (`docs/lfe-bands.md`, `docs/lfe-surfaces.md`); Audio Control's Output (Stereo, Phones, 5.1, 7.1) and LFE
Level (0 = off, the default); `[port] lfe_*` source levels, `tactile_eq` (an hz:dB correction curve), `crossover`,
`game_eq` / `movie_eq` (the movies' top octave shelved so cutscenes are not brighter than the game). `tools/TactileRender`
renders the synth to WAV and makes measurement sweeps; `eq_from_measurement.py` turns a force reading into the curve.
Sizes: +496/−6, +432/−46, +52/−9, +480/−72, +840/−31 (+697/−8), +1436/−79. New files: `TactileSynth.cs`,
`BassShaker.cs`, `WheelCrossings.cs`, `SoundEq.cs`, `AudioLevels.cs`.

### Network

| Branch | What it changes | Evidence | Size |
|---|---|---|---|
| `feat/net-esc-self-destruct` + `fix/net-esc-labels` | Self Destruct (new car) on the network Esc menu where the original's Replay was; Leave Match / Exit I76 wording | spec | 7, +120/−21; 7, +26/−10 |
| `fix/scoreboard-host-guards` | a double-click on the locked board asks before booting; `/ban` refuses unknown names | local | 8, +89/−12 |
| `fix/coop-damage-remainder` | Co-op Damage below x1 carries the fraction per car and channel (flame still burns) | local | 11, +85/−15 |
| `feat/build-id-in-lobby` | the forms show each game's build id; a joiner on another build is let in with a notice (protocol 26); `BuildId.cs` with `export-subst` | ux | 19, +233/−24 |
| `fix/net-melee-music-tracks` | an arena asking for a shell track starts on the next mission track; Nitro passes over its menu and credits tracks | WRLD tracks read from every mission | 5, +58/−14 |
| `feat/host-form-master` | the fork's host form over your 56373bd, with your labelled rows folded in (section 10; it replaces `feat/host-form-redesign` + `fix/host-form-polish`, dropped at the reconcile) | ux | 8, +592/−614 |
| `fix/melee-list-arenas-only` | the melee lists leave out co-op packs' byte-identical copies of story missions | original lists every addon level | 5, +65/−2 |

### Shell, bookmarks, movies

| Branch | What it changes | Evidence | Size |
|---|---|---|---|
| `fix/delete-bookmark-from-parts-form-1997` + `fix/shell-layout-2` | Save Bookmark has Delete too; Delete on the bottom button line left of Load/Save (the second's host-form scrap move went with 56373bd) | local | 3, +81/−38; 5, +38/−31 |
| `fix/scene-names` | trip missions listed as "Scene N: name" by what happens in them; the exe's unshown titles no longer read | original shows "Scene N." only | 9, +67/−57 |
| `fix/shell-music-no-title-tracks` | the 3D garage keeps the menu track as CONFIGURE CHASSIS does; the boombox only with `garage_radio=on` | spec | 8, +77/−5 |
| `feat/movie-while-loading` + `fix/briefing-overlap-title` | the briefing plays while the mission builds on a thread (nodes outside the tree; frames capped at 240 during the build), the loading card after the movie; `[port] briefing_while_loading` on\* (modern look only; the 1997 look, retries, `--skip-movies`, co-op and the editor keep the original order); saves 5–8 s per briefed mission. HD movies: `user://movies/<stem>.ogv` (Theora + Vorbis) replaces the SMK | measured t01/t07/t08/m01 | 14, +309/−7; 6, +31/−9 |
| `feat/build-version-rules` | `user://build-rules.cfg`: `turret_slew` and `ai_tactics` follow the 1998 GOG build or the 1997 CD build (both measured), 1998 by default | original, two builds | 13, +331/−12 |
| `fix/known-test-failures` | the four known Sim failures answered (T14 checkpoint, co-op side ram, T01Follow at 30 Hz, Nitro caltrop) | local | 7, +52/−12 |

### Map edge: a chain whose net effect is zero

`feat/map-edge-treatment` → `feat/map-edge-hide` → `fix/map-edge-on-off` → `feat/map-edge-thin-barriers` →
`fix/map-edge-roads-and-1997` → `fix/map-edge-placed-objects` → `fix/map-edge-row-name` → `fix/map-edge-roadside` →
`fix/map-edge-slopes` → **`remove/map-edge-cliffs`** (24 files, +47/−1340). After a week of trying to hide the boundary
walls the fork's author decided the walls and the slopes up to them are the maps' intended shape and removed the whole option.
Nothing to pull; your 798c651 (the ground runs on past the edge) is the better answer. `EdgeWalls.cs` measured the
114 stock and Nitro maps (terrace steps 5–40 m, plateaus 400 m deep) if that number is useful.

## 10. New since the first edition: manifest sections S to AA (2026-11-05)

All eleven are *stacked*: built after the reconcile, on files the stack added (the Graphic Detail pages, the Developer
panel, the HD pack tool, the cockpit panels, the briefing overlap), so each needs the branches it stands on. Size is
the branch's own change as merged into `play`. **Default** is the fork's suggestion: **pull** (worth having as it is),
**option** (take the mechanism if you want the feature; the default value is taste), **leave** (the fork's own, or
not worth your review).

| § | Branch | What it changes | Evidence | Size | Default |
|---|---|---|---|---|---|
| S | `perf/frame-cost-survey` | the frame-cost survey: every modern-look feature toggled one at a time against a full-detail baseline in the T01 start at 3440x1440 and 1920x1080, three runs each, medians and the noise stated (`docs/perf/costs.md`), and the ranked plan it leads to (`docs/perf/plan.md`, with a frame-time budget for new features); FrameProfiler's timed window, per-frame CSV, screenshot and VRAM series; `I76_SMOOTH_KEY=0`, `I76_MIRROR_RATE`, `I76_MIRROR_SSAO` to cost single features; the `tools/frame-cost/` harness | local: GTX 1080 Ti, the HD table on a GTX 1650 Ti too | 6 commits; 15, +1352/−6 | **pull** (nothing changes without the hooks; the numbers are worth having) |
| S | `perf/graphics-defaults` | the plan's items: Ambient Occlusion a Graphic Detail row of its own, Off (it was 29 % of a full-detail frame at 3440x1440, and Shadow Detail High turned it on unseen); Shadow Detail Low two splits to 600 m (High four to 1500 m, as before); first-start Anti-Aliasing 2x; the modern mirror drawn every second frame as the original's, through its own environment without SSAO, and `mirror_range=original`'s 100 m fogged from 55 m instead of cut off; the terrain shader in three distance tiers (the mirror and the shadow passes on the far one); the distant ring's main/rim depth split re-ported onto your outside-ground shader (`I76_HORIZON_SPLIT=0` draws yours); HD Textures Off / 4x, 4x the first start's when the pack is there and the GPU has 4 GB; a help line under the eight costliest rows | local: first start, T01 cockpit, GTX 1080 Ti at 3440x1440: GPU 12.6 → 8.1 ms (chase view 18.1 → 12.7); the ring split alone 2.2 ms (1.2 at 1080p) | 12 commits; 36, +823/−198 | **pull** the mechanisms (AO row, mirror cadence, tiers, ring split); the first-start values (2x, Low, the 4x pack) are *opinion* |
| T | `feat/font-redraws`, then `feat/font-redraws-3` | `tools/text-redraws.py` and per-texture specs: the small painted lettering of a player's own HD pack set again from matched free fonts at the pack's resolution (the dials of the four dash families, the damage panel and MR.DAMAGE plate, the Piranha's plate and badge in every dent stage, the police doors in three liveries, seven cars' tails, the school bus's rear, town-sign and billboard small print), on the CPU in seconds; the label-tape faces for the weapon plates listed with their licences only | local: `tools/test_text_redraws.py` 9 passed; checked in play from the views named in the commits | 5 commits; 55, +6210/−3, and 5 commits; 31, +1301/−145 | **option** (only with the HD pack tool; adds 11 font families under `tools/text-redraws/fonts/`, SIL OFL bar Permanent Marker, Apache 2.0; the tool's input, not shipped in the game) |
| U | `feat/quiet-test-window` | `I76_QUIET_WINDOW`: test and capture runs start in a window behind the user's other windows without taking the foreground, keyboard or pointer (`=min` minimised); the game reads `--resolution` from the process's own command line, since Godot keeps it out of `OS.GetCmdlineArgs`; the harnesses and `tools/shot.sh` default to it | local | 3 commits; 9, +243/−6 | **pull** (a dev hook; its `play-setup.md` hunk is the fork's and comes out) |
| V | `perf/load-time` | the load-time survey (`docs/perf/loadtime.md`, `loadtime-plan.md`, `tools/load-time/`, `I76_LOAD_PROFILE`) and its changes: the mission's world built on a thread from the scene's start; the loading card first for the retail game's time, then the briefing, then the card until ready (`[port] briefing_while_loading=card_first`; `movie_first` is the previous order); the cars' parts, the cockpit's art and the lighting calibration's solve off the main thread; the modern effects made under the card; the threaded build's frame cap 240 → 1000 fps; `menu_warmup` built and left off (no saving); the debug builds' main-thread check lets the build thread through | original: the retail card on the emulated P166 (T01 up 5.0 s, 5.7 s to the briefing; T05 6.5 / 7.4 s). Local: T01 without a briefing 11.89 → 11.27 s, with the briefing skipped after 3 s 17.85 → 16.66 s; T01's terrain chunks 4.6 → 1.3 s at the new cap | 18 commits; 28, +2092/−174 | **pull** (card_first is the original's order) |
| W | `feat/1997-parity` | `docs/1997-parity.md`: the 1997 look side by side with i76-everywhere on one machine (frame pacing, key-to-screen latency from a 180 Hz capture, the tick), ten differences ranked. Early Tick (`[port] early_tick`, on): a change of the driving or weapon keys runs the next tick at once, borrowed from the following one, every tick still one step (off online, under Modern Physics, in replays); `aa_1997` (on): the 1997 look takes the Anti-Aliasing row's MSAA (not the Software renderer). Evenly placed muzzle flashes tried and withdrawn | local: throttle from rest, key to first changed frame at 24 Hz, 50 → 22 ms median (i76-everywhere 17), n 9 / 10; 179 fps at 1080p with and without AA | 4 commits; 14, +416/−7 | **option** (the original waits for the tick; both defaults on are *opinion*) |
| X | `feat/host-form-master` | the fork's host form put back over 56373bd (the original's art, the options box in its paper, the arena scrap with its size, rule tips, the list's scroll bar) with your work folded in: a labelled rules table (Sim Rate, Unlimited Turrets, Ignition Lock, Handicaps, Password, Chase Crosshair) where the car's front view was, full-width co-op rows, every hotspot id yours, HostFormLayout and its tests on the fork's layout; the Nitro form unchanged | ux | 3 commits; 8, +592/−614 | **leave** (yours is in master; this one is taste. If you prefer it, it is the whole form in one branch) |
| Y | `feat/hd-pack-8x` | the loader serves each texture from the largest pack size that has it (a partial 8x over the 4x); the HD Textures row lists a partial size as "8x (cockpit & cars)\*"; a first start never picks 8x; `upscale-textures.py --scale 8` with the GPU and CPU steps apart and `--covers`; the key bleed's reach follows the scale and runs 18x faster, bit-identical (`--self-check`); text-redraws at any scale | local: an A/B at 3440x1440 and 1920x1080 showed no difference from the 4x, for +0.54–0.61 GB of video memory and +2.0–2.7 s of load | 9 commits; 20, +791/−65 | **leave** (the partial-pack loader and the faster bleed could be picked out if you take the pack tool) |
| Z | `fix/hd-pack-seams` | `i76 tex-seams` finds every edge where two textures meet or one wraps (27,952 segments: every .vcf and .sdf, the road strips, the horizon panels, ground and sky); `upscale-textures.py --neighbours / --context / --then` upscales those textures inside a canvas of what meets them in the game | local: 115 textures re-upscaled, border-seam score 2.26 → 0.52; the worst remaining seams are UV islands inside one texture | 4 commits; 7, +609/−7 | **option** (with the pack tool) |
| AA | `fix/counter-drums-and-hd-maps` | the modern cockpit's ammunition counters shaded as drums with the figures cut in (`I76_COUNTER_DRUM` picks flat / drum / inset / raised / gaps; the 1997 look keeps the original strip); `tools/menu-maps.py` makes the HD menu art's mission maps (the M screen's 91 maps at 4x, the pen writing lightly cleaned), so the map screen no longer falls back to the 640x480 originals | ux | 4 commits; 10, +590/−39 | **option** (the drums are *opinion*; the maps go with the pack tool) |

## 11. Pull-request readiness

Taken on 2026-11-05 for each of the 247 manifest branches with `git merge-base <branch> origin/master` (is it cut from
your master, or does it carry `play`'s merges?) and `git merge-tree --write-tree --name-only origin/master <branch>`
(does it merge onto 8edea2a, and which files conflict?). `origin/master` is 8edea2a as of the fork's last fetch.

| Class | Branches | What it means |
|---|---|---|
| Cut from your master, clean | **92** | cut from 0.11.0 (5192e29) or 32988ae; nothing but the CHANGELOG to settle: 13 with no conflict at all, 79 conflicting in CHANGELOG.md only. Can be offered as they stand |
| Cut from your master, conflicting | **35** | cut from 32988ae, but a file besides CHANGELOG.md conflicts with your 39 commits since; each needs a rebase by hand first (list below) |
| Stacked | **120** | carry `play`'s merges (all of section 10, most of section 9); offered after what they stack on, or a chain as one change. Includes the nine merge-glue rows (`fix/round3-integration*`, `fix/upstream-rebuild-integration`), never offered |

**The CHANGELOG, once for all.** Every branch with a bullet put it under `## Unreleased`, which your 6e5f8fc removed.
The branches cut from 32988ae conflict there; the ones cut from 0.11.0 merge "cleanly" only by putting an Unreleased
section back. Either way the bullet comes out when a branch is offered: each pull request is a `pr/<branch>` cut from
your current master with the branch's commits cherry-picked and CHANGELOG.md left as yours (as #150 was), and the
bullet's wording goes in the description for your release notes. The fork's own files (`docs/play-*.md`,
`tools/play-branches.txt`, `tools/sync-play.ps1`, this page) are never in a pull request.

**Clean with no conflict at all (13):** `fix/wheel-suspension-prime`, `fix/script-camera-cuts`,
`fix/mission-music-wrap`, `fix/smoke-sky-flicker`, `fix/taurus-load-cornering`, `fix/1997-tracers`,
`fix/bookmark-cancel-returns`, `fix/burst-rearm-after-batched-shot`, `fix/zero-length-hit-zone`,
`fix/start-engine-pulse`, `fix/movie-skip-deviation-row`, `fix/network-deviation-rows`, `feat/original-save-reader`.
The other 79 cut from your master need only the CHANGELOG step; the section tables mark them `changelog`.

**Cut from your master, conflicting (35)**, by what they hit:

- the docs tables only (`docs/deviations.md`, `docs/dev-hooks.md`): `feat/regen-pad-hold-readout`,
  `fix/modern-target-health-bar`, `fix/overview-zoom-per-tick`, `feat/ghost-cockpit-toggle`,
  `feat/voices-overlay-folder`, `feat/extra-music-folder`;
- one code file: `fix/ai-turn-speed-at-spawn` (WireTests.cs, the version pin), `fix/sky-scroll-counts-mirror-pass`
  (Main.cs), `fix/rough-terrain-damage-surface` (JumpLinks.cs, deleted by your f8db1bb), `fix/silent-dropper-hit-sounds`
  (Combat.cs), `fix/death-camera-orbit-rate` (ChaseCamera.cs), `fix/network-heli-lift-at-init` (MissionSetup.cs),
  `fix/salvage-specials-amount` (Salvage.cs), `perf/interop-garbage` (SmokeLayer.cs), `feat/dev-menu` (Main.cs),
  `feat/sim-rate-24` (tools/I76.Server/Program.cs);
- your Play Options list, Mouse Options and Graphic Detail (Overlays.cs, Shell.cs, EscapeMenu.cs, CursorPolicy.cs,
  ChaseCamera.cs): `fix/mouse-in-window-menus`, `feat/head-tracking-opentrack`, `feat/wheel-force-feedback-windows`,
  `feat/rumble-strength-and-events`, `feat/bot-skill-from-difficulty`, `feat/field-of-view-row`, `feat/resolution-row`,
  `feat/hood-matte-option`, `feat/render-scale-row`, `fix/1997-look-draw-distance-default`, `feat/map-edge-treatment`;
- your host form and protocol 22 (MeleeScreen.cs, NetForms.cs, MainNet.cs, Protocol.cs, Discovery.cs, MeleeHost.cs, the
  server's Program.cs, WireTests): `feat/net-scoreboard-sticky-with-kick`, `feat/net-return-to-server-list`,
  `feat/host-rule-ignition-stays-on`, `feat/coop-enemy-scaling`, `feat/coop-damage-factor`,
  `feat/coop-joiner-matches-speed`, `feat/net-handicaps`, `feat/net-auto-handicap`.

On `play` all of these were resolved inside the reconcile merge (`docs/play-tracker.md`, "Integration notes — pass 19
reconcile", lists how each file went); a rebased branch redoes the same resolution on its own.

**Already offered**: #150, `pr/smoke-depth` (= `fix/smoke-depth` + `fix/smoke-depth-metal`, cut from 32988ae on
2026-11-03). It now conflicts with 8edea2a in CHANGELOG.md and `game/Scripts/Sim/SmokeLayer.cs` and needs a rebase
before it can merge.

### A first batch of ten

Section A of the manifest (bugs reported to you) in manifest order, all cut from your master and needing only the
CHANGELOG step. The fork's tracker paces them one or two open at a time, the next in the same area waiting for your
reply. Titles, descriptions and the commands are in [upstream-handoff-first-prs.md](upstream-handoff-first-prs.md).

| # | Branch | Issue | On 8edea2a | Pitch |
|---|---|---|---|---|
| 1 | `fix/wheel-suspension-prime` | #141 | clean | every placed car (the mission's, a melee's, a respawn, a co-op seat) starts with its wheels on the ground under them, as the original's entity set-up does; your 01228e7 settles the player's car only |
| 2 | `fix/script-camera-cuts` | #140 | clean | a cut between two placements of the same camera op is a cut, not an 11–36 m blended swing (t01 1, t04 6 → 0); the garage half of #140 is not this |
| 3 | `fix/script-camera-arrived-flag` | – | changelog | every placing path-camera call clears camIsArrived, as the original's do; no stock mission changes, a custom script can |
| 4 | `fix/mission-music-wrap` | #128 | clean | the default sequence wraps 14 → 2 as the original's CD requests do, instead of playing on through the disc's tracks 15–17 |
| 5 | `fix/smoke-sky-flicker` | – | clean | the 1997 look's damage smoke spawns from the sim's pose, is drawn between ticks, and no puff nearer than 1 m is drawn, as the original: cockpit sky flicker 26.3 % → 0.31 % of pixels |
| 6 | `fix/middle-wheel-suspension` | #124 | changelog | six-wheelers' middle pair rides its own suspension as the original steps it; the tank part of #124 is its body pose (answered, not changed) |
| 7 | `fix/predicted-shots-of-jammable-weapon` | – | changelog | online, a weapon that can jam is drawn from the server's shots: 62 % of such predicted shots were being taken back |
| 8 | `fix/own-shots-counter-at-respawn` | – | changelog | the `I76_NET_DEBUG` taken-back count no longer jumps at every respawn (debug only; can ride with 7) |
| 9 | `fix/net-ping-low-percentile` | – | changelog | the shown ping is the lowest of the last ten samples: a 60 ms link at 10 % loss read 155–624 ms; no wire change |
| 10 | `fix/escape-menu-right-click-reconfigure` | #106 | changelog | right-click rebinding works in a network game's Esc menu; the hidden fly camera took the press |

Next in line: `fix/first-garage-load-prewarm` (#143), `fix/first-explosion-stutter` (#116), `fix/gate-fence-grounding`
(#137), `fix/1997-effect-sprites-upright` (#105), all `changelog`; then `feat/regen-pad-hold-readout` (#102) and
`fix/mouse-in-window-menus` (#147) after their rebase. `fix/cockpit-art-bilinear` is superseded (section 3) and not
offered.

## 12. Things to weigh before taking

- **Defaults that are the fork's author's taste**, marked *opinion* above: Hood Gloss 0, `vegetation_density` 60,
  impact marks a third fainter, the dymo label font, bold garage tags, `sky_drift=steady`, Import Saves and All Scenes
  in the Developer panel; since the first edition also first-start Anti-Aliasing 2x, Shadow Detail Low and the 4x pack
  (`perf/graphics-defaults`), `early_tick` and `aa_1997` on (`feat/1997-parity`), the counter drums. The mechanisms
  stand on their own; pick your defaults.
- **Fonts in the repository**: in the game, `game/Fonts/ImpactLabel/Impact Label Reversed.ttf` (Tension Type free-font
  licence, redistributed unmodified) and `game/Fonts/Roboto/Roboto-Bold.ttf` (SIL OFL 1.1), both licences in
  THIRD-PARTY-NOTICES and copied by `tools/release.sh`; if you would rather not ship fonts, the dymo labels and the
  counter digits are the rows to leave. New with section T: eleven families under `tools/text-redraws/fonts/` (Anton,
  Archivo Narrow, Bitter, Cinzel, Comfortaa, D-DIN Condensed, League Gothic, Lobster, Mr Dafoe, Russo One under the SIL
  OFL with their OFL.txt; Permanent Marker under Apache 2.0), input to the pack tool only, never in a release. The
  label-tape candidates that are free for personal use only are not in the repository (the folder is ignored but for a
  README table of authors, licences and sources).
- **Non-commercial AI models** in the pack tool's optional routes (`--families`, and the 8x and `--then` routes of
  sections Y and Z, which name the HDCube models). The tool never downloads them, the default path is
  4xTextures_GTAV (CC-BY-4.0), and every model used is listed in the pack's `pack.json` and `ATTRIBUTION.txt`.
- **Formats**: snapshot 15 → 25 (turn speed, user radar, teleport ground, the three co-op options, the ignition rule,
  handicap, weapon files in a car's make-up, auto handicap, co-op damage carry, death seed); protocol 22 → 28 (the
  fork's ignition, handicaps, handicap mode, pistol keys, build id and death variety on 21 made 27; your 22's Chase
  Crosshair and roster names on top made 28; a `play` client and an upstream server refuse each other by version);
  `shell.cfg` 11 → 13. Taking a subset means numbering again; the fork's `WireTests` pin both.
- **The mission start changes order** with `perf/load-time` (the card first for the retail time, the build on a
  thread from the scene's start); `movie_first` keeps the previous order, and a start without a briefing builds on the
  main thread as before.
- **`feat/death-variety-2`** is under playtest; the drawn-throw versus simulated-wreck mismatch is a known open item.
- **Dev-only hooks** (`I76_*`, now about 210 lines added to docs/dev-hooks.md) ride along with many rows; harmless, but
  you may want fewer.
- **One machine's numbers**: the frame-cost and load-time tables are from one Windows desktop (GTX 1080 Ti) with the
  noise stated; the harnesses are in the repository to rerun them elsewhere.
- **`fix/cockpit-art-bilinear`** is superseded by `fix/cockpit-gauge-dials-too-dark` (section 3) and not offered.

## 13. Repository and project changes outside the code

- `.gitattributes`: `src/I76.Net/BuildId.cs export-subst` (the build id in a `git archive`).
- `.gitignore`: `!tools/deck/*.vdf`; `tools/text-redraws/fonts/candidates/*` ignored but for its README.
- `game/project.godot`: `textures/default_filters/anisotropic_filtering_level=4`; shader globals `i76_hood_gloss`,
  `i76_fx_size`, `i76_fx_bright`, `i76_fx_alpha`, `i76_daylight`, `i76_veg_field`, `i76_veg_field_rect`,
  `i76_ground_tint`, `i76_terrain_tiers`, `i76_object_tint`, `i76_object_sky`, `i76_prelit_gain`, `i76_glass_pane`.
  (`driver/enable_input.macos=false` is yours now.)
- `AGENTS.md`: 24 Hz added to the tick list, and a "play fork" paragraph that is the fork's only.
- `tools/release.sh`, `tools/release/THIRD-PARTY-NOTICES.txt`: the two game font licences.
- New docs: `docs/lfe-bands.md`, `docs/lfe-surfaces.md`, `docs/perf/costs.md`, `docs/perf/plan.md`,
  `docs/perf/loadtime.md`, `docs/perf/loadtime-plan.md`, `docs/1997-parity.md`; large additions to `docs/playing.md`
  (the "Settings without a row" table), `docs/multiplayer.md`, `docs/joystick.md`, `docs/dev-hooks.md`,
  `docs/deviations.md` (about 115 lines new or reworded).
- The fork's own, never offered: `docs/play-setup.md`, `docs/play-tracker.md`, `docs/upstream-handoff.md` (this page),
  `docs/upstream-handoff-first-prs.md`, `tools/play-branches.txt`, `tools/sync-play.ps1`, the AGENTS.md paragraph.
- Tools: `tools/TactileRender/` (the shaker renderer and sweeps), `tools/deck/`, `tools/upscale-textures.py`,
  `tools/text-redraws.py` with `tools/text-redraws/` (specs, generators, fonts), `tools/menu-maps.py`,
  `tools/hd-8x-cars.txt`, `tools/frame-cost/`, `tools/load-time/`, `i76 keysheet`, `i76 tex-seams`,
  `tools/I76.Cli/MeleeSim.cs` (the bot melee the handicap numbers come from).

## 14. Issues we think you can close or answer

From the fork's sweeps of the tracker (2026-11-02; every issue named under "Fixed by rows above" is still open on
2026-11-05):

- **Fixed upstream already**: #132 #120 #131 #126 #115 #71 #119 #123 #69 #127 #19; since then #146 (e1926cc).
- **The original's own behaviour** (answer with the spec, no fix): #112 #84 #85 #57 #114 (tanker) #113 (bus texture)
  #101 (pad surface, confirmed in the retail game) #102 (regen hold, confirmed; `feat/regen-pad-hold-readout` shows it)
  #72/#121 (helicopter reverse spin) #111 (far creepers) #137's placements (the fence centring part is fixed above).
- **#136**: the overpass road servers need TLS 1.3, which Windows 10 cannot do; no fix here.
- **#133** AMD/Vulkan 1997 look: not reproducible on the fork's machines; D3D12 as the AMD default is the suggestion.
- **Need a repro**: #130 #118 #99.
- **Fixed by rows above**: #95 (smoke depth; offered as #150), #102, #105, #106, #108, #116, #117, #122, #124, #128,
  #137 (part), #140 (the cut-scene part), #141 (the wider version), #143, #147.

## 15. Test state

At the reconcile (`play` ff27ef1, 2026-11-05), on one Windows desktop with `I76_INSTALL` set, against `play` just
before the merge (f8022fc) built and run the same way: **Sim** 1154 passed / 29 failed, the same 29 names as before
the merge; **Game** 390 / 1 (RoadPieceTypeTests.EveryStockPieceIsStillPainted: your new test counts 392 stock pieces,
and this install's addon folder holds co-op pack levels); **Net** 454 / 18 against 447 / 20 before (gone: two
BotJumpTests, reverted upstream, and two AnetPunchTests, fixed upstream in 4e9a234; new: one co-op test that passes
alone and the prediction test's 50 ms laggy link, which fails alone before the merge too); **Formats** 1011 / 4, the
desktop's path and parallel-build failures, as before. Without `I76_INSTALL`: Game 363 / 0, Sim 1187 / 0, Net 472 / 0.

The eleven branches of section 10 were each checked in their own commits (unit tests where the change has them,
captures and measured runs otherwise; see the table); the full suites have not been run again on the tip 12ec3e5.
The first ten pull requests' own tests were run on 2026-11-05 with each branch applied onto 8edea2a without its
CHANGELOG hunk (counts in the companion page). The fork's earlier comparison on a second Windows machine (the
2026-11-03 rebuild) found no failure added against `master`'s own run there.

## 16. Measured facts worth having even if you take no code

- Rounds never inherit the carrier's velocity (n 114, excess median −0.02 m/s); the original's turrets hit from a
  moving carrier (37/37 at 21–23 m/s; T11 helicopters 85–96 %).
- The 1997 CD build and the 1998 GOG build differ in turret slew (1.5 vs 1.7 rad/s, health scaling) and the AI tactic
  chooser (a stub vs the weighted draw); every other rule checked is the same in both, the T05 jump included.
- T05's cliff: a drop over 35 m kills in the original too; no bug. The jump needs 45–47 m/s at the lip.
- The AI turn speed is 12.52198 m/s for 13 cars with zero spread; the dial needles are palette entry 160, pivot 0.69.
- The original draws the sky scroll twice a frame with the mirror in full mode, and redraws the mirror every second
  frame.
- The retail game's loading card, emulated P166: T01 5.0 s on screen, 5.7 s from its first frame to the briefing's;
  T05 6.5 s and 7.4 s.
- The modern look's costliest feature is SSAO, about 29 % of a full-detail frame at 3440x1440; an 8x pack of the
  cockpit and cars looks the same as the 4x at 3440x1440 and costs +0.54–0.61 GB of video memory.
- A threaded build waits a main-loop frame for every renderer call that returns a value: at a 240 fps cap T01's terrain
  chunks took 4.6 s, at 1000 fps 1.3 s.
- The 1997 look's key-to-screen latency at 24 Hz is up to a tick of waiting: 50 ms median, 22 ms with Early Tick.

## How this page is kept

It lives on the integration branch next to `docs/play-tracker.md` (the per-branch evidence and order) and
`tools/play-branches.txt` (the order itself). When a manifest section is added its branches get a row here; when you
land or redo something, its row moves to section 2; when a branch is offered, its PR number goes beside it. Section
11's counts are the two `git` commands named there, run again against your current `master`.
