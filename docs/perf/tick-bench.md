# The tick bench: the simulation's tick timed headless

The owner's budget: the whole frame in 5 ms (200 fps) on the next machine, about twice this desktop's single-core speed, with
the sim fitting in it comfortably on the frames where a tick lands, not only on average. This note is the instrument for the
sim's share and what it found. The threading plan's section 2.8/2.9 assessment uses its numbers ([threading-plan.md](threading-plan.md)).

## The instrument

`i76 tick-bench SCENARIO [--rate HZ] [--minutes 5] [--split] [--warm] [--hashes FILE] [--ticks FILE] [--cpu MASK] [--slow-ms 1]`
(`tools/I76.Cli/TickBench.cs`; needs `I76_INSTALL`).

| Scenario | What runs |
|---|---|
| `t01`, `t05`, `t06` | the mission from its start, nobody at the wheel, as `i76 run` plays it; ticked on past the mission's end (T05's fights come to the parked player) |
| `melee8` | M01 as a network melee of eight bots, the server's session (`MeleeHost`): eight computer cars fighting and respawning for the whole run |
| `resim` | `melee8`, and every 5 ticks a client's re-simulation burst: 8 predicted ticks (`SimSession.PredictTick`) of one bot's car from a capture, put back after; the burst is timed apart from the tick |

- **Timing.** The whole of `SimSession.Tick` (nothing of the melee host's own bookkeeping), from the `TickProbe` hook's
  outermost mark. The process is pinned to the last physical core (`--cpu c0`: logical processors 6 and 7) at high priority.
  The report gives the mean, median, p95, p99, p99.9 and worst tick, and the counts over 1, 3 and 5 ms.
- **Allocation** inside the tick only (`GC.GetAllocatedBytesForCurrentThread` at the tick's two marks), its steady median
  after the first 10 s; the collections that began inside ticks, and their pauses.
- **Every slow tick** (over `--slow-ms`) is listed with what it held: the collections and their pause, the methods compiled on
  the tick's thread and their time (`JitInfo`), the bytes, the cars and rounds, the sections, and what the script logged.
- **`--split`**: the exclusive time and bytes of each section a tick (`TickProbe`: controls; the AI's order machines; the
  collision pass; the cars' updates, i.e. physics substeps; the script, less the AI; weapons and rounds; damage packets; the
  rest is the tick's own bookkeeping). The marks cost a little: compare split runs with split runs.
- **`--hashes FILE`** writes the StateHash after every tick and times nothing (the hashing allocates). Every optimisation on
  this branch was checked by running all fifteen scenario x rate combinations (20, 24 and 60 Hz) of 5 minutes and comparing
  every tick's hash with the baseline build's: 156,620 ticks, all equal, for each commit.
- **`--warm`** compiles the sim's code before the first tick (`SimWarm`, below).
- **`--alloc-types`** counts the runtime's allocation samples on the tick's thread by type.

`TickProbe` is a static hook in the sim, null in the game and in tests (a load and a test per mark). The sim reads no clock;
the bench's timer does.

The machine is shared: other lanes build and capture, and it was 20-100 % busy during these runs (the bench itself is about
12 %, one logical processor of eight). Every figure here is from runs of the two builds interleaved, and each run's load is
in its file. Absolute times move by 10-20 % with the load; the ratios and the allocation (which is as deterministic as the sim)
are the reliable part.

## What the slow ticks are

Before this work every 5-minute run had a worst tick of 70-110 ms and a p99.9 of 2-11 ms. Attributed headless, tick by tick:

| Cause | Size | How often | What was done |
|---|---|---|---|
| **The JIT: a path's first run** (the first tick; the first shot, kill, path search or respawn of a mission) | first tick 60-105 ms, later 2-25 ms | the first tick and a dozen more a mission | `SimWarm` (opt-in): worst tick 85-105 -> 17-24 ms; see its cost below |
| **A respawn or regen pad** rebuilt the car from the archive inside the tick (ZFS reads, LZO unpacking, the cockpit model assembled to find the handgun arms) | 7-23 ms, 1.2-1.7 MB allocated | 14 in 5 min of the melee | `CarFiles`: the mission's car files read once; median respawn tick 9.75 -> 1.42 ms |
| **A young-generation collection** begun inside the tick | 0.4-8 ms (12-13 ms with gen 2) | 16-170 in 5 min, with the tick's garbage | the tick's garbage cut 4-10x (below); none at all would need the rest of the work listed under "Not done" |
| **An AI path search** (100 expansions of the planner's A*) | 1.6-4 ms, about 235 kB | a few a minute in T06 | none kept (below); pooling its nodes would take most of the bytes |
| **The first build of a car file mid-game** (a bot picks a car not yet loaded) | 13-15 ms | once per new file | it reads the archive once; warm-up or a load-time read of the melee's car list would remove it |
| **The machine**: a tick preempted on a busy core | 8-19 ms in one random section, no collection, no compile | a few in 5 min at 80-100 % load, never at the same tick twice | none in the sim: it is the other lanes' load |

The game saw the same kinds (the threading plan's 3-9 ms ticks, once 23 and once 44 ms): a respawn or a first-time path in a
fight, and a collection that the whole frame's garbage triggered on the tick.

## What was changed

One commit each, each measured by itself, every tick's hash equal to the baseline's:

1. The contact tests and script opcodes make no garbage (cherry-picked from the perf/frame-spikes lane, 66c8877a).
2. The script's opcode handler no longer builds a closure for every opcode it runs.
3. The object pass rejects far objects by their box first, and keeps the "is this a sign" answer.
4. A respawn builds its car from the files the mission's load read (`CarFiles`).
5. The object pass asks a grid for the objects near each car (`ObjectIndex`), instead of walking the map's whole list.
6. `SimWarm` (opt-in): the sim's code compiled before the first tick.
7. The ground query asks the same grid for the structures under a point.
8. The cars' substeps and the weapons' aims make no garbage (the wheels from a span on the stack, two loops for the aims).

Tried and dropped: the path search's pool bookkeeping (a sampled profile put 87 % of a search in `List.RemoveAt` and array
range copies; batching them changed nothing measurable: the fifteen search ticks of T06 0.73 -> 0.74 ms median).

## SimWarm's trade

`RuntimeHelpers.PrepareMethod` over the sim, the readers, the server and the network code: 16,893 methods, 0.8-1.6 s on two
threads. It takes the worst tick from 85-105 ms to 17-24 ms, but it does not reach generic instantiations (the first tick still
compiles 135-180 methods), and the median tick came out 6-12 % slower in the T missions: the prepared code is final and skips
the runtime's profile-guided tier. The game would call it behind the loading card (its own `Boot/JitWarm` does the same and
nothing calls it); ReadyToRun for the sim's assembly in an export would be the better form if Godot's export honours it
(threading plan, 2.1).

## Two machines

The same build and install give different StateHashes on two machines: the development desktop (i7-7700K, .NET 8.0.27) and
the laptop (i7-1165G7, .NET 8.0.34) agree on 4 of the 15 scenario x rate runs and part from tick 264 to 3398 on the other 11
(the melee from tick 264 at 60 Hz, T05 from tick 1153 at 20 Hz), the baseline build as much as this branch's. On each machine
every build of this branch is equal to that machine's own baseline, tick for tick. So the speed work is not the cause; the
float results of some path differ between the two runtimes or processors. A network game whose host and client run
different machines predicts and reconciles against that; a replay or snapshot taken on one machine and checked on another
will part. Not looked into here: a lane of its own (which operation, and whether the runtime's version alone explains it).

## Not done

- **The rest of the garbage.** What a settled tick still allocates: event records (shots, impacts, sounds) the views take,
  LINQ in the AI's target choice and the weapons' per-car loops (`Weapons.Concat(Handguns.OfType<Weapon>())`), the cars'
  `Wheels` property (an array a call, per substep), the path search's nodes. Each is a local rewrite like the ones above.
- **The AI in the melee** (the largest section with eight cars: 0.22-0.28 ms a tick): its target and avoidance scans.
- Anything that would need new serialised state or a snapshot version: none was needed.
