# VirtualPilot — simulated landings and taxis against the real guidance code

`tools/VirtualPilot` flies landings and taxis through the **real** `TaxiGuidanceManager`
against the pilot's own navdata DB, steering **only by what a blind pilot gets**: the tone's
pan and the spoken callouts. It exists because unit tests only pin bugs we already know and the
DB sweeps check exit *tables*, not what happens second by second as an aircraft rolls through
handoff → miss check → retarget → taxi. Built 2026-09-18 after the YPPH 21 C9 false-miss flight.

Nothing touches the pilot's machine state:

- **Time is simulated.** Every clock read in the guidance code (`TaxiGuidanceManager*.cs`,
  `TaxiSteeringTone`) goes through `Utils/SimClock.UtcNow`; the harness installs
  `SimClock.Override`, so a landing takes ~10 ms, and every cooldown, persistence window and
  grace period still sees simulated seconds.
- **No audio.** `TaxiSteeringTone.Headless` skips the audio device; `ObservedPan` reports what the
  pilot would hear (signed pan, 0 when silent or paused).
- **No log writes.** `Log.Redirect` swallows every line (trace mode captures them for display).

All three hooks are `internal` and must stay **inert in the app** — never set any of them outside
the harness. The `Legacy…ForHarness` switches (`LegacyExitAnchorForHarness`,
`LegacyReversedEntryForHarness`) are the same kind of hook: each restores an earlier rule for A/B
measurement only.

> **On this branch.** The rounds below were measured on the owner's local build before its fixes
> were ported onto main (`feat/taxi-landing-port`); main's own designs replaced some of what they
> describe (the off-route runway warning now uses main's `RunwayIncursionWatch`; exit measurement is
> main's `ExitBranch`). Two things a fresh checkout needs: an OSM snapshot (`fetchosm`, or point
> `VP_OSMDIR` at an existing `runs/_osm`) — without one the named-holding-point (`HP`) departures are
> not flown at all and the graph has no OSM taxiway names, so the run is not comparable with one that
> had it; and the `frozen/` before-build project described under A/B is not in the repo — build the
> harness against an older checkout instead (`-p:MainApp=true` compiles the harness against plain
> main before the port).

## Running

```
dotnet build tools/VirtualPilot -c Release -p:Platform=x64
tools/VirtualPilot/bin/x64/Release/net10.0-windows/VirtualPilot.exe <mode> [options]
```

| mode | what it flies |
|---|---|
| `landings` | every listed exit of every runway: once **following the tone**, once **rolling straight past** (skipped for End exits / exits < 600 m from the runway end) |
| `taxi` | per airport, K plans: gate → runway (lineup) and landing-exit vacate point → gate; each flown on the **shortest path** and again on an **ATC-style clearance** (the shortest route's taxiway names in order) |
| `wrongturn` | follows a gate → runway route, then deliberately turns into a branch that leads to a runway hold line not on the route; the app must say "off route" first |
| `missturn` | follows a route to the junction before a hold on the way, where the taxiway it came in on carries straight on off the route, and rolls straight on (once at 10 kt, once at 20 kt): the turn onto the hold's connector missed. Only where the hold node lies 25 to 150 m to the side of that taxiway. The app must answer with the off-route recalculation, not with a hold-short stop on the taxiway. `runall.sh` flies it with 40 plans per airport |
| `rwystop` | a route loaded while stopped on a runway, midway across the widest gap between two exits, to a random gate, with and without a clearance. `VP_LEGACY_RWYSTART=1` and `VP_LEGACY_TURNSIDE=1` restore the older route start and opening turn for an A/B |
| `missedexit` | per runway, two flights where the pilot rolled past the planned exit and vacated at the next one, then loaded the gate route with the clearance given for the planned exit. The flight that prompted it (VTBS 02R, 2026-11-18) vacated one exit EARLY, which this mode does not fly; that one is replayed with `--trace ICAO:from:…`. Where the two exits are 300 m or more apart, the same clearance is also loaded while still on the runway, 150 m past the planned exit (`MISSEDEXIT_RWY`) |
| `all` | landings + taxi |
| `departures` | per runway end: named (OSM) holding-point departures (`HP`, flown free and on a clearance), intersection departures (`IX`), full-length backtracks where the runway list would say "(backtrack required)" (`BT`); per airport: gate → gate taxis across the field (`G2G`) and ATC clearances with explicit "hold short of runway X" instructions (`HSCLR`) — each also voiced as a written and a spoken-NATO clearance and fed to the SayIntentions import's own parser (`CLR_*`) |
| `turnpicks` | per taxi plan, the clearance a controller would give, then, for every row and both sides, the Turn pick a pilot could set on that row: each is loaded as the planner loads it and what became of it is recorded (already made, charted, off-chart leg, or refused and why); a pick that changes the route is flown following the tone, and flown once more with the pilot taking the charted way at the junction instead. `turn_report.py runs/NAME` summarises it |
| `fetchosm` | downloads the raw Overpass answer for each airport into `runs/_osm/ICAO.json` (one polite request at a time — Overpass rate-limits hard, expect ~1 airport a minute). Every mode then builds the app's OWN OSM-augmented graph from that snapshot (names, holding points); `VP_NOOSM=1` flies plain navdata, `VP_OSMDIR` points elsewhere. The snapshot pins the data so a before/after run measures the code, not OSM churn |

Options: `--airports N` (busiest by taxi-path count, default 100), `--icao A,B`, `--shard i/n`
(run n processes in parallel — the hooks are process-global, so parallelise by process, never by
thread), `--taxi-per-airport K`, `--out file`, and
`--trace ICAO:RWY:EXIT[:straight]` / `--trace ICAO:taxi:N[:clearance]` for one scenario frame by
frame (speech, state changes, routes, the harness's view of the runway frame).

`--trace ICAO:from:<lat>,<lon>,<heading>:<text in the gate's name>[:A,B,C]` loads a route where a real
flight loaded it, with the clearance it had, taken from the taxi and router logs.

**Which navdata.** `--sim fs2020|fs2024` (or `VP_SIM`) picks the simulator whose database is flown,
found where the app finds it (`DatabasePathResolver`); `--db file` (or `VP_DB`) names a database by
path. With neither, the MSFS 2020 database is flown when it is installed (the rounds below up to the
fifth flew it) and the MSFS 2024 one when it is not. The run prints the choice and writes it into the
findings header. **A before/after pair only compares when both runs fly the same database**: the
airports, the plans drawn and every finding differ between the two. The two simulators draw the same
airport differently, so measure a routing or guidance change on both.

Every run writes `<out>` (findings) and `<out>.results.txt` (one outcome line per landing).
`tools/VirtualPilot/compare.py BEFORE.results.txt AFTER.results.txt` diffs two runs landing by
landing — **run it before and after any change to rollout / exit logic; "no degrades" means the
WORSE column is explained case by case.** Run outputs go under `tools/VirtualPilot/runs/` (ignored).

Trace forms: `ICAO:RWY:EXIT[:straight]` (a landing), `ICAO:taxi:N[:clearance]` (the Nth taxi
plan), `ICAO:dep:<text>` / `ICAO:any:<text>` (every departures / every taxi-or-departures scenario
whose description contains the text — e.g. `KORD:dep:IX Y4 L 5`), `ICAO:turn:<text>` (the same for
`turnpicks`, e.g. `LTFM:turn:turn Right onto N1`), `ICAO:missturn:<text>` (the same for `missturn`,
e.g. `KDTW:missturn:before the hold n2753`, with `--taxi-per-airport 40` as the run flies it), and
`ICAO:pick:<lat>,<lon>,<heading>:<runway>:<clearance>[:miss]`: one clearance from a position to a
runway, a Turn pick written `=L` or `=R` after its taxiway, `:miss` to take the charted way at the
junction instead. The KBOS incident of 2026-11-14 is
`KBOS:pick:42.363953,-71.006981,170:33L:H,C=L`. ATC's "hold short of runway X" after a taxiway is
written `@X` (`OMDB:pick:25.251047,55.363293,120:30R:K,K7@30L`): it is loaded as that row's
hold-short pick, as the SayIntentions import seats it, and the clearance is voiced through the
import's own parser. `VP_SHOWCLR=1` prints the voiced
clearances, `VP_STUBSTATS=1` the destination entry-stub length and hold label of every departure.

### Whose settings a run flies with

Its own, never the `settings.json` of the PC it runs on. The taxi manager reads three settings
on every position frame (the tone's direction, hard pan and "ask before switching to a new
route"); until 2026-11-27 it read them from `SettingsManager.Current` inside the harness too,
which is the developer's own file. With "ask before switching" saved as on, each route change of
a simulated flight became an offer the simulated pilot never takes: on the 40 busiest airports
3 of 56 wrong-turn outcomes differed from the same code with it off, which read as a regression
in whichever build was being compared. The harness now sets
`TaxiGuidanceManager.FrameSettingsForHarness` to the settings it starts guidance with.

### A/B against a frozen build

Comparing two runs only means something when both fly the SAME scenarios, and a harness change
(a new check, a different random draw) changes the scenarios. So the "before" is the current
harness compiled against the OLD app: freeze the harness output folder before touching the app,
then build `tools/VirtualPilot/frozen` against it (its csproj header has the four steps;
`OLDAPP` compiles out harness calls to app APIs the old build lacks) and run
`VP_EXE=tools/VirtualPilot/frozen/out/VirtualPilot.exe tools/VirtualPilot/runall.sh BEFORE`.
Scenario keys can still drift when the app's own output feeds scenario selection (the
intersection list's order picks which intersections are flown) — `compare_codes.py` prints the
"only before / only after" counts; read those rows as new scenarios, not regressions.

The usual loop: `tools/VirtualPilot/runall.sh NAME` (landings + taxis + wrong turns, 100 airports,
6 processes, ~10 min — it BUILDS the harness first, because the harness is not in the solution and
a solution build leaves a stale binary), then
`py tools/VirtualPilot/compare_codes.py runs/BEFORE runs/AFTER [n]`, which lists per scenario
every finding code that disappeared or appeared. Trace one scenario with
`--trace ICAO:RWY:n<nodeId>` (exit names repeat — KMSP 04 has three B junctions) or
`--trace ICAO:taxi:N --taxi-per-airport 10` (N as numbered by the run); `VP_ALLLOG=1` adds the
app's log lines, and the landing trace prints the exit path in the runway frame.

## The pilot model

`PilotModel`: 0.3 s reaction lag; yaw rate from the pan, limited by speed (0.15 g lateral, max
15°/s) and yaw acceleration; holds heading when the tone is silent; 15 kt taxi, 9 kt on a hard
pan, 10 kt after "Slow…". In **lineup** (precision tone: 1° activation / 15° full pan) it reads the
pan as an error estimate and corrects proportionally — the turn-hard-on-any-pan taxi model
limit-cycles against a precision tone, which is a harness artefact, not an app bug. It stops at
hold shorts and presses Continue after 2 s, stops on "Lined up", and follows the rollout tone at a
decelerating speed (Normal/End exits 18 kt at the junction, High-speed 35 kt).

## What it checks, and how much to trust each

| finding | meaning | confidence |
|---|---|---|
| `LANDING_FALSE_MISS` | "Missed X" / retarget while the pilot followed the tone. The detail says how far the pilot was from the route and where the route leaves the centreline | high when on-route ≤ 5 m |
| `STRAIGHT_MISS_NOT_CALLED` / `_LATE` / `STRAIGHT_ROLL_ARRIVED` | rolling straight past is not called, called > 200 m late, or reported as vacated | high |
| `LANDING_NOT_ARRIVED`, `LANDING_RAN_OFF_END` | never reached an arrival | medium — check the trace |
| `EXIT_ROUTE_HAIRPIN` | the route off the runway turns > 120° within 150 m of the handoff: an exit listed for this direction that needs a U-turn (KPHL 35 E4) | high |
| `HANDOFF_ROUTE_STARTS_BACKWARDS` | the handoff route's first leg points > 120° off the heading | high |
| `UTURN_SPOKEN_ON_RUNWAY` / `UTURN_ON_FLAGGED_TURNBACK_EXIT` | "Make a U-turn" spoken on the runway — on an unlabelled exit (a bug) or one the list labels "(sharp turn back)" (warned) | high |
| `TOUCHDOWN_NAMES_OTHER_TAXIWAY` | at touchdown the first guidance line names a taxiway that is not the chosen exit ("Taxiway A. Steering guidance active." before "…exit taxiway C9") | high (every landing) |
| `OFF_ROUTE_WARNING_ON_ROUTE` | "Warning: approaching runway X, off route" with the aircraft ≤ 15 m from its route | high |
| `WRONG_TURN_SILENT` | a deliberate wrong turn reached a runway hold line with no warning | high |
| `MISSED_TURN_STOPPED_BEHIND_LINE` | `missturn`: stopped for the hold short more than 26 m from its node while more than 10 m farther from the hold's runway than the node is (the harness's own sum against `RunwayIncursionWatch.GuardedRunway`): a stop on the taxiway the pilot never left | high |
| `MISSED_TURN_STOPPED_AT_LINE` / `_HELD_IN_CIRCLE` / `_REROUTED` / `_NOTHING` | `missturn`: stopped beside the node but as near the runway as it is, or at a hold that guards no runway; stopped inside the 25 m circle; answered by the off-route recalculation ("Route changed", or "Off route." with why the route was kept); neither within 300 m | information |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` | the pilot stopped for a hold short while standing on runway pavement (`RunwayShape`) | high |
| `RUNWAY_ENTERED_UNHELD` / `DEST_…` | runway pavement entered > 250 m after the last hold (edge flicker and lineup onto the destination runway excluded) | medium |
| `CLEARANCE_ROUTE_DETOUR` | the constrained router over the shortest route's own taxiway names is 1.3× + 200 m longer — usually unnamed connectors a clearance cannot name vs the `FindRunwayBridge` 200 m cap (PGUA "via A": 450 m of unnamed connector) | medium, often by design |
| `TAXI_RECALC_WHILE_FOLLOWING` | "Route changed" while following the tone | medium |
| `TAXI_OFF_PAVEMENT`, `LANDING_OFF_PAVEMENT` | > 12 m beyond the nearest taxi edge's half-width | **low** — the graph has no apron polygons, so open aprons read as grass |
| `LINEUP_WRONG_PLACE` | "Lined up" > 120 m along the runway from where the pilot selected (the intersection's listed distance; the full-length lineup point) | medium — a precise lineup turn at 6 kt takes 30-60 m |
| `HOLD_NAMED_OTHER_RUNWAY` / `HOLD_FAR_BEFORE_RUNWAY` / `HOLD_WITHOUT_RUNWAY` | the stop named a runway other than the one then entered; it was > 220 m before it; or a runway stop was never followed by any runway | high |
| `DEST_HOLD_FAR_FROM_ENTRY` | the destination hold is > 600 m of taxiing before the runway entry | high |
| `HOLDPICK_NOT_SET` | an explicit "hold short of runway X" the app could not set (it says so) | medium — the harness derives the picks from the SHORTEST route, the clearance route can differ |
| `HP_HELD_AWAY_FROM_POINT` / `HP_HOLD_NOT_NAMED` / `HP_ROUTE_MISSES_POINT[_WARNED]` | a named-holding-point departure held > 30 m from the painted line picked / held there without naming it / the route never passes it (`_WARNED`: the app said so) | medium — hold placement stays navdata-authoritative by design, and a painted line BEYOND the navdata hold is correctly never reached |
| `CLR_{WRITTEN,SPOKEN}_{TAXIWAYS,HOLDS,RUNWAY}_WRONG` | the voiced clearance parsed to different taxiways / hold shorts / runway | high for spellable names; navdata names ATC would never say ("N-North") are noise |
| `LINEUP_LEAVES_MAPPED_PAVEMENT`, `LINEUP_NEVER_ALIGNED` | the lineup intercept leaves every mapped edge; never "Lined up" in 400 m | **low** — many sceneries stop the entry taxiway at the hold line (KIAH SA), so the ground between hold line and runway is unmapped |
| `TURN_PICK_SILENT`, `TURN_PICK_BROKE_THE_LOAD` | a Turn pick was neither applied nor said; a clearance that loads without the pick fails with it | high |
| `TURN_HONORED_WRONG_SIDE`, `TURN_MET_WRONG_SIDE`, `TURN_BOTH_SIDES_MET` | the route turns the other way onto the row's taxiway than the pick it honored or called already made (the harness's own sum of heading changes along that taxiway, a contradiction of more than 25°); both sides read as already made | high for the first two; the third is a straight-on join onto a taxiway that then bends both ways |
| `TURN_TRAVELS_THE_OTHER_ARM`, `TURN_CROSSES_THE_OTHER_ARM` | a route that honored a pick later runs along, or passes a node of, the arm of that taxiway the pick turned away from (worked out by the harness from the graph; the destination node does not count) | high — refused by the app since the sixth round, so either is a regression |
| `OFFCHART_NOTE_MISSING`, `TURN_CHARTED_NOT_CONFIRMED` | an off-chart leg, or a charted pick that changed the route, with no word of it at load | high |
| `TURN_PICK_LOST_WHILE_FOLLOWING` | "was missed" or "could not be kept" while the pilot followed the tone | medium — exit→gate plans start facing away from their route, and a first-row pick at a stand is judged from the harness's arbitrary start heading |
| `OFFCHART_LEG_NOT_TAXIED`, `OFFCHART_LEG_NOT_ANNOUNCED`, `OFFCHART_LEG_OFF_PAVEMENT` | arrived without coming within 25 m of the middle of the leg; a leg of 40 m or more that is not the route's first segment taxied with no "Off-chart leg" sentence; more than 12 m beyond the nearest taxi edge while on the leg | medium; the last is **low**, as `TAXI_OFF_PAVEMENT` (the app's own pavement map has the aprons this index lacks) |
| `TURN_MISS_SAID_MISSED` / `_SAID_NOT_KEPT` / `_NOT_SAID` / `_NO_REROUTE` / `_NOT_FLOWN` | the miss flight: the route change said so; changed with no word about the pick; never changed in 150 m (the pilot then follows the tone again); the pilot never came within 5 m of the junction | information; `_NOT_SAID` is the known limit (a pick counts as made once the segment index passes its join) |

## Findings of the first run (2026-09-18, 100 busiest fs2020 airports)

4,857 landing runs, 1,960 taxis, 409 wrong turns.

**Fixed:**

1. **Centreline-stub false misses** — many sceneries draw the exit taxiway down the runway
   centreline before it turns off (KORD 27L M 80 m, ZSPD 35R B7 99 m, EGLL 27L N7 40 m after a
   3 m jog). The route tone correctly said "continue"; the miss check at junction + 100 ft said
   "Missed". `LandingExitPathFollow.OnAxisRunMetres` now extends both miss detectors by that run
   (hairpins and paths that come back across the centreline excluded). **996 → 194** false misses,
   "never arrived" 227 → 48, off-pavement 671 → 466, ran off the end 226 → 50. Per-landing diff:
   ~1,550 better; 5 changed for the worse, all on broken exit geometry (KSDF 29 F, KCVG 18C K,
   PGUA 24L B: false "missed" → a different false "missed"; KLAX 25L A7 14 m off pavement).
   The trade, accepted: rolling straight past a stub exit is called "missed" later (median 59 m,
   p90 107 m, max 201 m), because until the drawn exit leaves the centreline "rolled past" and
   "following the exit" are the same position; 101 of those retargets pick a later exit (mostly
   because the old pick was 5–75 m ahead).
2. **False "approaching runway X, off route"** — the on-route set was the REMAINING route only,
   so the hold line just crossed dropped out the moment it was passed; hold lines beside the route
   (entry stubs off a parallel taxiway) tripped it too. Now: the whole route counts, a line behind
   the aircraft is not "approaching", and a line beside the route is silent while the aircraft is
   within 8 m of its route and heading along it (±30°). **1,121 → 239** of 1,960 taxis. Wrong-turn
   A/B (408 deliberate wrong turns): 0 warnings lost, 2 gained.

## Second round (2026-09-18, same 100 airports)

Every change below was A/B-measured with `compare_codes.py`; against the start of the round,
nothing new appears except what is listed as accepted.

1. **Touchdown callout named the wrong taxiway** on every landing ("Taxiway A. Steering guidance
   active." before "…exit taxiway C9") — `StartGuidance(announceStart: false)` at touchdown.
2. **U-turns on the runway at the handoff: 41 → 1** (KPHL 27R K4, a data mismatch). The
   landing-exit anchor retries a route that starts backwards, points back on its first leg or
   hairpins within 150 m (`LoadRoute`, see [RTE-32] in `docs/invariants/taxi-routing.md` for every gate and why). Exits whose own path
   turns back are labelled "(sharp turn back)" in the list, warned at touchdown and not picked by
   default (`PathTurnsBack`). Route hairpins 272 → 106.
3. **Destination hold on runway pavement** (EFHK 22R WD, KSLC 14): the stop moves back to the
   last node clear of every runway. 6 → 0.
4. **False misses 194 → 66**: the stub allowance no longer drops for an exit that loops back once
   well out on the apron (44), plus the retry above.
5. **False "off route" 239 → 115**: the on-route tracking test looks three segments back (a pilot
   stops short of a hold line and Continue moves the index past it).
6. Harness: "ran off the end" only counts inside the runway strip (End exits run on beside it).

**Accepted, recorded:**

- 260 U-turns on exits labelled "(sharp turn back)": the pilot was told at touchdown, and the
  alternatives (loops through other taxiways that hairpin 50 m later, or an 860 m detour at
  ZGGG 20L A) are worse.
- Offset stubs (OOMS 26R A, 7 m off the centreline for 87 m): widening the 6 m band trades each
  fix for a straight roll called "missed" > 200 m late. Stubs > 200 m (KLAX 25L A7) hit the cap.
- ZGGG 20L B: the retry lets the 300 ft early handoff go ahead and the pilot misses the curve at
  48 kt — disabling the retry there costs two other exits.
- ZSSS 18L (island start, uncharted crossing, B drawn over the runway) and KPHX 25R E12 (E11/E10
  drawn 260 m along 25R): the route itself runs on runway pavement.
- A geometric "hold line further from the runway than the aircraft" silence for off-route warnings
  fixed 19 and silenced two real wrong turns at RKTP 03R — removed, do not rebuild.

**Open (not fixed — each needs a decision):** the remaining 66 false misses and 115 off-route
warnings, 71 recalcs while following (mostly a harness artefact: exit→gate taxis start facing away
from their route), lineup from a set-back hold cutting across unmapped ground, clearance detours.

## Third round — taxi and departures (2026-09-18, same 100 airports)

New scenario families (`departures` mode) and the OSM snapshot. Old build vs new, frozen-build A/B,
13,774 scenarios on navdata (landings, taxis, wrong turns, departures) plus the named-holding-point
runs at the 20 airports with an OSM snapshot. Landings and wrong-turn warnings are unchanged
(25 silent wrong turns before and after, all pre-existing).

**Fixed:**

1. **Lineup from a set-back hold cut across the grass.** After Continue the intercept law steered
   for the centreline straight from the hold line (KORD 04R Y4: 95 m out, "Turn left" — 137° — at
   the line, "Lined up" 500 m past the intersection picked). The tone now follows the cut-off entry
   stub onto the pavement first (`TaxiRoute.RunwayEntryStub`, kept only when it reaches the
   destination runway). Grass lineups **2,104 → 238**, wrong-place lineups **801 → 212**,
   never-aligned 52 → 30.
2. **The destination hold was the latest hold anywhere on the route** — another runway's hold or this
   runway's far end: WIII 06 at "runway 07L at N8M" 612-868 m out, RPLL 31 2.5 km out, OTBH 16L 6 km
   out. Now only a hold within 200 m of the entry, or within 1,000 m naming this runway, counts; else
   the synthetic back-off, which must now be 10 m clear of every runway edge (VIDP 27 via D6 stopped
   on the edge). Phantom holds 207 → 107.
3. **Holds that named the wrong runway** 32 → 2: the entry stub can roll onto an intersecting runway
   before the destination (KSLC 32/35, KCOS, KSTL, KDTW) — now named at the stop ("Hold short of
   Runway 32 and runway 35") — and the countdown used the scenery's reciprocal designator
   ("Hold short runway 22L at Y4" then "Stop. Hold short of Runway 04R").
4. **Intersection distances were overstated on angled entries** (EGLL 09L AB11, KORD 04R Y4): now
   quoted from where the taxiway reaches the pavement on the aircraft's side, forward only.
5. **False "off route" warnings** 228 → 138: the on-route tolerance is 12 m, not 8 (a pilot rounding a
   junction 8-9 m wide — KORD G/G2, RPLL F5); wrong-turn A/B 391/391 still warned.
6. **Named holding points:** OSM labels that describe a hold kind ("28C APCH", "GP HOLD LINE", "ILS")
   are no longer offered as departure entries (KORD "28R APCH" stopped the pilot on runway 04R), and
   a holding point the route cannot be pinned through is now SPOKEN instead of log-only (EDDF 18 via P8).
7. Harness: the spoken-clearance check (the SayIntentions parser recovers taxiways, hold shorts and
   runway from written and NATO-spoken clearances at every airport — it does, apart from water runways
   "04W" and navdata names no controller says); a crossing through a runway intersection is one
   crossing, not two entries (CYYC C over 35L/11).

**Measured and rejected:** handing the lineup over to the centreline intercept 12 m BEFORE the
pavement edge (to shorten the lineup turn on narrow runways — YPDN 36 overshoots a 30 m runway by
2 m): 59 new findings for 24 fixed, it cuts the corner on angled entries.

**Open after this round:** see the fourth round below.

## Fourth round — the open list (2026-09-18 evening, same 100 airports)

A/B against a frozen build of the start of the round (`tools/VirtualPilot/frozen`, which now copies
the frozen dependencies itself and only compiles `OLDAPP` out with `-p:OldApp=true`).

**Harness corrections first** — each changed what a finding means, not the app:

- Taxi-planner routes are now loaded with `announceSummary: true`, as the form does. That flag also
  runs the route-start checks, so KVAD Parking 45 (a stand joined to the network only across runway
  19R) is correctly REFUSED by the app ("reaching it means crossing runway 19R where there is no
  taxiway data") — it was an artefact, not an unheld runway entry. Refusals are recorded as
  `TAXI_ROUTE_REFUSED` (31: honest island messages).
- A wrong turn the app answers by RE-ROUTING through the line is scored on the new route:
  `WRONG_TURN_REROUTED_NO_RUNWAY` (no runway beyond the line) / `_HELD` / `_UNHELD` (the only bad one).
  Scenarios whose "junction" lies beyond the lineup are skipped (KMCI).
- Rolling from one runway's pavement straight onto another's is one crossing (CYYC C over 35L/11).

**Fixed (125 findings, 0 new on navdata; on the full OSM set — 98 airports, 3,457 departures incl.
692 named-holding-point runs — 49 fixed / 12 changed, every changed one a variant of the same
scenario's old finding or a safer route: OIII 11L via B8 no longer drives onto 11L and back out to
hold; KPDX 03 via 4E no longer takes a 4.7 km detour to pass the painted point, and says so):**

1. **Clearances that repeat a taxiway** — both readings built, the one that follows the clearance
   taken unless it loops (see [RTE-33] in `docs/invariants/taxi-routing.md`). OTBH 16L "A, C, A" 9.1 → 2.9 km, ZPPP 04L "A, B, A"
   6.2 → 3.9 km, EDDF/LEMD clearances the old router abandoned for shortest path now followed.
2. **"Crossing runway X" for a hold line the route never goes past onto a runway** → "Passing the
   runway X hold line" (KDTW F, an approach-area line).
3. A holding-point route's summary read "Route to  via …" (the pinned route lost its name).
4. Water runways: "runway 04W" parsed as 04.

**Measured and rejected** (details in [HLD-15], `docs/invariants/runway-holds.md`): a dead-ahead exception to the off-route tracking
silence (204, then 148 false warnings), a cost on taxiing along runways, pinning intersection
departures through the picked taxiway, stopping at a picked painted line behind the navdata hold.

**Still open — each is the scenery or the pilot model, not a code path left unmeasured:**

- 10 silent wrong turns: a missed turn and a wide turn are the same geometry when the hold line is
  20-30 m past the junction (YPDN A2/A3 ×3, OETF, KEWR, LEMD, KATL, KPHL, KFBG, KMCI).
- Stops on runway pavement (8) and unheld entries (9) where the ROUTE drives along a runway (KLAX
  H8/H9 down 07R, KABQ along 08/26, USSS M, OOMS A): the shortest path uses runway pavement as a
  taxiway and there is no clear node to stop at.
- YBBN 19L "A6": that intersection is reached along A4S; A6's own approach lies beyond the junction.
- Painted holding points behind the navdata hold line: the stop stays navdata-authoritative.

## Painted-line hold audit (2026-09-23)

Set `VP_AMDBDIR` to an unpacked AMDB Bridge airport cache (one folder per ICAO with
`taxiwayholdingposition.geojson`; the free X-Plane-Gateway/OSM database from
github.com/Vihaan2012-cmyk/Free-Airport-Mapping-DB) and every taxi/departure also checks each hold
stop against the painted RUNWAY hold lines (catstop 1):

- `HOLD_PAST_PAINT_NAVDATA_HOLD_UNUSED` — the stop is on the runway side of every painted line for
  that runway, and our navdata HAS a hold node within 40 m of the line: our placement logic.
- `HOLD_PAST_PAINT_NO_NAVDATA_HOLD` — same, but our navdata has no hold there: the two sceneries
  disagree.
- `HOLD_WELL_SHORT_OF_PAINT` — stopped more than 120 m before the first painted line.

X-Plane positions are ~20 m off MSFS, so every comparison carries a 30 m allowance, and only lines
off the entered runway's pavement, on the stop's side, within 250 m and nearer it than any other
runway count. Unset, the audit is off and the harness is unchanged. Result and the fix it drove:
docs/taxi-guidance.md, "Runway holds on a diagonal approach".

## Fifth round — what the pilot hears (2026-09-25, same 100 airports)

New harness checks: `SPOKEN_DIRECTION_CONTRADICTS` (opposite turn directions in one frame, or
"Curving X" over a hard opposite tone) and `VP_BURST=1` (prints every frame that speaks more than
one line — each `AnnounceImmediate` cuts the previous one off, so only the last is heard). The
taxi trace also prints the route's start node and every hold line within 150 m (`HSNEAR`, with
distance from the nearest centreline).

**Fixed (0 new findings; wrong-turn warnings 385/385, same distances):**

1. **One utterance per frame** (`SpeakNow` / `ComposeFrameUtterance`). ~12,000 frames spoke 2-4
   interrupting lines; the pilot heard only the last ("In 30 metres, slight right." → "Slight right
   now." → "Slow for turn.": the direction was never heard). Safety lines go first; an advance notice
   in the same frame as its "… now" is dropped.
2. **"Curving X" deferred while the tone asks for a turn the other way ≥ 25°** — 89 → 2
   contradictions (EGLL 27R A7/A10E/AB11: "Turn left to come around." + "Curving right.").
3. **Junction callouts wait until the pilot has come round after the initial turn cue**
   (`_initialTurnPending`) — the route summary's "Turn left to come around" was cut off by "Left."
   / "Left now." for a junction behind the aircraft.
4. **Off-route runway warning**: the LOADED route's start node counts as on-route (a stand 31 m
   short of LTFM A11A; recalcs excluded — whitelisting them lost 19 wrong-turn warnings), the pilot
   standing at it is ignored, and "approaching" needs ≥ 2 kt of closing speed on the line. False
   warnings on route 147 → 77.
5. Wording: callouts capitalised, no "continue now.", no "sharp slight right … 160 degrees".
6. Hold-short countdown speaks only the deepest tier already crossed ("in 55 metres." then
   "in 55 metres. Slow down." 0.1 s apart at LTFM B9B).
7. Post-handoff, well off to the side of a leg running beside the runway with the next turn toward
   that side, the tone steers at the leg's end (KTPA 28 N: silent tone under "Sharp right now").

Follow-up the same evening (A/B against the round's end, 0 new findings, wrong turns 385/385):

8. **Vacate stops past the landed runway's own hold line on a curving exit**
   (`RunwayVacateResolver.ExtendPastOwnHoldAhead`). EGLL 09L A1 stopped 22 m short of "A1, Runway
   27R" at 141 m because the greedy walk needs every step further out and A1 curves (120 → 123 →
   120 m). Gates, each measured: a line NAMED for the landed runway, ≤ 180 m out (1,446 → 1,332
   moves before this cap; 01G was moving to 252 m), ≤ 80 m of taxiing, never more than 10 m back
   toward the runway, a ≤ 60 m hop past the line must exist (KCVG 09 C's only onward leg is 298 m
   and the planner then started the route 250 m away), and not on an exit whose path turns back
   (`LandingExitDestination.Resolve`; RJAA 34L A8, CYYC 11 A got worse). Whole DB
   (`tools/LandingExitDestinationSweep`): 497 exits at 229 airports move, all outward (+5 to +120 m,
   median 46 m), none closer, no vacate verdict changes — Heathrow's N5E/N4W/S4E/S6/N6/A6 stopped at
   ~90 m against painted lines at ~145-150 m.
9. **Handoff route anchor follows the exit's own path** (`ExitPathStartAnchor`): KCLT 18 "D6"
   leaves via R, and the nearest D6 node was on D6's other arm beside the runway — the route opened
   with a leg that panned the tone away from the exit and the pilot heard "Missed taxiway D6". Only
   where the path beyond the junction carries no node of the exit's name, and never on a turn-back
   exit (overriding every off-path anchor fixed 59 findings and made 43).
10. **The post-handoff miss retarget uses the undershoot scan's 11 ft/kt lead** — "Retargeting taxiway
    R, 25 metres ahead" at 25 kt. A junction of the missed exit's own taxiway is exempt (KSLC 35 K8
    is rescued by it), a close exit is still taken when nothing further qualifies, and
    `RetargetLandingExit` tries it last when every further exit fails to route (KEDW 23L past C).
    314 straight-roll retargets now name an exit a median 100 m ahead instead of 35 m.

## Sixth round — route starts off the pavement (2026-11-08, same 100 airports, plain navdata)

A/B with both legacy switches on and off, 6,893 scenarios. `TAXI_OFF_PAVEMENT` went away on 610 scenarios and appeared on 22. In `rwystop` alone, 843 of 1,124 flights were flagged before and 265 after, `TAXI_NOT_ARRIVED` went from 12 to 1 and stops on runway pavement from 9 to 4. Silent wrong turns stayed at 14.

The first attempt took the nearest exit whatever the route did next, which added 10 stops short of the runway just left (KFLL 10L Q); an exit whose route comes back onto the runway is now skipped. The walk target also aimed the roll beside the runway where the first segment leaves at an angle, so the tone now aims at the start node until the aircraft arrives.

Accepted: 13 new `TAXI_RECALC_WHILE_FOLLOWING` against 32 gone. `RUNWAY_ENTERED_UNHELD` in `rwystop` rose from 17 flights to 25, but 22 of the 25 name the runway the flight started on, which the check reads as an entry after drifting to the edge, not a real entry.

## Seventh round — a clearance that starts behind the aircraft (2026-11-19, 100 busiest airports of each navdata, plain navdata)

Two flights on 2026-11-18 left the pavement following the tone: VTBS 02R (cleared E7, vacated one exit early at E10 after the rollout switched to it at 20 kt) and VHHH 07L (cleared C8, stopped on the runway 600 m before it). Both replay exactly with `--trace ICAO:from:…` on the 2024 navdata. `missedexit` flies the other way round, past the planned exit and off at the next; the early case rests on the VTBS replay alone. The sixth round's runway start already covers VHHH; VTBS needed RTE-38 and RTE-39 ([route-runway-start.md](invariants/route-runway-start.md)).

A/B against a frozen build, modes `missedexit taxi rwystop departures wrongturn`, on both navdata sets:

| | 2024 | 2020 |
|---|---|---|
| scenarios | 7,974 | 8,013 |
| `TAXI_OFF_PAVEMENT` gone / appeared | 196 / 4 | 165 / 4 |
| `RUNWAY_ENTERED_UNHELD` gone / appeared | 38 / 1 | 36 / 1 |
| `HOLD_FAR_BEFORE_RUNWAY` gone | 37 | 35 |
| `missedexit` flights more than 40 m off the pavement, before / after | 135 / 30 | 144 / 46 |
| scenarios changed outside `missedexit` | 24 of 6,868 | 18 of 6,893 |

Leaving out every cleared taxiway the route does not drive was measured first and rejected: at VTBS it dropped the cleared D6 and took D7.

A review of that change found the same out-and-back on a route loaded while still on the runway past the cleared exit, where the runway start (RTE-37) had kept RTE-39 from running. `MISSEDEXIT_RWY` flies it: 603 flights on 2024 and 640 on 2020. With the check run from the runway start too, `TAXI_OFF_PAVEMENT` went away on 8 and 7 of them, and no code appeared in any of the 8,577 and 8,653 scenarios.

Accepted: 21 (2024) and 20 (2020) new `TAXI_RECALC_WHILE_FOLLOWING` against 10 and 8 gone, and the four new `TAXI_OFF_PAVEMENT` on each. In the cases traced (KIAD 19R U3, LTFM 35R C14, KATL 27R L, ZBAD 17L C4, VTBS 20R F3) the lead-in is now kept and starts behind the aircraft, so the flight opens with "Taxiway X is behind you" and a turn the pilot model swings wide on, where it used to be sent 180 to 380 m in a straight line. At ZBAD the route change during that turn has no stop left for the runway just vacated, and says so. Three 2020 `rwystop` flights gained `RUNWAY_ENTERED_UNHELD_WARNED`; the one traced, VTBS 20R, now loads a route that leaves by the exit behind it and announces that the runway it stands on has no hold short point. The other new codes were not traced one by one.

## Eighth round — review fixes to the route start (2026-11-19, 100 busiest airports of each navdata, plain navdata)

A/B of the review fixes against a frozen build of the seventh round's end, the same harness, modes `missedexit taxi rwystop departures wrongturn`: every scenario matched, 8,579 on fs2024 and 8,516 on fs2020. Kept, with no finding new on either database:

| | 2024 | 2020 |
|---|---|---|
| `TAXI_OFF_PAVEMENT` gone | 4 | 6 |
| `HOLD_WITHOUT_RUNWAY` gone | 0 | 2 |
| `RUNWAY_ENTERED_UNHELD` gone | 0 | 1 |
| `TAXI_RECALC_WHILE_FOLLOWING` gone | 0 | 1 |

The traced gains come from the near-cleared test ([RTE-37](invariants/route-runway-start.md#rte-37)): a first cleared taxiway within 75 m cancels the runway start only when it is on the runway or at its edge. KIAH 15L (WM, 43 m off the pavement before, 0 after) and KBNA 31 (K, 99 m before, 2 after) now start at an exit along the runway instead of a node across the grass.

With the OSM snapshot loaded (fs2024, modes `missedexit taxi rwystop departures`, the 37 busiest airports fetched, 3,425 scenarios), measured because RTE-38 and RTE-39 find the junction by the names on its nodes and online names add more: the review fixes against the seventh round's end, `TAXI_OFF_PAVEMENT` gone on 1, nothing new. The route-start work as a whole against the build before it (that harness compiled against the older app), `TAXI_OFF_PAVEMENT` gone on 396 and new on 13. None of the three new ones traced reads a junction wrongly from an online name: KORD 04L E is a runway start, KMSP 04 M6 flies the same route with a different opening turn, and LTFM 35R B12B leaves out a taxiway at a real out-and-back.

Measured and rejected along the way, each with its own A/B:

- Reading the turn-back over 30 m of route either side of the junction ([RTE-39](invariants/route-runway-start.md#rte-39)): 4 scenarios better and 3 worse. KDEN 07 B4 reaches the cleared taxiway by a 3.3 m spur and back, which the smoothed reading missed, and EDDB 24R K1 gained three findings. Every reversal the per-node test flagged in those flights was a real out-and-back over one edge, 3.3 to 56 m long.
- The opening turn's stand evidence from the stand rows instead of the graph's Parking nodes ([STR-24](invariants/taxi-steering.md#str-24)): more opening turns flipped, at gate departures (KMCI gates 25 and 26 on fs2020) and after an exit vacate (KPIT 28C exit C, both databases), and those flights left the pavement or were re-routed where before they had not.
- Preferring a start that follows the clearance over every start ahead: KLSV 21L took an exit behind and turned round on the runway, 99 m off the pavement. The preference now applies only among the exits one way.
- Splitting the exits by bank with the six-place cap unchanged: the two halves of a crossing took two places and KBNA 02L (fs2020) lost the only exit whose route stayed off the runway. A twin on the other bank no longer takes a place.

**Open:** OSM hold names like "C@10/28" (KTPA, KDFW, KONT, KABQ) are read out verbatim. EGLL 09R S6
→ Gate 223 routes back up S6 onto the runway and along it (the known "runway used as a taxiway"
residual).

## Sixth round — Turn picks, on MSFS 2024 navdata (2026-11-15, 100 busiest fs2024 airports)

The first round flown on `fs2024.sqlite` (`--sim fs2024`; the harness could only open the MSFS 2020
database before). Plain navdata (`VP_NOOSM=1`), 12 processes, under two minutes a run.

**No pick, before and after the Turn pick branch** (`all wrongturn departures`, the harness of the
branch's database change against base `6684fab3a`, then the final harness against the final app):
11,963 scenarios (3,705 landings, 5,835 taxis), the same in both; 18,225 result lines and 20,970
finding lines, not one different. A clearance with every Turn on Automatic routes and is guided
exactly as before.

**Every pick** (`turnpicks`): 10,008 picks on 931 clearances, 2,805 flights. No pick was dropped in
silence, none broke a load, none is honored or called already made with the route turning the other
way, and no honored route touches the arm of the taxiway it turned away from. What became of them:
already made 3,033; no such turn 3,907; does not lead there 2,029; long detour 31; honored on a
charted link 482; honored with an off-chart leg 526 (median 59 m, 90 % under 115 m, 391 of them 40 m
or longer). The sweep tries BOTH sides of every row, so most picks point away from the destination by
construction; the refusal rate says nothing about a pilot's own picks. In the 1,004 flights of
honored picks, a pick adds no runway entered without a hold, no stop on runway pavement and no
flight that fails to arrive.

**Found and fixed, each flown first:**

1. **The KBOS incident route was refused.** `KBOS:pick:…:33L:H,C=L` on the whole airport: "turning
   left onto taxiway C does not lead to Runway 33L". Taxiway C has 400 m with no name 900 m east of
   the junction, and a blocked node switched gap chaining off for its whole taxiway. The unit-test
   replica of the junction had no gap. Now only a hop over the block is refused ([TURN-5]); the route
   flies: the note, the hold short of 04R at H, "Off-chart leg. No taxi line for the next 95 metres…",
   the left turn, 33L. 30 of 396 "does not lead" refusals were this.
2. **A turn onto the next taxiway counted as this row's.** "Already made" walked 80 m along the
   route: a straight-on join followed by a left onto the next row read as "left, already made", and
   the pick vanished without a word. It now walks the row's own taxiway only.
3. **Picks "honored" by loops**, about 1,500 of the 2,515 the first cut honored ([TURN-4],
   [TURN-6]): a 153° reversal 55 m after the turn (LTFM "right onto N1"); a loop back through a node
   the route had passed (WMKK "left onto G"); a trip back up the stand's own lead-in (ZJHK "right
   onto C", where guidance then skipped the loop altogether and steered onto the charted way with the
   pick still reading as made); 116 m out along a taxiway and straight back over the same nodes (ZBAD
   "right onto L2", and 486 others); a turn and straight back past the junction on other nodes.
4. **The long way round onto the same taxiway's other arm**, which the first design allowed. Of the
   1,013 picks still honored after 3, 379 travelled along the other arm and 169 more crossed it. One
   never arrived: KCLE "left onto G7" at a runway exit went back across the runway just vacated and
   round to G7's other arm, and a recalculation on that lap began it again. By the owner's ruling the
   rest of a picked route no longer touches that arm ([TURN-6]); the pick gets "does not lead to".
5. **A pick honored along a runway.** OMDB "left onto N": onto runway 12L/30R at N8 with "Passing
   the hold line", 370 m along it, unheld and unwarned. A pick no longer adds a stretch along a
   runway ([TURN-7]); the same pick now gets a 70 m off-chart leg and flies clean.

**Measured, left as it is:**

- **What an honored pick costs.** A charted pick adds a median 204 m to the route (a quarter add
  over 610 m, 68 of 482 over 1 km); an off-chart pick a median 32 m, and 298 of 526 leave the route
  within 50 m of its charted length. The bound is twice the charted route plus 500 m.
- **A missed turn is not always called missed.** Of 512 flights where the pilot took the charted way
  at the junction, 209 heard "was missed", 142 heard "Route changed" with no word about the pick (it
  counts as made once the segment index passes its join, about 25 m before a crossing node), and in
  161 the route never changed within 150 m and the pilot followed the tone again. KBOS, taking the
  connector as on the day: the tone hard left throughout, and after 60 m "Route changed… The left
  turn onto taxiway C was missed."
- **Three rows where both sides read as already made** (RPLL G2, MMMX L): a straight-on join onto a
  taxiway that bends slightly one way and, 140 m on, the other. Nothing changes and nothing is said.
- **Three picks lost while following the tone** ("was missed" or "could not be kept"): each a
  first-row pick or an exit-to-gate plan that the harness starts facing away from its route.
- In the miss flights, three runway entries with no hold: the pilot, deliberately off the route, drove
  on at a runway the charted way leads to (KATL M6: "Warning: approaching runway 09L at M6, off
  route." was spoken, and the recalculated route said "with no hold short point").

## Ninth round — a hold passed beside its node, and the missed turn (2026-11-27, 100 busiest fs2024 airports, plain navdata)

PR 369 (the hold taken beside its node, [HLD-24](invariants/hold-abeam.md#hld-24), and a route that ends far from its runway spoken as the end of the route) and its review fixes, against main: three frozen builds (main, the PR as authored, the final tree) with the same harness source. `VP_NOOSM=1`, as no OSM snapshot is on this machine; fs2024 only, as no fs2020 database is installed.

**The standing modes** (`all wrongturn departures rwystop missedexit turnpicks`), 26,571 scenarios, every one matched, main against the final tree:

| code | gone | appeared |
|---|---|---|
| `TAXI_NOT_ARRIVED` | 5 | 0 |
| `HOLD_STOP_ON_RUNWAY_PAVEMENT` | 1 | 0 |
| `LINEUP_NEVER_ALIGNED` | 11 | 0 |
| `LINEUP_LEAVES_MAPPED_PAVEMENT` | 11 | 0 |
| `HOLD_WITHOUT_RUNWAY` | 9 | 1 |
| `HOLD_NAMED_OTHER_RUNWAY` | 2 | 0 |

- The five flights that now arrive are the hold taken beside its node: KCLE, KSLC, KSFO and VHHH twice, all `missedexit` or `rwystop` routes that double back on a hold or start beside one. At VHHH the route runs out along a J5 spur and back to the hold node, a 180 degree turn; the pilot swings round 42 m from the node, main never latched the hold and the flight circled for 7 km. The stop gone from runway pavement is KSFO, where the aircraft comes at the 19R hold on C from the side and is now stopped level with it, 9 seconds before main stopped it on the pavement. The one code that appeared (KSLC 34R) is a stop at the route's start that main makes as well; the check only reports it once a flight completes.
- The 11 lineups are five routes that stop short of their runway more than 400 m to the side of it (KDEN South 14 to 17L, KNYL to 35, DABO to 34, KCOS to 13, RKSI to 34R, each on a clearance or with a Turn pick). Main and the PR as authored held short of the runway there (at KDEN, more than 1,500 m from it) and ran 400 m of lineup tone across unmapped ground. They now count down to the end of the route and stop there: "Stop. End of this route. It does not reach Runway 17L. Reprogram the taxi route, including the taxiway that connects to the runway." With an end-state readout patched into the harness for this round (not committed), 7,617 taxi, departure, runway-stop and missed-exit flights: no flight that lined up before ends this way.
- Wrong turns: 399 warned and 11 silent before and after.

**Missed turns** (`missturn`, new this round). The review's question was whether the new capture stops a pilot who misses the turn onto the hold's connector and rolls on along the taxiway. It did: 1,502 missed turns, 40 plans an airport, 751 at 10 kt and 751 at 20 kt.

| | main | the PR as authored | final |
|---|---|---|---|
| `MISSED_TURN_STOPPED_BEHIND_LINE` | 0 | 61 | 0 |
| `MISSED_TURN_STOPPED_AT_LINE` | 0 | 7 | 7 |
| `MISSED_TURN_HELD_IN_CIRCLE` | 11 | 11 | 11 |
| `MISSED_TURN_REROUTED` | 1,352 | 1,288 | 1,345 |
| `MISSED_TURN_NOTHING` | 139 | 135 | 139 |

The fix is [HLD-25](invariants/hold-abeam.md#hld-25): not taken while more than 10 m farther from the hold's runway than the hold node AND with the tone at full pan. It changes none of the 26,571 standing scenarios, and the 11 stops the capture adds to those 7,617 flights are all kept at the same places.

**Measured and rejected:**

- Holding the capture back while the off-route clock is running, so that the recalculation answers first: it took away only 24 of the 70 stops beside a hold. In the other 46 the clock was not running when the aircraft came level with the node; why was not traced.
- Either half of [HLD-25] alone. Behind-the-line alone loses the VHHH and KSFO stops (21 to 35 m behind, the tone leading the pilot round), which are three of the five flights that now arrive. The tone alone loses KCLE, where the tone is at full pan with the aircraft 45 m past the line.
- A plain distance-from-the-runway test with a wider margin: the VHHH stops (22 m behind) and the nearest missed turn (26 m behind, KORD R) are 4 m apart.

**Open:** at KDEN the clearance made of the shortest route's own taxiway names (SC, L, EA, M, EC, P, ED) is routed 1.9 km along ED past the 17L hold line and never reaches the runway, where the shortest route does in 6.6 km. That is the constrained router, on main as well; the route now at least ends as the end of the route. Not flown: a missed turn above 20 kt, and either database with an OSM snapshot.
