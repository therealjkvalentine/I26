# Remix missions: generated scenarios from a stock map

The play fork's first step toward a roguelike mode: a mission **generated from a small data file** and played as a scenario.
A remix description names a stock mission whose map it borrows (terrain, roads, placements, look, and the script's library
routines), what changes on that map, and an objective template with its parameters. The generator turns it into a mission file,
its objective text and its script's source, from the player's install, into the user folder. Nothing of the game is in the
repository: the descriptions hold coordinates, placement and car file names, and objective lines written for the remix.

Code: `src/I76.Formats/Remix` (`RemixSpec`, `RemixBuilder`, `RemixTemplates`; no Godot, no sim). The shipped descriptions are
`src/I76.Formats/Remix/Scenarios/*.yaml`, embedded in the assembly. The game's side is `game/Scripts/Shell/RemixScenarios.cs`.

## How a mission is built

1. The base mission is read from the install (never written). Its script is decompiled to source
   ([mission-scripts.md](mission-scripts.md)).
2. A copy of the mission file is edited with the map writers ([map-format-writing.md](map-format-writing.md)): the player's
   placement moved (and flagged as the player's, so a scenario car never lands on another placement of the same car), new
   parked cars added (ODEF type 1, each with name bits no other placement of that name has, so its actor binds to it alone),
   the description's places appended to the script's nav-path table as `rx_NAME`, the WRLD hour set, the briefing and outro
   movies cleared, the objective text pointed at `STEM.npt`, the listed name set.
3. The script: the base's source keeps every routine, table and data cell, and of its threads only those running the
   **library** routines (the first 15 of an Interstate '76 mission's code, 28 of a Nitro one's): leaving the map, the player's
   death taunt and hit chatter, kill and friendly-fire lines. Every mission thread of the base is dropped. A leave-map thread
   on an exit the description lists under `drop_exits` is dropped too. The template's source (`rx_` names) is merged in and the
   whole compiled into the copy (`MissionScriptCompiler`); the library threads keep their bytecode, so co-op's analyses still
   recognise them.
4. **Idle cars**: the stock car placements that neither the template nor a `bind` names (the base's mission threads drove
   them; nothing would any more) are removed from the copy, with the library threads that watched them (a car's kill line, an
   ally's friendly-fire chatter). `idle_cars: keep` keeps them as decor.
5. Written to the output folder: `STEM.msn`, `STEM.npt`, `STEM.yaml` (the merged source, for reading), `STEM.marks` (what the
   map shows, below) and `STEM.remix`, the stamp: the generator's version, the description's hash, the base file's hash. A
   build is redone only when the stamp differs.

The game builds the shipped descriptions at start-up into `user://remix` (about 20 ms when current, about 0.2 s the
first time), points `GameInstall.UserMissionDir` there (missions are found by name after a downloaded map's folder and before
the install's own; their `.npt` is found by name, adding names only) and lists them on the base game's SCENARIO form after the
seven stock scenarios, then RANDOM JOB (the director, below). Base game only: every shipped description uses an Interstate '76
map. The list shows six rows: "N more above" and "N more below" beside its arrows, and a scroll bar, say there are more.

### Gates

A gate ignores a trigger while it slides (the original's `GateMotion`; a slide takes 15-20 s), so a template never triggers
one directly: each moment (start, trap, win) adds one to the gate's count of wanted triggers, and a thread per gate sends them
one at a time, `gates.settle` seconds apart (30 by default, longer than any stock gate's slide). A trap that springs while the
start's opening is still under way shuts the gate once it has opened, rather than being lost.

### The map (M)

The base mission's map art (WRLD +0x79) is a drawing of the stock mission with its own marks painted in (T14's says START at
the truckstop), so a remix shows the port's own: the pen sketch of the ground the host form draws for an arena
(`ArenaSketch.Around`: hills hatched, roads in sepia) round the remix's places, on a plain sheet, with each mark ringed and
lettered in the game's hand font, the title above, a north arrow, and a convoy's route dashed. The marks are written by the
builder (`RemixMarks`): START, then by template DEFEND (the arrival zone), PICKUP and HOME, HIDEOUT, CONVOY and EXIT; two marks
within 150 m share one ring ("START / HOME"). Both looks: anti-aliased in the modern look, hard pixels in the 1997 look.

## The description

```yaml
id: x07farm                  # the generated stem (1-8 of A-Z a-z 0-9 _), not a stock name; x for remix
title: 380 Farm Raid         # the listed name (at most 15 characters)
base: t07                    # the stock mission lending its map
hour: 17                     # optional: the WRLD hour (the light preset)
start: {at: [x, z], heading: deg}   # optional: the player's placement moved (degrees from +Z toward +X)
places:                      # new nav paths, rx_NAME: a zone is one point, an entry two (where and which way), a route more
  farms: [[1150, 48500]]
bind:                        # existing placements the script needs, by object name and copy (0 first)
  barn_a: {object: bhbarn1, nth: 0}
cars:                        # new parked cars: the car file's stem and where they wait (off the map: a hidden lot)
  raider1: {vcf: vccameo1, at: [260, 49350]}
remove: [{object: NAME, nth: 0}]   # stock placements taken out
drop_exits: [exit1]          # stock leave-map threads dropped, by their exit path's label
gates: {start: [gate1], trap: [gate1], win: [gate1, gateblock], settle: 30}   # gates triggered at the start, the trap moment, the win
idle_cars: remove            # or keep: the stock cars nothing names
radio: {start: [...], trap: [...], wave: [...], win: [...]}       # CB clips (sound file stems of the install) at those moments
objective: {template: defend | deliver | tail | intercept, ...}
text: {objectives: [...], failures: [...]}   # the .npt: up to 6 objective lines ("(hidden)" = revealed later), the failures
```

A name in a template's parameters is, in order: a `cars` or `bind` entry, or the base script's own actor key (`enemy10`,
`cop4`, `gate1` as `i76 fsm-decompile` names them). A place is a `places` entry or a stock path label (`jump1`, `behind1`).

Failure line 1 is always the player's death and line 2 leaving the map (the stock leave-map routine shows line 2); a template's
own failures follow. Every template ends the way the originals do: objectives marked, the win radio, then `successAll` once
the CB is quiet.

### Template `defend`

Nitro P11's farm defence, written fresh. Attackers wait off the map and start in **waves** (`waves: [{cars, at_seconds,
at_kills}]`: a car starts once its wave's seconds from the start have passed and its kill count is reached: the player's kills
only (`isGroovesFault`), so a raider dead to friendly fire or its own mines never starts a wave; once the player has arrived, a
wave whose earlier waves are all dead starts however they died, so none waits for a kill that can no longer come); each teleports to
a random entry of `entries`, picks a random target among the `defend` structures (or the player, once he has arrived), keeps it
until it dies or it has been within `near` metres of it for `retarget` seconds, and turns on the player when hit. Parameters:
`defend`, `waves`, `entries`, `may_lose` (structures that may be lost; default all but one), `retarget` (10), `near` (150),
`arrive: {place, radius, seconds}` (optional), `skill` (1 best .. 5), `aggression`. The trap gates trigger on arrival, the
win gates when the last attacker dies; the wave radio plays as each later wave's kill count is reached.

Objectives: 1 arrive (done at once without `arrive`), 2 defend, 3 kill them all. Failures: 1 death, 2 left the map,
3 more than `may_lose` structures lost, 4 too late to arrive.

### Template `deliver`

T05's rescue run, made generic: an optional `pickup` zone (a zone stands in for the package: the engine has no carried
object), then `home`, where the win needs no enemy within `clear` metres (T05's 300 m rule). `sleepers: [{car, wake,
siren}]` stay parked where they are until the player comes within `wake` metres or shoots them; `pursuit: {cars, entry, delay,
speed}` wait off the map and are teleported in, one behind the other, at `entry` `delay` seconds after the pickup (at the start
without one). `deadline` seconds run from the pickup (`deadline_from: pickup`, T05's clock on the way back; the default with
a pickup) or from the start.

Objectives: with a pickup 1 pickup, 2 home, 3 nobody follows; without, 1 home, 2 nobody follows. Failures: 1 death, 2 left the
map, 3 too slow.

### Template `tail`

Lose the tail, then the hideout (the design list's lose-the-tail). `pursuit: {cars, entry, delay, speed, siren}` are teleported
in behind the player `delay` seconds after the start; `sleepers` wake as he passes. Once they have caught up (an enemy within
the lost radius, or a minute gone by without), they are **lost** when nobody hostile has been within `lost.radius` metres for
`lost.seconds` (dead ones count as lost): objective 1, the hideout revealed. Then each gives up: it parks where it is and chases
again only while the player is within half the radius, until he is beyond it. `hideout: {place, radius}` counts only after
that, and only with nobody within `clear` (default the lost radius); reaching it with an enemy that near fails: he led them
home. `deadline` runs from the start.

Objectives: 1 lose them, 2 the hideout. Failures: 1 death, 2 left the map, 3 too slow (with a deadline), then led them home
(line 4 with a deadline, 3 without).

### Template `intercept`

Stop a convoy (the design list's convoy ambush). `convoy: {lead, escorts, route, exit, speed, alert_speed, alert, delay,
spacing, kill_escorts}`: the lead waits `delay` seconds, then drives `route` (a place of several points or a stock path) at
`speed`; the escorts keep station behind it in pairs, `spacing` metres apart (`follow`). Alerted (the player within `alert`
metres of the lead, the lead or an escort shot, or the lead dead: a bump among themselves is no alert), the lead drives on at
`alert_speed` (the same route, from where it is) and the escorts attack the player. The lead within `exit`'s radius fails.

Objectives: 1 the lead dead, 2 the escorts dead (with `kill_escorts`, the default; revealed with the lead's death). Failures:
1 death, 2 left the map, 3 the convoy got through.

## The shipped scenarios

| Id | Listed as | Base | What |
|---|---|---|---|
| `x07farm` | 380 Farm Raid | T07 | Six raiders in three waves (25 s; at 2 and at 4 kills) come for the three farmsteads T07 places between its roadblocks, by T07's own wave entries (`jump1`, `jump2`) and two from the north and the ridge. T07's east fence opens at the start and slides shut behind the player at the farms; the west roadblock opens at the win. |
| `x05grab` | Grab and Go | T05 | Out east to the wreck (a zone), back to the start with four pursuers teleported in behind and 175 s on the clock; T05's guards, roadside ambushers and pincer pair wait where T05 parks them. Home with nobody within 300 m. |
| `x10tail` | Tatum Tail | T10 | Lose the tail on 380: three of T10's roadblock cops are brought round behind you east of the roadblock, sirens on; the roadblock's two and the billboard's two wake as you pass (T10's fence is open from the start). Lost: nobody within 500 m for 12 s. Then the hideout in Tatum with nobody within 500 m, inside 6 minutes. |
| `x02conv` | Levelland Run | T02 | T02's own Leadsled gang (the lead and two wingmen) drives T02's route north up the winding road to Levelland; catch the lead before it gets there, then its wingmen. Alerted within 250 m or by a shot, they speed up and the wingmen attack. |
| `x14back` | Back to Spanner | T14 | T14 driven backwards: from the county line up 285 to Spanner's truckstop past T14's police, which wake as the player passes (sirens on); the truckstop's cop and SWAT vans come out to meet him. Home within 6 minutes with nobody within 400 m. |

## Playing them

- **In the game**: MELEE, AUTO MELEE, SCENARIO: the five are at the end of the list (scroll down), sorted by id, and RANDOM JOB
  last; pick a car and ENTER AREA. `--mission x07farm` (with `--drive`) starts one directly; `--mission xjob` the last job rolled.
- **Headless**: `i76 remix build --builtin --out DIR`, then `i76 --remix-dir DIR run x07farm 300`. `I76_REMIX_DIR` stands in
  for `--remix-dir`. The run's drivers (`I76_RUN_AIDRIVE`, `I76_RUN_ROUTE`, `--player-at`) take the generated places by their
  `rx_` labels; `--kill NAME@S` (counted as the player's kill), `--crash NAME@S` (nobody's kill: friendly fire, a car's own
  mine) and `--wreck NAME@S` (every node of a structure shot through, gunfire's way) script the fights. `i76 remix map BASE`
  lists what a description can name on a stock map (placements with the base script's actor keys, paths, roads as JSON).

## Saves, determinism, the net

The generator touches nothing in the simulation: a generated mission is a mission file the sim builds and runs like a stock one.
Runs are deterministic (the script's `rand` is the session's seeded stream), and a snapshot restores into the rebuilt mission
(tests: `RemixMissionTests`). The session's install identity holds the mission file's size and CRC, so a save of a remix
restores only into the same build of it; editing a description (or rolling a new RANDOM JOB, which reuses `xjob`) makes old
saves of it refuse to load, and the player is told so ("This scenario has been rebuilt since the checkpoint") before the mission
starts from its beginning. Co-op and network games do not offer generated missions (the co-op list takes stock missions only,
and a client would need the file: `MapFileSet` sends melee arenas only), and a server asked to host one (a mission found in the
generated missions' folder: a command line, a stale setting) refuses it ("single player only").

## Node tables

`src/I76.Formats/Remix/Nodes/*.yaml` (embedded; `RemixNodes`), one per stock map: the places a generated job may use, written
by hand as world coordinates read off the map (`i76 remix map BASE`, checked to lie on a road) and names, nothing of the game's
data:

```yaml
base: t10                     # the stock mission lending the map
hour: 21                      # optional light preset
lot: [5690, 46790]            # where new cars wait, hidden, out of every job's way
gates: [gate]                 # stock gates (actor keys) opened at the start: a fence across a road the jobs use
starts: [{name, at: [x, z], heading}]
nodes: [{name, kinds: [pickup, home, hideout], at: [x, z]}]
entries: [{name, at: [[x, z], [x, z]]}]   # where a car is put in and the way it faces; or {name, path: LABEL}, a stock path
routes: [{name, points: [...] | path: LABEL, park: [x, z], heading, exit: [x, z]}]   # a convoy's road, where it parks, where it ends
clusters: [{name, called, at, radius, objects: [{object, nth}], entries: [names]}]   # the base's own structures to defend
```

Shipped: T02, T05, T07, T10, T14.

## The director

`RemixDirector.Roll(seed)` (`src/I76.Formats/Remix/RemixDirector.cs`, outside the simulation, its own SplitMix64 stream) picks,
from a seed: a (map, template) pair the node tables allow (deliver needs a pickup 0.7-4 km from a start; tail a hideout 2 km
or more and an entry 250-800 m behind a start; intercept a route starting 0.5-2.5 km away; defend a cluster), a heat (0-2: the
cars' pool, their count and skill), the start, the nodes, the cars (the base game's generic cars by heat, the police for a
tail, a truck to lead a convoy; new placements at the lot, or at the route's start for a convoy) and the deadlines (from the
distances). It writes an ordinary description (title `JOB N`, objective 1 begins `Job N:`, so a player's job can be rolled
again); stock cars are left out (removed as idle). The same seed gives the same file.

- **In the game**: SCENARIO, RANDOM JOB (the list's last row): ENTER AREA rolls a new seed, builds the job as `xjob` into
  `user://remix` (its description beside it as `xjob.job.yaml`) and starts it. The game log says `remix: RANDOM JOB seed N`.
- **Headless**: `i76 remix roll --seed N [--yaml FILE] [--build [--out DIR]] [--id STEM]`, then `i76 --remix-dir DIR run xjob`.
- **Tests**: 200 seeds roll, build (the builder's validation included) with every car file in the install; one job of each
  template loads and runs half a minute.
