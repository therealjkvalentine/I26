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
4. Written to the output folder: `STEM.msn`, `STEM.npt`, `STEM.yaml` (the merged source, for reading) and `STEM.remix`, the
   stamp: the generator's version, the description's hash, the base file's hash. A build is redone only when the stamp differs.

The game builds the shipped descriptions at start-up into `user://remix` (about 20 ms when current, about 0.2 s the
first time), points `GameInstall.UserMissionDir` there (missions are found by name after a downloaded map's folder and before
the install's own; their `.npt` is found by name, adding names only) and lists them on the base game's SCENARIO form after the
seven stock scenarios. Base game only: every shipped description uses an Interstate '76 map.

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
gates: {start: [gate1], trap: [gate1], win: [gate1, gateblock]}   # gates triggered at the start, the trap moment, the win
radio: {start: [...], trap: [...], wave: [...], win: [...]}       # CB clips (sound file stems of the install) at those moments
objective: {template: defend | deliver, ...}
text: {objectives: [...], failures: [...]}   # the .npt: up to 6 objective lines ("(hidden)" = revealed later), the failures
```

A name in a template's parameters is, in order: a `cars` or `bind` entry, or the base script's own actor key (`enemy10`,
`cop4`, `gate1` as `i76 fsm-decompile` names them). A place is a `places` entry or a stock path label (`jump1`, `behind1`).

Failure line 1 is always the player's death and line 2 leaving the map (the stock leave-map routine shows line 2); a template's
own failures follow. Every template ends the way the originals do: objectives marked, the win radio, then `successAll` once
the CB is quiet.

### Template `defend`

Nitro P11's farm defence, written fresh. Attackers wait off the map and start in **waves** (`waves: [{cars, at_seconds,
at_kills}]`: a car starts once its wave's seconds from the start have passed and its kill count is reached); each teleports to
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

## The shipped scenarios

| Id | Listed as | Base | What |
|---|---|---|---|
| `x07farm` | 380 Farm Raid | T07 | Six raiders in three waves (25 s; at 2 and at 4 kills) come for the three farmsteads T07 places between its roadblocks, by T07's own wave entries (`jump1`, `jump2`) and two from the north and the ridge. T07's east fence opens at the start and slides shut behind the player at the farms; the west roadblock opens at the win. |
| `x05grab` | Grab and Go | T05 | Out east to the wreck (a zone), back to the start with four pursuers teleported in behind and 175 s on the clock; T05's guards, roadside ambushers and pincer pair wait where T05 parks them. Home with nobody within 300 m. |
| `x14back` | Back to Spanner | T14 | T14 driven backwards: from the county line up 285 to Spanner's truckstop past T14's police, which wake as the player passes (sirens on); the truckstop's cop and SWAT vans come out to meet him. Home with nobody within 400 m. |

## Playing them

- **In the game**: MELEE, AUTO MELEE, SCENARIO: the three are at the end of the list (scroll down); pick a car and ENTER AREA.
  `--mission x07farm` (with `--drive`) starts one directly.
- **Headless**: `i76 remix build --builtin --out DIR`, then `i76 --remix-dir DIR run x07farm 300`. `I76_REMIX_DIR` stands in
  for `--remix-dir`. The run's drivers (`I76_RUN_AIDRIVE`, `I76_RUN_ROUTE`, `--player-at`) take the generated places by their
  `rx_` labels; `--kill NAME@S` and `--wreck NAME@S` (every node of a structure shot through, gunfire's way) script the
  player's fights.

## Saves, determinism, the net

The generator touches nothing in the simulation: a generated mission is a mission file the sim builds and runs like a stock one.
Runs are deterministic (the script's `rand` is the session's seeded stream), and a snapshot restores into the rebuilt mission
(tests: `RemixMissionTests`). The session's install identity holds the mission file's size and CRC, so a save of a remix
restores only into the same build of it; editing a description makes old saves of it refuse to load. Co-op and network games
do not offer generated missions (the co-op list takes stock missions only, and a client would need the file:
`MapFileSet` sends melee arenas only).
