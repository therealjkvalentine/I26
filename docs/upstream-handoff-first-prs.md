# The first ten pull requests to upstream

*Companion to [upstream-handoff.md](upstream-handoff.md), section 11. Written 2026-11-05 against upstream `master`
8edea2a; nothing below has been pushed or opened. Section A of the manifest (bugs reported upstream), in manifest
order. The fork's rule is one or two open at a time, the next in the same area waiting for the lead's reply.*

## How each one is offered

Each pull request is a `pr/<name>` branch cut from upstream's current `master`, carrying the topic branch's own
commits with the CHANGELOG hunk left out (upstream writes release notes at release time, 6e5f8fc). The topic branch
itself is not touched. Remotes as in [play-setup.md](play-setup.md): `origin` is upstream, `fork` is the public fork.
Replace `<fork-owner>` with the fork's GitHub owner.

Define this once in a Git Bash shell at the root of a clean worktree of the repository:

```sh
# offer <topic-branch>: make pr/<name> from origin/master with the branch's commits, CHANGELOG.md left as upstream's
offer() {
  b=$1; n=${b#*/}; mb=$(git merge-base "$b" origin/master)
  git switch -c "pr/$n" origin/master || return 1
  for c in $(git rev-list --reverse --no-merges "$mb..$b"); do
    git cherry-pick --no-commit "$c"
    git checkout origin/master -- CHANGELOG.md           # drops the bullet, settles the CHANGELOG conflict
    if [ -n "$(git diff --name-only --diff-filter=U)" ]; then
      echo "conflict outside CHANGELOG.md in $c; resolve by hand"; return 1
    fi
    git cherry-pick --quit 2>/dev/null
    git diff --cached --quiet || git commit --quiet -C "$c"
  done
  git log --oneline origin/master..HEAD
}
git fetch origin
```

Every one of the ten applies onto 8edea2a with no conflict outside CHANGELOG.md, and its tests below pass there (each
branch's diff without the CHANGELOG hunk applied onto 8edea2a, then `dotnet test` of the named project with the
filter, no game install; the test project builds the game's scripts too). If `origin/master` has moved, run the test
line again on the `pr/` branch before pushing.

Each body follows upstream's own PR style (as #150): what the original does and what was wrong here, then what
changes, the issue it fixes, and a `Checked:` paragraph with the commands and counts.

---

## 1. `fix/wheel-suspension-prime` (#141)

**Title:** A placed car's wheels start from the ground under them, so a car at rest no longer bobs as a mission starts (#141)

```sh
offer fix/wheel-suspension-prime
dotnet test tests/I76.Sim.Tests --filter SuspensionStart
git push fork pr/wheel-suspension-prime
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/wheel-suspension-prime \
  --title "A placed car's wheels start from the ground under them, so a car at rest no longer bobs as a mission starts (#141)" \
  --body-file - <<'EOF'
The original's wheel record starts with the ground and the last target at 64, but its entity set-up overwrites both with the ground under each wheel (and the rate with 0) when the car is placed, before the first frame's physics: measured in T01, T08 and T16, a stationary car's travel is exactly 0 from the first substep. Here the suspension's history (each wheel's ground, last target and rate) was left at 0 when a car was built, so a car's first substep took the whole terrain height under it as one bump: five times the height as the filter's input, the springs at their limits for 9 to 21 substeps while the car stood still. 01228e7 settles the single-player car behind the loading card; the mission's other cars, a melee's, a respawn and a co-op seat still dropped and bobbed.

VehicleStep.PrimeWheels does what the original does, with the ground the suspension step itself queries at each wheel, middle pair included. Every path that places a fresh car calls it once the pose is set: the mission's own cars and a co-op game's other seats (MissionSetup), a melee's cars at their spawn points (a new player, the start, the opponents, a network respawn and a network regen's renewed car), a co-op respawn beside its teammate and a co-op seat's change of car. A single player's regen keeps the car's history, a save or a snapshot restores its own, and a network client takes its car's wheels from the server as before.

Fixes #141.

Checked: dotnet test tests/I76.Sim.Tests --filter SuspensionStart: 9 passed (SuspensionStartTests, new), on master 8edea2a. Against the original under the patch kit, a placed car's travel matches within 2.5 cm (n = 2).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 2. `fix/script-camera-cuts` (#140)

**Title:** A cut-scene's cut to another placement of the same camera op is a cut, not a move (#140)

```sh
offer fix/script-camera-cuts
dotnet test tests/I76.Sim.Tests --filter ScriptCamera
git push fork pr/script-camera-cuts
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/script-camera-cuts \
  --title "A cut-scene's cut to another placement of the same camera op is a cut, not a move (#140)" \
  --body-file - <<'EOF'
The script camera is drawn between its last two ticks' placements, and only a change of op reset the previous placement. A cut between two placements of the same op (camObjObj to camObjObj as the camera turns from one speaker to the other, or a new path for camTransObj) kept the old shot as the previous one, so the frame drawn between the two ticks showed the camera halfway across. Headless over the first 90 s of each Trip mission at 20 Hz, t01 has one such cut and t04 six, blended across 11 to 36 m; no other mission has one.

A new placement is now a cut when the op, its numbers, its objects or its path differ; the same op re-issued with the same arguments, as a travelling shot is every frame, still glides. Over the same runs the cuts go (t01 1 -> 0, t04 6 -> 0) and every other moving frame keeps its blend. This is the cut-scene half of #140; the 3D garage's entry is not touched here.

Fixes the cut-scene part of #140.

Checked: dotnet test tests/I76.Sim.Tests --filter ScriptCamera: 4 passed (one new), on master 8edea2a. The headless counts above; a live capture of the t01 cut in the 1997 look.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 3. `fix/script-camera-arrived-flag`

**Title:** A path camera call that places the camera clears camIsArrived, as the original's does

```sh
offer fix/script-camera-arrived-flag
dotnet test tests/I76.Sim.Tests --filter ScriptCamera
git push fork pr/script-camera-arrived-flag
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/script-camera-arrived-flag \
  --title "A path camera call that places the camera clears camIsArrived, as the original's does" \
  --body-file - <<'EOF'
In the original, every path-camera call (camPosObj, camPosDir, camTransObj, camTransDir) that does not arrive clears the arrival flag before it places the camera; only the call that runs off the path's last segment sets it. Here the flag was cleared only by camIsArrived itself and by pushCam / popCam, so a script that arrived and then started another path without asking camIsArrived would read "arrived" on its first test of the new path. The stock scripts test it every frame, so no stock mission changes; a custom script can.

The placing path of camPosObj / camTransObj / camTransDir and camPosDir now clear it. (It touches the same file as the previous PR and can ride with it.)

Checked: dotnet test tests/I76.Sim.Tests --filter ScriptCamera: 3 passed, on master 8edea2a; the new test fails on master. The full tests/I76.Sim.Tests run on the branch had 31 failures, every one in the Windows baseline list (BrakeTests, FrameRateTraceTests, MortarRangeTests, ...), none touching the script camera.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 4. `fix/mission-music-wrap` (#128)

**Title:** Mission music wraps to track 2 after track 14 in the default sequence, as the original's does (#128)

```sh
offer fix/mission-music-wrap
dotnet test tests/I76.Formats.Tests --filter MissionMusic
git push fork pr/mission-music-wrap
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/mission-music-wrap \
  --title "Mission music wraps to track 2 after track 14 in the default sequence, as the original's does (#128)" \
  --body-file - <<'EOF'
The 1997 retail build plays MCI from the mission's disc track N to the start of track max(N+1, 15), so a mission plays N..14 without a break, a track of 15 or more alone, then 2..14 again (the executable's own CD requests, two runs). The default Disc policy went on through the disc's last audio tracks (15 to 17) before going back to track 2.

The Disc policy now follows the same sequence and keeps its own timing: the next track starts at once, with no poll wait. The deviations row for what follows the track says so; I76_MUSIC_SEQUENCE=retail and =gog are unchanged.

Fixes #128.

Checked: dotnet test tests/I76.Formats.Tests --filter MissionMusic: 19 passed (the sequence tests changed to the wrap), on master 8edea2a.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 5. `fix/smoke-sky-flicker` (two commits)

**Title:** 1997 damage smoke: puffs leave the car where the sim has it, drawn between ticks, none nearer the eye than 1 m

```sh
offer fix/smoke-sky-flicker
dotnet test tests/I76.Game.Tests --filter SmokeWindow
git push fork pr/smoke-sky-flicker
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/smoke-sky-flicker \
  --title "1997 damage smoke: puffs leave the car where the sim has it, drawn between ticks, none nearer the eye than 1 m" \
  --body-file - <<'EOF'
In the cockpit, with the player's own car smoking, the 1997 look's puffs flickered over the sky, worst at speed. Each puff was spawned off the car as drawn (between two sim ticks) and then drawn at its own tick positions, so at 30 fps on the 20 Hz sim it jumped against the car by up to a tick's travel from frame to frame, through the cab; and puffs a few centimetres from the eye were drawn (the camera's near plane is 0.05 m), filling the screen for a frame. The original draws a puff only between 1 m and 60 m of the eye (its main view's near plane is 1.0).

The puffs now spawn off the sim's pose, keep their previous tick's position and are drawn between the two as the cars are; and no puff nearer than 1 m is drawn, in both looks (SmokeWindow). A second commit puts the modern look's clumping on the smoke's own game time instead of the wall clock, so it no longer swells and ebbs through a pause.

Checked: dotnet test tests/I76.Game.Tests --filter SmokeWindow: 4 passed (new), on master 8edea2a. T01, own car at health 0.3, cockpit, 1997 look, about 40 m/s, 30 fps, 61 frame pairs: sky pixels changing by more than 12 levels between frames 26.3 % before, 3.4 % with the 1 m cut alone, 17.0 % with the interpolation alone, 0.31 % with both (two runs, identical; no smoke: 0.31 %).

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 6. `fix/middle-wheel-suspension` (#124)

**Title:** A six-wheeler's middle pair rides its own suspension: stepped with the others on the ground with no weight transfer, drooping with them in flight

```sh
offer fix/middle-wheel-suspension
dotnet test tests/I76.Sim.Tests --filter MiddleWheel
git push fork pr/middle-wheel-suspension
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/middle-wheel-suspension \
  --title "A six-wheeler's middle pair rides its own suspension: stepped with the others on the ground with no weight transfer, drooping with them in flight" \
  --body-file - <<'EOF'
The original's ground contact runs the per-wheel suspension step on the middle pair (when the car has one) after the four corner wheels, passing no weight transfer; the step in flight droops every wheel the car has. Its forces, the push-out and the attitude plane still read only the four corner wheels. I26 never stepped the middle pair: its ground height stayed unset and it was drawn at the mean of its side's front and rear travel. The stock cars with a middle pair are the Marx trucks (vmxmarx1-3, normarx, s07xm01, t17mx01) and the milk tankers (vxmilk1-3, nomilk3). The tank (vstank1) has no middle pair, only slots 0, 1, 4 and 5: its road wheels are the body's tread mesh, so the depth they sit at in #124 is the body's pose and is not changed here.

The middle pair is now stepped as the original steps it, on the ground and in flight. Network clients are not sent the middle pair's travel (no wire change): they draw it at the front/rear mean as before.

Fixes the six-wheeler part of #124 (the tank's wheels are its body's pose, as in the original).

Checked: dotnet test tests/I76.Sim.Tests --filter MiddleWheel: 4 passed, on master 8edea2a (at rest on flat ground all six wheels stand at rest travel with the ground under them; over a ridge under the middle axle only the middle wheels move and the body stays on the corners; in flight the middle pair droops to its limit; every stock six-wheeler of both installs parks on all six); the first three fail on the old code. The full tests/I76.Sim.Tests run on the branch with both installs: 31 failures, all in the Windows baseline list. Not compared with the original's six-wheeler wheel offsets.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 7. `fix/predicted-shots-of-jammable-weapon`

**Title:** Online, a damaged weapon's shots are the server's to show: the client no longer draws shots its jam then takes back

```sh
offer fix/predicted-shots-of-jammable-weapon
dotnet test tests/I76.Net.Tests --filter CombatEffects
git push fork pr/predicted-shots-of-jammable-weapon
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/predicted-shots-of-jammable-weapon \
  --title "Online, a damaged weapon's shots are the server's to show: the client no longer draws shots its jam then takes back" \
  --body-file - <<'EOF'
A weapon under 70 % of its hit points can jam in a network game (p = min(0.5, 1 - hp - 0.3) a tick while the trigger is held, the trigger then cleared), and the roll is the server's alone. The client's prediction of its own car ran the aims, lock and fire timers but not that roll, so it drew every shot at once and the server's shot counts then took most of them back: measured over 240 s with two clients, weapons held at 20 % had 62-63 % of their drawn shots taken back (tracers, flashes and sounds of shots that never happened) and some real ones drawn late; at full condition none.

The prediction now runs such a weapon's timers and ammo silently (Combat.ShotsAreServers: the jam can catch it, and it is not a flame thrower, whose stream stays predicted); the server's shot events for it are drawn and its rounds adopted, a round trip late. docs/multiplayer.md's "Own shots at once" says so.

Checked: dotnet test tests/I76.Net.Tests --filter CombatEffects: 15 passed, on master 8edea2a. The new ADamagedWeaponsShotsAreTheServers: a healthy gun drawn at once, then at 20 %: 23 fired by the server, 0 drawn at once, 23 server shots drawn, 0 taken back, 23 adopted; with the rule switched off the same test draws 37 at once and takes 22 back.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 8. `fix/own-shots-counter-at-respawn`

**Title:** A respawn starts the own shot counts again from the server's without counting the last life's shots as taken back

```sh
offer fix/own-shots-counter-at-respawn
dotnet test tests/I76.Net.Tests --filter ARespawnTakesNoShotsBack
git push fork pr/own-shots-counter-at-respawn
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/own-shots-counter-at-respawn \
  --title "A respawn starts the own shot counts again from the server's without counting the last life's shots as taken back" \
  --body-file - <<'EOF'
OwnShots compares the server's per-weapon shot counts with the shots the client drew, and starts again from the server's at a new generation. The comparison ran before the snapshot was written into the car, so it read the last life's generation, took the respawn's snapshot down the normal path (the server's counts restarted with the new armoury) and counted every shot of the last life as taken back: the I76_NET_DEBUG "taken back" figure jumped by the life's predicted count at each respawn (a step at each generation in a 90-minute two-client soak, flat in between).

The generation is now read from the snapshot's own car. Nothing visible changes beyond the debug line. (It touches the same test file as the previous PR and can ride with it.)

Checked: dotnet test tests/I76.Net.Tests --filter ARespawnTakesNoShotsBack: 1 passed (20 shots in the first life, 0 taken back), on master 8edea2a; on master's OwnShots the same test fails with 20 taken back.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 9. `fix/net-ping-low-percentile`

**Title:** The network ping shows the lowest of the last ten round-trip samples, so packet loss no longer inflates it

```sh
offer fix/net-ping-low-percentile
dotnet test tests/I76.Net.Tests --filter PingWindow
git push fork pr/net-ping-low-percentile
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/net-ping-low-percentile \
  --title "The network ping shows the lowest of the last ten round-trip samples, so packet loss no longer inflates it" \
  --body-file - <<'EOF'
The score window's ping (the server's per-seat value, in the existing player-row field) and the HUD's (the client's own) were the transport's latest round-trip sample, which under packet loss reads far above the link: measured over 150 s, a 60 ms link read a median of 66 ms without loss but 155 ms (up to 624) with 10 % loss, and a 300 ms link with 5 % loss up to 677 ms.

Both now show the lowest of the last ten samples (PingWindow, one sample a second): the link's round trip, still following a link that really gets slower after ten seconds. The prediction's use of the round trip (how long a predicted lasting round waits for the server's) keeps the raw sample. No wire change.

Checked: dotnet test tests/I76.Net.Tests --filter PingWindow: 2 passed, on master 8edea2a (a lossy series modelled on the measured one reads 62-66 ms throughout; a slower link shows after ten samples). Not run over a real lossy link.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```

## 10. `fix/escape-menu-right-click-reconfigure` (#106)

**Title:** A right click in the Control Configuration over a network game waits for a key again instead of capturing the pointer (#106)

```sh
offer fix/escape-menu-right-click-reconfigure
dotnet test tests/I76.Game.Tests --filter FlyCameraMouse
git push fork pr/escape-menu-right-click-reconfigure
gh pr create -R vahokif/I26 --base master --head <fork-owner>:pr/escape-menu-right-click-reconfigure \
  --title "A right click in the Control Configuration over a network game waits for a key again instead of capturing the pointer (#106)" \
  --body-file - <<'EOF'
The in-game Control Configuration (Esc, Control Configuration) rebinds a row on a right click, as the main menu's does. In a network game nothing happened: the game keeps a debug fly camera for the whole mission, off screen, and it took every unhandled right press to capture the pointer for its mouse look. A network game is never paused, so that camera saw the click the Control Configuration had just read; capturing the pointer moved the shell's mouse position off the row, so no prompt came, and the next frame let the pointer go again. A single-player game pauses the tree under the menu, which is why only sessions showed it.

The fly camera now takes the mouse (right-button look, wheel speed) only while it is the camera on screen, as its keys already do.

Fixes #106.

Checked: dotnet test tests/I76.Game.Tests --filter FlyCameraMouse: 2 passed, on master 8edea2a (FlyCameraMouseTests, new). Reproduced on a listen host (--host-listen --arena m01, Esc, Control Configuration, right click on Map, windowed on Windows): before, the cursor log showed "Captured -> Visible" on the press and no prompt; after, the "Press any key or button..." prompt opens, Z binds to Map, no capture logged. Single-player melee (--melee --mission m01 --drive) worked before and after.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01MtT8cX5neBeG1S99t56CpL
EOF
```
