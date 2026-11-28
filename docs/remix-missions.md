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
  raider1: {vcf: vccameo1, at: [260, 49350]}    # team: the enemy's (2) for a car the template turns on the player, else 0
props:                       # structures set down before the start (below, "Structures")
  - {object: akwreck3, type: 2, at: [8762, 45384], heading: 30}
remove: [{object: NAME, nth: 0}]   # stock placements taken out
drop_exits: [exit1]          # stock leave-map threads dropped, by their exit path's label
gates: {start: [gate1], trap: [gate1], win: [gate1, gateblock], settle: 30}   # gates triggered at the start, the trap moment, the win
idle_cars: remove            # or keep: the stock cars nothing names
radio: {start: [...], trap: [...], wave: [...], win: [...], steps: [[...], ...], reinforce: [...]}   # CB clips (sound file stems of the install)
objective: {template: defend | deliver | tail | intercept | escort | raid, reinforce: [...], finish: {place, radius}, finish_clear, finish_after, ...}
text: {objectives: [...], failures: [...], deaths: [...], notes: [...]}   # the .npt and the notepad's notes
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

### Template `escort`

See a slow friendly through (the design list's escort, written fresh). `escort: {car, route, mid, goal, speed, wait, resume,
wingmen}`: the escorted car (a `cars` entry, on the player's side, team 1) drives `route` at `speed` by way of `mid` (objective
1) to `goal` (objective 2), but only while the player keeps up: once he is more than `wait` metres away (220) it stops, and it
goes on when he is back within `resume` (110), so it never meets an ambush alone (the owner's complaint about the stock escorts:
helpers that ran ahead or fell behind). Wingmen (`cars` entries, the player's side) ride with it in line behind it on the road,
40 m back and 15 m more each, firing at the nearest enemy, and stop when it stops. (A station off to its side put one in a ditch;
one nearer than 40 m kept nosing into its back and stopping to dodge it; one following the player fell 250 m behind, dodging
the player's own car in the column.) The ambush: sleepers on the way, and reinforcements whose `target: escort` comes for the
escorted car itself rather than the player.

Objectives: 1 the halfway point, 2 the goal, then the finish (see it in: the player at the goal with nobody within 300 m).
Failures: 1 death, 2 left the map, 3 the escorted car wrecked.

### Template `raid`

Hit and run. `targets`: what must fall, each a `bind` entry (a structure of the base: a cluster's oil pumps, a town's shops) or a
`cars` entry that never starts (trucks parked at a yard, on the enemy's side); `arrive` (optional) the target area; `escape:
{place, radius, seconds}` the way out. Objective 1 is getting there (or hitting a target from afar), 2 every target down, 3 out
to the escape zone within its seconds of the last target's fall, while the reinforcements of step 3 close in on the way out
(a roadblock of wrecks at their entry). Guards parked on the road in wake as the player comes.

Objectives: 1 there, 2 the targets, 3 out. Failures: 1 death, 2 left the map, 3 too slow ("They closed the roads").

### Every template: steps, the twist, the finish, sleepers

- **Sides.** Every car the template turns on the player (raiders, sleepers, pursuers, a convoy, reinforcements) is placed on the
  enemy's side, team 2, as every stock mission's enemies are (`RemixRoles`); a new car is otherwise nobody's (team 0). The target
  keys (E, T) skip team 0 outside a melee (the original's rule), so before this a remix's own cars could never be targeted.
- **Steps.** A controller keeps the open objective's number (`rx_step`): as objective k opens it plays `radio.steps[k-1]` (its
  instruction) and sets the death line.
- **Death lines.** `text.deaths` (one a objective; made from the objective's own line when missing) follow the failures in the
  .npt; a death shows the open objective's ("Ocean 5 caught up with you", "You were killed on the way to the safe house").
- **Reinforcements** (`reinforce: [{cars, entry, at_step, delay, speed, siren, target}]`): wait hidden in the lot until their step opens,
  then `delay` seconds, are teleported in at the entry in pairs side by side and come for the player (`target: escort`: for the
  escorted car); `radio.reinforce` on arrival.
- **Finish** (`finish`, `finish_clear`, `finish_after`): after the template's own win, the last objective line opens: get to the
  zone with nobody within `finish_clear`, counted only `finish_after` seconds after it opened (a finish at the start is not done
  by standing there).
- **Sleepers** for intercept, defend, escort and raid too (a roadblock on a convoy's road, an ambush on an escort's, a raid's
  guards): parked until the player is within `wake` or shoots.
- **Idle cars**: the stock cars and helicopters (type 9) nothing drives any more are removed (an idle helicopter left by T11's
  riverbed kept a tail from ever being lost: it counted as an enemy near the player).

### Structures

`props` sets structures of the install down on the copy before the mission starts: an object by its placement name (any `.sdf`
of the archive; the base game's models are global, so T05's wrecks can be set down on T02's map), placed as the ODEF type the base
game places it (2 building, 3 small object, 4 scenery), at a point and heading, standing on the terrain's height under it (the
sim's bilinear ground). It is an ordinary placement: the sim builds its collision and damage from its model as for a stock one
(the check finds each as a collidable static). Shipped: a wreck by Levelland Run's start and wrecks at its hairpin roadblock; the
director sets a pile of wrecks beside every pickup and a pair of wrecks either side of the road at a tail's or a convoy's
reinforcement entry. What it can't do: the terrain and roads stay the base's (no new ramps or roads; a structure on a slope
stands at its centre's height); props go beside roads, never across one the AI drives (the AI's routes do not avoid new objects
well); destroyed props stay as the base game's do; a structure type with nodes the script names (a gate) needs its stock
actor; nothing with a script role of its own (a new gate, a moving object) is placed this way.

### The map, the notepad and the radio line

The marks file carries `NOTE` lines (the description's `text.notes`: where the places are, in words, and a rolled job's number and
challenge): they are written under the objectives on the notepad and along the map's foot. A remix opens on the notepad half a
second into the mission (once no cut-scene holds the view): any key closes it. As each objective opens its line is lettered across
the top of the screen for eight seconds over the note sound (`RemixRadioLine`), besides the step's clip. The map picture is kept
per stem, look and build stamp: RANDOM JOB rebuilds `xjob` for every roll, and a picture kept by the stem alone had shown the first
job's map for every later roll in the same run (the owner's convoy "marked east, started west").

### Checking a build

`i76 remix check --nodes [BASE|FILE.yaml...]` (and `RemixRound4Tests`) holds each node table to its map (`RemixNodeCheck`): every
point on the map's ground, on a road (22 m) or on drivable ground (no slope over 32 degrees within 5 m), every start, node, cluster
and escort route's ends 300 m or more from a leave-map exit, every point reachable from the table's first start over drivable
ground or roads (a flood over a 10 m grid; roads always pass, so a bridge counts), an entry's two points 20-400 m apart, a route's
points on roads, a cluster's structures where it says, the lot 350 m or more from every place, the neighbours tables, the words
(`called`, `name`) written. (It found T06's crossroads by Santana's on ground of their own, cut off from the rest of the map.)


`i76 remix check [--builtin] [--seeds A-B]` (and `RemixMissionTests`, over the shipped remixes and 500 rolled jobs) loads each
build into the sim and holds it to its description and map (`RemixCheck`): every hostile car exists, is on the enemy's team, stands
where it is parked, on the ground; the convoy's lead starts at its CONVOY mark; every mark is the zone its script tests; every
place the player must reach is 300 m or more from a leave-map exit (the library's warning starts at 275 m and fails the mission
at 50 m: the director's first side nodes sat in road-end exit stubs); every prop is a collidable static. It then walks the job
(the player teleported to each step's place as it opens; an escort walked 70 m behind its car, which must reach its goal; a raid's
targets shot through as its second step opens; each hostile car killed as the player's kill once checked) until every car has
come out, and from 60 m checks that it is a radar contact the next-target key reaches.

### Challenge

`RemixChallenge`: a car's strength is sqrt(its hit points (armour + chassis) over the Piranha's x its guns' damage a second over the
Piranha's), times 1.3 (setSkill 1) .. 0.7 (5); a convoy's lead counts half. Cars that come at once (a wave, a pursuit, a
reinforcement, sleepers within 300 m of each other, a convoy) make a group scoring (sum of strengths)^1.5. Score = the groups +
1 a step after the first + 2 x (the legs' length at 22 m/s over the deadline, at most 2). `i76 remix score [--builtin]
[--seeds A-B] [--level N] [--cars]` measures it with the install's cars. Back to Spanner scores 16.8, the calibration (1.0);
the director fills each job to 16.8 x its level (0.6, 0.8, 1.0, 1.25, 1.6; RANDOM JOB uses 1.0), growing its smallest group a car
at a time (14 cars at most); measured over 300 jobs at 1.0 the median is 0.98 (0.6 to 1.4). The pools are tiered by measured
strength (0.3-0.6, 0.65-0.95, 1.0-1.7 Piranhas).

## The shipped scenarios

| Id | Listed as | Base | What |
|---|---|---|---|
| `x07farm` | 380 Farm Raid | T07 | Six raiders in three waves (25 s; at 2 and at 4 kills) come for the three farmsteads T07 places between its roadblocks, by T07's own wave entries (`jump1`, `jump2`) and two from the north and the ridge. T07's east fence opens at the start and slides shut behind the player at the farms; the west roadblock opens at the win. |
| `x05grab` | Grab and Go | T05 | Out east to the wreck (a zone), back to the start with four pursuers teleported in behind and 175 s on the clock; T05's guards, roadside ambushers and pincer pair wait where T05 parks them. Home with nobody within 300 m. |
| `x10tail` | Tatum Tail | T10 | Through the side roads: start on the south road at the east end with three of T10's roadblock cops coming up behind (sirens on); the roadblock's two and the billboard's two wake as you pass. Lost: nobody within 500 m for 12 s. Then the hideout at the top of the dirt loop north of the roadblock; then the twist: it is watched, Cloaker's crew comes in from the north, make for the safe house up the road north out of Tatum. 10 minutes. Challenge 0.98. |
| `x02conv` | Levelland Run | T02 | T02's own Leadsled gang (the lead, its two wingmen and two more) drives T02's route north up the winding road to Levelland, past the gang's roadblock (two cars and wrecks) at the hairpins; catch the lead before it gets there, then the wingmen; then Gas Bandit's crew comes down from Levelland: get back to the crossroads with nobody behind. A wreck by the start. Challenge 0.98 (was 0.11). |
| `x14back` | Back to Spanner | T14 | T14 driven backwards: from the county line up 285 to Spanner's truckstop past T14's police, which wake as the player passes (sirens on); the truckstop's cop and SWAT vans come out to meet him. Home within 6 minutes with nobody within 400 m. |

## Playing them

- **In the game**: MELEE, AUTO MELEE, SCENARIO: the five are at the end of the list (scroll down), sorted by id, then RANDOM JOB
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
starts from its beginning. The only generated missions a network game plays are a co-op roguetrip's jobs (below, "Roguetrip in
co-op"), which every player builds from the host's numbers; a server asked to host any other generated mission (one found in the
generated missions' folder: a command line, a stale setting) refuses it ("single player only"), and the map transfer never sends one.

## Node tables

Each table also names its places in the port's words (`called`), the cast whose crews a job sets on the player (`cast`: a name of
the game's own cast and `gang` or `police`: Leadsled, Gas Bandit, Balrog, Road Knight, Inferno, Patriot, 7 Edwards 7, Cloaker,
Ocean 5, 6 Nora 6, Callisto, Malocchio, Union 12) and `stock_action`: where the stock mission's own action is (its car placements
and its non-exit script paths, thinned to a point every 300 m, computed from the base and written in). The director weighs a place
1 + 8 x (its distance from the stock action / 1.2 km, at most 1)^2, so the corners the stock mission never visits come up nine
times as often; T05's long dirt road south and southwest road, T10's roads off 380 at the east end and out of Tatum, T14's dirt
roads east of the truckstop and the west road, T02's crossroad were added for that. Every start and node is on a road and 300 m or
more from a leave-map exit.

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

Shipped: sixteen, the trip's maps but T04 (a dream): T01-T03, T05-T17. Round four added T01, T03, T06, T08, T09, T11, T12, T13,
T15, T16 and T17, read off the map dumps (`i76 remix map BASE --json`) and the printable atlas's unvisited areas (its grid squares,
roads and buildings): side roads, road ends and the open ground beside them for nodes, the building clusters for defence and raids
(Seagraves' main street, Plains' warehouses, the oil fields' pumps of T09, T15 and T17, Roswell's school and the Komplex's
outbuildings), and escort routes along roads a slow car can drive (three were cut short where the AI's car jammed: under the
Pecos bridge, round the corner east at the Bar-D, through the hairpin west of the mesa's pumps). Each table also has:

```yaml
name: Seagraves               # the map on a roguetrip's choice screen
neighbours: [t02, t07, t08, t09]   # the maps next to it (bases with tables), for a roguetrip's route
nitro: false                  # a Nitro Pack map: offered only with the Nitro install (no Nitro table yet)
routes: [{name, points, escort: true, called: the dirt road east of town}]   # a route an escort can drive, and its end in words
clusters: [{..., uses: [defend, raid]}]   # what a cluster serves (defend only when left out)
nodes: [{kinds: [yard]}]      # a yard: where a raid's trucks can be parked
```

## The director

`RemixDirector.Roll(seed)` (`src/I76.Formats/Remix/RemixDirector.cs`, outside the simulation, its own SplitMix64 stream) picks,
from a seed: a (map, template) pair the node tables allow (deliver needs a pickup 0.7-4 km from a start; tail a hideout 2 km
or more and an entry 250-800 m behind a start; intercept a route starting 0.5-2.5 km away; defend a cluster that serves defence;
escort an escort route (the player starts 35 m behind the escorted car at its first point); raid a raid cluster or a yard
0.7-3.5 km from a start), a heat (0-2: the
cars' pool, their count and skill), the start, the nodes, the cars (the base game's generic cars by heat, the police for a
tail, a truck to lead a convoy; new placements at the lot, or at the route's start for a convoy) and the deadlines (from the
distances). It writes an ordinary description (title `JOB N`, objective 1 begins `Job N:`, so a player's job can be rolled
again); stock cars are left out (removed as idle). The same seed gives the same file, and the same files play the same (a test
builds a job twice and compares the files and the state hash after 30 s).

Every job has three steps or more and a twist: deliver (the pickup, guarded, with a pile of wrecks; home with pursuers; a crew cuts
the way home), tail (shake the police; the hideout, with a roadblock of more police on the way; the hideout's watched: a safe house,
with a stakeout's crew behind), intercept (the lead, which rolls when the player is within 600 m or once he has had time to get
there; its escorts; the gang's backup; home), defend (arrive; the waves; a heavier late crew). Each place is said in words with its
distance and compass direction from the start ("Hideout: the road north out of Tatum, 5.5 km west of the start."), each step has a
radio instruction from the install's clips (named by id: get over here, that's just north of here, get over to the rendezvous,
pick your shots, got away, we got a problem, company, bandits) and its own death line.

- **In the game**: SCENARIO, RANDOM JOB (the list's last row): ENTER AREA asks for the job number (the PASSWORD popup's art and
  typing: digits, Backspace, Enter; blank rolls a new one; Up and Down step through the last ten, kept in `user://remix/jobs.txt`;
  Esc stays on the form; `I76_JOB_NUMBER` answers for a capture). The number is on the notepad's first line, its notes and the
  map's title. Then it rolls that seed, builds the job as `xjob` into
  `user://remix` (its description beside it as `xjob.job.yaml`) and starts it. The game log says `remix: RANDOM JOB seed N`.
- **Headless**: `i76 remix roll --seed N [--yaml FILE] [--build [--out DIR]] [--id STEM] [--template T] [--map BASE] [--challenge C]`,
  then `i76 --remix-dir DIR run xjob`; `--template`, `--map` and `--challenge` narrow a roll as a roguetrip's offer does (also
  for `check` and `score`).
- **Tests**: 200 seeds roll, build (the builder's validation included) with every car file in the install; one job of each
  template loads and runs half a minute.

## Roguetrip

The first run of the roguelike plan: four generated jobs across neighbouring maps of the region, the challenge rising 0.8, 1.0,
1.2, 1.4 (Back to Spanner's 1.0). `RemixRun` (`src/I76.Formats/Remix/RemixRun.cs`, no Godot, no sim) and the shell's
`RoguetripScreen` (`game/Scripts/Shell/RoguetripScreen.cs`). Rounds four and five called it ROAD TRIP and listed it last on the
SCENARIO form; it is now ROGUETRIP, an entry of its own on the main menu.

- **The way in.** The main menu's **MELEE** row has ROGUETRIP after MULTI MELEE and AUTO MELEE (base game only): set in Impact
  as the row's sprites are, as NITRO is in AUTO MELEE's row (`PortButton`; `MainMenuScreen.MeleeRowPlace`), white when lit with
  its banner on the banner line, cut into the frame's pixels in the 1997 look. The mouse, the pad's focus and the arrow keys (the
  main menu takes the keyboard as the pad: arrows move the focus, Enter clicks it) reach it. It opens the car pick (the scenario
  form, its list the one ROGUETRIP row), then ENTER AREA asks the run number, then the choice map. Esc steps back: from the run
  number to the car pick, from the car pick (or CANCEL) to the main menu with MELEE open, and from the choice of a run just begun
  (no job taken) to the car pick, the run dropped (its number stays among the recent ones). With a run in progress ROGUETRIP goes
  straight to it (its car is the run's), and Esc on its choice goes to the main menu with MELEE open, the run kept. RANDOM JOB
  stays on the SCENARIO form.

- **The offers.** Before each job the player picks one of two or three offers: a map (by its node table's `name`: "Seagraves",
  "the Pecos bridge"), the job (DELIVERY, SHAKE THE TAIL, CONVOY HIT, DEFENCE, ESCORT, RAID), its challenge, and a line of flavour
  that is the job's own first objective, naming the gang ("Gas Bandit's crew left the goods..."). The first job's offers are on the
  run's home map (picked by the run's number) and its neighbours; later ones on the neighbours (`neighbours` in each table, written
  from the roguelike gazetteer's road and neighbour links) of the map the last job was on, ones not yet visited first, a different
  template each where the maps allow. Each offer carries its job's seed; the job is the director's roll for that seed on that map
  and template at that challenge (`RemixDirector.JobAsk`), built as `xrun`, titled `ROGUETRIP k/4`, its first objective labelled
  "Roguetrip N, job k:".
- **The number.** Everything a run offers is a function of its number and the picks made (a SplitMix64 stream of its own,
  outside the simulation, never the wall clock): the same number and the same picks give the same offers and the same jobs.
  ROGUETRIP asks for the run number on the job number's popup (blank: a new one; Up and Down: the last ten, `user://remix/runs.txt`;
  `I76_RUN_NUMBER` answers for a capture).
- **The car.** The car picked on ROGUETRIP's car pick drives the whole run. Its damage carries from job to job (`RunCarry`: read off
  at a job's end, put back at the next one's load before the first tick): armour and chassis on each side, engine, brakes,
  suspension, wheels, each gun's hit points and rounds. The trip's own carry (the original's) leaves armour and chassis out and
  fills the guns; a run carries all of it (docs/deviations.md). Each win grants one pick: FULL REPAIR (armour, chassis and parts as
  built; the rounds as they are), FULL AMMO (every gun loaded; the damage as it is) or PUSH ON (nothing, a bonus mark).
- **The end.** A failed job ends the run (the escape menu's Replay leaves a roguetrip's job as Abort does: a job is played once);
  four wins finish it. The summary sheet lists the jobs, the kills (the player's, counted at each job's end), the bonus marks and
  the run's number; DONE keeps the run as `run-last.yaml` and clears the way for a new one.
- **Kept.** The run is `user://remix/run.yaml` (YAML: the number, the phase, the map, the jobs played, the car's state), written
  before each job and after each result (to a new file, then moved over the old). Quitting the game keeps it: MELEE > ROGUETRIP
  takes it up at the same choice; a job the game closed during comes back as the same choice (the same offers),
  not as a loss. Base game only; in co-op the host keeps its own run (below, "Roguetrip in co-op"). The files keep the names they had under ROAD TRIP (`run.yaml`,
  `run-last.yaml`, `runs.txt`, the job's stem `xrun`, the hook `I76_RUN_NUMBER`), so a run saved before the rename resumes.
- **The screen.** The port's paper sheet (record 16's paper, as Control Configuration's) in the shell's fonts: the title in its
  heavy caps, the offers in the hand of the scenario list, one box each, the chosen one marked in red pencil; Up and Down (or 1-3,
  or a click) choose, Enter or TAKE THE JOB takes it, Esc or BACK goes back to the scenario form with the run kept.
- **Round five** (below): busts, the debrief page, the salvage and garage between jobs, the choice map, reproduction lines.
- **Not yet.** Fuel: see the plan (fuel would be a run-level number spent per kilometre driven and per job, read off the player's
  odometer at a job's end, refilled at a job's gas station node or by a fourth pick; an offer would show its cost). Nitro maps:
  the tables' `nitro` flag and the offers' filter are there, no Nitro table yet.

## Round five: pacing, places, busts, salvage, reproduction, screens, radio

### Pacing

The owner: "1-2 mins of driving between events on average (snaky drive for fun or race closer to 3-4 okay)". `RemixPacing` times a
job's drives on its map's roads (each road's centre line every 10 m, junctions within 30 m; a point off the road joins its nearest
sample across country; the quicker of that and straight across country at 12 m/s), at 22 m/s on the road (about 50 mph): the start
to the first contact (a tail's pursuit comes to the player: its delay and a run in; a parked guard or sleeper on the way can come
first), then each event to the next (deliver: pickup, home; tail: hideout, safe house; intercept: the convoy, back from where it is
caught; defend: the place; escort: the ambush, halfway and the goal at the escorted car's pace; raid: the target, the way out). Each
job declares its budget (`pacing: {first, mean, leg, long, long_legs}`; by default 120 s to the first contact, 120 s between events
on average, 150 s for one leg, 240 s for a long one: an escort's legs, a raid's escape). `i76 remix check` fails a job over budget;
`i76 remix pace [--seeds A-B] [--trips A-B] [--builtin] [--stock] [--places]` prints the distributions.

The director places the start near the job's first place (350-1300 m in a straight line, from the table's starts and its nodes, the
places of note six times as likely), the next places 600-1500 m on (a home node near the pickup rather than back at the map's other
end; a hideout 900-1400 m from the start), cuts an escort's route at about four minutes at the escorted car's pace, and puts its
ambush a third of the way. Measured (300 rolled jobs; the stock missions: the player's placement to the nearest enemy car, and between
enemy groups): first contact median 92 s before, 47 s after (stock 58 s); between events median 104 s before, 62 s after (stock 54 s);
over budget 49 % before, 0 after. Tatum Tail and Back to Spanner declare their own long budgets (a winding drive; a race).

### Places

Node tables may name places inside or under structures and places of note in the open (`sort`: fort, barn, yard, canopy, awning,
compound, overlook, spur, riverbed, underpass): `heading` (the way out), `inside: {object, nth}`, `out` (a point outside the car
drives to), `open: [{object, nth, nodes: [...]}]` (structure nodes taken away before the start), `remove` (placements taken away),
`friend: {vcf, at, heading}` (a friendly car parked there: Skeeter's van in a barn), and the kind `start`. Found with
`i76 remix structures BASE [--near X,Z,R] [--nodes]` (each placement's colliders and nodes) and `i76 remix roadsurvey BASE` (the
highest road points, the dead ends). `i76 remix check --nodes` builds a job started at each inside place and drives it out
(`RemixDriveOut`: full throttle steered to the way out; out within 14 m, on its wheels, never lifted over a wall); `i76 remix check`
drives out of every inside place a job uses. Shipped: T16 Fort Davis's yard (its gate's doors HF1_DRL1, HF1_DRR1 and arch HF1_GAT1
taken off: the doors hold a car in, the arch's box closes the gateway), T07's and T15's open barns (Skeeter's van inside), T01, T03,
T05 and T08's Sincere station canopies, T02's garage yard, T06's Gas Parade awning, T12's Komplex yard (its signed wall section
removed); in the open: T02's hairpins, T05's canyon rim and floor, T09's hilltop and west spur, T10's summit and south spur, T03's
underpass, T11's riverbed under the Pecos bridge, T15's mesa top. Left out: T07's livery stable (its door so tight a car jams), T06's
garage yard and T02's south spur (on ground the rest of the map cannot reach).

A description's `open:` is written as an actor bound to the placement, labelled `rx_open__NODE__NODE...`; the mission build
(`MissionSetup`) takes those nodes' colliders away before the first tick (`StaticObject.RemoveNodes`, `StructureNode.Removed`:
structural, from the mission file) and the game draws the placement without them (their 'A' wrecks too). Fort Davis's wall is one
box over the whole fort (the original's class-0x2000 rule): a car cannot drive back in from outside, gate or no gate.

### Busts

A roguetrip's job ends won, busted or with the player dead (`RemixRun.Classify`): failure line 1 or a death line (the lines after
the failures) or the car destroyed is a death and ends the run; any other failure (leaving the map, the job's own: the target lost,
the convoy through, too slow, the escorted car wrecked) or walking away (Abort) is a bust: the damage carries, no salvage, and the
next job's challenge is one step on (a bust in job 1 makes job 2 1.2). Four jobs played end the run, busts and all. A failed job
skips the end menu and goes straight to the run's debrief page in the shell (JOB DONE, JOB BUSTED or WRECKED: the line it ended on,
its objectives DONE / FAILED / open, the kills, the car, what follows); with the Developer setting on it has RETRY JOB (the same job,
the car as it began: `RemixRun.Retry`). The pause pad's Abort has no Replay in a roguetrip.

### Salvage between jobs

After a won job the trip's own FIELD SALVAGE and garage run on the run's inventory (`TripInventory`, kept in run.yaml): the job's
wrecks feed a salvage pool (no tier clamp since builder v7: "Rosters and salvage" below; the stock mission's scripted salvage, its .fsi,
left out, the job's prize in its place), the trip's thinning, its repair order (finished after the next job, as the trip's), the van. The
roguetrip's own: every part left in the field when DONE is pressed is scrapped for armour plate, a tenth of the car's full armour
each, the weakest side first, then the chassis (`RemixRun.Patch`); the guns are reloaded, as the trip's are. The next job's car is
the garage's (served from memory as vehrun.vcf, its parts' state from the inventory), its armour and chassis carried.

### Rosters and salvage

The owner, after a won job in a 595ci V-10 with turrets: "none of this salvage is anywhere near the level it should be for the number of
cars i fought and my current gear?" Builder v6's job (run 850764, job 3: T01's raid at challenge 1.4; `i76 remix salvage --run-file
run.yaml --legacy` plays it again headless) fielded 13 cars from the director's heat pools, whatever the player drove (2 to 7 levels
under the V-10 on the ladder below), and levelled their wrecks as the trip mission of the map: T01's clamp (level 2) walked every engine
down to the 261ci six, every suspension to Stock, every gun to the 30cal and every dropper to the oil slick, which also merged the 147
parts of 13 wrecks into ten groups that the trip's thinning cut to about 21 items, none above trip level 7. Builder v7:

- **The ladder** (`RemixRoster`, `src/I76.Formats/Remix/RemixRoster.cs`). Every base-game car file is ranked by its parts' trip levels:
  `PartTiers` is the original's salvage tier table (the trip level from which the original lets a part out of a wreck: the 261ci 1, the
  305ci 5, the 432ci 9, the 595ci V-10 13; Stock 1 to EtherX Rally 12; drums 1 to aircraft brakes 10; the 30cal MG 1 to the 25mm
  turret 14). A car's level is the mean of its engine's, suspension's, brakes' and best gun's (the wheels left out: every enemy rolls on
  stock tyres). 60 cars a job may field (not the player's Piranhas, helicopters, tanks, emplacements, scripted f-variants, gunless cars,
  the convoy leads or the police, which stay the director's own lists), levels 1.75 (valepre3) to 10.25 (vdlight2). The table is stems
  and numbers only (`i76 remix ladder --table` writes it from a stock install; `--check` and `RemixRosterTests` hold it to the install):
  the same on every machine whatever an install's ADDON holds; the Nitro Pack's own cars are never fielded.
- **The gear.** What the player drives on the same scale, per slot (engine, suspension, brakes, wheels, best gun; "E.S.B.W.G", e.g.
  the owner's 13.12.10.11.10, level 11.25): the parts in the run's car as its garage left them (the run file's inventory), else the car
  file picked (a garage-saved file under a new name as its model's first variant). Each offer carries it (`RunOffer.Gear`); the
  reproduction line adds `--gear` (`i76 remix roll --run N --job K --picks ... --gear E.S.B.W.G`).
- **The roster.** Each car the director sets on the player comes from the ladder around a centre at the gear's level: half a level
  under it at challenge 1.0, 2.5 levels on per 1.0 of challenge (0.8: a level under; 1.4: half a level over). 55 % are drawn up to two
  levels under the centre, 30 % up to one over, 15 % one to two and a half over (the ones worth salvaging); a heavier group (a roadblock,
  a late crew) a level on; the car among those within three quarters of a level of the draw, at least the six nearest. From the second
  job, or at challenge 1.0, the template's last car is the **lead**: about a level and a half over the centre (half a level more each job
  after the second), chosen to carry a part at or above the player's in some slot. The roster draws on its own SplitMix64 stream from the
  job's seed (the stock pick is still drawn), so a job's places, start and steps are the stock roll's; only the cars and how many differ.
- **The budget.** Back to Spanner's score x the challenge x the gear's scale: the ladder's line of strength on level, at the gear over at
  the melee Piranha's (the car the challenge was calibrated in), held to 0.7..1.5; the fill scores ladder cars at their measured
  strengths. Measured (`i76 remix salvage --survey 500`, jobs 1-4 at 0.8-1.4): the challenge against the player's own car (its measured
  strength) over the job's nominal, median 1.04 in the melee Piranha (v6 1.00), 0.86 in the trip's first Piranha vppt01 (v6 1.34: the
  stock pools set cars five levels over it), 1.11 in the owner's V-10 build (v6 0.74).
- **Salvage follows the fight.** Every destroyed enemy gives its parts as before (the wheels, engine, suspension, brakes, guns and
  specials, at their health after the original's component wipe: a wreck's parts arrive under half, mostly in the red). A roguetrip's
  pool has no tier clamp (`SalvagePool.Roguetrip`): the roster already set the cars at the player's gear, so a wreck gives what it
  carried. The trip's thinning is kept as the original's (each group by part name: tyres four, a special one, anything else 3, 1 or 2):
  it was the clamp's merging of every part into a few bottom groups that cut the field, not the thinning. The owner's job won in v7
  offers about 52 items from 14 wrecks (v6: 21 from 13), the best at trip level 14.
- **The floor: the prize.** A job at challenge 1.0 or over writes its lead car's part in the slot where it is furthest above the player
  (`prize:` in the description). A won job adds it to the field beside the wrecks, as a stock mission adds its scripted salvage (its
  .fsi), in the stock random condition except the quarter (full 5 %, three quarters 25 %, half 70 %): never in the red. It is the
  lead's own part; a job under 1.0 has none. Over 60 played jobs for each of three cars, every won job at 1.0 or over had a usable part
  at or above the player's gear in some slot.
- **The field's order.** The trip's FIELD SALVAGE lists each kind of part by its number then its health, the weakest first (the
  original's order, kept for the trip). A roguetrip's lists best first instead (by the part's trip level, then its condition), so the
  parts worth taking are the rows on show without scrolling, and a part that beats what the car has fitted for it (an engine,
  suspension, brakes or tyre over the car's; a gun or dropper over the car's weakest of the same kind, turret or not) has a "*" in
  the form's own hand before its name; the colour bands stay the part's condition.
- **Busts** give no salvage (as before). **Repairs**: the garage's repair order runs in a run as in the trip (finished after the next won
  job: the owner's run queued the 30cal turret at 99 of 200 after job 2 and had it at 200 after job 3). Parts left in the field are still
  scrapped for armour plate.
- **Co-op.** No field salvage in co-op, but the roster: the crew's gear is each slot's mean over the lobby's car files (a melee form's
  part changes are not counted), so the cars sit at the crew's average car while the crew's size sets how many come. It is a roster input,
  so it travels with the job (`CoopJob.Gear`), written at the end of `RoguetripState` (an older reader stops before it; a builder v6 game
  refuses a v7 job by its number anyway): **host and guest must both run builder v7**.
- **Tools.** `i76 remix salvage --run N [--picks A,Bb,C] [--car F.vcf | --gear E.S.B.W.G] [--legacy]` prints, per job of a run, the cars
  fielded (stem, level against the gear, strength, parts) and what a clean win offers (every hostile destroyed early, played headless,
  thinned over 200 draws: the count, the best level, how often a usable part at the gear comes, one draw's list); `--run-file run.yaml`
  measures the job a run file played last (`--write-run OUT` writes the run with that draw's field); `--survey N --players a.vcf,run.yaml
  [--sim K] [--csv F]` the distributions. `i76 remix check --car F.vcf` (or `--gear`) adds each job's roster against the gear and a clean
  win's salvage (about how many items, the best level, the prize). `i76 remix ladder [--table|--check]` the ladder.

### Reproducing a job

Every generated job's launch writes one line to the game log and (a roguetrip's) the run file:
`remix: RUN 004242 JOB 2/4 #637742 builder v5 map t07 (the farms on 380) template deliver challenge 1.2 car vppirna1.vcf commit
abc1234 (i76 remix roll --run 4242 --job 2 --picks 0,1b)`; RANDOM JOB's: `remix: JOB #N builder v5 map ... commit ...`. The pause
pad's free rows 9 and 10 show "RUN 004242 JOB 2/4 #637742" and "(t07 the farms on 380, delivery) v5" (both looks; the original's rows
untouched). `i76 remix roll --run N --job K --picks A,Bb,C` rolls that job again (a `b` after a pick that was busted). A run made by
another builder version says so on its choice screen; older versions are not kept (the director's code is the version).

### Loading, the map, finding the places

A generated job's loading card is its own briefing: its map on the left, its notepad's text (the objectives shown at the start, the
notes) on a paper column (`RemixLoadingCard`); PLEASE STAND BY stays in its corner. Its map (M) draws the whole map
(`ArenaSketch.Whole`: every road, paved solid, dirt dashed, a wash or planks dotted, a key in the corner), one or two landmarks (the
table's clusters and places of note nearest the job), the marks to make for double-ringed. The stock game has no objective marker on
the radar or a compass, so as each objective opens its line at the top of the screen adds how far and which way its place is from
the player then ("Bring them home, and hurry! (1.2 km northeast)"; the marks file's `STEP` lines).

### The choice map

The roguetrip's choice screen is the region (`RemixRegion`, Remix/Region/region.yaml: the state line, towns, highways as chains of
towns, the trip maps' places, all typed by hand from real geography) with the offers as numbered rings, the run's road so far in red
pencil, and beside it the offers and the chosen one's own map (its whole-map sketch and marks) and notes. Up, Down, 1-3 or a click on
a ring or a box choose; Enter or a second click takes the job.

Its layout (`RoguetripLayout`, game/Scripts/Shell; held by `RoguetripLayoutTests` over rolled runs on every map, each offer chosen).
The owner: "The atlas can take up the whole space and the options can be on scraps and they can show one at a time so there is space
for the mission detail map and notes/objectives".

- **The map fills the sheet**, edge to edge inside the frame's double rule (588 x 392 px): the region's latitudes fill its height, and
  the trip's maps are centred in the part of it west of the scrap (`RegionView`); the rest shows more of the country (the trans-Pecos
  and the Guadalupes west, Midland east). The region names minor towns and roads for that size (Lovington, Eunice, Jal, Andrews,
  Kermit, Monahans, Midland, Van Horn, Kent, Toyah; I-20, I-10, NM 18, US 180, 82 west of Artesia, 90; quiet ones named only where clear).
  Offer rings have a 10 px radius (the shown one ringed again at 14), the trip's maps are small rings, the run's road and the way to
  the shown offer in red pencil.
- **One offer at a time, on a page** laid over the map's east side (334-608 x 46-428), where no trip map lies, so it never covers a
  ring or the route (the test holds that for every run and pick). The owner: "The mission map can be bigger like the right half and the
  notepad/details can go on the top of that stuff so you can see the important stuff". The page is a scrap of the sheet's paper (torn
  edges, turned a third of a degree, a shadow: `ArenaSketches.DrawScrap`) carrying **the job's own map as large as the half allows**
  (the whole map's sketch at 260 x 368 px; its marks ringed in red, 6 px, their words in printed capitals 9 px high, clear of each other,
  of the rings and, where they can be, of the job's routes). On it lies a **notepad slip** (whiter paper in the modern look, ruled in
  pale blue with a red margin, as the notepad the objectives are read on; `RoguetripLayout.Page`): the run's line small at its head beside "1 of 3" in red,
  the title and challenge in the form's big hand, the offer's words, the job's other objectives shown from the start and its notes,
  tightly set, every word whole (the help line is in the sheet's foot, left of the buttons). The slip goes where the map carries nothing that matters: of six widths
  (the page's down to 45 % of it, wrapping longer: a tall map at the page's height leaves a narrow side free) and every place on the page (the corners and edges first, then a 12 px grid), one that covers no ring, no mark's word and no route,
  covering the least of the sketch's ink; the map is first tried where it is drawn, then pushed to the page's foot, top, right or left
  (a wide map leaves room above or below it, a tall one beside it). Only where nothing is free does the map give up a band at the
  page's foot for the slip (none of 157 rolled offers: the drawing is also tried at 88 % and 76 % of the page, pushed aside, to free a column). Up and Down, Left and Right, 1-3, a click on a ring or the pad's focus on one
  change the offer shown; Enter or TAKE THE JOB takes it. In co-op the guest's page follows the host's highlight and says the host
  chooses. `I76_ROGUE_NOTEPAD=beside` lays every page out the other way (the slip always in its band under a smaller map), for comparing.
- **The job's map, lettered once for all three views** (the page above, the loading card and the map, M): one routine places its words
  (`RoguetripLayout.JobMarks`; `MissionPicture.LabelPlan` for the card and the map). Marks within a ring's width of each other share
  one ring ("START / HOME"; a long set of words each on its own round it); each word goes beside its ring (right, left, above, below,
  the corners, nearer first), on no other word, ring or landmark's square and off the route between the marks (a crossing costs more
  than any distance), as little on the roads and canyon walls as it can; set further out it gets a short leader to its ring. The
  landmarks' names come last and yield (one with no room left is left out). On the card and the map every word has a halo of the
  paper, so the route and the roads run under the lettering. The owner's screenshot (run 11, job 2: a T02 convoy hit packed into a few
  hundred metres, EXIT, START / HOME and CONVOY in a row on the route) was the case: its words piled over each other and the next ring.
- **Words placed, none on another.** Each town's name, highway number and state name is printed beside its mark as the stock forms print
  their labels (Arial Bold capitals, 7 px high, in the small sans's cell): the four sides of the dot, then the corners, then further out,
  the first place that lies inside the sheet, off the scrap, on no word or ring, at least 2.5 px from other words and as far as it can
  from other towns' dots, crossing the fewest lines. The named towns west of the scrap, the through highways' numbers and the states are
  always placed; a quiet town's name only where it touches no line, a short spur's number only where there is room.
- **Drawn at the screen's resolution.** In the modern look the lines, dots and rings are a layer of their own (`RoguetripMapLayer`),
  antialiased at any window size (3440 x 1440 included), and the printed words go to the text layer. The 1997 look draws the same plan
  into the 640 x 480 pixels.
- **Written by hand, printed by type.** What a person wrote on the form is in its hand (font 0x44, as the stock forms' entries): the run
  line, the offer, its notes. What is printed is in the forms' printed lettering: the title, the buttons, the map's words, the job map's
  marks.
- **The stock forms' lettering.** The title (ROGUETRIP, JOB WON...) is set as the scenario form's AUTO MELEE · DRIVER ENTRY FORM is
  (Arial Bold at its height, `ArtButtons.Letter`), and the buttons are that form's ENTER AREA button, resized, lettered as its label is,
  in both looks.

**The car pick** (the scenario form) says what it is: the list's header SCENARIOS is lettered ROGUETRIP on its bar, and "Lightest car to
win:" (a scenario's record) is "Drives every job of the run:", with "its damage carries / from job to job" in the form's hand under it,
each word lettered as the art's own (no new art).

**A road atlas under the region (optional).** The drawn map stands alone; a player who has a scanned road map of the country may lay it
under the region: put the picture in the user folder's `remix` folder as `roguetrip-atlas.png` (or `.jpg`, `.webp`) with
`roguetrip-atlas.txt` beside it, the picture's control points, one a line: `latitude longitude x y` (degrees, north and east positive;
the picture's pixels from its top left; `#` starts a comment), three or more, towns whose dots are easy to find. The game fits an affine
map from latitude and longitude to the picture by least squares (`RoguetripLayout.AtlasFit`; the game log says how near the fit comes
to its own points), resamples the sheet's part of it off the main thread (4 texels a pixel; a 7000 px scan takes a second or two),
takes its paper (the brightest tenth's level) to white and fades its edges over 6 px (and where the scan ends), and keeps the result
beside it (`roguetrip-atlas.fit2-*.png`, named by the picture's size and time, the size and the points), so a later session reads that
at once. It is laid
on the sheet multiplied, as print on paper (the sheet's paper shows through; the 1997 look matches the product to the palette), under
the trip's maps, the run's road and the offers, which get a halo of paper (the rings' insides cleared) so they read over its roads;
the region's own lines, towns and words are then left to the atlas. Until the atlas is ready (being read), and without it or when it
cannot be read, the drawn map shows; the screen draws itself again the moment it is ready. Nothing of an atlas ships.

### Template `ambush`

The owner: "there are some opportunities for us to be the ones ambushing instead of the creepers". The player starts at a place
by a route (`AmbushSpots`: a table's start or node, places of note preferred, 40-450 m from a route point with at least 700 m of
route before it and 400 m after); a convoy (a lead and its escorts, the intercept template's machinery) parks about 1100 m up the
route and rolls five seconds in, unaware: it keeps its pace (16-20 m/s) and is alerted only when the player comes within 120 m or a
shot or a death in it gives him away, then the lead runs at its alert speed and the escorts turn on him. The first objective says
when it passes ("in about a minute"); objective 1 the lead dead, 2 the escorts, then away to a hideout or home nearby with nobody
behind, while the gang's backup comes in. The convoy through is failure line 3 (a roguetrip's bust). Measured over 150 rolls: it
passes the player 71 s in (median; 60-82 s quartiles), the away leg 46 s.

### The radio

A radio clip may be a cut: `STEM@START-END` in milliseconds into the clip (`SoundFiles.Cut`; faded in over 12 ms and out over 35 ms;
its length is the cut's for the CB queue). The incoming-enemies cue is Groove's "Yeah, I dig. Dig incoming bandits." cut to its last
two words (`06sgc15@1625-2895`, from the splice work's alignment): the owner rated the whole stuttering line and Taurus's panicked
takes bad; the good-rated "More creepers coming from the east." is Nitro Pack words spliced and names a direction a job does not
know. A description's `radio.words` gives each clip's words: an install without the clip letters them instead.

## Roguetrip in co-op

The owner, playing co-op with a second player: "can we get the RogueTrip on the coop melee form please and play that together?". A
roguetrip played by a crew of one to eight in a co-op lobby game (`CoopRun`, `CoopJob` in `src/I76.Formats/Remix/RemixCoop.cs`; the
server's `NetMeleeServer.Roguetrip.cs`; the game's `MainNetRoguetrip.cs`, `RoguetripScreen.Coop.cs` and the lobby's ROUTE MAP).

- **The way in.** The co-op host form's AREA OF PLAY list (base game) ends with **ROGUETRIP** (its stem `xcoop`, the generated job's).
  BROADCAST GAME hosts it as a co-op game with a lobby (`--host-listen --coop xcoop --coop-lobby` on the command line). The host's run is
  `user://remix/coop-run.yaml` (the run, the crew's cars by name, the route map's pick, the last debrief), apart from the single
  player's `run.yaml`: a run in progress goes on when ROGUETRIP is hosted again, else a new run number (`I76_RUN_NUMBER` for a capture).
  Host migration is off for it (the run is the host's). A dedicated server does not host one (a design for it: "A dedicated ROGUETRIP").
- **The route map.** In the lobby the GAME SETUP button is ROUTE MAP (everyone's; R): the run's choice sheet (the region, the offers,
  the chosen offer's own map) drawn from the host's run. The host's Up, Down, 1-3 or a click highlight an offer for everyone; Enter or
  TAKE THE JOB goes back to the lobby, where START plays the highlighted one. A guest's sheet follows the host's highlight and says the
  host chooses. The debrief, and the run's end, open the sheet by themselves on every game; the host's CONTINUE (Enter) goes on to the
  next choice, and after the run's end DONE starts a new run for whoever is in the lobby.
- **The job on every machine.** At START the server takes the highlighted offer (`RemixRun.Take`) and rolls it for the players in the
  lobby (`CoopJob`: the run, the builder's version, the job's index, the map, the template, the seed, the challenge and the crew's
  size), builds it into the generated missions' folder and builds the game again on it. Before each join accept it sends
  `RoguetripState` (the play fork's message 66, only to a peer whose join says `PeerFeatures.Roguetrip`): the run, the offers, the pick,
  the job and the SHA-256 of its build (mission, text, marks). The client builds the job itself from those numbers and checks it: a
  job of another builder version ("made by builder v5 and this game's by v6") or another hash ("differs from the host's (host build
  abc1234, this build def5678)") is refused before anything is built, and the accept's mission fingerprint (the original's JO check) is
  checked after it. The mission is never sent (`MapFileSet` is off for it): it is derived from a stock mission, the inputs are a few
  numbers, and each machine needs the build's text and marks anyway (the notepad, the map, the loading card). A peer without the bit
  is turned away at the join ("your build of I26 cannot build its jobs"). The protocol version is unchanged (26), as for the fork's
  other additions; an older fork build still joins stock co-op games.
- **The crew.** Each player in the car picked in the lobby (or the mission's own), seat 0 the host's, as a co-op mission's. Each
  player's car carries its damage between jobs by name (`RunCarry`: armour, chassis, parts, guns) while the player keeps the same car
  file; another car, or a player new to the run, starts as built. **Killed in a job, a player is out of it** (no respawn: the seat's
  respawn delay is `Protocol.CoopNoRespawn`, the death card says "Out of this job: back for the next one") and comes back for the next
  with every armour and chassis side at least half of full (`CoopRun.Floor`), the parts and guns as built. The script's "player dead" is
  the whole crew dead at once (co-op's rule), so the run ends only when everyone is killed; any other failure is a bust, as single
  player's (`RemixRun.Classify`). The host's Back to Lobby during a job walks away from it (a bust). A job is played once: no Replay.
- **The enemies grow with the crew.** The cars are drawn around the crew's average car ("Rosters and salvage"). The job is rolled at its challenge times 1 + 0.75 a player after the first (`CoopJob.Scale`: two
  1.75, three 2.5, four 3.25); the director's fill adds cars to its groups (14 at most). Measured at challenge 1.0, one job of each
  template: 5 to 9 hostile cars for one, 7 to 11 for two, 11 to 14 for four (run 4242's first offer, an ambush at 0.8: 5 and 10).
  Co-op Scaling (EXTRA ENEMIES) still copies on top when the host sets it.
- **Between jobs: the salvage.** A won job's wrecks are one shared field (the server's `SalvagePool.Roguetrip` from the job's dead
  cars, the players' own left out, plus the template's prize; each part's condition rolled once on the host, so every machine sees the
  same part at the same health). Past the debrief (the host's CONTINUE) every game opens the FIELD SALVAGE screen, the single
  player's, on the player's own garage with the field laid in (`NetMeleeServer.RoguetripSalvage.cs`, `GarageScreens.Coop.cs`). Picks go
  in turns round the crew (`RoguetripDraft`): the turn is the player with the fewest turns had who is still picking, the host first on a
  tie; a click on a field part on your turn takes it (into your van, on the host), and it is gone from everyone's list at once; 20 s a
  turn, then it passes; ACCEPT SALVAGE ends your picking. The line under the lists says whose pick it is and the seconds left. The car's
  own parts cannot be scrapped in co-op. When everyone is done (or the field is empty) each player's GARAGE (the single player's) fits
  and repairs their own car; DONE sends the garage to the host, which checks it as a join's car and makes it the player's lobby car (a
  configuration served by its content). 150 s for the garages together, then the route map. What nobody took is scrap: each surviving
  car gets the plate of an equal share of it (`CoopRun.Plate`: a tenth of full armour a part, `RemixRun.Patch`'s way), or 30 % of its
  full armour if that is more (the pass-one patch, kept as the floor), and its guns reloaded. A bust has no salvage and carries the
  damage as it is. After each job everyone goes back to the lobby (`CoopPlan` Lobby after the hold), where the debrief shows: how it
  ended, the objectives as they stood, the crew's cars.
- **Garages by name.** The host keeps each player's garage (the trip's `TripInventory`) in `coop-run.yaml` (`inventory`, and the
  install car it was made from, `model`). At each job's end it takes the car's parts as the job left them (a car killed keeps its garage
  and comes back at the floor). The next job seats a player in their garage's car, its parts as the garage left them, then the armour
  carried. A player who chooses another car in the lobby starts a new garage; a resumed player (by name) gets their garage's car back as
  their lobby car when they join; a player new to the run starts in their own car as built. A game closed during a salvage loses its
  field (the resumed run goes to the choice); the garages are kept.
- **The wire.** Behind `PeerFeatures.Roguetrip` (protocol still 26): 67 `RoguetripSalvage` (server to each lobby player: the field
  with each part's taker, the crew's turns and marks, whose turn and its seconds, the garages' clock, that player's own garage), 68
  `RoguetripPick` (take, pass, done), 69 `RoguetripGarage` (the garage). Host and guest builds must both have them; a build without the
  bit is already turned away from a ROGUETRIP game.
- **Coming and going.** A player who leaves mid-job does not end it (co-op's `CoopLeave`); their car keeps what it carried into the job.
  Joining between jobs is joining the lobby; mid-job, a late joiner comes in as in any co-op mission, in a fresh car, having built the
  job from the state sent before its accept.
- **The templates with several seats.** Every test a template makes on the player is the co-op VM's (docs/multiplayer.md "What the player
  means"): a zone reached, an ambush's 120 m, an escort's 220 m wait and 110 m go-on, a raid's escape and a defence's arrival hold for any
  living human; a negation (nobody near, the convoy unalarmed) for none; a death line for the whole crew. `RemixCheck.Walk` plays a job
  through (each step's place, a raid's targets, every hostile car killed once out) with a crew of one, two and four, the others parked
  at the start: every template wins with each (`RemixCoopTests`), no template fails a crew idling at the start for anything but their
  deaths, one of two killed goes on and both killed is a death.
- **Tests.** `RoguetripCoopTests` (Net): the state's round trip; the host's and a client's builds of every job equal byte for byte and by
  hash (113 jobs: 12 runs, the first two jobs' offers, crews of 1, 2 and 4); another build refused naming both; a build without the bit turned
  away; two players through two jobs with the guest's damage carried and a salvage draft between them (the field equal on the host and
  both clients after every pick, a pick out of turn refused, the plate); the draft's turns, timer, READY and leaver (`TheDraftGoesRoundTheCrew`);
  a run resumed after a salvage with both garages and an engine fitted; a leaver mid-job, one killed then the whole crew killed ending the
  run and a new run; a run saved after a bust resumed by one player with the host's carried car.

### A dedicated ROGUETRIP (designed, not built)

The rule: on a dedicated server the **lead** chooses: the first player in the lobby (the earliest join still there; when the lead
leaves, the next earliest). The lead's route map is the listen host's (highlight, TAKE THE JOB, CONTINUE, DONE for a new run) and the
lead's START starts the job; the salvage draft's tie-break is the lead first. What it needs: `i26-server --coop xcoop --roguetrip DIR`
(the run file in DIR, `PrepareRoguetrip` as now); a client-to-server `RoguetripChoose` (select K, continue, new run, start) honoured
only from the lead, behind the same bit; `LobbyState`'s host byte (or a new field) naming the lead so the lead's game shows the host's
controls and the lobby's START, while kicking and the game's settings stay off for the lead; and the ROGUETRIP row's capture hooks on
the lead instead of a listen host. Not built in this pass: the lobby's START and the host's buttons are tied to the listen host in
several places of the lobby screen and the server's lobby, and a stand-in for the host there is a change of its own.

## Spoken lines

The owner: "Eventually we could try to remix the audio files to say different things", "would prefer to use audio recorded from the
same game when possible", lines short and mostly nameless. A generated job's radio moments say new lines recombined from the game's
own recorded dialogue (Taurus, Jade, and Groove where the stock game has the player speak), rebuilt at run time from the player's
install. The splice work (outside the repository) cut and joined the takes and the owner rated them over five rounds; 116 lines are
in the table (its round-five set): 104 the owner heard and rated good, 11 rebuilt or reworded since from heard pieces but not heard
whole ("unheard"), 1 okay.

**What is in the repository** (`src/I76.Formats/Remix/Voice/lines.json`, embedded; `RemixVoice`): each line's text (ours: the
sentence we composed, lettered on screen), its voice and rating, the radio moments it may be said at and what must be true of the job
for it (its needs), and its recipe in numbers only: per piece the source clip's stem and game (`i76`, `nitro`), the cut's start and
end in samples at 11025 Hz, where it lands, its gain, fades, an optional high shelf at 1 kHz (the tilt), an optional pitch-keeping
stretch (WSOLA) and an optional EQ match of a Nitro Pack take toward the 1997 session ([Hz, dB] points, a level, a seeded noise
floor); per line its length and level; once, the CB treatment (320-3200 Hz, a 3:1 compressor, tanh drive, the static bed and the
squelch bursts as cuts of clips) and the CRC-32 of every source clip's 8-bit PCM. No audio, no transcript of an original line, no
ratings. Two recipe options replay a line exactly as it was first heard: the fades before the match filter, and silence from a
sample on. `tools/remix-voice-table.py APPROVED.json` writes the table from the splice work's approved set; the moments and needs of
each line are written in that script by hand (a line it does not list stays unused).

**Rebuilt as the job loads** (`game/Scripts/Assets/RemixVoiceBank.cs`, the DSP in `src/I76.Formats/Audio/SpliceRender.cs`): the
lines the job names are rendered on a worker (their clips read first on the main thread), about 0.1 s for a job's nine; the radio
plays them by sound name (`rxvt108`: rxv, the voice's letter, the number). A clip whose checksum differs (another release or
language) or that this machine lacks makes its line silent: its text is lettered all the same. Lines of a Nitro Pack voice play only
with the Nitro Pack beside the base game (`I76_NITRO_INSTALL` or the places the game looks for the other game); 69 of the 116 build
from the base game alone. The squelch bursts and two of the three static-bed cuts are Nitro Pack clips: without it the bursts are
left out and the base game's bed cut loops alone. The renderer is held to the splice work's reference renderer (`RemixVoiceTests`):
the plain line sample for sample, the radio within 1.5 dB in every 50 ms (its hiss and the match's noise floor are our own seeded
stream).

**The simulation** sees a line only by its name and its length, the table's (both bursts and their pads counted, whatever this
machine built), so a job plays the same with or without the audio. In a co-op roguetrip every machine builds the job, and with it the same line plan
(the job seed's): each builds and letters the lines itself as the server's radio starts them, with nothing new on the wire; a machine
without the clips letters them only. (The notepad and the objectives' lettering stay single player's.)

**On screen**: as a line goes on the air, its speaker and words are lettered under the objective's line at the top of the screen
("Taurus: Get over to the oil field.") for its length and two seconds more; Groove's own lines are said from the player's car, so the
CB handset comes up as for the stock lines he speaks.

**Which line where** (`RemixVoice.Plan`, called by the director after a job is filled, on a stream of its own from the job's seed: the
rest of the roll is unchanged). Each template's radio moments (start, contact, each step, reinforcements one by one, counts, the clock,
the convoy's getaway, a defence's last structure, the win) state what is true of the job then: the place's kind (`place:oil field`,
from `Voice/places.yaml`: a node table's start, node, cluster or route end tagged by hand only when it IS that kind of place, "the
Pecos bridge on 380" but not "the riverbed north of the bridge"), `rendezvous` (a home or hideout node away from the start), `clear`
(a tail lost, a finish with nobody near), `plural` (two cars or more), `police`, `convoy`, `lead:truck` (a milk truck), `count:2/3`,
`left:120` (two minutes on a clock of three or more), `from:east` (a reinforcement's entry east of where the player is then), and
so on. A line is said only where its moment is one of its own and every need is a fact; the owner's good lines first, then the
unheard ones, an okay one only where neither fits; never one line twice in a job; a moment no line fits keeps its stock clip of that intent, or none.
New radio moments the templates gained for it: a reinforcement's own line (its first car says it), a count as the group reaches it
("Two down, one to go": a raid's targets, a convoy's escorts after the lead; three or five), the clock (two minutes and 45 seconds
before a deadline, an escape's or an arrival's time runs out, while that step is open), the convoy's lead alive within 600 m of its
way out, a defence one structure from lost; none after the win.

**Checked**: `i76 remix voices --seeds 1-300` rolls 300 jobs across the sixteen maps and holds each chosen line to the built
description itself (`RemixVoiceCheck`: the moment's zone near a place of that kind, the group's real size, the lead's car, the
clocks), and scans each line's words (a place, a road, a direction, a count, a name) for a need that makes it true: 0 problems.
About nine in ten radio moments get a spoken line; place lines are rare, as few places are a kind a voice can name. Lines never said
(they name what no job has: an airfield, Skeeter, civilians, a tanker, 385) are listed with the reason by `i76 remix lines`.

The round-five radio cut (`06sgc15@1625-2895`) never reached the player: the CB queue cut every name at 15 characters, its span
with it. A cut's name is now kept whole (`CbRadio.Queued`).

## Boundaries

The owner: "careful that we don't end missions by being CLOSE to the boundaries when we start them close to the boundaries". Every way a
generated job can end by where a car is (`RemixBounds`, `src/I76.Sim/Missions/RemixBounds.cs`):

| What | Where it comes from | Kept in generated jobs | Test, radius | Outcome |
|---|---|---|---|---|
| Leaving the map | the stock leave-map library routine, one thread per exit (`exit1`, `exit2` ...; the `exitNa` paths are the drive-off, not watched) | every template, every map (T01 2 exits, T02 4, T03 2, T05 7, T06 4, T07 1, T08 4, T09 2, T10 5, T11 6, T12 4, T13 4, T14 4, T15 2, T16 2, T17 1), less a description's `drop_exits` | a square on the exit path's first point (isWithinSqNav): CB warnings at 275 and 175, the mission failed at 50 (`failAllObj(6, 2)`), then the car driven off along the exit | failure line 2 ("You can't just leave."): RANDOM JOB and the scenarios fail; a roguetrip's job is a **bust** (`RemixRun.Classify`), its debrief saying the line. Co-op: that player is put back beside a teammate (the routine per human); the job fails only with nobody to come back to |
| The map's edge | the engine (the original's: docs/deviations.md "Off the map") | — | no rule; off the map's tiles the ground falls to 0, and a drop of more than 35 m kills the car | a death (single player: the run's end; co-op: out of the job). Most edges are walls the car cannot climb |
| A convoy through | intercept / ambush template | yes | the convoy's lead within its exit's radius (120) | failure 3: a bust. The convoy's position, not the player's |
| Led them home | tail template | yes, now only once the hideout is revealed (objective 2): before, reaching an unrevealed hideout with a pursuer near failed the job | at the hideout with an enemy within the lost radius | failure: a bust |
| Escape, way out, home, finish | raid, deliver, intercept, ambush, escort finish | yes | counted only once its step is open (the finish `finish_after` seconds after) | a win |
| A leash | none: the generator adds none (T17's stock leash is a mission thread, dropped) | — | — | — |

**What was found** (`i76 remix bounds --seeds 1-2000 --grid 5 --places`, 2,549 jobs: 2,000 rolled, 5 of every map's every template,
every start and place of note as a forced start; each measured with a co-op crew of eight, the routine's own square measure):
- before: 309 jobs (12.1 %) with a start, seat or first drive inside a leave-map warning, or a 200 m wander off the first drive into the
  fail; 282 (11.1 %) packed (below). The cause was the node tables' convoy roads, which ran past exits into the drive-off stubs: T02's
  crossroad road ran from beyond its east exit (the convoy parked 350 m past it) to beyond its west exit, T05's 380 began past its
  east exit, T14's two western and northeastern roads, T10's road through Tatum passing two exits, T05's spur south of the bend on an
  exit road; a few starts and places 265-348 m from an exit (T14's northeast) or at the map's edge (T17's northwest, T01's farm road).
- after: 0 of 2,539 at risk (the grid lost T05's ambush and T17's intercept, whose only roads ran by an exit or past the only starts);
  packed 12, every one a forced start set down at the job's own pickup (the measure's, not the director's). A co-op run's first
  offers for two players (1,500 runs, 4,500 jobs): 698 (15.5 %) at risk and 528 packed before, none after.

**The rules** (version 7 of the builder: run and job numbers roll other jobs than before; in co-op host and guest update together):
- **Margins** (`RemixBoundsTable`; `i76 remix check --nodes` and `RemixBoundsTests`): every start, place, cluster and escort road's ends
  **350 m** from every exit's trigger on the routine's square: the first warning (275) plus the farthest seat of a crew of eight in
  formation (52 m, measured over 2,549 starts) and a little; every convoy road point, its parking and its end **300 m**, so a chase
  along it, and a 200 m wander off it, never reaches the fail (50); starts and places **100 m** from the end of the map's tiles. Tables
  broken by them were put right by hand (roads trimmed inside their exits, places moved 15-30 m and T14's northeast start down its road, T10's road through Tatum and T05's
  spur south of the bend dropped).
- **A job** (`RemixBounds.Problems`, in `i76 remix check` and the tests): the start 350 m from every exit; every seat of a crew of eight
  outside the first warning; every place the player must reach 300 m; the drive to the first event (along the roads, its first 1.3 km)
  outside the first warning, and a 200 m wander off it toward the nearest exit short of the fail; the start and the seats 100 m from
  the map's edge.
- **The outcome**: near is a warning and nothing more; leaving is the stock routine's own gate (50 m of the exit's trigger, after its two
  warnings), a bust with its line. The trigger was not moved outward ("only by actually crossing out"): the exit paths' direction does
  not say which side is off the map (T06's exit3 path runs north into the map), so the original's gate is kept. In co-op the rule is per
  player (put back) and ends the job only with every living player gone, as for every co-op mission.

## Spacing

Round five holds a job's drives to a most (its pacing budget); `RemixSpacing` holds them to a least, so a job is not over in seconds or
its marks told apart on the map. The stock trip missions' own spacing (`i76 remix pace --stock`): first contact median 58 s (as low as 7 s,
T12's tanker, and 14 s, T14), between enemy groups median 54 s (as low as 15-23 s). The floors: any two of a job's marks **350 m** apart
(a place the job sends the player back to, a home, an escape or the finish near the start or the goal, is near by design and not
counted), the first event **350 m** from the start (round five's start rule, which a fallback could break), and an intercept's start
**250 m** off its convoy's own road (the convoy would drive into the player at once: the owner's run 850764, job 2, whose start sat on
T02's crossroad road between the convoy and its way out, all four marks in one row). The director now picks an intercept's road and
start together so that one is clear of the other (`RemixDirector`, "intercept"). `i76 remix check` fails a job under a floor;
`i76 remix bounds` prints the at-risk and packed jobs and counts.
